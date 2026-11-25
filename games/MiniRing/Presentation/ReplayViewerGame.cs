using System.Globalization;
using System.Numerics;
using Aigis.Physics;
using Aigis.Player;
using Aigis.Render;
using Aigis.Render.Voxels;
using MiniRing.Audio;
using MiniRing.Replay;

namespace MiniRing.Presentation;

/// <summary>
/// リプレイを見るための画面（ホストに渡す <see cref="IGame{TInput}"/>）。試合は進めず、裏の走査（<see cref="ReplayScanner"/>）が貯めた場面を、
/// 再生ヘッド（<see cref="Transport"/>）の時刻で前後の 2 枚から補間して描く。だから巻き戻し・スロー・コマ送り・シークは即座で、何度見ても同じ。
/// 操作（キー・マウス・カメラ）はホスト側（デスクトップの <c>ReplayControls</c>）が持ち、ここは時刻・表示の切り替え・描画だけ。
/// </summary>
public sealed class ReplayViewerGame : IGame<BoxingInput>, IMatchPresentation
{
    /// <summary>この速さまでは音を鳴らす（それより速い早送り・巻き戻し・シークでは鳴らさない）。</summary>
    public const float MaxSoundRate = 2.01f;

    private readonly ReplayScanner _scanner;
    private readonly VoxelSnapshotConsumer _voxels = new();
    private readonly PhysicsWorld _debugWorld = new(Vector3.Zero);
    private readonly HitSoundPlayer? _sounds;
    private readonly ReliableEventWorker<Action>? _soundWorker;
    private readonly FaceCam? _faceCam;
    private readonly object _canvasGate = new();
    private Vector2 _canvas = new(1920f, 1080f);
    private Pinned _pinned;
    private string _toast = "";
    private float _toastUntil;
    private float _clock;
    private int _disposed;

    private readonly record struct Pinned(ReplayFrame From, ReplayFrame To, float Blend, PrimitiveInstance[] Instances, VoxelSnapshot Voxels)
    {
        public ReplayFrame Nearer => Blend < 0.5f ? From : To;
    }

    /// <param name="data">記録。</param>
    /// <param name="sounds">鳴らす先（null なら音なし）。この画面と一緒に破棄する。</param>
    /// <param name="faceCam">顔の窓（null なら出さない）。</param>
    /// <param name="budgetBytes">場面に使うメモリの見積もりの上限（null なら既定）。</param>
    public ReplayViewerGame(ReplayData data, HitSoundPlayer? sounds = null, FaceCam? faceCam = null, long? budgetBytes = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        _faceCam = faceCam;
        _scanner = new ReplayScanner(data, new ReplayScanOptions(
            BudgetBytes: budgetBytes ?? new ReplayScanOptions().BudgetBytes, FaceCam: faceCam, HudSize: () => HudCanvas));
        Transport = new ReplayTransport(_scanner.Duration);
        _sounds = sounds;
        _soundWorker = sounds is null ? null : new ReliableEventWorker<Action>(static play => play());
        try
        {
            _scanner.Start();
            // 最初の場面（試合の開始前）は走査を始めてすぐに写る。それまでは描く物が無いので待つ。
            var waited = 0;
            while (_scanner.Frames.Count == 0 && _scanner.Fault is null && waited++ < 6000) { Thread.Sleep(5); }
            if (_scanner.Fault is { } fault) { throw new InvalidOperationException("リプレイを読み込めませんでした。", fault); }
            if (_scanner.Frames.Count == 0) { throw new TimeoutException("リプレイの最初の場面が 30 秒たっても写らない。"); }
            _pinned = Pin();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public ReplayTransport Transport { get; }

    public ReplayScanner Scanner => _scanner;

    public ReplayTimeline Timeline => _scanner.Timeline;

    public bool ShowPanel { get; set; } = true;

    public bool ShowGameHud { get; set; } = true;

    public bool ShowHelp { get; set; }

    public bool ShowFaces { get; set; } = true;

    /// <summary>マウスの位置（シークのバーの上なら時刻と印の説明を出す）。</summary>
    public Vector2? Hover { get; set; }

    /// <summary>カメラの方式の説明（画面の右上）。</summary>
    public string CameraLabel { get; set; } = "";

    /// <summary>操作の一覧（[H]）。</summary>
    public IReadOnlyList<(string Keys, string Action)> HelpRows { get; set; } = [];

    /// <summary>試合の HUD を描く枠（パネルを出している間はその上の部分）。走査が HUD を組む大きさにも使う。</summary>
    public Vector2 HudCanvas { get { lock (_canvasGate) { return _canvas; } } }

    /// <summary>今描いている場面の、カメラが追う点（前後の場面の間を補間）。</summary>
    public ReplayFocus Focus
    {
        get
        {
            var p = _pinned;
            var a = p.From.Focus;
            var b = p.To.Focus;
            return a with
            {
                RedChest = Vector3.Lerp(a.RedChest, b.RedChest, p.Blend),
                BlueChest = Vector3.Lerp(a.BlueChest, b.BlueChest, p.Blend),
                RedCount = p.Nearer.Focus.RedCount,
                BlueCount = p.Nearer.Focus.BlueCount,
            };
        }
    }

    public string RedLabel => _scanner.RedLabel;

    public string BlueLabel => _scanner.BlueLabel;

    public PhysicsWorld World => _debugWorld;

    public bool IsOver => false;

    /// <summary>変わったときだけ出力される行なので、刻々と変わる時刻は入れない。</summary>
    public string StatusLine => _scanner.FirstDivergence is { } step ? $"REPLAY DIVERGED at step {step}"
        : _scanner.Fault is not null ? "REPLAY scan failed" : _scanner.Completed ? "REPLAY scanned, all checksums match" : "REPLAY";

    private string Verdict => _scanner.FirstDivergence is { } step ? $"記録とずれた（歩 {step}）"
        : _scanner.Fault is not null ? "読み込みに失敗"
        : $"記録と一致 {_scanner.ChecksumsMatched}";

    /// <summary>画面の下に短い知らせを出す（A を置いた、など）。</summary>
    public void Toast(string message)
    {
        _toast = message;
        _toastUntil = _clock + 1.6f;
    }

    /// <summary>実時間を進める（ホストの 1 回の更新ごと）。前へ等速くらいで進んだ分の音を鳴らす。</summary>
    public void Update(float realSeconds)
    {
        if (!(realSeconds > 0f)) { realSeconds = 0f; }
        _clock += realSeconds;
        var before = Transport.Time;
        Transport.Advance(MathF.Min(realSeconds, 0.1f), _scanner.ScannedTime);
        var after = Transport.Time;
        if (_sounds is null || _soundWorker is null || Transport.Rate <= 0f || Transport.Rate > MaxSoundRate || after - before > 0.5f)
        {
            return;
        }

        foreach (var frame in _scanner.Frames.Between(before, after))
        {
            var sound = frame.Sound;
            if (sound.IsSilent) { continue; }
            var sounds = _sounds;
            var cues = sounds.PlayHits ? sound.Cues : [.. sound.Cues.Where(c => c.Hit is null)];
            _soundWorker.Enqueue(() =>
            {
                sounds.PlayImpactCues(sound.Dt, cues, sound.Shatters);
                sounds.PlayLandings(sound.Landings);
                sounds.PlaySwings(sound.Swings);
                sounds.PlayBell(sound.Bell);
                sounds.PlaySevers(sound.Severs);
            });
        }
    }

    public void Step(BoxingInput input, float dt) { }

    public void SubmitPresentation(BoxingInput input) { }

    private Pinned Pin()
    {
        if (!_scanner.Frames.TryGetPair(Transport.Time, out var from, out var to, out var blend))
        {
            return _pinned;
        }

        return new Pinned(from, to, blend, PoseBlend.Instances(from.Instances, to.Instances, blend),
            VoxelSnapshot.Blend(from.Voxels, to.Voxels, blend, PoseBlend.Matrix));
    }

    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables()
    {
        _soundWorker?.ThrowIfFaulted();
        _pinned = Pin(); // この描画フレームの間は同じ場面を使う
        return (_pinned.Instances, _pinned.Nearer.Lines);
    }

    public IReadOnlyList<VoxelVolumeDraw> BuildVoxelVolumes() => _voxels.Resolve(_pinned.Voxels);

    public IReadOnlyList<ParticleInstance> BuildParticles() => _pinned.Nearer.Particles;

    public SceneLighting BuildLighting() => _pinned.Nearer.Lighting;

    public ShadowMapConfig? BuildShadow() => _pinned.Nearer.Shadow;

    public PostProcessSettings? BuildPostProcess() => _pinned.Nearer.PostProcess;

    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 viewProjection, Vector2 screenSizePixels)
    {
        UpdateCanvas(screenSizePixels);
        var frame = _pinned.Nearer;
        var list = new List<ScreenTextDraw>();
        if (ShowGameHud)
        {
            var (scale, size) = HudScale(frame);
            list.AddRange(frame.Text.Select(t => t with { ScreenPosPixels = t.ScreenPosPixels * scale, PixelHeight = t.PixelHeight * size }));
            DownTags(list, viewProjection, screenSizePixels);
        }

        if (ShowPanel)
        {
            list.AddRange(Panel(screenSizePixels).Text);
        }

        if (ShowHelp)
        {
            list.AddRange(ReplayHud.Help(screenSizePixels, HelpRows).Text);
        }

        if (_toast.Length > 0 && _clock < _toastUntil)
        {
            var y = (ShowPanel ? screenSizePixels.Y - ReplayHud.PanelHeight : screenSizePixels.Y) - 74f;
            list.Add(new ScreenTextDraw(_toast, new Vector2(screenSizePixels.X * 0.5f, y), 24f, new Vector4(1f, 0.9f, 0.5f, 1f), TextAnchor.Center));
        }

        return list;
    }

    public IReadOnlyList<UiShapeDraw> BuildOverlayShapes(Matrix4x4 viewProjection, Vector2 screenSizePixels)
    {
        UpdateCanvas(screenSizePixels);
        var frame = _pinned.Nearer;
        var list = new List<UiShapeDraw>();
        if (ShowGameHud)
        {
            var (scale, size) = HudScale(frame);
            list.AddRange(frame.Shapes
                .Where(s => ShowFaces || _faceCam is null || !IsFaceFrame(s, frame.HudSize))
                .Select(s => s with { CenterPixels = s.CenterPixels * scale, SizePixels = s.SizePixels * scale, BorderPixels = s.BorderPixels * size }));
        }

        if (ShowPanel)
        {
            list.AddRange(Panel(screenSizePixels).Shapes);
        }

        if (ShowHelp)
        {
            list.AddRange(ReplayHud.Help(screenSizePixels, HelpRows).Shapes);
        }

        return list;
    }

    /// <summary>顔の窓（前後の場面のうち近い方の、顔に向けたカメラ）。</summary>
    public IReadOnlyList<CameraView>? Views(IGameCamera main, Vector2 size)
    {
        ArgumentNullException.ThrowIfNull(main);
        var frame = _pinned.Nearer;
        if (!ShowFaces || _faceCam is null || frame.RedFace is not { } red || frame.BlueFace is not { } blue) { return null; }
        var canvas = CanvasFor(size); // この画面の大きさから（窓を変えた直後に前の大きさの枠がはみ出さないように）
        return
        [
            CameraView.Create(new(0, 0, Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y)), main.Eye, main.ViewProjection),
            CameraView.Create(_faceCam.Panel(canvas, FaceSide.Left), red.Eye, _ => red.Matrix, isOverlay: true),
            CameraView.Create(_faceCam.Panel(canvas, FaceSide.Right), blue.Eye, _ => blue.Matrix, isOverlay: true),
        ];
    }

    /// <summary>何度呼んでもよい（デスクトップの試合の枠が画面と後片付けの両方として持つ）。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        try { _scanner.Dispose(); }
        finally
        {
            try { _soundWorker?.Dispose(); }
            finally
            {
                try { _sounds?.Dispose(); }
                finally { _debugWorld.Dispose(); }
            }
        }
    }

    private (List<UiShapeDraw> Shapes, List<ScreenTextDraw> Text) Panel(Vector2 screen)
    {
        var (samples, marks, _) = Timeline.Snapshot();
        var state = new ReplayHudState(Transport.Time, Transport.Duration, _scanner.Completed ? Transport.Duration : _scanner.ScannedTime,
            Transport.Rate, Transport.Speed, Transport.LoopStart, Transport.LoopEnd, CameraLabel, Verdict, Hover, RedLabel, BlueLabel);
        return ReplayHud.Draw(screen, state, samples, marks);
    }

    /// <summary>倒れた選手の上の「名前 DOWN」と数（世界の点に付くので、今のカメラで描き直す）。</summary>
    private void DownTags(List<ScreenTextDraw> list, Matrix4x4 viewProjection, Vector2 screen)
    {
        var focus = Focus;
        void Tag(Vector3 chest, int count, string name, Vector4 color)
        {
            if (count < 1 || !Camera.TryWorldToScreen(viewProjection, chest + new Vector3(0f, 0.12f, 0f), screen, out var pixel)) { return; }
            list.Add(new ScreenTextDraw($"{name} DOWN", pixel - new Vector2(0f, 64f), 20f, color, TextAnchor.Center));
            list.Add(new ScreenTextDraw(count.ToString(CultureInfo.InvariantCulture), pixel - new Vector2(0f, 42f), 40f, new Vector4(1f, 0.82f, 0.3f, 1f), TextAnchor.Center));
        }

        Tag(focus.RedChest, focus.RedCount, RedLabel, ReplayHud.Red);
        Tag(focus.BlueChest, focus.BlueCount, BlueLabel, ReplayHud.Blue);
    }

    /// <summary>写した HUD を今の枠へ伸ばす倍率（位置）と、文字・線の太さの倍率。</summary>
    private (Vector2 Scale, float Size) HudScale(ReplayFrame frame)
    {
        var canvas = HudCanvas;
        var scale = new Vector2(canvas.X / MathF.Max(1f, frame.HudSize.X), canvas.Y / MathF.Max(1f, frame.HudSize.Y));
        return (scale, MathF.Min(scale.X, scale.Y));
    }

    /// <summary>写した HUD の中の顔の窓の縁か（写したときの大きさの枠で比べる）。</summary>
    private bool IsFaceFrame(UiShapeDraw shape, Vector2 canvas)
    {
        if (_faceCam is null) { return false; }
        foreach (var side in new[] { FaceSide.Left, FaceSide.Right })
        {
            var p = _faceCam.Panel(canvas, side);
            var center = new Vector2(p.XPx + (p.WidthPx * 0.5f), p.YPx + (p.HeightPx * 0.5f));
            if (Vector2.Distance(center, shape.CenterPixels) < 12f && shape.SizePixels.X >= p.WidthPx * 0.8f) { return true; }
        }

        return false;
    }

    private void UpdateCanvas(Vector2 screen)
    {
        var canvas = CanvasFor(screen);
        lock (_canvasGate) { _canvas = canvas; }
    }

    private Vector2 CanvasFor(Vector2 screen) => ShowPanel ? new Vector2(screen.X, MathF.Max(1f, screen.Y - ReplayHud.PanelHeight)) : screen;
}
