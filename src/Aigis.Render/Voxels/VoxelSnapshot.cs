using System.Numerics;
using System.Runtime.CompilerServices;

namespace Aigis.Render.Voxels;

/// <summary>A complete immutable voxel frame. No source volume or grid is retained.</summary>
public sealed class VoxelSnapshot
{
    internal VoxelSnapshot(VoxelSnapshotDraw[] draws) => Draws = draws;

    internal VoxelSnapshotDraw[] Draws { get; }

    public int Count => Draws.Length;
}

internal sealed record VoxelSnapshotData(object Identity, int Revision, int Width, int Height, int Depth, byte[] Rgba);

internal readonly record struct VoxelSnapshotDraw(VoxelSnapshotData Data, Matrix4x4 CellToWorld);

/// <summary>Simulation-thread owner: copies texels only when the source revision changes.</summary>
public sealed class VoxelSnapshotProducer
{
    private readonly ConditionalWeakTable<VoxelVolume, CachedVolume> _cache = new();

    private sealed class CachedVolume
    {
        public object Identity { get; } = new();

        public VoxelSnapshotData? Data { get; set; }
    }

    public VoxelSnapshot Capture(IReadOnlyList<VoxelVolumeDraw> draws)
    {
        ArgumentNullException.ThrowIfNull(draws);
        var captured = new VoxelSnapshotDraw[draws.Count];
        for (var index = 0; index < draws.Count; index++)
        {
            var draw = draws[index];
            ArgumentNullException.ThrowIfNull(draw.Volume);
            var volume = draw.Volume;
            var cached = _cache.GetValue(volume, static _ => new CachedVolume());
            if (cached.Data is null || cached.Data.Revision != volume.Revision)
            {
                cached.Data = new(cached.Identity, volume.Revision, volume.Width, volume.Height, volume.Depth,
                    (byte[])volume.Rgba.Clone());
            }

            captured[index] = new(cached.Data, draw.CellToWorld);
        }

        return new(captured);
    }
}

/// <summary>Render-thread owner: keeps texture identities while applying the latest complete revision.</summary>
public sealed class VoxelSnapshotConsumer
{
    private readonly Dictionary<object, Replica> _volumes = new(ReferenceEqualityComparer.Instance);
    private VoxelSnapshot? _lastSnapshot;
    private IReadOnlyList<VoxelVolumeDraw> _lastResolved = [];

    private sealed record Replica(VoxelVolume Volume, VoxelSnapshotData Data);

    public IReadOnlyList<VoxelVolumeDraw> Resolve(VoxelSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (ReferenceEquals(snapshot, _lastSnapshot)) { return _lastResolved; }
        var resolved = new VoxelVolumeDraw[snapshot.Count];
        var live = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < snapshot.Count; index++)
        {
            var draw = snapshot.Draws[index];
            var data = draw.Data;
            live.Add(data.Identity);
            if (!_volumes.TryGetValue(data.Identity, out var replica))
            {
                var volume = new VoxelVolume(data.Width, data.Height, data.Depth);
                volume.ApplySnapshot(data.Rgba);
                replica = new(volume, data);
                _volumes.Add(data.Identity, replica);
            }
            else if (!ReferenceEquals(replica.Data, data))
            {
                replica.Volume.ApplySnapshot(data.Rgba);
                _volumes[data.Identity] = replica with { Data = data };
            }

            resolved[index] = new(replica.Volume, draw.CellToWorld);
        }

        foreach (var identity in _volumes.Keys.Where(identity => !live.Contains(identity)).ToArray())
        {
            _volumes.Remove(identity);
        }

        _lastResolved = Array.AsReadOnly(resolved);
        _lastSnapshot = snapshot;
        return _lastResolved;
    }
}
