using System.Numerics;
using Aigis.Render.Voxels;
using MiniRing.Audio;
using MiniRing.Highlights;
using MiniRing.Voxels;

namespace MiniRing.Replay;

/// <summary>走査の設定。</summary>
/// <param name="FrameRate">場面を写す数 [枚 / 試合の秒]。描くときに前後を補間するので 30 で滑らかに見える。</param>
/// <param name="BudgetBytes">場面に使うメモリの見積もりの上限 [byte]。越えたら間引く（<see cref="ReplayFrameCache"/>）。</param>
/// <param name="FaceCam">顔の窓（null なら顔のカメラを写さない）。</param>
/// <param name="HudSize">試合の HUD を組む画面の大きさ（描画のスレッドが今の窓の大きさを返す）。null なら 1920×1080。</param>
public sealed record ReplayScanOptions(
    float FrameRate = 30f,
    long BudgetBytes = 384L * 1024 * 1024,
    FaceCam? FaceCam = null,
    Func<Vector2>? HudSize = null)
{
    /// <summary>場面 1 枚の大きさの見込み [byte]（測った値: 打ち合いの 8〜20 秒で平均 41〜90 KiB。体の色が変わるたびに写し直すので、打たれるほど大きい）。長い記録で最初から間隔を広げるのに使う。</summary>
    public const long ExpectedFrameBytes = 90 * 1024;
}

/// <summary>
/// 記録を裏のスレッドで一度だけ最初から最後まで再生し、再生・巻き戻し・シークに要る物を貯める: 一定間隔の場面（<see cref="Frames"/>）と、
/// 試合の展開（<see cref="Timeline"/>: 両者の状態の推移・打撃・見どころ・ラウンド）。
/// <para>
/// なぜ世界の状態のスナップショットから再シミュレーションしないか: Bepu の接触の記憶（warm start の衝撃）や選手・ボット・ボクセルの
/// すべての状態を写さないとビット単位で同じ続きにならず、それは試合の全部を複製することになる。物理は決定的なので、最初から 1 回だけ
/// 進めて場面を貯めれば、どの時刻へも同じ物を出せる（場面に写した <see cref="ReplayFrame.Checksum"/> が先頭から再生した試合と一致することを
/// テストが確かめる）。走査は実時間の数倍で進むので、ふつうの再生は走査に追いつかない。
/// </para>
/// <para>試合を変えない: 記録の入力を与えて進めるだけで、観測（<see cref="MatchHighlights"/>）は読むだけ。</para>
/// </summary>
public sealed class ReplayScanner : IDisposable
{
    private readonly ReplayData _data;
    private readonly ReplayScanOptions _options;
    private Thread? _thread;
    private volatile bool _cancel;
    private volatile float _scannedTime;
    private volatile bool _completed;
    private volatile Exception? _fault;
    private int _firstDivergence = -1;
    private int _checksumsMatched;

    public ReplayScanner(ReplayData data, ReplayScanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _options = options ?? new ReplayScanOptions();
        if (!(_options.FrameRate > 0f)) { throw new ArgumentOutOfRangeException(nameof(options), "FrameRate は正の数です。"); }
        // ReplayPlayer.Time と同じく float で 1 歩ずつ足す（足し方が違うと、長い記録で終わりの時刻が食い違う）。
        var duration = 0f;
        foreach (var step in data.Steps) { duration += step.Dt; }
        Duration = duration;
        // 長い記録は最初から間隔を広げる（全部を写したときに上限へ収まる間隔）。それでも越えたら間引く。
        var fits = Duration * ReplayScanOptions.ExpectedFrameBytes / _options.BudgetBytes;
        Frames = new ReplayFrameCache(MathF.Max(1f / _options.FrameRate, fits), _options.BudgetBytes);
        Header = data.Header;
    }

    public ReplayHeader Header { get; }

    /// <summary>記録の長さ [s]。</summary>
    public float Duration { get; }

    public ReplayFrameCache Frames { get; }

    public ReplayTimeline Timeline { get; } = new();

    /// <summary>走査し終えた試合の時刻 [s]。再生とシークはここまで。</summary>
    public float ScannedTime => _scannedTime;

    /// <summary>最後まで走査したか。</summary>
    public bool Completed => _completed;

    /// <summary>走査が例外で止まった（null なら止まっていない）。</summary>
    public Exception? Fault => _fault;

    /// <summary>記録のチェックサムと最初に食い違ったステップ（null なら一致している）。</summary>
    public int? FirstDivergence { get { var d = Volatile.Read(ref _firstDivergence); return d < 0 ? null : d; } }

    public int ChecksumsMatched => Volatile.Read(ref _checksumsMatched);

    /// <summary>赤（プレイヤー側）の名前（走査が試合を作るまでは "RED"）。</summary>
    public string RedLabel { get; private set; } = "RED";

    /// <summary>青（CPU 側）の名前。</summary>
    public string BlueLabel { get; private set; } = "BLUE";

    /// <summary>裏のスレッドで走査を始める（描画を邪魔しないよう、優先度を下げる）。</summary>
    public void Start()
    {
        if (_thread is not null) { throw new InvalidOperationException("走査はもう始まっている。"); }
        _thread = new Thread(() =>
        {
            try { Run(); }
            catch (Exception error)
            {
                _fault = error;
                Console.Error.WriteLine($"Replay scan failed: {error}");
            }
        })
        {
            IsBackground = true,
            Name = "Replay scan",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    /// <summary>このスレッドで最後まで走査する（テスト・道具用）。</summary>
    public void ScanAll()
    {
        if (_thread is not null) { throw new InvalidOperationException("走査はもう裏で始まっている。"); }
        Run();
    }

    public void Dispose()
    {
        _cancel = true;
        _thread?.Join();
    }

    private void Run()
    {
        using var player = new ReplayPlayer(_data);
        var game = player.Game;
        game.FaceCam = _options.FaceCam;
        RedLabel = game.RedLabel;
        BlueLabel = game.BlueLabel;
        var producer = new VoxelSnapshotProducer();
        var highlights = new MatchHighlights(matchSeconds: MathF.Max(1f, Duration));
        var sound = new SoundAccumulator();
        var red = new BodyCount();
        var blue = new BodyCount();
        var publishedHighlights = 0;
        var publishedBeliefs = 0;
        var redWinChance = 0.5f;
        var round = -1;
        var nextSample = 0f;

        void Sample()
        {
            if (game.Round != round)
            {
                round = game.Round;
                Timeline.AddMark(new ReplayMark(player.Time, ReplayMarkKind.RoundStart, default, true, round, $"R{round}"));
            }

            if (player.Time + 1e-5f < nextSample) { return; }
            nextSample = player.Time + ReplayTimeline.SampleInterval;
            Timeline.AddSample(new ReplaySample(player.Time, game.Round, game.InBreak,
                Side(game.Player, red), Side(game.Cpu, blue), redWinChance));
        }

        Frames.Add(Capture(player, producer, sound.Take(), out var bytes), bytes);
        var nextCapture = Frames.Interval;
        Sample();
        while (!player.Finished && !_cancel)
        {
            var dt = _data.Steps[player.Position].Dt;
            player.StepOnce();
            sound.Add(game, dt);
            highlights.Observe(game, player.Time, dt);
            foreach (var hit in HighlightSource.Hits(game))
            {
                Timeline.AddMark(new ReplayMark(player.Time, ReplayMarkKind.Hit, default, ReferenceEquals(hit.Attacker, game.Player),
                    hit.Energy, $"{HitZones.Label(hit.Zone)} {hit.Energy:0.0} J"));
            }

            publishedHighlights = PublishHighlights(highlights, publishedHighlights);
            var beliefs = highlights.Beliefs;
            if (beliefs.Count > publishedBeliefs)
            {
                redWinChance = WinModel.Default.Predict(beliefs[^1].Features);
                publishedBeliefs = beliefs.Count;
            }

            Sample();
            if (player.Time + 1e-5f >= nextCapture || player.Finished)
            {
                Frames.Add(Capture(player, producer, sound.Take(), out bytes), bytes);
                nextCapture = player.Time + Frames.Interval;
            }

            Volatile.Write(ref _checksumsMatched, player.ChecksumsMatched);
            if (player.FirstDivergence is { } diverged) { Volatile.Write(ref _firstDivergence, diverged); }
            _scannedTime = Frames.LastTime;
        }

        if (_cancel) { return; }
        highlights.Finish();
        PublishHighlights(highlights, publishedHighlights);
        _scannedTime = Frames.LastTime;
        _completed = true;
    }

    private int PublishHighlights(MatchHighlights highlights, int published)
    {
        var events = highlights.Events;
        for (; published < events.Count; published++)
        {
            var e = events[published];
            Timeline.AddMark(new ReplayMark(e.Time, ReplayMarkKind.Highlight, e.Kind, e.Red, e.Intensity, e.Note));
        }

        return published;
    }

    private static ReplaySideSample Side(Fighter fighter, BodyCount count) =>
        new(fighter.Consciousness, fighter.Stamina, count.Remaining(fighter), fighter.IsKnockedDown);

    /// <summary>今の場面を写す。bytes は大きさの見積もり（ボクセルの色の写しは除く。共有されるので <see cref="ReplayFrameCache"/> が数える）。</summary>
    private ReplayFrame Capture(ReplayPlayer player, VoxelSnapshotProducer producer, ReplayFrameSound sound, out long bytes)
    {
        var game = player.Game;
        var hud = _options.HudSize?.Invoke() ?? new Vector2(1920f, 1080f);
        if (!(hud.X >= 1f && hud.Y >= 1f)) { hud = new Vector2(1920f, 1080f); }
        var (instances, lines) = game.BuildRenderables();
        var volumes = game.BuildVoxelVolumes();
        var particles = game.BuildParticles().ToArray();
        bytes = 1024 + (instances.Count * 160L) + (particles.Length * 64L) + (volumes.Count * 96L);
        // 10 カウントの表示は世界の点に付くので、写した HUD からは外し（どこにも映らない行列を渡す）、描くときに今のカメラで描き直す。
        var hidden = default(Matrix4x4);
        var display = game.Display;
        var ringCenter = display.Point(game.Layout.Center with { Y = BoxingGame.RingSurfaceHeight + 0.563f });
        var counts = game.CountsDowns && !game.RoundOver;
        var focus = new ReplayFocus(display.Point(game.Player.ChestPosition), display.Point(game.Cpu.ChestPosition), ringCenter,
            counts && game.Player.IsKnockedDown ? DownCountHud.Count(game.Player) : 0,
            counts && game.Cpu.IsKnockedDown ? DownCountHud.Count(game.Cpu) : 0);
        var text = game.BuildOverlayText(hidden, hud).ToArray();
        var shapes = game.BuildOverlayShapes(hidden, hud).ToArray();
        bytes += (text.Length * 96L) + (shapes.Length * 112L);
        return new ReplayFrame(player.Time, player.Position, game.StateChecksum(), [.. instances], [.. lines], particles,
            producer.Capture(volumes), game.BuildLighting(), game.BuildShadow(), game.BuildPostProcess(), hud, text, shapes,
            _options.FaceCam?.Camera(game, game.Player, 1f), _options.FaceCam?.Camera(game, game.Cpu, 1f), focus, sound);
    }

    /// <summary>体の残り: 最初に見たセルの数に対する今の数（ラウンドで選手の物が作り直されても、数は最初のものを基準にする）。</summary>
    private sealed class BodyCount
    {
        private int _start;

        public float Remaining(Fighter fighter)
        {
            var filled = HighlightSource.Grids(fighter).Sum(g => g.Grid.FilledCount);
            if (_start <= 0) { _start = filled; }
            return _start > 0 ? Math.Clamp(filled / (float)_start, 0f, 1f) : 1f;
        }
    }

    /// <summary>場面と場面の間に鳴った音を貯める（<see cref="HitSoundGame"/> の後で鳴らす形と同じ物）。</summary>
    private sealed class SoundAccumulator
    {
        private readonly List<ImpactCue> _cues = [];
        private readonly List<ShatterEvent> _shatters = [];
        private readonly List<DebrisLanding> _landings = [];
        private readonly List<SwingEvent> _swings = [];
        private readonly List<SeverEvent> _severs = [];
        private RoundBell _bell;
        private float _dt;

        public void Add(BoxingGame game, float dt)
        {
            var shatters = game.RecentShatters.Select(s => s with { Grains = s.Grains.ToArray() }).ToArray();
            _cues.AddRange(ImpactAudio.Gather(game.RecentHits, game.RecentContacts, shatters, true, [.. game.WeaponHits, .. game.SelfWeaponHits]));
            _shatters.AddRange(shatters);
            _landings.AddRange(game.RecentLandings);
            _swings.AddRange(game.RecentSwings);
            _severs.AddRange(game.RecentSevers.Select(s => s with { Victim = null }));
            _bell = game.RecentBell != RoundBell.None ? game.RecentBell : _bell;
            _dt += dt;
        }

        public ReplayFrameSound Take()
        {
            var taken = new ReplayFrameSound(_dt, [.. _cues], [.. _shatters], [.. _landings], [.. _swings], _bell, [.. _severs]);
            _cues.Clear();
            _shatters.Clear();
            _landings.Clear();
            _swings.Clear();
            _severs.Clear();
            _bell = RoundBell.None;
            _dt = 0f;
            return taken.IsSilent && taken.Dt == 0f ? ReplayFrameSound.Silent : taken;
        }
    }
}
