using System.Numerics;
using MiniRing.Motion;

namespace MiniRing.Bots;

/// <summary>
/// 剣士の守り方: 相手の剣先を見て、当たりそうなら受ける（刀で）か外す（足で下がる・横へ）。<see cref="Mode"/> が <see cref="DefenseMode.Legacy"/> 以外なら、
/// 相手の武器を握りから先までの線として読み、受け流す・足と頭で外す（<see cref="WeaponDefense"/>。武器の種類によらない）。
/// </summary>
/// <param name="Enabled">守るか。</param>
/// <param name="BlockShare">受けるか外すかのうち、受ける割合（0..1。<see cref="DefenseMode.Legacy"/> だけ）。</param>
/// <param name="Horizon">剣先がこの先どこへ来るかを読む時間 [s]（剣先の今の速度のまま進むとして）。</param>
/// <param name="Margin">剣先が体の線（頭から腰）へこれより近づくと読めたら守る [腕の長さ]。</param>
/// <param name="ThreatSpeed">これより遅い剣先は見ない [腕の長さ / s]（構えた剣の揺れ）。</param>
/// <param name="HoldTime">受け・外しを続ける時間 [s]（読めなくなってから）。</param>
/// <param name="Counter">受けた後すぐに、近い間合いの技で打ち返すか（返し技）。</param>
/// <param name="WatchFists">相手の拳も見て守るか（拳で戦う相手・武器を落とした相手に）。</param>
/// <param name="Mode">守りの組み立て方。</param>
/// <param name="Deflect">受け流しで、刀が来る線を押す向きを、来る向きの真逆から体の外側へ傾ける角 [rad]（0 = 正面で止める）。</param>
/// <param name="Forte">受け流しで来る線と交わる、自分の刀の所（0 = 手元・1 = 先）。手元ほど手首にかかる梃子が短い。</param>
/// <param name="Lead">受け流しで、読んだ所よりこれだけ手前（来る向きの逆）で迎える [腕の長さ]。</param>
/// <param name="StepDelay">足で外すとき、足が動き出すまでの遅れの読み [s]。</param>
/// <param name="HeadTime">頭を沈める・引くのにかかる時間の読み [s]。</param>
/// <param name="Drive">受け流しで、迎える所より押す向きへこれだけ先を手の目標にする [腕の長さ]（当たる時にも腕が押している）。</param>
/// <param name="Bind">受け流しで、来る線のどこで迎えるか（0 = 相手の握り・1 = 先）。体へ来る点より握りの側なら、その分遅い所で迎える。</param>
/// <param name="Flat">受け流しで、刃でなく鎬（刀の平らな面）を来る線へ向けるか。</param>
/// <param name="Clearance">受け流しの刀（左手から先まで）を、自分の体の線（頭から腰）からこれより離しておく [腕の長さ]。</param>
public sealed record SwordDefense(bool Enabled = true, float BlockShare = 0.6f, float Horizon = 0.25f, float Margin = 0.45f, float ThreatSpeed = 6f,
    float HoldTime = 0.3f, bool Counter = true, bool WatchFists = true, DefenseMode Mode = DefenseMode.Legacy, float Deflect = 0.5f, float Forte = 0.35f,
    float Lead = 0.1f, float StepDelay = 0.05f, float HeadTime = 0.15f, float Clearance = 0.6f, float Drive = 0f, float Bind = 1f, bool Flat = false)
{
    /// <summary>守らない。</summary>
    public static SwordDefense None { get; } = new(Enabled: false);

    /// <summary>
    /// 守りのドリル（guardlab）と練習試合で選んだ守り: 武器を握りから先までの線として読み、足と頭で、振られている武器の届く範囲から出る
    /// （<see cref="WeaponDefense"/>。docs/WEAPON_DEFENSE.md）。
    /// </summary>
    public static SwordDefense Adopted { get; } = new(Mode: DefenseMode.Step);

    /// <summary>
    /// 受け流しながら足と頭で外す守り（ドリルで「受け流しだけ」より効いた値: 来る線の握りの側 3 割で迎え（遅い所）、外へ 30° 傾けて押し、
    /// 迎える所より 0.3 腕の長さ先を手の目標にして払う。docs/WEAPON_DEFENSE.md）。
    /// </summary>
    public static SwordDefense Parrying { get; } = new(Mode: DefenseMode.ParryAndStep, Deflect: 0.52f, Lead: 0.2f, Drive: 0.3f, Bind: 0.3f);
}

/// <summary>
/// 間合いの取り合い（打って離れる: ヒットアンドアウェイ）。相手の届く距離を読み、その少し外（遠間）に立って待つ。相手に隙が見えたら
/// （振り終えた直後・武器を落とした）、または待ちきれなくなったら、一気に技の間合いまで詰めて打ち、打ち終えたらすぐ遠間まで下がる。
/// 相手が自分の技の間合いへ入ってきたら、その出端を打つ。下がる・待つ間はロープを背負わないよう、空いている横へ回る。
/// </summary>
/// <param name="Margin">相手の届く距離（読み）より外に立つ余裕 [m]。</param>
/// <param name="Lunge">相手の届く距離の読みに足す、相手の踏み込みの分 [腕の長さ]。</param>
/// <param name="OpeningTime">相手の振りが終わってから、隙と見て打ちに出る時間の窓 [s]。</param>
/// <param name="Patience">隙が来ないとき、自分から攻めて出るまでに待つ時間 [s]。</param>
/// <param name="Circle">遠間で待つ間に横へ回る強さ（歩く速さに対する割合）。</param>
/// <param name="Debana">相手が自分の技の間合いへ踏み込んできたら、その出端を打つか。</param>
public sealed record HitAndAway(float Margin = 0.06f, float Lunge = 0.35f, float OpeningTime = 0.4f, float Patience = 1.2f, float Circle = 0.35f, bool Debana = true)
{
    /// <summary>形の検査をして自分を返す。</summary>
    public HitAndAway Validated() =>
        Margin is >= -0.2f and <= 0.5f && Lunge is >= 0f and <= 2f && OpeningTime is >= 0f and <= 2f && Patience is > 0f and <= 10f && Circle is >= 0f and <= 1f
            ? this
            : throw new ArgumentOutOfRangeException(nameof(Margin), "間合いの取り合いの値が範囲の外です。");
}

/// <summary>
/// 両手で剣を握って戦うボット。技は動きの区間（<see cref="MotionTechnique"/>）で、構え（コンストラクタに渡すフレーム）から出す:
/// <list type="bullet">
/// <item>構えのまま、遠間（いちばん遠い技の間合いの少し外、<see cref="StandOff"/>）で相手と向き合う。</item>
/// <item>次の技を 1 つ決めて、その技の間合いまで歩いて詰め、間合いに入ったら技の動きをなぞる（踏み込みも動きのうち）。</item>
/// <item>相手が狙いより内側へ詰めてきたら、その近さで出せる技で迎え撃つ。</item>
/// <item>技を出し終えたら <see cref="Rest"/> 秒、構えに戻って遠間へ下がる。</item>
/// <item>守り（<see cref="SwordDefense"/>）: 技を出していない間、相手の剣先がこの先体の線へ近づくと読めたら、刀を剣先の通り道へ横たえて受けるか、
/// 剣先の来る向きの逆へ足で下がって外す。受けた後は、近い間合いの技で打ち返す。</item>
/// </list>
/// 技の選び方・守り方の選び方だけが乱数で、種が同じなら同じ試合になる。
/// </summary>
public sealed class SwordsmanBot : IFighterControl
{
    private readonly IReadOnlyList<MotionTechnique> _techniques;
    private readonly MotionFrame _guard;
    private readonly Random _random;
    private readonly float _rate;
    private readonly float _walkSpeed;
    private readonly float _maxTwist;
    private readonly SwordDefense _defense;
    private readonly float _bladeLength;
    private readonly float _spacing;
    private readonly float _opponentReach;
    private MotionTechnique? _plan;
    private MotionTechnique? _current;
    private float _time;
    private float _start;
    private float _restUntil;
    private float _plannedAt;
    private float _defendUntil;
    private MotionFrame? _block;
    private Vector3 _evade;
    private Vector3 _head;
    private IReadOnlyList<WeaponLine> _lines = [];
    private Vector3 _threatHilt;
    private Vector3 _threatDirection;
    private bool _evadedThisBlow;
    private bool _counter;
    private readonly HitAndAway? _hitAndAway;
    private bool _attacking;
    private bool _retreating;
    private float _waitStart;
    private float _openUntil;
    private bool _opponentSwinging;
    private float _circleSide = 1f;
    private float _ropeSide;
    private readonly AimTuning _aimTuning;
    private float _aimForward;
    private readonly Dictionary<BodyPartKind, float> _forwardNow = [];
    private float _retreatStart;
    private bool _circleTurned;
    private BodyPartKind? _openTarget;

    /// <summary>狙った技の間合いに着けないまま、この秒数が過ぎたら狙いを決め直す。</summary>
    public const float PlanPatience = 1.5f;

    /// <param name="techniques">持っている技。</param>
    /// <param name="guard">構え（技を出していない間のフレーム）。</param>
    /// <param name="seed">技の選び方の乱数の種。</param>
    /// <param name="rate">動きの再生の速さ（<see cref="MotionImitation.Rate"/>）。</param>
    /// <param name="rest">技を出し終えてから次を狙うまでの間 [s]。</param>
    /// <param name="exact">技を、その間合い ± <see cref="MotionTechnique.Window"/> に入ったときだけ出す（近すぎれば下がる。ドリル用）。</param>
    /// <param name="tuning">歩く速さ・胴のひねりの上限。</param>
    /// <param name="defense">守り方（null なら守らない）。</param>
    /// <param name="bladeLength">剣の握り（右手）から先までの長さ [腕の長さ]（受けるとき、剣のどこで受けるか）。</param>
    /// <param name="spacing">両手の間隔 [腕の長さ]（受けの構えの左手の位置）。</param>
    /// <param name="opponentReach">相手の肩から剣先までが届く長さ [腕の長さ]（これより先を通る剣先は届かないので守らない）。</param>
    /// <param name="hitAndAway">間合いの取り合い（打って離れる）。null なら、遠間で構えて技を 1 つずつ狙って詰める（ドリルと同じ）。</param>
    /// <param name="aim">狙いの付け方（<see cref="Aim"/> を決めたときの、通り道のずらし方）。null なら既定。</param>
    public SwordsmanBot(IReadOnlyList<MotionTechnique> techniques, MotionFrame guard, int seed, float rate, float rest = 0.6f, bool exact = false, FighterTuning? tuning = null,
        SwordDefense? defense = null, float bladeLength = 1.4f, float spacing = SwordForms.KatanaSpacing, float opponentReach = 3f, HitAndAway? hitAndAway = null, AimTuning? aim = null)
    {
        ArgumentNullException.ThrowIfNull(techniques);
        if (techniques.Count == 0)
        {
            throw new ArgumentException("技が 1 つも無い。", nameof(techniques));
        }

        _techniques = techniques;
        _guard = guard with { Velocity = default };
        _random = new Random(seed);
        _rate = rate;
        Rest = rest;
        Exact = exact;
        var t = tuning ?? FighterTuning.Default;
        _walkSpeed = t.WalkSpeed;
        _maxTwist = t.MaxTorsoTwistRadians;
        _defense = defense ?? SwordDefense.None;
        _bladeLength = bladeLength;
        _spacing = spacing;
        _opponentReach = opponentReach;
        _hitAndAway = hitAndAway?.Validated();
        _aimTuning = (aim ?? new AimTuning()).Validated();
        _circleSide = _hitAndAway is null || _random.Next(2) == 0 ? 1f : -1f;
        StandOff = techniques.Max(x => x.Range) + 0.08f;
    }

    /// <summary>構えて向き合う距離 [m]（いちばん遠い技の間合いの少し外: 遠間）。</summary>
    public float StandOff { get; }

    /// <summary>技を出し終えてから次を狙うまでの間 [s]。</summary>
    public float Rest { get; }

    /// <summary>技を間合いぴったりでだけ出すか。</summary>
    public bool Exact { get; }

    /// <summary>
    /// 自分からは打たず、その場で構えて守るだけ（守りのドリルの受け手）。打ち返しもしない。
    /// </summary>
    public bool Passive { get; init; }

    /// <summary>今出している技（出していなければ null）。</summary>
    public MotionTechnique? Current => _current;

    /// <summary>今守っているか（受けている・外している）。</summary>
    public bool Defending => _time < _defendUntil;

    /// <summary>今受けているか（守っていて、受けを選んだ）。</summary>
    public bool Blocking => Defending && _block is not null;

    /// <summary>これまでに出した技の名前（順に）。</summary>
    public List<string> Used { get; } = [];

    /// <summary>技を出した時の間合い（胸と胸の水平の距離、順に）。</summary>
    public List<float> UsedAt { get; } = [];

    /// <summary>受けた回数。</summary>
    public int Blocks { get; private set; }

    /// <summary>足で外した回数。</summary>
    public int Evasions { get; private set; }

    /// <summary>
    /// 狙う相手の部位（<see cref="MotionAim.Targets"/> のどれか。null なら技そのままの通り道）。入力元の意図（コントローラの方向など）から
    /// 毎フレーム変えてよい。狙う部位があれば、技はその部位を打てる技（ドリルで測った <see cref="MotionTechnique.Skill"/>）から選び、
    /// 相手の今の姿勢でのその部位の位置へ、間合いと両手の通り道を合わせて出す（<see cref="MotionAim"/>）。
    /// </summary>
    public BodyPartKind? Aim { get; set; }

    /// <summary>
    /// 狙う部位を自分で選ぶか（ボット）。技を決めるたびに、相手の構えでいちばん空いている部位（<see cref="OpenTarget"/>）を <see cref="Aim"/> にする。
    /// </summary>
    public bool AutoAim { get; init; }

    /// <summary>
    /// 相手の構えでいちばん空いている部位: 部位ごとに、相手の拳と剣（握りから先までの線）からの近さ（守られている度合い）と、その部位を打てる
    /// 自分の技の割合（<see cref="MotionTechnique.Skill"/>）を掛けた点がいちばん高い所。打てる技が 1 つも無ければ null。
    /// </summary>
    private BodyPartKind? OpenTarget(in FighterSense sense)
    {
        BodyPartKind? best = null;
        var bestScore = 0f;
        foreach (var part in MotionAim.Targets)
        {
            var skill = _techniques.Max(t => t.Skill?.GetValueOrDefault(part) ?? 0f);
            if (skill <= 0f)
            {
                continue;
            }

            var at = MotionAim.TargetPoint(sense, part);
            var cover = float.MaxValue;
            foreach (var fist in new[] { sense.OpponentLeft.Position, sense.OpponentRight.Position })
            {
                // 部位そのものが拳・前腕なら、その拳では守れない。
                if (fist != default && !(part == BodyPartKind.Forearm && Vector3.Distance(fist, at) < 0.3f * sense.ArmReach))
                {
                    cover = MathF.Min(cover, Vector3.Distance(fist, at));
                }
            }

            if (sense.OpponentBlade.Present)
            {
                cover = MathF.Min(cover, Vector3.Distance(at, ClosestOnSegment(at, sense.OpponentBlade.Hilt, sense.OpponentBlade.Tip)));
            }

            // 守りから腕の長さほど離れていれば、空いている（それより遠くても点は増えない）。
            var open = MathF.Min(1f, cover / sense.ArmReach);
            var score = open * skill;
            if (score > bestScore)
            {
                bestScore = score;
                best = part;
            }
        }

        return best;
    }

    /// <summary>技 <paramref name="technique"/> を出す間合い: 技の間合いに、狙う所が相手の胸より手前にある分を足す。</summary>
    private float RangeOf(MotionTechnique technique) => technique.Range + _aimForward;

    private bool Fits(MotionTechnique technique, float distance) => MathF.Abs(distance - RangeOf(technique)) <= MotionTechnique.Window;

    /// <summary>
    /// 次に狙う技: 狙う部位が無ければ全ての技から乱数で。あれば、その部位を打てた割合（<see cref="MotionTechnique.Skill"/>）がいちばん高い技の
    /// 8 割以上の技から乱数で（測っていなければ、ドリルでいちばん斬った部位がそこの技、それも無ければ全ての技から）。
    /// </summary>
    private MotionTechnique ChoosePlan()
    {
        if (AutoAim)
        {
            // 狙いを決め直したら、その部位の前後の差で間合いも決め直す（このフレームのうちに）。空いた所が無ければ狙わない。
            Aim = _openTarget;
            _aimForward = Aim is { } chosen ? _forwardNow.GetValueOrDefault(chosen) : 0f;
        }

        if (Aim is not { } aimAt)
        {
            return _techniques[_random.Next(_techniques.Count)];
        }

        var best = _techniques.Max(t => t.Skill?.GetValueOrDefault(aimAt) ?? 0f);
        IReadOnlyList<MotionTechnique> pool = best > 0f
            ? [.. _techniques.Where(t => (t.Skill?.GetValueOrDefault(aimAt) ?? 0f) >= 0.8f * best)]
            : _techniques.Where(t => t.Target == aimAt).ToList() is { Count: > 0 } named ? named : _techniques;
        return pool[_random.Next(pool.Count)];
    }

    /// <summary>相手の隙（振り終えた直後）を見て打ちに出た回数（<see cref="HitAndAway"/>）。</summary>
    public int Openings { get; private set; }

    /// <summary>相手が間合いへ踏み込んできた出端を打った回数。</summary>
    public int Debanas { get; private set; }

    /// <summary>隙が来ないまま待ちきれず、自分から攻めて出た回数。</summary>
    public int Pressures { get; private set; }

    /// <summary>今、打った後に遠間へ下がっているか。</summary>
    public bool Retreating => _retreating;

    /// <summary>今、技の間合いへ詰めているか（打ちに出た）。</summary>
    public bool Attacking => _attacking;

    /// <summary>
    /// 相手の武器（無ければ拳）がこちらの胸に届く、胸と胸の距離の読み [m]: 武器の握りから先までの長さ + 腕の長さの 1.15 倍（肩の回り込み）
    /// + 踏み込み（<see cref="HitAndAway.Lunge"/>）。体の形は同じ人形なので、腕の長さは自分のもので読む。
    /// </summary>
    public static float OpponentReach(in FighterSense sense, float lunge)
    {
        var blade = sense.OpponentBlade;
        var weapon = blade.Present ? Vector3.Distance(blade.Hilt, blade.Tip) : 0f;
        return weapon + (sense.ArmReach * (WeaponDefense.ShoulderReach + lunge));
    }

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        _time += dt;
        var toOpponent = sense.OpponentChest - sense.Self.Origin;
        toOpponent.Y = 0f;
        var distance = toOpponent.Length();
        var face = distance > 1e-4f ? toOpponent / distance : sense.Self.Forward;
        _lines = WeaponDefense.Lines(sense, _defense.WatchFists);
        WatchOpponent(sense);
        _openTarget = AutoAim ? OpenTarget(sense) : null;
        _forwardNow.Clear();
        if (AutoAim || Aim is not null)
        {
            foreach (var part in MotionAim.Targets)
            {
                _forwardNow[part] = _aimTuning.ForwardGain * MotionAim.ForwardOffset(sense, MotionAim.TargetPoint(sense, part), face);
            }
        }

        _aimForward = Aim is { } aimAt ? _forwardNow[aimAt] : 0f;
        if (!sense.OpponentAlive)
        {
            _current = null;
            return MotionImitation.ToCommand(_guard, sense, face, 0f, _walkSpeed, _maxTwist);
        }

        if (_current is { } technique)
        {
            var t = (_time - _start) * _rate;
            if (t < technique.Motion.Duration)
            {
                return MotionImitation.ToCommand(technique.Motion.Sample(t), sense, face, _rate, _walkSpeed, _maxTwist);
            }

            _current = null;
            _plan = null;
            _restUntil = _time + Rest;
            _attacking = false;
            _retreating = _hitAndAway is not null; // 打ったらすぐ遠間へ下がる
            _retreatStart = _time;
        }

        // 守り: 技を出していない間だけ（打っている最中は打ち切る）。
        if (_defense.Enabled && _defense.Mode != DefenseMode.Legacy)
        {
            if (Guard(sense, face) is { } guarded)
            {
                return guarded;
            }
        }
        else if (_defense.Enabled && !Defending && Threat(sense) is { } threat)
        {
            StartDefense(sense, face, threat);
        }

        if (Defending)
        {
            return _block is { } block
                ? MotionImitation.ToCommand(block, sense, face, 0f, _walkSpeed, _maxTwist)
                : MotionImitation.ToCommand(_guard with { Head = new Vector3(0f, 0f, -0.15f) }, sense, face, 0f, _walkSpeed, _maxTwist, _evade);
        }

        // 打たない受け手: 間合いの取り合いが無ければその場で構え、あれば遠間を保って回るだけ。
        _counter &= !Passive;
        if (Passive && _hitAndAway is null)
        {
            return MotionImitation.ToCommand(_guard, sense, face, 0f, _walkSpeed, _maxTwist);
        }

        if (_counter)
        {
            // 受けた直後: 休まず、近い間合いの技で打ち返す。
            _counter = false;
            _restUntil = _time;
            _plan = _techniques.MinBy(x => MathF.Abs(RangeOf(x) - distance));
            _plannedAt = _time;
            _attacking = _hitAndAway is not null;
            _retreating = false;
        }

        if (_hitAndAway is { } h)
        {
            return HitAndAwayStep(sense, h, distance, face);
        }

        var resting = _time < _restUntil;
        var closest = _techniques.Min(RangeOf);
        var pressed = !Exact && (resting ? distance <= closest + MotionTechnique.Window : _plan is { } p && distance < RangeOf(p) - MotionTechnique.Window);
        if (!resting || pressed)
        {
            if (_plan is null || _time - _plannedAt > PlanPatience)
            {
                _plan = ChoosePlan();
                _plannedAt = _time;
            }

            var fits = _techniques.Where(x => Fits(x, distance)).ToList();
            if (fits.Count > 0 && (pressed || fits.Contains(_plan)))
            {
                return Strike(fits.Contains(_plan) ? _plan : fits[_random.Next(fits.Count)], sense, face, distance);
            }
        }

        var want = resting ? StandOff : RangeOf(_plan!);
        var move = distance > want + 0.02f ? face : distance < want - 0.02f ? -face : Vector3.Zero;
        return MotionImitation.ToCommand(_guard, sense, face, 0f, _walkSpeed, _maxTwist, move);
    }

    /// <summary>技 <paramref name="technique"/> を今の間合い <paramref name="distance"/> から出す。</summary>
    private FighterCommand Strike(MotionTechnique technique, in FighterSense sense, Vector3 face, float distance)
    {
        // 狙う部位があれば、今のその位置へ両手の通り道をずらす（姿勢でなく目標の通り道を変える）。
        if (Aim is { } aimAt)
        {
            var target = sense.Self.WorldToLocal(MotionAim.TargetPoint(sense, aimAt)) / sense.ArmReach;
            technique = MotionAim.Aimed(technique, target, sense.ArmReach, _aimTuning) with { Name = technique.Name };
        }

        _current = technique;
        _start = _time;
        Used.Add(technique.Name);
        UsedAt.Add(distance);
        return MotionImitation.ToCommand(technique.Motion.Frames[0], sense, face, _rate, _walkSpeed, _maxTwist);
    }

    /// <summary>
    /// 相手の振りを見る: 剣先（武器が無ければ拳）が、相手の胴の動きを差し引いて守りの読みの速さ（<see cref="SwordDefense.ThreatSpeed"/>）より
    /// 速ければ振っている。振りが終わったら、そこから <see cref="HitAndAway.OpeningTime"/> 秒を隙と見る（振り抜いた剣は戻るまで受けに使えない）。
    /// </summary>
    private void WatchOpponent(in FighterSense sense)
    {
        SeeReach(sense);
        if (_hitAndAway is not { } h)
        {
            return;
        }

        var blade = sense.OpponentBlade;
        var speed = blade.Present
            ? (blade.TipVelocity - sense.OpponentVelocity).Length()
            : MathF.Max((sense.OpponentLeft.Velocity - sense.OpponentVelocity).Length(), (sense.OpponentRight.Velocity - sense.OpponentVelocity).Length());
        var swinging = speed > _defense.ThreatSpeed * sense.ArmReach;
        if (_opponentSwinging && !swinging)
        {
            _openUntil = _time + h.OpeningTime;
        }

        _opponentSwinging = swinging;
    }

    /// <summary>
    /// 見て覚えた相手の届く距離 [m]: 相手が振っている間（<see cref="WeaponLine.SwingSpeed"/> が守りの読みの速さを超える間）に、相手の武器の線（と拳）の
    /// 握り・先が、相手の胸からこちらの向きへ水平に出た距離のいちばん大きいもの。読み（<see cref="OpponentReach"/>）は武器の長さと腕の長さからの
    /// 見積もりなので、見積もりより遠くまで届く武器・振り方（これから足す武器も）は、1 度見たらその距離を覚えて遠間に入れる。
    /// </summary>
    public float SeenReach { get; private set; }

    private void SeeReach(in FighterSense sense)
    {
        var toMe = sense.Self.Origin - sense.OpponentChest;
        toMe.Y = 0f;
        // 立っている相手の振りだけを覚える（倒れる時・武器を落とす時の暴れた動きで遠間を広げない）。
        if (toMe.LengthSquared() < 1e-8f || !sense.OpponentAlive)
        {
            return;
        }

        toMe = Vector3.Normalize(toMe);
        foreach (var line in _lines)
        {
            if (line.SwingSpeed >= _defense.ThreatSpeed * sense.ArmReach)
            {
                SeenReach = MathF.Max(SeenReach, MathF.Max(Vector3.Dot(line.Hilt - sense.OpponentChest, toMe), Vector3.Dot(line.Tip - sense.OpponentChest, toMe)));
            }
        }
    }

    /// <summary>遠間（打たれない距離）[m]: 相手の届く距離の読みと、見て覚えた距離（<see cref="SeenReach"/> + 踏み込み）の大きい方 + 余裕。</summary>
    private float SafeDistance(in FighterSense sense, HitAndAway h) =>
        MathF.Max(OpponentReach(sense, h.Lunge), SeenReach + (h.Lunge * sense.ArmReach)) + h.Margin;

    /// <summary>
    /// 打って離れる 1 歩（<see cref="HitAndAway"/>）: 遠間で待つ → 隙・待ちきれない・出端で打ちに出る → 技の間合いで打つ → 遠間へ下がる。
    /// </summary>
    private FighterCommand HitAndAwayStep(in FighterSense sense, HitAndAway h, float distance, Vector3 face)
    {
        var reach = sense.ArmReach;
        var safe = SafeDistance(sense, h);
        // 遠間まで下がれたら待つ。追われて下がりきれないまま（ロープ際など）長く続いたら、下がるのをやめて待ち直す。
        if (_retreating && (distance >= safe - 0.02f || _time - _retreatStart > PlanPatience))
        {
            _retreating = false;
            _waitStart = _time;
        }

        if (!_attacking)
        {
            // 出端: 相手がこちらへ歩いて詰めてきて、今の間合いで出せる技がある（下がっている最中に追ってきた相手にも）。
            var advancing = Vector3.Dot(sense.OpponentVelocity, -face) > 0.25f * _walkSpeed;
            var ready = _techniques.Where(x => Fits(x, distance)).ToList();
            if (h.Debana && advancing && ready.Count > 0 && !Passive)
            {
                Debanas++;
                _attacking = true;
                _retreating = false;
                return Strike(ready[_random.Next(ready.Count)], sense, face, distance);
            }
        }

        if (!_attacking && !_retreating && !Passive)
        {
            var open = _time < _openUntil;
            if (open || _time - _waitStart > h.Patience)
            {
                Openings += open ? 1 : 0;
                Pressures += open ? 0 : 1;
                _attacking = true;
                _plan = ChoosePlan();
                _plannedAt = _time;
            }
        }

        if (_attacking && _plan is { } plan)
        {
            var fits = _techniques.Where(x => Fits(x, distance)).ToList();
            if (fits.Count > 0 && (fits.Contains(plan) || distance < RangeOf(plan) - MotionTechnique.Window))
            {
                return Strike(fits.Contains(plan) ? plan : fits[_random.Next(fits.Count)], sense, face, distance);
            }

            if (_time - _plannedAt <= PlanPatience)
            {
                var step = distance > RangeOf(plan) + 0.02f ? face : distance < RangeOf(plan) - 0.02f ? -face : Vector3.Zero;
                return MotionImitation.ToCommand(_guard, sense, face, 0f, _walkSpeed, _maxTwist, Footing(sense, step, face, reach));
            }

            // 詰めきれなかった（相手も下がった）: 遠間へ戻って待ち直す。
            _attacking = false;
            _retreating = true;
            _retreatStart = _time;
        }

        // 待つ（遠間で横へ回る）・下がる。
        var lateral = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, face));
        var keep = _retreating || distance < safe - 0.03f ? -face : distance > safe + 0.03f ? face * 0.6f : Vector3.Zero;
        // 回る先にロープが近づいたら逆へ回る。決めたら、そのロープから離れるまで変えない（隅で毎フレーム入れ替わると動けない）。
        var ahead = BoxerBot.RopeBehind(sense.Self.Origin, -lateral * _circleSide, face, reach, sense.Arena).Behind;
        if (ahead > 0.5f && !_circleTurned)
        {
            _circleSide = -_circleSide;
            _circleTurned = true;
        }
        else if (ahead < 0.2f)
        {
            _circleTurned = false;
        }

        var circle = _retreating ? Vector3.Zero : lateral * (h.Circle * _circleSide);
        return MotionImitation.ToCommand(_guard, sense, face, 0f, _walkSpeed, _maxTwist, Footing(sense, keep + circle, face, reach));
    }

    /// <summary>
    /// ロープを背負わない足の運び（拳のボットと同じ: <see cref="BoxerBot.RopeBehind"/>）: 背中側のロープが近いほど、下がる動きを空いている横への
    /// 動きに替え（逃げる側はロープを感じた時に決めて、離れるまで変えない）、ロープ際では中へ戻る向きを足す。
    /// </summary>
    private Vector3 Footing(in FighterSense sense, Vector3 move, Vector3 face, float reach)
    {
        var ring = sense.Arena;
        var lateral = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, face));
        var (behind, roomy) = BoxerBot.RopeBehind(sense.Self.Origin, face, lateral, reach, ring);
        if (behind <= 0f)
        {
            _ropeSide = 0f;
        }
        else if (_ropeSide == 0f)
        {
            _ropeSide = roomy;
        }

        var approach = Vector3.Dot(move, face);
        if (approach < 0f && behind > 0f)
        {
            var converted = -approach * behind;
            move += (face * converted) + (lateral * (_ropeSide * converted));
        }

        return move + BoxerBot.StayInside(sense.Self.Origin, reach, ring);
    }

    /// <summary>
    /// 武器を選ばない守り（<see cref="WeaponDefense"/>）の 1 歩: 相手の武器の線（と拳）を読み、当たりそうなら <see cref="SwordDefense.Mode"/> に従って
    /// 受け流す・足と頭で外す。読めている間は毎歩、構えと足を読み直す（来る線を追う）。読めなくなっても <see cref="SwordDefense.HoldTime"/> 秒は続ける。
    /// 守っていなければ null。
    /// </summary>
    private FighterCommand? Guard(in FighterSense sense, Vector3 face)
    {
        var lines = _lines;
        if (WeaponDefense.Read(sense, lines, _defense) is { } threat)
        {
            var parry = _defense.Mode is DefenseMode.Parry or DefenseMode.ParryAndStep;
            var step = _defense.Mode is DefenseMode.Step or DefenseMode.ParryAndStep;
            var fresh = !Defending;
            // 受け流しの構えは、1 つの打ちに 1 度だけ決めて続ける（毎歩決め直すと刀の向きが揺れ、回る途中で自分を斬る）。
            // 別の打ち（別の武器・拳、来る向きが 60° 以上変わった）が来たら決め直す。
            var direction = threat.Velocity.LengthSquared() > 1e-10f ? Vector3.Normalize(threat.Velocity) : _threatDirection;
            var newBlow = fresh || Vector3.Distance(threat.Line.Hilt, _threatHilt) > 0.5f * sense.ArmReach || Vector3.Dot(direction, _threatDirection) < 0.5f;
            if (newBlow)
            {
                _block = parry ? WeaponDefense.Parry(sense, threat, _defense, _bladeLength, _spacing, _guard.Blade) : null;
                _counter = parry && _defense.Counter && !Passive;
                Blocks += parry ? 1 : 0;
                _threatDirection = direction;
                _evadedThisBlow = false;
            }

            _threatHilt = threat.Line.Hilt;
            (_evade, _head) = step ? WeaponDefense.BestStep(sense, lines, _defense, _walkSpeed, face) : (Vector3.Zero, Vector3.Zero);
            if (!_evadedThisBlow && (_evade != Vector3.Zero || _head != Vector3.Zero))
            {
                Evasions++;
                _evadedThisBlow = true;
            }

            _defendUntil = _time + _defense.HoldTime;
        }

        return Defending
            ? MotionImitation.ToCommand((_block ?? _guard) with { Head = _guard.Head + _head }, sense, face, 0f, _walkSpeed, _maxTwist, _evade)
            : null;
    }

    /// <summary>
    /// 相手の剣先が、この先 <see cref="SwordDefense.Horizon"/> 秒のうちに自分の体の線（頭から腰）へ <see cref="SwordDefense.Margin"/> より
    /// 近づくと読めたら、初めてそこまで近づく所（ワールド。受けるのはそこ: 体へ届く前）と剣先の速度を返す。剣先は今の速度のまま進むとして読む。
    /// </summary>
    private (Vector3 Point, Vector3 Velocity)? Threat(in FighterSense sense)
    {
        var reach = sense.ArmReach;
        var shoulder = sense.OpponentBody.Pelvis == default ? sense.OpponentChest : (sense.OpponentBody.LeftShoulder + sense.OpponentBody.RightShoulder) * 0.5f;
        var blade = sense.OpponentBlade;
        if (blade.Present && Incoming(sense, blade.Tip, blade.TipVelocity, shoulder, _opponentReach * reach) is { } cut)
        {
            return cut;
        }

        // 相手の拳も同じに読む（拳の届く長さは腕の長さ。相手の胴の動きは差し引いて、拳を出した動きだけを見る）。
        if (_defense.WatchFists)
        {
            foreach (var fist in new[] { sense.OpponentLeft, sense.OpponentRight })
            {
                if (fist.Position != default && Incoming(sense, fist.Position, fist.Velocity - sense.OpponentVelocity, shoulder, WeaponDefense.ShoulderReach * reach) is { } punch)
                {
                    return punch;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="tip"/>（剣先や拳）が速度 <paramref name="velocity"/> のまま進むとして、相手の肩から <paramref name="limit"/> の内で、
    /// 自分の体の線（頭から腰）へ <see cref="SwordDefense.Margin"/> より近づく初めての所。
    /// </summary>
    private (Vector3 Point, Vector3 Velocity)? Incoming(in FighterSense sense, Vector3 tip, Vector3 velocity, Vector3 shoulder, float limit)
    {
        var reach = sense.ArmReach;
        if (velocity.Length() < _defense.ThreatSpeed * reach)
        {
            return null;
        }

        var top = sense.SelfHead == default ? sense.Self.Origin + new Vector3(0f, 0.4f * reach, 0f) : sense.SelfHead;
        var bottom = sense.Self.Origin - new Vector3(0f, 1.2f * reach, 0f);
        // 近づいてくる（体の線へ向かう）ときだけ。
        if (Vector3.Dot(velocity, ClosestOnSegment(tip, top, bottom) - tip) <= 0f)
        {
            return null;
        }

        for (var k = 0; k <= 12; k++)
        {
            var p = tip + (velocity * (_defense.Horizon * k / 12f));
            if (Vector3.Distance(p, shoulder) > limit)
            {
                break;
            }

            if (Vector3.Distance(p, ClosestOnSegment(p, top, bottom)) < _defense.Margin * reach)
            {
                return (p, velocity);
            }
        }

        return null;
    }

    private void StartDefense(in FighterSense sense, Vector3 face, (Vector3 Point, Vector3 Velocity) threat)
    {
        var reach = sense.ArmReach;
        _defendUntil = _time + _defense.HoldTime;
        if (_random.NextDouble() < _defense.BlockShare)
        {
            // 受け: 剣先の通り道（読んだ所を、少し自分の側へ寄せた所）へ、剣を剣先の動きに直角に横たえる。刃（鎬）を剣先の来る向きへ。
            var local = sense.Self.WorldToLocal(threat.Point) / reach;
            var v = sense.Self.InverseRotate(threat.Velocity);
            v = v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : -Vector3.UnitZ;
            var axis = Vector3.Cross(v, Vector3.UnitZ);
            if (axis.LengthSquared() < 1e-4f)
            {
                axis = Vector3.Cross(v, Vector3.UnitY); // 真っすぐ来る突き: 剣を横に寝かせて上から押さえる
            }

            axis = Vector3.Normalize(axis);
            axis = axis.Y < -0.2f || (MathF.Abs(axis.Y) <= 0.2f && axis.X < 0f) ? -axis : axis; // 剣先を上か左へ
            var meet = Vector3.Lerp(new Vector3(0f, -0.2f, 0.3f), local, 0.8f) + new Vector3(0f, 0f, 0.15f);
            var right = meet - (axis * (0.5f * _bladeLength));
            _block = SwordForms.Block(right, axis, -v, _spacing);
            Blocks++;
            _counter = _defense.Counter;
        }
        else
        {
            // 外し: 下がりながら、剣先が横へ進む向きの逆（剣先が通り過ぎた側）へ寄る。進む向きへ寄ると刃の通り道に残る。
            _block = null;
            var lateral = threat.Velocity - (Vector3.Dot(threat.Velocity, face) * face);
            lateral.Y = 0f;
            var side = lateral.LengthSquared() > 1e-6f ? -Vector3.Normalize(lateral) * 0.5f : Vector3.Zero;
            _evade = -face + side;
            Evasions++;
        }
    }

    private static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() > 1e-12f ? Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
        return a + (ab * t);
    }
}
