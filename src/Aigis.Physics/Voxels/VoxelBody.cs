using System.Numerics;
using Aigis.Foundation.Voxels;

namespace Aigis.Physics.Voxels;

/// <summary>A voxel carved off a body, in world space, with the velocity it flies off at (for debris effects).</summary>
public readonly record struct VoxelChip(Vector3 Position, Vector3 Velocity, Quaternion Orientation, Vector3 Size, byte Material, bool WasSurface);

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

    /// <summary>Where the grid's centre sits in the body's frame.</summary>
    public Vector3 GridCenterLocal { get; }

    /// <summary>The grid's frame in world space (scale 1): grid-local point → world.</summary>
    public Matrix4x4 GridToWorld()
    {
        var s = _world.GetState(BodyId);
        return Matrix4x4.CreateTranslation(GridCenterLocal) * Matrix4x4.CreateFromQuaternion(s.Orientation) * Matrix4x4.CreateTranslation(s.Position);
    }

    public Vector3 WorldToGrid(Vector3 world)
    {
        var s = _world.GetState(BodyId);
        return Vector3.Transform(world - s.Position, Quaternion.Conjugate(s.Orientation)) - GridCenterLocal;
    }

    /// <summary>
    /// Carves a blow landing at <paramref name="worldPoint"/> travelling along <paramref name="worldDirection"/> with
    /// <paramref name="energy"/> damage (<see cref="VoxelGrid.Carve"/>). Carved voxels are appended to
    /// <paramref name="chips"/>: each flies off along the blow and back out of the crater at about
    /// <paramref name="chipSpeed"/>, on top of the body's own velocity. <paramref name="jitter"/> (−1..1 per call)
    /// spreads them; pass a seeded source to stay deterministic.
    /// </summary>
    public int Impact(Vector3 worldPoint, Vector3 worldDirection, float energy, float chipSpeed, Func<float> jitter, ICollection<VoxelChip> chips)
    {
        ArgumentNullException.ThrowIfNull(jitter);
        ArgumentNullException.ThrowIfNull(chips);
        var s = _world.GetState(BodyId);
        var inverse = Quaternion.Conjugate(s.Orientation);
        var localPoint = WorldToGrid(worldPoint);
        var localDirection = Vector3.Transform(worldDirection, inverse);
        var removed = new List<RemovedVoxel>();
        Grid.Carve(localPoint, localDirection, energy, removed);
        if (removed.Count == 0)
        {
            return 0;
        }

        var dir = worldDirection.LengthSquared() > 1e-12f ? Vector3.Normalize(worldDirection) : Vector3.UnitY;
        var size = Grid.CellSize;
        foreach (var v in removed)
        {
            var local = v.LocalCenter + GridCenterLocal;
            var position = s.Position + Vector3.Transform(local, s.Orientation);
            var pointVelocity = s.LinearVelocity + Vector3.Cross(s.AngularVelocity, position - s.Position);
            // Out of the crater (against the blow) and onward with it, plus some scatter and a little lift.
            var outward = Vector3.Normalize(position - worldPoint + (dir * 1e-3f));
            var scatter = new Vector3(jitter(), jitter(), jitter());
            var velocity = pointVelocity + (chipSpeed * ((0.6f * dir) + (0.5f * -dir * (0.5f + (0.5f * jitter()))) + (0.6f * outward) + (0.5f * scatter) + new Vector3(0f, 0.6f, 0f)));
            chips.Add(new VoxelChip(position, velocity, s.Orientation, size, v.Material, v.WasSurface));
        }

        UpdateMass();
        return removed.Count;
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
        if (slice is not { } cut)
        {
            return null;
        }

        var split = Grid.Split(axis, cut, takeHigh: distalHigh);
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
