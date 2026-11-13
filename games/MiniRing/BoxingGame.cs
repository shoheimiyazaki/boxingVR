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
/// <para><paramref name="CpuWeapon"/> / <paramref name="PlayerWeapon"/> を渡すと、その側はラウンドの初めから右手のグローブを外し、
/// その武器を握って立つ（<see cref="Fighter.Wield"/>）。</para>
/// <para><paramref name="PlayerWear"/>（赤コーナー）/ <paramref name="CpuWear"/>（青コーナー）は、武器を握っていない手にはめる物
/// （<see cref="Weapons.HandWear"/>: グローブ・素手・メリケンサック・カタール・籠手。既定はグローブ）。<paramref name="VoxelGloves"/> が false なら
/// どちらの手にも何もはめず、これまでの拳（前腕の箱の当たりと、運動エネルギーで掘る近道。§8）で打つ（比べもの用）。</para>
/// <para><paramref name="Ring"/> はリングの大きさと始めの間。null なら、両方が武器を握る試合は <see cref="RingLayout.Weapons"/>（広いリングで遠間から）、
/// それ以外は <see cref="RingLayout.Boxing"/>。</para>
public sealed record BoxingCorners(string CpuName, ControlFactory Cpu, ControlFactory? Player = null, string? CpuStyleJson = null,
    Weapons.WeaponKind? CpuWeapon = null, Weapons.WeaponKind? PlayerWeapon = null, bool CpuTwoHanded = false, bool PlayerTwoHanded = false, bool VoxelGloves = true,
    Weapons.HandWear PlayerWear = Weapons.HandWear.Glove, Weapons.HandWear CpuWear = Weapons.HandWear.Glove,
    string? PlayerName = null, RingLayout? Ring = null)
{
    /// <summary>ゲームの既定の CPU。</summary>
    public static BoxingCorners Default => Bots.BotCatalog.Corners(Bots.BotCatalog.DefaultCpu);

    /// <summary>この試合のリング。</summary>
    public RingLayout Layout => (Ring ?? (CpuWeapon is not null && PlayerWeapon is not null ? RingLayout.Weapons : RingLayout.Boxing)).Validated();
}

/// <summary>
/// 試合本体（ホスト非依存）: リング・2 体のファイター・CPU・判定・ラウンド進行・描画データ。
/// デスクトップは Aigis の <see cref="GameHost"/> から、VR は OpenXR ホストから同じ <see cref="Step"/> を呼ぶ。
/// <para>物理は内部で約 240Hz にサブステップする（60cm の小さな体を 60Hz・1 サブステップで回すと
/// サーボが発散しやすい／Aigis 調査メモ）。ホストの dt は 60Hz でも 90Hz でもよい。</para>
/// <para>勝敗: 意識が尽きたら KO、倒れて 10 秒立てなければ KO（脚を壊されて立てない場合も）、リングから落ちたら負け。</para>
/// </summary>
public sealed partial class BoxingGame : IGame<BoxingInput>
{
    public const float CountSeconds = 10f;

    /// <summary>
    /// 倒れたまま体がまったく動かない（<see cref="Fighter.StillTime"/>）のがこの秒数続いたら、10 カウントを待たずにその場で K.O.
    /// （審判が試合を止める）。意識が少しでもあれば起き上がろうとする試みで体が動く（試みの間隔 <see cref="FighterTuning.StirPeriodSlow"/> より長い）ので、
    /// 止まるのは死んだ・完全に意識を失ったときだけ。ルールの値（物理量ではない）。
    /// </summary>
    public const float StillKoSeconds = 4f;
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
    private readonly List<GrazeEvent> _recentGrazes = [];
    private readonly List<SeverEvent> _recentSevers = [];
    private readonly Dictionary<(Fighter, Hand), bool> _grazeArmed = [];
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

    /// <param name="tuning">体の調整値。</param>
    /// <param name="seed">試合の種。</param>
    /// <param name="corners">両コーナーの操作元。</param>
    /// <param name="rules">テレビ中継の試合の決まり（<see cref="MatchRules"/>）。null なら従来どおり。</param>
    public BoxingGame(FighterTuning? tuning = null, int seed = 1, BoxingCorners? corners = null, MatchRules? rules = null)
    {
        Rules = rules;
        _tuning = tuning ?? FighterTuning.Default;
        _corners = corners ?? BoxingCorners.Default;
        Layout = _corners.Layout;
        _lighting = ArenaLook.Lighting(Layout.Center, Layout.Size, RingSurfaceHeight);
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

    /// <summary>直近の <see cref="Step"/> で、拳が相手の体をかすめた（皮膚に触れる・ぎりぎりで外れた）瞬間（音のため。試合には響かない）。</summary>
    public IReadOnlyList<GrazeEvent> RecentGrazes => _recentGrazes;

    /// <summary>直近の <see cref="Step"/> で、骨が断たれて部位が切り離された瞬間（音のため）。</summary>
    public IReadOnlyList<SeverEvent> RecentSevers => _recentSevers;

    public string StatusLine => string.Create(CultureInfo.InvariantCulture,
        $"Round {_round}  You {Cpu.Condition.TotalDamage:0} - CPU {Player.Condition.TotalDamage:0} damage  ({PlayerWins}-{CpuWins})");

    public bool IsOver => false;

    /// <summary>拳の試合のリングの中心（<see cref="RingLayout.Boxing"/>。この試合のリングは <see cref="Layout"/>）。</summary>
    public static Vector3 RingCenter => RingLayout.Boxing.Center;

    /// <summary>拳の試合のリングの 1 辺 [m]（<see cref="RingLayout.Boxing"/>。この試合のリングは <see cref="Layout"/>）。</summary>
    public static float RingSize => RingLayout.Boxing.Size;

    public const float RingSurfaceHeight = 0.75f;

    /// <summary>この試合のリング（大きさと始めの間）。</summary>
    public RingLayout Layout { get; }

    private readonly SceneLighting _lighting;

    public void Step(BoxingInput input, float dt)
    {
        RecentBell = _startBellPending ? RoundBell.Start : RoundBell.None;
        _startBellPending = false;
        _recentHits.Clear();
        _recentCuts.Clear();
        WeaponHits.Clear();
        SelfWeaponHits.Clear();
        _recentShatters.Clear();
        _recentLandings.Clear();
        _recentSwings.Clear();
        _recentGrazes.Clear();
        _recentSevers.Clear();
        BrokenBones.Clear();
        _recentContacts.Clear();
        _recentContactBodies.Clear();
        QuietContacts(dt);
        if (input.ResetRequested)
        {
            StartRound();
            return;
        }

        // 1/80 s（0.75 倍速の 1 歩）× 240 が浮動小数で 3.0000002 になり 4 つに割らないよう、わずかに切り下げてから切り上げる。
        var substeps = Math.Max(1, (int)MathF.Ceiling((dt * SubstepRate) - 1e-3f));
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

    /// <summary>
    /// （サブステップごとに調べる: 拳は 1 歩で数 cm 進むので、歩ごとでは 4 mm のすき間を飛び越えてしまう。）
    /// 拳が相手の頭・胴・腰の表面から <see cref="GrazeGap"/> 以内を速く（<see cref="GrazeMinSpeed"/> 以上）通り、打撃に数えられなかった
    /// （当たらなかった・触れただけ）とき、1 回の振りにつき 1 回、かすった音を出す。拳が速さの半分を下回ったら次を待つ。音のためだけ。
    /// </summary>
    private void DetectGrazes()
    {
        if (Resting || RoundOver || Holding)
        {
            return; // 休み・決着の後・ダウンの後の一呼吸は数えない
        }

        foreach (var fighter in Fighters)
        {
            var opponent = fighter == Player ? Cpu : Player;
            foreach (var hand in Hands)
            {
                if (fighter.ArmSevered(hand))
                {
                    continue;
                }

                var glove = fighter.GloveCenter(hand);
                var speed = fighter.GloveVelocity(hand).Length();
                var armed = _grazeArmed.GetValueOrDefault((fighter, hand), true);
                if (armed && speed >= GrazeMinSpeed && _hitCooldown.GetValueOrDefault((fighter, hand)) <= 0f)
                {
                    var gap = MathF.Min(opponent.DistanceToPart(opponent.Parts.Head, glove),
                        MathF.Min(opponent.DistanceToPart(opponent.Parts.Chest, glove), opponent.DistanceToPart(opponent.Parts.Pelvis, glove))) - fighter.GloveRadius;
                    if (gap < GrazeGap)
                    {
                        _recentGrazes.Add(new GrazeEvent(glove, speed));
                        armed = false;
                    }
                }

                _grazeArmed[(fighter, hand)] = armed || speed < 0.5f * GrazeMinSpeed;
            }
        }
    }

    /// <summary>かすったとみなす、拳の表面と相手の体の表面のすき間 [m]（人形の体で 4 mm: 皮膚 1 層ほど）。音のためだけ。</summary>
    private const float GrazeGap = 0.004f;

    /// <summary>かすった音を出す拳の速さ [m/s]（振りの音 SwingSound.MinSpeed より少し下: 押し合いでは鳴らさない）。</summary>
    private const float GrazeMinSpeed = 1f;

    private void Substep(in FighterCommand playerCommand, float dt)
    {
        if (Resting)
        {
            // ラウンドの間（と試合の後）: 自分のコーナーへ歩いて戻り、ガードを下げて休む。入力元（ボット・プレイヤー）は読まない。
            Drive(Player, Cpu, RestCommand(Player, Cpu), dt);
            Drive(Cpu, Player, RestCommand(Cpu, Player), dt);
            _resumeHold = 0f; // 休みに入ったら一呼吸は持ち越さない（次のラウンドはゴングで始まる）
        }
        else if (Holding)
        {
            // ダウンから立ち直った直後の一呼吸（審判が構えを確かめてから試合を戻す）: 両者その場で構えて向き合う。打撃は数えない。
            Drive(Player, Cpu, HoldCommand(Player, Cpu), dt);
            Drive(Cpu, Player, HoldCommand(Cpu, Player), dt);
            _resumeHold = MathF.Max(0f, _resumeHold - dt);
            if (!Holding)
            {
                ShowMessage("BOX!", 0.8f);
            }
        }
        else
        {
            var cpuCommand = _cpuControl.Read(ArmedSense(Cpu, Player), dt);
            Drive(Player, Cpu, _playerControl?.Read(ArmedSense(Player, Cpu), dt) ?? playerCommand, dt);
            Drive(Cpu, Player, cpuCommand, dt);
        }

        foreach (var fighter in Fighters)
        {
            foreach (var hand in Hands)
            {
                _preStepGloveVelocity[(fighter, hand)] = fighter.GloveVelocity(hand);
                _hitCooldown[(fighter, hand)] = MathF.Max(0f, _hitCooldown.GetValueOrDefault((fighter, hand)) - dt);
            }
        }

        RememberBeforeStep();
        CoolWeapons(dt);
        _world.Step(dt);
        DetectHits();
        ResolveWeapons();
        DetectGrazes();
        WatchRecoveries();
    }

    /// <summary>
    /// ダウンから立ち直って構えた後、試合を戻すまでの一呼吸 [s]（審判の判断の値: 2026-11-13、ユーザーの指定「ワンテンポ入れてから再開」）。
    /// 人形の 0.75 倍速の動画で 1.6 秒ほど: 立ち直った体が構えを取り直し、見ている人が「戻った」と分かる間。
    /// </summary>
    public const float ResumePause = 1.2f;

    /// <summary>ダウンから立ち直った後の一呼吸の残り [s]（<see cref="ResumePause"/>）。</summary>
    private float _resumeHold;

    /// <summary>前の物理の歩で、ダウン（審判が数えていた）だった両者。</summary>
    private readonly HashSet<Fighter> _wasCounted = [];

    /// <summary>試合を一呼吸止めているか（ダウンから立ち直った直後）。打撃は数えず、両者その場で構える。</summary>
    public bool Holding => _resumeHold > 0f && !Resting && !RoundOver;

    /// <summary>ダウンの数えから立ち直った（立って構えた）ファイターがいたら、試合を <see cref="ResumePause"/> だけ止める。</summary>
    private void WatchRecoveries()
    {
        foreach (var fighter in Fighters)
        {
            if (_wasCounted.Contains(fighter) && fighter.State == FighterState.Standing && !Resting)
            {
                _resumeHold = ResumePause;
            }

            if (fighter.IsKnockedDown)
            {
                _wasCounted.Add(fighter);
            }
            else
            {
                _wasCounted.Remove(fighter);
            }
        }
    }

    /// <summary>一呼吸の間の構え: その場に立って（歩かない）相手を向き、いつものガードの位置に両手を置く。</summary>
    private static FighterCommand HoldCommand(Fighter self, Fighter opponent) =>
        new(Vector3.Zero, opponent.ChestPosition - self.ChestPosition,
            new HandCommand(AiControl.Guard(Hand.Left, self.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, self.ArmReach), false));

    private void Drive(Fighter self, Fighter opponent, in FighterCommand command, float dt)
    {
        foreach (var hand in Hands)
        {
            if (command.For(hand).LetGo)
            {
                LetGo(self, hand);
            }

            if (self.UpdateGrab(hand, command.For(hand).Grip, opponent) is not null)
            {
                ShowMessage(self == Player ? "THROW!" : "THROWN!", 1f);
            }
        }

        BareFreeHands(self);
        self.ApplyControl(command, dt);
    }

    /// <summary>
    /// 手 <paramref name="hand"/> が握っている武器を放す（<see cref="HandCommand.LetGo"/>）。利き手なら、添えていた手も放す。添えていた手なら、
    /// 利き手は片手で持つ。はめた物（グローブ）は外さない。
    /// </summary>
    private static void LetGo(Fighter self, Hand hand)
    {
        if (self.WearsGlove(hand) || !self.Wields(hand))
        {
            return;
        }

        if (self.WieldedBody(hand) is not null)
        {
            self.Unwield(hand);
        }
        else
        {
            self.LetGoSupport(hand);
        }
    }

    /// <summary>
    /// 何も握っていない・はめていない手（武器を放した・握っていた武器を失った）に、ボクセルの素手の拳をはめる: 拳の打撃はいつも、はめた物と
    /// 相手の体のボクセル同士の接触（圧力のモデル）で起きる（古い、運動エネルギーで掘る拳に戻さない）。拳の試合では両手ともグローブなので何もしない。
    /// </summary>
    private void BareFreeHands(Fighter self)
    {
        if (!_corners.VoxelGloves)
        {
            return;
        }

        foreach (var hand in Hands)
        {
            if (!self.ArmSevered(hand) && !self.Wields(hand) && !self.WearsGlove(hand) && ClearOfDroppedWeapon(self, hand))
            {
                Wear(self, MiniRing.Weapons.HandWear.None);
                return; // Wear は空いている手すべてにはめる
            }
        }
    }

    /// <summary>
    /// 手 <paramref name="hand"/> が、放した武器から離れたか（その武器の格子を囲む球の外 + 1 cm）。放した瞬間の拳の所には武器があるので、
    /// そこに素手の拳を作ると重なったまま始まる（押し出しの跳ねと、重なった所からの接触）。
    /// </summary>
    private bool ClearOfDroppedWeapon(Fighter self, Hand hand)
    {
        if (WeaponOf(self) is not { } weapon)
        {
            return true;
        }

        var s = _world.GetState(weapon.Body.BodyId);
        var center = s.Position + Vector3.Transform(weapon.Body.GridCenterLocal, s.Orientation);
        var radius = weapon.Body.Grid.HalfExtents.Length();
        return Vector3.Distance(center, self.GloveCenter(hand)) > radius + 0.01f;
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
            // ボクセルのグローブをはめた手の打撃は、グローブのボクセルの接触で数える（BoxingGame.Punch）。
            if (attacker.ArmSevered(hand) || attacker.Wields(hand) || attacker.WearsGlove(hand) || attacker.ForearmId(hand) != attackerBody || _hitCooldown[(attacker, hand)] > 0f)
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

    /// <summary>
    /// 体重の乗った打撃が相手へ運ぶ運動量 <paramref name="carried"/>（腕より先の体の質量 × 速さ × 受け渡しの割合。物理の接触が運ぶ
    /// 拳や武器そのものの分は含まない）を、当たった部位と全身へ配る。拳でも、拳が握った武器でも同じ。
    /// <list type="bullet">
    /// <item>部位に残る分（strike）は当たる速さ <paramref name="contactSpeed"/> とともに増える（非線形: 部位に残る運動量 ∝ m v²）。
    /// 速い打撃ほど接触が短く、その間に首（関節のバネ）が体へ逃がせる運動量は限られるので、頭がそのまま持っていかれる。
    /// ゆっくり押し当てる打撃は、関節を通して全身へ配られる。部位に与える速さには上限を置き、超えた分は全身を押す側へ回す。</item>
    /// <item>部位に残る運動量は、当たった点に掛かる: 部位を押し（頭なら首の上でのけぞる）、中心から外れた分だけ回す（あご・こめかみを
    /// 振り抜くと頭が首の上でねじれる）。頭なら、そのねじれと <paramref name="alsoTwist"/>（物理の接触がすでに頭を回した分）を合わせて
    /// 脳しんとう。</item>
    /// <item>反作用は打った側の全身へ（運動量は湧かない）。</item>
    /// </list>
    /// 全身を押す速度（破片を流す向き）を返す。
    /// </summary>
    private Vector3 CarryBody(Fighter attacker, Fighter victim, int victimBody, Vector3 point, Vector3 carried, float contactSpeed, Vector3 alsoTwist)
    {
        var victimMass = victim.AttachedMass;
        var partMass = _world.GetMass(victimBody);
        var strike = ClampLength(carried * StrikeShare(contactSpeed), _tuning.MaxStrikeSpeed * partMass);
        var push = ClampLength((carried - strike) / victimMass, MaxKnockbackSpeed);
        var part = _world.GetState(victimBody);
        var twist = Vector3.Cross(victim.ClosestPointOnPart(victimBody, point) - part.Position, strike);
        _world.ApplyLinearImpulse(victimBody, strike);
        _world.ApplyAngularImpulse(victimBody, twist);
        if (victimBody == victim.Parts.Head)
        {
            victim.TwistHead(twist + alsoTwist); // 頭がねじられた分だけ脳しんとう
        }

        victim.Push(push);
        attacker.Push(-((push * victimMass) + strike) / attacker.AttachedMass);
        return push;
    }

    /// <param name="hit">打撃。</param>
    /// <param name="victimBody">打たれた部位の剛体。</param>
    /// <param name="gloveVelocity">拳の速度（歩の前）。</param>
    /// <param name="carve">拳の運動エネルギーで当たった所を掘るか（素手の拳の近道）。ボクセルのグローブの打撃は、接触そのものが壊した分だけなので false。</param>
    /// <param name="alsoTwist">物理の接触が頭を回した分（グローブのボクセルの接触の力積）。</param>
    internal void ApplyHit(HitEvent hit, int victimBody, Vector3 gloveVelocity, bool carve = true, Vector3 alsoTwist = default)
    {
        if (RoundOver || Resting || Holding)
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
        var extraMass = hit.ImpactMass - hit.Attacker.ArmMass(hit.Hand);
        var push = extraMass > 0f && gloveVelocity.LengthSquared() > 1e-8f
            ? CarryBody(hit.Attacker, victim, victimBody, hit.Point, Vector3.Normalize(gloveVelocity) * (extraMass * hit.Speed * KnockbackTransfer), hit.Speed, alsoTwist)
            : Vector3.Zero;
        if (!carve)
        {
            // グローブの打撃: 壊れたボクセルは接触が散らす（Shatter）。ここは当たった光だけ。
            ImpactFlash(hit.Point, gloveVelocity, HitStrength(hit.Damage), hit.Attacker);
        }


        // 削る量は打撃そのものの強さ（部位の倍率を外したダメージ）。得点ではガードの上は 0.25 倍に割り引くが、
        // 受けた腕にかかる衝撃は同じ: ブロックし続けた腕は削れていく。破片は、のけぞる体の動き（push）に乗って流れる。
        // 削る量は拳の運動エネルギー（½ m v²）に比例: 体重の乗った速い 1 発ほど急に深く掘れる（非線形）。
        var energy = 0.5f * hit.ImpactMass * hit.Speed * hit.Speed * PunchMechanics.Sharpness(hit.Speed);
        // 皮膚の上からでも、衝撃は組織を伝わって臓器を打つ（一時的な打撲。削れれば別に恒久的に効く）。
        victim.BruiseOrgans(hit.Point, energy);
        if (carve)
        {
            Strike(victim, victimBody, hit.Point, gloveVelocity, hit.Damage / ZoneDamage.Multiplier(hit.Zone), hit.Attacker, push, MathF.Min(_tuning.MaxCarve, energy * _tuning.CarvePerJoule));
        }

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
        if (result?.Break is { } broken)
        {
            BreakBone(victim, broken, strength);
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
            if (material.Tissue == VoxelTissue.Organ)
            {
                // 臓器: 紫に光る火花（光の尾を引く）。血しぶきも飛ぶ。
                _debris.Spawn(chip.Position, Fly(1.5f, 0.12f), chip.Orientation, chip.Size * 0.6f, Spin(40f), OrganSpark, life: 0.6f, glow: true, tag: chip.Material, heat: ChipHeat, trail: 1f, ownColor: true);
                for (var k = 0; k < 3; k++)
                {
                    _debris.Spawn(chip.Position, Fly(2f, 0.3f), chip.Orientation, chip.Size * 0.3f, Spin(50f), OrganSpark, life: 0.45f, glow: true, tag: VoxelBreakSound.SparkTag, heat: ChipHeat * 0.7f, trail: 1f, ownColor: true);
                }

                Blood(chip, Fly, Spin);
                continue;
            }

            // 欠片はそのままの色で、その色の光の尾を引く。尾の長さは材質で: 骨は長く（火花ほど）、皮膚・肉・金属・木は短く（その半分）。
            var trail = material.Tissue == VoxelTissue.Bone ? BoneTrail : ChipTrail;
            _debris.Spawn(chip.Position, Fly(1.5f, 0.12f), chip.Orientation, chip.Size * 0.9f, Spin(40f), color, tag: chip.Material, heat: ChipHeat, trail: trail, ownColor: true);
            for (var k = 0; k < 2; k++)
            {
                _debris.Spawn(chip.Position, Fly(1.8f, 0.25f), chip.Orientation, chip.Size * 0.45f, Spin(70f), color, life: 1.6f, tag: chip.Material, heat: ChipHeat * 0.7f, trail: trail, ownColor: true);
            }

            switch (material.Tissue)
            {
                case VoxelTissue.Bone:
                    for (var k = 0; k < 3; k++)
                    {
                        _debris.Spawn(chip.Position, Fly(1.6f, 0.35f), chip.Orientation, chip.Size * 0.25f, Spin(90f), BoneDust, life: 1.2f, tag: VoxelBreakSound.DustTag);
                    }

                    break;
                case VoxelTissue.Solid when chip.Material != BoxerAnatomy.Iron:
                    // 木が潰れると木くず。
                    _debris.Spawn(chip.Position, Fly(1.4f, 0.4f), chip.Orientation, chip.Size * 0.4f, Spin(80f), material.Color, life: 1.4f, tag: VoxelBreakSound.DustTag);
                    break;
                case VoxelTissue.Flesh:
                    Blood(chip, Fly, Spin);
                    break;
            }
        }
    }

    private void Blood(VoxelChip chip, Func<float, float, Vector3> fly, Func<float, Vector3> spin)
    {
        for (var k = 0; k < 3; k++)
        {
            _debris.Spawn(chip.Position, fly(2.1f, 0.3f), chip.Orientation, chip.Size * 0.35f, spin(60f), BloodColor, life: 2.2f, tag: VoxelBreakSound.BloodTag);
        }
    }

    /// <summary>
    /// 金属と金属がぶつかった所の火花: 白く光って黄・赤へ冷める小さな粒が、光の尾を引いて飛ぶ。数と速さは、ぶつかった所の
    /// 近づく速さ <paramref name="speed"/> [m/s] とともに増える（擦れ合う刃が削る熱の量の見え方）。
    /// </summary>
    internal void MetalSparks(Vector3 point, Vector3 normal, Vector3 along, float speed)
    {
        var count = Math.Clamp((int)(speed * SparksPerSpeed), 0, MaxSparks);
        for (var k = 0; k < count; k++)
        {
            var scatter = new Vector3(Jitter(), Jitter(), Jitter());
            var dir = Vector3.Normalize((normal * (0.4f + (0.6f * MathF.Abs(Jitter())))) + (along * 0.6f) + (scatter * 0.7f) + new Vector3(1e-4f));
            var v = dir * (speed * (0.6f + (0.5f * MathF.Abs(Jitter()))));
            _debris.Spawn(point, v, Quaternion.Identity, new Vector3(0.0012f), Vector3.Zero, Spark, life: 0.3f + (0.15f * MathF.Abs(Jitter())), glow: true,
                tag: VoxelBreakSound.SparkTag, heat: 0.3f, trail: BoneTrail);
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
    private static readonly Vector4 OrganSpark = new(0.75f, 0.2f, 1.6f, 1f); // 紫（1 を超える分は光ってにじむ）

    /// <summary>欠片の光の尾の長さ（<see cref="VoxelDebris.TrailSeconds"/> ぶんの何倍か）: 皮膚・肉・金属・木は短く、骨と火花は長く。</summary>
    private const float ChipTrail = 0.5f;
    private const float BoneTrail = 1f;

    /// <summary>金属同士の火花: 近づく速さ 1 m/s あたりの粒の数と、1 度に出す数の上限。</summary>
    private const float SparksPerSpeed = 6f;
    private const int MaxSparks = 40;
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
        if (Rules is { } rules)
        {
            UpdateMatch(rules, dt);
            return;
        }

        if (RoundOver)
        {
            _roundOverTimer -= dt;
            if (_roundOverTimer <= 0f)
            {
                StartRound();
            }

            return;
        }

        CheckFinish();
    }

    /// <summary>決着（死・動かない K.O.・リングアウト・10 カウント）を調べ、ついていればラウンドを終える。</summary>
    private void CheckFinish()
    {
        if (Player.IsDead || Cpu.IsDead)
        {
            var dead = Player.IsDead ? Player : Cpu;
            EndRound(cpuWon: dead == Player, $"K.O. — {dead.DeathCause!.ToUpperInvariant()} DESTROYED");
        }
        else if (Fighters.FirstOrDefault(f => f.StillTime > StillKoSeconds) is { } still)
        {
            EndRound(cpuWon: still == Player, "K.O. — OUT COLD");
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

    // ── テレビ中継の試合（Rules があるとき）: 決まった長さのラウンド・ラウンドの間の休み・判定 ──

    /// <summary>テレビ中継の試合の決まり。null なら従来どおり（時間の無いラウンド、K.O. のたびに作り直す）。</summary>
    public MatchRules? Rules { get; }

    /// <summary>今のラウンド（1 始まり）。</summary>
    public int MatchRound { get; private set; } = 1;

    /// <summary>ラウンドの残り時間 [s]（ラウンド中）。</summary>
    public float RoundLeft { get; private set; }

    /// <summary>ラウンドの間の休みの残り時間 [s]（休み中）。</summary>
    public float BreakLeft { get; private set; }

    /// <summary>ラウンドの間の休みか。</summary>
    public bool InBreak { get; private set; }

    /// <summary>試合が終わった（K.O. か、最終ラウンドの終わりの判定）。</summary>
    public bool MatchOver { get; private set; }

    /// <summary>判定の結果（"YOU WIN BY DECISION" など）。K.O. なら null。</summary>
    public string? Decision { get; private set; }

    /// <summary>休んでいる（ラウンドの間・試合の後）: 入力元を読まず、コーナーへ戻って休む（K.O. の後も打ち続けない）。</summary>
    public bool Resting => InBreak || MatchOver;

    private readonly Dictionary<Fighter, float> _regenBudget = [];

    /// <summary>テレビ中継の試合を 1 歩進める: 決着（K.O. は時計より先に調べる）・時計・ラウンドの終わり・休み・判定。</summary>
    private void UpdateMatch(MatchRules rules, float dt)
    {
        if (MatchOver)
        {
            return;
        }

        if (InBreak)
        {
            BreakLeft -= dt;
            foreach (var fighter in Fighters)
            {
                // 削れた体は一定の速さで治る（RegenVoxelsPerSecond 個/秒）。打撲・スタミナ・脳しんとうはそれぞれの速さで戻る。
                var budget = _regenBudget.GetValueOrDefault(fighter) + (rules.RegenVoxelsPerSecond * dt);
                var whole = (int)budget;
                if (whole > 0)
                {
                    fighter.Voxels?.Regenerate(whole);
                }

                _regenBudget[fighter] = budget - whole;
            }

            if (BreakLeft <= 0f)
            {
                InBreak = false;
                MatchRound++;
                RoundLeft = rules.RoundSeconds;
                foreach (var fighter in Fighters)
                {
                    fighter.ResetCount(); // 倒れたまま休みに入っても、次のラウンドで数え直す
                }

                RecentBell = RoundBell.Start;
                ShowMessage($"ROUND {MatchRound}", 1.5f);
            }

            return;
        }

        if (!RoundOver)
        {
            CheckFinish(); // 時計が切れるのと同じ歩の K.O. を落とさない
        }

        if (RoundOver)
        {
            MatchOver = true; // K.O. で試合が終わる（次のラウンドへ作り直さない）
            return;
        }

        RoundLeft -= dt;
        if (RoundLeft > 0f)
        {
            return;
        }

        RoundLeft = 0f;
        RecentBell = RoundBell.End;
        if (MatchRound >= rules.Rounds)
        {
            // 判定: 与えたダメージ（得点）の多い方。差がわずかなら引き分け。
            var player = Cpu.Condition.TotalDamage;
            var cpu = Player.Condition.TotalDamage;
            var draw = MathF.Abs(player - cpu) <= MathF.Max(rules.DrawMinimum, rules.DrawShare * (player + cpu) * 0.5f);
            Decision = draw ? "DRAW" : player > cpu ? "YOU WIN BY DECISION" : "CPU WINS BY DECISION";
            if (!draw)
            {
                if (player > cpu)
                {
                    PlayerWins++;
                }
                else
                {
                    CpuWins++;
                }
            }

            MatchOver = true;
            ShowMessage(Decision, 1e6f);
            return;
        }

        InBreak = true;
        BreakLeft = rules.BreakSeconds;
        ShowMessage($"END OF ROUND {MatchRound}", 2f);
    }

    /// <summary>休むときの指令: 自分のコーナー（赤は手前左、青は奥右）へ歩き、相手の方を向いて、ガードを下げる。</summary>
    private FighterCommand RestCommand(Fighter self, Fighter opponent)
    {
        var rules = Rules ?? MatchRules.Tv;
        var inset = (Layout.Size * 0.5f) - rules.CornerInset;
        var corner = Layout.Center + (self == Player ? new Vector3(-inset, 0f, -inset) : new Vector3(inset, 0f, inset));
        var toCorner = corner - self.PelvisPosition;
        toCorner.Y = 0f;
        var distance = toCorner.Length();
        var move = distance > rules.CornerArrive ? Vector3.Normalize(toCorner) * MathF.Min(1f, distance / rules.CornerSlowDown) * rules.RestWalk : Vector3.Zero;
        var low = rules.RestGuardDrop * self.ArmReach;
        return new FighterCommand(move, opponent.ChestPosition - self.ChestPosition,
            new HandCommand(AiControl.Guard(Hand.Left, self.ArmReach) + low, false),
            new HandCommand(AiControl.Guard(Hand.Right, self.ArmReach) + low, false));
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
        RecentBell = RoundBell.End;
        ShowMessage($"{reason}  {(cpuWon ? "CPU WINS" : "YOU WIN")}", RoundResetDelay);
    }

    /// <summary>
    /// ラウンドを作り直す。Aigis では剛体の削除がハンドル再利用で危険（architecture.md risk 4）なので、
    /// ワールドごと作り直す（体の状態もラウンドごとに新品）。
    /// </summary>
    private void StartRound()
    {
        _world?.Dispose();
        _weapons.Clear(); // 武器は世界ごと消える
        _recentCuts.Clear();
        _beforeStep.Clear();
        _resumeHold = 0f;
        _wasCounted.Clear();
        _round++;
        _world = new PhysicsWorld();
        LivingRoom.AddColliders(_world, Layout);
        _ring = new Ring(_world, Layout.Center, Layout.Size, RingSurfaceHeight, LivingRoom.StageBase);
        var surface = _ring.SurfaceCenter;
        var apart = new Vector3(0f, 0f, Layout.StartGap * 0.5f);
        // VR のプレイヤーには、手触りの調整が終わるまでスタミナの疲れを効かせない（FighterTuning.PlayerFatigue）。
        Player = Fighter.Spawn(_world, _tuning.PlayerFatigue ? _tuning : _tuning with { TiredStrength = 1f, TiredHandSlowness = 0f }, "player", surface - apart, 0f);
        Cpu = Fighter.Spawn(_world, _tuning, "cpu", surface + apart, MathF.PI);
        PlayerSkin = new BoxerSkin(_model, Player, _tuning);
        // 体の形はローポリのモデル（頭・胸・腕の形）。その内側をボクセルで埋める。トランクスはグローブと同じコーナーの色。
        // 顔はボットのリングネームから（同じボットはいつも同じ顔）。人が操る赤コーナーは名前が無ければ既定の顔。
        Player.Voxels = new BoxerBody(Player, _world, _tuning, _model, Outfit.RedCorner, BoxerVoxelOverrides.Shipped,
            _corners.PlayerName is { } redName ? FaceLook.For(Bots.BotNames.RingNameOf(redName)) : null);
        Cpu.Voxels = new BoxerBody(Cpu, _world, _tuning, _model, Outfit.BlueCorner, BoxerVoxelOverrides.Shipped,
            FaceLook.For(Bots.BotNames.RingNameOf(_corners.CpuName)));
        // 破片はリングの上ならリングの面、その外は箱庭の台座・ローテーブルの天板、さらに外は床で止まる。
        _debris.Clear();
        _impacts.Clear();
        _wounds.Clear();
        _debris.GroundAt = p => LivingRoom.GroundAt(p, Layout, RingSurfaceHeight);
        _debris.Gravity = 9.81f * _tuning.DebrisGravityScale;
        CpuSkin = new BoxerSkin(_model, Cpu, _tuning);
        _fighters = [Player, Cpu];
        _wielders.Clear();
        _reach.Clear();
        _fullReach.Clear();
        _wieldHand.Clear();
        _weaponCooldown.Clear();
        _selfCooldown.Clear();
        if (_corners.PlayerWeapon is { } playerWeapon)
        {
            Arm(Player, playerWeapon, _corners.PlayerTwoHanded);
        }

        if (_corners.CpuWeapon is { } cpuWeapon)
        {
            Arm(Cpu, cpuWeapon, _corners.CpuTwoHanded);
        }

        if (_corners.VoxelGloves)
        {
            Wear(Player, _corners.PlayerWear);
            Wear(Cpu, _corners.CpuWear);
        }

        // 前のラウンドの操作元から、覚えたことを引き継がせる（学ぶボット。IFighterControl.CarryOver）。
        var previousCpu = _round > 1 ? _cpuControl : null;
        var previousPlayer = _playerControl;
        _cpuControl = _corners.Cpu(_seed + _round);
        _playerControl = _corners.Player?.Invoke((_seed * 7919) + _round);
        if (previousCpu is not null)
        {
            _cpuControl.CarryOver(previousCpu);
        }

        if (previousPlayer is not null)
        {
            _playerControl?.CarryOver(previousPlayer);
        }
        _roundOverTimer = 0f;
        LastHit = null;
        _preStepGloveVelocity.Clear();
        _hitCooldown.Clear();
        ShowMessage(Rules is null ? "FIGHT!" : "ROUND 1", 1.5f);
        _startBellPending = true;
        _regenBudget.Clear();
        _grazeArmed.Clear();
        MatchRound = 1;
        RoundLeft = Rules?.RoundSeconds ?? 0f;
        InBreak = false;
        MatchOver = false;
        Decision = null;
    }

    /// <summary>
    /// 直近の <see cref="Step"/> で鳴ったゴング（ホストの音用。試合には響かない）。ラウンドの始まりは、そのラウンドの最初の <see cref="Step"/> で
    /// <see cref="RoundBell.Start"/>、終わり（K.O.・リングアウト）は終わった <see cref="Step"/> で <see cref="RoundBell.End"/>。
    /// </summary>
    public RoundBell RecentBell { get; private set; }

    private bool _startBellPending;

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
            self.Faintness, OpponentTargets: opponent.AsTarget, SelfStamina: self.Stamina);
    }

    public FighterSense PlayerSense => ArmedSense(Player, Cpu);

    /// <summary>CPU の入力元が今見ている物（調べもの・テスト用）。</summary>
    public FighterSense CpuSense => ArmedSense(Cpu, Player);

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
        ArenaLook.WriteAtmosphere(Layout.Center, Layout.Size, RingSurfaceHeight, _lookTime, _renderParticles);
        _impacts.WriteParticles(_renderParticles);
        _debris.WriteTrails(_renderParticles);
        _wounds.WriteHalos(_renderParticles);
        return _renderParticles;
    }

    /// <summary>会場の照明（<see cref="ArenaLook"/>）。</summary>
    public SceneLighting BuildLighting() => _lighting;

    /// <summary>リングに落ちる影。</summary>
    public ShadowMapConfig? BuildShadow() => ArenaLook.Shadow(Layout.Center, Layout.Size, RingSurfaceHeight);

    /// <summary>画面用の仕上げ（光のにじみ・陰り・コントラスト）。</summary>
    public PostProcessSettings? BuildPostProcess() => ArenaLook.ScreenFinish;

    /// <summary>
    /// 両ボクサーの顔の窓（<see cref="MiniRing.FaceCam"/>）を出すか。描画だけの設定で、試合の計算・記録には響かない。
    /// ここでは窓の縁（HUD の図形）だけを描く。窓の中身はホストが <see cref="FaceCam.Views"/> で重ねる。
    /// </summary>
    public FaceCam? FaceCam { get; set; }

    /// <summary>ホストが試合を進めている速さ（<see cref="GameSpeed"/>）。HUD に出すだけで、試合の計算には響かない（速さはホストが 1 歩の時間で決める）。</summary>
    public GameSpeed? Speed { get; set; }

    /// <summary>HUD の図形: スタミナのメーター（テレビ中継なら得点表と時計）、10 カウントの目盛り、顔の窓の縁（<see cref="FaceCam"/> があるときだけ）。</summary>
    public IReadOnlyList<UiShapeDraw> BuildOverlayShapes(Matrix4x4 viewProjection, Vector2 screenSizePixels)
        => [
            .. Rules is not null
                ? TvHud.Shapes(this, screenSizePixels)
                : [.. TvHud.StaminaBar(new Vector2(24f, 102f), StaminaBarWidth, 8f, Player),
                   .. TvHud.StaminaBar(new Vector2(screenSizePixels.X - 24f - StaminaBarWidth, 102f), StaminaBarWidth, 8f, Cpu)],
            .. DownCountHud.Shapes(this, viewProjection, screenSizePixels),
            .. FaceCam?.Frames(this, screenSizePixels) ?? [],
        ];

    /// <summary>名前の下のスタミナのメーターの幅 [px]。</summary>
    private const float StaminaBarWidth = 130f;

    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 viewProjection, Vector2 screenSizePixels)
    {
        var white = new Vector4(1f, 1f, 1f, 1f);
        var list = new List<ScreenTextDraw>();
        if (Rules is not null)
        {
            list.AddRange(TvHud.Text(this, screenSizePixels, "YOU", "CPU")); // テレビ中継: 得点表と時計
        }
        else
        {
            list.Add(new($"{PlayerWins} - {CpuWins}", new Vector2(screenSizePixels.X * 0.5f, 20f), 28f, white, TextAnchor.Center));
            AddCondition(list, "YOU", Cpu, new Vector2(24f, 20f), TextAnchor.Left, new Vector4(1f, 0.45f, 0.4f, 1f));
            AddCondition(list, "CPU", Player, new Vector2(screenSizePixels.X - 24f, 20f), TextAnchor.Right, new Vector4(0.5f, 0.65f, 1f, 1f));
        }

        if (LastHit is { } hit)
        {
            var who = hit.Attacker == Player ? "YOU" : "CPU";
            var text = $"{who} {(hit.Hand == Hand.Left ? "L" : "R")} > {HitZones.Label(hit.Zone)}  {hit.Damage:0}  " +
                $"{hit.ImpactMass:0.0}kg x {hit.Speed:0.0}m/s  BODY {Percent(hit.BodyShare)}";
            list.Add(new ScreenTextDraw(text, new Vector2(screenSizePixels.X * 0.5f, screenSizePixels.Y - 44f), 22f, white, TextAnchor.Center));
        }

        list.AddRange(DownCountHud.Text(this, viewProjection, screenSizePixels, f => f == Player ? "YOU" : "CPU",
            f => f == Player ? new Vector4(1f, 0.45f, 0.4f, 1f) : new Vector4(0.5f, 0.65f, 1f, 1f)));

        if (Speed is { } speed)
        {
            list.Add(new ScreenTextDraw($"{speed.Label}  [1] 0.5x  [2] 0.75x  [3] 1.0x", new Vector2(24f, screenSizePixels.Y - 20f), 18f,
                new Vector4(0.85f, 0.85f, 0.85f, 0.8f), TextAnchor.Left));
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

/// <summary>ラウンドのゴング: 鳴らない・始まり・終わり。</summary>
public enum RoundBell
{
    None,
    Start,
    End,
}

/// <summary>拳が相手の体をかすめた（皮膚に触れる・ぎりぎりで外れた）瞬間。<paramref name="Speed"/> は拳の速さ [m/s]。</summary>
public readonly record struct GrazeEvent(Vector3 Position, float Speed);

/// <summary>骨が断たれて部位が切り離された瞬間。<paramref name="Strength"/> は打撃の強さ（0..1）。</summary>
public readonly record struct SeverEvent(Vector3 Position, float Strength);

/// <summary>骨が折れて、肉だけでつながった（<paramref name="Bent"/> なら打撃の曲げで、でなければ削られて断たれた）。</summary>
public readonly record struct BoneBreakEvent(Fighter Victim, BodyPartKind Part, Vector3 Position, bool Bent);

/// <summary>
/// ボクセルの物どうしが、何も壊さずにぶつかった瞬間（2026-11-12）。<paramref name="Pair"/> はぶつかった面の材質の組
/// （金属どうし・グローブと皮膚など）、<paramref name="Strength"/> は近づく速さから決めた強さ（0..1）。
/// </summary>
public readonly record struct ContactSoundEvent(Vector3 Position, Audio.ContactPair Pair, float Strength);

/// <summary>
/// テレビ中継の試合の決まり（2026-11-12）: 1 ラウンド <paramref name="RoundSeconds"/> 秒 × <paramref name="Rounds"/> ラウンド、ラウンドの間
/// <paramref name="BreakSeconds"/> 秒。休みの間、削れた体は一定の速さ（<paramref name="RegenVoxelsPerSecond"/> 個/秒）で内側から治る。
/// K.O. で試合が終わり、最終ラウンドの終わりは判定（与えたダメージ）。
/// </summary>
/// <param name="RoundSeconds">1 ラウンドの長さ [s]。</param>
/// <param name="Rounds">ラウンドの数。</param>
/// <param name="BreakSeconds">ラウンドの間の休み [s]。</param>
/// <param name="RegenVoxelsPerSecond">休みの間に体が治る速さ [ボクセル/s]（1 人あたり。重い 1 発で削れる数十〜百個を 1 秒ほどで）。</param>
public sealed record MatchRules(float RoundSeconds = 30f, int Rounds = 5, float BreakSeconds = 10f, float RegenVoxelsPerSecond = 150f)
{
    /// <summary>試合の長さの合計 [s]（全ラウンドと休み）。</summary>
    public float TotalSeconds => (RoundSeconds * Rounds) + (BreakSeconds * Math.Max(0, Rounds - 1));

    /// <summary>判定で引き分けとする、与えたダメージの差の割合（両者の平均に対して）。</summary>
    public float DrawShare { get; init; } = 0.05f;

    /// <summary>判定で引き分けとする、与えたダメージの差の下限（どちらもほとんど当てていないとき）。</summary>
    public float DrawMinimum { get; init; } = 1f;

    /// <summary>休むコーナーの、リングの縁からの内側の距離 [m]（ロープとコーナーポストに触れない所）。</summary>
    public float CornerInset { get; init; } = 0.07f;

    /// <summary>コーナーに着いたとみなす距離 [m]（それより近ければ歩かない）。</summary>
    public float CornerArrive { get; init; } = 0.03f;

    /// <summary>コーナーの手前で歩みを緩め始める距離 [m]。</summary>
    public float CornerSlowDown { get; init; } = 0.1f;

    /// <summary>コーナーへ歩く速さ（移動指令の大きさ 0..1。疲れた足取り）。</summary>
    public float RestWalk { get; init; } = 0.6f;

    /// <summary>休むときに拳を構えから下げる量（腕の長さ単位: 下へ・前へ）。</summary>
    public Vector3 RestGuardDrop { get; init; } = new(0f, -0.12f, 0.15f);

    /// <summary>テレビ中継の標準: 30 秒 × 5 ラウンド、休み 10 秒。</summary>
    public static MatchRules Tv { get; } = new();
}
