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
public readonly record struct BoxingInput(FighterCommand Player, bool ResetRequested = false, FighterCommand? Blue = null);

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
/// <para><paramref name="Ring"/> はリングの大きさと始めの立ち位置。null なら、ドリル・調べものの場（両方が武器を握る試合は <see cref="RingLayout.DrillWeapons"/>、
/// それ以外は <see cref="RingLayout.DrillBoxing"/>。中央から）。試合のホストは <see cref="StartingAtCorners"/> で標準のリング（人の大きさで 7.3 m）・コーナーからにする。</para>
/// <para><paramref name="PlayerLook"/> / <paramref name="CpuLook"/> は名簿のキャラクター（<see cref="Characters.FighterCharacter"/>）の顔そのもの（肌・髪・瞳・髪型・眉・ひげ・顔の部位）。
/// null なら従来どおり名前（<see cref="PlayerName"/> / <see cref="CpuName"/>）から決めた顔。<paramref name="PlayerLabel"/> / <paramref name="CpuLabel"/> は
/// 画面（得点・状態・実況）に出す名前（null なら YOU / CPU や RED / BLUE）。</para>
/// <para><paramref name="PlayerStyle"/> は赤（プレイヤー側）のスタイル（名簿のキャラクターなら教育を重ねた解決済みの全体。<see cref="Characters.FighterCharacter.Resolve(string)"/>）。
/// 人が操る赤の意図の解釈（技の選び方・倒れた相手への攻めの好み）に渡す。赤がボットなら <paramref name="RedBotStyle"/> が同じ物を持つ。null ならスタイルを持たない赤（既定の拳か空手）。
/// 名前（<paramref name="PlayerName"/> / <paramref name="CpuName"/>）で <see cref="Bots.BotCatalog"/> を引き直さない: 解決したスタイルをこの値・<paramref name="CpuStyleJson"/> で通す。</para>
/// <para><paramref name="BreakActions"/>（既定 true）は、ラウンドの間の休みに選手が動作（水を飲む・汗を拭く・電話・深呼吸…、<see cref="Break.BreakRoutine"/>）をするか。false なら休みはコーナーで構えを下げて立つだけ。
/// <paramref name="BreakSource"/> は休みの入力元の差し替え（種と選手から作る。null なら <see cref="Break.BreakRoutine"/>。null を返すとその選手は動作をしない）。
/// 試合の始め・終わりの動作（<see cref="MatchRules.IntroSeconds"/>・<see cref="MatchRules.EndSeconds"/>）も同じ <see cref="BreakActions"/> で切り替わるが、差し替え口は休みの間だけに使う。
/// <paramref name="SettleSeconds"/>（既定 0）は、世界を作った直後に両者を構えさせて回す助走の長さ [s]（映像の始めで T 字の姿勢を映さないため。試合の時計は進めない）。</para>
/// <para><paramref name="Referee"/> はレフェリーがいるか（既定はいる）。いないときもダウンは数える（10 カウントで K.O.）が、数えている間の攻撃も打撃に数え、立ち直った後の一呼吸も無い（ゲームの決まり。2026-11-16、ユーザーの指定）。</para>
public sealed record BoxingCorners(string CpuName, ControlFactory Cpu, ControlFactory? Player = null, string? CpuStyleJson = null,
    Weapons.WeaponKind? CpuWeapon = null, Weapons.WeaponKind? PlayerWeapon = null, bool CpuTwoHanded = false, bool PlayerTwoHanded = false, bool VoxelGloves = true,
    Weapons.HandWear PlayerWear = Weapons.HandWear.Glove, Weapons.HandWear CpuWear = Weapons.HandWear.Glove,
    string? PlayerName = null, RingLayout? Ring = null, BodyBuild? PlayerBody = null, BodyBuild? CpuBody = null,
    FaceParts? PlayerFace = null, FaceParts? CpuFace = null, bool ExternalBlue = false, Bots.BotStyle? RedBotStyle = null, bool UseWeaponBots = false,
    bool Referee = true, Voxels.FaceLook? PlayerLook = null, Voxels.FaceLook? CpuLook = null, string? PlayerLabel = null, string? CpuLabel = null,
    Bots.BotStyle? PlayerStyle = null, Bots.WeaponStyle? PlayerWeaponStyle = null, Bots.WeaponStyle? CpuWeaponStyle = null,
    bool BreakActions = true, Func<int, Fighter, Break.IBreakSource?>? BreakSource = null, float SettleSeconds = 0f,
    Voxels.FaceSpec? PlayerFaceSpec = null, Voxels.FaceSpec? CpuFaceSpec = null)
{
    /// <summary>CPU 側のスタイル（<see cref="CpuStyleJson"/> を読んだもの。解決済みの全体）。スタイルを持たない CPU（legacy・外の入力）は null。</summary>
    public Bots.BotStyle? CpuStyle => CpuStyleJson is { } json ? Bots.BotStyle.FromJson(json) : null;

    /// <summary>ゲームの既定の CPU。</summary>
    public static BoxingCorners Default => Bots.BotCatalog.Corners(Bots.BotCatalog.DefaultCpu);

    /// <summary>
    /// 同じ試合を、両者のコーナーから始める形（2026-11-18、ユーザーの指定「試合の開始はコーナーから」）。試合のホスト（デスクトップ・VR・動画・テレビ中継）が使う。
    /// リングを <see cref="Ring"/> に書くので、記録（<see cref="Replay.ReplayMatchSettings"/>）にも残り、再生は同じ所から始まる。
    /// </summary>
    public BoxingCorners StartingAtCorners() => this with { Ring = (Ring ?? (BothArmed ? RingLayout.Weapons : RingLayout.Boxing)).AtCorners() };

    private bool BothArmed => CpuWeapon is not null && PlayerWeapon is not null;

    /// <summary>この試合のリング。</summary>
    public RingLayout Layout => (Ring ?? (BothArmed ? RingLayout.DrillWeapons : RingLayout.DrillBoxing)).Validated();
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
    /// <summary>調べもの用（スレッドごと）: 試合の決まり（<see cref="MatchRules.LimbBendStrengthScale"/>）が無いとき、腕・脚の骨の曲げ強さに掛ける倍率（0 以下は 1）。骨折の頻度の測定（FractureRateProbe）。</summary>
    [ThreadStatic]
    internal static float LimbBendProbeScale;

    /// <summary>10 カウントの長さ [s]（試合の時間。人形の単位の 10 s を実物大の時間へ ×√L）。数える数は <see cref="CountNumbers"/>、1 つの間は <see cref="CountInterval"/>。</summary>
    public const float CountSeconds = 16.78f;

    /// <summary>ダウンで数える数（10 カウント）。</summary>
    public const int CountNumbers = 10;

    /// <summary>カウントの 1 つの間 [s]（試合の時間。<see cref="CountSeconds"/> / <see cref="CountNumbers"/>）。</summary>
    public const float CountInterval = CountSeconds / CountNumbers;

    /// <summary>
    /// 倒れたまま体がまったく動かない（<see cref="Fighter.StillTime"/>）のがこの秒数続いたら、10 カウントを待たずにその場で K.O.
    /// （審判が試合を止める）。意識が少しでもあれば起き上がろうとする試みで体が動く（試みの間隔 <see cref="FighterTuning.StirPeriodSlow"/> より長い）ので、
    /// 止まるのは死んだ・完全に意識を失ったときだけ。ルールの値（物理量ではない。試合の時間なので実物大の時間: 人形の 4 s × √L）。
    /// </summary>
    public const float StillKoSeconds = 6.714f;
    /// <summary>物理のサブステップの速さ [Hz]。ホストの dt をこの速さの刻みに割る（既定は <see cref="DefaultSubstepRate"/> = 240）。試合ごとに変えられる（簡素化の測定用: 120・180）。ホストの dt がこの刻みの整数倍のときだけ割り切れる。記録の見出しには入らないので、既定以外で撮った記録は既定では再現しない。</summary>
    public float SubstepRate { get; set; } = DefaultSubstepRate;

    /// <summary><see cref="SubstepRate"/> の既定（240 Hz）。簡素化の測定が試合を作る前に変える。
    /// 非同期の流れごとに持つ（<see cref="System.Threading.AsyncLocal{T}"/>）: 変えたコードと、その後に始めたスレッド・タスクだけに効き、並行して走っている別の試合・テストには漏れない。</summary>
    public static float DefaultSubstepRate
    {
        get => _defaultSubstepRate.Value ?? 240f;
        set => _defaultSubstepRate.Value = value;
    }

    private static readonly System.Threading.AsyncLocal<float?> _defaultSubstepRate = new();

    private const float RoundResetDelay = 5.874f;
    private const float RehitCooldown = 0.3021f;
    private const float MinimumDamage = 1f;

    /// <summary>拳に乗った質量のうち、物理の接触では伝わらない体の分を相手へ押し込む割合（反発を含めた運動量の受け渡し）。</summary>
    private const float KnockbackTransfer = 0.6f;

    /// <summary>1 発で相手の全身に与える速度変化の上限 [m/s]（吹き飛ばしすぎない）。</summary>
    private const float MaxKnockbackSpeed = 1.007f;

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
    /// <summary>両者の体の部位の、この物理の歩の前の状態（打たれた点の速度から当たる速さを相対速度で出す: <see cref="PunchMechanics.ClosingSpeed"/>）。</summary>
    private readonly Dictionary<int, BodyState> _preStepBodies = [];
    private readonly Dictionary<(Fighter, Hand), float> _hitCooldown = [];

    /// <summary>試合の物理の時計 [s]（サブステップの和。決定的）。</summary>
    private float _time;

    /// <summary>相手に掴まれていた最後の時刻（投げの見出し用）。</summary>
    private readonly Dictionary<Fighter, float> _heldAt = [];

    /// <summary>前の歩で立っていたか（投げの見出し用）。</summary>
    private readonly Dictionary<Fighter, bool> _wasStanding = [];

    /// <summary>離された後、この時間 [s] 以内に倒れたら投げとみなす（見出しと記録だけ。物理は分けない）。</summary>
    public const float ThrowWindow = 1.343f;

    /// <summary>投げが決まった（掴んでいた側, 倒れた側）。見出し・ドリルの記録用。</summary>
    public event Action<Fighter, Fighter>? Thrown;
    /// <summary>蹴りの打ち直しまでの間（脚ごと。拳の <see cref="_hitCooldown"/> と同じ長さ）。</summary>
    private readonly Dictionary<(Fighter, Hand), float> _kickCooldown = [];
    private readonly Dictionary<(Fighter, Hand), float> _elbowCooldown = [];
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
    /// <param name="mood">会場の雰囲気（照明と画面の仕上げ。描画だけで試合に響かない）。null なら今の明るい部屋。</param>
    public BoxingGame(FighterTuning? tuning = null, int seed = 1, BoxingCorners? corners = null, MatchRules? rules = null, ArenaMood? mood = null)
    {
        if (rules is not null && (!float.IsFinite(rules.ConcussionSpinScale) || rules.ConcussionSpinScale <= 0f
            || !float.IsFinite(rules.LimbBendStrengthScale) || rules.LimbBendStrengthScale <= 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(rules), "Impact response scales must be finite and positive.");
        }

        Rules = rules;
        Mood = mood ?? ArenaMood.Default;
        _tuning = tuning ?? FighterTuning.Default;
        _corners = corners ?? BoxingCorners.Default;
        Layout = _corners.Layout;
        _lighting = ArenaLook.Lighting(Display.Point(Layout.Center), Display.Length(Layout.Size), Display.Length(RingSurfaceHeight), mood); // 部屋の照明は表示の空間
        _screenFinish = mood is null || mood == ArenaMood.Default ? ArenaLook.ScreenFinish : ArenaLook.ScreenFinishFor(mood);
        _fx = new Random(seed ^ 0x5eed);
        _model = Models.GetOrAdd((_tuning.HalfExtents, _tuning.ModelSubdivisions), key => BoxerModel.Build(key.Item1, key.Item2));
        _seed = seed;
        StartRound();
        if (_corners.SettleSeconds > 0f)
        {
            Settle(_corners.SettleSeconds);
        }
    }

    /// <summary>会場の雰囲気（描画だけ。窓の外の空の明るさにも使う）。</summary>
    public ArenaMood Mood { get; }

    public PhysicsWorld World => _world;

    /// <summary>CPU の乱数の種（リプレイで同じ試合を作り直すため）。</summary>
    public int Seed => _seed;

    /// <summary>ファイターの調整値（リプレイで同じ試合を作り直すため）。</summary>
    public FighterTuning Tuning => _tuning;

    /// <summary>CPU 側のボットの名前（リプレイで同じ相手を作り直すため）。</summary>
    public string CpuName => _corners.CpuName;

    /// <summary>CPU 側のボットのスタイル（JSON）。legacy や外から渡した操作元なら null。</summary>
    public string? CpuStyleJson => _corners.CpuStyleJson;

    internal BoxingCorners InitialCorners => _corners;

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

    private bool HasTwoNamedCorners => _corners.ExternalBlue || _corners.RedBotStyle is not null;
    public string RedLabel => _corners.PlayerLabel ?? (HasTwoNamedCorners ? "RED" : "YOU");
    public string BlueLabel => _corners.CpuLabel ?? (HasTwoNamedCorners ? "BLUE" : "CPU");

    public string StatusLine => string.Create(CultureInfo.InvariantCulture,
        $"Round {_round}  {RedLabel} {Cpu.Condition.TotalDamage:0} - {BlueLabel} {Player.Condition.TotalDamage:0} damage  ({PlayerWins}-{CpuWins})");

    public bool IsOver => false;

    /// <summary>リングを指定しない拳の試合（ドリルの場 <see cref="RingLayout.DrillBoxing"/>）のリングの中心。この試合のリングは <see cref="Layout"/>。</summary>
    public static Vector3 RingCenter => RingLayout.DrillBoxing.Center;

    /// <summary>リングを指定しない拳の試合（ドリルの場 <see cref="RingLayout.DrillBoxing"/>）のリングの 1 辺 [m]。この試合のリングは <see cref="Layout"/>。</summary>
    public static float RingSize => RingLayout.DrillBoxing.Size;

    public const float RingSurfaceHeight = 2.113f;

    /// <summary>この試合のリング（大きさと始めの間）。</summary>
    public RingLayout Layout { get; }

    private readonly SceneLighting _lighting;
    private readonly PostProcessSettings _screenFinish;

    public void Step(BoxingInput input, float dt)
    {
        if (_settling)
        {
            SettleStep(dt);
            return;
        }

        RecentBell = _startBellPending ? RoundBell.Start : RoundBell.None;
        _startBellPending = false;
        _recentHits.Clear();
        _recentCuts.Clear();
        WeaponHits.Clear();
        SelfWeaponHits.Clear();
        SelfTouches.Clear();
        _recentShatters.Clear();
        _recentLandings.Clear();
        _recentSwings.Clear();
        _recentGrazes.Clear();
        _recentSevers.Clear();
        RecentSupplies.Clear();
        RecentRegrowths.Clear();
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
            Substep(input.Player, input.Blue, sub);
        }

        var postStart = StepProfile.Begin();
        _messageTimer = MathF.Max(0f, _messageTimer - dt);
        _debris.Step(dt, _recentLandings);
        _impacts.Step(dt);
        _wounds.Step(dt);
        _lookTime += dt;
        DetectSwings();
        UpdateRound(dt);
        StepProfile.End(StepProfile.Slot.PostStep, postStart);
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
    /// （サブステップごとに調べる: 拳は 1 歩で数 cm 進むので、歩ごとでは 11 mm のすき間を飛び越えてしまう。）
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

    /// <summary>かすったとみなす、拳の表面と相手の体の表面のすき間 [m]（約 11 mm: 皮膚 1 層ほど）。音のためだけ。</summary>
    private const float GrazeGap = 0.01127f;

    /// <summary>かすった音を出す拳の速さ [m/s]（振りの音 SwingSound.MinSpeed より少し下: 押し合いでは鳴らさない）。</summary>
    private const float GrazeMinSpeed = 1.678f;

    private void Substep(in FighterCommand playerCommand, FighterCommand? blue, float dt)
    {
        _time += dt;
        var controlStart = StepProfile.Begin();
        var perceptionStart = StepProfile.Begin();
        UpdatePerception(dt);
        StepProfile.End(StepProfile.Slot.Perception, perceptionStart);
        if (!Counting)
        {
            _neutralCorners.Clear();
        }

        var live = false; // 入力元が試合を動かしている歩（休み・ダウンの数え・一呼吸の間ではない）

        if (_stagePose is { } stage)
        {
            // 試合の前の場面のための影の試合（StepStage）: ボットも入力も読まず、構えて立つ指令だけ。
            Drive(Player, Cpu, StageCommand(Player, Cpu, stage), dt);
            Drive(Cpu, Player, StageCommand(Cpu, Player, stage), dt);
        }
        else if (_settling)
        {
            // 生成直後の姿勢（腕を真横に広げた形）を映さないための助走: 両者その場で構えるだけ。試合の時計・打撃・入力元の読みは進めない。
            Drive(Player, Cpu, HoldCommand(Player, Cpu), dt);
            Drive(Cpu, Player, HoldCommand(Cpu, Player), dt);
        }
        else if (Resting)
        {
            // ラウンドの間（と試合の後）: 自分のコーナーへ歩いて戻り、ガードを下げて休む。入力元（ボット・プレイヤー）は読まない。
            // 休みの間（試合の後は除く）は、休みの入力元（水を飲む・汗を拭く…の目標の経路）が休みの構えへ重ねる。
            Drive(Player, Cpu, BreakCommand(Player, Cpu, dt), dt);
            Drive(Cpu, Player, BreakCommand(Cpu, Player, dt), dt);
            _resumeHold = 0f; // 休みに入ったら一呼吸は持ち越さない（次のラウンドはゴングで始まる）
        }
        else if (Counting)
        {
            // ダウンを数えている間（レフェリーのいる試合）: 倒れた側はその場で立ち、立っている側はニュートラルコーナーで待つ。入力元は読まない。
            Drive(Player, Cpu, DownCommand(Player, Cpu), dt);
            Drive(Cpu, Player, DownCommand(Cpu, Player), dt);
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
            live = true;
            EndBreakActions();
            var readStart = StepProfile.Begin();
            var cpuCommand = _corners.ExternalBlue ? blue.GetValueOrDefault() : _cpuControl.Read(ArmedSense(Cpu, Player), dt);
            var playerRead = _playerControl?.Read(ArmedSense(Player, Cpu), dt) ?? playerCommand;
            StepProfile.End(StepProfile.Slot.BotRead, readStart);
            Drive(Player, Cpu, playerRead, dt);
            Drive(Cpu, Player, cpuCommand, dt);
        }

        var tailStart = StepProfile.Begin();
        foreach (var fighter in Fighters)
        {
            foreach (var part in fighter.AllParts)
            {
                _preStepBodies[part] = _world.GetState(part);
            }

            foreach (var hand in Hands)
            {
                _preStepGloveVelocity[(fighter, hand)] = fighter.GloveVelocity(hand);
                _hitCooldown[(fighter, hand)] = MathF.Max(0f, _hitCooldown.GetValueOrDefault((fighter, hand)) - dt);
                _kickCooldown[(fighter, hand)] = MathF.Max(0f, _kickCooldown.GetValueOrDefault((fighter, hand)) - dt);
                _elbowCooldown[(fighter, hand)] = MathF.Max(0f, _elbowCooldown.GetValueOrDefault((fighter, hand)) - dt);
            }
        }

        FilterKickPairs();
        FilterElbowPairs();
        StepProfile.End(StepProfile.Slot.ControlTail, tailStart);
        StepProfile.End(StepProfile.Slot.Control, controlStart);
        RememberBeforeStep();
        CoolWeapons(dt);
        var bepuStart = StepProfile.Begin();
        RememberCanvasVelocities();
        UpdateCarriedLoads();
        _world.Step(dt);
        StepProfile.End(StepProfile.Slot.Bepu, bepuStart);
        if (_stagePose is not null)
        {
            return; // 影の試合（StepStage）は打撃を数えない（映像のための立ち姿だけ）
        }

        DetectCanvasImpacts(dt);
        var hitsStart = StepProfile.Begin();
        DetectHits();
        StepProfile.End(StepProfile.Slot.FistHits, hitsStart);
        ResolveWeapons();
        SplitDisconnectedWeapons();
        ResolveKicks();
        ResolveElbows();
        var afterStart = StepProfile.Begin();
        DetectGrazes();
        WatchRecoveries();
        CountMatchClock(live && !RoundOver, dt);
        StepProfile.End(StepProfile.Slot.After, afterStart);
    }

    /// <summary>
    /// ダウンから立ち直って構えた後、試合を戻すまでの一呼吸 [s]（審判の判断の値: 2026-11-13、ユーザーの指定「ワンテンポ入れてから再開」）。
    /// 人形の 0.75 倍速の動画で 1.6 秒ほど（人形の単位で 1.2 s、実物大の時間で 2.01 s）: 立ち直った体が構えを取り直し、見ている人が「戻った」と分かる間。
    /// </summary>
    public const float ResumePause = 2.014f;

    /// <summary>ダウンから立ち直った後の一呼吸の残り [s]（<see cref="ResumePause"/>）。</summary>
    private float _resumeHold;

    /// <summary>前の物理の歩で、ダウン（審判が数えていた）だった両者。</summary>
    private readonly HashSet<Fighter> _wasCounted = [];

    /// <summary>このラウンドに取られたダウンの数（<see cref="MatchRules.KnockdownLimit"/> 用。ラウンドの始まりに 0 へ戻す）。</summary>
    private readonly Dictionary<Fighter, int> _roundKnockdowns = [];

    /// <summary>このラウンドに <paramref name="fighter"/> が取られたダウンの数（ダウンを数える団体で、休み・試合の後を除く）。</summary>
    public int RoundKnockdowns(Fighter fighter) => _roundKnockdowns.GetValueOrDefault(fighter);

    /// <summary>
    /// 相手の体への打撃を数えてよいか（ゲームの決まり。物理は同じ）。立っている相手には常に数える。倒れている相手（起き上がり中を含む）は、
    /// レフェリーがいれば数えない（ダウンはカウントで決着）、いなければ数える（<see cref="BoxingCorners.Referee"/>。技の種類では分けない）。
    /// </summary>
    private bool MayStrike(Fighter victim) => victim.State == FighterState.Standing || !_corners.Referee;

    /// <summary>ダウンを数えるか（<see cref="MatchRules.CountsDowns"/>。<see cref="Rules"/> が無ければ数える）。数えないなら 10 カウント・動かないままの K.O. で決着せず、一呼吸も無い。</summary>
    public bool CountsDowns => Rules?.CountsDowns ?? true;

    /// <summary>レフェリーがいるか（いなければ数えている間も攻撃でき、立ち直った後の一呼吸も無い）。</summary>
    public bool Referee => _corners.Referee;

    /// <summary>試合を一呼吸止めているか（ダウンから立ち直った直後）。打撃は数えず、両者その場で構える。</summary>
    public bool Holding => _settling || (_resumeHold > 0f && !Resting && !RoundOver);

    /// <summary>
    /// レフェリーがダウンを数えているか（どちらかが <see cref="Fighter.IsKnockedDown"/>）。数えている間は審判の指示で動く（<see cref="DownCommand"/>）。
    /// レフェリーのいない試合では数えていても入力元のまま（倒れた相手を攻めてよい）。
    /// </summary>
    public bool Counting => _corners.Referee && !Resting && !RoundOver && (Player.IsKnockedDown || Cpu.IsKnockedDown);

    /// <summary>ダウンの数えから立ち直った（立って構えた）ファイターがいたら、試合を <see cref="ResumePause"/> だけ止める。</summary>
    private void WatchRecoveries()
    {
        foreach (var fighter in Fighters)
        {
            if (_corners.Referee && CountsDowns && _wasCounted.Contains(fighter) && fighter.State == FighterState.Standing && !Resting)
            {
                _resumeHold = ResumePause;
            }

            if (fighter.IsKnockedDown)
            {
                if (_wasCounted.Add(fighter))
                {
                    if (CountsDowns && !Resting)
                    {
                        _roundKnockdowns[fighter] = RoundKnockdowns(fighter) + 1; // 新しいダウン（立っていた → 倒れた）。3 ノックダウン制（KnockdownLimit）が数える
                    }

                    // 取られたダウンを試合を通して数える（戦略の層の点の差 MatchSense.DownLead。読むだけ）。
                    if (fighter == Player)
                    {
                        _playerDowns++;
                    }
                    else
                    {
                        _cpuDowns++;
                    }
                }
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
        var driveStart = StepProfile.Begin();
        foreach (var hand in Hands)
        {
            if (command.For(hand).LetGo)
            {
                // もう一方の手も同じ歩に放すなら、2 本とも放す（従来どおり）。そうでなければ、残った手が片手で持つ。
                LetGo(self, hand, command.For(hand == Hand.Left ? Hand.Right : Hand.Left).LetGo);
            }
            else if (command.For(hand).Grip)
            {
                TryGraspWeapon(self, hand);
            }

            self.UpdateGrab(hand, command.For(hand).Grip, opponent);
            if (self.IsHolding(hand))
            {
                _heldAt[opponent] = _time;
            }
        }

        // 掴まれている間か離された直後に倒れた: 投げ（見出しだけ。倒れたかどうかは物理と審判の読みで決まる）。
        var wasUp = _wasStanding.GetValueOrDefault(opponent, true);
        _wasStanding[opponent] = opponent.State == FighterState.Standing;
        if (wasUp && opponent.State != FighterState.Standing && _heldAt.TryGetValue(opponent, out var held) && _time - held <= ThrowWindow)
        {
            ShowMessage(self == Player ? "THROW!" : "THROWN!", 1f);
            Thrown?.Invoke(self, opponent);
        }

        BareFreeHands(self);
        foreach (var hand in Hands)
        {
            GloveOf(self, hand)?.SetHandShape(command.For(hand).Shape, dt);
        }

        self.ApplyControl(command, dt);
        StepProfile.End(StepProfile.Slot.DriveFighter, driveStart);
    }

    /// <summary>
    /// 手 <paramref name="hand"/> が握っている武器を放す（<see cref="HandCommand.LetGo"/>）。利き手なら、添えていた手も放す（その手も同じ歩に
    /// 放すとき。そうでなければ添えていた手がそのまま片手で持つ）。添えていた手なら、利き手は片手で持つ。はめた物（グローブ）は外さない。
    /// </summary>
    private static void LetGo(Fighter self, Hand hand, bool otherLetsGoToo)
    {
        if (self.WearsGlove(hand) || !self.Wields(hand))
        {
            return;
        }

        if (self.WieldedBody(hand) is not null)
        {
            if (otherLetsGoToo)
            {
                self.Unwield(hand);
            }
            else
            {
                self.UnwieldKeepingSupport(hand);
            }
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

        // 全ての格子（物と重ねた手）を囲む球の外か。
        var s = _world.GetState(weapon.Body.BodyId);
        var glove = self.GloveCenter(hand);
        for (var i = 0; i < weapon.Grids.Count; i++)
        {
            var grid = weapon.Grids[i];
            var center = s.Position + Vector3.Transform(grid.GridCenterLocal, s.Orientation);
            if (Vector3.Distance(center, glove) <= grid.Grid.HalfExtents.Length() + 0.02817f)
            {
                return false;
            }
        }

        return true;
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
        if (!victim.Owns(victimBody) || !MayStrike(victim))
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

            // 当たる速さは拳と打たれた点の相対速度（歩の前）: 下がる相手に付いたまま押す拳は打撃にならず、出てくる相手へ合わせた拳は速く当たる。
            // 向き（velocity: へこみ・押し込み・体の有効質量の向き）は拳の速度のまま。
            var velocity = _preStepGloveVelocity[(attacker, hand)];
            var into = _world.GetState(victimBody).Position - glove;
            var struck = PreStepVelocityAt(victimBody, glove);
            var speed = into.LengthSquared() > 1e-8f ? PunchMechanics.ClosingSpeed(velocity, struck, into) : 0f;
            if (speed <= 0f)
            {
                continue;
            }

            var drive = BodyFactor(attacker, velocity, glove, attacker.ShoulderPosition(hand), attacker.Parts.Chest, struck, into);
            var impactMass = attacker.ImpactMass(hand, velocity, out var bodyShare, drive);
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
    /// 部位 <paramref name="body"/> の点 <paramref name="point"/> の、この物理の歩の前の速度（並進 ＋ 回転 × 腕の長さ）。
    /// 体の部位は歩の前に覚えた状態、それ以外（切り落とされた破片など）は武器の接触のために覚えた状態、どちらも無ければ今の状態。
    /// </summary>
    private Vector3 PreStepVelocityAt(int body, Vector3 point)
    {
        var s = _preStepBodies.TryGetValue(body, out var before) ? before
            : _beforeStep.TryGetValue(body, out var remembered) ? remembered : _world.GetState(body);
        return s.LinearVelocity + Vector3.Cross(s.AngularVelocity, point - s.Position);
    }

    /// <summary>
    /// 打った側の体の質量のうち、当たりへ運べる割合 = 押し出し（<see cref="PunchMechanics.LimbDrive"/>）× 体自身の動き（<see cref="PunchMechanics.BodyCarry"/>）。
    /// 打つ点の速度 <paramref name="strikeVelocity"/>、当たる点 <paramref name="contact"/>、手足の付け根 <paramref name="root"/>（腕・肘は肩、脚は股）とそれが乗る胴の部位
    /// <paramref name="torsoBody"/>（胸・骨盤）、打たれた点の速度 <paramref name="struckVelocity"/>（どれも歩の前）。押し出しの根元を肩・股でなく重心にするのは、胴の回転が運ぶ
    /// フック・肘打ちでは肩も一緒に回り、肩に対する伸びが小さいため。体自身の動きは、突っ張り（付け根の速度: クロスで後ろの肩が前へ出る・踏み込み）と、
    /// 梃子（手足と握った武器を胴に固めたとして当たる点が胴と一緒に動く速度: フック・武器の振り）を、連結度と同じ重みで混ぜる（<see cref="PunchMechanics.BodyClosingSpeed"/>）。
    /// 混ぜる重みの手足の線は、連結度と同じ線（付け根から <paramref name="limbEnd"/>: 拳・はめた物・握った武器は拳、肘・蹴りは当たる点。null なら当たる点）。
    /// </summary>
    private float BodyFactor(Fighter striker, Vector3 strikeVelocity, Vector3 contact, Vector3 root, int torsoBody, Vector3 struckVelocity, Vector3 direction, Vector3? limbEnd = null)
    {
        var center = PreStepBodyVelocity(striker);
        var closing = PunchMechanics.ClosingSpeed(strikeVelocity, struckVelocity, direction);
        var body = PunchMechanics.BodyClosingSpeed(PreStepVelocityAt(torsoBody, root), PreStepVelocityAt(torsoBody, contact), struckVelocity, (limbEnd ?? contact) - root, direction);
        return PunchMechanics.LimbDrive(strikeVelocity, center, struckVelocity, direction) * PunchMechanics.BodyCarry(body, closing);
    }

    /// <summary>ファイターの付いている部位の重心の、この物理の歩の前の速度（運動量 ÷ 質量）。</summary>
    private Vector3 PreStepBodyVelocity(Fighter fighter)
    {
        var momentum = Vector3.Zero;
        var mass = 0f;
        foreach (var part in fighter.AllParts)
        {
            if (fighter.Owns(part) && _preStepBodies.TryGetValue(part, out var s))
            {
                var m = _world.GetMass(part);
                momentum += s.LinearVelocity * m;
                mass += m;
            }
        }

        return mass > 0f ? momentum / mass : Vector3.Zero;
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
    /// <param name="legacyWornImpact">関節駆動でも、グローブ接触を解いた呼び出しでは従来の運動量を渡す。</param>
    /// <param name="carryBody">体の分の運動量を押し込む近道（CarryBody）を使うか。関節で連なった体の衝突として解いた打撃（ArticulatedPunch）は、力積をもう加えたので false。</param>
    internal void ApplyHit(HitEvent hit, int victimBody, Vector3 gloveVelocity, bool carve = true, Vector3 alsoTwist = default, bool legacyWornImpact = false, bool carryBody = true)
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
        // 物理の接触が運ぶのは打った四肢そのものの質量（腕、蹴りなら脚）だけ。残りの体の分を足す。
        var extraMass = hit.ImpactMass - (hit.Kick ? hit.Attacker.KickingMass(hit.Hand) : hit.Attacker.ArmMass(hit.Hand));
        var push = carryBody && (!hit.Attacker.UsesJointDrive || legacyWornImpact) && extraMass > 0f && gloveVelocity.LengthSquared() > 1e-8f
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
        var energy = hit.Energy;
        if (!hit.Kick && !hit.Elbow)
        {
            _impacts.SpawnPunchCue(hit.Point, gloveVelocity, energy,
                hit.Zone is HitZone.Guard or HitZone.Elbow or HitZone.Shoulder);
        }

        // 皮膚の上からでも、衝撃は組織を伝わって臓器を打つ（一時的な打撲。削れれば別に恒久的に効く）。
        victim.BruiseOrgans(hit.Point, energy);
        victim.Eyes.Flinch(energy, Vector3.Distance(hit.Point, victim.HeadPosition)); // 打たれた瞬間の反射（見た目だけ）
        if (carve)
        {
            Strike(victim, victimBody, hit.Point, gloveVelocity, hit.Damage / ZoneDamage.Multiplier(hit.Zone), hit.Attacker, push, MathF.Min(_tuning.MaxCarve, energy * _tuning.CarvePerJoule));
        }

        _recentHits.Add(hit);
        LastHit = hit;
        if (!hit.Kick)
        {
            ControlOf(hit.Attacker)?.OnPunchLanded(hit.Hand, hit.Damage);
        }

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

    /// <summary>打撃の強さ 0（かすり）〜 1（とても重い 1 発）。尺度（どのダメージで 0・1 か）と測った分布は <see cref="HitLook"/>。</summary>
    internal static float HitStrength(float damage) => HitLook.Strength(damage);

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
                _debris.Spawn(chip.Position, Fly(1.5f, 0.12f), chip.Orientation, chip.Size * 0.6f, Spin(23.83f), OrganSpark, life: 1.007f, glow: true, tag: chip.Material, heat: ChipHeat, trail: 1f, ownColor: true);
                for (var k = 0; k < 3; k++)
                {
                    _debris.Spawn(chip.Position, Fly(2f, 0.3f), chip.Orientation, chip.Size * 0.3f, Spin(29.79f), OrganSpark, life: 0.7553f, glow: true, tag: VoxelBreakSound.SparkTag, heat: ChipHeat * 0.7f, trail: 1f, ownColor: true);
                }

                Blood(chip, Fly, Spin);
                continue;
            }

            // 欠片はそのままの色で、その色の光の尾を引く。尾の長さは材質で: 骨は長く（火花ほど）、皮膚・肉・金属・木は短く（その半分）。
            var trail = material.Tissue == VoxelTissue.Bone ? BoneTrail : ChipTrail;
            _debris.Spawn(chip.Position, Fly(1.5f, 0.12f), chip.Orientation, chip.Size * 0.9f, Spin(23.83f), color, tag: chip.Material, heat: ChipHeat, trail: trail, ownColor: true);
            for (var k = 0; k < 2; k++)
            {
                _debris.Spawn(chip.Position, Fly(1.8f, 0.25f), chip.Orientation, chip.Size * 0.45f, Spin(41.71f), color, life: 2.685f, tag: chip.Material, heat: ChipHeat * 0.7f, trail: trail, ownColor: true);
            }

            switch (material.Tissue)
            {
                case VoxelTissue.Bone:
                    for (var k = 0; k < 3; k++)
                    {
                        _debris.Spawn(chip.Position, Fly(1.6f, 0.35f), chip.Orientation, chip.Size * 0.25f, Spin(53.62f), BoneDust, life: 2.014f, tag: VoxelBreakSound.DustTag);
                    }

                    break;
                case VoxelTissue.Solid when chip.Material != BoxerAnatomy.Iron:
                    // 木が潰れると木くず。
                    _debris.Spawn(chip.Position, Fly(1.4f, 0.4f), chip.Orientation, chip.Size * 0.4f, Spin(47.67f), material.Color, life: 2.35f, tag: VoxelBreakSound.DustTag);
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
            _debris.Spawn(chip.Position, fly(2.1f, 0.3f), chip.Orientation, chip.Size * 0.35f, spin(35.75f), BloodColor, life: 3.692f, tag: VoxelBreakSound.BloodTag);
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
            _debris.Spawn(point, v, Quaternion.Identity, new Vector3(0.00338f), Vector3.Zero, Spark, life: 0.5035f + (0.2518f * MathF.Abs(Jitter())), glow: true,
                tag: VoxelBreakSound.SparkTag, heat: 0.5035f, trail: BoneTrail);
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
                _debris.Spawn(chunk.Position, drop, chunk.Orientation, chunk.CellSize * 0.35f, new Vector3(Jitter(), Jitter(), Jitter()) * 35.75f, BloodColor, life: 3.692f, tag: VoxelBreakSound.BloodTag);
            }
        }
    }

    /// <summary>塊のはがれた断面が光っている時間 [s]（欠片と同じく白 → 黄 → 赤 → 元の色）。</summary>
    private const float ChunkHeat = 0.8392f;

    /// <summary>塊の飛ぶ速さの倍率（欠片の 1.5 倍より遅い: 重い）。</summary>
    private const float ChunkFling = 1.3f;

    /// <summary>塊の回る速さ [rad/s]（欠片より、ゆっくり大きく回る）。</summary>
    private const float ChunkSpin = 8.341f;

    /// <summary>当たった瞬間の光（<see cref="ImpactEffects"/>: 球とカプセルで、飛び散るボクセルと紛れない）。打った側のグローブの色。</summary>
    private void ImpactFlash(Vector3 point, Vector3 direction, float strength, Fighter? attacker)
    {
        var color = attacker is null ? Spark : attacker == Player ? PlayerSpark : CpuSpark;
        _impacts.Spawn(point, direction, strength, color, Jitter);
    }

    /// <summary>砕けた欠片が光っている時間 [s]: 白 → 黄 → 赤 → 元の色と、空中で冷めていく（<see cref="VoxelDebris.HeatColor"/>）。</summary>
    private const float ChipHeat = 0.8392f;

    private static readonly Vector4 BloodColor = new(0.55f, 0.02f, 0.03f, 1f);
    private static readonly Vector4 BoneDust = new(1f, 0.98f, 0.9f, 1f);
    private static readonly Vector4 Spark = new(1f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 OrganSpark = new(0.75f, 0.2f, 1.6f, 1f); // 紫（1 を超える分は光ってにじむ）

    /// <summary>欠片の光の尾の長さ（<see cref="VoxelDebris.TrailSeconds"/> ぶんの何倍か）: 皮膚・肉・金属・木は短く、骨と火花は長く。</summary>
    private const float ChipTrail = 0.5f;
    private const float BoneTrail = 1f;

    /// <summary>金属同士の火花: 近づく速さ 1 m/s あたりの粒の数と、1 度に出す数の上限。</summary>
    private const float SparksPerSpeed = 3.575f;
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
        else if (CountsDowns && Fighters.FirstOrDefault(f => f.StillTime > StillKoSeconds) is { } still)
        {
            EndRound(cpuWon: still == Player, "K.O. — OUT COLD");
        }
        else if (CountsDowns && Rules?.KnockdownLimit is > 0 and var limit && Fighters.FirstOrDefault(f => RoundKnockdowns(f) >= limit) is { } floored)
        {
            EndRound(cpuWon: floored == Player, $"TKO — {limit} KNOCKDOWNS"); // 1 ラウンドに limit 回のダウンで止める（団体の決まり）
        }
        else if (_ring.IsOut(Player.PelvisPosition))
        {
            EndRound(cpuWon: true, "RING OUT");
        }
        else if (_ring.IsOut(Cpu.PelvisPosition))
        {
            EndRound(cpuWon: false, "RING OUT");
        }
        else if (CountsDowns && Player.DownTime > CountSeconds)
        {
            EndRound(cpuWon: true, "10 COUNT K.O.");
        }
        else if (CountsDowns && Cpu.DownTime > CountSeconds)
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

    /// <summary>試合の始め（入場してから最初のゴングまで。<see cref="MatchRules.IntroSeconds"/>）か。選手は始めのエモートをし、打撃も時計も動かない。</summary>
    public bool InIntro { get; private set; }

    /// <summary>試合の始めの待ち時間の残り [s]。</summary>
    public float IntroLeft { get; private set; }

    /// <summary>試合が終わった（K.O. か、最終ラウンドの終わりの判定）。</summary>
    public bool MatchOver { get; private set; }

    /// <summary>判定の結果（"YOU WIN BY DECISION" など）。K.O. なら null。</summary>
    public string? Decision { get; private set; }

    /// <summary>休んでいる（ラウンドの間・試合の後）: 入力元を読まず、コーナーへ戻って休む（K.O. の後も打ち続けない）。</summary>
    public bool Resting => InBreak || MatchOver || InIntro;

    private readonly Dictionary<Fighter, float> _regenBudget = [];

    /// <summary>傷の前線の再生の抽選の乱数（試合の種から作った専用の流れ。ほかの乱数の流れを動かさない: CLAUDE.md §1.6）。</summary>
    private Random? _regrowRandom;

    /// <summary>次の再生の抽選までにたまった時間 [s]。</summary>
    private float _regrowClock;

    /// <summary>ラウンド中に、両者が倒れたまま意識だけでは立てない状態が続いている時間 [s]（<see cref="MatchRules.StuckDoublingSeconds"/>）。</summary>
    private float _stuckTime;

    /// <summary>今、両者が倒れたまま意識だけでは立てないので体を再生させているか（中継・調べもの用）。</summary>
    public bool RegrowingWhileStuck { get; private set; }

    /// <summary>直近の <see cref="Step"/> で、芽から生え直し始めた部位（中継の記録 <c>regrown</c>）。</summary>
    public List<RegrowthEvent> RecentRegrowths { get; } = [];

    /// <summary>今の再生の倍率（休みは 1、両者ダウンで立てない間は 2^(t / T)。再生していなければ 0）。</summary>
    public float RegrowthRate { get; private set; }

    /// <summary>
    /// 傷の前線の再生（<see cref="Fighter.Regrow"/>）を <paramref name="dt"/> ぶん進める: <see cref="MatchRules.RegrowthTickSeconds"/> ごとに 1 回の抽選で、
    /// 前線を 速さ × 間隔 × <paramref name="rate"/> だけ進める（両者とも）。
    /// </summary>
    private void RegrowBodies(MatchRules rules, float dt, float rate)
    {
        if (rules.RegrowthSpeed <= 0f || rules.RegrowthTickSeconds <= 0f)
        {
            RegrowthRate = 0f;
            return;
        }

        RegrowthRate = rate;
        _regrowRandom ??= new Random(_seed ^ 0x2e9e0);
        _regrowClock += dt;
        while (_regrowClock >= rules.RegrowthTickSeconds)
        {
            _regrowClock -= rules.RegrowthTickSeconds;
            foreach (var fighter in Fighters)
            {
                var report = fighter.Regrow(_regrowRandom, rules.RegrowthSpeed * rules.RegrowthTickSeconds * rate);
                foreach (var piece in report.NewPieces)
                {
                    UntouchedByWeaponBoxes(piece); // 古い中身を写した床の破片も、ほかの破片と同じに武器とは箱で当たらない
                }

                foreach (var part in report.Rebudded)
                {
                    RecentRegrowths.Add(new RegrowthEvent(fighter, part));
                }
            }
        }
    }

    /// <summary>
    /// ラウンド中の再生: 両者とも倒れていて（立っていない）、どちらも意識だけでは立てない（<see cref="Fighter.BodyLetsStand"/> が偽）間だけ、
    /// 両者の体を再生させる。倍率はその状態が続いた時間 t から 2^(t / <see cref="MatchRules.StuckDoublingSeconds"/>)。
    /// 片方だけ倒れているなら再生しない（相手が攻める場面を消さない）。死んだ体は再生しない（<see cref="Fighter.Regrow"/>）。
    /// </summary>
    private void RegrowWhileStuck(MatchRules rules, float dt)
    {
        var stuck = rules.StuckDoublingSeconds > 0f && rules.RegrowthSpeed > 0f
            && Player.State != FighterState.Standing && Cpu.State != FighterState.Standing
            && !Player.BodyLetsStand && !Cpu.BodyLetsStand;
        if (!stuck)
        {
            if (RegrowingWhileStuck)
            {
                _regrowClock = 0f; // 次に止まったときは、抽選の間隔を 0 から数える
            }

            RegrowingWhileStuck = false;
            RegrowthRate = 0f;
            _stuckTime = 0f;
            return;
        }

        RegrowingWhileStuck = true;
        _stuckTime += dt;
        RegrowBodies(rules, dt, MathF.Pow(2f, _stuckTime / rules.StuckDoublingSeconds));
    }


    /// <summary>テレビ中継の試合を 1 歩進める: 決着（K.O. は時計より先に調べる）・時計・ラウンドの終わり・休み・判定。</summary>
    private void UpdateMatch(MatchRules rules, float dt)
    {
        if (MatchOver)
        {
            return;
        }

        if (InIntro)
        {
            // 試合の始め: 入場の待ち時間（選手は始めのエモートをする）。ラウンドの時計は動かさず、終わったらゴング。
            IntroLeft -= dt;
            if (IntroLeft <= 0f)
            {
                InIntro = false;
                IntroLeft = 0f;
                RecentBell = RoundBell.Start;
                ShowMessage("ROUND 1", 1.5f);
            }

            return;
        }

        if (InBreak)
        {
            BreakLeft -= dt;
            foreach (var fighter in Fighters)
            {
                // 削れた体は一定の速さで治る（RegenVoxelsPerSecond 個/秒）。打撲・スタミナ・脳しんとうはそれぞれの速さで戻る。
                if (rules.RegenVoxelsPerSecond > 0f)
                {
                    var budget = _regenBudget.GetValueOrDefault(fighter) + (rules.RegenVoxelsPerSecond * dt);
                    var whole = (int)budget;
                    if (whole > 0)
                    {
                        fighter.Voxels?.Regenerate(whole);
                    }

                    _regenBudget[fighter] = budget - whole;
                }

                // 皮膚のあざも休みの間だけ少しずつ引く（見た目だけ。ラウンド中は引かない）。
                fighter.Voxels?.FadeSkinBruises(dt, rules.BruiseHalfLifeSeconds);
            }

            // 傷の前線の再生（休みの間は倍率 1）と、失った武器の支給。
            RegrowBodies(rules, dt, 1f);
            if (rules.ResupplyWeapons)
            {
                ResupplyWeapons();
            }

            if (BreakLeft <= 0f)
            {
                InBreak = false;
                MatchRound++;
                RoundLeft = rules.RoundSeconds;
                RestartMatchClock();
                foreach (var fighter in Fighters)
                {
                    fighter.ResetCount(); // 倒れたまま休みに入っても、次のラウンドで数え直す
                }

                _roundKnockdowns.Clear(); // ダウンの数はラウンドごと

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

        RegrowWhileStuck(rules, dt);
        RoundLeft -= dt;
        if (RoundLeft > 0f)
        {
            return;
        }

        RoundLeft = 0f;
        RecentBell = RoundBell.End;
        if (MatchRound >= rules.Rounds && !rules.JudgesDecision)
        {
            // 判定しない団体: 5 ラウンドを終えて両者が生きていれば引き分け（勝ち数は増やさない）。
            Decision = "DRAW — BOTH SURVIVE";
            MatchOver = true;
            ShowMessage(Decision, 1e6f);
            return;
        }

        if (MatchRound >= rules.Rounds)
        {
            // 判定: 与えたダメージ（得点）の多い方。差がわずかなら引き分け。
            var player = Cpu.Condition.TotalDamage;
            var cpu = Player.Condition.TotalDamage;
            var draw = MathF.Abs(player - cpu) <= MathF.Max(rules.DrawMinimum, rules.DrawShare * (player + cpu) * 0.5f);
            Decision = draw ? "DRAW" : player > cpu ? $"{RedLabel} WIN BY DECISION" : $"{BlueLabel} WINS BY DECISION";
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
        _stuckTime = 0f; // 両者ダウンの倍率はラウンドごとに 1 から
        RegrowingWhileStuck = false;
        _regrowClock = 0f;
        ShowMessage($"END OF ROUND {MatchRound}", 2f);
    }

    /// <summary>休むときの指令: 自分のコーナー（赤は手前左、青は奥右）へ歩き、相手の方を向いて、ガードを下げる。</summary>
    private FighterCommand RestCommand(Fighter self, Fighter opponent)
    {
        var rules = Rules ?? MatchRules.Tv;
        return WalkTo(self, opponent, Layout.RestCorner(self == Player, FootprintOf(self)), rules.RestGuardDrop, rules.RestWalk);
    }

    /// <summary>
    /// ダウンの間の、審判の指示（ゲームの決まり。2026-11-18、ユーザーの指定）: 倒れた側はその場で立つ（入力元の移動を使わない: 起き上がりながら
    /// よろよろと相手へ詰めていた）、立っている側は倒れた相手からいちばん遠いニュートラルコーナーへ下がって待つ。体は同じ物理で目標を追う。
    /// </summary>
    private FighterCommand DownCommand(Fighter self, Fighter opponent)
    {
        var rules = Rules ?? MatchRules.Tv;
        if (self.IsKnockedDown)
        {
            return HoldCommand(self, opponent);
        }

        // 行き先のコーナーは数え始めに 1 度だけ決める（倒れた体が対角線の上で動くたびに選び直すと、行き先が入れ替わって揺れる）。
        if (!_neutralCorners.TryGetValue(self, out var corner))
        {
            corner = Layout.NeutralCorner(opponent.PelvisPosition, FootprintOf(self));
            _neutralCorners[self] = corner;
        }

        var walk = WalkTo(self, opponent, corner, Vector3.Zero, walk: 1f); // 数えている間に着くよう、普段の足で
        // 倒れた相手の体をまたがない: 近いほど相手から離れる向きを足し、体の脇を回ってコーナーへ行く（まっすぐ向かうと足が体に絡んで止まった）。
        var away = self.PelvisPosition - opponent.PelvisPosition;
        away.Y = 0f;
        var gap = away.Length();
        if (gap > 1e-4f && gap < DownClearance)
        {
            // 離れる向きと、コーナーへ向かう向きのうち相手の体の脇を回る成分（体の向こうにコーナーがあるときは決まった側へ回る）。
            var outward = away / gap;
            var around = walk.MoveWorld - (Vector3.Dot(walk.MoveWorld, outward) * outward);
            around = around.LengthSquared() > 1e-4f ? Vector3.Normalize(around) : Vector3.Cross(Vector3.UnitY, outward);
            var near = 1f - (gap / DownClearance);
            var move = walk.MoveWorld + (outward * (2f * near)) + (around * near);
            walk = walk with { MoveWorld = move.LengthSquared() > 1f ? Vector3.Normalize(move) : move };
        }

        return walk;
    }

    /// <summary>ダウンを数えている間に立っている側が向かうニュートラルコーナー（数え始めに決め、数え終わったら捨てる）。</summary>
    private readonly Dictionary<Fighter, Vector3> _neutralCorners = [];

    /// <summary>ダウンを数えている間、立っている側が倒れた相手の体から取る距離 [m]（身長 1.7 m の 2/3: 倒れた体の長さの半分と足の幅）。</summary>
    private const float DownClearance = 1.127f;

    /// <summary>
    /// <paramref name="spot"/>（床の上）へ歩き、相手の方を向いて、拳を構えから <paramref name="guardDrop"/>（腕の長さ単位）だけ下げる指令
    /// （休むコーナーへ戻る・ダウンの間にニュートラルコーナーで待つ）。
    /// </summary>
    private FighterCommand WalkTo(Fighter self, Fighter opponent, Vector3 spot, Vector3 guardDrop, float walk)
    {
        var rules = Rules ?? MatchRules.Tv;
        var toCorner = spot - self.PelvisPosition;
        toCorner.Y = 0f;
        var distance = toCorner.Length();
        var move = distance > rules.CornerArrive ? Vector3.Normalize(toCorner) * MathF.Min(1f, distance / rules.CornerSlowDown) * walk : Vector3.Zero;
        var low = guardDrop * self.ArmReach;
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
        ShowMessage($"{reason}  {(cpuWon ? $"{BlueLabel} WINS" : $"{RedLabel} WIN")}", RoundResetDelay);
    }

    private bool _settling;

    /// <summary>
    /// 世界を作った直後の助走（映像の始めで、生成直後の T 字の姿勢を映さないため）: <paramref name="seconds"/> だけ、両者をその場で構えさせて世界を回す。
    /// 姿勢は書かない（構えの目標を最初の歩から出し、体がバネでそこへ追いつくまで待つだけ）。試合の時計・打撃・ボットの乱数は進めない。
    /// コンストラクタから呼ぶ（<see cref="BoxingCorners.SettleSeconds"/>）ので、同じ種・同じ角から作り直した試合（リプレイ・影の試合）も同じ助走をして一致する。
    /// </summary>
    private void Settle(float seconds)
    {
        _settling = true;
        try
        {
            var steps = (int)MathF.Ceiling(seconds * 60f);
            for (var i = 0; i < steps; i++)
            {
                Step(default, 1f / 60f);
            }
        }
        finally
        {
            _settling = false;
        }
    }

    private void SettleStep(float dt)
    {
        RecentBell = RoundBell.None;
        var substeps = Math.Max(1, (int)MathF.Ceiling((dt * SubstepRate) - 1e-3f));
        var sub = dt / substeps;
        for (var i = 0; i < substeps; i++)
        {
            Substep(default, null, sub);
        }
    }

    /// <summary>
    /// ラウンドを作り直す。Aigis では剛体の削除がハンドル再利用で危険（architecture.md risk 4）なので、
    /// ワールドごと作り直す（体の状態もラウンドごとに新品）。
    /// </summary>
    private void StartRound()
    {
        _perception.Clear();
        _perceived.Clear();
        _world?.Dispose();
        _weapons.Clear(); // 武器は世界ごと消える
        _connectedAt.Clear();
        EndBreakActions(); // 休みの小物・入力元も世界と一緒に捨てる
        _recentCuts.Clear();
        _beforeStep.Clear();
        _resumeHold = 0f;
        _wasCounted.Clear();
        _neutralCorners.Clear();
        _round++;
        _world = new PhysicsWorld(conserveAngularMomentum: _tuning.JointDrive);
        LivingRoom.AddColliders(_world, Layout);
        _ring = new Ring(_world, Layout, RingSurfaceHeight, LivingRoom.PhysicsStageBase);
        var surface = _ring.SurfaceCenter;
        // コーナーから始める試合は、赤・青の休むコーナー（四角は手前左・奥右、檻は 240°・60° の頂点の側）の対角線の上で、互いに向き合う（生成の向きは +Z が 0、Y 軸まわり）。
        // ここでは仮に角の近くへ生み、持ち物を持たせてから、体と持ち物の輪郭が縁に触れない所へ移す（PlaceAtCorner）。
        // コーナーから始めない形（ドリル）は、中央で StartGap だけ離れて Z 軸の上で向き合う。
        var redCorner = Layout.RestCorner(true, []);
        var blueCorner = Layout.RestCorner(false, []);
        var (redFeet, blueFeet, redYaw) = Layout.FromCorners
            ? (redCorner with { Y = surface.Y }, blueCorner with { Y = surface.Y }, MathF.Atan2(blueCorner.X - redCorner.X, blueCorner.Z - redCorner.Z))
            : (surface - new Vector3(0f, 0f, Layout.StartGap * 0.5f), surface + new Vector3(0f, 0f, Layout.StartGap * 0.5f), 0f);
        // VR のプレイヤーには、手触りの調整が終わるまでスタミナの疲れを効かせない（FighterTuning.PlayerFatigue）。
        var concussionSpin = _tuning.ConcussionSpin * (Rules?.ConcussionSpinScale ?? 1f);
        var playerTuning = (_tuning.PlayerFatigue ? _tuning : _tuning with { StaminaFatigue = false })
            with { Build = _corners.PlayerBody ?? _tuning.Build, ConcussionSpin = concussionSpin };
        var cpuTuning = _tuning with { Build = _corners.CpuBody ?? _tuning.Build, ConcussionSpin = concussionSpin };
        if (!_corners.Referee)
        {
            // レフェリーなし: 倒れた相手を打ち下ろせるよう、腰を落とせる深さを広げる（レフェリーありの試合の体は変わらない）。
            playerTuning = playerTuning with { MaxCrouch = playerTuning.GroundCrouch };
            cpuTuning = cpuTuning with { MaxCrouch = cpuTuning.GroundCrouch };
        }

        Player = Fighter.Spawn(_world, playerTuning, "player", redFeet, redYaw);
        Cpu = Fighter.Spawn(_world, cpuTuning, "cpu", blueFeet, redYaw + MathF.PI);
        var playerModel = Models.GetOrAdd((playerTuning.HalfExtents, _tuning.ModelSubdivisions), key => BoxerModel.Build(key.Item1, key.Item2));
        var cpuModel = Models.GetOrAdd((cpuTuning.HalfExtents, _tuning.ModelSubdivisions), key => BoxerModel.Build(key.Item1, key.Item2));
        PlayerSkin = new BoxerSkin(playerModel, Player, playerTuning);
        // 体の形はローポリのモデル（頭・胸・腕の形）。その内側をボクセルで埋める。トランクスはグローブと同じコーナーの色。
        // 顔はボットのリングネームから（同じボットはいつも同じ顔）。人が操る赤コーナーは名前が無ければ既定の顔。
        var playerLook = _corners.PlayerLook ?? (_corners.PlayerName is { } redName ? FaceLook.For(Bots.BotNames.RingNameOf(redName)) with { Parts = _corners.PlayerFace ?? FaceParts.For(redName) } : null);
        var cpuLook = _corners.CpuLook ?? (FaceLook.For(Bots.BotNames.RingNameOf(_corners.CpuName)) with { Parts = _corners.CpuFace ?? FaceParts.For(_corners.CpuName) });
        // 顔の層の形（部品）: 名簿のキャラは自分の顔（PlayerFaceSpec / CpuFaceSpec）、無ければ見た目の髪型・眉・ひげと名前から（FaceSpec.FromLook）。
        var limbBendStrength = Rules?.LimbBendStrengthScale ?? (LimbBendProbeScale > 0f ? LimbBendProbeScale : 1f);
        Player.Voxels = new BoxerBody(Player, _world, playerTuning, playerModel, Outfit.RedCorner, BoxerVoxelOverrides.Shipped, playerLook,
            _corners.PlayerFaceSpec ?? (playerLook is null ? null : FaceSpec.FromLook(playerLook, _corners.PlayerLabel ?? _corners.PlayerName ?? "player")), limbBendStrength);
        Cpu.Voxels = new BoxerBody(Cpu, _world, cpuTuning, cpuModel, Outfit.BlueCorner, BoxerVoxelOverrides.Shipped, cpuLook,
            _corners.CpuFaceSpec ?? FaceSpec.FromLook(cpuLook, _corners.CpuLabel ?? _corners.CpuName), limbBendStrength);
        Player.RefreshVoxelMass();
        Cpu.RefreshVoxelMass();
        // 破片はリングの上ならリングの面、その外は箱庭の台座・ローテーブルの天板、さらに外は床で止まる。
        _debris.Clear();
        _impacts.Clear();
        _wounds.Clear();
        _debris.GroundAt = p => LivingRoom.GroundAt(p, Layout, RingSurfaceHeight);
        _debris.Gravity = 9.81f * _tuning.DebrisGravityScale;
        CpuSkin = new BoxerSkin(cpuModel, Cpu, cpuTuning);
        _fighters = [Player, Cpu];
        _wielders.Clear();
        _reach.Clear();
        _fullReach.Clear();
        _wieldHand.Clear();
        _weaponCooldown.Clear();
        _selfCooldown.Clear();
        _spawnGrace.Clear();
        _spawnOverlaps.Clear();
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

        _footprints.Clear();
        PlaceAtCorner(Player, red: true);
        PlaceAtCorner(Cpu, red: false);

        // 前のラウンドの操作元から、覚えたことを引き継がせる（学ぶボット。IFighterControl.CarryOver）。
        var previousCpu = _round > 1 ? _cpuControl : null;
        var previousPlayer = _playerControl;
        var cpuSeed = _seed + _round;
        _cpuControl = WeaponBot(_corners.CpuWeapon, _corners.CpuWeaponStyle, Cpu, cpuSeed, _corners.CpuWear) ?? _corners.Cpu(cpuSeed);
        var playerSeed = (_seed * 7919) + _round;
        _playerControl = _corners.RedBotStyle is { } style ? WeaponBot(_corners.PlayerWeapon, _corners.PlayerWeaponStyle, Player, playerSeed, _corners.PlayerWear) ?? new Bots.BoxerBot(style, playerSeed) : _corners.Player?.Invoke(playerSeed);
        if (!_corners.Referee)
        {
            // レフェリーなし: ボットは倒れた相手への攻め（GroundStrike）を足す。レフェリーありの試合には何も足さない（振る舞いも記録も今のまま）。
            if (!_corners.ExternalBlue && _cpuControl is not (IntentControl or ExternalControl))
            {
                _cpuControl = GroundStriking(_cpuControl, Cpu, GroundStyle(_corners.CpuStyle), _corners.CpuWeapon, _corners.CpuTwoHanded, cpuSeed);
            }

            if (_playerControl is not null and not (IntentControl or ExternalControl))
            {
                _playerControl = GroundStriking(_playerControl, Player, GroundStyle(_corners.RedBotStyle ?? _corners.PlayerStyle), _corners.PlayerWeapon, _corners.PlayerTwoHanded, playerSeed);
            }
        }

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
        _preStepBodies.Clear();
        _hitCooldown.Clear();
        _kickCooldown.Clear();
        _elbowCooldown.Clear();
        _elbowFiltered.Clear();
        _kickFiltered.Clear(); // 世界ごと作り直すので、切った組も消える
        // 試合の始め（入場してから最初のゴングまで）の間 IntroSeconds があれば、ゴングはその後（UpdateMatch）。
        InIntro = Rules is { IntroSeconds: > 0f };
        IntroLeft = InIntro ? Rules!.IntroSeconds : 0f;
        ShowMessage(InIntro ? string.Empty : Rules is null ? "FIGHT!" : "ROUND 1", InIntro ? 0f : 1.5f);
        _startBellPending = !InIntro;
        _regenBudget.Clear();
        _suppliedAfterRound.Clear();
        _supplies.Clear();
        _regrowClock = 0f;
        _stuckTime = 0f;
        RegrowingWhileStuck = false;
        _grazeArmed.Clear();
        MatchRound = 1;
        RoundLeft = Rules?.RoundSeconds ?? 0f;
        RestartMatchClock();
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

    /// <summary>
    /// 倒れた相手への好み（<c>Ground*</c>）を持つスタイル: コーナーの解決済みのスタイル（名簿のキャラクターなら教育を重ねたもの）、無ければボクサー。
    /// 武器のボット（BoxerBot ではない）にも、そのスタイルの好み（空手の踏みつけなど）を効かせる。名前では引き直さない。
    /// </summary>
    private static Bots.BotStyle GroundStyle(Bots.BotStyle? resolved) => resolved ?? Bots.BotCatalog.Boxer;

    /// <summary>ボットに倒れた相手への攻めを足す（武器を握る側は、その武器の長さ・握りで）。</summary>
    private static Bots.GroundStriker GroundStriking(IFighterControl bot, Fighter self, Bots.BotStyle style, MiniRing.Weapons.WeaponKind? weapon, bool twoHanded, int seed)
    {
        var tool = Bots.GroundTool.Fist;
        var club = false;
        if (weapon is { } kind && !MiniRing.Weapons.Weapon.IsWorn(kind))
        {
            var length = MiniRing.Weapons.Weapon.TipOf(kind).X - MiniRing.Weapons.Weapon.GripOf(kind).X;
            var spacing = !twoHanded ? 0f : Bots.FormSwordsman.Handles(kind) ? Bots.FormSwordsman.Spacing(kind) : MiniRing.Weapons.Weapon.HandSpacing / self.ArmReach;
            tool = new Bots.GroundTool(length / self.ArmReach, twoHanded, spacing);
            club = kind is MiniRing.Weapons.WeaponKind.WoodenClub or MiniRing.Weapons.WeaponKind.Bat;
        }

        return new Bots.GroundStriker(bot, bot is Bots.BoxerBot boxer ? boxer.Style : style, tool, seed ^ 0x2a2a, club);
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
            self.Faintness, OpponentTargets: opponent.AsTarget, SelfStamina: self.Stamina,
            SelfVision: self.Voxels?.Vision ?? VisionState.Healthy, OpponentVision: opponent.Voxels?.Vision ?? VisionState.Healthy,
            OpponentArmReach: opponent.ArmReach, OpponentDowned: opponent.State != FighterState.Standing,
            SelfBuild: self.Build, OpponentBuild: opponent.Build,
            SelfLegLeft: self.LegBodyOf(Hand.Left), SelfLegRight: self.LegBodyOf(Hand.Right),
            SelfInjury: self.Injury(felt: true), OpponentInjury: opponent.Injury(felt: false), OpponentWinded: opponent.Winded, Grapple: GrappleSenseOf(self, opponent),
            SelfLegOxygenLeft: self.LegOxygen(Hand.Left), SelfLegOxygenRight: self.LegOxygen(Hand.Right));
    }

    // ── 試合の時刻と点（戦略の層の入力 MatchSense。読むだけで、試合の物理には響かない）──

    /// <summary>このラウンドの、試合が動いていた時間 [s]（休み・ダウンの数え・一呼吸の間は進めない）。</summary>
    private float _roundElapsed;

    /// <summary>打ち合いの無い時間 [s]: どちらかの得点（受けたダメージの合計）が最後に動いてから。</summary>
    private float _quiet;

    /// <summary>前の歩の両者の受けたダメージの合計（得点が動いたかを見る）。</summary>
    private float _scoredBefore;

    /// <summary>試合を通して取られたダウンの数（赤 = Player、青 = Cpu）。ラウンドで体が作り直されても残す。</summary>
    private int _playerDowns;
    private int _cpuDowns;

    /// <summary><paramref name="self"/> から見た試合の時刻と点（<see cref="MatchSense"/>）。</summary>
    private MatchSense MatchSenseOf(Fighter self, Fighter opponent)
    {
        var mine = self == Player ? _playerDowns : _cpuDowns;
        var theirs = self == Player ? _cpuDowns : _playerDowns;
        return new MatchSense(
            RoundElapsed: _roundElapsed,
            RoundLength: Rules?.RoundSeconds ?? 0f,
            RoundsLeft: Rules is { } rules ? Math.Max(0, rules.Rounds - MatchRound) : 0,
            DamageLead: opponent.Condition.TotalDamage - self.Condition.TotalDamage,
            DownLead: theirs - mine,
            Quiet: _quiet);
    }

    /// <summary>試合の時計と点の動きを 1 歩ぶん数える（物理の歩の後。試合が動いている歩だけ時計を進める）。</summary>
    private void CountMatchClock(bool live, float dt)
    {
        var scored = Player.Condition.TotalDamage + Cpu.Condition.TotalDamage;
        if (scored != _scoredBefore)
        {
            _quiet = 0f;
            _scoredBefore = scored;
        }
        else if (live)
        {
            _quiet += dt;
        }

        if (live)
        {
            _roundElapsed += dt;
        }
    }

    /// <summary>新しいラウンドの始まり: ラウンドの時計と打ち合いの無い時間を 0 から数え直す（ダウンの数は試合を通して残す）。</summary>
    private void RestartMatchClock()
    {
        _roundElapsed = 0f;
        _quiet = 0f;
        _scoredBefore = Player is null || Cpu is null ? 0f : Player.Condition.TotalDamage + Cpu.Condition.TotalDamage;
    }

    /// <summary>組み技のための見え方（<see cref="GrappleSense"/>）。</summary>
    public static GrappleSense GrappleSenseOf(Fighter self, Fighter opponent)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(opponent);
        float Load(Fighter f, Hand h) => f.ElbowLockTorque(h) / MathF.Max(1e-6f, f.ElbowLigamentYield);
        return new GrappleSense(opponent.CenterOfMass, opponent.CenterOfMassVelocity, opponent.SupportMargin,
            opponent.ElbowPosition(Hand.Left), opponent.ElbowPosition(Hand.Right), opponent.WristPosition(Hand.Left), opponent.WristPosition(Hand.Right),
            opponent.AnklePosition(Hand.Left), opponent.AnklePosition(Hand.Right),
            self.IsHolding(Hand.Left), self.IsHolding(Hand.Right), self.IsGrabbed, Load(self, Hand.Left), Load(self, Hand.Right),
            Load(opponent, Hand.Left), Load(opponent, Hand.Right), self.SupportMargin);
    }

    public FighterSense PlayerSense => ArmedSense(Player, Cpu);

    /// <summary>CPU の入力元が今見ている物（調べもの・テスト用）。</summary>
    private IFighterControl? WeaponBot(Weapons.WeaponKind? weapon, Bots.WeaponStyle? style, Fighter self, int seed, MiniRing.Weapons.HandWear wear = MiniRing.Weapons.HandWear.Glove) => !_corners.UseWeaponBots ? null : weapon switch
    {
        // 値は武器の教科書（style が null ならその武器の組み込みの教科書。キャラなら教育を重ねた物が corners に入っている）。
        MiniRing.Weapons.WeaponKind.Katana or MiniRing.Weapons.WeaponKind.Longsword or MiniRing.Weapons.WeaponKind.Staff or MiniRing.Weapons.WeaponKind.Jo or MiniRing.Weapons.WeaponKind.Bat => Bots.FormSwordsman.Bot(weapon.Value, seed, self.ArmReach, _tuning, style ?? Bots.WeaponCatalog.Textbook(weapon.Value)), // 刀・ロングソード・棒・バットは、その武器の型で
        // 片手剣も型で動かす（右手の剣の経路 + 左手の物の経路）。左手の物（盾・グローブ）の見える大きさを、守りの覆う広さとして渡す。
        MiniRing.Weapons.WeaponKind.IronSword => Bots.FormSwordsman.Bot(weapon.Value, seed, self.ArmReach, _tuning, style ?? Bots.WeaponCatalog.Textbook(weapon.Value), MiniRing.Weapons.Weapon.CoverRadiusOf(MiniRing.Weapons.HandWears.Kind(wear)) / self.ArmReach),
        MiniRing.Weapons.WeaponKind.WoodenClub => Bots.ClubBot.FromStyle(style ?? Bots.WeaponCatalog.Textbook(weapon.Value), seed),
        _ => null,
    };

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
        ArenaLook.WriteAtmosphere(Display.Point(Layout.Center), Display.Length(Layout.Size), Display.Length(RingSurfaceHeight), _lookTime, _renderParticles); // 表示の空間
        var arena = _renderParticles.Count;
        _impacts.WriteParticles(_renderParticles);
        _debris.WriteTrails(_renderParticles);
        _wounds.WriteHalos(_renderParticles);
        Display.Particles(_renderParticles, arena); // 物理の空間 → 表示の空間
        return _renderParticles;
    }

    /// <summary>会場の照明（<see cref="ArenaLook"/>）。</summary>
    public SceneLighting BuildLighting() => _lighting;

    /// <summary>リングに落ちる影。</summary>
    public ShadowMapConfig? BuildShadow() => ArenaLook.Shadow(Display.Point(Layout.Center), Display.Length(Layout.Size), Display.Length(RingSurfaceHeight));

    /// <summary>物理の空間 → 表示の空間の変換（描画・カメラ・HUD が通す。試合の計算には入らない）。</summary>
    public DisplaySpace Display { get; } = DisplaySpace.Standard;

    /// <summary>画面用の仕上げ（光のにじみ・陰り・コントラスト）。</summary>
    public PostProcessSettings? BuildPostProcess() => _screenFinish;

    /// <summary>
    /// 両ボクサーの顔の窓（<see cref="MiniRing.FaceCam"/>）を出すか。描画だけの設定で、試合の計算・記録には響かない。
    /// ここでは窓の縁（HUD の図形）だけを描く。窓の中身はホストが <see cref="FaceCam.Views"/> で重ねる。
    /// </summary>
    public FaceCam? FaceCam { get; set; }

    /// <summary>
    /// 一人称（VR の <c>--first-person</c>）の目の描画で、自分の頭（付いている頭の部位のボクセル。髪・顔・目はその格子の中）を描かない。カメラが頭の中に入り、
    /// 目の前に自分の頭が来て邪魔になるのを避ける描画だけの設定: 物理・当たり・相手から見た姿・試合の計算・記録には響かない。切り落とされた頭（破片）は描く。
    /// </summary>
    public bool HidePlayerHead { get; set; }

    /// <summary>ホストが試合を進めている速さ（<see cref="GameSpeed"/>）。HUD に出すだけで、試合の計算には響かない（速さはホストが 1 歩の時間で決める）。</summary>
    public GameSpeed? Speed { get; set; }

    /// <summary>HUD の図形: スタミナのメーター（テレビ中継なら得点表と時計）、10 カウントの目盛り、潰れた目の側を暗くする帯（<see cref="BlindHud"/>）、顔の窓の縁（<see cref="FaceCam"/> があるときだけ）。</summary>
    public IReadOnlyList<UiShapeDraw> BuildOverlayShapes(Matrix4x4 viewProjection, Vector2 screenSizePixels)
        => [
            .. Rules is not null
                ? TvHud.Shapes(this, screenSizePixels)
                : [.. TvHud.StaminaBar(new Vector2(24f, 102f), StaminaBarWidth, 8f, Player),
                   .. TvHud.StaminaBar(new Vector2(screenSizePixels.X - 24f - StaminaBarWidth, 102f), StaminaBarWidth, 8f, Cpu)],
            .. DownCountHud.Shapes(this, viewProjection, screenSizePixels),
            .. BlindHud.Shapes(this, screenSizePixels),
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
            list.AddRange(TvHud.Text(this, screenSizePixels, RedLabel, BlueLabel)); // テレビ中継: 得点表と時計
        }
        else
        {
            list.Add(new($"{PlayerWins} - {CpuWins}", new Vector2(screenSizePixels.X * 0.5f, 20f), 28f, white, TextAnchor.Center));
            AddCondition(list, RedLabel, Cpu, new Vector2(24f, 20f), TextAnchor.Left, new Vector4(1f, 0.45f, 0.4f, 1f));
            AddCondition(list, BlueLabel, Player, new Vector2(screenSizePixels.X - 24f, 20f), TextAnchor.Right, new Vector4(0.5f, 0.65f, 1f, 1f));
        }

        if (LastHit is { } hit)
        {
            var who = hit.Attacker == Player ? RedLabel : BlueLabel;
            var text = $"{who} {(hit.Hand == Hand.Left ? "L" : "R")} > {HitZones.Label(hit.Zone)}  {hit.Damage:0}  " +
                $"{hit.ImpactMass:0.0}kg x {hit.Speed:0.0}m/s  BODY {Percent(hit.BodyShare)}";
            list.Add(new ScreenTextDraw(text, new Vector2(screenSizePixels.X * 0.5f, screenSizePixels.Y - 44f), 22f, white, TextAnchor.Center));
        }

        list.AddRange(DownCountHud.Text(this, viewProjection, screenSizePixels, f => f == Player ? RedLabel : BlueLabel,
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

        // 頭に重ねた顔の層（削れ・へこみ）。
        foreach (var fighter in _fighters)
        {
            if (fighter.Voxels?.Face is { } face)
            {
                Add(face.Body.Grid.FilledCount);
                Add(face.Body.Grid.Version);
            }
        }

        // 武器に重ねた手の格子（物 → 手の固定の順）。手が無ければ何も足さない（手の無い試合のチェックサムは変わらない）。
        foreach (var weapon in _weapons)
        {
            for (var i = 0; i < weapon.Hands.Count; i++)
            {
                Add(weapon.Hands[i].Body.Grid.FilledCount);
                Add(weapon.Hands[i].Body.Grid.Version);
            }
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

/// <summary>
/// 骨が断たれて部位が切り離された瞬間。<paramref name="Strength"/> は打撃の強さ（0..1）。<paramref name="Victim"/> と <paramref name="Body"/>
/// （切り離された部位の剛体 id）は実況の資料用（音や記録の再生では無いこともある）。
/// </summary>
public readonly record struct SeverEvent(Vector3 Position, float Strength, Fighter? Victim = null, int Body = -1);

/// <summary>体から外れていた部位（剛体 <paramref name="Body"/>）が、芽から生え直し始めた（VFC の再生: <see cref="Fighter.Regrow"/>）。</summary>
public readonly record struct RegrowthEvent(Fighter Fighter, int Body);

/// <summary>休みに武器を支給された（<see cref="MatchRules.ResupplyWeapons"/>）: 誰に、どの種類の武器を。</summary>
public readonly record struct WeaponSupplyEvent(Fighter Fighter, Weapons.WeaponKind Kind);

/// <summary>骨が折れて、肉だけでつながった（<paramref name="Bent"/> なら打撃の曲げで、でなければ削られて断たれた）。</summary>
public readonly record struct BoneBreakEvent(Fighter Victim, BodyPartKind Part, Vector3 Position, bool Bent, int Body = -1);

/// <summary>
/// ボクセルの物どうしが、何も壊さずにぶつかった瞬間（2026-11-12）。<paramref name="Pair"/> はぶつかった面の材質の組
/// （金属どうし・グローブと皮膚など）、<paramref name="Strength"/> は近づく速さから決めた強さ（0..1）。
/// </summary>
public readonly record struct ContactSoundEvent(Vector3 Position, Audio.ContactPair Pair, float Strength, Audio.ContactResonance? Resonance = null, float Energy = -1f);

/// <summary>
/// テレビ中継の試合の決まり（2026-11-12。秒は試合の時間 = 実物大の物理の時間）: 1 ラウンド <paramref name="RoundSeconds"/> 秒 × <paramref name="Rounds"/> ラウンド、ラウンドの間
/// <paramref name="BreakSeconds"/> 秒。休みの間、削れた体は一定の速さ（<paramref name="RegenVoxelsPerSecond"/> 個/秒）で内側から治る。
/// K.O. で試合が終わり、最終ラウンドの終わりは判定（与えたダメージ）。団体によってダウンを数えない（<see cref="CountsDowns"/>）・判定しない（<see cref="JudgesDecision"/>）・
/// 予算で治さない（<paramref name="RegenVoxelsPerSecond"/> = 0。前線の再生 <see cref="RegrowthSpeed"/> で治す <see cref="Vfc"/>）決まりもある。治る速さは団体のファイルの値（WVC はほぼ治らない）。
/// </summary>
/// <param name="RoundSeconds">1 ラウンドの長さ [s]。</param>
/// <param name="Rounds">ラウンドの数。</param>
/// <param name="BreakSeconds">ラウンドの間の休み [s]。</param>
/// <param name="RegenVoxelsPerSecond">休みの間に体が治る速さ [ボクセル/s]（1 人あたり。治る量は休みの長さ × これ。標準は重い 1 発で削れる数十〜百個を 1.7 秒ほどで: IVF の値。団体ごとの値は団体のファイル）。</param>
public sealed record MatchRules(float RoundSeconds = 50.35f, int Rounds = 5, float BreakSeconds = 16.78f, float RegenVoxelsPerSecond = 89.37f)
{
    /// <summary>意識を失う頭部角速度の倍率。1 は体の標準値。</summary>
    public float ConcussionSpinScale { get; init; } = 1f;

    /// <summary>腕・脚の骨の曲げ破断応力の倍率。1 は材質の標準値。</summary>
    public float LimbBendStrengthScale { get; init; } = 1f;

    /// <summary>試合の長さの合計 [s]（始めの待ち時間・全ラウンドと休み。決着の後の <see cref="EndSeconds"/> は含まない）。</summary>
    public float TotalSeconds => IntroSeconds + (RoundSeconds * Rounds) + (BreakSeconds * Math.Max(0, Rounds - 1));

    /// <summary>判定で引き分けとする、与えたダメージの差の割合（両者の平均に対して）。</summary>
    public float DrawShare { get; init; } = 0.05f;

    /// <summary>判定で引き分けとする、与えたダメージの差の下限（どちらもほとんど当てていないとき）。</summary>
    public float DrawMinimum { get; init; } = 1f;

    /// <summary>コーナーに着いたとみなす距離 [m]（それより近ければ歩かない）。</summary>
    public float CornerArrive { get; init; } = 0.08451f;

    /// <summary>コーナーの手前で歩みを緩め始める距離 [m]。</summary>
    public float CornerSlowDown { get; init; } = 0.2817f;

    /// <summary>コーナーへ歩く速さ（移動指令の大きさ 0..1。疲れた足取り）。</summary>
    public float RestWalk { get; init; } = 0.6f;

    /// <summary>休むときに拳を構えから下げる量（腕の長さ単位: 下へ・前へ）。</summary>
    public Vector3 RestGuardDrop { get; init; } = new(0f, -0.12f, 0.15f);

    /// <summary>
    /// 休みの間に皮膚のあざが半分に引く時間 [s]（見た目だけのルール。生理ではない）: 休み 33.6 秒（人形の単位で 20 秒）で中くらいのあざが半分ほどに見える。
    /// 0 以下ならあざは引かない。ラウンド中は引かない。臓器の打撲（別の物）には触れない。
    /// </summary>
    public float BruiseHalfLifeSeconds { get; init; } = 33.57f;

    /// <summary>
    /// ダウンを数えるか。false なら動かないままの K.O.（<see cref="BoxingGame.StillKoSeconds"/>）も 10 カウント K.O. も無い（死とリングアウトは今のまま決着する）。
    /// 倒れた体の状態（<c>DownTime</c> など）そのものは体の状態として残る（数えないだけ）。既定は数える（WVC）。
    /// </summary>
    public bool CountsDowns { get; init; } = true;

    /// <summary>
    /// 最終ラウンドの終わりに判定するか。false なら判定せず、両者生還の引き分け（<c>"DRAW — BOTH SURVIVE"</c>、勝ち数は増やさない）。既定は判定する（WVC）。
    /// </summary>
    public bool JudgesDecision { get; init; } = true;

    /// <summary>
    /// 試合の始め（入場してから最初のゴングまで）の長さ [s]。この間、選手はその場で始めのエモート（観客に手を振る・準備運動…）をし、ラウンドの時計・打撃は動かない。
    /// 0 なら試合は最初の歩からゴング（従来）。ゲームの決まりの値で、物理ではない。
    /// </summary>
    public float IntroSeconds { get; init; }

    /// <summary>
    /// 試合の終わり（決着の後）に、立っている選手が終わりのエモート（勝者は喜ぶ・敗者は悔しがる・相手を称える）をする長さ [s]（計画を立てる長さ。決着の後の撮影の長さと合わせる）。
    /// 0 なら決着の後は今までどおりコーナーへ歩くだけ。倒れている選手は倒れたまま。
    /// </summary>
    public float EndSeconds { get; init; }

    /// <summary>
    /// 傷の前線の再生の速さ [m/s]（<see cref="Voxels.BoxerBody.Regrow"/>: 生きているセルに接する壊れたセルが、確率で元に戻る。1 回の抽選の確率は
    /// 速さ × 間隔 / セルの大きさ = 1 列の前線が進む速さ。実際の前線は横からも届くので、測るとこの約 2.2 倍で進む: <see cref="Vfc"/>）。
    /// 0 なら前線の再生をしない（WVC は <c>RegenVoxelsPerSecond</c> の予算で内側から治す、別のモデル: docs/DEBT.md）。
    /// 値と根拠は <see cref="Vfc"/>。
    /// </summary>
    public float RegrowthSpeed { get; init; }

    /// <summary>前線の再生の抽選の間隔 [s]（1 回の抽選で前線は 速さ × 間隔 だけ進む。間隔が短いほど滑らかに見え、計算は増える）。</summary>
    public float RegrowthTickSeconds { get; init; } = 0.4196f;

    /// <summary>
    /// ラウンド中に両者が倒れたまま、どちらも意識だけでは立てない（<see cref="Fighter.BodyLetsStand"/> が偽）とき、前線の再生が倍の速さになる時間 [s]
    /// （倍率 2^(続いた時間 / これ)。どちらかが立てる体に戻れば 0 から）。0 ならラウンド中は再生しない。番組として止まった場面を短くする、ゲームの決まり。
    /// </summary>
    public float StuckDoublingSeconds { get; init; }

    /// <summary>
    /// 休みの間に、試合の始めに持っていた武器を失った選手へ、同じ種類の新しい武器を手に支給するか（手が武器を保持できるときだけ。1 回の休みに 1 本まで）。
    /// </summary>
    public bool ResupplyWeapons { get; init; }

    /// <summary>
    /// 1 ラウンドに取られたダウンがこの数に届いたら、その場で TKO（<c>"TKO — 3 KNOCKDOWNS"</c>）。0 なら無い（既定、WVC）。IVF は 3（3 ノックダウン制）。
    /// ダウンを数える団体（<see cref="CountsDowns"/>）でだけ効く。数は審判がダウンを取った回数（<see cref="Fighter.IsKnockedDown"/> になった回数）で、ラウンドの始まりに 0 へ戻る。
    /// </summary>
    public int KnockdownLimit { get; init; }

    /// <summary>テレビ中継の標準: 50.35 秒 × 5 ラウンド、休み 16.78 秒（人形の単位の 30 秒・10 秒を実物大の時間へ ×√L）。</summary>
    public static MatchRules Tv { get; } = new();

    /// <summary>
    /// VFC（何でもありの団体、ユーザーの指定 2026-11-18）: 50.35 秒 × 5 ラウンド、休み 41.96 秒（人形の単位の 30 秒・25 秒 × √L）。ダウンは数えず（<see cref="CountsDowns"/> false）、
    /// 判定もしない（<see cref="JudgesDecision"/> false）。決着は死だけで、5 ラウンドを終えて両者が生きていれば両者生還の引き分け。
    /// 傷は休みの間に前線の再生で少しずつ戻る（予算で治す <c>RegenVoxelsPerSecond</c> は 0）。皮膚のあざも休みに引く（33.6 s、WVC と同じ）。
    /// ラウンド中に両者が倒れて意識だけでは立てないときも再生し、続くほど速くなる。休みの間に、失った武器を同じ種類で支給する。
    /// スタミナ・意識・打撲は今の生理の速さで戻る。
    /// <para>再生の値（ユーザーの指定「軽い傷は休み中におおよそ再生、大きな欠損は回復しきらない程度」から決めた。測った値は docs/VFC.md「再生」）:
    /// 速さ 0.84 mm/s・抽選 0.42 s ごと（人形の単位で 0.5 mm/s・0.25 s。ボクセル 14 mm で 1 回の抽選の確率 0.025、相似で変わらない）。以下の測定は人形の単位: 指示書の初めの値 1.2 mm/s では、前腕（人形で 9 cm）を
    /// 肘から 3 割で切った切り株が休み 1 回で元の長さまで戻った（+6.5 cm）: 候補のセルは横のどの生きたセルからでも届くので、実際の前線は 1 列ぶんの
    /// 確率から見積もった速さの約 2.2 倍で進む。0.5 mm/s で測り直すと、拳の削れ（4 発・208 セル）は休み 1 回で 88% 戻り、前腕の切り株は休み 1 回で
    /// +3.0 cm（失った 6.5 cm の半分ほど）伸びる（<c>VfcRegrowthTests</c>）。両者ダウンの倍率は 5.03 s（人形の単位で 3 s）で倍。
    /// 2026-11-24 にユーザーの指定「VFC はもっと早く」で 1.5 倍の 1.26 mm/s（人形の単位で 0.75 mm/s）にした。2 倍（人形で 1.0 mm/s）は、初めの値 1.2 mm/s で
    /// 切り株が休み 1 回で元へ戻ったのに近く、「大きな欠損は回復しきらない」が残らないので 1.5 倍にした（測った値は docs/VFC.md「再生」）。</para>
    /// </summary>
    public static MatchRules Vfc { get; } = new(50.35f, 5, 41.96f, 0f)
    {
        CountsDowns = false,
        JudgesDecision = false,
        RegrowthSpeed = 0.001259f,
        RegrowthTickSeconds = 0.4196f,
        StuckDoublingSeconds = 5.035f,
        ResupplyWeapons = true,
    };
}
