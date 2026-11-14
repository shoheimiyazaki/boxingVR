using System.Runtime.CompilerServices;
using Aigis.Render;
using Aigis.Render.Voxels;

namespace MiniRing.Vr;

internal sealed record VrScene(PrimitiveInstance[] Instances, DebugLine[] Lines, VoxelVolumeDraw[] Voxels,
    ParticleInstance[] Particles, SceneLighting Lighting, ShadowMapConfig? Shadow, string Status, VrHaptic[] Haptics);

internal readonly record struct VrHaptic(Hand Hand, float Amplitude, float Duration);

internal sealed class VrSceneCapture
{
    private readonly ConditionalWeakTable<VoxelVolume, VolumeCopy> _volumes = new();

    public VrScene Capture(BoxingGame game)
    {
        var (instances, lines) = game.BuildRenderables();
        var voxels = game.BuildVoxelVolumes().Select(draw =>
        {
            var cache = _volumes.GetOrCreateValue(draw.Volume);
            if (cache.Revision != draw.Volume.Revision)
            {
                var source = draw.Volume;
                var copy = new VoxelVolume(source.Width, source.Height, source.Depth);
                source.Rgba.CopyTo(copy.Rgba, 0);
                cache.Volume = copy;
                cache.Revision = source.Revision;
            }
            return draw with { Volume = cache.Volume! };
        }).ToArray();
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

        return new(instances.ToArray(), lines.ToArray(), voxels, game.BuildParticles().ToArray(),
            game.BuildLighting(), game.BuildShadow(), $"{game.StatusLine} {game.Message}", haptics.ToArray());
    }

    private sealed class VolumeCopy
    {
        public int Revision = -1;
        public VoxelVolume? Volume;
    }
}

internal sealed class VrSimulation : IDisposable
{
    private sealed record Request(XrInputFrame Input, float Dt);

    private readonly AutoResetEvent _ready = new(false);
    private readonly Task _worker;
    private Request? _pending;
    private VrScene _scene;
    private volatile bool _stopping;

    public VrSimulation(VrScene initial, Func<XrInputFrame, float, VrScene> advance)
    {
        _scene = initial;
        _worker = Task.Factory.StartNew(() =>
        {
            while (!_stopping)
            {
                _ready.WaitOne();
                if (!_stopping && Interlocked.Exchange(ref _pending, null) is { } request)
                {
                    Volatile.Write(ref _scene, advance(request.Input, request.Dt));
                }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public VrScene Latest
    {
        get
        {
            if (_worker.IsFaulted)
            {
                _worker.GetAwaiter().GetResult();
            }
            return Volatile.Read(ref _scene);
        }
    }

    public void Submit(XrInputFrame input, float dt)
    {
        Request? previous;
        Request next;
        do
        {
            previous = Volatile.Read(ref _pending);
            next = new Request(input with { CalibratePressed = input.CalibratePressed || previous?.Input.CalibratePressed == true }, dt);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _pending, next, previous), previous));
        _ready.Set();
    }

    public void Dispose()
    {
        _stopping = true;
        _ready.Set();
        try { _worker.GetAwaiter().GetResult(); }
        finally { _ready.Dispose(); }
    }
}
