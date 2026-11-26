using System.Numerics;
using Aigis.Physics;
using Aigis.Platform;
using Aigis.Player;
using Aigis.Render;
using Aigis.Render.Voxels;
using MiniRing.Replay;
using MiniRing.Presentation;
using Silk.NET.Input;

namespace MiniRing.Desktop;

/// <summary>メニューから始めた試合かリプレイ。<paramref name="Camera"/> があれば、その間はホストのカメラの代わりにこれを使う（リプレイの自由カメラ）。</summary>
/// <paramref name="Outcome"/> は試合の結果（終わっていなければ null。リプレイは持たない）。キャンペーンの戦績はこれで書く。
/// <paramref name="Settled"/> は試合が決着したか（<see cref="MatchFinish.Settled"/>。リプレイは持たない）、<paramref name="Summarize"/> は決着した試合の結果の画面の中身（引数はキャンペーンの試合か）。
internal sealed record MatchSession(IGame<BoxingInput> Host,
    Func<InputSnapshot, Vector2, BoxingInput> Read, IDisposable Lifetime, IMatchPresentation Presentation, IGameCamera? Camera = null,
    Func<MiniRing.Arena.Campaign.BoutOutcome?>? Outcome = null, Func<bool>? Settled = null, Func<bool, BoutSummary>? Summarize = null) : IDisposable
{
    public void Dispose()
    {
        try { Presentation.Dispose(); }
        finally { Lifetime.Dispose(); }
    }
}

/// <summary>
/// メニューと試合をつなぐ: メニューで選んだ試合を始め、試合が決着したら決着の瞬間を数秒見せて（<see cref="FinishGate"/>）結果の画面へ進む。
/// 決着する前に Esc で抜けた試合は記録しない（<see cref="MatchMenu.Return"/>）。決着した試合は <see cref="MatchMenu.Finish"/>（キャンペーンなら戦績を書く）。
/// </summary>
/// <param name="menu">メニュー。</param>
/// <param name="start">選んだ試合を始める。</param>
/// <param name="playback">記録を再生する。</param>
/// <param name="clock">画面の流れの時計 [s]（決着を見せる長さだけに使う。試合の計算には入らない）。null なら実時間。</param>
internal sealed class MenuGame(MatchMenu menu, Func<MatchSelection, MatchSession> start,
    Func<ReplayData, MatchSession> playback, Func<double>? clock = null) : IGame<BoxingInput>, IPresentationGame<BoxingInput>
{
    private readonly Func<double> _clock = clock ?? (() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
    private readonly FinishGate _finish = new();
    private FinishStage _stage;
    private bool _advanceHeld;
    private readonly PhysicsWorld _menuWorld = new(Vector3.Zero);
    private MatchSession? _session;
    private bool _escapeHeld;
    private Task<MatchSession>? _loading;
    private readonly List<Task> _retired = [];
    public bool IsLiveMatch { get; private set; }
    public bool IsLoadingMatch => _loading is not null;

    public IMatchPresentation? Presentation => _session?.Presentation;

    /// <summary>今の試合・リプレイが自分のカメラを持っていれば、それ（無ければ null でホストのカメラ）。</summary>
    public IGameCamera? Camera => _session?.Camera;
    public PhysicsWorld World => _session?.Host.World ?? _menuWorld;
    public string StatusLine => _session?.Host.StatusLine ?? "Game Select";
    public bool IsOver => false;
    public bool QuitRequested => menu.QuitRequested;

    // GameHost combines Escape and Start. Handle them separately in Read so Start can still reset.
    public bool HandleSystemMenuKey(bool held) => true;

    public BoxingInput Read(InputSnapshot input, Vector2 size)
    {
        PollLoading();
        // 試合からメニューへ戻るのはキーボードの Escape だけ。コントローラのボタンでは戻らない
        // （試合中に押し間違えて抜けていた。ユーザーの指定）。
        var escape = input.IsKeyDown(Key.Escape);
        var returnToMenu = input.WasKeyPressed(Key.Escape) || (escape && !_escapeHeld);
        _escapeHeld = escape;
        if (_session is { } session)
        {
            // 決着の後に進める押し: Enter・A・左クリック（押し下げの瞬間だけ）。
            var advanceDown = input.IsKeyDown(Key.Enter) || input.IsGamepadButtonDown(GamepadButton.South) || input.IsMouseButtonDown(MouseButton.Left);
            var advance = input.WasKeyPressed(Key.Enter) || input.WasMouseButtonPressed(MouseButton.Left) || (advanceDown && !_advanceHeld);
            _advanceHeld = advanceDown;
            _stage = _finish.Update(session.Settled?.Invoke() == true, advance, _clock());
            if (_stage == FinishStage.Advance || (returnToMenu && _stage != FinishStage.Playing))
            {
                Leave(session, settled: true, backHeld: false);
                return default;
            }

            if (returnToMenu)
            {
                Leave(session, settled: false, backHeld: input.IsGamepadButtonDown(GamepadButton.East));
                return default;
            }
            return session.Read(input, size);
        }

        if (IsLoadingMatch) { return default; }
        var (match, replay) = menu.Read(input, size);
        if (match is not null)
        {
            StartMatch(match);
        }
        else if (replay is not null)
        {
            _loading = Task.Run(() => playback(replay));
        }
        return _session?.Read(input, size) ?? default;
    }

    /// <summary>試合を片付けてメニューへ戻る。決着していれば結果の画面へ（キャンペーンなら戦績を書く）、決着の前に抜けたなら記録しない。</summary>
    private void Leave(MatchSession session, bool settled, bool backHeld)
    {
        _session = null;
        IsLiveMatch = false;
        _finish.Reset();
        _stage = FinishStage.Playing;
        var outcome = session.Outcome?.Invoke(); // 片付ける前に結果を読む
        var summary = settled ? session.Summarize?.Invoke(menu.InCampaignBout) : null;
        _retired.Add(Task.Run(session.Dispose));
        if (summary is not null)
        {
            menu.Finish(summary, outcome);
        }
        else
        {
            menu.Return(backHeld, settled ? outcome : null);
        }
    }

    internal void StartMatch(MatchSelection selection)
    {
        if (_loading is not null || _session is not null) { throw new InvalidOperationException("A match is already active or loading."); }
        _loading = Task.Run(() => start(selection));
    }

    internal void PollLoading()
    {
        foreach (var retired in _retired.Where(t => t.IsCompleted).ToArray())
        {
            try { retired.GetAwaiter().GetResult(); }
            catch (Exception error) { Console.Error.WriteLine($"Match cleanup failed: {error}"); menu.PlaybackFailed(error); }
            _retired.Remove(retired);
        }
        if (_loading is not { IsCompleted: true } loading) { return; }
        _loading = null;
        try
        {
            _session = loading.GetAwaiter().GetResult();
            IsLiveMatch = true;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Match loading failed: {error}");
            menu.PlaybackFailed(error);
        }
    }

    public void Step(BoxingInput input, float dt)
    {
        _session?.Host.Step(input, dt);
    }

    public void SubmitPresentation(BoxingInput input) => _session?.Presentation.SubmitPresentation(input);

    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables()
    {
        PollLoading();
        return _session?.Host.BuildRenderables() ?? ([], []);
    }
    public IReadOnlyList<VoxelVolumeDraw> BuildVoxelVolumes() => _session?.Host.BuildVoxelVolumes() ?? [];
    public IReadOnlyList<ParticleInstance> BuildParticles() => _session?.Host.BuildParticles() ?? [];
    public SceneLighting BuildLighting() => _session?.Host.BuildLighting() ?? SceneLighting.Default;
    public ShadowMapConfig? BuildShadow() => _session?.Host.BuildShadow();
    public PostProcessSettings? BuildPostProcess() => _session?.Host.BuildPostProcess();
    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 view, Vector2 size)
        => _session is { } session ? WithFinishHint(session.Host.BuildOverlayText(view, size), size) : (IsLoadingMatch
            ? [UiFont.Text("試合を準備中…", size * 0.5f, 32f, Vector4.One, TextAnchor.Center)]
            : menu.Text(size));

    /// <summary>決着を見せている間、進め方の 1 行を試合の文字に足す。</summary>
    private IReadOnlyList<ScreenTextDraw> WithFinishHint(IReadOnlyList<ScreenTextDraw> text, Vector2 size) => _stage == FinishStage.Ready
        ? [.. text, UiFont.Text("Enter / A / クリック: 結果へ", new Vector2(size.X * 0.5f, size.Y - 48f), 26f, new Vector4(1f, 0.85f, 0.35f, 1f), TextAnchor.Center)]
        : text;

    public IReadOnlyList<UiShapeDraw> BuildOverlayShapes(Matrix4x4 view, Vector2 size)
        => _session?.Host.BuildOverlayShapes(view, size) ?? menu.Shapes(size);
    public void Dispose()
    {
        try
        {
            if (_loading is { } loading)
            {
                try { _session = loading.GetAwaiter().GetResult(); }
                catch (Exception error) { Console.Error.WriteLine($"Match loading failed during shutdown: {error}"); }
            }
            try { _session?.Dispose(); }
            finally { Task.WhenAll(_retired).GetAwaiter().GetResult(); }
        }
        finally { _menuWorld.Dispose(); }
    }
}

internal sealed class SessionResources(IDisposable game, IDisposable? recording, IDisposable sound) : IDisposable
{
    public void Dispose()
    {
        try { sound.Dispose(); }
        finally
        {
            try { recording?.Dispose(); }
            finally { game.Dispose(); }
        }
    }
}
