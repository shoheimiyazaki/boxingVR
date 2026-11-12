using System.Numerics;
using System.Runtime.CompilerServices;
using Aigis.Foundation.Voxels;

namespace Aigis.Physics.Voxels;

/// <summary>What one <see cref="VoxelContact.Resolve"/> did.</summary>
/// <param name="Touched">Whether the two bodies' voxels met during the step.</param>
/// <param name="A">The first body passed.</param>
/// <param name="B">The second body passed.</param>
/// <param name="RemovedA">Cells of <paramref name="A"/> crushed or cut away (its grid-local centres, before any snap).</param>
/// <param name="RemovedB">Cells of <paramref name="B"/> crushed or cut away.</param>
/// <param name="Point">Where they met (world).</param>
/// <param name="Normal">The face of <paramref name="B"/> they met at, pointing out of B toward A (world, unit).</param>
/// <param name="Direction">How A moved relative to B at the contact (world, unit): the way a cut runs.</param>
/// <param name="Pressure">The highest contact pressure the blow reached [Pa] (energy over give, over the area touching).</param>
/// <param name="Force">The highest contact force [N] (capped by the softer side's yield strength over the area).</param>
/// <param name="EnergySpent">Kinetic energy spent breaking cells [J].</param>
/// <param name="Stopped">Whether the blow was stopped (the bodies bounced or came to rest against each other) rather
/// than cutting on through.</param>
/// <param name="SnappedA">If <paramref name="A"/> snapped under the blow, the piece that broke off.</param>
/// <param name="SnappedB">If <paramref name="B"/> snapped, the piece that broke off.</param>
/// <param name="ImpulseOnB">The impulse [N·s] the contact gave <paramref name="B"/> at <paramref name="Point"/> (A got the
/// opposite): how hard the blow knocked it.</param>
public sealed record VoxelContactResult(
    bool Touched, VoxelBody A, VoxelBody B, IReadOnlyList<RemovedVoxel> RemovedA, IReadOnlyList<RemovedVoxel> RemovedB,
    Vector3 Point, Vector3 Normal, Vector3 Direction, float Pressure, float Force, float EnergySpent, bool Stopped,
    VoxelBody? SnappedA, VoxelBody? SnappedB, Vector3 ImpulseOnB = default)
{
    /// <summary>The cells <paramref name="body"/> lost (empty if it is neither side).</summary>
    public IReadOnlyList<RemovedVoxel> RemovedFor(VoxelBody body) => body == A ? RemovedA : body == B ? RemovedB : [];

    /// <summary>The piece that snapped off <paramref name="body"/>, if any.</summary>
    public VoxelBody? SnappedFor(VoxelBody body) => body == A ? SnappedA : body == B ? SnappedB : null;

    internal static VoxelContactResult Miss(VoxelBody a, VoxelBody b) =>
        new(false, a, b, [], [], Vector3.Zero, Vector3.Zero, Vector3.Zero, 0f, 0f, 0f, false, null, null);
}

/// <summary>
/// Collision between two voxel bodies, and what it breaks. Nothing here knows what a sword or an arm is: whether a cell
/// breaks follows only from the shape of what touches (how concentrated the blow is) and the materials.
/// <para>
/// <b>The model.</b> Over one physics step the solver sweeps one body's cells through the other's grid (relative motion,
/// finely sampled so a fast blade cannot pass through). At first contact the blow carries the kinetic energy
/// <c>E = ½ m v²</c> of the two bodies' relative motion at the contact (m = their effective mass there, rotation
/// included). It is stopped over the two materials' give δ, so it pushes with force <c>F = E / δ</c>, spread over the area
/// <c>A</c> the two touch once the contact has given way by δ (the area projected across the blow). The pressure is
/// <c>p = F / A</c>:
/// </para>
/// <list type="bullet">
/// <item>An edge or a point touches little, so the same blow makes a high pressure; the flat of a blade touches much, so a low one.</item>
/// <item>If <c>p</c> is above a side's <see cref="VoxelMechanics.Strength"/>, that side yields: its cells in the way are
/// crushed or cut away as the blow advances, and every metre it advances costs <c>strength × area</c> of the energy
/// (plastic work). The pressure at the contact can never be much more than the softer side's strength (it flows first),
/// so only the softer side breaks — iron cuts wood, wood never cuts iron — unless the two are about as strong (both yield).</item>
/// <item>As the blow goes in, the area changes with the shape (a wedge widens, a spike does not) and the energy runs down,
/// so the pressure falls; when it drops below the strength of what is in the way (or the blow hits something stronger,
/// a bone), the blow stops there.</item>
/// <item>A stopped blow is an ordinary inelastic collision: the approach speed along the face is taken out (shared by
/// momentum), and the lighter body is set back to where the blow stopped. A blow that cut on loses only the speed it spent.</item>
/// </list>
/// <para>
/// <b>Snapping.</b> A body made of materials with a <see cref="VoxelMechanics.Tensile"/> strength (a weapon, not a body
/// part) is also checked as a beam: the contact force and the body's own inertia bend it, and the bending stress at
/// every cross-section along its length is read from the cells of that section (<see cref="VoxelBending"/>). Where it
/// exceeds the tensile strength, the body breaks in two there (<see cref="VoxelBody.SplitAt"/>).
/// </para>
/// Call it after a <see cref="PhysicsWorld.Step(float)"/> with both bodies' states from before that step. The two bodies
/// should not also collide in the physics engine (<see cref="PhysicsWorld.SetPairCollisionEnabled"/>): their boxes would
/// keep the voxels from ever meeting. Deterministic: no randomness, every loop in index order.
/// </summary>
public static class VoxelContact
{
    /// <summary>How much more than the softer side's strength the contact pressure can reach (both sides yield only when
    /// they are about this close in strength).</summary>
    public const float FlowMargin = 1.25f;

    /// <summary>Bounce of a stopped blow (0 = dead stop).</summary>
    public const float Restitution = 0.1f;

    /// <summary>Most samples of the sweep in one step.</summary>
    private const int MaxSamples = 400;

    /// <summary>Most samples the give looks ahead.</summary>
    private const int MaxLookahead = 48;

    /// <summary>Most samples an overlap left from before the step is pushed out by in one step (beyond that, the rest on the
    /// next steps). A smaller cap leaves a blade overlapping over several steps, where it drags instead of cutting
    /// (measured: a kesa through the chest cut 6 cells at 16, 17 at 64).</summary>
    private const int MaxPushOut = 64;

    /// <summary>Resolves the contact between <paramref name="a"/> and <paramref name="b"/> over the step just taken.</summary>
    /// <param name="a">One body.</param>
    /// <param name="aBefore">Its state before the step.</param>
    /// <param name="b">The other body (same world).</param>
    /// <param name="bBefore">Its state before the step.</param>
    /// <param name="driveA">
    /// A steady force [N, world] still pushing <paramref name="a"/> besides its momentum (a hand pressing the blade it holds).
    /// Only its part along the blow (pushing A into B) counts. That drive's work over each bit of advance pays for breaking
    /// cells before the blow's own energy does, and it adds to how hard the blow pushes, so a held edge can slice what the
    /// hand alone can push through even when the blow is slow. It does nothing while the two are not coming together.
    /// Zero (the default) is a free blow.
    /// </param>
    /// <param name="driveB">The same for <paramref name="b"/> (pushing B into A).</param>
    public static VoxelContactResult Resolve(VoxelBody a, BodyState aBefore, VoxelBody b, BodyState bBefore, Vector3 driveA = default, Vector3 driveB = default)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.World != b.World)
        {
            throw new ArgumentException("Both bodies must live in the same physics world.", nameof(b));
        }

        // Sweep the finer grid's cells through the coarser grid (a fine edge sampled into coarse flesh, never the reverse).
        var aFiner = MinCell(a.Grid) < MinCell(b.Grid) - 1e-7f || (MathF.Abs(MinCell(a.Grid) - MinCell(b.Grid)) <= 1e-7f && a.Grid.FilledCount <= b.Grid.FilledCount);
        var (probe, probeBefore, field, fieldBefore) = aFiner ? (a, aBefore, b, bBefore) : (b, bBefore, a, aBefore);
        // The sweep moves the probe into the field: what pushes it on is the probe's drive less the field's.
        var sweep = new Sweep(probe, probeBefore, field, fieldBefore, aFiner ? driveA - driveB : driveB - driveA);
        var hit = sweep.Run();
        if (hit is null)
        {
            return VoxelContactResult.Miss(a, b);
        }

        var h = hit.Value;
        var snapProbe = h.Force > 0f ? Snap(probe, h.Point, -h.Direction, h.Force, h.Energy) : null;
        var snapField = h.Force > 0f ? Snap(field, h.Point, h.Direction, h.Force, h.Energy) : null;
        // The sweep's normal points out of the field toward the probe; the result's normal points out of B toward A.
        var normal = aFiner ? h.Normal : -h.Normal;
        var direction = aFiner ? h.Direction : -h.Direction;
        return new VoxelContactResult(true, a, b,
            aFiner ? h.RemovedProbe : h.RemovedField, aFiner ? h.RemovedField : h.RemovedProbe,
            h.Point, normal, direction, h.Pressure, h.Force, h.EnergySpent, h.Stopped,
            aFiner ? snapProbe : snapField, aFiner ? snapField : snapProbe,
            aFiner ? -sweep.ImpulseOnProbe : sweep.ImpulseOnProbe);
    }

    private static float MinCell(VoxelGrid grid) => MathF.Min(grid.CellSize.X, MathF.Min(grid.CellSize.Y, grid.CellSize.Z));

    /// <summary>
    /// Snaps <paramref name="body"/> where the blow bends it past its tensile strength (only a body every material of
    /// which has one). The force on the beam is the contact's <paramref name="contactForce"/>, but no more than a
    /// springy beam can take from the blow's <paramref name="energy"/> before it has flexed it all up
    /// (<c>√(2 E k)</c>, k = the beam's bending stiffness): a slender beam gives, which caps the force. Returns the piece
    /// that broke off.
    /// </summary>
    private static VoxelBody? Snap(VoxelBody body, Vector3 worldPoint, Vector3 worldDirection, float contactForce, float energy)
    {
        var grid = body.Grid;
        for (var m = 1; m < grid.Palette.Count; m++)
        {
            if (grid.Remaining((byte)m) > 0 && grid.Palette[(byte)m].MechanicsOrDefault.Tensile is null)
            {
                return null;
            }
        }

        var s = body.World.GetState(body.BodyId);
        var inverse = Quaternion.Conjugate(s.Orientation);
        var localPoint = body.WorldToGrid(worldPoint);
        var localDirection = Vector3.Transform(worldDirection, inverse);
        var stiffness = VoxelBending.BeamStiffness(grid, localDirection);
        var force = stiffness is { } k ? MathF.Min(contactForce, MathF.Sqrt(2f * energy * k)) : contactForce;
        if (VoxelBending.FindBreak(grid, localPoint, localDirection * force) is not { } cut)
        {
            return null;
        }

        return body.SplitAt(cut.Axis, cut.Slice, cut.TakeHigh);
    }

    private readonly record struct Hit(
        Vector3 Point, Vector3 Normal, Vector3 Direction, float Pressure, float Force, float Energy, float EnergySpent, bool Stopped,
        List<RemovedVoxel> RemovedProbe, List<RemovedVoxel> RemovedField);

    /// <summary>Cells per side of a brick: the filled cells are grouped into bricks so whole bricks far from the field are skipped at once.</summary>
    private const int BrickSize = 8;

    /// <summary>A brick of filled cells: the sphere around their centres, and where they sit in the cell arrays.</summary>
    private readonly record struct Brick(Vector3 Center, float Radius, int Start, int Count);

    /// <summary>The filled cells of a grid (index and centre), grouped by brick, cached until the grid changes.</summary>
    private static readonly ConditionalWeakTable<VoxelGrid, StrongBox<(int Version, int[] Index, Vector3[] Center, Brick[] Bricks)>> Filled = new();

    private static (int[] Index, Vector3[] Center, Brick[] Bricks) FilledCells(VoxelGrid grid)
    {
        var box = Filled.GetValue(grid, static _ => new StrongBox<(int, int[], Vector3[], Brick[])>((-1, [], [], [])));
        if (box.Value.Version != grid.Version)
        {
            var groups = new Dictionary<(int, int, int), List<int>>();
            for (var i = 0; i < grid.CellCount; i++)
            {
                if (grid.MaterialAt(i) != 0)
                {
                    var (x, y, z) = grid.Coordinates(i);
                    var key = (x / BrickSize, y / BrickSize, z / BrickSize);
                    if (!groups.TryGetValue(key, out var cells))
                    {
                        groups[key] = cells = [];
                    }

                    cells.Add(i);
                }
            }

            var index = new List<int>(grid.FilledCount);
            var center = new List<Vector3>(grid.FilledCount);
            var bricks = new List<Brick>(groups.Count);
            foreach (var cells in groups.Values)
            {
                var start = index.Count;
                var lo = new Vector3(float.MaxValue);
                var hi = new Vector3(float.MinValue);
                foreach (var i in cells)
                {
                    var (x, y, z) = grid.Coordinates(i);
                    var c = grid.CellCenter(x, y, z);
                    index.Add(i);
                    center.Add(c);
                    lo = Vector3.Min(lo, c);
                    hi = Vector3.Max(hi, c);
                }

                bricks.Add(new Brick((lo + hi) * 0.5f, 0.5f * Vector3.Distance(lo, hi), start, cells.Count));
            }

            box.Value = (grid.Version, [.. index], [.. center], [.. bricks]);
        }

        return (box.Value.Index, box.Value.Center, box.Value.Bricks);
    }

    /// <summary>
    /// One step's sweep of the probe's cells through the field's grid. Everything is in the field's grid frame at the end
    /// of the step (the field "stands still"; the probe moves relative to it from its pose before the step to its pose now).
    /// </summary>
    private sealed class Sweep
    {
        private readonly VoxelBody _probe;
        private readonly VoxelBody _field;
        private readonly BodyState _probeNow;
        private readonly BodyState _fieldNow;
        private readonly Quaternion _q0;
        private readonly Vector3 _axis;
        private readonly float _angle;
        private readonly Vector3 _t0;
        private readonly Vector3 _t1;
        private readonly int[] _cellIndex;
        private readonly Vector3[] _cellCenter;
        private readonly float _sample;
        private readonly int _samples;
        private readonly float _advance;
        private readonly Vector3 _drive;

        /// <summary>Field cells broken during this sweep, with what they were. They stay in the way (and cost work) until
        /// the step is over: a fine edge crossing a coarse cell pays for the whole crossing, not only the moment it enters.</summary>
        private readonly Dictionary<int, byte> _brokenField = [];

        public Sweep(VoxelBody probe, BodyState probeBefore, VoxelBody field, BodyState fieldBefore, Vector3 drive)
        {
            _drive = drive;
            _probe = probe;
            _field = field;
            _probeNow = probe.World.GetState(probe.BodyId);
            _fieldNow = field.World.GetState(field.BodyId);
            var (q0, t0) = Relative(probe.GridToWorld(probeBefore), field.GridToWorld(fieldBefore));
            var (q1, t1) = Relative(probe.GridToWorld(_probeNow), field.GridToWorld(_fieldNow));
            _q0 = q0;
            _t0 = t0;
            _t1 = t1;
            // The turn from q0 to q1, as an axis and an angle (the shorter way round).
            var dq = q1 * Quaternion.Conjugate(q0);
            if (dq.W < 0f)
            {
                dq = -dq;
            }

            var sinHalf = new Vector3(dq.X, dq.Y, dq.Z).Length();
            _angle = 2f * MathF.Atan2(sinHalf, dq.W);
            _axis = sinHalf > 1e-7f ? new Vector3(dq.X, dq.Y, dq.Z) / sinHalf : Vector3.UnitX;
            _sample = 0.5f * MathF.Min(MinCell(probe.Grid), MinCell(field.Grid));
            var reach = probe.Grid.HalfExtents.Length();
            var travel = Vector3.Distance(t0, t1) + (_angle * reach);
            _samples = Math.Clamp((int)MathF.Ceiling(travel / _sample), 1, MaxSamples);
            _advance = travel / _samples;
            // Far apart all step long (the probe's bounding sphere never comes near the field's box): nothing to sample.
            // Then the field's bounding sphere against the probe's box, in the probe's frame (a sword whose sphere reaches
            // every part of a fighter still misses most of them by its thin box).
            Far = DistanceToSegment(Vector3.Zero, t0, t1) > reach + field.Grid.HalfExtents.Length()
                || BoxDistance(probe.Grid.HalfExtents, FieldOrigin(q0, t0), FieldOrigin(q1, t1)) > field.Grid.HalfExtents.Length() + MaxCell(probe.Grid);
            (_cellIndex, _cellCenter) = Far ? ([], []) : Candidates(FilledCells(probe.Grid), reach);
        }

        /// <summary>The field grid's centre in the probe grid's frame, for the probe's pose (<paramref name="q"/>, <paramref name="t"/>) in the field frame.</summary>
        private static Vector3 FieldOrigin(Quaternion q, Vector3 t) => Vector3.Transform(-t, Quaternion.Conjugate(q));

        /// <summary>The least distance from a box of <paramref name="half"/> (at the origin) to the segment from
        /// <paramref name="a"/> to <paramref name="b"/>, from below: the nearer end's distance, less the segment's length.</summary>
        private static float BoxDistance(Vector3 half, Vector3 a, Vector3 b)
        {
            static float Outside(Vector3 half, Vector3 p) => Vector3.Max(Vector3.Abs(p) - half, Vector3.Zero).Length();
            return MathF.Min(Outside(half, a), Outside(half, b)) - Vector3.Distance(a, b);
        }

        /// <summary>The two never come near each other this step.</summary>
        public bool Far { get; }

        /// <summary>
        /// The probe cells that can come near the field's box at all during the sweep (and the give's look past it): each
        /// cell's path, sampled at the start, middle, end and a half step on, boxed and widened by how far a turning path
        /// bows out between samples, must meet the field's box. Saves sampling the whole probe (a sword) every time.
        /// </summary>
        private (int[] Index, Vector3[] Center) Candidates((int[] Index, Vector3[] Center, Brick[] Bricks) all, float reach)
        {
            var half = _field.Grid.HalfExtents + new Vector3(MaxCell(_field.Grid) + MaxCell(_probe.Grid));
            var bow = (reach * _angle * _angle / 8f) + (0.5f * travelStep());
            var poses = new[] { Pose(0), Pose(_samples / 2), Pose(_samples), Pose(_samples + (_samples / 2) + MaxLookahead) };
            var index = new List<int>();
            var center = new List<Vector3>();
            bool Meets(Vector3 lo, Vector3 hi, float widen) =>
                lo.X - widen <= half.X && hi.X + widen >= -half.X && lo.Y - widen <= half.Y && hi.Y + widen >= -half.Y && lo.Z - widen <= half.Z && hi.Z + widen >= -half.Z;

            foreach (var brick in all.Bricks)
            {
                // Every cell centre in the brick lies within its sphere, so a brick whose sphere's path misses the field's box
                // has no cell that meets it.
                var blo = new Vector3(float.MaxValue);
                var bhi = new Vector3(float.MinValue);
                foreach (var (q, t) in poses)
                {
                    var p = Vector3.Transform(brick.Center, q) + t;
                    blo = Vector3.Min(blo, p);
                    bhi = Vector3.Max(bhi, p);
                }

                if (!Meets(blo, bhi, bow + brick.Radius))
                {
                    continue;
                }

                for (var i = brick.Start; i < brick.Start + brick.Count; i++)
                {
                    TestCell(i);
                }
            }

            // In cell order, as without the bricks (which cells are tried first decides what breaks first).
            var indexArray = index.ToArray();
            var centerArray = center.ToArray();
            Array.Sort(indexArray, centerArray);
            return (indexArray, centerArray);

            void TestCell(int i)
            {
                var lo = new Vector3(float.MaxValue);
                var hi = new Vector3(float.MinValue);
                foreach (var (q, t) in poses)
                {
                    var p = Vector3.Transform(all.Center[i], q) + t;
                    lo = Vector3.Min(lo, p);
                    hi = Vector3.Max(hi, p);
                }

                if (Meets(lo, hi, bow))
                {
                    index.Add(all.Index[i]);
                    center.Add(all.Center[i]);
                    _reachLo = Vector3.Min(_reachLo, lo - new Vector3(bow));
                    _reachHi = Vector3.Max(_reachHi, hi + new Vector3(bow));
                }
            }

            float travelStep() => _advance * (MaxLookahead + 1);
        }

        /// <summary>The box (field frame) the candidate probe cells sweep through: only field cells in it can be touched.</summary>
        private Vector3 _reachLo = new(float.MaxValue);
        private Vector3 _reachHi = new(float.MinValue);

        private static float MaxCell(VoxelGrid grid) => MathF.Max(grid.CellSize.X, MathF.Max(grid.CellSize.Y, grid.CellSize.Z));

        private static (Quaternion Rotation, Vector3 Translation) Relative(Matrix4x4 probeToWorld, Matrix4x4 fieldToWorld)
        {
            Matrix4x4.Invert(fieldToWorld, out var worldToField);
            Matrix4x4.Decompose(probeToWorld * worldToField, out _, out var rotation, out var translation);
            return (Quaternion.Normalize(rotation), translation);
        }

        /// <summary>The probe's pose in the field's frame at sample <paramref name="k"/> (0 = before the step,
        /// <see cref="_samples"/> = now; past that, the motion carried on).</summary>
        private (Quaternion Rotation, Vector3 Translation) Pose(int k)
        {
            var t = (float)k / _samples;
            var turn = _angle > 1e-7f ? Quaternion.CreateFromAxisAngle(_axis, _angle * t) : Quaternion.Identity;
            return (turn * _q0, Vector3.Lerp(_t0, _t1, t));
        }

        /// <summary>The probe cells inside a filled field cell at sample <paramref name="k"/> (moved on by <paramref name="offset"/>, field frame): (probe cell slot, field cell
        /// index, probe cell centre in the field frame).</summary>
        private List<(int Slot, int Field, Vector3 At)> Overlaps(int k, Vector3 offset = default)
        {
            if (_cellCenter.Length == 0)
            {
                return [];
            }

            var (q, t) = Pose(k);
            var hits = new List<(int, int, Vector3)>();
            var grid = _field.Grid;
            for (var i = 0; i < _cellCenter.Length; i++)
            {
                if (_probe.Grid.MaterialAt(_cellIndex[i]) == 0)
                {
                    continue;
                }

                var p = Vector3.Transform(_cellCenter[i], q) + t + offset;
                var f = grid.CellAt(p);
                if (f >= 0 && (grid.MaterialAt(f) != 0 || _brokenField.ContainsKey(f)))
                {
                    hits.Add((i, f, p));
                }
            }

            return hits;
        }

        /// <summary>The area [m²] the overlapping probe cells cover across the blow (projected on the plane square to
        /// <paramref name="along"/>, field frame, in probe-cell-sized bins).</summary>
        private float Area(List<(int Slot, int Field, Vector3 At)> overlaps, Vector3 along)
        {
            if (overlaps.Count == 0)
            {
                return 0f;
            }

            var u = Vector3.Normalize(Vector3.Cross(along, MathF.Abs(along.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
            var w = Vector3.Cross(along, u);
            var bin = MinCell(_probe.Grid);
            var bins = new HashSet<(int, int)>();
            foreach (var (_, _, at) in overlaps)
            {
                bins.Add(((int)MathF.Floor(Vector3.Dot(at, u) / bin), (int)MathF.Floor(Vector3.Dot(at, w) / bin)));
            }

            return bins.Count * bin * bin;
        }

        public Hit? Run()
        {
            if (Far)
            {
                return null;
            }

            var first = -1;
            List<(int Slot, int Field, Vector3 At)> touching = [];
            for (var k = 0; k <= _samples; k++)
            {
                touching = Overlaps(k);
                if (touching.Count > 0)
                {
                    first = k;
                    break;
                }
            }

            if (first < 0)
            {
                return null;
            }

            // The contact in the world, the face of the field there, and how fast they come together.
            var fieldRotation = _fieldNow.Orientation;
            var fieldToWorld = _field.GridToWorld(_fieldNow);
            var localPoint = touching.Aggregate(Vector3.Zero, (sum, o) => sum + o.At) / touching.Count;
            var point = Vector3.Transform(localPoint, fieldToWorld);
            var normal = Vector3.Transform(OutwardNormal(_field.Grid, touching), fieldRotation);
            var relative = VelocityAt(_probeNow, point) - VelocityAt(_fieldNow, point);
            var speed = relative.Length();
            var approach = -Vector3.Dot(relative, normal);
            var removedProbe = new List<RemovedVoxel>();
            var removedField = new List<RemovedVoxel>();
            var mEffNormal = EffectiveMass(point, normal);
            if (approach <= 1e-5f || speed <= 1e-5f || mEffNormal <= 0f)
            {
                // Resting, sliding or parting: nothing to break. An overlap left from before the step (an arm pressing a blade in
                // that cannot cut) is pushed out: what does not break is pushed aside, it does not pass through.
                if (first == 0)
                {
                    Separate(Vector3.Zero, normal, 0);
                }

                return new Hit(point, normal, speed > 1e-5f ? relative / speed : -normal, 0f, 0f, 0f, 0f, true, removedProbe, removedField);
            }

            var direction = relative / speed;
            var localAlong = Vector3.Transform(direction, Quaternion.Conjugate(fieldRotation));
            var mEff = EffectiveMass(point, direction);
            var energy = 0.5f * mEff * speed * speed;
            var drive = MathF.Max(0f, Vector3.Dot(_drive, direction)); // only the push along the blow drives it on
            var left = energy;
            var fieldFront = FieldFront(localAlong);
            // The contact point riding on the probe: how far it moves along the blow from sample to sample is how far the cut
            // advances (a turning blade's tip moves further than the place it cuts with).
            var (qFirst, tFirst) = Pose(first);
            var contactOnProbe = Vector3.Transform(localPoint - tFirst, Quaternion.Conjugate(qFirst));
            float peakPressure = 0f, peakForce = 0f;
            var stoppedAt = -1;
            for (var k = first; k <= _samples; k++)
            {
                var now = k == first ? touching : Overlaps(k);
                if (now.Count == 0)
                {
                    continue;
                }

                // How hard the blow pushes: its energy over the give. What each side can bear: the force it takes to push
                // through the patch the two touch over once the contact has given way by the give (its softer cells give
                // first and leave the load on the harder ones, so a little bone under the blade carries the whole blow).
                var give = Give(now);
                var push = (left / give) + drive;
                var patch = Patch(k, fieldFront, give);
                // A patch that missed the field's heightfield (a corner between bins) falls back to the cells overlapping now.
                var probeHold = patch.Count > 0 ? Hold(patch, probeSide: true) : Resistance(now, probeSide: true, localAlong);
                var fieldHold = patch.Count > 0 ? Hold(patch, probeSide: false) : Resistance(now, probeSide: false, localAlong);
                var patchArea = patch.Count > 0 ? patch.Count * _bin * _bin : Area(now, localAlong);
                // At the contact the force cannot go much past what the softer side bears: it flows first.
                var force = MathF.Min(push, MathF.Min(probeHold, fieldHold) * FlowMargin);
                var probeYields = force > probeHold;
                var fieldYields = force > fieldHold;
                peakPressure = patchArea > 0f ? MathF.Max(peakPressure, push / patchArea) : peakPressure;
                peakForce = MathF.Max(peakForce, force);
                if (!probeYields && !fieldYields)
                {
                    stoppedAt = k;
                    break;
                }

                // Pushing on through costs plastic work: the force it takes to push through the cells in the way now, for
                // this advance. A blow that cannot make that force (a bone it has reached) stops there.
                var through = probeYields && fieldYields
                    ? MathF.Max(Resistance(now, probeSide: true, localAlong), Resistance(now, probeSide: false, localAlong))
                    : Resistance(now, probeSide: probeYields, localAlong);
                // A steady drive pays for its share of the advance first; the blow's own energy pays the rest.
                var advance = Advance(contactOnProbe, k, localAlong);
                var cost = through * advance;
                var driven = drive * advance;
                if (push < through || cost > left + driven)
                {
                    stoppedAt = k;
                    break;
                }

                left -= MathF.Max(0f, cost - driven);
                foreach (var (slot, fieldCell, _) in now)
                {
                    if (probeYields)
                    {
                        _probe.Grid.Break(_cellIndex[slot], removedProbe);
                    }

                    var material = _field.Grid.MaterialAt(fieldCell);
                    if (fieldYields && material != 0 && _field.Grid.Break(fieldCell, removedField))
                    {
                        _brokenField[fieldCell] = material;
                    }
                }
            }

            var stopped = stoppedAt >= 0;
            if (stopped)
            {
                // An inelastic collision along the face, and back to where the blow stopped.
                var j = mEffNormal * (1f + Restitution) * approach;
                Push(point, normal * j);
                Separate(contactOnProbe, normal, Math.Max(0, stoppedAt - 1));
            }
            else if (left < energy)
            {
                // Cut on through: only the speed spent breaking cells is lost.
                var after = speed * MathF.Sqrt(left / energy);
                Push(point, -direction * (mEff * (speed - after)));
            }

            if (removedProbe.Count > 0)
            {
                _probe.UpdateMass();
            }

            if (removedField.Count > 0)
            {
                _field.UpdateMass();
            }

            return new Hit(point, normal, direction, peakPressure, peakForce, energy, energy - left, stopped, removedProbe, removedField);
        }

        /// <summary>The side of one bin of the contact heightfield (<see cref="Patch"/>): a probe cell.</summary>
        private float _bin;

        /// <summary>
        /// The field's face toward the blow, as a heightfield across it: for every bin (probe-cell-sized, on the plane square
        /// to <paramref name="along"/>, field frame), how far along the blow its nearest cell starts, and that cell's
        /// material. Cells broken during this sweep still count (they were in the way when the blow arrived).
        /// </summary>
        private Dictionary<(int, int), (float Front, byte Material)> FieldFront(Vector3 along)
        {
            _bin = MinCell(_probe.Grid);
            (_u, _w) = Basis(along);
            _along = along;
            var grid = _field.Grid;
            var front = new Dictionary<(int, int), (float, byte)>();
            // A cube seen at a slant covers more than half a cell on either side: its projected half extent along each axis.
            var half = grid.CellSize * 0.5f;
            float Projected(Vector3 axis) => (MathF.Abs(axis.X) * half.X) + (MathF.Abs(axis.Y) * half.Y) + (MathF.Abs(axis.Z) * half.Z);
            var (ru, rw, ra) = (Projected(_u), Projected(_w), Projected(along));
            var lo = _reachLo - half;
            var hi = _reachHi + half;
            for (var i = 0; i < grid.CellCount; i++)
            {
                var material = FieldMaterial(i);
                if (material == 0)
                {
                    continue;
                }

                var (x, y, z) = grid.Coordinates(i);
                var c = grid.CellCenter(x, y, z);
                if (c.X < lo.X || c.Y < lo.Y || c.Z < lo.Z || c.X > hi.X || c.Y > hi.Y || c.Z > hi.Z)
                {
                    continue;
                }

                var depth = Vector3.Dot(c, along) - ra;
                var cu = Vector3.Dot(c, _u);
                var cw = Vector3.Dot(c, _w);
                for (var bu = (int)MathF.Floor((cu - ru) / _bin); bu <= (int)MathF.Floor((cu + ru) / _bin); bu++)
                {
                    for (var bw = (int)MathF.Floor((cw - rw) / _bin); bw <= (int)MathF.Floor((cw + rw) / _bin); bw++)
                    {
                        if (!front.TryGetValue((bu, bw), out var f) || depth < f.Item1)
                        {
                            front[(bu, bw)] = (depth, material);
                        }
                    }
                }
            }

            return front;
        }

        private Vector3 _u;
        private Vector3 _w;
        private Vector3 _along;

        private static (Vector3 U, Vector3 W) Basis(Vector3 along)
        {
            var u = Vector3.Normalize(Vector3.Cross(along, MathF.Abs(along.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
            return (u, Vector3.Cross(along, u));
        }

        /// <summary>
        /// The contact patch at sample <paramref name="k"/>: the probe's face toward the blow as a heightfield on the same bins,
        /// and the bins where the two faces come within <paramref name="give"/> of the place they meet deepest — the patch
        /// they press over once the contact has given way. An edge or a point leaves a thin strip or a dot; a flat face
        /// against a round limb, the whole chord its curve allows. Each bin keeps the material at either face.
        /// </summary>
        private List<(byte Probe, byte Field)> Patch(int k, Dictionary<(int, int), (float Front, byte Material)> fieldFront, float give)
        {
            var (q, t) = Pose(k);
            var reach = 0.5f * MaxCell(_probe.Grid);
            var probeFront = new Dictionary<(int, int), (float Front, byte Material)>();
            for (var i = 0; i < _cellCenter.Length; i++)
            {
                var material = _probe.Grid.MaterialAt(_cellIndex[i]);
                if (material == 0)
                {
                    continue;
                }

                var p = Vector3.Transform(_cellCenter[i], q) + t;
                var key = ((int)MathF.Floor(Vector3.Dot(p, _u) / _bin), (int)MathF.Floor(Vector3.Dot(p, _w) / _bin));
                var lead = Vector3.Dot(p, _along) + reach;
                if (!probeFront.TryGetValue(key, out var f) || lead > f.Front)
                {
                    probeFront[key] = (lead, material);
                }
            }

            var deepest = float.MaxValue;
            foreach (var (key, pf) in probeFront)
            {
                if (fieldFront.TryGetValue(key, out var ff))
                {
                    deepest = MathF.Min(deepest, ff.Front - pf.Front);
                }
            }

            var patch = new List<(byte, byte)>();
            foreach (var (key, pf) in probeFront)
            {
                if (fieldFront.TryGetValue(key, out var ff) && ff.Front - pf.Front <= deepest + give)
                {
                    patch.Add((pf.Material, ff.Material));
                }
            }

            return patch;
        }

        /// <summary>The force [N] it takes to push through one side of the <paramref name="patch"/> (as
        /// <see cref="Resistance"/>, over the patch's bins).</summary>
        private float Hold(List<(byte Probe, byte Field)> patch, bool probeSide)
        {
            var grid = probeSide ? _probe.Grid : _field.Grid;
            var strengths = patch.Select(b => grid.Palette[probeSide ? b.Probe : b.Field].MechanicsOrDefault.Strength).ToArray();
            Array.Sort(strengths);
            // Strongest first: the i-th weakest has (n − i) bins at least as strong.
            var hold = 0f;
            for (var i = 0; i < strengths.Length; i++)
            {
                hold = MathF.Max(hold, strengths[i] * (strengths.Length - i) * _bin * _bin);
            }

            return hold;
        }

        /// <summary>
        /// The force [N] it takes to push through one side's cells among <paramref name="overlaps"/>: going from its
        /// weakest material to its strongest, the weaker cells give way and leave the load on the stronger ones, so the
        /// side holds until the force passes <c>strength × area</c> for every material (the area of the cells of that
        /// material or stronger, across the blow).
        /// </summary>
        private float Resistance(List<(int Slot, int Field, Vector3 At)> overlaps, bool probeSide, Vector3 along)
        {
            var strengths = new float[overlaps.Count];
            for (var i = 0; i < overlaps.Count; i++)
            {
                var (slot, field, _) = overlaps[i];
                var grid = probeSide ? _probe.Grid : _field.Grid;
                var material = probeSide ? grid.MaterialAt(_cellIndex[slot]) : FieldMaterial(field);
                strengths[i] = (material == 0 ? VoxelMechanics.Default : grid.Palette[material].MechanicsOrDefault).Strength;
            }

            var hold = 0f;
            foreach (var strength in strengths.Distinct())
            {
                var harder = new List<(int, int, Vector3)>();
                for (var i = 0; i < overlaps.Count; i++)
                {
                    if (strengths[i] >= strength)
                    {
                        harder.Add(overlaps[i]);
                    }
                }

                hold = MathF.Max(hold, strength * Area(harder, along));
            }

            return hold;
        }

        /// <summary>
        /// How far the contact gives [m] where the two touch now: the probe's give there, plus the give of the hardest field material it
        /// touches (what stops a blade is the bone it meets, not the flesh around it).
        /// </summary>
        private float Give(List<(int Slot, int Field, Vector3 At)> overlaps)
        {
            float probeGive = 0f, hardest = -1f, fieldGive = VoxelMechanics.Default.Give;
            var n = 0;
            foreach (var (slot, field, _) in overlaps)
            {
                var pm = _probe.Grid.MaterialAt(_cellIndex[slot]);
                probeGive += (pm == 0 ? VoxelMechanics.Default : _probe.Grid.Palette[pm].MechanicsOrDefault).Give;
                n++;
                var fm = FieldMaterial(field);
                var k = fm == 0 ? VoxelMechanics.Default : _field.Grid.Palette[fm].MechanicsOrDefault;
                if (k.Strength > hardest)
                {
                    hardest = k.Strength;
                    fieldGive = k.Give;
                }
            }

            return MathF.Max(1e-5f, (n == 0 ? VoxelMechanics.Default.Give : probeGive / n) + fieldGive);
        }

        /// <summary>A field cell's material, or what it was if this sweep broke it.</summary>
        private byte FieldMaterial(int cell) => _brokenField.TryGetValue(cell, out var was) ? was : _field.Grid.MaterialAt(cell);

        /// <summary>The impulse given to the probe this sweep (the field got the opposite).</summary>
        public Vector3 ImpulseOnProbe { get; private set; }

        /// <summary>Applies <paramref name="impulse"/> to the probe at <paramref name="point"/>, and its opposite to the field.</summary>
        private void Push(Vector3 point, Vector3 impulse)
        {
            ImpulseOnProbe += impulse;
            Apply(_probe.BodyId, _probeNow, point, impulse);
            Apply(_field.BodyId, _fieldNow, point, -impulse);
        }

        private void Apply(int id, BodyState s, Vector3 point, Vector3 impulse)
        {
            var world = _probe.World;
            if (world.IsKinematic(id))
            {
                return;
            }

            world.ApplyLinearImpulse(id, impulse);
            world.ApplyAngularImpulse(id, Vector3.Cross(point - s.Position, impulse));
        }

        /// <summary>How far [m] a point riding on the probe (<paramref name="onProbe"/>, probe frame) moves along
        /// <paramref name="along"/> (field frame) from sample <paramref name="k"/> − 1 to <paramref name="k"/>.</summary>
        private float Advance(Vector3 onProbe, int k, Vector3 along)
        {
            var (q0, t0) = Pose(Math.Max(0, k - 1));
            var (q1, t1) = Pose(k);
            var moved = (Vector3.Transform(onProbe, q1) + t1) - (Vector3.Transform(onProbe, q0) + t0);
            return MathF.Max(0f, Vector3.Dot(moved, along));
        }

        /// <summary>
        /// Moves the lighter body back so the contact point riding on the probe (<paramref name="onProbe"/>, probe frame) sits
        /// where it was at sample <paramref name="k"/> relative to the field (the turn is left as it is). At sample 0 there is no
        /// earlier free place to go back to: pushes them apart along the face by as far as they overlap now (<see cref="PushOutDepth"/>).
        /// </summary>
        private void Separate(Vector3 onProbe, Vector3 normal, int k)
        {
            var world = _probe.World;
            if (world.IsKinematic(_probe.BodyId) && world.IsKinematic(_field.BodyId))
            {
                return;
            }

            var (qk, tk) = Pose(k);
            var (qEnd, tEnd) = Pose(_samples);
            var shift = k > 0 ? Vector3.Transform((Vector3.Transform(onProbe, qk) + tk) - (Vector3.Transform(onProbe, qEnd) + tEnd), _fieldNow.Orientation)
                : normal * PushOutDepth(Vector3.Transform(normal, Quaternion.Conjugate(_fieldNow.Orientation)));
            var probeMass = world.GetMass(_probe.BodyId);
            var fieldMass = world.GetMass(_field.BodyId);
            var moveProbe = !world.IsKinematic(_probe.BodyId) && (world.IsKinematic(_field.BodyId) || probeMass <= fieldMass);
            if (moveProbe)
            {
                world.TranslateBody(_probe.BodyId, shift);
            }
            else if (!world.IsKinematic(_field.BodyId))
            {
                world.TranslateBody(_field.BodyId, -shift);
            }
        }

        /// <summary>
        /// How far [m] the probe, as it is now, must move out along the field's face (<paramref name="localNormal"/>, field frame) to
        /// touch no filled field cell (cells broken during this sweep still count), in whole samples, up to <see cref="MaxPushOut"/>
        /// samples (a deeper overlap is pushed out that far now and the rest on the next steps).
        /// </summary>
        private float PushOutDepth(Vector3 localNormal)
        {
            for (var n = 1; n <= MaxPushOut; n++)
            {
                if (Overlaps(_samples, localNormal * (n * _sample)).Count == 0)
                {
                    return n * _sample;
                }
            }

            return MaxPushOut * _sample;
        }

        private static Vector3 VelocityAt(BodyState s, Vector3 point) => s.LinearVelocity + Vector3.Cross(s.AngularVelocity, point - s.Position);

        /// <summary>The two bodies' effective mass for a push along <paramref name="direction"/> at <paramref name="point"/>
        /// (their masses and turning, a kinematic body counting as immovable). 0 if neither can move.</summary>
        private float EffectiveMass(Vector3 point, Vector3 direction)
        {
            var inverse = InverseMass(_probe.BodyId, _probeNow, point, direction) + InverseMass(_field.BodyId, _fieldNow, point, direction);
            return inverse > 1e-12f ? 1f / inverse : 0f;
        }

        private float InverseMass(int id, BodyState s, Vector3 point, Vector3 direction)
        {
            var world = _probe.World;
            if (world.IsKinematic(id))
            {
                return 0f;
            }

            var r = point - s.Position;
            var torque = Vector3.Cross(r, direction);
            var local = Vector3.Transform(torque, Quaternion.Conjugate(s.Orientation));
            var inertia = world.GetLocalInertiaDiagonal(id);
            var spin = Vector3.Transform(local / Vector3.Max(inertia, new Vector3(1e-12f)), s.Orientation);
            return (1f / world.GetMass(id)) + Vector3.Dot(Vector3.Cross(spin, r), direction);
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var t = ab.LengthSquared() > 1e-12f ? Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            return Vector3.Distance(p, a + (ab * t));
        }
    }

    /// <summary>
    /// The outward face of the field where the probe touches it (field grid frame, unit): from every touched cell, the
    /// directions to its open neighbours (empty, or off the grid), summed. Falls back to "away from the grid's centre"
    /// (the shortest way out of a deep overlap, when the open sides cancel).
    /// </summary>
    private static Vector3 OutwardNormal(VoxelGrid grid, List<(int Slot, int Field, Vector3 At)> touching)
    {
        var (nx, ny, nz) = grid.Dimensions;
        var sum = Vector3.Zero;
        Span<(int X, int Y, int Z)> steps = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];
        foreach (var f in touching.Select(o => o.Field).Distinct())
        {
            var (x, y, z) = grid.Coordinates(f);
            foreach (var (dx, dy, dz) in steps)
            {
                var (ax, ay, az) = (x + dx, y + dy, z + dz);
                var open = ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz || grid.At(ax, ay, az) == 0;
                if (open)
                {
                    sum += new Vector3(dx, dy, dz);
                }
            }
        }

        if (sum.LengthSquared() > 1e-8f)
        {
            return Vector3.Normalize(sum);
        }

        var away = touching.Aggregate(Vector3.Zero, (s, o) => s + o.At);
        return away.LengthSquared() > 1e-12f ? Vector3.Normalize(away) : Vector3.UnitY;
    }
}
