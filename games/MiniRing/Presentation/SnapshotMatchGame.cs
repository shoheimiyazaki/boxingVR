using System.Diagnostics;
using System.Numerics;
using Aigis.Physics;
using Aigis.Player;
using Aigis.Render;
using Aigis.Render.Voxels;
using MiniRing.Audio;

namespace MiniRing.Presentation;

/// <summary>Desktop presentation boundary. The live game is accessed only by the worker.</summary>
public sealed class SnapshotMatchGame : IGame<BoxingInput>, IMatchPresentation
{
    public sealed record Scene(long Step, int Round, FighterSense Player, FighterSense Cpu,
        PrimitiveInstance[] Instances, DebugLine[] Lines, ParticleInstance[] Particles, VoxelSnapshot Voxels,
        SceneLighting Lighting, ShadowMapConfig? Shadow, PostProcessSettings? PostProcess,
        string Status, bool IsOver, ScreenTextDraw[] Text, UiShapeDraw[] Shapes,
        (Matrix4x4 Matrix, Vector3 Eye)? RedFace, (Matrix4x4 Matrix, Vector3 Eye)? BlueFace);

    private sealed record OverlayRequest(Matrix4x4 Matrix, Vector2 Size);
    private readonly FixedSimulation<BoxingInput, Scene> _simulation;
    private readonly VoxelSnapshotConsumer _voxels = new();
    private readonly PhysicsWorld _debugWorld = new(Vector3.Zero);
    private readonly HitSoundGame? _sound;
    private readonly FaceCam? _faceCam;
    private readonly Func<float> _speed;
    private OverlayRequest _overlay = new(Matrix4x4.Identity, new Vector2(1280, 720));
    private Scene _frame;

    public SnapshotMatchGame(BoxingGame game, IGame<BoxingInput> source, Func<float>? speed = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(source);
        _sound = source as HitSoundGame;
        _faceCam = game.FaceCam;
        _speed = speed ?? (() => 1f);
        var producer = new VoxelSnapshotProducer();
        Scene Capture(long step)
        {
            var overlay = Volatile.Read(ref _overlay);
            var (instances, lines) = source.BuildRenderables();
            FighterSense Freeze(FighterSense sense) => sense with
            {
                OpponentTargets = sense.OpponentTargets?.Perceived(sense.OpponentHead, sense.OpponentHead, sense.Self.Origin, 1f),
            };
            return new(step, game.Round, Freeze(game.PlayerSense), Freeze(game.CpuSense),
                instances.ToArray(), lines.ToArray(), source.BuildParticles().ToArray(), producer.Capture(source.BuildVoxelVolumes()),
                source.BuildLighting(), source.BuildShadow(), source.BuildPostProcess(), source.StatusLine, source.IsOver,
                source.BuildOverlayText(overlay.Matrix, overlay.Size).ToArray(), source.BuildOverlayShapes(overlay.Matrix, overlay.Size).ToArray(),
                _faceCam?.Camera(game, game.Player, 1f), _faceCam?.Camera(game, game.Cpu, 1f));
        }
        _frame = Capture(-1);
        var published = _frame;
        var nextCapture = 0L;
        // 90 Hz copies suffice for presentation; every physical step still records input and sound.
        _simulation = new(_frame, default,
            (old, next) => next with { ResetRequested = old.ResetRequested || next.ResetRequested },
            input => input with { ResetRequested = false }, (step, input, dt) =>
            {
                source.Step(input, dt);
                var now = Stopwatch.GetTimestamp();
                if (now >= nextCapture)
                {
                    published = Capture(step);
                    nextCapture = now + Stopwatch.Frequency / 90;
                }
                return published;
            });
    }

    public Scene Latest => _simulation.Latest;
    public long CompletedSteps => _simulation.CompletedSteps;
    public double MaximumStepMilliseconds => _simulation.MaximumStepMilliseconds;
    public PhysicsWorld World => _debugWorld;
    public string StatusLine => _frame.Status;
    public bool IsOver => _frame.IsOver;
    public void Step(BoxingInput input, float dt) => SubmitPresentation(input);
    public void SubmitPresentation(BoxingInput input) => _simulation.Submit(input, _speed());

    public (IReadOnlyList<PrimitiveInstance> Instances, IReadOnlyList<DebugLine> Lines) BuildRenderables()
    {
        _frame = Latest; // Pin one complete snapshot for all scene/HUD/camera reads this frame.
        _sound?.PresentSounds();
        return (_frame.Instances, _frame.Lines);
    }
    public IReadOnlyList<VoxelVolumeDraw> BuildVoxelVolumes() => _voxels.Resolve(_frame.Voxels);
    public IReadOnlyList<ParticleInstance> BuildParticles() => _frame.Particles;
    public SceneLighting BuildLighting() => _frame.Lighting;
    public ShadowMapConfig? BuildShadow() => _frame.Shadow;
    public PostProcessSettings? BuildPostProcess() => _frame.PostProcess;
    public IReadOnlyList<ScreenTextDraw> BuildOverlayText(Matrix4x4 viewProjection, Vector2 screenSizePixels)
    {
        Volatile.Write(ref _overlay, new(viewProjection, screenSizePixels));
        return _frame.Text;
    }
    public IReadOnlyList<UiShapeDraw> BuildOverlayShapes(Matrix4x4 viewProjection, Vector2 screenSizePixels) => _frame.Shapes;

    public IReadOnlyList<CameraView>? Views(IGameCamera main, Vector2 size)
    {
        if (_faceCam is null || _frame.RedFace is not { } red || _frame.BlueFace is not { } blue) { return null; }
        return
        [
            CameraView.Create(new(0, 0, Math.Max(1, (int)size.X), Math.Max(1, (int)size.Y)), main.Eye, main.ViewProjection),
            CameraView.Create(_faceCam.Panel(size, FaceSide.Left), red.Eye, _ => red.Matrix, isOverlay: true),
            CameraView.Create(_faceCam.Panel(size, FaceSide.Right), blue.Eye, _ => blue.Matrix, isOverlay: true),
        ];
    }
    public void Dispose()
    {
        try { _simulation.Dispose(); }
        finally
        {
            _debugWorld.Dispose();
            Console.WriteLine($"[physics profile] steps {CompletedSteps}, max {MaximumStepMilliseconds:F2} ms");
        }
    }
}
