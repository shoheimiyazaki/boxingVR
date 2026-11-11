using System.Globalization;
using System.Numerics;
using Aigis.Physics;
using Aigis.Player;
using Aigis.Render;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using Aigis.Render.Voxels;
using MiniRing.Audio;
using MiniRing.LowPoly;
using MiniRing.Voxels;

namespace MiniRing;

/// <summary>ホストから毎ステップ渡される入力: プレイヤーの指令と、ラウンドのやり直し要求。</summary>
public readonly record struct BoxingInput(FighterCommand Player, bool ResetRequested = false);

/// <summary>ラウンドごとに操作元を作る（引数はそのラウンドの乱数の種）。</summary>
public delegate IFighterControl ControlFactory(int roundSeed);

/// <summary>
/// 両コーナーの操作元。<paramref name="Cpu"/> は CPU 側（<see cref="Bots.BotCatalog"/> の名前やスタイルからも作れる）。
/// <paramref name="Player"/> を渡すとプレイヤー側もボットになり、<see cref="BoxingInput.Player"/> は無視される
/// （ボット同士の対戦。どちらもサブステップごとに読むので条件は対等）。
/// <paramref name="CpuStyleJson"/> は CPU のスタイルそのもの（リプレイに書き、後で <c>champion</c> が変わっても同じ相手で再生するため）。
/// </summary>
public sealed record BoxingCorners(string CpuName, ControlFactory Cpu, ControlFactory? Player = null, string? CpuStyleJson = null)
{
    /// <summary>ゲームの既定の CPU。</summary>
    public static BoxingCorners Default => Bots.BotCatalog.Corners(Bots.BotCatalog.DefaultCpu);
}

/// <summary>
/// 試合本体（ホスト非依存）: リング・2 体のファイター・CPU・判定・ラウンド進行・描画データ。
/// デスクトップは Aigis の <see cref="GameHost"/> から、VR は OpenXR ホストから同じ <see cref="Step"/> を呼ぶ。
/// <para>物理は内部で約 240Hz にサブステップする（60cm の小さな体を 60Hz・1 サブステップで回すと
/// サーボが発散しやすい／Aigis 調査メモ）。ホストの dt は 60Hz でも 90Hz でもよい。</para>
/// <para>勝敗: 意識が尽きたら KO、倒れて 10 秒立てなければ KO（脚を壊されて立てない場合も）、リングから落ちたら負け。</para>
/// </summary>
public sealed class BoxingGame : IGame<BoxingInput>
{
    public const float CountSeconds = 10f;
    private const float SubstepRate = 240f;
    private const float RoundResetDelay = 3.5f;
    private const float RehitCooldown = 0.18f;
    private const float MinimumDamage = 1f;

    /// <summary>拳に乗った質量のうち、物理の接触では伝わらない体の分を相手へ押し込む割合（反発を含めた運動量の受け渡し）。</summary>
    private const float KnockbackTransfer = 0.6f;

    /// <summary>1 発で相手の全身に与える速度変化の上限 [m/s]（吹き飛ばしすぎない）。</summary>
    private const float MaxKnockbackSpeed = 0.6f;

    private static readonly Hand[] Hands = [Hand.Left, Hand.Right];

    private readonly FighterTuning _tuning;
    private readonly BoxerModel _model;
    private readonly int _seed;
    /// <summary>
    /// 体の形（ローポリのモデル）は不変なので、同じ体格の試合どうしで共有する。ボクセルの「形の内側」の判定もモデルごとに
    /// 1 度で済む（試合を作るたびに作り直すと 1 試合 0.6 秒かかり、対戦場やテストで何百試合も作ると効く）。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(HumanoidRagdoll.HalfExtents, int), BoxerModel> Models = new();

    private readonly List<HitEvent> _recentHits = [];
    private readonly List<VoxelChip> _chips = [];
    private readonly List<VoxelChunk> _chunks = [];
    private readonly List<VoxelCutFace> _cutFaces = [];
    private readonly WoundGlow _wounds = new();
    private readonly VoxelDebris _debris = new(capacity: 9000);
    private readonly ImpactEffects _impacts = new();
    private readonly List<ShatterEvent> _recentShatters = [];
    private readonly List<DebrisLanding> _recentLandings = [];
    private readonly List<SwingEvent> _recentSwings = [];
    private readonly Dictionary<(Fighter, Hand), (Vector3 Velocity, bool Armed)> _swing = [];
    private readonly Random _fx;
    private readonly Dictionary<(Fighter, Hand), Vector3> _preStepGloveVelocity = [];
    private readonly Dictionary<(Fighter, Hand), float> _hitCooldown = [];
    private readonly List<PrimitiveInstance> _renderInstances = [];
    private readonly List<DebugLine> _renderLines = [];
    private readonly List<VoxelVolumeDraw> _renderVoxels = [];
    private readonly List<ParticleInstance> _renderParticles = [];
    private float _lookTime;
    private PhysicsWorld _world = null!;
    private Fighter[] _fighters = [];
    private Ring _ring = null!;
    private readonly BoxingCorners _corners;
    private IFighterControl _cpuControl = null!;
    private IFighterControl? _playerControl;
    private float _roundOverTimer;
    private string _roundMessage = "";
    private float _messageTimer;
    private int _round;

    public BoxingGame(FighterTuning? tuning = null, int seed = 1, BoxingCorners? corners = null)
    {
        _tuning = tuning ?? FighterTuning.Default;
        _corners = corners ?? BoxingCorners.Default;
        _fx = new Random(seed ^ 0x5eed);
        _model = Models.GetOrAdd((_tuning.HalfExtents, _tuning.ModelSubdivisions), key => BoxerModel.Build(key.Item1, key.Item2));
        _seed = seed;
        StartRound();
    }

    public PhysicsWorld World => _world;

    /// <summary>CPU の乱数の種（リプレイで同じ試合を作り直すため）。</summary>
    public int Seed => _seed;

    /// <summary>ファイターの調整値（リプレイで同じ試合を作り直すため）。</summary>
    public FighterTuning Tuning => _tuning;

    /// <summary>CPU 側のボットの名前（リプレイで同じ相手を作り直すため）。</summary>
    public string CpuName => _corners.CpuName;

    /// <summary>CPU 側のボットのスタイル（JSON）。legacy や外から渡した操作元なら null。</summary>
    public string? CpuStyleJson => _corners.CpuStyleJson;

    /// <summary>このラウンドの CPU 側の操作元。</summary>
    public IFighterControl CpuControl => _cpuControl;

    /// <summary>このラウンドのプレイヤー側の操作元（ボット同士のときだけ。人が操作するときは null）。</summary>
    public IFighterControl? PlayerControl => _playerControl;

    /// <summary>今のラウンド番号（1 から）。</summary>
    public int Round => _round;
    public Ring Ring => _ring;
    public Fighter Player { get; private set; } = null!;
    public Fighter Cpu { get; private set; } = null!;

    /// <summary>見た目の体（打撃でへこむローポリ）。ファイターと同じくラウンドごとに新品。</summary>
    public BoxerSkin PlayerSkin { get; private set; } = null!;
    public BoxerSkin CpuSkin { get; private set; } = null!;

    /// <summary>この試合で使っている体のメッシュ（部位ごとの変形前の形）。</summary>
    public BoxerModel Model => _model;

    public BoxerSkin SkinOf(Fighter fighter) => fighter == Player ? PlayerSkin
        : fighter == Cpu ? CpuSkin
        : throw new ArgumentException($"この試合のファイターではありません: {fighter?.Name}", nameof(fighter));
    public int PlayerWins { get; private set; }
    public int CpuWins { get; private set; }
    public bool RoundOver => _roundOverTimer > 0f;
    public string Message => _messageTimer > 0f ? _roundMessage : "";

    /// <summary>直近のヒット（HUD 表示用。次のヒットまで残る）。</summary>
    public HitEvent? LastHit { get; private set; }

    /// <summary>直近の <see cref="Step"/> で発生したヒット（ホストの効果音・ハプティクス用）。</summary>
    public IReadOnlyList<HitEvent> RecentHits => _recentHits;

    /// <summary>直近の <see cref="Step"/> で砕けたボクセルの列（1 発に 1 つ。ホストの砕ける音用）。</summary>
    public IReadOnlyList<ShatterEvent> RecentShatters => _recentShatters;

    /// <summary>直近の <see cref="Step"/> で床に落ちた（跳ねた）破片（ホストの落下音用）。</summary>
    public IReadOnlyList<DebrisLanding> RecentLandings => _recentLandings;

    /// <summary>直前の <see cref="Step"/> で振り抜かれた拳（風切り音）。</summary>
    public IReadOnlyList<SwingEvent> RecentSwings => _recentSwings;

    public string StatusLine => string.Create(CultureInfo.InvariantCulture,
        $"Round {_round}  You {Cpu.Condition.TotalDamage:0} - CPU {Player.Condition.TotalDamage:0} damage  ({PlayerWins}-{CpuWins})");

    public bool IsOver => false;

    public static Vector3 RingCenter { get; } = new(0f, 0f, 0.95f);
    public const float RingSize = 1.2f;
    public const float RingSurfaceHeight = 0.75f;

    public void Step(BoxingInput input, float dt)
    {
        _recentHits.Clear();
        _recentShatters.Clear();
        _recentLandings.Clear();
        _recentSwings.Clear();
        if (input.ResetRequested)
        {
            StartRound();
            return;
        }

        var substeps = Math.Max(1, (int)MathF.Ceiling(dt * SubstepRate));
        var sub = dt / substeps;
        for (var i = 0; i < substeps; i++)
        {
            Substep(input.Player, sub);
        }

        _messageTimer = MathF.Max(0f, _messageTimer - dt);
        _debris.Step(dt, _recentLandings);
        _impacts.Step(dt);
        _wounds.Step(dt);
        _lookTime += dt;
        DetectSwings();
        UpdateRound(dt);
    }

    /// <summary>
    /// グローブの速さが <see cref="SwingSound.MinSpeed"/> を超えて、いちばん速くなった（減り始めた）フレームを 1 回の振りとして出す。
    /// 速さがその半分を下回ったら次の振りを待つ。音のためだけで、試合（物理・記録）には響かない。
    /// </summary>
    private void DetectSwings()
    {
        foreach (var fighter in Fighters)
        {
            var opponent = fighter == Player ? Cpu : Player;
            foreach (var hand in Hands)
            {
                var velocity = fighter.GloveVelocity(hand);
                var speed = velocity.Length();
                // 最初は「止まるまで待つ」: 試合開始の構えに入る揺れを振りと数えない。
                var (previous, armed) = _swing.GetValueOrDefault((fighter, hand), (Vector3.Zero, false));
                var previousSpeed = previous.Length();
                var toOpponent = Vector3.Normalize(opponent.HeadPosition - fighter.GloveCenter(hand));
                // 相手へ向かう振りだけ（引き戻しは鳴らさない）。
                if (armed && previousSpeed >= SwingSound.MinSpeed && speed < previousSpeed && Vector3.Dot(previous, toOpponent) > 0.5f * previousSpeed)
                {
                    _recentSwings.Add(new SwingEvent(fighter.GloveCenter(hand), previousSpeed));
                    armed = false;
                }

                _swing[(fighter, hand)] = (velocity, armed || speed < 0.5f * SwingSound.MinSpeed);
            }
        }
    }

    private void Substep(in FighterCommand playerCommand, float dt)
    {
        var cpuCommand = _cpuControl.Read(Sense(Cpu, Player), dt);
        Drive(Player, Cpu, _playerControl?.Read(Sense(Player, Cpu), dt) ?? playerCommand, dt);
        Drive(Cpu, Player, cpuCommand, dt);

        foreach (var fighter in Fighters)
        {
            foreach (var hand in Hands)
            {
                _preStepGloveVelocity[(fighter, hand)] = fighter.GloveVelocity(hand);
                _hitCooldown[(fighter, hand)] = MathF.Max(0f, _hitCooldown.GetValueOrDefault((fighter, hand)) - dt);
            }
        }

        _world.Step(dt);
        DetectHits();
    }

    private void Drive(Fighter self, Fighter opponent, in FighterCommand command, float dt)
    {
        foreach (var hand in Hands)
        {
            if (self.UpdateGrab(hand, command.For(hand).Grip, opponent) is not null)
            {
                ShowMessage(self == Player ? "THROW!" : "THROWN!", 1f);
            }
        }

        self.ApplyControl(command, dt);
    }

    /// <summary>物理の接触一覧から「拳（前腕の先）× 相手の部位」を拾う。</summary>
    private void DetectHits()
    {
        foreach (var contact in _world.Contacts())
        {
            if (contact.StaticA || contact.StaticB)
            {
                continue;
            }

            TryHit(Player, Cpu, contact.A, contact.B);
            TryHit(Player, Cpu, contact.B, contact.A);
            TryHit(Cpu, Player, contact.A, contact.B);
            TryHit(Cpu, Player, contact.B, contact.A);
        }
    }

    /// <summary>
    /// 1 発の判定。衝撃 = 拳に乗った質量（<see cref="Fighter.ImpactMass(Hand, Vector3)"/>: 姿勢と向きだけで決まる）
    /// × 衝突直前に相手へ踏み込む拳の速さ。急所の判定は当たった点の部位ローカル位置から（<see cref="HitZones"/>）。
    /// </summary>
    private void TryHit(Fighter attacker, Fighter victim, int attackerBody, int victimBody)
    {
        // 倒れている相手（起き上がり中を含む）への打撃は数えない: ダウンはカウントで決着させる。
        if (!victim.Owns(victimBody) || victim.State != FighterState.Standing)
        {
            return;
        }

        foreach (var hand in Hands)
        {
            // 切り落とされた腕（落ちていく破片）は打撃に数えない。
            if (attacker.ArmSevered(hand) || attacker.ForearmId(hand) != attackerBody || _hitCooldown[(attacker, hand)] > 0f)
            {
                continue;
            }

            // 前腕のどこが当たっても接触は出るので、拳（グローブ球）の近くで当たったものだけをパンチとする（肘打ちを除外）。
            var glove = attacker.GloveCenter(hand);
            if (victim.DistanceToPart(victimBody, glove) > _tuning.GloveRadius * 1.6f)
            {
                continue;
            }

            var velocity = _preStepGloveVelocity[(attacker, hand)];
            var into = _world.GetState(victimBody).Position - glove;
            var speed = into.LengthSquared() > 1e-8f ? MathF.Max(0f, Vector3.Dot(velocity, Vector3.Normalize(into))) : 0f;
            if (speed <= 0f)
            {
                continue;
            }

            var impactMass = attacker.ImpactMass(hand, velocity, out var bodyShare);
            var (kind, normalized, armHand) = victim.Locate(victimBody, glove);
            var zone = HitZones.Classify(kind, normalized, armHand.LocalSide());
            var damage = PunchMechanics.Damage(impactMass * speed) * PunchMechanics.Sharpness(speed) * ZoneDamage.Multiplier(zone);
            if (damage < MinimumDamage)
            {
                continue; // 触れただけ・押し合い
            }

            _hitCooldown[(attacker, hand)] = RehitCooldown;
            ApplyHit(new HitEvent(attacker, hand, victim, zone, glove, speed, impactMass, bodyShare, damage), victimBody, velocity);
        }
    }

    internal void ApplyHit(HitEvent hit, int victimBody, Vector3 gloveVelocity)
    {
        if (RoundOver)
        {
            return;
        }

        var victim = hit.Victim;
        // ダメージは記録だけ（見た目と得点）。動きは落とさない。
        victim.Condition.ApplyHit(hit.Zone, hit.Damage);

        // 当たった部位の、拳に近いノードを拳の進む向きへへこませる（見た目だけ。当たり判定の箱は変わらない）。
        SkinOf(victim).Dent(victimBody, _world.GetState(victimBody), hit.Point, gloveVelocity, hit.Damage);
        // 体重の乗った拳は相手を押し込む: 物理の接触が運ぶのは腕の質量だけなので、体から来た分の運動量を足す。
        // 一部は当たった部位に残り（strike）、残りは相手の（付いている）全身へ一様な速度変化として配り（上限付き）、
        // 同じ運動量の反作用を打った側の全身へ返す（運動量は湧かない）。
        var victimMass = victim.AttachedMass;
        var partMass = _world.GetMass(victimBody);
        var push = Vector3.Zero;
        var strike = Vector3.Zero;
        var extraMass = hit.ImpactMass - hit.Attacker.ArmMass(hit.Hand);
        if (extraMass > 0f && gloveVelocity.LengthSquared() > 1e-8f)
        {
            // 運ぶ運動量のうち、当たった部位に残る分（strike）は速さとともに増える（非線形: 部位に残る運動量 ∝ m v²）。
            // 速い拳ほど接触が短く、その間に首（関節のバネ）が体へ逃がせる運動量は限られるので、頭がそのまま持っていかれる。
            // ゆっくり押し当てる拳は、関節を通して全身へ配られる（鋭さで 0 へ）。部位に与える速さには上限を置き（拳の速さの
            // 測り違いで部位が飛ばない）、上限を超えた分は全身を押す側へ回す。
            var carried = Vector3.Normalize(gloveVelocity) * (extraMass * hit.Speed * KnockbackTransfer);
            strike = ClampLength(carried * StrikeShare(hit.Speed), _tuning.MaxStrikeSpeed * partMass);
            push = ClampLength((carried - strike) / victimMass, MaxKnockbackSpeed);
        }

        if (strike != Vector3.Zero || push != Vector3.Zero)
        {
            // 部位に残る運動量は、拳が部位の表面に触れた点に掛かる: 部位を押し（頭なら首の上でのけぞる）、中心から外れた分だけ回す
            // （あご・こめかみを振り抜くと頭が首の上でねじれる。回転の中心を通る拳は回さない）。削って千切る前に掛ける
            // （千切れた先の点で、残った切り株を回さない）。
            var part = _world.GetState(victimBody);
            var twist = Vector3.Cross(victim.ClosestPointOnPart(victimBody, hit.Point) - part.Position, strike);
            _world.ApplyLinearImpulse(victimBody, strike);
            _world.ApplyAngularImpulse(victimBody, twist);
            if (victimBody == victim.Parts.Head)
            {
                victim.TwistHead(twist); // 頭がねじられた分だけ脳しんとう
            }

            victim.Push(push);
            // 反作用は打った側の（付いている）全身へ（部位に残した分も含めて、運動量は湧かない）。
            hit.Attacker.Push(-((push * victimMass) + strike) / hit.Attacker.AttachedMass);
        }

        // 削る量は打撃そのものの強さ（部位の倍率を外したダメージ）。得点ではガードの上は 0.25 倍に割り引くが、
        // 受けた腕にかかる衝撃は同じ: ブロックし続けた腕は削れていく。破片は、のけぞる体の動き（push）に乗って流れる。
        // 削る量は拳の運動エネルギー（½ m v²）に比例: 体重の乗った速い 1 発ほど急に深く掘れる（非線形）。
        var energy = 0.5f * hit.ImpactMass * hit.Speed * hit.Speed * PunchMechanics.Sharpness(hit.Speed);
        Strike(victim, victimBody, hit.Point, gloveVelocity, hit.Damage / ZoneDamage.Multiplier(hit.Zone), hit.Attacker, push, MathF.Min(_tuning.MaxCarve, energy * _tuning.CarvePerJoule));

        _recentHits.Add(hit);
        LastHit = hit;
        ControlOf(hit.Attacker)?.OnPunchLanded(hit.Hand, hit.Damage);
        ControlOf(victim)?.OnPunchTaken(hit);
    }

    /// <summary>
    /// ボクセルの体を削る: ダメージに比例した量を、拳の進む向きへ掘る。削れたボクセルは破片と血しぶきになって飛ぶ。
    /// 腕・脚の骨が断たれたら、そこから先が落ちる。臓器が半分以上壊れたら死ぬ。
    /// </summary>
    /// <param name="victim">打たれたファイター。</param>
    /// <param name="victimBody">打たれた部位の剛体。</param>
    /// <param name="point">当たった点（ワールド）。</param>
    /// <param name="direction">拳の進む向き（速度でよい）。</param>
    /// <param name="damage">打撃の強さ（ダメージ点）。</param>
    /// <param name="attacker">打った側（火花をその側のグローブの色にする）。分からなければ null。</param>
    /// <param name="recoil">打たれた体がこれから受ける押し込みの速度（破片はこれに乗って、のけぞる向きへ流れる）。</param>
    /// <param name="carve">削る量（肉 1 ボクセル = 1）。null ならダメージ × <see cref="FighterTuning.CarvePerDamage"/>。</param>
    internal void Strike(Fighter victim, int victimBody, Vector3 point, Vector3 direction, float damage, Fighter? attacker = null, Vector3 recoil = default, float? carve = null)
    {
        if (victim.Voxels is not { } body)
        {
            return;
        }

        // 飛び散り方で打撃を読ませる: 強いほど速く・細く・遠くへ（弱い 1 発は近くにぽろっと落ちる）。
        var strength = HitStrength(damage);
        var chipSpeed = _tuning.ChipSpeed * (0.5f + (1.3f * strength));
        var spread = 0.75f - (0.5f * strength);
        _chips.Clear();
        _chunks.Clear();
        _cutFaces.Clear();
        var result = body.Impact(victimBody, point, direction, carve ?? (damage * _tuning.CarvePerDamage), chipSpeed, Jitter, _chips, spread, _chunks, _cutFaces);
        if (result is { Part: var struck } && _cutFaces.Count > 0)
        {
            _wounds.Add(struck, _cutFaces); // えぐれた穴の断面も、欠片と同じく白から冷めていく
        }

        SpawnDebris(_chips, point, direction, strength, attacker, recoil);
        SpawnChunks(_chunks, recoil);
        if (result?.Fragment is { } fragment)
        {
            victim.Sever(victimBody, fragment.Body);
            ShowMessage(fragment.Kind is BodyPartKind.UpperArm or BodyPartKind.Forearm ? "ARM OFF!" : "LEG OFF!", 1.2f);
        }

        var fatal = false;
        if (!victim.IsDead && body.DeathCause() is { } cause)
        {
            victim.Die(cause);
            fatal = true;
            _impacts.Spawn(point, direction, 1f, FatalFlash, Jitter); // 致命傷: いちばん大きな赤い光
        }

        if (_chips.Count > 0 || _chunks.Count > 0 || fatal)
        {
            // 音の粒: 砕けた順（当たった所に近い順）に 3.5ms ずつ、少しばらつかせて並べる。塊のボクセルも 1 個 1 粒。
            var materials = _chips.Select(c => c.Material).Concat(_chunks.SelectMany(c => c.Cells.Select(cell => cell.Material))).ToList();
            var grains = new ShatterGrain[materials.Count];
            for (var i = 0; i < grains.Length; i++)
            {
                grains[i] = new ShatterGrain(materials[i], MathF.Min(0.3f, (i * 0.0035f) + (0.002f * (1f + Jitter()))));
            }

            _recentShatters.Add(new ShatterEvent(point, grains, fatal));
        }
    }

    /// <summary>打撃の強さ 0（かすり）〜 1（とても重い 1 発）。ダメージ 3 で 0、30 で 1 にかけて、平方根で（中くらいの差も見える）。</summary>
    internal static float HitStrength(float damage) => MathF.Sqrt(Math.Clamp((damage - 3f) / 27f, 0f, 1f));

    /// <summary>
    /// 削れたボクセルを砕け散らせる。散り方で「どの向きに・どれだけ強く」当たったかが読めるように、どの破片も
    /// 欠片の飛ぶ向き（<see cref="VoxelBody.Impact"/> の噴き出し）に沿わせ、その速さに比べた小さな揺らぎだけを足す。
    /// 1 個ごとに、その材質の色の欠片と細かい破片 2 つ。骨からは白い粉と光る火花、肉と臓器からは血しぶき、臓器からはさらに
    /// 光る赤い飛沫。当たった所には打った側の色の閃光（<see cref="ImpactFlash"/>）。
    /// </summary>
    private void SpawnDebris(List<VoxelChip> chips, Vector3 point, Vector3 direction, float strength, Fighter? attacker, Vector3 recoil)
    {
        ImpactFlash(point, direction, strength, attacker);
        if (chips.Count == 0)
        {
            return;
        }

        var palette = BoxerAnatomy.Palette;
        foreach (var chip in chips)
        {
            var material = palette[chip.Material];
            var color = chip.WasSurface ? material.SurfaceColor : material.Color;
            var along = chip.Velocity;
            var pace = along.Length();
            // 欠片の向きに沿って、速さを掛け、その速さに比べた揺らぎ（spread）を足す。のけぞる体の動きにも乗る。
            Vector3 Fly(float speedScale, float wobble) => (along * speedScale) + (new Vector3(Jitter(), Jitter(), Jitter()) * (wobble * pace * speedScale)) + recoil;
            Vector3 Spin(float s) => new Vector3(Jitter(), Jitter(), Jitter()) * s;
            // 砕けた直後の欠片は一瞬光り、短い光の尾を引く（ChipHeat 秒で元の色に戻る）。
            _debris.Spawn(chip.Position, Fly(1.5f, 0.12f), chip.Orientation, chip.Size * 0.9f, Spin(40f), color, tag: chip.Material, heat: ChipHeat);
            for (var k = 0; k < 2; k++)
            {
                _debris.Spawn(chip.Position, Fly(1.8f, 0.25f), chip.Orientation, chip.Size * 0.45f, Spin(70f), color, life: 1.6f, tag: chip.Material, heat: ChipHeat * 0.7f);
            }

            switch (material.Tissue)
            {
                case VoxelTissue.Bone:
                    for (var k = 0; k < 3; k++)
                    {
                        _debris.Spawn(chip.Position, Fly(1.6f, 0.35f), chip.Orientation, chip.Size * 0.25f, Spin(90f), BoneDust, life: 1.2f, tag: VoxelBreakSound.DustTag);
                    }

                    _debris.Spawn(chip.Position, Fly(2.4f, 0.3f), chip.Orientation, chip.Size * 0.22f, Spin(30f), Spark, life: 0.25f, glow: true, tag: VoxelBreakSound.SparkTag);
                    break;
                case VoxelTissue.Organ:
                    for (var k = 0; k < 2; k++)
                    {
                        _debris.Spawn(chip.Position, Fly(2f, 0.3f), chip.Orientation, chip.Size * 0.35f, Spin(50f), OrganGlow, life: 0.4f, glow: true, tag: VoxelBreakSound.SparkTag);
                    }

                    goto case VoxelTissue.Flesh;
                case VoxelTissue.Flesh:
                    for (var k = 0; k < 3; k++)
                    {
                        _debris.Spawn(chip.Position, Fly(2.1f, 0.3f), chip.Orientation, chip.Size * 0.35f, Spin(60f), BloodColor, life: 2.2f, tag: VoxelBreakSound.BloodTag);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// 塊のまま剥がれたボクセルを飛ばす: 1 つの塊として回りながら飛び、はがれた断面だけが一瞬光る（外側だった面は光らない）。
    /// 肉の混じった塊からは、断面から血が少し飛ぶ。
    /// </summary>
    private void SpawnChunks(List<VoxelChunk> chunks, Vector3 recoil)
    {
        var palette = BoxerAnatomy.Palette;
        foreach (var chunk in chunks)
        {
            var cells = new DebrisCell[chunk.Cells.Count];
            var flesh = 0;
            for (var i = 0; i < cells.Length; i++)
            {
                var c = chunk.Cells[i];
                var material = palette[c.Material];
                cells[i] = new DebrisCell(c.Offset, c.WasSurface ? material.SurfaceColor : material.Color, c.TornFaces);
                flesh += material.Tissue is VoxelTissue.Flesh or VoxelTissue.Organ ? 1 : 0;
            }

            var velocity = (chunk.Velocity * ChunkFling) + recoil;
            var spin = chunk.AngularVelocity + (new Vector3(Jitter(), Jitter(), Jitter()) * ChunkSpin);
            _debris.SpawnChunk(chunk.Position, velocity, chunk.Orientation, chunk.CellSize, cells, spin, tag: chunk.Cells[0].Material, heat: ChunkHeat);
            for (var k = 0; k < Math.Min(4, flesh / 2); k++)
            {
                var drop = (velocity * 1.2f) + (new Vector3(Jitter(), Jitter(), Jitter()) * (0.3f * velocity.Length()));
                _debris.Spawn(chunk.Position, drop, chunk.Orientation, chunk.CellSize * 0.35f, new Vector3(Jitter(), Jitter(), Jitter()) * 60f, BloodColor, life: 2.2f, tag: VoxelBreakSound.BloodTag);
            }
        }
    }

    /// <summary>塊のはがれた断面が光っている時間 [s]（欠片と同じく白 → 黄 → 赤 → 元の色）。</summary>
    private const float ChunkHeat = 0.5f;

    /// <summary>塊の飛ぶ速さの倍率（欠片の 1.5 倍より遅い: 重い）。</summary>
    private const float ChunkFling = 1.3f;

    /// <summary>塊の回る速さ [rad/s]（欠片より、ゆっくり大きく回る）。</summary>
    private const float ChunkSpin = 14f;

    /// <summary>当たった瞬間の光（<see cref="ImpactEffects"/>: 球とカプセルで、飛び散るボクセルと紛れない）。打った側のグローブの色。</summary>
    private void ImpactFlash(Vector3 point, Vector3 direction, float strength, Fighter? attacker)
    {
        var color = attacker is null ? Spark : attacker == Player ? PlayerSpark : CpuSpark;
        _impacts.Spawn(point, direction, strength, color, Jitter);
    }

    /// <summary>砕けた欠片が光っている時間 [s]: 白 → 黄 → 赤 → 元の色と、空中で冷めていく（<see cref="VoxelDebris.HeatColor"/>）。</summary>
    private const float ChipHeat = 0.5f;

    private static readonly Vector4 BloodColor = new(0.55f, 0.02f, 0.03f, 1f);
    private static readonly Vector4 BoneDust = new(1f, 0.98f, 0.9f, 1f);
    private static readonly Vector4 Spark = new(1f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 OrganGlow = new(1f, 0.15f, 0.25f, 1f);
    private static readonly Vector4 PlayerSpark = new(1f, 0.08f, 0.04f, 1f);
    private static readonly Vector4 CpuSpark = new(0.1f, 0.35f, 1f, 1f);
    private static readonly Vector4 FatalFlash = new(1f, 0.15f, 0.1f, 1f);

    private float Jitter() => (2f * _fx.NextSingle()) - 1f;

    /// <summary>飛び散ったボクセルの破片（描画用）。</summary>
    public VoxelDebris Debris => _debris;

    /// <summary>当たった瞬間の光（描画用）。</summary>
    public ImpactEffects Impacts => _impacts;

    /// <summary>削られた体の、光っている断面（描画用）。</summary>
    public WoundGlow Wounds => _wounds;

    private void UpdateRound(float dt)
    {
        if (RoundOver)
        {
            _roundOverTimer -= dt;
            if (_roundOverTimer <= 0f)
            {
                StartRound();
            }

            return;
        }

        if (Player.IsDead || Cpu.IsDead)
        {
            var dead = Player.IsDead ? Player : Cpu;
            EndRound(cpuWon: dead == Player, $"K.O. — {dead.DeathCause!.ToUpperInvariant()} DESTROYED");
        }
        else if (_ring.IsOut(Player.PelvisPosition))
        {
            EndRound(cpuWon: true, "RING OUT");
        }
        else if (_ring.IsOut(Cpu.PelvisPosition))
        {
            EndRound(cpuWon: false, "RING OUT");
        }
        else if (Player.DownTime > CountSeconds)
        {
            EndRound(cpuWon: true, "10 COUNT K.O.");
        }
        else if (Cpu.DownTime > CountSeconds)
        {
            EndRound(cpuWon: false, "10 COUNT K.O.");
        }
    }

    private void EndRound(bool cpuWon, string reason)
    {
        if (RoundOver)
        {
            return;
        }

        if (cpuWon)
        {
            CpuWins++;
        }
        else
        {
            PlayerWins++;
        }

        _roundOverTimer = RoundResetDelay;
        ShowMessage($"{reason}  {(cpuWon ? "CPU WINS" : "YOU WIN")}", RoundResetDelay);
    }

    /// <summary>
    /// ラウンドを作り直す。Aigis では剛体の削除がハンドル再利用で危険（architecture.md risk 4）なので、
    /// ワールドごと作り直す（体の状態もラウンドごとに新品）。
    /// </summary>
    private void StartRound()
    {
        _world?.Dispose();
        _round++;
        _world = new PhysicsWorld();
        _ring = new Ring(_world, RingCenter, RingSize, RingSurfaceHeight);
        var surface = _ring.SurfaceCenter;
        Player = Fighter.Spawn(_world, _tuning, "player", surface - new Vector3(0f, 0f, 0.22f), 0f);
        Cpu = Fighter.Spawn(_world, _tuning, "cpu", surface + new Vector3(0f, 0f, 0.22f), MathF.PI);
        PlayerSkin = new BoxerSkin(_model, Player, _tuning);
        // 体の形はローポリのモデル（頭・胸・腕の形）。その内側をボクセルで埋める。トランクスはグローブと同じコーナーの色。
        Player.Voxels = new BoxerBody(Player, _world, _tuning, _model, Outfit.RedCorner);
        Cpu.Voxels = new BoxerBody(Cpu, _world, _tuning, _model, Outfit.BlueCorner);
        // 破片はリングの上ならリングの面、外なら床で止まる。
        var ringHalf = (RingSize * 0.5f) + 0.02f;
        _debris.Clear();
        _impacts.Clear();
        _wounds.Clear();
        _debris.GroundAt = p => MathF.Abs(p.X - RingCenter.X) < ringHalf && MathF.Abs(p.Z - RingCenter.Z) < ringHalf ? RingSurfaceHeight : 0f;
        _debris.Gravity = 9.81f * _tuning.DebrisGravityScale;
        CpuSkin = new BoxerSkin(_model, Cpu, _tuning);
        _fighters = [Player, Cpu];
        _cpuControl = _corners.Cpu(_seed + _round);
        _playerControl = _corners.Player?.Invoke((_seed * 7919) + _round);
        _roundOverTimer = 0f;
        LastHit = null;
        _preStepGloveVelocity.Clear();
        _hitCooldown.Clear();
        ShowMessage("FIGHT!", 1.5f);
    }

    private void ShowMessage(string text, float seconds)
    {
        _roundMessage = text;
        _messageTimer = seconds;
    }

    private IFighterControl? ControlOf(Fighter fighter) => fighter == Cpu ? _cpuControl : _playerControl;

    /// <summary>入力元に渡す、<paramref name="self"/> から見た状況。</summary>
    public static FighterSense Sense(Fighter self, Fighter opponent)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(opponent);
        return new FighterSense(self.Frame, self.ArmReach, opponent.HeadPosition, opponent.ChestPosition,
            opponent.State == FighterState.Standing, self.HeadPosition,
            new GloveSense(opponent.GloveCenter(Hand.Left), opponent.GloveVelocity(Hand.Left)),
            new GloveSense(opponent.GloveCenter(Hand.Right), opponent.GloveVelocity(Hand.Right)),
            opponent.ChestVelocity,
            new BodySense(opponent.ShoulderPosition(Hand.Left), opponent.ShoulderPosition(Hand.Right), opponent.PelvisPosition),
            self.Faintness);
    }

    public FighterSense PlayerSense => Sense(Player, Cpu);

    private Fighter[] Fighters => _fighters;

    /// <summary>
    /// このフレームの描画データ。返すリストは使い回しで、次の呼び出しで中身が入れ替わる
    /// （Triage の <c>BuildRenderables</c> と同じ。受け取ったらそのフレームのうちに描くこと）。
    /// </summary>
    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables()
    {
        BoxingRenderer.Fill(this, showTargetGhosts: true, _renderInstances, _renderLines);
        return (_renderInstances, _renderLines);
    }

    /// <summary>
    /// このフレームのボクセルの体（3D テクスチャと置き場所）。返すリストは使い回し（<see cref="BuildRenderables"/> と同じ）。
    /// </summary>
    public IReadOnlyList<VoxelVolumeDraw> BuildVoxelVolumes()
    {
        BoxingRenderer.FillVoxels(this, _renderVoxels);
        return _renderVoxels;
    }

    /// <summary>当たった瞬間の光と会場の空気の粒（返すリストは使い回し）。</summary>
    public IReadOnlyList<ParticleInstance> BuildParticles()
    {
        _renderParticles.Clear();
        ArenaLook.WriteAtmosphere(RingCenter, RingSize, RingSurfaceHeight, _lookTime, _renderParticles);
        _impacts.WriteParticles(_renderParticles);
        _debris.WriteTrails(_renderParticles);
        _wounds.WriteHalos(_renderParticles);
        return _renderParticles;
    }

    /// <summary>会場の照明（<see cref="ArenaLook"/>）。</summary>
    public SceneLighting BuildLighting() => Lighting;

    /// <summary>リングに落ちる影。</summary>
    public ShadowMapConfig? BuildShadow() => ArenaLook.Shadow(RingCenter, RingSize, RingSurfaceHeight);

    /// <summary>画面用の仕上げ（光のにじみ・陰り・コントラスト）。</summary>
    public PostProcessSettings? BuildPostProcess() => ArenaLook.ScreenFinish;

    private static SceneLighting Lighting { get; } = ArenaLook.Lighting(RingCenter, RingSize, RingSurfaceHeight);

    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 viewProjection, Vector2 screenSizePixels)
    {
        var white = new Vector4(1f, 1f, 1f, 1f);
        var list = new List<ScreenTextDraw>
        {
            new($"{PlayerWins} - {CpuWins}", new Vector2(screenSizePixels.X * 0.5f, 20f), 28f, white, TextAnchor.Center),
        };
        AddCondition(list, "YOU", Cpu, new Vector2(24f, 20f), TextAnchor.Left, new Vector4(1f, 0.45f, 0.4f, 1f));
        AddCondition(list, "CPU", Player, new Vector2(screenSizePixels.X - 24f, 20f), TextAnchor.Right, new Vector4(0.5f, 0.65f, 1f, 1f));
        if (LastHit is { } hit)
        {
            var who = hit.Attacker == Player ? "YOU" : "CPU";
            var text = $"{who} {(hit.Hand == Hand.Left ? "L" : "R")} > {HitZones.Label(hit.Zone)}  {hit.Damage:0}  " +
                $"{hit.ImpactMass:0.0}kg x {hit.Speed:0.0}m/s  BODY {Percent(hit.BodyShare)}";
            list.Add(new ScreenTextDraw(text, new Vector2(screenSizePixels.X * 0.5f, screenSizePixels.Y - 44f), 22f, white, TextAnchor.Center));
        }

        foreach (var fighter in Fighters)
        {
            if (fighter.DownTime > 0.5f && !RoundOver)
            {
                list.Add(new ScreenTextDraw($"{(fighter == Player ? "YOU" : "CPU")} DOWN  {MathF.Min(CountSeconds, MathF.Floor(fighter.DownTime) + 1f):0}",
                    new Vector2(screenSizePixels.X * 0.5f, screenSizePixels.Y * 0.42f), 40f, new Vector4(1f, 0.85f, 0.3f, 1f), TextAnchor.Center));
            }
        }

        if (Message.Length > 0)
        {
            list.Add(new ScreenTextDraw(Message, new Vector2(screenSizePixels.X * 0.5f, screenSizePixels.Y * 0.3f), 56f, white, TextAnchor.Center));
        }

        return list;
    }

    private static void AddCondition(List<ScreenTextDraw> list, string name, Fighter opponent, Vector2 at, TextAnchor anchor, Vector4 color)
    {
        // 得点 = 相手に与えたダメージとクリーンヒット数（ダメージは動きに影響しない）。
        var given = opponent.Condition;
        string[] lines =
        [
            name,
            string.Create(CultureInfo.InvariantCulture, $"DAMAGE {given.TotalDamage:0}"),
            string.Create(CultureInfo.InvariantCulture, $"CLEAN HITS {given.CleanHitsTaken}"),
        ];
        for (var i = 0; i < lines.Length; i++)
        {
            var y = i == 0 ? 0f : 12f + (i * 22f);
            list.Add(new ScreenTextDraw(lines[i], at + new Vector2(0f, y), i == 0 ? 26f : 18f, color, anchor));
        }
    }

    private static string Percent(float value) => (value * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// 試合の状態のチェックサム（FNV-1a 64）: 全剛体の位置・姿勢・速度、両者の受けたダメージ、勝敗。
    /// リプレイが記録と同じ動きをしているかを、ビット単位で照合するのに使う。
    /// </summary>
    public ulong StateChecksum()
    {
        var hash = 14695981039346656037UL;
        void Add(float value)
        {
            var bits = (uint)BitConverter.SingleToInt32Bits(value);
            for (var i = 0; i < 4; i++)
            {
                hash = (hash ^ ((bits >> (8 * i)) & 0xFF)) * 1099511628211UL;
            }
        }

        foreach (var body in _world.SnapshotDynamics())
        {
            Add(body.Position.X);
            Add(body.Position.Y);
            Add(body.Position.Z);
            Add(body.Orientation.X);
            Add(body.Orientation.Y);
            Add(body.Orientation.Z);
            Add(body.Orientation.W);
            Add(body.LinearVelocity.X);
            Add(body.LinearVelocity.Y);
            Add(body.LinearVelocity.Z);
        }

        foreach (var fighter in _fighters)
        {
            Add(fighter.Condition.TotalDamage);
            Add(fighter.Condition.CleanHitsTaken);
            Add(fighter.IsDead ? 1f : 0f);
            Add((float)fighter.State);
        }

        Add(PlayerWins);
        Add(CpuWins);
        Add(_round);
        return hash;
    }

    public void Dispose() => _world.Dispose();

    /// <summary>
    /// 運ぶ運動量のうち当たった部位に残る割合: 拳の速さに比例して <see cref="FighterTuning.FullStrikeSpeed"/> で
    /// <see cref="FighterTuning.StrikeShare"/> に達する（押し当てるだけの遅い拳は鋭さで 0）。
    /// </summary>
    private float StrikeShare(float speed)
        => _tuning.StrikeShare * PunchMechanics.Sharpness(speed) * Math.Clamp(speed / MathF.Max(1e-3f, _tuning.FullStrikeSpeed), 0f, 1f);

    private static Vector3 ClampLength(Vector3 v, float max)
    {
        var length = v.Length();
        return length > max ? v * (max / length) : v;
    }
}
