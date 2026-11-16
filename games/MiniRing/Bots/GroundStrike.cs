using System.Numerics;
using MiniRing.Motion;

namespace MiniRing.Bots;

/// <summary>倒れた相手を攻める手足。</summary>
public enum GroundLimb { Hand, Foot }

/// <summary>
/// 倒れた相手への攻めに使う道具（手に持った物の寸法）。<paramref name="Length"/> は握りから先の端まで（腕の長さ = 1）。素手・グローブ・
/// 手にはめた物は 0（拳そのものが先）。<paramref name="TwoHanded"/> なら左手は握りの下（<paramref name="Spacing"/>、腕の長さ = 1）。
/// </summary>
public readonly record struct GroundTool(float Length, bool TwoHanded = false, float Spacing = 0f)
{
    /// <summary>素手・グローブ・手にはめた物（拳が先）。</summary>
    public static GroundTool Fist => new(0f);

    public bool Armed => Length > 0f;
}

/// <summary>
/// 倒れた相手への攻め 1 つの経路の寸法（長さは腕の長さ = 1）。ほかの技（<see cref="PunchSpec"/>・<see cref="KickSpec"/>）と同じく、
/// 威力の倍率を持たない目標の経路: 構え → 振りかぶり（<see cref="Rise"/>・<see cref="Pull"/>）→ 相手の頭・胴の少し奥（<see cref="Overshoot"/>）→ 構え。
/// 威力は、打ち下ろす先（拳・武器・足）の速さと、後ろに乗る体の質量、体のバネ（<see cref="Dip"/> で沈む・<see cref="Spring"/>）から物理で出る。
/// </summary>
/// <param name="Name">名前（観戦・記録用）。</param>
/// <param name="Limb">使う手足。</param>
/// <param name="Duration">振りかぶって打って戻るまでの時間 [s]。</param>
/// <param name="Stand">倒れた相手の頭・胴の脇に立つ水平の距離。</param>
/// <param name="Reach">打つ先に手（足）が届く、肩（腰）から相手までの距離の上限。これより遠いと体を沈めて近づける。</param>
/// <param name="Rise">振りかぶりの手を、打つ手の位置より上へ上げる量。</param>
/// <param name="Pull">振りかぶりの手を、打つ手の位置より体の側へ引く量。</param>
/// <param name="WindElevation">振りかぶりの武器の向きの仰角 [rad]（π/2 を越えると後ろ上へ寝かせる）。</param>
/// <param name="StrikeElevation">打つときの武器の向きの仰角 [rad]（負が下）。</param>
/// <param name="Overshoot">相手の点より下へ突き抜ける量。</param>
/// <param name="Dip">振りかぶる間に体を持ち上げる量（沈みに対する割合。打つ間にそこから落とす: 体重を落として乗せる）。</param>
/// <param name="Lean">打つ間、頭を前（相手の側）へ出す量。</param>
/// <param name="Spring">打ち下ろす間に体の動き・バネを手へ移す割合（<see cref="HandCommand.Spring"/>）。</param>
public sealed record GroundMove(string Name, GroundLimb Limb, float Duration, float Stand, float Reach, float Rise, float Pull,
    float WindElevation, float StrikeElevation, float Overshoot, float Dip, float Lean, float Spring)
{
    /// <summary>この道具で出せる攻めの 2 系統: 振り下ろす系（<see cref="GroundTuning.Hammer"/> / <see cref="GroundTuning.Cut"/>）と突き下ろす系（<see cref="GroundTuning.DropThrust"/> / <see cref="GroundTuning.Thrust"/>）。棍棒は振り下ろす系だけ。</summary>
    public static (GroundMove Swing, GroundMove? Thrust) For(GroundTool tool, GroundTuning tuning, bool club = false)
        => !tool.Armed ? (tuning.Hammer, tuning.DropThrust) : club ? (tuning.Cut, null) : (tuning.Cut, tuning.Thrust);
}

/// <summary>
/// 倒れた相手への攻め（<see cref="GroundStrike"/>）の、体と経路の寸法（長さは腕の長さ = 1）。手で決めた値で、物理から導いた値ではない（CLAUDE.md §8）。
/// スタイルごとの好み（攻める気・溜め・振り下ろす系の割合・踏みつけ・合間）は <see cref="BotStyle"/> の <c>Ground*</c>。
/// </summary>
public sealed record GroundTuning
{
    public static GroundTuning Default { get; } = new();

    /// <summary>1 回の攻めの時間のうち、振りかぶりが占める割合（0..この値で構えから振りかぶりへ）。</summary>
    public float WindShare { get; init; } = 0.40f;

    /// <summary>振りかぶりの後、打つ（落とす）区間の終わり（この値まで。以降は構えへ戻す）。</summary>
    public float StrikeShare { get; init; } = 0.62f;

    /// <summary>沈みを動かす速さ [腕の長さ / s]（目標が届く所から外れた量に比例。手で決めた値）。</summary>
    public float SinkRate { get; init; } = 4f;

    /// <summary>沈みの上限 [腕の長さ]。これ以上は上体が倒れて腕が使えない（試して決めた）。</summary>
    public float MaxSink { get; init; } = 1.5f;

    /// <summary>1 歩あたりの沈みの更新に使う、届く所からの外れ [腕の長さ] の下限（戻す側）と上限（沈める側）。急に動かさないため。</summary>
    public float SinkStepDown { get; init; } = 0.5f;

    public float SinkStepUp { get; init; } = 0.6f;

    /// <summary>届く所から外れた量がこれ以下になったら、打ち始めてよい [腕の長さ]。</summary>
    public float ReadyGap { get; init; } = 0.15f;

    /// <summary>立つ所からの水平のずれがこれ以下なら、着いたとみなす [腕の長さ]。</summary>
    public float ReadyDistance { get; init; } = 0.50f;

    /// <summary>立つ所までのずれがこれ以下なら歩くのをやめる [腕の長さ]。</summary>
    public float ArriveTolerance { get; init; } = 0.08f;

    /// <summary>立つ所までの距離がこれより遠い間は全速で歩く [腕の長さ]（近づくほど遅く）。</summary>
    public float SlowDistance { get; init; } = 0.6f;

    /// <summary>立つ所へ近づくにつれて沈み始める幅 [腕の長さ]（<see cref="ReadyDistance"/> からこの幅で 0 → 1。遠い間は立ったまま歩く。深くしゃがんだままでは歩けない）。</summary>
    public float CrouchSpan { get; init; } = 0.35f;

    /// <summary>沈むとき頭を相手の側へ出す量（沈みの大きさ 1 のとき、腕の長さ倍）。上体を前へ傾けて、肩を倒れた相手の上へ運ぶ。</summary>
    public float ApproachLean { get; init; } = 0.2f;

    /// <summary>着いたのに沈みきれないとき、打つのを待つ上限 [s]。</summary>
    public float MaxWait { get; init; } = 0.8f;

    /// <summary>立つ所へ着いても届かないとき、届くまで待つ上限 [s]（過ぎたら、届く所まで寄って打ちに行く）。</summary>
    public float GiveUpSeconds { get; init; } = 1.0f;

    /// <summary>あきらめて寄って打ちに行く距離の上限 [腕の長さ]。</summary>
    public float GiveUpRange { get; init; } = 1.2f;

    /// <summary>打たないと決めたあと、次を決めるまでの間 [s]。</summary>
    public float RetryDelay { get; init; } = 0.25f;

    /// <summary>手の目標を、肩から腕の長さのこの倍までに丸める（腕は伸びきっても越えられない）。</summary>
    public float ReachLimit { get; init; } = 0.98f;

    /// <summary>構えの武器の向き（方位角・仰角 [rad]）。剣の中段の構え（<see cref="MiniRing.Weapons.WeaponSwing.SwordGuard"/>）に合わせた値。</summary>
    public float GuardBladeAzimuth { get; init; } = 0.1f;

    public float GuardBladeElevation { get; init; } = 0.4f;

    /// <summary>踏み下ろしの、足を置く構えの位置（肩から横へ・下へ。歩く足の構えと同じ値）。</summary>
    public float FootRestSide { get; init; } = 0.2f;

    public float FootRestDrop { get; init; } = 1.4f;

    /// <summary>踏み下ろしの膝の向き（足を上げた位置から上へ・前へ）。蹴りの膝の向き（<see cref="KickSpec"/>）と同じ値。</summary>
    public float KneeUp { get; init; } = 0.2f;

    public float KneeForward { get; init; } = 0.15f;

    /// <summary>素手・グローブ・手にはめた物: 鉄槌（拳を高く上げて縦に打ち下ろす）。</summary>
    public GroundMove Hammer { get; init; } = new("hammer", GroundLimb.Hand, 0.50f, 0.30f, 1.00f, 0.65f, 0.15f, 0f, 0f, 0.15f, 0.30f, 0.10f, 1f);

    /// <summary>素手・グローブ・手にはめた物: 腰を落として体ごと突き下ろす（沈みで体のバネを縮め、落ちる重さを乗せる）。</summary>
    public GroundMove DropThrust { get; init; } = new("drop-thrust", GroundLimb.Hand, 0.55f, 0.30f, 1.00f, 0.35f, 0.40f, 0f, 0f, 0.15f, 0.50f, 0.25f, 1f);

    /// <summary>踏み下ろし（踏みつけ）: 足を相手の上へ上げ、真下へ踏み下ろす。</summary>
    public GroundMove Stomp { get; init; } = new("stomp", GroundLimb.Foot, 0.70f, 0.45f, 1.45f, 0.55f, 0.10f, 0f, 0f, 0.20f, 0f, 0.05f, 1f);

    /// <summary>片手・両手で握った武器: 上段から振りかぶって斬り下ろす（刀・ロングソード・棍棒。握りの先が頭の後ろへ寝るまで振りかぶる）。</summary>
    public GroundMove Cut { get; init; } = new("cut", GroundLimb.Hand, 0.65f, 0.80f, 0.95f, 0.70f, 0.20f, 2.3f, -0.75f, 0.10f, 0.30f, 0.10f, 1f);

    /// <summary>刀・ロングソード: 切っ先を相手へ向けて高く構え、真上から突き下ろす（とどめ）。</summary>
    public GroundMove Thrust { get; init; } = new("thrust-down", GroundLimb.Hand, 0.60f, 0.70f, 0.95f, 0.40f, 0.45f, -0.55f, -1.15f, 0.12f, 0.30f, 0.15f, 1f);
}

/// <summary>入力元から倒れた相手への攻めへの呼びかけ。</summary>
public enum GroundCall
{
    /// <summary>ボットが決める（立つ位置へ歩き、沈み、間合いに入ったら好みに応じて打つ）。</summary>
    Auto,

    /// <summary>打たない（プレイヤーの移動中）。</summary>
    Hold,

    /// <summary>振り下ろす系を今打つ（弱）。</summary>
    Swing,

    /// <summary>突き下ろす系を今打つ（強）。</summary>
    Thrust,

    /// <summary>踏み下ろしを今打つ（空手の強。素手のとき）。</summary>
    Stomp,
}

/// <summary>
/// 倒れた相手（<see cref="FighterSense.OpponentDowned"/>）を攻める技の実行（レフェリーなしの試合。<see cref="FighterSense.MayStrikeDowned"/>）。
/// 入力元の指令（<c>basis</c>）の上に重ねる: 相手の頭か胴の脇（<see cref="GroundMove.Stand"/>）へ歩き、目標が肩の届く所へ来るまで頭を沈めて前傾し
/// （<see cref="FighterCommand.HeadOffset"/>。腰を落とす・腰から前へ折るのは体の力学）、手（足）の目標を上から下へ打ち下ろす経路で動かす。
/// 姿勢は書き込まない。倍率・急所の分岐も無い。種類ごとの違いは <see cref="GroundMove"/> の寸法と道具の長さだけ。
/// </summary>
public sealed class GroundStrike
{
    private readonly GroundTuning _t;
    private readonly BotStyle _style;
    private readonly GroundTool _tool;
    private readonly bool _club;
    private readonly Random _random;
    private float _clock;
    private float _nextAt;
    private float _sink;
    private float _waited;
    private GroundMove? _plan;
    private GroundMove? _move;
    private float _time;
    private Hand _hand;
    private Hand _nextHand;
    private bool _stomped;

    public GroundStrike(BotStyle style, GroundTool tool, int seed, bool club = false, GroundTuning? tuning = null)
    {
        _t = tuning ?? GroundTuning.Default;
        _style = style ?? throw new ArgumentNullException(nameof(style));
        _tool = tool;
        _club = club;
        _random = new Random(seed);
        _nextHand = style.Lead == Hand.Left ? Hand.Right : Hand.Left;
    }

    /// <summary>この入力元が倒れた相手を攻める場面か（相手は倒れていて、レフェリーはいない）。</summary>
    public static bool Applies(in FighterSense sense) => !sense.OpponentAlive && sense.OpponentDowned && sense.MayStrikeDowned;

    /// <summary>今打ち下ろしている最中か。</summary>
    public bool Striking => _move is not null;

    /// <summary>打ち始めた回数（観戦・テスト用）。</summary>
    public int Started { get; private set; }

    /// <summary>立つ所までの水平の距離（腕の長さ = 1）・打つ先が届く距離からの外れ・沈み（観戦・テスト用）。</summary>
    public float Distance { get; private set; }

    public float Gap { get; private set; }

    /// <summary>今（直近）打っている手（足の技のときは使わない）。</summary>
    public Hand CurrentHand => _hand;

    public float Sink { get; private set; }

    /// <summary>今の（直近の）攻めの名前。</summary>
    public string? CurrentName => (_move ?? _plan)?.Name;

    /// <summary>倒れた相手への攻めをやめる（相手が立ち直った。次に始めるときは構えから）。</summary>
    public void Cancel()
    {
        _move = null;
        _plan = null;
        _sink = 0f;
        _waited = 0f;
    }

    /// <summary>
    /// <paramref name="basis"/>（入力元がいつもの技を考えて出した指令）に、倒れた相手への攻めを重ねる。<paramref name="steer"/> が true なら立つ位置へ歩く
    /// （ボット）、false なら移動は <paramref name="basis"/> のまま（プレイヤー）。<paramref name="load"/> は呼びかけで始めるときの溜め [s]。
    /// </summary>
    public FighterCommand Read(in FighterSense sense, in FighterCommand basis, float dt, GroundCall call = GroundCall.Auto, float load = 0f, bool steer = true)
    {
        _clock += dt;
        var reach = sense.ArmReach;
        var self = sense.Self;
        var tool = ToolOf(sense);
        var catalog = GroundMove.For(tool, _t, _club);

        // 動いていなければ、どの攻めにするかを決める。
        if (_move is null)
        {
            _plan = call switch
            {
                GroundCall.Swing => catalog.Swing,
                GroundCall.Thrust => catalog.Thrust ?? catalog.Swing,
                GroundCall.Stomp => tool.Armed ? catalog.Thrust ?? catalog.Swing : _t.Stomp,
                GroundCall.Hold => null,
                _ => _plan ?? Choose(catalog),
            };
        }

        var aimW = Nearest(sense, self.Origin);
        var plan = _move ?? _plan;
        var toAim = Flat(aimW - self.Origin);
        var face = toAim.LengthSquared() > 1e-8f ? Vector3.Normalize(toAim) : self.Forward;

        // 立つ所: 倒れた相手の体の軸の横（今いる側）。軸が決まらなければ今の向きの手前。
        var move = basis.MoveWorld;
        var distance = 0f;
        if (plan is not null)
        {
            var axis = Flat(sense.OpponentHead - sense.OpponentChest);
            var fromAim = -toAim;
            var side = axis.LengthSquared() > 1e-6f ? new Vector3(-axis.Z, 0f, axis.X) : fromAim;
            side = Vector3.Dot(side, fromAim) < 0f ? -side : side;
            side = side.LengthSquared() > 1e-8f ? Vector3.Normalize(side) : -face;
            var stand = Flat(aimW) + (side * (plan.Stand * reach));
            var delta = stand - Flat(self.Origin);
            distance = delta.Length();
            if (steer)
            {
                move = distance > _t.ArriveTolerance * reach ? delta / distance * Math.Clamp(distance / (_t.SlowDistance * reach), 0f, 1f) : Vector3.Zero;
            }
        }

        // 沈み: 立つ所に着いたとして、打つ手が肩から届く高さまで頭を下げる（肩が打つ点より高すぎる分。届いていれば戻す）。
        // 水平の距離は立つ所での値で見る（まだ歩いている間に深く沈みすぎないように）。
        var aimLocal = self.WorldToLocal(aimW);
        var gap = 0f;
        if (plan is not null)
        {
            var (_, bladeStrike, handStrike) = Strike(plan, tool, aimLocal, reach);
            gap = (handStrike.Length() / reach) - plan.Reach;
            var horizontalAtStand = MathF.Abs((plan.Stand * reach) - (bladeStrike.Z * tool.Length * reach));
            var vertical = MathF.Sqrt(MathF.Max(0f, MathF.Pow(plan.Reach * reach, 2f) - (horizontalAtStand * horizontalAtStand)));
            var sinkGap = (-handStrike.Y - vertical) / reach;
            // 立つ所へ近づくほど沈む（遠い間は立ったまま歩く。深くしゃがんだままでは歩けない）。戻すときはすぐ。
            var near = Math.Clamp(1f - (((distance / reach) - _t.ReadyDistance) / _t.CrouchSpan), 0f, 1f);
            _sink = Math.Clamp(_sink + (Math.Clamp(sinkGap, -_t.SinkStepDown, _t.SinkStepUp) * _t.SinkRate * dt * (sinkGap < 0f ? 1f : near)), 0f, _t.MaxSink);
        }
        else
        {
            _sink = MathF.Max(0f, _sink - (_t.SinkRate * dt));
        }

        // 打ち始め: 着いて（水平）、沈みも届いたら、好みに応じて打つ。呼びかけは、押した時に始める（沈みきるまで振りかぶりで待つ）。
        Distance = distance / reach;
        Gap = gap;
        Sink = _sink;
        // 届きにくい（体を沈めきれない・足が倒れた体にさえぎられる）ときは、しばらく試したあと、近くにいれば届く所まで寄って打つ。
        var ready = plan is not null && ((distance <= _t.ReadyDistance * reach || !steer) && gap <= _t.ReadyGap
            || (_waited > _t.GiveUpSeconds && distance <= _t.GiveUpRange * reach));
        _waited = _move is null ? plan is not null && !ready ? _waited + dt : 0f : _waited;
        if (_move is null && plan is not null)
        {
            var called = call is GroundCall.Swing or GroundCall.Thrust or GroundCall.Stomp;
            var go = called || (ready && _clock >= _nextAt && call == GroundCall.Auto && _random.NextDouble() < _style.GroundDrive);
            if (go)
            {
                _move = plan;
                _time = -(called ? load : _style.GroundLoad);
                _hand = _nextHand;
                _nextHand = _hand == Hand.Left ? Hand.Right : Hand.Left;
                Started++;
            }
            else if (call == GroundCall.Auto && ready && _clock >= _nextAt)
            {
                _nextAt = _clock + _t.RetryDelay; // 打たなかった: 少し待ってまた決める
            }
        }

        var result = basis with { MoveWorld = move, FaceDirection = face };
        if (_move is not { } m)
        {
            return result with { HeadOffset = basis.HeadOffset + new Vector3(0f, -_sink * reach, _t.ApproachLean * MathF.Min(_sink, 1f) * reach) };
        }

        // 打ち下ろしの途中: 打つ位置まで届かない間は、打ち始めの手前（振りかぶり）で待つ。
        var hold = _time >= m.Duration * _t.WindShare && gap > _t.ReadyGap && _waited < _t.MaxWait;
        if (hold)
        {
            _waited += dt;
        }
        else
        {
            _time += dt;
        }

        if (_time < m.Duration * _t.WindShare)
        {
            _waited = 0f; // 振りかぶりの間は待ちを数え直す（打つ位置の手前で待つ上限は、打ち始めの手前だけ）
        }

        if (_time >= m.Duration)
        {
            _move = null;
            _plan = null;
            _stomped = m.Limb == GroundLimb.Foot;
            _nextAt = _clock + _style.GroundRest;
            _time = 0f;
            return result with { HeadOffset = basis.HeadOffset + new Vector3(0f, -_sink * reach, _t.ApproachLean * MathF.Min(_sink, 1f) * reach) };
        }

        var t = _time / m.Duration;
        var envelope = t < _t.WindShare ? Smooth(MathF.Max(0f, t) / _t.WindShare) : t < _t.StrikeShare ? 1f : 1f - Smooth((t - _t.StrikeShare) / (1f - _t.StrikeShare));
        // 体の上げ下げ: 振りかぶる間は沈みの一部（Dip）を戻して体を持ち上げ、打つ間にそこから落とす（体重が落ちる分が手に乗る。上げた分は体のバネに貯まる）。
        var lift = t < _t.WindShare ? Smooth(MathF.Max(0f, t) / _t.WindShare) : t < _t.StrikeShare ? 1f - Smooth((t - _t.WindShare) / (_t.StrikeShare - _t.WindShare)) : 0f;
        var lean = (m.Lean * envelope) + (_t.ApproachLean * MathF.Min(_sink, 1f));
        var head = basis.HeadOffset + new Vector3(0f, -_sink * (1f - (m.Dip * lift)) * reach, lean * reach);
        var spring = t >= _t.WindShare && t < _t.StrikeShare ? m.Spring : 0f;
        result = result with { HeadOffset = head };

        if (m.Limb == GroundLimb.Foot)
        {
            var side = aimLocal.X >= 0f ? Hand.Left : Hand.Right;
            var leg = Foot(m, side, t, aimLocal, reach);
            return side == Hand.Left ? result with { LeftLeg = leg } : result with { RightLeg = leg };
        }

        var right = HandPath(m, tool, basis, t, aimLocal, reach);
        if (tool.Armed)
        {
            var hand = tool.TwoHanded ? Hand.Right : BoxingGame.WeaponHand;
            var held = new HandCommand(right.Position, false, spring, right.Orientation);
            if (tool.TwoHanded)
            {
                var left = new HandCommand(right.Position - (right.Blade * (tool.Spacing * reach)), false, spring);
                return hand == Hand.Right ? result with { Left = left, Right = held } : result with { Left = held, Right = left };
            }

            return hand == Hand.Right ? result with { Right = held } : result with { Left = held };
        }

        var fist = new HandCommand(right.Position, false, spring, Shape: basis.For(_hand).Shape);
        return _hand == Hand.Left ? result with { Left = fist } : result with { Right = fist };
    }

    private GroundTool ToolOf(in FighterSense sense)
    {
        // 握った武器が折れて短くなっていれば、その長さで（武器を落としていれば拳で）。
        if (_tool.Armed && sense.SelfArmed == true && sense.SelfWeapon.Held && sense.ArmReach > 0f)
        {
            return _tool with { Length = sense.SelfWeapon.Reach / sense.ArmReach };
        }

        return sense.SelfArmed == true || !_tool.Armed ? _tool : GroundTool.Fist;
    }

    private GroundMove Choose((GroundMove Swing, GroundMove? Thrust) catalog)
    {
        // 踏みつけ（足）を好むスタイルは、直前に踏んでいなければ好みの割合で踏む。
        if (!_tool.Armed && !_stomped && _random.NextDouble() < _style.GroundStomp)
        {
            return _t.Stomp;
        }

        return catalog.Thrust is null || _random.NextDouble() < _style.GroundCut ? catalog.Swing : catalog.Thrust;
    }

    /// <summary>
    /// 狙う点: 倒れた相手の体の軸（腰から頭まで）のうち、水平で自分にいちばん近い点。倒れた相手の頭・胴のどちらでも、遠回りせず近い所を打つ
    /// （足は倒れた体にさえぎられて、反対側の端へは回り込めない）。
    /// </summary>
    private static Vector3 Nearest(in FighterSense sense, Vector3 from)
    {
        var head = sense.OpponentHead;
        var pelvis = sense.OpponentBody == default ? sense.OpponentChest - (head - sense.OpponentChest) : sense.OpponentBody.Pelvis;
        var axis = Flat(head - pelvis);
        var length2 = axis.LengthSquared();
        var t = length2 > 1e-8f ? Math.Clamp(Vector3.Dot(Flat(from - pelvis), axis) / length2, 0f, 1f) : 1f;
        return Vector3.Lerp(pelvis, head, t);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    private static float Smooth(float t) => t * t * (3f - (2f * t));

    /// <summary>打つ瞬間: 先が相手の点の少し奥（下）へ届く、手の位置と武器の向き。手の位置は先の位置から武器の長さだけ戻した所（届かなければ <c>handStrike</c> が肩から遠い）。</summary>
    private static (Vector3 Tip, Vector3 Blade, Vector3 Hand) Strike(GroundMove m, GroundTool tool, Vector3 aimLocal, float reach)
    {
        var tip = aimLocal + (new Vector3(0f, -m.Overshoot, 0f) * reach);
        var blade = tool.Armed ? SwordForms.Direction(0f, m.StrikeElevation) : Vector3.UnitZ;
        var hand = tool.Armed ? tip - (blade * (tool.Length * reach)) : tip;
        return (tip, blade, hand);
    }

    private (Vector3 Position, Quaternion? Orientation, Vector3 Blade) HandPath(GroundMove m, GroundTool tool, in FighterCommand basis, float t, Vector3 aimLocal, float reach)
    {
        var (_, bladeStrike, handStrike) = Strike(m, tool, aimLocal, reach);
        var handWind = handStrike + (new Vector3(0f, m.Rise, -m.Pull) * reach);
        var bladeWind = tool.Armed ? SwordForms.Direction(0f, m.WindElevation) : Vector3.UnitZ;
        var guardPos = basis.For(tool.TwoHanded ? Hand.Right : tool.Armed ? BoxingGame.WeaponHand : Hand.Right).TargetLocal;
        var guardBlade = tool.Armed ? SwordForms.Direction(_t.GuardBladeAzimuth, _t.GuardBladeElevation) : Vector3.UnitZ;
        Vector3 pos;
        Vector3 blade;
        if (t < _t.WindShare)
        {
            var u = Smooth(MathF.Max(0f, t) / _t.WindShare);
            pos = Vector3.Lerp(guardPos, handWind, u);
            blade = SwordForms.Slerp(guardBlade, bladeWind, u);
        }
        else if (t < _t.StrikeShare)
        {
            var u = (t - _t.WindShare) / (_t.StrikeShare - _t.WindShare);
            var s = u * u; // 初めはゆっくり、最後に速く（振りかぶった先を落とす）
            pos = Vector3.Lerp(handWind, handStrike, s);
            blade = SwordForms.Slerp(bladeWind, bladeStrike, Smooth(u));
        }
        else
        {
            var u = Smooth((t - _t.StrikeShare) / (1f - _t.StrikeShare));
            pos = Vector3.Lerp(handStrike, guardPos, u);
            blade = SwordForms.Slerp(bladeStrike, guardBlade, u);
        }

        // 肩から腕の長さを越える所へは手が行かない: 届く内側へ丸める（体を沈めて前傾して近づける）。
        var limit = _t.ReachLimit * reach;
        if (pos.Length() > limit)
        {
            pos = Vector3.Normalize(pos) * limit;
        }

        Quaternion? orientation = null;
        if (tool.Armed)
        {
            var edge = SwordMotion.Perpendicular(Vector3.UnitZ, blade);
            orientation = new MotionFrame(Vector3.Zero, Vector3.Zero, blade, edge, Vector3.Zero, 0f, Vector3.Zero).Orientation;
        }

        return (pos, orientation, blade);
    }

    /// <summary>踏み下ろし: 足を相手の上へ上げ（振りかぶり）、真下へ踏み下ろし、引き戻す。足首の目標は体フレーム。</summary>
    private LegCommand Foot(GroundMove m, Hand side, float t, Vector3 aimLocal, float reach)
    {
        var rest = new Vector3(side.LocalSide() * _t.FootRestSide * reach, -_t.FootRestDrop * reach, 0f);
        var contact = aimLocal + (new Vector3(0f, -m.Overshoot, 0f) * reach);
        var chamber = contact + (new Vector3(0f, m.Rise, -m.Pull) * reach);
        Vector3 target;
        if (t < _t.WindShare)
        {
            target = Vector3.Lerp(rest, chamber, Smooth(MathF.Max(0f, t) / _t.WindShare));
        }
        else if (t < _t.StrikeShare)
        {
            var u = (t - _t.WindShare) / (_t.StrikeShare - _t.WindShare);
            target = Vector3.Lerp(chamber, contact, u * u);
        }
        else
        {
            target = Vector3.Lerp(contact, rest, Smooth((t - _t.StrikeShare) / (1f - _t.StrikeShare)));
        }

        var knee = chamber + new Vector3(0f, _t.KneeUp * reach, _t.KneeForward * reach);
        return new LegCommand(target, knee, Quaternion.Identity);
    }
}

/// <summary>
/// 人が操るとき、弱・強ボタンを倒れた相手への攻めの呼びかけ（<see cref="GroundCall"/>）にする: 押している間は溜め、離して打つ。
/// 倒れた相手を攻める場面に入った時に押していたボタンは、いったん離すまで数えない（押し続けたまま入って、満タンの溜めで即打たないため）。
/// 攻める場面でない間は、溜めも押した状態の記憶も捨てる。
/// </summary>
public sealed class GroundTrigger
{
    private bool _lightArmed;
    private bool _heavyArmed;
    private bool _lightPrev;
    private bool _heavyPrev;
    private float _lightHeld;
    private float _heavyHeld;

    /// <summary>溜めの上限 [s]。</summary>
    public float FullLoadSeconds { get; init; } = BotStyle.MaxTelegraph;

    /// <summary>
    /// 意図操作へ渡す入力: 倒れた相手を攻める場面では弱・強を渡さない（意図操作が普通の技を始めて、その状態だけ進み、
    /// 相手が立ち上がった時に突然続きが出るのを防ぐ）。移動・回避はそのまま。
    /// </summary>
    public static FighterIntent ForIntent(FighterIntent input, bool ground) => ground ? input with { Light = false, Heavy = false } : input;

    /// <summary>1 歩ぶんの更新。<paramref name="heavyCall"/> は強を離したときの呼びかけ（突き下ろし・空手は踏み下ろし）。</summary>
    public GroundCall Update(bool ground, bool light, bool heavy, float dt, GroundCall heavyCall, out float load)
    {
        load = 0f;
        if (!ground)
        {
            (_lightArmed, _heavyArmed, _lightPrev, _heavyPrev, _lightHeld, _heavyHeld) = (false, false, false, false, 0f, 0f);
            return GroundCall.Hold;
        }

        // 離している間に、次の押下を数えられる状態になる。
        _lightArmed |= !light;
        _heavyArmed |= !heavy;
        var lightDown = light && _lightArmed;
        var heavyDown = heavy && _heavyArmed;
        _lightHeld = lightDown ? MathF.Min(FullLoadSeconds, _lightHeld + dt) : _lightHeld;
        _heavyHeld = heavyDown ? MathF.Min(FullLoadSeconds, _heavyHeld + dt) : _heavyHeld;
        var call = GroundCall.Hold;
        if (_heavyPrev && !heavyDown)
        {
            call = heavyCall;
            load = _heavyHeld;
        }
        else if (_lightPrev && !lightDown)
        {
            call = GroundCall.Swing;
            load = _lightHeld;
        }

        _lightPrev = lightDown;
        _heavyPrev = heavyDown;
        if (!lightDown)
        {
            _lightHeld = 0f;
        }

        if (!heavyDown)
        {
            _heavyHeld = 0f;
        }

        return call;
    }
}

/// <summary>
/// 倒れた相手を攻められる試合（レフェリーなし）で、いつものボットに、倒れた相手への攻め（<see cref="GroundStrike"/>）を足す入力元。
/// 相手が立っている・レフェリーがいる間は、中のボットの指令をそのまま返す（レフェリーありの試合の振る舞いは変わらない）。
/// </summary>
public sealed class GroundStriker : IFighterControl
{
    private readonly GroundStrike _ground;

    /// <param name="inner">いつものボット。倒れた相手を攻める間も毎歩読む（中の状態を進めるため）。</param>
    /// <param name="style">倒れた相手への好み（<see cref="BotStyle.GroundDrive"/> など）を持つスタイル。</param>
    /// <param name="tool">武器を握っているときの道具（握っていなければ <see cref="GroundTool.Fist"/>）。</param>
    /// <param name="seed">打つかどうか・どの技かを決める乱数の種。</param>
    /// <param name="club">道具が棍棒か（振り下ろす系だけを使う）。</param>
    public GroundStriker(IFighterControl inner, BotStyle style, GroundTool tool, int seed, bool club = false)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _ground = new GroundStrike(style, tool, seed, club);
    }

    public IFighterControl Inner { get; }

    public GroundStrike Ground => _ground;

    /// <summary>直近に返した指令（観戦・テスト用）。</summary>
    public FighterCommand Last { get; private set; }

    public FighterCommand Read(in FighterSense sense, float dt)
    {
        var basis = Inner.Read(sense, dt);
        if (!GroundStrike.Applies(sense))
        {
            _ground.Cancel();
            return Last = basis;
        }

        return Last = _ground.Read(sense, basis, dt);
    }

    public void OnPunchLanded(Hand hand, float damage) => Inner.OnPunchLanded(hand, damage);

    public void CarryOver(IFighterControl previous) => Inner.CarryOver(previous is GroundStriker g ? g.Inner : previous);

    public void OnPunchTaken(in HitEvent hit) => Inner.OnPunchTaken(hit);
}
