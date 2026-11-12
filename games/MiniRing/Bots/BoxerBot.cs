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

    /// <summary>
    /// 拳が狙いを追う時間（その拳の時間に対する割合）。打ち始めのこの間だけ相手の頭（胴）の今の位置を追い、あとはその点へ打ち抜く。
    /// 人の拳は打ち出したら行き先を直せない。以前は当たるまで毎回狙い直していたため、頭をどう動かしても拳が付いてきて、
    /// 合図と同時に回避を始めても右ストレートを 1 発もきれいに外せなかった（回避の基本動作 B2）。
    /// </summary>
    private const float AimTrackShare = 0.15f;
    /// <summary>クリンチを考えるダメージ（約 1 秒の時定数で減っていく直近の被ダメージ。クリーンヒット 1〜2 発ぶん）。</summary>
    private const float ClinchDamage = 14f;
    private const float ClinchDuration = 0.9f;
    private const float ThrowDuration = 1.3f;
    private const float ApproachTimeout = 1.2f;

    /// <summary>1 回の守りを続ける上限 [s]（拳が向かってくる限り守るが、守ったまま固まらない）。</summary>
    private const float MaxDefenseDuration = 0.7f;

    /// <summary>頭がこれ（腕の長さ単位）より下がっていたら「しゃがんでいる」。</summary>
    private const float CrouchDepth = 0.25f;

    /// <summary>しゃがみの疲れ（続けてしゃがんだ時間）が、立っている間に戻る速さ（しゃがんだ時間の何倍の速さで抜けるか）。</summary>
    private const float CrouchRecovery = 0.7f;

    /// <summary>
    /// しゃがみの疲れの上限 [s]。これを超えたら頭を沈める守り（ダッキング・ウィービング・沈むスリップ）を選びにくくし、
    /// 拳が来続けても沈んだまま守り続けない（起き上がる）。ずっとしゃがんだまま戦わない（予測編 P7: インファイターが 30 % しゃがんでいた）。
    /// </summary>
    private const float CrouchBudget = 0.4f;

    /// <summary>疲れが上限を超えたとき、頭を沈める守りの重みに掛ける倍率。</summary>
    private const float TiredCrouchWeight = 0.15f;

    /// <summary>頭を沈める守りを、拳が来続けても沈んだまま延ばしてよい上限 [s]（沈まない守りは <see cref="MaxDefenseDuration"/>）。</summary>
    private const float MaxCrouchedDefense = 0.45f;

    /// <summary>
    /// 溜めで胴を逆へひねる大きさ [rad]（後ろの手 / 前の手）。打つ手の肩を後ろへ引いて胴のバネを縮め、打ち出すと前へ回す。
    /// 溜めの時間が <see cref="FullLoad"/> でこの大きさまで引ききる（短い溜めは浅くしか引けない）。
    /// </summary>
    private const float LoadTwistRear = 25f * MathF.PI / 180f;
    private const float LoadTwistLead = 12f * MathF.PI / 180f;

    /// <summary>溜めで引ききるまでの時間 [s]（<see cref="BotStyle.Telegraph"/> の上限と同じ）。</summary>
    private const float FullLoad = BotStyle.MaxTelegraph;

    /// <summary>溜めで頭を沈める量（腕の長さ倍）: 膝を沈めて脚のバネを縮める。</summary>
    private const float LoadDip = 0.08f;

    /// <summary>
    /// 溜めで拳を引く量（腕の長さ倍、後ろの手 / 前の手）。打つ前に拳をわずかに引いて肩を入れる（実際のボクシングでよく言う「テレフォン」）。
    /// 胸へ戻る向きの動きなので、向かってくる拳とは見なされない。
    /// </summary>
    private static readonly Vector3 LoadCockRear = new(0f, -0.03f, -0.1f);
    private static readonly Vector3 LoadCockLead = new(0f, -0.01f, -0.05f);

    /// <summary>予測で守りを始めたあと、次に予測で動くまでの間 [s]（同じ手がかりで何度も守りを出さない）。</summary>
    private const float PredictionCooldown = 0.5f;

    /// <summary>相手の打ち始めの間合いから外へ置く余裕（腕の長さ倍、<see cref="BotStyle.EdgeRange"/>）。</summary>
    private const float EdgeMargin = 0.1f;

    private static readonly Hand[] Hands = [Hand.Left, Hand.Right];

    private readonly Random _random;
    private readonly (Combo Combo, float Cumulative)[] _comboTable;
    private readonly HandAction[] _hands = new HandAction[2];
    private readonly ThreatTrack[] _threats = new ThreatTrack[2];
    private readonly Defense? _forcedDefense;
    private readonly OpponentRead _read = new();
    private float _predictionCooldown;
    private bool _predicted;
    private bool _predictedPunchCame;
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

    /// <summary>しゃがみの疲れ [s]: しゃがんでいる間に溜まり、立てば抜ける。</summary>
    private float _crouchTime;
    private bool _crouchTired;
    private bool _defenseHit;
    private float _defenseSide;
    private float _defenseProgress;
    private Combo? _plannedCounter;
    private float _plannedCounterAt;
    private bool _counterLaunched;
    private PunchKind _defenseKind;
    private DefenseMove _defenseMove = new();
    private float _pivotTimer;
    private float _pivotSide;
    private float _stopJabCooldown;
    private float _opponentClosing;
    private float _ropeEscapeSide;

    /// <summary>
    /// 守っていない時の頭の高さ（ワールド）。守りの拳を構えの高さに残すときの基準（<see cref="DefenseMove.GloveAnchor"/>）。
    /// 体フレームの原点は肩なので、体フレームの高さで覚えると、腰から沈んだとき肩と一緒に拳も下がってしまう（レビュー指摘）。
    /// </summary>
    private float? _stanceHeadY;

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
    /// 変えたら 1 増やす（1: 最初のボット、2: 防御技の表・動き出しの読み・ストップジャブ・ピボット、
    /// 3: インサイドスリップ・ローリング、回避しながら打つカウンターと拳の種類ごとの打ち返し、4: 狙いの固定・外側への回避・守りの拳の高さ、
    /// 5: 打つ前の溜め（腰と肩のひねり）・相手の読みによる予測の回避・相手の届く間合いのすぐ外に立つ、6: しゃがみの疲れ・予測の回避の読み直し、
    /// 7: 意識が薄れるとガードを下げる、8: 回避からの返しの拳に体の動きを移す（HandCommand.Spring）、9: 溜めは逆へ引いて体のバネを縮め、全ての拳に体の動きとタメを移す、
    /// 10: ロープの位置を試合のリング（RingLayout）から読む。剣士（SwordsmanBot）は間合いを読んで打って離れる（HitAndAway）、
    /// 11: 剣士は相手の武器を握りから先までの線として読み、受け流し・足と頭で外す（WeaponDefense）。遠間に、見て覚えた相手の届く距離を入れる（SeenReach））。
    /// </summary>
    public const int LogicVersion = 13; // 13: 折れた武器を、残った長さで短いまま使う（失った分だけ間合いを詰める）か、柄だけなら捨てて素手で戦う（DisarmedFallback）。10: 狙いどころ（AimWeights: あご・こめかみ・露出した脳・肝臓・みぞおち・心臓・ガードの腕）を選んで、無理のない範囲で狙う。11: 剣士の守り（claude/voxel-damage）。12: 10 と 11 を合わせた

    public BotStyle Style { get; }

    public BotStats Stats { get; } = new();

    /// <summary>相手の読み（覚えた拳の数・打ち始めの間合い。観戦・調査用）。</summary>
    internal OpponentRead Opponent => _read;

    /// <summary>いまの守りの側（体フレームの X の符号、調査用）と、予測で始めた守りか。</summary>
    internal (float Side, bool Predicted) DefenseSide => (_defenseSide, _predicted);

    /// <summary>いま出している守り（観戦・集計用）。</summary>
    public Defense CurrentDefense => _defense;

    /// <summary>その手で出している（または当たり判定の猶予中の）パンチ（観戦・集計用）。</summary>
    public Technique? CurrentTechnique(Hand hand)
    {
        var action = _hands[(int)hand];
        return action.Time >= 0f && action.Time < action.Duration + LandWindow ? action.Technique : null;
    }

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        _targets = sense.OpponentTargets;
        _faintness = float.IsFinite(sense.SelfFaintness) ? Math.Clamp(sense.SelfFaintness, 0f, 1f) : 0f;
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
            _read.Pause();
            _crouchTime = MathF.Max(0f, _crouchTime - (CrouchRecovery * dt)); // 相手が倒れている間に、しゃがみの疲れも抜ける
            var back = Math.Clamp((distance - (Style.NeutralRange + 0.3f) * reach) / reach * 3f, -1f, 0f);
            _head = Vector3.Lerp(_head, Vector3.Zero, 1f - MathF.Exp(-8f * dt));
            return new FighterCommand(dir * back, dir,
                new HandCommand(Guard(Hand.Left, reach, headLocal), false), new HandCommand(Guard(Hand.Right, reach, headLocal), false), _head);
        }

        if (_defense == Defense.None)
        {
            _stanceHeadY = self.Origin.Y + headLocal.Y;
        }

        ReadOpponentGuard(sense);
        _read.Observe(sense, dt);
        UpdatePrediction(sense, dt);
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
                // 溜めのある拳は、溜めの始め（決めた時）に狙いを決める。溜めの無い拳は打ち始めの少しの間だけ追う。
                if (action.AimWorld == default || (!action.Loaded && action.Time <= action.Duration * AimTrackShare))
                {
                    var natural = action.Spec.ToBody ? sense.OpponentChest : sense.OpponentHead;
                    // 狙いどころ（戦略が選んだ点）へ、無理のない範囲（Targeting）で寄せる。使えなくなった狙い（脳が隠れた等）は真ん中へ。
                    var chosen = action.Goal == AimGoal.Center ? null : sense.OpponentTargets?.Point(action.Goal, sense.SelfHead);
                    action = action with { AimWorld = chosen is { } point ? Targeting.Clamp(natural, point, sense.SelfHead, reach) : natural };
                    _hands[(int)hand] = action;
                }

                var aim = self.WorldToLocal(action.AimWorld) + action.AimOffset;
                var target = action.Spec.Evaluate(hand, action.Time, action.Duration, Guard(hand, reach, headLocal), aim, reach);
                if (action.Time < 0f && action.Loaded)
                {
                    target += (hand == Style.Lead ? LoadCockLead : LoadCockRear) * (reach * Wound(action));
                }

                Set(hand, ref left, ref right, target);
            }
        }

        var move = Footwork(self, dir, distance, reach, sense.Arena, dt);
        if (_defense != Defense.None)
        {
            DefenseGloves(reach, headLocal, self.Origin.Y, ref left, ref right);
        }

        if (_clinchTime >= 0f || _throwTime >= 0f)
        {
            GrappleTargets(sense, reach, dt, ref left, ref right, ref grip, ref move, dir);
        }

        _head = Vector3.Lerp(_head, HeadTarget(reach, dt), 1f - MathF.Exp(-(_defense is Defense.None ? 12f : _defenseMove.Rate) * dt));
        var crouched = _head.Y < -CrouchDepth * reach;
        _crouchTime = crouched ? _crouchTime + dt : MathF.Max(0f, _crouchTime - (CrouchRecovery * dt));
        // 一度疲れたら、疲れが抜けきるまで（立ち上がって息を整えるまで）沈む守りを控える。
        _crouchTired = _crouchTime > CrouchBudget || (_crouchTired && _crouchTime > 0f);
        Stats.Tick(dt, crouched, _defense != Defense.None, _hands[0].Active || _hands[1].Active);
        return new FighterCommand(move, dir, new HandCommand(left, grip.Left, Spring(Hand.Left)), new HandCommand(right, grip.Right, Spring(Hand.Right)), _head, LoadTwist());
    }

    /// <summary>
    /// 体の動きとタメを拳へ移すか（<see cref="HandCommand.Spring"/>）: 拳を出していく間。溜め（引いて縮めた体のバネ）・回避・前の拳で
    /// 動いた体の運動量が拳に乗る。溜めの無い拳・止まった体から出す拳には、渡すものがほとんど無い（場合分けしない）。
    /// </summary>
    private float Spring(Hand hand)
    {
        // 拳を出していく間だけ（引き戻しでは移さない）。
        var action = _hands[(int)hand];
        // フェイント（振り出しきらない拳）は体を乗せない（打ち抜かない見せの拳で、体の勢いを使わない）。
        return action.Active && action.Spec.Extend >= 1f && action.Time >= 0f && action.Time < action.Duration * PunchSpec.OutboundShare ? 1f : 0f;
    }

    public void OnPunchLanded(Hand hand, float damage)
    {
        var action = _hands[(int)hand];
        // 溜めの間（拳はまだ構え）の接触は、その拳の命中に数えない（レビュー指摘）。
        if (action.Technique is { } technique && action.Time >= 0f && action.Time < action.Duration + LandWindow)
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

    /// <summary>
    /// 構え（体フレームのローカル）: 前の手は前へ、後ろの手はあご寄り。高さは <see cref="BotStyle.GuardHeight"/>、
    /// 左右の開きと前への出しは <see cref="BotStyle.GuardWidth"/>・<see cref="BotStyle.GuardReach"/>。
    /// </summary>
    /// <summary>意識が途切れる手前で、構えの拳が下がる量 [腕の長さ]。</summary>
    private const float FaintGuardDrop = 0.35f;

    /// <summary>自分の意識が薄れた度合い（<see cref="FighterSense.SelfFaintness"/>）。</summary>
    private float _faintness;

    public Vector3 Guard(Hand hand, float reach, Vector3 headLocal)
    {
        var lead = hand == Style.Lead;
        var chin = headLocal.Y - (0.15f * reach);
        var low = -0.12f * reach;
        // 意識が薄れると、ぼんやりして構えを保てずガードが下がる。
        var y = low + ((chin - low) * Style.GuardHeight * (lead ? 0.85f : 1f)) - (FaintGuardDrop * _faintness * reach);
        return new Vector3(hand.LocalSide() * (lead ? 0.17f : 0.13f) * Style.GuardWidth * reach, y, (lead ? 0.52f : 0.36f) * Style.GuardReach * reach);
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
            // 届かない拳は避けない（避ける動きで間合いを崩して、かえって打たれる）。ただし決めずにおき、毎回見直す:
            // 振りかぶりの時点では届かなくても、相手が踏み込めば届く（レビュー指摘: 見送った拳を二度と見ていなかった）。
            if (!track.Decided && track.Time >= Style.ReactionTime && _defense == Defense.None && !Grappling && !OutOfReach(sense, head))
            {
                track = track with { Decided = true };
                if (_random.NextSingle() < Style.DefenseRate)
                {
                    var kind = Classify(sense.Self.InverseRotate(glove.Velocity), toBody);
                    var gloveLocal = sense.Self.WorldToLocal(glove.Position);
                    var defense = _forcedDefense ?? Choose(kind);
                    StartDefense(defense, kind, SideFor(defense, kind, hand, gloveLocal.X >= 0f ? -1f : 1f));
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

    /// <summary>
    /// 届かない拳か: 相手の肩（胸の少し上）から自分の頭までが、腕の長さ × <see cref="BotStyle.EvadeReach"/> より遠い。
    /// 0 なら見ない（来る拳は全部避ける）。回避編 E2: 拳の届かない間合いで避けると、避けない受け手の 2〜3 倍打たれた。
    /// </summary>
    private bool OutOfReach(in FighterSense sense, Vector3 head)
    {
        if (Style.EvadeReach <= 0f)
        {
            return false;
        }

        var shoulder = sense.OpponentChest + new Vector3(0f, 0.25f * sense.ArmReach, 0f);
        return Vector3.Distance(shoulder, head) > Style.EvadeReach * sense.ArmReach;
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

    /// <summary>技の拳の種類（打つ側が知っている種類。経路の形から: 胴打ち・横から（フック）・下から（アッパー）・まっすぐ）。</summary>
    internal static PunchKind KindOf(Technique technique)
    {
        var spec = PunchSpec.Of(technique);
        return spec.ToBody ? PunchKind.Body : spec.Lateral >= 0.5f ? PunchKind.Hook : spec.Low >= 0.5f ? PunchKind.Uppercut : PunchKind.Straight;
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
            total += ChoiceWeight(d, kind);
        }

        if (total <= 0f)
        {
            return Defense.None;
        }

        var pick = _random.NextSingle() * total;
        var last = Defense.None;
        foreach (var d in DefenseBook.Techniques)
        {
            var w = ChoiceWeight(d, kind);
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

    /// <summary>守りを選ぶ重み。しゃがみ疲れが溜まっていたら、頭を沈める守りは選びにくい。</summary>
    private float ChoiceWeight(Defense defense, PunchKind kind)
    {
        var move = Style.Defenses.Of(defense);
        var w = Style.Weight(defense) * move.Against(kind);
        return _crouchTired && move.HeadY < -CrouchDepth ? w * TiredCrouchWeight : w;
    }

    /// <summary>
    /// 守りの側（頭・足を動かす向き、体フレームの X の符号）。ふつうは拳から離れる向き（<paramref name="awaySide"/>）。
    /// 外側へ外す技（<see cref="DefenseMove.Outside"/>）は、ストレートには打ってくる腕の肩の側へ（相手の右手なら自分の左）。
    /// </summary>
    private float SideFor(Defense defense, PunchKind kind, Hand opponentHand, float awaySide)
        => defense != Defense.None && kind == PunchKind.Straight && Style.Defenses.Of(defense).Outside ? -opponentHand.LocalSide() : awaySide;

    /// <summary>
    /// 練習の合図: 相手が <paramref name="opponentHand"/> で <paramref name="kind"/> を打ち始めたと知らせ、すぐ守りを始める
    /// （決まった拳を打ってもらう練習で、来る拳とその時を知っている形。反応時間を除いて、回避の動きそのものを磨くため）。
    /// </summary>
    /// <returns>守りを始めたか（守りの最中・組み付き中は始めない）。</returns>
    internal bool Cue(Hand opponentHand, PunchKind kind)
    {
        if (_defense != Defense.None || Grappling)
        {
            return false;
        }

        var defense = _forcedDefense ?? Choose(kind);
        StartDefense(defense, kind, SideFor(defense, kind, opponentHand, opponentHand.LocalSide()));
        return true;
    }

    private void StartDefense(Defense defense, PunchKind kind, float side)
    {
        if (defense == Defense.None)
        {
            return;
        }

        _defense = defense;
        _predicted = false;
        _predictedPunchCame = false;
        _defenseKind = kind;
        _defenseSide = side;
        _defenseTime = 0f;
        _defenseProgress = 0f;
        _defenseHit = false;
        _defenseMove = Style.Defenses.Of(defense);
        _defenseDuration = _defenseMove.Duration;
        // 打ち返すかは守りを始めたときに決める（回避しながら打つ技は、守り終えるのを待たない）。
        _counterLaunched = false;
        _plannedCounter = null;
        if (_random.NextSingle() < Style.CounterRate)
        {
            var plan = _defenseMove.CounterFor(kind);
            _plannedCounter = plan is { } p ? Combo.Named(p.Combo) : CounterFor(defense);
            // ブロックは両手で覆う守りなので、打ち返すのは守り終えてから（途中で打つと片手の覆いが外れる）。
            _plannedCounterAt = defense == Defense.Block ? 1f : plan?.At ?? 1f;
        }

        _combo = null; // 守るときは連打を打ち切る（出ている拳はそのまま戻す）
        _wantAttack = false;
        foreach (var hand in Hands)
        {
            // 溜めの途中の拳（まだ構えにある）は取りやめる。ブロックは両手で覆うので、出ている拳も引く（レビュー指摘: 回避の最中に溜めの拳が出ていた）。
            var action = _hands[(int)hand];
            if (action.Time < 0f || defense == Defense.Block)
            {
                _hands[(int)hand] = action with { Time = MathF.Max(action.Time, action.Duration) };
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
        _predictedPunchCame |= stillComing;
        var deep = _defenseMove.HeadY < -CrouchDepth;
        var holding = stillComing && _defenseProgress >= 0.5f && _defenseTime < (deep ? MaxCrouchedDefense : MaxDefenseDuration)
            && !(_crouchTired && _defenseMove.HeadY < -CrouchDepth); // 疲れたら沈んだまま守り続けない
        if (!holding)
        {
            _defenseProgress += dt / MathF.Max(1e-3f, _defenseDuration);
        }

        // 回避しながら打つカウンター: 決めた進み具合まで打たれていなければ、守りの姿勢のまま打ち始める。
        if (_plannedCounter is { } during && !_counterLaunched && _plannedCounterAt < 1f && _defenseProgress >= _plannedCounterAt
            && !_defenseHit && !Grappling && distance < Style.EngageRange * reach)
        {
            _counterLaunched = true;
            StartCombo(during, counter: true);
        }

        if (_defenseProgress < 1f && _defenseTime < MaxDefenseDuration + _defenseDuration)
        {
            return;
        }

        var defended = _defense;
        EndDefenseStats();
        _defense = Defense.None;
        if (_plannedCounter is { } after && !_counterLaunched && !_defenseHit && !Grappling && distance < Style.EngageRange * reach)
        {
            StartCombo(after, counter: true);
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
        Defense.SlipIn => new Combo("inside-counter", [Technique.Cross, Technique.LeadHook]),
        Defense.Roll => new Combo("roll-counter", [Technique.LeadHook, Technique.Cross]),
        _ => new Combo("block-counter", [Technique.Jab, Technique.Cross]),
    };

    /// <summary>
    /// 守りの間の拳: 顔を覆う位置（頭の中心から）へ <see cref="DefenseMove.HandsUp"/> だけ寄せる。
    /// 胴への拳には肘を締めて脇腹を覆い、フックには来る側の拳でこめかみを覆う。
    /// </summary>
    private void DefenseGloves(float reach, Vector3 headLocal, float originY, ref Vector3 left, ref Vector3 right)
    {
        var m = _defenseMove;
        if (m.HandsUp <= 0f)
        {
            return;
        }

        // 拳の基準: 頭と一緒に下げるか、構えの高さに残す（残せば、沈んだ頭は自分のブロックの下に入る）。
        // 残すのは高さだけで、横と前後は頭（肩）と一緒に動かす。構えの位置ごと残すと、拳が「頭のあった所」＝相手の狙った所に
        // 残って、外したはずのストレートを毎回グローブで受けていた（回避の基本動作 B3: きれいに外した 0 %）。
        var stanceY = _stanceHeadY is { } y ? y - originY : headLocal.Y;
        var anchor = headLocal with { Y = headLocal.Y + ((stanceY - headLocal.Y) * m.GloveAnchor) };
        foreach (var hand in Hands)
        {
            var side = hand.LocalSide();
            var cover = _defenseKind == PunchKind.Body
                ? headLocal + (new Vector3(side * (m.GloveWidth + 0.07f), m.GloveHeight - m.BodyDrop, m.GloveForward * 0.8f) * reach)
                : _defenseKind == PunchKind.Hook && side == -_defenseSide
                    ? anchor + (new Vector3(side * m.TempleWidth, m.GloveHeight + 0.05f, m.GloveForward * 0.55f) * reach)
                    : anchor + (new Vector3(side * m.GloveWidth, m.GloveHeight, m.GloveForward) * reach);
            if (_hands[(int)hand].Active)
            {
                continue; // 回避しながら打っている手は覆いに使わない
            }

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
        // 戦略の狙いどころ: 頭・胴を問わず重みで選び、拳の高さが合わなければ同じ手・同じ形の拳で打ち分ける。
        var goal = Style.Aim is { } aimWeights && technique != Technique.Feint
            ? Targeting.PickAny(aimWeights, _targets, _random.NextSingle())
            : AimGoal.Center;
        if ((Targeting.IsBodyGoal(goal) && !spec.ToBody) || (Targeting.IsHeadGoal(goal) && spec.ToBody))
        {
            var retargeted = Targeting.Retarget(technique, Targeting.IsBodyGoal(goal));
            if (PunchSpec.Of(retargeted).ToBody == Targeting.IsBodyGoal(goal))
            {
                technique = retargeted;
                spec = PunchSpec.Of(retargeted);
            }
            else
            {
                goal = AimGoal.Center; // 打ち分けられない拳（アッパー等）は真ん中へ
            }
        }

        if (goal != AimGoal.Center)
        {
            aimOffset = Vector3.Zero; // 狙いどころを打つときは、構えの隙へのずらしは使わない
            Stats.Aimed(goal);
        }

        var duration = spec.Duration / Style.PunchSpeed;
        // 連打の最初の拳だけ、体の溜め（Time が負の間）から始める。2 発目以降は前の拳の流れで出る。
        // 溜めの威力は物理に任せる（溜めの間に回した胴と踏み込みの勢いが拳に乗る）。打ち抜きを深くすると、外したはずの頭まで届いた。
        // カウンターとストップジャブは溜めない（回避の流れ・相手の踏み込みに合わせて、すぐ出す）。
        var load = _comboIndex == 0 && !_comboIsCounter && combo != StopJabCombo ? Style.Telegraph : 0f;
        _hands[(int)puncher] = new HandAction(technique, spec, -load, duration, _comboIsCounter, aimOffset, Load: load, Goal: goal);
        Stats.Thrown(technique, _comboIsCounter);
        _comboIndex++;
        _comboClock = load + (duration * combo.Gap); // 次の拳は、この拳が出てから数える（溜めを含めないと 1-2 がほぼ同時に出た。レビュー指摘）
        if (_comboIndex >= combo.Steps.Count)
        {
            _combo = null;
            _cooldown = ((1.3f + ((0.3f - 1.3f) * Style.Aggression)) * Range(0.7f, 1.3f)) + duration + load;
            if (_random.NextSingle() < Style.ExitAfterCombo)
            {
                _exitTimer = load + duration + 0.5f;
            }

            if (_random.NextSingle() < Style.PivotAfterCombo)
            {
                // 前の手の側へ回り込む（相手の後ろの手から離れ、相手の正面から外れる）。
                _pivotTimer = load + duration + 0.45f;
                _pivotSide = Style.Lead.LocalSide();
                Stats.Pivoted();
            }
        }
    }

    private bool AnyPunchActive => _hands[0].Active || _hands[1].Active;

    /// <summary>相手の体の狙いどころ（最後に読んだ知覚）。</summary>
    private TargetMap? _targets;

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

    private Vector3 Footwork(BodyFrame self, Vector3 dir, float distance, float reach, RingLayout ring, float dt)
    {
        _circleTimer -= dt;
        if (_circleTimer <= 0f)
        {
            _circleTimer = Range(0.8f, 2.5f);
            // 基本は前の手の側へ回る（相手の強い後ろの手から離れる）。
            _circleDirection = (_random.NextSingle() < 0.7f ? 1f : -1f) * Style.Lead.LocalSide();
        }

        // 相手の届く間合いのすぐ外に立つ（覚えた相手の打ち始めの間合い＋余裕へ、普段の間合いを寄せる）。
        var neutral = Style.EdgeRange > 0f && _read.AttackDistance is { } edge
            ? Style.NeutralRange + ((MathF.Max(Style.AttackRange, edge + EdgeMargin) - Style.NeutralRange) * Style.EdgeRange)
            : Style.NeutralRange;
        var desired = _wantAttack || _combo is not null ? Style.AttackRange
            : _exitTimer > 0f ? neutral + 0.3f
            : neutral;
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

        // ロープを背負わない: 背中側のロープが近いほど、下がる動きを空いている横への動きに替える（回避の後退でも、間合いを
        // 取り直す後退でも）。下がり続けてロープに詰まると回避が効かず打たれ続けた（回避編 E1: ロープ際にいた時間が
        // 守らない受け手の 0% に対し、バックステップで回避する受け手は 63%、被ダメージは 3.6 倍）。
        // 逃げる側は、ロープを感じた時に決めて、離れるまで変えない（毎回「広い側」を選び直すと、ロープの真ん中で左右が
        // 入れ替わり続けて離れられない）。守りの最中は、来る拳から離れる側へ逃げる（拳の方へ寄らない）。レビュー指摘。
        var (behind, roomySide) = RopeBehind(self.Origin, dir, lateral, reach, ring);
        if (behind <= 0f)
        {
            _ropeEscapeSide = 0f;
        }
        else if (_ropeEscapeSide == 0f)
        {
            _ropeEscapeSide = roomySide;
        }

        if (approach < 0f && Style.RopeEscape > 0f && behind > 0f)
        {
            var escapeSide = _defense != Defense.None ? _defenseSide : _ropeEscapeSide;
            var converted = -approach * behind * Style.RopeEscape;
            approach += converted;
            side += escapeSide * converted;
        }

        var circling = _combo is null && !_wantAttack && _pivotTimer <= 0f ? Style.Circling * _circleDirection : 0f;
        var move = (dir * Math.Clamp(approach, -1f, 1f)) + (lateral * Math.Clamp(circling + side, -1f, 1f));
        return move + StayInside(self.Origin, reach, ring);
    }

    /// <summary>
    /// 背中側のロープの近さ（0 = 遠い … 1 = すぐ後ろ。腕の長さの 2 倍より近づくと効き始める）と、横のどちらが空いているか
    /// （<paramref name="lateral"/> の向きを +1）。
    /// </summary>
    internal static (float Behind, float EscapeSide) RopeBehind(Vector3 position, Vector3 dir, Vector3 lateral, float reach, RingLayout? ring = null)
    {
        ring ??= RingLayout.Boxing;
        var half = ring.Half;
        var p = position - ring.Center;
        static float Room(Vector3 p, Vector3 toward, float half)
        {
            var room = float.MaxValue;
            if (MathF.Abs(toward.X) > 1e-4f)
            {
                room = MathF.Min(room, ((MathF.Sign(toward.X) * half) - p.X) / toward.X);
            }

            if (MathF.Abs(toward.Z) > 1e-4f)
            {
                room = MathF.Min(room, ((MathF.Sign(toward.Z) * half) - p.Z) / toward.Z);
            }

            return MathF.Max(0f, room);
        }

        var behind = Math.Clamp(1f - (Room(p, -dir, half) / (2f * reach)), 0f, 1f);
        var escape = Room(p, lateral, half) >= Room(p, -lateral, half) ? 1f : -1f;
        return (behind, escape);
    }

    /// <summary>ロープ際に近づいたら中央へ戻る向きを足す（追い詰められると打たれ続け、落ちれば負け）。</summary>
    internal static Vector3 StayInside(Vector3 position, float reach, RingLayout ring)
    {
        var half = ring.Half;
        var margin = MathF.Max(0.12f, 0.8f * reach);
        var offset = position - ring.Center;
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
                // 溜め: 打つ向きと逆へ頭を引き、膝を沈める（体のバネを縮める。打ち出すとその反発で前へ出る）。
                shift += new Vector3(-0.5f * s.X * lead, -LoadDip, -0.5f * s.Z) * (reach * Wound(action));
            }
        }

        _headIdleTimer -= dt;
        if (_headIdleTimer <= 0f)
        {
            _headIdleTimer = Range(0.35f, 1.1f);
            var a = Style.HeadMovement;
            _headIdle = _random.NextSingle() < 1f - (0.7f * a)
                ? Vector3.Zero
                : new Vector3(Range(-0.32f, 0.32f), Range(-0.15f, 0.02f), Range(-0.08f, 0.2f)) * (reach * a); // 上下は浅く（構えたまま沈み込まない）
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
            EndDefenseStats();
        }

        _defense = Defense.None;
        _clinchTime = -1f;
        _throwTime = -1f;
        foreach (var hand in Hands)
        {
            _hands[(int)hand] = _hands[(int)hand] with { Time = _hands[(int)hand].Duration + LandWindow };
        }
    }

    /// <summary>
    /// 拳に合わせた体の動き（頭の動き・踏み込み）の大きさ。溜めの間（Time が負）は 0（逆へ引く: <see cref="Wound"/>）、
    /// 拳が出てからは拳の振りに合わせて山を描く。
    /// </summary>
    private static float Envelope(in HandAction action)
    {
        if (action.Time < 0f)
        {
            return 0f; // 溜めの間は逆へ引く（<see cref="Wound"/>）
        }

        var u = Math.Clamp(action.Time / action.Duration, 0f, 1f);
        return action.Loaded ? MathF.Max(MathF.Sin(MathF.PI * u), u < 0.5f ? 0.5f : 0f) : MathF.Sin(MathF.PI * u);
    }

    /// <summary>溜めでどれだけ引いたか（0..1）: 溜めを始めてからの時間 ÷ <see cref="FullLoad"/>。溜めの間だけ。</summary>
    private static float Wound(in HandAction action)
        => action.Loaded && action.Time < 0f ? Math.Clamp((action.Time + action.Load) / FullLoad, 0f, 1f) : 0f;

    /// <summary>
    /// 溜めの胴のひねり [rad]（正で右肩が前）: 溜めの間に打つ手の肩を後ろへ引いて胴のバネを縮め（拳より先に動く、読める手がかり）、
    /// 打ち出したら前へ回してバネの反発を拳に乗せる（<see cref="Spring"/>）。その後は腕の動きからのひねり（<see cref="PunchMechanics.TorsoTwist"/>）へ引き継ぐ。
    /// </summary>
    private float LoadTwist()
    {
        var twist = 0f;
        foreach (var hand in Hands)
        {
            var action = _hands[(int)hand];
            if (!action.Active || !action.Loaded)
            {
                continue;
            }

            // 溜めの間は打つ手の肩を後ろへ引き（胴のバネを縮める）、打ち出したら引いた大きさに見合うだけ前へ回す。
            var share = action.Time < 0f ? -Wound(action) : MathF.Min(1f, action.Load / FullLoad) * MathF.Max(0f, 1f - (2f * action.Time / action.Duration));
            var size = hand == Style.Lead ? LoadTwistLead : LoadTwistRear;
            twist += (hand == Hand.Right ? 1f : -1f) * size * share;
        }

        return twist;
    }

    /// <summary>
    /// 予測の回避: 相手の読み（<see cref="OpponentRead"/>）が「まもなく来る」と見積もったら、拳が出る前に守りを始める。
    /// 確からしさの閾値は <see cref="BotStyle.Prediction"/> が大きいほど低い（早く動くが、釣られやすい）。
    /// <para>
    /// 間合い・リズム・体の手がかりを合わせた見積もりで動くが、体の手がかり（ひねり・前傾）が「打たない」側を示している時は動かない。
    /// 実際のボクサーも状況から来る拳を見込み、体の動き出しと合わせて避ける（Martínez de Quel ほか 2019）。
    /// 体の手がかりを引き金に必須にした版は、手がかりが物理の遅れで溜めの終わりにしか出ず、動き出しが拳の 0.05 秒前まで遅れて外し切れなかった
    /// （予測編 P3: 溜め 0.12 秒で頭 98 %、必須にしなければ 15 %）。
    /// </para>
    /// </summary>
    private void UpdatePrediction(in FighterSense sense, float dt)
    {
        _predictionCooldown = MathF.Max(0f, _predictionCooldown - dt);
        if (Style.Prediction <= 0f || _predictionCooldown > 0f || _defense != Defense.None || Grappling || AnyPunchActive)
        {
            return;
        }

        var head = sense.SelfHead == default ? sense.Self.Origin + new Vector3(0f, 0.3f * sense.ArmReach, 0f) : sense.SelfHead;
        var forecast = _read.Predict();
        if (forecast.Likelihood < PredictionThreshold || forecast.BodyEvidence < Style.BodyCue || OutOfReach(sense, head))
        {
            return;
        }

        _predictionCooldown = PredictionCooldown;
        var defense = _forcedDefense ?? Choose(forecast.Kind);
        StartDefense(defense, forecast.Kind, SideFor(defense, forecast.Kind, forecast.Hand, forecast.Hand.LocalSide()));
        _predicted = _defense != Defense.None;
    }

    /// <summary>予測で動き出す確からしさ（<see cref="BotStyle.Prediction"/> = 1 で 0.35、小さいほど確かな時だけ）。</summary>
    private float PredictionThreshold => 0.95f - (0.6f * Style.Prediction);

    private void EndDefenseStats()
    {
        Stats.DefenseEnded(_defense, _defenseHit);
        if (_predicted)
        {
            Stats.Predicted(_defenseHit, _predictedPunchCame);
            if (!_predictedPunchCame)
            {
                // 読みが早すぎて拳が来ないまま守りが終わったら、すぐ読み直せるようにする（待たされている間に本当の拳が来ていた。足運び編 F6）。
                _predictionCooldown = 0f;
            }
        }

        _predicted = false;
    }

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
    /// <param name="Technique">技（命中を数えたら null）。</param>
    /// <param name="Spec">経路と体の使い方。</param>
    /// <param name="Time">打ち始めからの時間 [s]。</param>
    /// <param name="Duration">打ち出して構えに戻るまでの時間 [s]。</param>
    /// <param name="Counter">カウンターとして打った拳か。</param>
    /// <param name="AimOffset">狙いのずらし（構えを読んで隙を狙うとき、体フレーム）。</param>
    /// <param name="AimWorld">狙いの点（ワールド）。溜めのある拳は溜めの始めに決め、溜めの無い拳は打ち始めの <see cref="AimTrackShare"/> の間だけ相手を追う。</param>
    /// <param name="Load">打つ前の溜め [s]（<see cref="Time"/> は −Load から始まる）。</param>
    /// <param name="Goal">狙いどころ（戦略が選ぶ。<see cref="Targeting"/>）。</param>
    private readonly record struct HandAction(Technique? Technique, PunchSpec Spec, float Time, float Duration, bool Counter, Vector3 AimOffset = default, Vector3 AimWorld = default, float Load = 0f,
        AimGoal Goal = AimGoal.Center)
    {
        public bool Active => Duration > 0f && Time < Duration;

        public bool Loaded => Load > 0f;
    }

    private readonly record struct ThreatTrack(float Time, bool Decided, float Gap = 0f);
}
