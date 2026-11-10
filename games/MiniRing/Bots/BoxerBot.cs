using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// ボクシングの技を使うボット。<see cref="BotStyle"/> の値に従って、次のことを毎サブステップ決める。
/// <list type="bullet">
/// <item>攻め: コンビネーション（<see cref="Combo"/>）を選び、パンチ（<see cref="PunchSpec"/>）を順に打つ。打つ間は体重移動・踏み込み・レベルチェンジもする。</item>
/// <item>守り: 相手の拳が自分へ向かってくるのを見て、反応時間ののちに、来る拳の種類に効く防御技
/// （ブロック・ダッキング・ウィービング・スリップ・スウェー・バックステップ・サイドステップ）を選ぶ。動きは <see cref="DefenseBook"/>。</item>
/// <item>カウンター: 守り切ったら、守り方に合った打ち返しをすぐ出す。</item>
/// <item>足（間合い）: 間合いの出入り、打った後に下がる・回り込む（ピボット）、踏み込んでくる相手をジャブで止める、
/// 相手の周りを回る、ロープ際から離れる。続けて打たれたらクリンチ、近距離ではたまに投げ。</item>
/// </list>
/// 決めるのは指令（拳の目標・頭の位置・移動）だけで、当たるか・どれだけ効くかは物理が決める。乱数は種付きで決定的。
/// </summary>
public sealed class BoxerBot : IFighterControl
{
    /// <summary>
    /// 「向かってくる」とみなす拳の速さ [腕の長さ/s]。60cm の人形の腕は約 0.19m、パンチは 0.5〜1m/s なので、
    /// 人の大きさの絶対値（0.7m/s など）ではほとんどの拳を見落とす（PDCA サイクル 1）。
    /// </summary>
    private const float MinThreatSpeed = 2f;
    private const float ThreatHorizon = 0.45f;

    /// <summary>
    /// 動き出しを読み切ったとき（<see cref="BotStyle.Anticipation"/> = 1）の、気づく拳の速さ [腕の長さ/s] と先読みの時間 [s]。
    /// 拳が相手の体に対してこの速さで動き出せば、まだ遠くても「来る」と見る（防御ドリル D1）。
    /// </summary>
    private const float CueThreatSpeed = 0.8f;
    private const float CueHorizon = 0.7f;

    /// <summary>
    /// 振りかぶりを合図に読むときの拳の速さ [腕の長さ/s]（<see cref="BotStyle.Anticipation"/> = 1 のとき。0 では実質読まない）。
    /// フックは外へ約 0.09 秒振りかぶってから約 0.11 秒で振り込むので、振り込みを見てからでは人並みの反応時間では間に合わない
    /// （防御ドリル D1 の計測）。振りかぶりの拳はおよそ 3 腕の長さ/s で動く。
    /// </summary>
    private const float WindupCueSpeed = 2.5f;

    /// <summary>拳の手がかりが途切れても追跡を続ける時間 [s]（振りかぶりから振り込みへの切り返しで一瞬遅くなる）。</summary>
    private const float ThreatGap = 0.06f;
    private const float LandWindow = 0.15f;
    /// <summary>クリンチを考えるダメージ（約 1 秒の時定数で減っていく直近の被ダメージ。クリーンヒット 1〜2 発ぶん）。</summary>
    private const float ClinchDamage = 14f;
    private const float ClinchDuration = 0.9f;
    private const float ThrowDuration = 1.3f;
    private const float ApproachTimeout = 1.2f;

    /// <summary>1 回の守りを続ける上限 [s]（拳が向かってくる限り守るが、守ったまま固まらない）。</summary>
    private const float MaxDefenseDuration = 0.7f;

    private static readonly Hand[] Hands = [Hand.Left, Hand.Right];

    private readonly Random _random;
    private readonly (Combo Combo, float Cumulative)[] _comboTable;
    private readonly HandAction[] _hands = new HandAction[2];
    private readonly ThreatTrack[] _threats = new ThreatTrack[2];
    private readonly Defense? _forcedDefense;
    private Combo? _combo;
    private int _comboIndex;
    private float _comboClock;
    private bool _comboIsCounter;
    private bool _wantAttack;
    private float _approachTime;
    private float _cooldown = 1f;
    private float _exitTimer;
    private Defense _defense;
    private float _defenseTime;
    private float _defenseDuration;
    private bool _defenseHit;
    private float _defenseSide;
    private float _defenseProgress;
    private PunchKind _defenseKind;
    private DefenseMove _defenseMove = new();
    private float _pivotTimer;
    private float _pivotSide;
    private float _stopJabCooldown;
    private float _opponentClosing;
    private float _clinchTime = -1f;
    private float _throwTime = -1f;
    private Hand _throwHand;
    private float _recentDamage;
    private float _circleDirection = 1f;
    private float _circleTimer;
    private Vector3 _headIdle;
    private float _headIdleTimer;
    private Vector3 _head;
    private bool _opponentHighGuard;
    private float _reach;
    private float _gapSide;

    /// <param name="style">戦い方。</param>
    /// <param name="seed">乱数の種。</param>
    /// <param name="forcedDefense">
    /// 防御ドリル用: 気づいた拳には（効き目の表に関わらず）必ずこの技で守る。守る率は <see cref="BotStyle.DefenseRate"/> のまま。
    /// </param>
    public BoxerBot(BotStyle style, int seed, Defense? forcedDefense = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        Style = style.Validated();
        _random = new Random(seed);
        _forcedDefense = forcedDefense is Defense.None ? null : forcedDefense;
        // 名前順の累積の重み。辞書の並び（JSON の読み書きで変わりうる）に依らず、同じ種なら同じ型を選ぶ。
        var cumulative = 0f;
        _comboTable = Style.ComboWeights.Where(p => p.Value > 0f).OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => (Combo.Named(p.Key), cumulative += p.Value)).ToArray();
    }

    /// <summary>
    /// ボットの判断の仕組みの版。スタイルの値が同じでも、仕組みが変われば同じ入力から違う試合になるので、リプレイの見出しに残して照らし合わせる。
    /// 変えたら 1 増やす（1: 最初のボット、2: 防御技の表・動き出しの読み・ストップジャブ・ピボット）。
    /// </summary>
    public const int LogicVersion = 2;

    public BotStyle Style { get; }

    public BotStats Stats { get; } = new();

    /// <summary>いま出している守り（観戦・集計用）。</summary>
    public Defense CurrentDefense => _defense;

    /// <summary>その手で出している（または当たり判定の猶予中の）パンチ（観戦・集計用）。</summary>
    public Technique? CurrentTechnique(Hand hand)
    {
        var action = _hands[(int)hand];
        return action.Time < action.Duration + LandWindow ? action.Technique : null;
    }

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        var self = sense.Self;
        var reach = sense.ArmReach;
        _reach = reach;
        var toOpponent = sense.OpponentChest - self.Origin;
        toOpponent.Y = 0f;
        var distance = toOpponent.Length();
        var dir = distance > 1e-3f ? toOpponent / distance : self.Forward;
        var headLocal = sense.SelfHead == default ? new Vector3(0f, 0.3f * reach, 0f) : self.WorldToLocal(sense.SelfHead);

        _recentDamage *= MathF.Exp(-dt);
        if (!sense.OpponentAlive)
        {
            ResetActions();
            var back = Math.Clamp((distance - (Style.NeutralRange + 0.3f) * reach) / reach * 3f, -1f, 0f);
            _head = Vector3.Lerp(_head, Vector3.Zero, 1f - MathF.Exp(-8f * dt));
            return new FighterCommand(dir * back, dir,
                new HandCommand(Guard(Hand.Left, reach, headLocal), false), new HandCommand(Guard(Hand.Right, reach, headLocal), false), _head);
        }

        ReadOpponentGuard(sense);
        UpdateThreats(sense, dt);
        UpdateDefense(distance, reach, dt);
        UpdateStopJab(sense, dir, distance, reach, dt);
        UpdateAttack(distance, reach, dt);
        UpdatePunches(dt);

        var grip = (Left: false, Right: false);
        var left = Guard(Hand.Left, reach, headLocal);
        var right = Guard(Hand.Right, reach, headLocal);
        foreach (var hand in Hands)
        {
            var action = _hands[(int)hand];
            if (action.Active)
            {
                var aim = self.WorldToLocal(action.Spec.ToBody ? sense.OpponentChest : sense.OpponentHead) + action.AimOffset;
                Set(hand, ref left, ref right, action.Spec.Evaluate(hand, action.Time, action.Duration, Guard(hand, reach, headLocal), aim, reach));
            }
        }

        var move = Footwork(self, dir, distance, reach, dt);
        if (_defense != Defense.None)
        {
            DefenseGloves(reach, headLocal, ref left, ref right);
        }

        if (_clinchTime >= 0f || _throwTime >= 0f)
        {
            GrappleTargets(sense, reach, dt, ref left, ref right, ref grip, ref move, dir);
        }

        _head = Vector3.Lerp(_head, HeadTarget(reach, dt), 1f - MathF.Exp(-(_defense is Defense.None ? 12f : _defenseMove.Rate) * dt));
        return new FighterCommand(move, dir, new HandCommand(left, grip.Left), new HandCommand(right, grip.Right), _head);
    }

    public void OnPunchLanded(Hand hand, float damage)
    {
        var action = _hands[(int)hand];
        if (action.Technique is { } technique && action.Time < action.Duration + LandWindow)
        {
            Stats.Landed(technique, damage, action.Counter);
            _hands[(int)hand] = action with { Technique = null }; // 1 発で 1 回だけ数える
        }
    }

    public void OnPunchTaken(in HitEvent hit)
    {
        _recentDamage += hit.Damage;
        if (_defense != Defense.None)
        {
            _defenseHit = true;
        }
        else
        {
            Stats.TookOpenHit();
        }
    }

    /// <summary>構え（体フレームのローカル）: 前の手は前へ、後ろの手はあご寄り。高さは <see cref="BotStyle.GuardHeight"/>。</summary>
    public Vector3 Guard(Hand hand, float reach, Vector3 headLocal)
    {
        var lead = hand == Style.Lead;
        var chin = headLocal.Y - (0.15f * reach);
        var low = -0.12f * reach;
        var y = low + ((chin - low) * Style.GuardHeight * (lead ? 0.85f : 1f));
        return new Vector3(hand.LocalSide() * (lead ? 0.17f : 0.13f) * reach, y, (lead ? 0.52f : 0.36f) * reach);
    }

    // ── 守り ──

    /// <summary>相手の拳それぞれについて、自分の頭か胴へ向かってくるかを見る。反応時間だけ続いたら守りを選ぶ。</summary>
    private void UpdateThreats(in FighterSense sense, float dt)
    {
        var reach = sense.ArmReach;
        var head = sense.SelfHead == default ? sense.Self.Origin + new Vector3(0f, 0.3f * reach, 0f) : sense.SelfHead;
        var body = sense.Self.Origin - new Vector3(0f, 0.3f * reach, 0f);
        var a = Style.Anticipation;
        var minSpeed = (MinThreatSpeed + ((CueThreatSpeed - MinThreatSpeed) * a)) * reach;
        var horizon = ThreatHorizon + ((CueHorizon - ThreatHorizon) * a);
        foreach (var hand in Hands)
        {
            // 拳の速度から相手の体の動き（歩き・踏み込み）を引いた「腕の動き」で見る。
            var seen = sense.Opponent(hand);
            var glove = seen with { Velocity = seen.Velocity - sense.OpponentVelocity };
            var track = _threats[(int)hand];
            var toBody = false;
            var seenComing = seen.Position != default
                && (Approaching(glove, head, body, reach, minSpeed, horizon, out toBody) || WindingUp(glove, head, body, sense.OpponentChest, reach, a, out toBody));
            if (!seenComing)
            {
                var gap = track.Gap + dt;
                _threats[(int)hand] = track.Time > 0f && gap <= ThreatGap ? track with { Gap = gap } : new ThreatTrack();
                continue;
            }

            track = track with { Time = track.Time + dt, Gap = 0f };
            // 守りの最中・組み付き中は決めずにおく（終わってからまだ来ていれば、そのとき決める）。
            if (!track.Decided && track.Time >= Style.ReactionTime && _defense == Defense.None && !Grappling)
            {
                track = track with { Decided = true };
                if (_random.NextSingle() < Style.DefenseRate)
                {
                    var kind = Classify(sense.Self.InverseRotate(glove.Velocity), toBody);
                    var gloveLocal = sense.Self.WorldToLocal(glove.Position);
                    StartDefense(_forcedDefense ?? Choose(kind), kind, side: gloveLocal.X >= 0f ? -1f : 1f);
                }
            }

            _threats[(int)hand] = track;
        }
    }

    /// <summary>
    /// 振りかぶり（読み）: 頭の届く範囲にある拳が、相手の体に対して速く動いた（どの向きでも）。
    /// 読む度合い <paramref name="anticipation"/> が低いほど速い動きでないと合図にしない（0 では実質見ない）。
    /// </summary>
    private static bool WindingUp(GloveSense glove, Vector3 head, Vector3 body, Vector3 ownerChest, float reach, float anticipation, out bool toBody)
    {
        toBody = glove.Position.Y < (head.Y + body.Y) * 0.5f;
        if (anticipation <= 0f)
        {
            return false;
        }

        var cueSpeed = (WindupCueSpeed + (6f * (1f - anticipation))) * reach;
        var r = head - glove.Position;
        var speed = glove.Velocity.Length();
        // 引き戻す拳（相手自身の胸へまっすぐ戻る）は合図にしない。振りかぶりは横・下へ開くので残る。頭から離れるかどうかでは
        // 見分けられない: フックの振りかぶりも頭から斜めに離れる（余弦 −0.6 前後。防御ドリル D1 の計測）。
        // レビュー指摘: 引き戻しを合図にすると追跡が切れず、同じ手の 2 発目に守りの判断が入らなかった。
        var home = ownerChest - glove.Position;
        var retracting = Vector3.Dot(glove.Velocity, home) > 0.7f * speed * home.Length();
        return r.Length() < (1f + (0.3f * anticipation)) * reach && speed > cueSpeed && !retracting;
    }

    /// <summary>拳が速く近づいてきていて、まもなく頭か胴の近くを通るか。</summary>
    private static bool Approaching(GloveSense glove, Vector3 head, Vector3 body, float reach, float minSpeed, float horizon, out bool toBody)
    {
        toBody = false;
        var speed = glove.Velocity.Length();
        if (speed < minSpeed)
        {
            return false;
        }

        var heading = glove.Velocity / speed;
        var best = float.MaxValue;
        foreach (var (target, isBody) in new[] { (head, false), (body, true) })
        {
            var r = target - glove.Position;
            var distance = r.Length();
            var closing = Vector3.Dot(glove.Velocity, r) / MathF.Max(distance, 1e-4f);
            if (closing < minSpeed || distance / closing > horizon)
            {
                continue;
            }

            var miss = (r - (heading * Vector3.Dot(r, heading))).Length();
            var score = MathF.Min(miss, distance);
            if (score < 0.45f * reach && score < best)
            {
                best = score;
                toBody = isBody;
            }
        }

        if (best < float.MaxValue)
        {
            return true;
        }

        // 横から回り込む拳（フック）: まっすぐ外挿すると頭を外れるので上の判定では見えない。頭（胴）の届く範囲で、
        // その高さのまま水平に頭の方へ振られていれば「来る」と見る（防御ドリル D1: フックを 1 度も見つけられなかった）。
        foreach (var (target, isBody) in new[] { (head, false), (body, true) })
        {
            var r = target - glove.Position;
            var flatR = r with { Y = 0f };
            var flatV = glove.Velocity with { Y = 0f };
            var lateralSpeed = flatV.Length();
            if (r.Length() > reach || MathF.Abs(r.Y) > 0.45f * reach || lateralSpeed < minSpeed
                || Vector3.Dot(flatV, flatR) < 0.2f * lateralSpeed * flatR.Length())
            {
                continue;
            }

            toBody = isBody;
            return true;
        }

        return false;
    }

    /// <summary>拳の速度（自分のフレーム）から種類を見分ける: 上向きが強ければアッパー、横向きが強ければフック、ほかはストレート。</summary>
    internal static PunchKind Classify(Vector3 localVelocity, bool toBody)
    {
        if (toBody)
        {
            return PunchKind.Body;
        }

        var v = localVelocity;
        // 振りかぶり（下へ引く・横へ開く）でも見分けられるよう、向きの符号は見ない。
        return MathF.Abs(v.Y) > 0.8f * MathF.Max(MathF.Abs(v.X), MathF.Abs(v.Z)) ? PunchKind.Uppercut
            : MathF.Abs(v.X) > 0.8f * MathF.Abs(v.Z) ? PunchKind.Hook
            : PunchKind.Straight;
    }

    /// <summary>
    /// 守りを選ぶ: 重み = スタイルの好み × その拳への効き目（<see cref="DefenseMove.Against"/>、防御ドリルで測った値）。
    /// 効き目 0 の技（アッパーにダッキング＝自分から当たりに行く、など）は選ばない。
    /// </summary>
    private Defense Choose(PunchKind kind)
    {
        var total = 0f;
        foreach (var d in DefenseBook.Techniques)
        {
            total += Style.Weight(d) * Style.Defenses.Of(d).Against(kind);
        }

        if (total <= 0f)
        {
            return Defense.None;
        }

        var pick = _random.NextSingle() * total;
        var last = Defense.None;
        foreach (var d in DefenseBook.Techniques)
        {
            var w = Style.Weight(d) * Style.Defenses.Of(d).Against(kind);
            if (w <= 0f)
            {
                continue;
            }

            last = d;
            pick -= w;
            if (pick <= 0f)
            {
                return d;
            }
        }

        return last;
    }

    private void StartDefense(Defense defense, PunchKind kind, float side)
    {
        if (defense == Defense.None)
        {
            return;
        }

        _defense = defense;
        _defenseKind = kind;
        _defenseSide = side;
        _defenseTime = 0f;
        _defenseProgress = 0f;
        _defenseHit = false;
        _defenseMove = Style.Defenses.Of(defense);
        _defenseDuration = _defenseMove.Duration;
        _combo = null; // 守るときは連打を打ち切る（出ている拳はそのまま戻す）
        _wantAttack = false;
        if (defense == Defense.Block)
        {
            foreach (var hand in Hands)
            {
                _hands[(int)hand] = _hands[(int)hand] with { Time = MathF.Max(_hands[(int)hand].Time, _hands[(int)hand].Duration) };
            }
        }
    }

    private void UpdateDefense(float distance, float reach, float dt)
    {
        if (_defense == Defense.None)
        {
            return;
        }

        _defenseTime += dt;
        // 拳がまだ向かってきていれば、守りの最も深い形（U 字の底・守りの途中）で留まって守り続ける（上限あり）。
        // 早く解くと、守りの直後に打たれて「守れた」と数え、カウンターまで出してしまう。
        // 進み具合を時間で割るだけだと、延ばした間に U 字の頭が起き上がってしまっていた（レビュー指摘）。
        var stillComing = _threats[0].Time > 0f || _threats[1].Time > 0f;
        var holding = stillComing && _defenseProgress >= 0.5f && _defenseTime < MaxDefenseDuration;
        if (!holding)
        {
            _defenseProgress += dt / MathF.Max(1e-3f, _defenseDuration);
        }

        if (_defenseProgress < 1f && _defenseTime < MaxDefenseDuration + _defenseDuration)
        {
            return;
        }

        var defended = _defense;
        Stats.DefenseEnded(defended, _defenseHit);
        _defense = Defense.None;
        if (!_defenseHit && !Grappling && distance < Style.EngageRange * reach && _random.NextSingle() < Style.CounterRate)
        {
            StartCombo(CounterFor(defended), counter: true);
        }
    }

    /// <summary>守り方に合った打ち返し。</summary>
    private Combo CounterFor(Defense defense) => defense switch
    {
        Defense.Slip => new Combo("slip-counter", _defenseSide * Style.Lead.LocalSide() > 0f ? [Technique.Cross, Technique.LeadHook] : [Technique.LeadHook, Technique.Cross]),
        Defense.Duck => new Combo("duck-counter", _random.NextSingle() < 0.5f ? [Technique.RearUppercut, Technique.LeadHook] : [Technique.LeadBodyHook, Technique.LeadHook]),
        Defense.Sway => new Combo("pull-counter", [Technique.Cross]),
        Defense.Weave => new Combo("weave-counter", _defenseSide * Style.Lead.LocalSide() > 0f ? [Technique.LeadHook, Technique.Cross] : [Technique.RearHook, Technique.LeadHook]),
        Defense.SideStep => new Combo("angle-counter", [Technique.Cross, Technique.LeadHook]),
        Defense.StepBack => new Combo("step-counter", [Technique.Jab, Technique.Cross]),
        _ => new Combo("block-counter", [Technique.Jab, Technique.Cross]),
    };

    /// <summary>
    /// 守りの間の拳: 顔を覆う位置（頭の中心から）へ <see cref="DefenseMove.HandsUp"/> だけ寄せる。
    /// 胴への拳には肘を締めて脇腹を覆い、フックには来る側の拳でこめかみを覆う。
    /// </summary>
    private void DefenseGloves(float reach, Vector3 headLocal, ref Vector3 left, ref Vector3 right)
    {
        var m = _defenseMove;
        if (m.HandsUp <= 0f)
        {
            return;
        }

        foreach (var hand in Hands)
        {
            var side = hand.LocalSide();
            var cover = _defenseKind == PunchKind.Body
                ? headLocal + (new Vector3(side * (m.GloveWidth + 0.07f), m.GloveHeight - m.BodyDrop, m.GloveForward * 0.8f) * reach)
                : _defenseKind == PunchKind.Hook && side == -_defenseSide
                    ? headLocal + (new Vector3(side * m.TempleWidth, m.GloveHeight + 0.05f, m.GloveForward * 0.55f) * reach)
                    : headLocal + (new Vector3(side * m.GloveWidth, m.GloveHeight, m.GloveForward) * reach);
            var current = hand == Hand.Left ? left : right;
            Set(hand, ref left, ref right, Vector3.Lerp(current, cover, m.HandsUp));
        }
    }

    // ── 攻め ──

    private void UpdateAttack(float distance, float reach, float dt)
    {
        _cooldown -= dt;
        _exitTimer = MathF.Max(0f, _exitTimer - dt);
        if (_defense != Defense.None || _clinchTime >= 0f || _throwTime >= 0f || _combo is not null)
        {
            return;
        }

        if (_wantAttack)
        {
            _approachTime += dt;
            if (distance <= Style.AttackRange * reach * 1.15f)
            {
                _wantAttack = false;
                if (distance < 0.95f * reach && _random.NextSingle() < Style.ThrowRate)
                {
                    StartThrow();
                }
                else
                {
                    StartCombo(PickCombo(), counter: false);
                }
            }
            else if (_approachTime > ApproachTimeout || distance > Style.EngageRange * reach * 1.3f)
            {
                _wantAttack = false;
                _cooldown = 0.3f;
            }

            return;
        }

        if (!Style.Attacks || _cooldown > 0f || distance > Style.EngageRange * reach || AnyPunchActive)
        {
            return;
        }

        if (_recentDamage > ClinchDamage && distance < 1.1f * reach)
        {
            _recentDamage = 0f;
            if (_random.NextSingle() < Style.ClinchRate)
            {
                _clinchTime = 0f;
                Stats.Clinched();
                return;
            }
        }

        _wantAttack = true;
        _approachTime = 0f;
    }

    /// <summary>
    /// ストップジャブ: 攻めていないときに相手が普段の間合いの内側へ踏み込んできたら、前の手のジャブで止める。
    /// 相手の踏み込みは胸と胸の距離の縮む速さで見る（1 回の踏み込みに 1 回まで）。
    /// </summary>
    private void UpdateStopJab(in FighterSense sense, Vector3 dir, float distance, float reach, float dt)
    {
        // 相手の胴がこちらへ向かう速さ（自分の動きと物理の揺れを含めないよう、相手の速度から。0.05 秒でならす）。
        var closing = -Vector3.Dot(sense.OpponentVelocity with { Y = 0f }, dir);
        _opponentClosing += (closing - _opponentClosing) * (1f - MathF.Exp(-dt / 0.05f));
        _stopJabCooldown = MathF.Max(0f, _stopJabCooldown - dt);
        if (!Style.Attacks || Style.StopJab <= 0f || _stopJabCooldown > 0f || _defense != Defense.None || Grappling
            || _combo is not null || _wantAttack || _hands[(int)Style.Lead].Active)
        {
            return;
        }

        if (_opponentClosing > 1.5f * reach && distance < Style.NeutralRange * reach && distance > 0.8f * reach)
        {
            _stopJabCooldown = 0.6f;
            if (_random.NextSingle() < Style.StopJab)
            {
                StartCombo(StopJabCombo, counter: false);
            }
        }
    }

    private static readonly Combo StopJabCombo = new("stop-jab", [Technique.Jab]);

    private Combo PickCombo()
    {
        if (_comboTable.Length == 0)
        {
            return Combo.Named("1");
        }

        var pick = _random.NextSingle() * _comboTable[^1].Cumulative;
        foreach (var (combo, cumulative) in _comboTable)
        {
            if (pick < cumulative)
            {
                return combo;
            }
        }

        return _comboTable[^1].Combo;
    }

    private void StartCombo(Combo combo, bool counter)
    {
        _pivotTimer = 0f;
        _combo = combo;
        _comboIndex = 0;
        _comboClock = 0f;
        _comboIsCounter = counter;
        Stats.ComboStarted(combo.Name);
    }

    /// <summary>連打を進め（次のパンチの開始時刻が来て手が空いていれば打つ）、出ている拳の時計を進める。</summary>
    private void UpdatePunches(float dt)
    {
        foreach (var hand in Hands)
        {
            var action = _hands[(int)hand];
            if (action.Time < action.Duration + LandWindow)
            {
                _hands[(int)hand] = action with { Time = action.Time + dt };
            }
        }

        if (_combo is not { } combo || Grappling)
        {
            return;
        }

        _comboClock -= dt;
        if (_comboClock > 0f)
        {
            return;
        }

        var technique = combo.Steps[_comboIndex];
        var spec = PunchSpec.Of(technique);
        var puncher = spec.Lead ? Style.Lead : Opposite(Style.Lead);
        if (_hands[(int)puncher].Active)
        {
            return; // 同じ手が続くときは戻るのを待つ
        }

        (technique, spec, var aimOffset) = AimAtGap(technique, spec);
        var duration = spec.Duration / Style.PunchSpeed;
        _hands[(int)puncher] = new HandAction(technique, spec, 0f, duration, _comboIsCounter, aimOffset);
        Stats.Thrown(technique, _comboIsCounter);
        _comboIndex++;
        _comboClock = duration * combo.Gap;
        if (_comboIndex >= combo.Steps.Count)
        {
            _combo = null;
            _cooldown = (1.3f + ((0.3f - 1.3f) * Style.Aggression)) * Range(0.7f, 1.3f) + duration;
            if (_random.NextSingle() < Style.ExitAfterCombo)
            {
                _exitTimer = duration + 0.5f;
            }

            if (_random.NextSingle() < Style.PivotAfterCombo)
            {
                // 前の手の側へ回り込む（相手の後ろの手から離れ、相手の正面から外れる）。
                _pivotTimer = duration + 0.45f;
                _pivotSide = Style.Lead.LocalSide();
                Stats.Pivoted();
            }
        }
    }

    private bool AnyPunchActive => _hands[0].Active || _hands[1].Active;

    private bool Grappling => _clinchTime >= 0f || _throwTime >= 0f;

    /// <summary>相手の構え: 両拳が顔の近くにあるか（ハイガード）と、顔の左右どちらが空いているか（自分のフレームの X の符号）。</summary>
    private void ReadOpponentGuard(in FighterSense sense)
    {
        var reach = sense.ArmReach;
        var head = sense.OpponentHead;
        var left = sense.OpponentLeft.Position;
        var right = sense.OpponentRight.Position;
        if (left == default || right == default)
        {
            _opponentHighGuard = false; // 拳が見えないホスト（古い FighterSense）では読まない
            _gapSide = 0f;
            return;
        }

        _opponentHighGuard = Vector3.Distance(left, head) < 0.55f * reach && Vector3.Distance(right, head) < 0.55f * reach;
        var gap = sense.Self.WorldToLocal(head).X - sense.Self.WorldToLocal((left + right) * 0.5f).X;
        _gapSide = MathF.Abs(gap) < 0.05f * reach ? 0f : MathF.Sign(gap);
    }

    /// <summary>
    /// 構えを読んで狙いを変える（<see cref="BotStyle.ReadGuard"/> の確率で）: ハイガードにはストレートを胴へ、
    /// 頭を打つときは拳の無い側へ腕の長さの 0.25 倍ずらす。
    /// </summary>
    private (Technique Technique, PunchSpec Spec, Vector3 AimOffset) AimAtGap(Technique technique, PunchSpec spec)
    {
        if (Style.ReadGuard <= 0f || technique == Technique.Feint || _random.NextSingle() >= Style.ReadGuard)
        {
            return (technique, spec, Vector3.Zero);
        }

        if (_opponentHighGuard && technique is Technique.Jab or Technique.Cross)
        {
            var body = technique == Technique.Jab ? Technique.BodyJab : Technique.BodyCross;
            return (body, PunchSpec.Of(body), Vector3.Zero);
        }

        return spec.ToBody ? (technique, spec, Vector3.Zero) : (technique, spec, new Vector3(_gapSide * 0.25f * _reach, 0f, 0f));
    }

    // ── 組み付き ──

    private void StartThrow()
    {
        _throwTime = 0f;
        _throwHand = _random.NextSingle() < 0.5f ? Hand.Left : Hand.Right;
        Stats.TriedThrow();
    }

    private void GrappleTargets(in FighterSense sense, float reach, float dt, ref Vector3 left, ref Vector3 right,
        ref (bool Left, bool Right) grip, ref Vector3 move, Vector3 dir)
    {
        var chest = sense.Self.WorldToLocal(sense.OpponentChest);
        if (_clinchTime >= 0f)
        {
            // クリンチ: 両手で相手の胴を抱えて動きを止め、ゆっくり離す（投げにならない速さで）。
            var holding = _clinchTime > 0.12f && _clinchTime < ClinchDuration;
            left = chest + (new Vector3(0.12f, 0.05f, 0f) * reach);
            right = chest + (new Vector3(-0.12f, 0.05f, 0f) * reach);
            grip = (holding, holding);
            move = dir * 0.4f;
            _clinchTime += dt;
            if (_clinchTime > ClinchDuration + 0.1f)
            {
                _clinchTime = -1f;
                _exitTimer = 0.6f;
            }

            return;
        }

        // 投げ: 片手で胴を掴み、体の反対側へ引き倒して、速く動いているうちに離す。
        var pull = new Vector3(-_throwHand.LocalSide() * 0.6f, 0.4f, -0.6f) * reach;
        var target = _throwTime < 0.9f ? chest : chest + pull;
        Set(_throwHand, ref left, ref right, target);
        var held = _throwTime > 0.25f && _throwTime < ThrowDuration;
        grip = _throwHand == Hand.Left ? (held, false) : (false, held);
        if (_throwTime > 0.9f)
        {
            move = -dir;
        }

        _throwTime += dt;
        if (_throwTime > ThrowDuration + 0.05f)
        {
            _throwTime = -1f;
            _cooldown = 0.8f;
        }
    }

    // ── 足と頭 ──

    private Vector3 Footwork(BodyFrame self, Vector3 dir, float distance, float reach, float dt)
    {
        _circleTimer -= dt;
        if (_circleTimer <= 0f)
        {
            _circleTimer = Range(0.8f, 2.5f);
            // 基本は前の手の側へ回る（相手の強い後ろの手から離れる）。
            _circleDirection = (_random.NextSingle() < 0.7f ? 1f : -1f) * Style.Lead.LocalSide();
        }

        var desired = _wantAttack || _combo is not null ? Style.AttackRange
            : _exitTimer > 0f ? Style.NeutralRange + 0.3f
            : Style.NeutralRange;
        var approach = Math.Clamp((distance - (desired * reach)) / reach * 3f, -1f, 1f);
        foreach (var action in _hands)
        {
            if (action.Active)
            {
                approach += action.Spec.StepIn * Envelope(action);
            }
        }

        var lateral = Vector3.Cross(Vector3.UnitY, dir); // 自分の左（ローカル +X）
        var side = 0f;
        if (_defense != Defense.None)
        {
            // 守りの足: 下がる技は少なくともその分下がり、横へ出る技は来る拳から離れる側へ出る。
            var m = _defenseMove;
            approach = m.Move < 0f ? MathF.Min(approach, m.Move) : m.Move > 0f ? MathF.Max(approach, m.Move) : approach;
            side = _defenseSide * m.Step;
        }

        // ピボットは最後のパンチを打ち終えてから（打っている間に回ると、その踏み込みを打ち消す。レビュー指摘）。
        _pivotTimer = MathF.Max(0f, _pivotTimer - dt);
        if (_pivotTimer > 0f && _defense == Defense.None && !AnyPunchActive)
        {
            side += _pivotSide;
            approach = MathF.Min(approach, -0.2f);
        }

        var circling = _combo is null && !_wantAttack && _pivotTimer <= 0f ? Style.Circling * _circleDirection : 0f;
        var move = (dir * Math.Clamp(approach, -1f, 1f)) + (lateral * Math.Clamp(circling + side, -1f, 1f));
        return move + StayInside(self.Origin, reach);
    }

    /// <summary>ロープ際に近づいたら中央へ戻る向きを足す（追い詰められると打たれ続け、落ちれば負け）。</summary>
    private static Vector3 StayInside(Vector3 position, float reach)
    {
        var half = BoxingGame.RingSize * 0.5f;
        var margin = MathF.Max(0.12f, 0.8f * reach);
        var offset = position - BoxingGame.RingCenter;
        static float Push(float o, float half, float margin)
        {
            var over = MathF.Abs(o) - (half - margin);
            return over > 0f ? -MathF.Sign(o) * MathF.Min(1.5f, over / margin * 1.5f) : 0f;
        }

        return new Vector3(Push(offset.X, half, margin), 0f, Push(offset.Z, half, margin));
    }

    private Vector3 HeadTarget(float reach, float dt)
    {
        var lead = Style.Lead.LocalSide();
        if (_defense != Defense.None)
        {
            return DefenseHead(_defenseMove, _defenseSide, _defenseProgress) * reach;
        }

        var shift = Vector3.Zero;
        foreach (var action in _hands)
        {
            if (action.Active)
            {
                var s = action.Spec.HeadShift;
                shift += new Vector3(s.X * lead, s.Y, s.Z) * (reach * Envelope(action));
            }
        }

        _headIdleTimer -= dt;
        if (_headIdleTimer <= 0f)
        {
            _headIdleTimer = Range(0.35f, 1.1f);
            var a = Style.HeadMovement;
            _headIdle = _random.NextSingle() < 1f - (0.7f * a)
                ? Vector3.Zero
                : new Vector3(Range(-0.32f, 0.32f), Range(-0.35f, 0f), Range(-0.08f, 0.2f)) * (reach * a);
        }

        return _headIdle + shift;
    }

    /// <summary>
    /// 守りの間の頭の位置（腕の長さ単位）。横は始め（<see cref="DefenseMove.HeadX"/>）から終わり（<see cref="DefenseMove.EndX"/>）へ動き、
    /// 横へ抜ける技（ウィービング）では沈みが途中で最も深い U 字になる。
    /// </summary>
    internal static Vector3 DefenseHead(DefenseMove m, float side, float progress)
    {
        if (!m.Sweep)
        {
            return new Vector3(side * m.HeadX, m.HeadY, m.HeadZ);
        }

        var t = Math.Clamp(progress, 0f, 1f);
        var x = side * (m.HeadX + ((m.EndX - m.HeadX) * t));
        return new Vector3(x, m.HeadY * MathF.Sin(MathF.PI * t), m.HeadZ);
    }

    // ── 補助 ──

    private void ResetActions()
    {
        _combo = null;
        _wantAttack = false;
        if (_defense != Defense.None)
        {
            Stats.DefenseEnded(_defense, _defenseHit);
        }

        _defense = Defense.None;
        _clinchTime = -1f;
        _throwTime = -1f;
        foreach (var hand in Hands)
        {
            _hands[(int)hand] = _hands[(int)hand] with { Time = _hands[(int)hand].Duration + LandWindow };
        }
    }

    private static float Envelope(in HandAction action) => MathF.Sin(MathF.PI * Math.Clamp(action.Time / action.Duration, 0f, 1f));

    private static Hand Opposite(Hand hand) => hand == Hand.Left ? Hand.Right : Hand.Left;

    private static void Set(Hand hand, ref Vector3 left, ref Vector3 right, Vector3 target)
    {
        if (hand == Hand.Left)
        {
            left = target;
        }
        else
        {
            right = target;
        }
    }

    private float Range(float min, float max) => min + ((max - min) * _random.NextSingle());

    /// <summary>片手で出しているパンチ。<see cref="Technique"/> は命中を数えたら null（同じ 1 発を 2 回数えない）。</summary>
    private readonly record struct HandAction(Technique? Technique, PunchSpec Spec, float Time, float Duration, bool Counter, Vector3 AimOffset = default)
    {
        public bool Active => Duration > 0f && Time < Duration;
    }

    private readonly record struct ThreatTrack(float Time, bool Decided, float Gap = 0f);
}
