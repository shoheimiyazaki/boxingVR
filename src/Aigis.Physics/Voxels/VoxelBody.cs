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
    private readonly bool _materialMass;
    private float _densityScale = 1f;
    private Vector3 _appliedInertia;
    private (Vector3 Center, Vector3 Half, float Mass) _applied;
    private readonly List<VoxelBody> _companions = [];
    private VoxelBody? _owner;

    /// <summary>The body must have been built as a box that matches the grid: <paramref name="grid"/>'s half extents,
    /// centred at <paramref name="gridCenterLocal"/>. <paramref name="massPerCell"/> (null: the body's mass shared by the grid's cells)
    /// is the mass of one cell, for a body whose mass is shared with grids laid over it later (<see cref="AddCompanion"/>).
    /// <paramref name="materialMass"/>: the mass (and the inertia) is counted from the cells' materials (density × cell volume). With
    /// <paramref name="fitNow"/> false such a body keeps its collider and mass until <see cref="FitMassToCells"/> or the next change
    /// (a body whose companions are added first).</summary>
    /// <remarks><paramref name="densityScale"/>: multiplies every material's density in material mode (a caller's whole-body mass scale).</remarks>
    public VoxelBody(PhysicsWorld world, int bodyId, VoxelGrid grid, Vector3 gridCenterLocal = default, bool materialMass = false, float? massPerCell = null, bool fitNow = true,
        float densityScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(grid);
        _world = world;
        BodyId = bodyId;
        Grid = grid;
        GridCenterLocal = gridCenterLocal;
        var cells = Math.Max(1, grid.FilledCount);
        var mass = world.GetMass(bodyId);
        _massPerCell = massPerCell ?? (mass / cells);
        _applied = (gridCenterLocal, grid.HalfExtents, mass);
        _materialMass = materialMass;
        _densityScale = densityScale > 0f && float.IsFinite(densityScale) ? densityScale : throw new ArgumentOutOfRangeException(nameof(densityScale));
        if (materialMass && fitNow)
        {
            UpdateMass();
        }
    }

    /// <summary>
    /// Sets the rigid body's mass and principal moments of inertia from the cells of this grid and its companions (density × cell
    /// volume, moments about the body's origin), keeping the collider as it is. Only for a body that counts its mass from materials.
    /// </summary>
    public void FitMassToCells()
    {
        if (!_materialMass || _owner is not null || Massless)
        {
            throw new InvalidOperationException("Only the owner of a body that counts its mass from its materials can fit its mass to its cells.");
        }

        var (mass, inertia) = MassAndInertia();
        _world.SetDynamicMass(BodyId, mass, inertia);
        _applied = (_applied.Center, _applied.Half, mass);
        _appliedInertia = inertia;
    }

    /// <summary>The mass of this grid and its companions and their principal moments about the body's origin (material mode).</summary>
    private (float Mass, Vector3 Inertia) MassAndInertia()
    {
        var mass = 0f;
        var inertia = Vector3.Zero;
        for (var k = -1; k < _companions.Count; k++)
        {
            var body = k < 0 ? this : _companions[k];
            var grid = body.Grid;
            var cell = grid.CellSize;
            var volume = cell.X * cell.Y * cell.Z;
            // 1 つのセル（直方体）の自分の重心まわりの慣性: m (b² + c²) / 12。
            var own = new Vector3((cell.Y * cell.Y) + (cell.Z * cell.Z), (cell.X * cell.X) + (cell.Z * cell.Z), (cell.X * cell.X) + (cell.Y * cell.Y)) / 12f;
            for (var i = 0; i < grid.CellCount; i++)
            {
                var material = grid.MaterialAt(i);
                if (material == 0)
                {
                    continue;
                }

                var m = grid.Palette[material].MechanicsOrDefault.Density * _densityScale * volume;
                var (x, y, z) = grid.Coordinates(i);
                var p = body.GridCenterLocal + ((new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * cell) - grid.HalfExtents);
                var sq = p * p;
                inertia += m * (own + new Vector3(sq.Y + sq.Z, sq.X + sq.Z, sq.X + sq.Y));
                mass += m;
            }
        }

        mass = MathF.Max(1e-4f, mass);
        // 空になった格子でも正の慣性（小さな箱 1 つぶん）を保つ。
        var floor = mass * 1e-6f;
        return (mass, Vector3.Max(inertia, new Vector3(floor)));
    }

    private VoxelBody(PhysicsWorld world, int bodyId, VoxelGrid grid, Vector3 gridCenterLocal, float massPerCell, bool materialMass)
        : this(world, bodyId, grid, massPerCell, materialMass)
    {
        GridCenterLocal = gridCenterLocal;
    }

    private VoxelBody(PhysicsWorld world, int bodyId, VoxelGrid grid, float massPerCell, bool materialMass)
    {
        _world = world;
        BodyId = bodyId;
        Grid = grid;
        _massPerCell = massPerCell;
        _materialMass = materialMass;
        _applied = (Vector3.Zero, grid.HalfExtents, world.GetMass(bodyId));
    }

    public int BodyId { get; }

    public VoxelGrid Grid { get; private set; }

    /// <summary>The last fitted collider box in the rigid body's local coordinates.</summary>
    public (Vector3 Center, Vector3 HalfExtents) ColliderBox
    {
        get
        {
            var applied = _owner is { } owner ? owner._applied : _applied;
            return (applied.Center, applied.Half);
        }
    }

    /// <summary>
    /// Other grids carried by the same rigid body (<see cref="AddCompanion"/>), in the order they were added (fixed, so a loop over
    /// them is deterministic). Empty for an ordinary body and for a companion itself.
    /// </summary>
    public IReadOnlyList<VoxelBody> Companions => _companions;

    /// <summary>The grid that owns the rigid body when this is a companion (null for the owner and for an ordinary body).</summary>
    public VoxelBody? Owner => _owner;

    /// <summary>
    /// Lays another grid (finer cells, say: a hand inside a glove) over this body's rigid body, centred at <paramref name="gridCenterLocal"/>
    /// in the body's frame. Returns its <see cref="VoxelBody"/>, which shares <see cref="BodyId"/>; the collider box and the mass
    /// now cover every grid (<see cref="UpdateMass"/>: the sum of the masses, the union of the bounds), so a blow on one grid
    /// never resets what the other contributes. Only this (owner) grid is ever severed or split: a companion's
    /// <see cref="Sever"/>, <see cref="SplitAt"/> and <see cref="SeverDisconnected"/> do nothing, and a piece cut off the owner takes no companion with it.
    /// <paramref name="massPerCell"/> (null: this body's) is the mass of one cell unless the body derives mass from materials.
    /// </summary>
    /// <remarks><paramref name="refit"/> false leaves the collider and mass as they are until the grids next change (a body whose mass was
    /// already shared out to cover the companion, and whose box was built to hold both grids).</remarks>
    public VoxelBody AddCompanion(VoxelGrid grid, Vector3 gridCenterLocal, float? massPerCell = null, bool refit = true)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (_owner is not null)
        {
            throw new InvalidOperationException("A companion grid cannot carry companions of its own.");
        }

        var companion = new VoxelBody(_world, BodyId, grid, gridCenterLocal, massPerCell ?? _massPerCell, _materialMass) { _owner = this, _densityScale = _densityScale };
        _companions.Add(companion);
        if (refit)
        {
            UpdateMass();
        }

        return companion;
    }

    /// <summary>Changes the contact shape without moving the rigid body or its joints.</summary>
    public void ReplaceGrid(VoxelGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        Grid = grid;
    }

    /// <summary>
    /// The axis (0/1/2) along which this body is a beam held by its load-bearing cells (a limb: the bone carries the bending,
    /// the flesh only its mass), or null for a body every cell of which bears load (a weapon: it snaps where the blow bends
    /// it past its strength, and the piece flies off). A limb that bends past its bone's strength is not split here: the
    /// contact reports where (<see cref="VoxelContactResult.BentB"/>), and the caller breaks it there (the flesh still holds
    /// the two pieces together).
    /// </summary>
    public int? BeamAxis { get; set; }

    /// <summary>Multiplier on the beam material's tensile limit in bending.</summary>
    public float BeamTensileScale { get; init; } = 1f;

    /// <summary>
    /// True for a contact shape that only borrows another body's rigid body (a bridge between two limb parts: it rides the parent's pose and
    /// takes the contact's impulse on it) and owns none of its mass or collider: <see cref="UpdateMass"/> then changes nothing.
    /// </summary>
    public bool Massless { get; init; }

    /// <summary>
    /// Whether this body is the struck side of a contact whatever the grids' fineness (<see cref="VoxelContact.Resolve"/> sweeps the finer
    /// grid through the coarser one, and only the coarser one, the field, is crushed beneath a face that held). A layer of fine soft tissue
    /// over a coarser body (a face over the skull) stays the field against a coarser glove, club or shin: it is what the blow presses into.
    /// When both or neither prefer it, the finer grid is the probe as before.
    /// </summary>
    public bool PreferField { get; set; }

    /// <summary>
    /// The slices (inclusive) along <see cref="BeamAxis"/> where this beam may break (null: anywhere along it). A head sits on a thin
    /// neck: the whole head is a beam that carries the blow (the skull's inertia and bone), but it can only snap in the neck.
    /// </summary>
    public (int Low, int High)? BeamSlices { get; set; }

    /// <summary>
    /// How hard the beam's contractile cells pull (0..1): their <see cref="VoxelMechanics.ActiveTie"/> is scaled by it when a blow bends
    /// the beam (a braced neck holds more than a slack one). The owner sets it every step (a body's muscle tone); 0 by default (only the
    /// passive ties).
    /// </summary>
    public float TieActivation { get; set; }

    /// <summary>Where the grid's centre sits in the body's frame.</summary>
    public Vector3 GridCenterLocal { get; private set; }

    /// <summary>
    /// Moves a companion grid (<see cref="AddCompanion"/>) to <paramref name="gridCenterLocal"/> in the body's frame (a hand sliding its grip
    /// along a staff) and refits the owner's collider and mass. The owner grid itself cannot move: its frame is the body's.
    /// </summary>
    public void MoveCompanion(Vector3 gridCenterLocal)
    {
        if (_owner is null)
        {
            throw new InvalidOperationException("Only a companion grid can move within its body.");
        }

        if (GridCenterLocal == gridCenterLocal)
        {
            return;
        }

        GridCenterLocal = gridCenterLocal;
        _owner.UpdateMass();
    }

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
    /// <para>With <paramref name="chunks"/>, the carved cells leave in clumps (<see cref="VoxelClumps"/>: face-joined cells of one
    /// tissue stay together, and break apart only where they do not touch or the tissue changes), each a <see cref="VoxelChunk"/>
    /// (flying a little slower than the chips). A cell that joins no other leaves as a chip.</para>
    /// <para>With <paramref name="cutFaces"/>, every face the carve laid bare on what is left of the body (a <see cref="VoxelCutFace"/>).</para>
    /// </summary>
    public int Impact(Vector3 worldPoint, Vector3 worldDirection, float energy, float chipSpeed, Func<float> jitter, ICollection<VoxelChip> chips, float spread = 0.4f,
        ICollection<VoxelChunk>? chunks = null, ICollection<VoxelCutFace>? cutFaces = null)
        => ImpactSpending(worldPoint, worldDirection, energy, chipSpeed, jitter, chips, spread, chunks, cutFaces).Carved;

    /// <summary>
    /// <see cref="Impact"/>, also returning the damage the carve actually spent (<see cref="VoxelGrid.Carve"/>: less than
    /// <paramref name="energy"/> only when the reachable cells ran out). A body of several layers (a face over a skull) passes the rest on
    /// to the layer beneath.
    /// </summary>
    public (int Carved, float Spent) ImpactSpending(Vector3 worldPoint, Vector3 worldDirection, float energy, float chipSpeed, Func<float> jitter, ICollection<VoxelChip> chips, float spread = 0.4f,
        ICollection<VoxelChunk>? chunks = null, ICollection<VoxelCutFace>? cutFaces = null)
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
        var spent = Grid.Carve(localPoint, localDirection, energy, removed);
        if (removed.Count == 0)
        {
            return (0, spent);
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

        // Clumps of face-joined cells of one tissue leave whole (VoxelClumps); a cell alone leaves as a chip.
        int[]? group = null;
        if (chunks is not null)
        {
            group = new int[removed.Count];
            Array.Fill(group, -1);
            var clumps = VoxelClumps.Of(Grid, removed);
            for (var g = 0; g < clumps.Count; g++)
            {
                if (clumps[g].Count > 1)
                {
                    foreach (var i in clumps[g])
                    {
                        group[i] = g;
                    }
                }
            }

            EmitChunks(removed, group, s, Splash, chunks);
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
        return (removed.Count, spent);
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
        if (_owner is not null)
        {
            return null;
        }

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
        if (_owner is not null)
        {
            return null;
        }

        var split = Grid.Split(axis, slice, takeHigh: takeHigh);
        return split is { } piece ? Detach(piece.Piece, piece.CenterOffset) : null;
    }

    /// <summary>
    /// Breaks off every part of the body that the cells no longer connect to the rest (a weapon carved through in the middle: the far end
    /// is no longer part of it). The cells are grouped into 6-neighbour components; the one nearest <paramref name="keepNear"/> (grid-local;
    /// null: the largest) stays in this body, and each other becomes its own new dynamic body, as <see cref="SplitAt"/> makes one.
    /// Empty if the body is still one piece.
    /// </summary>
    public IReadOnlyList<VoxelBody> SeverDisconnected(Vector3? keepNear = null)
    {
        if (_owner is not null)
        {
            return [];
        }

        if (Grid.IsConnected())
        {
            return [];
        }

        var components = Grid.ConnectedComponents();
        if (components.Count < 2)
        {
            return [];
        }

        var keep = 0;
        var best = keepNear is null ? -1f : float.MaxValue;
        for (var c = 0; c < components.Count; c++)
        {
            if (keepNear is { } point)
            {
                foreach (var i in components[c])
                {
                    var (x, y, z) = Grid.Coordinates(i);
                    var d = Vector3.DistanceSquared(Grid.CellCenter(x, y, z), point);
                    if (d < best)
                    {
                        (best, keep) = (d, c);
                    }
                }
            }
            else if (components[c].Length > best)
            {
                (best, keep) = (components[c].Length, c);
            }
        }

        var pieces = new List<VoxelBody>(components.Count - 1);
        for (var c = 0; c < components.Count; c++)
        {
            if (c != keep && Grid.Extract(components[c]) is { } piece && Detach(piece.Piece, piece.CenterOffset) is { } body)
            {
                pieces.Add(body);
            }
        }

        return pieces;
    }

    /// <summary>
    /// Moves the cells <paramref name="cells"/> (indices of filled cells of this grid, e.g. one of <see cref="VoxelGrid.ConnectedComponents"/>)
    /// into a new dynamic body, as <see cref="SeverDisconnected"/> does for each piece it breaks off: the same collision filter, the velocity of
    /// this body at the piece, and the mass of its cells. This body is reshaped to what is left. Null if there is nothing to move.
    /// </summary>
    public VoxelBody? SplitOff(IReadOnlyList<int> cells)
    {
        if (_owner is not null)
        {
            return null;
        }

        return Grid.Extract(cells) is { } piece ? Detach(piece.Piece, piece.CenterOffset) : null;
    }

    /// <summary>The cells already moved out of this grid into <paramref name="grid"/> (centre <paramref name="centerOffset"/> from the grid's centre) become a new dynamic body that keeps flying as this one was.</summary>
    private VoxelBody? Detach(VoxelGrid grid, Vector3 centerOffset)
    {
        if (grid.OccupiedBounds() is null)
        {
            return null;
        }

        var s = _world.GetState(BodyId);
        // The new body is built around the piece's own grid (origin = grid centre).
        var pieceCenterLocal = GridCenterLocal + centerOffset;
        var pieceWorld = s.Position + Vector3.Transform(pieceCenterLocal, s.Orientation);
        var pieceMass = MassOf(grid);
        var id = _world.AddDynamicBox(pieceWorld, s.Orientation, grid.HalfExtents, pieceMass);
        _world.CopyCollisionFilter(BodyId, id);
        _world.SetLinearVelocity(id, s.LinearVelocity + Vector3.Cross(s.AngularVelocity, pieceWorld - s.Position));
        _world.SetAngularVelocity(id, s.AngularVelocity);
        var fragment = new VoxelBody(_world, id, grid, _massPerCell, _materialMass) { _densityScale = _densityScale };
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
        if (Massless)
        {
            return; // 剛体を借りているだけの接触の形（関節のつなぎ）: 剛体の形も質量も変えない
        }

        if (_owner is not null)
        {
            _owner.UpdateMass(); // one rigid body, one collider and one mass: the owner fits them to every grid it carries
            return;
        }

        var (wantCenter, half, mass) = _companions.Count == 0 ? Fit(this, !_materialMass) : FitAll(!_materialMass);
        Vector3? inertia = null;
        if (_materialMass)
        {
            (mass, var counted) = MassAndInertia();
            inertia = counted;
        }

        // Only rebuild the collider when it actually changes (the rebuild is not free): a crater that does not move the
        // bounding box changes only the mass, and a small change in mass is not worth a rebuild either.
        var sameShape = Vector3.DistanceSquared(_applied.Center, wantCenter) < 1e-10f && Vector3.DistanceSquared(_applied.Half, half) < 1e-10f;
        if (sameShape && inertia is { } moments)
        {
            // 材質から数える体: 箱が同じなら当たりの形は作り直さず、質量と慣性だけ入れ直す（削れ・中の格子の移動で慣性も変わる）。
            if (mass != _applied.Mass || moments != _appliedInertia)
            {
                _world.SetDynamicMass(BodyId, mass, moments);
                _applied = (_applied.Center, _applied.Half, mass);
                _appliedInertia = moments;
            }

            return;
        }

        if (sameShape && MathF.Abs(_applied.Mass - mass) <= 0.02f * _applied.Mass)
        {
            return;
        }

        _world.ReshapeDynamicBox(BodyId, wantCenter, half, mass, inertia);
        _applied = (wantCenter, half, mass);
        _appliedInertia = inertia ?? default;
    }

    /// <summary>The collider box (body-frame centre and half extents) and mass that fit one grid's body alone.</summary>
    private static (Vector3 Center, Vector3 Half, float Mass) Fit(VoxelBody body, bool withMass = true)
    {
        var (center, half) = body.Grid.OccupiedBounds() ?? (Vector3.Zero, body.Grid.CellSize * 0.5f);
        return (body.GridCenterLocal + center, half, withMass ? body.MassOf(body.Grid) : 0f);
    }

    /// <summary>The box that covers the cells of this grid and every companion (empty grids add nothing), and the sum of their masses.</summary>
    private (Vector3 Center, Vector3 Half, float Mass) FitAll(bool withMass = true)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        var mass = 0f;
        var any = false;
        for (var i = -1; i < _companions.Count; i++)
        {
            var body = i < 0 ? this : _companions[i];
            mass += withMass ? body.MassOf(body.Grid) : 0f;
            if (body.Grid.OccupiedBounds() is { } bounds)
            {
                any = true;
                var center = body.GridCenterLocal + bounds.Center;
                min = Vector3.Min(min, center - bounds.HalfExtents);
                max = Vector3.Max(max, center + bounds.HalfExtents);
            }
        }

        if (!any)
        {
            var empty = Fit(this);
            return (empty.Center, empty.Half, mass);
        }

        return ((min + max) * 0.5f, (max - min) * 0.5f, mass);
    }

    private float MassOf(VoxelGrid grid)
    {
        if (!_materialMass)
        {
            return MathF.Max(1e-4f, _massPerCell * Math.Max(1, grid.FilledCount));
        }
        var volume = grid.CellSize.X * grid.CellSize.Y * grid.CellSize.Z;
        var mass = 0f;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != 0)
            {
                mass += grid.Palette[grid.MaterialAt(i)].MechanicsOrDefault.Density * _densityScale * volume;
            }
        }
        return MathF.Max(1e-4f, mass);
    }
}
