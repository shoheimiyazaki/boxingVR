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
internal sealed record MatchSession(IGame<BoxingInput> Host,
    Func<InputSnapshot, Vector2, BoxingInput> Read, IDisposable Lifetime, IMatchPresentation Presentation, IGameCamera? Camera = null) : IDisposable
{
    public void Dispose()
    {
        try { Presentation.Dispose(); }
        finally { Lifetime.Dispose(); }
    }
}

internal sealed class MenuGame(MatchMenu menu, Func<MatchSelection, MatchSession> start,
    Func<ReplayData, MatchSession> playback) : IGame<BoxingInput>, IPresentationGame<BoxingInput>
{
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
            if (returnToMenu)
            {
                _session = null;
                IsLiveMatch = false;
                _retired.Add(Task.Run(session.Dispose));
                menu.Return(input.IsGamepadButtonDown(GamepadButton.East));
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
        => _session?.Host.BuildOverlayText(view, size) ?? (IsLoadingMatch
            ? [new ScreenTextDraw("試合を準備中…", size * 0.5f, 32f, Vector4.One, TextAnchor.Center)]
            : menu.Text(size));
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
