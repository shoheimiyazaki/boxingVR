using System.Numerics;
using MiniRing.Motion;

namespace MiniRing.Bots;

/// <summary>
/// 剣士の守り方: 相手の剣先を見て、当たりそうなら受ける（刀で）か外す（足で下がる・横へ）。
/// </summary>
/// <param name="Enabled">守るか。</param>
/// <param name="BlockShare">受けるか外すかのうち、受ける割合（0..1）。</param>
/// <param name="Horizon">剣先がこの先どこへ来るかを読む時間 [s]（剣先の今の速度のまま進むとして）。</param>
/// <param name="Margin">剣先が体の線（頭から腰）へこれより近づくと読めたら守る [腕の長さ]。</param>
/// <param name="ThreatSpeed">これより遅い剣先は見ない [腕の長さ / s]（構えた剣の揺れ）。</param>
/// <param name="HoldTime">受け・外しを続ける時間 [s]。</param>
/// <param name="Counter">受けた後すぐに、近い間合いの技で打ち返すか（返し技）。</param>
/// <param name="WatchFists">相手の拳も見て守るか（拳で戦う相手・武器を落とした相手に）。</param>
public sealed record SwordDefense(bool Enabled = true, float BlockShare = 0.6f, float Horizon = 0.25f, float Margin = 0.45f, float ThreatSpeed = 6f,
    float HoldTime = 0.3f, bool Counter = true, bool WatchFists = true)
{
    /// <summary>守らない。</summary>
    public static SwordDefense None { get; } = new(Enabled: false);
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
    private bool _counter;

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
    public SwordsmanBot(IReadOnlyList<MotionTechnique> techniques, MotionFrame guard, int seed, float rate, float rest = 0.6f, bool exact = false, FighterTuning? tuning = null,
        SwordDefense? defense = null, float bladeLength = 1.4f, float spacing = SwordForms.KatanaSpacing, float opponentReach = 3f)
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
        StandOff = techniques.Max(x => x.Range) + 0.08f;
    }

    /// <summary>構えて向き合う距離 [m]（いちばん遠い技の間合いの少し外: 遠間）。</summary>
    public float StandOff { get; }

    /// <summary>技を出し終えてから次を狙うまでの間 [s]。</summary>
    public float Rest { get; }

    /// <summary>技を間合いぴったりでだけ出すか。</summary>
    public bool Exact { get; }

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

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        _time += dt;
        var toOpponent = sense.OpponentChest - sense.Self.Origin;
        toOpponent.Y = 0f;
        var distance = toOpponent.Length();
        var face = distance > 1e-4f ? toOpponent / distance : sense.Self.Forward;
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
        }

        // 守り: 技を出していない間だけ（打っている最中は打ち切る）。
        if (_defense.Enabled && !Defending && Threat(sense) is { } threat)
        {
            StartDefense(sense, face, threat);
        }

        if (Defending)
        {
            return _block is { } block
                ? MotionImitation.ToCommand(block, sense, face, 0f, _walkSpeed, _maxTwist)
                : MotionImitation.ToCommand(_guard with { Head = new Vector3(0f, 0f, -0.15f) }, sense, face, 0f, _walkSpeed, _maxTwist, _evade);
        }

        if (_counter)
        {
            // 受けた直後: 休まず、近い間合いの技で打ち返す。
            _counter = false;
            _restUntil = _time;
            _plan = _techniques.MinBy(x => MathF.Abs(x.Range - distance));
            _plannedAt = _time;
        }

        var resting = _time < _restUntil;
        var closest = _techniques.Min(x => x.Range);
        var pressed = !Exact && (resting ? distance <= closest + MotionTechnique.Window : _plan is { } p && distance < p.Range - MotionTechnique.Window);
        if (!resting || pressed)
        {
            if (_plan is null || _time - _plannedAt > PlanPatience)
            {
                _plan = _techniques[_random.Next(_techniques.Count)];
                _plannedAt = _time;
            }

            var fits = _techniques.Where(x => x.Fits(distance)).ToList();
            if (fits.Count > 0 && (pressed || fits.Contains(_plan)))
            {
                var chosen = fits.Contains(_plan) ? _plan : fits[_random.Next(fits.Count)];
                _current = chosen;
                _start = _time;
                Used.Add(chosen.Name);
                UsedAt.Add(distance);
                return MotionImitation.ToCommand(chosen.Motion.Frames[0], sense, face, _rate, _walkSpeed, _maxTwist);
            }
        }

        var want = resting ? StandOff : _plan!.Range;
        var move = distance > want + 0.02f ? face : distance < want - 0.02f ? -face : Vector3.Zero;
        return MotionImitation.ToCommand(_guard, sense, face, 0f, _walkSpeed, _maxTwist, move);
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
                if (fist.Position != default && Incoming(sense, fist.Position, fist.Velocity - sense.OpponentVelocity, shoulder, 1.15f * reach) is { } punch)
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
