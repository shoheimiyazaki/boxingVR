using System.Numerics;
using Aigis.Foundation.Voxels;

namespace Aigis.Physics.Voxels;

/// <summary>A voxel carved off a body, in world space, with the velocity it flies off at (for debris effects).</summary>
public readonly record struct VoxelChip(Vector3 Position, Vector3 Velocity, Quaternion Orientation, Vector3 Size, byte Material, bool WasSurface);

/// <summary>One cell of a <see cref="VoxelChunk"/>.</summary>
/// <param name="Offset">Its centre from the chunk's centre, in the chunk's frame.</param>
/// <param name="Material">Its material (palette index).</param>
/// <param name="WasSurface">Whether it was on the body's original skin.</param>
/// <param name="TornFaces">Its faces that were torn (faced the body, or cells that flew off apart), one bit per face:
/// +X, −X, +Y, −Y, +Z, −Z (bits 0..5). The rest were open air before the blow, or face cells of the same chunk.</param>
public readonly record struct VoxelChunkCell(Vector3 Offset, byte Material, bool WasSurface, byte TornFaces);

/// <summary>A face a carve laid bare on what is left of a body: cell (<paramref name="X"/>, <paramref name="Y"/>,
/// <paramref name="Z"/>) of its grid, still filled, and which of its faces now looks into the crater (0..5: +X, −X, +Y,
/// −Y, +Z, −Z).</summary>
public readonly record struct VoxelCutFace(int X, int Y, int Z, byte Face);

/// <summary>A clump of voxels torn off a body whole, in world space, with how it flies off (for debris effects).</summary>
/// <param name="Position">Its centre.</param>
/// <param name="Velocity">How it flies off [m/s].</param>
/// <param name="Orientation">Its frame (the body's at the blow): <see cref="VoxelChunkCell.Offset"/> is in it.</param>
/// <param name="AngularVelocity">The body's spin at the blow [rad/s].</param>
/// <param name="CellSize">One cell's edges.</param>
/// <param name="Cells">Its cells.</param>
public sealed record VoxelChunk(Vector3 Position, Vector3 Velocity, Quaternion Orientation, Vector3 AngularVelocity, Vector3 CellSize, IReadOnlyList<VoxelChunkCell> Cells);

/// <summary>
/// A rigid body whose inside is a <see cref="VoxelGrid"/>: the grid is the body's box, centred at
/// <see cref="GridCenterLocal"/> in the body's frame (zero for a body built around its grid).
/// <list type="bullet">
/// <item><see cref="Impact"/> carves a blow into the grid and returns the carved voxels as flying chips.</item>
/// <item><see cref="Sever"/> breaks the body where its bone has been carved through: the far piece becomes a new
/// dynamic body (its own <see cref="VoxelBody"/>), and this body is reshaped to what is left of it, keeping its id
/// and joints (<see cref="PhysicsWorld.ReshapeDynamicBox"/>).</item>
/// </list>
/// Mass follows the voxels: a body's mass is its starting mass times the fraction of its cells left, so a piece cut off
/// takes its share of the mass with it.
/// </summary>
public sealed class VoxelBody
{
    private readonly PhysicsWorld _world;
    private readonly float _massPerCell;
    private (Vector3 Center, Vector3 Half, float Mass) _applied;

    /// <summary>The body must have been built as a box that matches the grid: <paramref name="grid"/>'s half extents,
    /// centred at <paramref name="gridCenterLocal"/>.</summary>
    public VoxelBody(PhysicsWorld world, int bodyId, VoxelGrid grid, Vector3 gridCenterLocal = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(grid);
        _world = world;
        BodyId = bodyId;
        Grid = grid;
        GridCenterLocal = gridCenterLocal;
        var cells = Math.Max(1, grid.FilledCount);
        var mass = world.GetMass(bodyId);
        _massPerCell = mass / cells;
        _applied = (gridCenterLocal, grid.HalfExtents, mass);
    }

    private VoxelBody(PhysicsWorld world, int bodyId, VoxelGrid grid, float massPerCell)
    {
        _world = world;
        BodyId = bodyId;
        Grid = grid;
        _massPerCell = massPerCell;
        _applied = (Vector3.Zero, grid.HalfExtents, world.GetMass(bodyId));
    }

    public int BodyId { get; }

    public VoxelGrid Grid { get; }

    /// <summary>
    /// The axis (0/1/2) along which this body is a beam held by its load-bearing cells (a limb: the bone carries the bending,
    /// the flesh only its mass), or null for a body every cell of which bears load (a weapon: it snaps where the blow bends
    /// it past its strength, and the piece flies off). A limb that bends past its bone's strength is not split here: the
    /// contact reports where (<see cref="VoxelContactResult.BentB"/>), and the caller breaks it there (the flesh still holds
    /// the two pieces together).
    /// </summary>
    public int? BeamAxis { get; set; }

    /// <summary>Where the grid's centre sits in the body's frame.</summary>
    public Vector3 GridCenterLocal { get; }

    /// <summary>The grid's frame in world space (scale 1): grid-local point → world.</summary>
    public Matrix4x4 GridToWorld() => GridToWorld(_world.GetState(BodyId));

    /// <summary>The grid's frame in world space for the body in state <paramref name="s"/> (a pose before a step, say).</summary>
    public Matrix4x4 GridToWorld(BodyState s) =>
        Matrix4x4.CreateTranslation(GridCenterLocal) * Matrix4x4.CreateFromQuaternion(s.Orientation) * Matrix4x4.CreateTranslation(s.Position);

    /// <summary>The physics world the body lives in.</summary>
    public PhysicsWorld World => _world;

    public Vector3 WorldToGrid(Vector3 world)
    {
        var s = _world.GetState(BodyId);
        return Vector3.Transform(world - s.Position, Quaternion.Conjugate(s.Orientation)) - GridCenterLocal;
    }

    /// <summary>
    /// Carves a blow landing at <paramref name="worldPoint"/> travelling along <paramref name="worldDirection"/> with
    /// <paramref name="energy"/> damage (<see cref="VoxelGrid.Carve"/>). Carved voxels are appended to
    /// <paramref name="chips"/> as a splash out of the crater: out of the struck face (never through the body), fanned
    /// around the fist (wider with <paramref name="spread"/>: 0 = within about 35° of the face's normal, 1 = out to about
    /// 65°) and swept along the part of the blow that runs along the surface, so a glancing blow throws them on its way.
    /// The body's own recoil is the caller's to add (it usually comes after the carve). Speeds are about <paramref name="chipSpeed"/> (±25%) on top of the body's own
    /// velocity. <paramref name="jitter"/> (−1..1 per call) spreads them; pass a seeded source to stay deterministic.
    /// <para>With <paramref name="chunks"/>, a blow that carves at least <see cref="MinCellsForChunks"/> cells tears some
    /// of them off whole: up to <paramref name="maxChunks"/> connected clumps holding about <paramref name="chunkShare"/>
    /// of the cells between them, each a <see cref="VoxelChunk"/> (flying a little slower than the chips). The rest
    /// leave as chips.</para>
    /// <para>With <paramref name="cutFaces"/>, every face the carve laid bare on what is left of the body (a <see cref="VoxelCutFace"/>).</para>
    /// </summary>
    public int Impact(Vector3 worldPoint, Vector3 worldDirection, float energy, float chipSpeed, Func<float> jitter, ICollection<VoxelChip> chips, float spread = 0.4f,
        ICollection<VoxelChunk>? chunks = null, float chunkShare = 0.6f, int maxChunks = 3, ICollection<VoxelCutFace>? cutFaces = null)
    {
        ArgumentNullException.ThrowIfNull(jitter);
        ArgumentNullException.ThrowIfNull(chips);
        var s = _world.GetState(BodyId);
        var inverse = Quaternion.Conjugate(s.Orientation);
        var localPoint = WorldToGrid(worldPoint);
        var localDirection = Vector3.Transform(worldDirection, inverse);
        // The face that was struck, read before the crater changes it.
        var localNormal = SurfaceNormal(Grid, localPoint, localDirection);
        var removed = new List<RemovedVoxel>();
        Grid.Carve(localPoint, localDirection, energy, removed);
        if (removed.Count == 0)
        {
            return 0;
        }

        var dir = worldDirection.LengthSquared() > 1e-12f ? Vector3.Normalize(worldDirection) : Vector3.UnitY;
        var normal = Vector3.Transform(localNormal, s.Orientation);
        // The part of the blow that runs along the surface (a glancing hook): the splash is swept that way.
        var glance = dir - (normal * Vector3.Dot(dir, normal));
        var cone = 0.5f + spread;
        var size = Grid.CellSize;

        // A splash out of the crater, never through the body: out of the struck face, fanned around the fist (which
        // blocks the way straight back), swept along the glancing part of the blow. A blow head-on to the face
        // sprays out around the fist and back toward the puncher, not out of the back of the head.
        Vector3 Splash(Vector3 position, float speedScale)
        {
            var pointVelocity = s.LinearVelocity + Vector3.Cross(s.AngularVelocity, position - s.Position);
            var scatter = new Vector3(jitter(), jitter(), jitter());
            var around = scatter - (normal * Vector3.Dot(scatter, normal));
            var radial = around.LengthSquared() > 1e-8f ? Vector3.Normalize(around) : Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX + new Vector3(0f, 0.3f, 0.1f)));
            var fan = cone * (0.35f + (0.65f * MathF.Abs(jitter())));
            var heading = (normal * 0.75f) + (radial * fan) + (glance * 0.9f) + (scatter * 0.12f);
            var speed = chipSpeed * speedScale * (1f + (0.25f * jitter()));
            return pointVelocity + (Vector3.Normalize(heading + new Vector3(1e-4f)) * speed);
        }

        if (cutFaces is not null)
        {
            CollectCutFaces(removed, cutFaces);
        }

        var group = chunks is null ? null : GroupIntoChunks(removed, chunkShare, maxChunks, jitter);
        if (group is not null)
        {
            EmitChunks(removed, group, s, Splash, chunks!);
        }

        for (var i = 0; i < removed.Count; i++)
        {
            if (group is not null && group[i] >= 0)
            {
                continue;
            }

            var v = removed[i];
            var position = s.Position + Vector3.Transform(v.LocalCenter + GridCenterLocal, s.Orientation);
            chips.Add(new VoxelChip(position, Splash(position, 1f), s.Orientation, size, v.Material, v.WasSurface));
        }

        UpdateMass();
        return removed.Count;
    }

    /// <summary>Fewest carved cells before any of them leave as a chunk (a light blow only crumbles).</summary>
    public const int MinCellsForChunks = 8;

    /// <summary>Smallest chunk [cells]; a group that grows smaller than this crumbles into chips.</summary>
    private const int MinChunkCells = 3;

    /// <summary>
    /// Sorts the carved cells into up to <paramref name="maxChunks"/> chunks (connected clumps, about
    /// <paramref name="share"/> of the cells between them) and loose chips: the group index of each cell, −1 for a chip.
    /// Null when nothing forms a chunk. More cells carved, more chunks (one, two from 60, three from 140).
    /// </summary>
    private int[]? GroupIntoChunks(List<RemovedVoxel> removed, float share, int maxChunks, Func<float> jitter)
    {
        if (removed.Count < MinCellsForChunks || maxChunks <= 0 || !(share > 0f))
        {
            return null;
        }

        var count = Math.Min(maxChunks, 1 + (removed.Count >= 60 ? 1 : 0) + (removed.Count >= 140 ? 1 : 0));
        var target = Math.Max(MinChunkCells, (int)(Math.Clamp(share, 0f, 1f) * removed.Count / count));
        var index = new Dictionary<(int X, int Y, int Z), int>(removed.Count);
        for (var i = 0; i < removed.Count; i++)
        {
            index[CellOf(removed[i].LocalCenter)] = i;
        }

        var group = new int[removed.Count];
        Array.Fill(group, -1);
        var any = false;
        for (var g = 0; g < count; g++)
        {
            // Seed somewhere on the torn-off skin when there is one free (a chunk is a flap of the surface and what is under it).
            var start = (int)((0.5f + (0.5f * jitter())) * removed.Count) % removed.Count;
            var seed = -1;
            for (var k = 0; k < removed.Count && seed < 0; k++)
            {
                var i = (start + k) % removed.Count;
                if (group[i] == -1 && removed[i].WasSurface)
                {
                    seed = i;
                }
            }

            for (var k = 0; k < removed.Count && seed < 0; k++)
            {
                var i = (start + k) % removed.Count;
                if (group[i] == -1)
                {
                    seed = i;
                }
            }

            if (seed < 0)
            {
                break;
            }

            // Grow a connected clump outward from the seed (breadth first over the six face neighbours).
            var members = new List<int> { seed };
            group[seed] = g;
            for (var head = 0; head < members.Count && members.Count < target; head++)
            {
                var (x, y, z) = CellOf(removed[members[head]].LocalCenter);
                foreach (var (dx, dy, dz) in FaceSteps)
                {
                    if (members.Count >= target)
                    {
                        break;
                    }

                    if (index.TryGetValue((x + dx, y + dy, z + dz), out var n) && group[n] == -1)
                    {
                        group[n] = g;
                        members.Add(n);
                    }
                }
            }

            if (members.Count < MinChunkCells)
            {
                foreach (var m in members)
                {
                    group[m] = -1;
                }

                // Mark the lone seed so it is not picked again (restored to a chip below).
                group[seed] = -2;
                continue;
            }

            any = true;
        }

        for (var i = 0; i < group.Length; i++)
        {
            if (group[i] == -2)
            {
                group[i] = -1;
            }
        }

        return any ? group : null;
    }

    /// <summary>
    /// Builds one <see cref="VoxelChunk"/> per group: its cells around their centre (in the body's orientation), each
    /// cell's torn faces (those that faced cells still on the body, or cells that went elsewhere), and a splash velocity
    /// a little slower than the chips' (it is heavier).
    /// </summary>
    private void EmitChunks(List<RemovedVoxel> removed, int[] group, BodyState s, Func<Vector3, float, Vector3> splash, ICollection<VoxelChunk> into)
    {
        var groups = group.Where(g => g >= 0).Distinct().Order().ToList();
        var index = new Dictionary<(int X, int Y, int Z), int>(removed.Count);
        for (var i = 0; i < removed.Count; i++)
        {
            index[CellOf(removed[i].LocalCenter)] = i;
        }

        var (nx, ny, nz) = Grid.Dimensions;
        foreach (var g in groups)
        {
            var members = Enumerable.Range(0, removed.Count).Where(i => group[i] == g).ToList();
            var center = members.Aggregate(Vector3.Zero, (sum, i) => sum + removed[i].LocalCenter) / members.Count;
            var cells = new VoxelChunkCell[members.Count];
            for (var k = 0; k < members.Count; k++)
            {
                var v = removed[members[k]];
                var (x, y, z) = CellOf(v.LocalCenter);
                byte torn = 0;
                for (var f = 0; f < FaceSteps.Length; f++)
                {
                    var (dx, dy, dz) = FaceSteps[f];
                    var (ax, ay, az) = (x + dx, y + dy, z + dz);
                    var onBody = ax >= 0 && ay >= 0 && az >= 0 && ax < nx && ay < ny && az < nz && Grid.At(ax, ay, az) != 0;
                    var elsewhere = index.TryGetValue((ax, ay, az), out var other) && group[other] != g;
                    if (onBody || elsewhere)
                    {
                        torn |= (byte)(1 << f);
                    }
                }

                cells[k] = new VoxelChunkCell(v.LocalCenter - center, v.Material, v.WasSurface, torn);
            }

            var position = s.Position + Vector3.Transform(center + GridCenterLocal, s.Orientation);
            into.Add(new VoxelChunk(position, splash(position, ChunkSpeedScale), s.Orientation, s.AngularVelocity, Grid.CellSize, cells));
        }
    }

    /// <summary>The faces the carve laid bare on the body: every face of a cell still filled that faced a carved cell.</summary>
    private void CollectCutFaces(List<RemovedVoxel> removed, ICollection<VoxelCutFace> into)
    {
        var (nx, ny, nz) = Grid.Dimensions;
        foreach (var v in removed)
        {
            var (x, y, z) = CellOf(v.LocalCenter);
            for (var f = 0; f < FaceSteps.Length; f++)
            {
                var (dx, dy, dz) = FaceSteps[f];
                var (ax, ay, az) = (x + dx, y + dy, z + dz);
                if (ax >= 0 && ay >= 0 && az >= 0 && ax < nx && ay < ny && az < nz && Grid.At(ax, ay, az) != 0)
                {
                    // The neighbour's face that looks back at the carved cell (the opposite face of step f).
                    into.Add(new VoxelCutFace(ax, ay, az, (byte)(f ^ 1)));
                }
            }
        }
    }

    /// <summary>A chunk flies at this fraction of the chips' speed.</summary>
    private const float ChunkSpeedScale = 0.75f;

    /// <summary>The six face neighbours, in the order of <see cref="VoxelChunkCell.TornFaces"/>'s bits: +X, −X, +Y, −Y, +Z, −Z.</summary>
    private static readonly (int X, int Y, int Z)[] FaceSteps = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    private (int X, int Y, int Z) CellOf(Vector3 local)
    {
        var c = (local + Grid.HalfExtents) / Grid.CellSize;
        return ((int)MathF.Floor(c.X), (int)MathF.Floor(c.Y), (int)MathF.Floor(c.Z));
    }

    /// <summary>
    /// The outward normal (grid-local, unit) of the surface a blow from <paramref name="localPoint"/> along
    /// <paramref name="localDirection"/> meets: walks along the blow to the first filled cell, then takes the direction
    /// away from the filled cells around it. Facing the blow (never along it); −direction when nothing is in the way.
    /// </summary>
    private static Vector3 SurfaceNormal(VoxelGrid grid, Vector3 localPoint, Vector3 localDirection)
    {
        var dir = localDirection.LengthSquared() > 1e-12f ? Vector3.Normalize(localDirection) : -Vector3.UnitY;
        var cell = grid.CellSize;
        var stepLength = 0.5f * MathF.Min(cell.X, MathF.Min(cell.Y, cell.Z));
        var (nx, ny, nz) = grid.Dimensions;
        var reach = 2f * grid.HalfExtents.Length();
        (int X, int Y, int Z) CellOf(Vector3 p)
        {
            var c = (p + grid.HalfExtents) / cell;
            return ((int)MathF.Floor(c.X), (int)MathF.Floor(c.Y), (int)MathF.Floor(c.Z));
        }

        bool Filled(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < nx && y < ny && z < nz && grid.At(x, y, z) != 0;

        // Start where the blow line enters the grid's box (the contact point may be the glove's centre, outside it).
        var start = Vector3.Clamp(localPoint, -grid.HalfExtents, grid.HalfExtents);
        (int X, int Y, int Z)? hit = null;
        for (var t = -2f * stepLength; t <= reach; t += stepLength)
        {
            var c = CellOf(start + (dir * t));
            if (Filled(c.X, c.Y, c.Z))
            {
                hit = c;
                break;
            }
        }

        if (hit is not { } h)
        {
            return -dir;
        }

        // Away from the filled cells within a couple of cells of the entry.
        const int R = 2;
        var inward = Vector3.Zero;
        var center = grid.CellCenter(h.X, h.Y, h.Z);
        for (var z = h.Z - R; z <= h.Z + R; z++)
        {
            for (var y = h.Y - R; y <= h.Y + R; y++)
            {
                for (var x = h.X - R; x <= h.X + R; x++)
                {
                    if (Filled(x, y, z))
                    {
                        inward += (grid.CellCenter(x, y, z) - center) / cell;
                    }
                }
            }
        }

        if (inward.LengthSquared() < 1e-6f)
        {
            return -dir;
        }

        var normal = -Vector3.Normalize(inward);
        // A face the blow does not run into (an edge, or a reading off a corner): lean it back toward the blow.
        var facing = Vector3.Dot(normal, -dir);
        return facing >= 0.2f ? normal : Vector3.Normalize(normal + (-dir * (0.2f - facing + 0.3f)));
    }

    /// <summary>
    /// If the bone along <paramref name="axis"/> (0/1/2 = the body's x/y/z) has been carved through, breaks the body
    /// there: everything on the <paramref name="distalHigh"/> side of the break becomes a new dynamic body, returned with
    /// its own <see cref="VoxelBody"/>. This body is reshaped to its remaining cells. Null if the bone is whole.
    /// <para>The new body gets this body's collision filter (it keeps not colliding with this body's neighbours), its
    /// velocity at the cut, and its share of the mass. Joints are the caller's business: a joint that held the far end
    /// of this body must be moved onto the new one (it now carries that end).</para>
    /// </summary>
    public VoxelBody? Sever(int axis, bool distalHigh)
    {
        var slice = Grid.FindBoneBreak(axis, fromHigh: !distalHigh);
        return slice is { } cut ? SplitAt(axis, cut, distalHigh) : null;
    }

    /// <summary>
    /// Breaks the body at slice <paramref name="slice"/> of <paramref name="axis"/> (the slice's cells stay with this
    /// body): everything on the <paramref name="takeHigh"/> side becomes a new dynamic body, as <see cref="Sever"/> does.
    /// Null if nothing lies on that side.
    /// </summary>
    public VoxelBody? SplitAt(int axis, int slice, bool takeHigh)
    {
        var split = Grid.Split(axis, slice, takeHigh: takeHigh);
        if (split is not { } piece)
        {
            return null;
        }

        var s = _world.GetState(BodyId);
        var pieceBounds = piece.Piece.OccupiedBounds();
        if (pieceBounds is null)
        {
            return null;
        }

        // The new body is built around the piece's own grid (origin = grid centre).
        var pieceCenterLocal = GridCenterLocal + piece.CenterOffset;
        var pieceWorld = s.Position + Vector3.Transform(pieceCenterLocal, s.Orientation);
        var pieceMass = MathF.Max(1e-4f, _massPerCell * piece.Piece.FilledCount);
        var id = _world.AddDynamicBox(pieceWorld, s.Orientation, piece.Piece.HalfExtents, pieceMass);
        _world.CopyCollisionFilter(BodyId, id);
        _world.SetLinearVelocity(id, s.LinearVelocity + Vector3.Cross(s.AngularVelocity, pieceWorld - s.Position));
        _world.SetAngularVelocity(id, s.AngularVelocity);
        var fragment = new VoxelBody(_world, id, piece.Piece, _massPerCell);
        fragment.UpdateMass();
        UpdateMass();
        return fragment;
    }

    /// <summary>
    /// Fits the collider to the cells that are left (their bounding box) and the mass to their count. Called after
    /// every change; a body with no cells keeps a tiny collider so it stays a valid body.
    /// </summary>
    public void UpdateMass()
    {
        var bounds = Grid.OccupiedBounds();
        var cells = Grid.FilledCount;
        var mass = MathF.Max(1e-4f, _massPerCell * Math.Max(1, cells));
        var (center, half) = bounds ?? (Vector3.Zero, Grid.CellSize * 0.5f);
        var wantCenter = GridCenterLocal + center;
        // Only rebuild the collider when it actually changes (the rebuild is not free): a crater that does not move the
        // bounding box changes only the mass, and a small change in mass is not worth a rebuild either.
        var sameShape = Vector3.DistanceSquared(_applied.Center, wantCenter) < 1e-10f && Vector3.DistanceSquared(_applied.Half, half) < 1e-10f;
        if (sameShape && MathF.Abs(_applied.Mass - mass) <= 0.02f * _applied.Mass)
        {
            return;
        }

        _world.ReshapeDynamicBox(BodyId, wantCenter, half, mass);
        _applied = (wantCenter, half, mass);
    }
}
