using System.Collections.Concurrent;
using System.Diagnostics;
using Aigis.Player;
using Aigis.Render;
using Aigis.Render.Voxels;

namespace MiniRing.Vr;

internal sealed record VrScene(PrimitiveInstance[] Instances, DebugLine[] Lines, VoxelVolumeDraw[] Voxels,
    ParticleInstance[] Particles, SceneLighting Lighting, ShadowMapConfig? Shadow, string Status, VrHaptic[] Haptics)
{
    public VoxelSnapshot? VoxelSnapshot { get; init; }
}

internal readonly record struct VrHaptic(Hand Hand, float Amplitude, float Duration);

internal sealed class VrSceneCapture
{
    private readonly VoxelSnapshotProducer _volumes = new();
    private VrScene? _published;
    private long _nextCapture;

    public VrScene Capture(BoxingGame game)
    {
        var haptics = new List<VrHaptic>();
        foreach (var hit in game.RecentHits)
        {
            if (hit.Attacker == game.Player)
            {
                haptics.Add(new(hit.Hand, 0.3f + hit.Damage / 25f, 0.06f + hit.Damage / 300f));
            }
            else if (hit.Victim == game.Player)
            {
                haptics.Add(new(Hand.Left, 0.5f, 0.1f));
                haptics.Add(new(Hand.Right, 0.5f, 0.1f));
            }
        }

        var now = Stopwatch.GetTimestamp();
        if (_published is null || now >= _nextCapture)
        {
            var (instances, lines) = game.BuildRenderables();
            var voxels = _volumes.Capture(game.BuildVoxelVolumes());
            _published = new(instances.ToArray(), lines.ToArray(), [], game.BuildParticles().ToArray(),
                game.BuildLighting(), game.BuildShadow(), $"{game.StatusLine} {game.Message}", [])
                { VoxelSnapshot = voxels };
            _nextCapture = now + Stopwatch.Frequency / 90;
        }
        // Scene copies may run at 90 Hz; transient events are still collected at every 240 Hz physics step.
        return haptics.Count == 0 ? _published : _published with { Haptics = haptics.ToArray() };
    }
}

internal sealed class VrSimulation : IDisposable
{
    private readonly FixedSimulation<XrInputFrame, VrScene> _simulation;
    private readonly ConcurrentQueue<XrInputFrame> _calibrations = new();
    private readonly ConcurrentQueue<VrHaptic> _haptics = new();

    public VrSimulation(VrScene initial, Func<XrInputFrame, float, VrScene> advance)
    {
        _simulation = new(initial, default, (_, latest) => latest,
            input => input with { CalibratePressed = false, RecordTogglePressed = false }, (_, input, dt) =>
        {
            // Preserve the pose at the button press, even when many tracking frames arrive during a slow step.
            var frame = _calibrations.TryDequeue(out var calibration) ? calibration : input;
            var scene = advance(frame, dt);
            foreach (var haptic in scene.Haptics) { _haptics.Enqueue(haptic); }
            return scene;
        });
    }

    public VrScene Latest => _simulation.Latest;
    public long CompletedSteps => _simulation.CompletedSteps;
    public double MaximumStepMilliseconds => _simulation.MaximumStepMilliseconds;

    public void Submit(XrInputFrame input, float dt)
    {
        _ = dt; // Display period is deliberately excluded from the deterministic physics clock.
        // ボタンの立ち上がり（キャリブレーション・記録の開始停止）は 1 歩ずつ確実に届ける。
        if (input.CalibratePressed || input.RecordTogglePressed) { _calibrations.Enqueue(input); }
        _simulation.Submit(input with { CalibratePressed = false, RecordTogglePressed = false });
    }

    public VrHaptic[] DrainHaptics()
    {
        var result = new List<VrHaptic>();
        while (_haptics.TryDequeue(out var haptic)) { result.Add(haptic); }
        return result.ToArray();
    }

    public void Dispose() => _simulation.Dispose();
}
