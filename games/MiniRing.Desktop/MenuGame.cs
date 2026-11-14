using System.Numerics;
using Aigis.Physics;
using Aigis.Platform;
using Aigis.Player;
using Aigis.Render;
using Aigis.Render.Voxels;
using MiniRing.Replay;
using Silk.NET.Input;

namespace MiniRing.Desktop;

internal sealed record MatchSession(BoxingGame Game, IGame<BoxingInput> Host,
    Func<InputSnapshot, BoxingInput> Read, IDisposable Lifetime) : IDisposable
{
    public void Dispose() => Lifetime.Dispose();
}

internal sealed class MenuGame(MatchMenu menu, Func<MatchSelection, MatchSession> start,
    Func<ReplayData, MatchSession> playback) : IGame<BoxingInput>
{
    private readonly PhysicsWorld _menuWorld = new(Vector3.Zero);
    private MatchSession? _session;
    private bool _escapeHeld, _backHeld;

    public BoxingGame? ActiveGame => _session?.Game;
    public PhysicsWorld World => _session?.Host.World ?? _menuWorld;
    public string StatusLine => _session?.Host.StatusLine ?? "Game Select";
    public bool IsOver => false;
    public bool QuitRequested => menu.QuitRequested;

    // GameHost combines Escape and Start. Handle them separately in Read so Start can still reset.
    public bool HandleSystemMenuKey(bool held) => true;

    public BoxingInput Read(InputSnapshot input)
    {
        var escape = input.IsKeyDown(Key.Escape);
        var back = input.IsGamepadButtonDown(GamepadButton.East);
        var returnToMenu = input.WasKeyPressed(Key.Escape) || (escape && !_escapeHeld) || (back && !_backHeld);
        (_escapeHeld, _backHeld) = (escape, back);
        if (_session is { } session)
        {
            if (returnToMenu)
            {
                _session = null;
                session.Dispose();
                menu.Return(back);
                return default;
            }
            return session.Read(input);
        }

        var (match, replay) = menu.Read(input);
        if (match is not null)
        {
            _session = start(match);
        }
        else if (replay is not null)
        {
            try
            {
                _session = playback(replay);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException)
            {
                menu.PlaybackFailed(ex);
            }
        }
        return _session?.Read(input) ?? default;
    }

    public void Step(BoxingInput input, float dt) => _session?.Host.Step(input, dt);
    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables()
        => _session?.Host.BuildRenderables() ?? ([], []);
    public IReadOnlyList<VoxelVolumeDraw> BuildVoxelVolumes() => _session?.Host.BuildVoxelVolumes() ?? [];
    public IReadOnlyList<ParticleInstance> BuildParticles() => _session?.Host.BuildParticles() ?? [];
    public SceneLighting BuildLighting() => _session?.Host.BuildLighting() ?? SceneLighting.Default;
    public ShadowMapConfig? BuildShadow() => _session?.Host.BuildShadow();
    public PostProcessSettings? BuildPostProcess() => _session?.Host.BuildPostProcess();
    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 view, Vector2 size)
        => _session?.Host.BuildOverlayText(view, size) ?? menu.Text(size);
    public IReadOnlyList<UiShapeDraw> BuildOverlayShapes(Matrix4x4 view, Vector2 size)
        => _session?.Host.BuildOverlayShapes(view, size) ?? menu.Shapes(size);
    public void Dispose()
    {
        try { _session?.Dispose(); }
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
