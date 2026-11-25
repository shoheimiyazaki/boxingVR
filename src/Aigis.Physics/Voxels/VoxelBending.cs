using System.Numerics;
using Aigis.Foundation.Voxels;

namespace Aigis.Physics.Voxels;

/// <summary>Where a voxel body snaps: across slice <paramref name="Slice"/> of <paramref name="Axis"/>, the
/// <paramref name="TakeHigh"/> side breaking off (the smaller piece). <paramref name="Ratio"/> is the bending stress
/// there over the tensile strength (above 1).</summary>
public readonly record struct VoxelBreak(int Axis, int Slice, bool TakeHigh, float Ratio);

/// <summary>
/// A voxel body as a beam: how a blow bends it, and where it snaps. The body is free (nothing holds it but the blow), so
/// the blow both pushes and turns it; every cell's inertia resists that, and the cross-section between the blow and the
/// rest of the body carries the difference as a bending moment. The section's stiffness against bending (its second
/// moment of area) comes straight from the cells in it, so a thin or notched place is where the stress concentrates.
/// Only the body's longest axis is checked (a weapon's length).
/// </summary>
public static class VoxelBending
{
    /// <summary>
    /// The cross-section along the longest axis of <paramref name="grid"/> where a force <paramref name="force"/> [N]
    /// pressed at <paramref name="point"/> (both grid-local) bends it past its materials' tensile strength — the worst one
    /// — or null if it holds everywhere (or a material there never snaps).
    /// </summary>
    /// <param name="grid">The body's cells.</param>
    /// <param name="point">Where the blow presses (grid-local).</param>
    /// <param name="force">The blow's force (grid-local) [N].</param>
    /// <param name="beamAxis">The axis the body runs along as a beam (null: its longest axis).</param>
    /// <param name="loadBearingOnly">
    /// Whether only the cells with a tensile strength carry the section (a limb: the bone carries the bending, the flesh
    /// around it only adds its mass and inertia). Without it, every material must have one (a weapon) or nothing snaps.
    /// </param>
    /// <param name="candidates">
    /// The slices (inclusive) where the body may snap (null: any). The rest of the body still carries the load and gives the inertia;
    /// it only never breaks there (a head: the skull is not where it snaps, the neck is).
    /// </param>
    /// <param name="tieActivation">
    /// How hard the soft cells that pull as ties (muscle: <see cref="VoxelMechanics.ActiveTie"/>) pull, 0..1 (only with
    /// <paramref name="loadBearingOnly"/>). The ties on the stretched side of a section take the bending first, each up to its tie stress
    /// (passive + activation × active) × area × its distance from the section's centre; the load-bearing cells take only what is left
    /// (a jointed column such as a spine gives before its bone is strained, so the flesh around it is what holds it first). A tie
    /// yields, it never snaps the beam.
    /// </param>
    /// <param name="tensileScale">Positive multiplier on the section's tensile limit.</param>
    public static VoxelBreak? FindBreak(VoxelGrid grid, Vector3 point, Vector3 force, int? beamAxis = null, bool loadBearingOnly = false, (int Low, int High)? candidates = null, float tieActivation = 0f, float tensileScale = 1f)
        => Worst(grid, point, force, beamAxis, loadBearingOnly, candidates, tieActivation, tensileScale, minRatio: 1f);

    /// <summary>
    /// The most strained cross-section, as <see cref="FindBreak"/> finds it but whether or not it snaps (<see cref="VoxelBreak.Ratio"/> may be
    /// below 1: how close the blow came to breaking it). Null when nothing carries a section. For diagnostics: the same sums as <see cref="FindBreak"/>,
    /// which returns this result exactly when its ratio is above 1.
    /// </summary>
    public static VoxelBreak? WorstBend(VoxelGrid grid, Vector3 point, Vector3 force, int? beamAxis = null, bool loadBearingOnly = false, (int Low, int High)? candidates = null, float tieActivation = 0f, float tensileScale = 1f)
        => Worst(grid, point, force, beamAxis, loadBearingOnly, candidates, tieActivation, tensileScale, minRatio: 0f);

    // Match b2762da: compile fully up front to avoid the .NET 10 OSR crash in long contact runs.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static VoxelBreak? Worst(VoxelGrid grid, Vector3 point, Vector3 force, int? beamAxis, bool loadBearingOnly, (int Low, int High)? candidates, float tieActivation, float tensileScale, float minRatio)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!float.IsFinite(tensileScale) || tensileScale <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(tensileScale));
        }
        var (nx, ny, nz) = grid.Dimensions;
        var axis = beamAxis ?? (nx >= ny && nx >= nz ? 0 : ny >= nz ? 1 : 2);
        var slices = axis == 0 ? nx : axis == 1 ? ny : nz;
        if (slices < 3 || !(force.LengthSquared() > 0f))
        {
            return null;
        }

        var cached = Geometries.GetOrCreateValue(grid);
        BeamGeometry? geometry;
        lock (cached)
        {
            Validate(cached, grid);
            if (!cached.Entries.TryGetValue((axis, loadBearingOnly), out geometry))
            {
                geometry = BuildGeometry(grid, axis, slices, loadBearingOnly);
                cached.Entries.Add((axis, loadBearingOnly), geometry);
            }
        }
        if (geometry is null)
        {
            return null;
        }

        var (cells, total, com, inverseInertia, bySlice) = geometry;
        var (cellSlice, cellAt, cellMass, cellTensile, cellStiffness) = (geometry.CellSlice, geometry.CellAt, geometry.CellMass, geometry.CellTensile, geometry.CellStiffness);
        // The blow's push and turn on the free body.
        var accel = force / total;
        var spin = Vector3.Transform(Vector3.Cross(point - com, force), inverseInertia);
        var lengthAxis = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
        var cell = grid.CellSize;
        var cellAlong = axis == 0 ? cell.X : axis == 1 ? cell.Y : cell.Z;
        var sectionCellArea = grid.CellVolume / cellAlong;
        var pointSlice = (int)MathF.Floor((Vector3.Dot(point, lengthAxis) + Vector3.Dot(grid.HalfExtents, lengthAxis)) / cellAlong);
        var activation = Math.Clamp(tieActivation, 0f, 1f);

        var sliceLoads = Scratch(ref _sliceLoadsScratch, slices);
        for (var c = 0; c < cellAt.Length; c++)
        {
            var inertial = (accel + Vector3.Cross(spin, cellAt[c] - com)) * cellMass[c];
            sliceLoads[cellSlice[c]] += new SectionLoad(inertial, Vector3.Cross(cellAt[c] - com, inertial));
        }

        var above = Scratch(ref _aboveScratch, slices);
        for (var s = slices - 2; s >= 0; s--)
        {
            above[s] = above[s + 1] + sliceLoads[s + 1];
        }

        var below = sliceLoads[0];
        var lowerCount = bySlice[0].Count;
        VoxelBreak? worst = null;
        for (var s = 1; s < slices - 1; s++)
        {
            var far = pointSlice > s ? above[s] : below;
            below += sliceLoads[s];
            var higherCount = cells.Count - lowerCount - bySlice[s].Count;
            var hasMaterialOnBothSides = lowerCount > 0 && higherCount > 0;
            lowerCount += bySlice[s].Count;
            if (!hasMaterialOnBothSides || (candidates is { } range && (s < range.Low || s > range.High)))
            {
                continue;
            }

            var section = geometry.Sections[s];
            if (section.Length == 0)
            {
                continue; // nothing carries this section (a limb whose bone is already broken here: the caller's joint holds it)
            }

            var high = pointSlice > s;
            var origin = geometry.Origins[s];
            // What the section carries: the blow's moment, less what the far piece's own inertia takes.
            var carried = Vector3.Cross(point - origin, force)
                - (far.Moment - Vector3.Cross(origin - com, far.Force));
            var bending = carried - (lengthAxis * Vector3.Dot(carried, lengthAxis));
            var magnitude = bending.Length();
            if (magnitude <= 1e-9f)
            {
                continue;
            }

            // Stress varies across the section along e (square to the length and to the bending axis).
            var e = Vector3.Normalize(Vector3.Cross(lengthAxis, bending / magnitude));
            var depth = MathF.Sqrt(Square(e.X * cell.X) + Square(e.Y * cell.Y) + Square(e.Z * cell.Z));
            // 引っぱりの紐（筋肉・靭帯・皮膚）: 伸ばされる側（e の向きの側。打撃の点が切れ目より下なら裏返る）のセルが、自分の紐の応力 × 面積 × 中心からの
            // 距離まで曲げを先に受け持つ。骨のセルは残りだけを受ける（関節の連なった柱は骨が歪む前に曲がるので、まず周りの肉が支える）。
            var ties = geometry.Ties[s];
            if (ties.Length > 0)
            {
                var stretched = high ? 1f : -1f;
                var held = 0f;
                foreach (var c in ties)
                {
                    var y = stretched * Vector3.Dot(cellAt[c] - origin, e);
                    if (y > 0f)
                    {
                        held += (geometry.CellPassiveTie[c] + (activation * geometry.CellActiveTie[c])) * sectionCellArea * y;
                    }
                }

                magnitude -= held;
                if (magnitude <= 1e-9f)
                {
                    continue;
                }
            }
            // 合成断面（換算断面法）: 曲げの剛性は各セルのヤング率 E × 断面二次モーメントの和 ΣEI。曲げ応力は面からの距離 y に比例し、
            // セルごとに σ = M·y·E / ΣEI（ヤング率の高い鉄の芯ほど大きく受け持つ）。どのセルも自分の引張強さ（木 60 MPa、鉄 1.5 GPa）を
            // 超えたら折れる: 比 = 最大の σ / 引張。1 種類の材料の断面（骨・木だけ）では E が約分されて従来の M·y/I/引張と同じ。
            float flexural = 0f;
            foreach (var c in section)
            {
                var y = Vector3.Dot(cellAt[c] - origin, e);
                flexural += cellStiffness[c] * sectionCellArea * ((y * y) + (depth * depth / 12f));
            }

            var ratio = 0f;
            foreach (var c in section)
            {
                var y = MathF.Abs(Vector3.Dot(cellAt[c] - origin, e)) + (0.5f * depth);
                ratio = MathF.Max(ratio, magnitude * y * cellStiffness[c] / flexural / (cellTensile[c] * tensileScale));
            }

            if (ratio > minRatio && (worst is null || ratio > worst.Value.Ratio))
            {
                // The smaller piece breaks off.
                var highCount = 0;
                var lowCount = 0;
                for (var t = 0; t < slices; t++)
                {
                    if (t > s)
                    {
                        highCount += bySlice[t].Count;
                    }
                    else if (t < s)
                    {
                        lowCount += bySlice[t].Count;
                    }
                }

                worst = new VoxelBreak(axis, s, highCount <= lowCount, ratio);
            }
        }

        return worst;
    }

    /// <summary>
    /// How stiff the body is against a push along <paramref name="direction"/> (grid-local) in the middle of its longest
    /// axis, as a beam [N/m]: <c>48 E I / L³</c>, with E the mean Young's modulus of its cells, I the mean second moment of
    /// area of its cross-sections about the bending axis, and L its filled length. Null if a material has no stiffness,
    /// or the push runs along the length (no bending).
    /// </summary>
    /// <param name="grid">The body's cells.</param>
    /// <param name="direction">The push (grid-local).</param>
    /// <param name="beamAxis">The axis the body runs along as a beam (null: its longest axis).</param>
    /// <param name="loadBearingOnly">Whether only the cells with a stiffness carry the bending (a limb's bone; see <see cref="FindBreak"/>).</param>
    public static float? BeamStiffness(VoxelGrid grid, Vector3 direction, int? beamAxis = null, bool loadBearingOnly = false)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var (nx, ny, nz) = grid.Dimensions;
        var axis = beamAxis ?? (nx >= ny && nx >= nz ? 0 : ny >= nz ? 1 : 2);
        var slices = axis == 0 ? nx : axis == 1 ? ny : nz;
        var lengthAxis = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
        var across = direction - (lengthAxis * Vector3.Dot(direction, lengthAxis));
        if (across.LengthSquared() < 1e-8f)
        {
            return null;
        }

        var e = Vector3.Normalize(across);
        var cell = grid.CellSize;
        var cellAlong = axis == 0 ? cell.X : axis == 1 ? cell.Y : cell.Z;
        var sectionCellArea = grid.CellVolume / cellAlong;
        var depth = MathF.Sqrt(Square(e.X * cell.X) + Square(e.Y * cell.Y) + Square(e.Z * cell.Z));
        // The cells that carry the stiffness, in cell order (cached until the grid changes: the sums below run in this order, so
        // they give the same floats as walking the grid).
        StiffnessCells? bearing;
        var cached = Geometries.GetOrCreateValue(grid);
        lock (cached)
        {
            Validate(cached, grid);
            if (!cached.Stiffness.TryGetValue((axis, loadBearingOnly), out bearing))
            {
                bearing = BuildStiffnessCells(grid, axis, slices, loadBearingOnly);
                cached.Stiffness.Add((axis, loadBearingOnly), bearing);
            }
        }

        if (bearing is null)
        {
            return null;
        }

        var sumY = ScratchFloats(ref _sumYScratch, slices);
        var sumYY = ScratchFloats(ref _sumYYScratch, slices);
        var (at, sliceOf) = (bearing.At, bearing.Slice);
        for (var i = 0; i < at.Length; i++)
        {
            var along = Vector3.Dot(at[i], e);
            sumY[sliceOf[i]] += along;
            sumYY[sliceOf[i]] += along * along;
        }

        // Each section's second moment about its own centroid: Σ a (y − ȳ)² = a (Σy² − (Σy)²/n), plus each cell's own depth.
        // Averaged over the sections as Enumerable.Average does (summed in a double).
        double momentSum = 0;
        for (var s = 0; s < slices; s++)
        {
            var n = bearing.CountBySlice[s];
            if (n > 0)
            {
                momentSum += sectionCellArea * ((sumYY[s] - (sumY[s] * sumY[s] / n)) + (n * depth * depth / 12f));
            }
        }

        var secondMoment = (float)(momentSum / bearing.Filled);
        var length = bearing.Filled * cellAlong;
        return 48f * (bearing.Modulus / bearing.Count) * secondMoment / (length * length * length);
    }

    private static float Square(float v) => v * v;

    // 梁の断面ごとの荷重の作業用の配列（毎回の割り当てを避ける。1 回の FindBreak の中だけで使い、毎回ゼロから積み直す）。
    [ThreadStatic]
    private static SectionLoad[]? _sliceLoadsScratch;

    [ThreadStatic]
    private static SectionLoad[]? _aboveScratch;

    [ThreadStatic]
    private static float[]? _sumYScratch;

    [ThreadStatic]
    private static float[]? _sumYYScratch;

    private static float[] ScratchFloats(ref float[]? buffer, int length)
    {
        if (buffer is null || buffer.Length < length)
        {
            buffer = new float[length];
        }
        else
        {
            Array.Clear(buffer, 0, length);
        }

        return buffer;
    }

    private static SectionLoad[] Scratch(ref SectionLoad[]? buffer, int length)
    {
        if (buffer is null || buffer.Length < length)
        {
            buffer = new SectionLoad[length];
        }
        else
        {
            Array.Clear(buffer, 0, length);
        }

        return buffer;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VoxelGrid, GeometryCache> Geometries = new();

    private sealed class GeometryCache
    {
        public int Version { get; set; }
        public Dictionary<(int Axis, bool LoadBearingOnly), BeamGeometry?> Entries { get; } = [];
        public Dictionary<(int Axis, bool LoadBearingOnly), StiffnessCells?> Stiffness { get; } = [];
    }

    /// <summary>Drops what was built from an older shape of the grid (call under the cache's lock).</summary>
    private static void Validate(GeometryCache cached, VoxelGrid grid)
    {
        if (cached.Version != grid.GeometryVersion)
        {
            cached.Entries.Clear();
            cached.Stiffness.Clear();
            cached.Version = grid.GeometryVersion;
        }
    }

    /// <summary>The cells that carry the bending stiffness (see <see cref="BeamStiffness"/>): centre and slice in cell order, and the sums that do not depend on the push.</summary>
    private sealed record StiffnessCells(Vector3[] At, int[] Slice, int[] CountBySlice, float Modulus, int Count, int Filled);

    /// <summary>Null if a material has no stiffness (and not only the load-bearing cells count), or no cell carries any.</summary>
    private static StiffnessCells? BuildStiffnessCells(VoxelGrid grid, int axis, int slices, bool loadBearingOnly)
    {
        var at = new List<Vector3>();
        var slice = new List<int>();
        var countBySlice = new int[slices];
        float modulus = 0f;
        var count = 0;
        for (var i = 0; i < grid.CellCount; i++)
        {
            var m = grid.MaterialAt(i);
            if (m == 0)
            {
                continue;
            }

            if (grid.Palette[m].MechanicsOrDefault.Stiffness is not { } stiffness)
            {
                if (loadBearingOnly)
                {
                    continue; // flesh: no part in the bending stiffness
                }

                return null;
            }

            var (x, y, z) = grid.Coordinates(i);
            var s = axis == 0 ? x : axis == 1 ? y : z;
            at.Add(grid.CellCenter(x, y, z));
            slice.Add(s);
            countBySlice[s]++;
            modulus += stiffness;
            count++;
        }

        var filled = countBySlice.Count(n => n > 0);
        return count == 0 || filled == 0 ? null : new StiffnessCells([.. at], [.. slice], countBySlice, modulus, count, filled);
    }

    private sealed record BeamGeometry(List<(int Slice, Vector3 At, float Mass, float Tensile)> Cells,
        float Total, Vector3 Com, Matrix4x4 InverseInertia, List<int>[] BySlice)
    {
        public int[] CellSlice { get; init; } = [];
        public Vector3[] CellAt { get; init; } = [];
        public float[] CellMass { get; init; } = [];
        public float[] CellTensile { get; init; } = [];

        /// <summary>Each cell's Young's modulus [Pa] (the one that has none counts as <see cref="DefaultStiffness"/>: only the ratio between materials matters).</summary>
        public float[] CellStiffness { get; init; } = [];

        /// <summary>The cells that carry each slice (the ones with a tensile strength when only the load-bearing cells count).</summary>
        public int[][] Sections { get; init; } = [];

        /// <summary>Where each slice's carrying cells centre.</summary>
        public Vector3[] Origins { get; init; } = [];

        /// <summary>The soft cells of each slice that pull as ties (<see cref="VoxelMechanics.PassiveTie"/> / <see cref="VoxelMechanics.ActiveTie"/>; only when only the load-bearing cells carry the section).</summary>
        public int[][] Ties { get; init; } = [];

        /// <summary>Each cell's passive tie stress [Pa] (0 for none).</summary>
        public float[] CellPassiveTie { get; init; } = [];

        /// <summary>Each cell's tie stress at full activation, on top of the passive one [Pa] (0 for none).</summary>
        public float[] CellActiveTie { get; init; } = [];
    }

    private const float DefaultStiffness = 12.60e9f; // 段 2（相似の換算）: 材料のヤング率が ×1.26 になるので、比を保つために同じ倍率

    private static BeamGeometry? BuildGeometry(VoxelGrid grid, int axis, int slices, bool loadBearingOnly)
    {
        // The body's mass, centre and inertia from its cells.
        var volume = grid.CellVolume;
        var cells = new List<(int Slice, Vector3 At, float Mass, float Tensile)>(grid.FilledCount);
        var stiffnesses = new List<float>(grid.FilledCount);
        var passiveTies = new List<float>(grid.FilledCount);
        var activeTies = new List<float>(grid.FilledCount);
        float total = 0f;
        var moment = Vector3.Zero;
        for (var i = 0; i < grid.CellCount; i++)
        {
            var m = grid.MaterialAt(i);
            if (m == 0)
            {
                continue;
            }

            var k = grid.Palette[m].MechanicsOrDefault;
            if (k.Tensile is not { } tensile)
            {
                if (!loadBearingOnly)
                {
                    return null;
                }

                tensile = float.NaN; // only mass: it does not carry the section
            }

            var (x, y, z) = grid.Coordinates(i);
            var at = grid.CellCenter(x, y, z);
            var mass = k.Density * volume;
            cells.Add((axis == 0 ? x : axis == 1 ? y : z, at, mass, tensile));
            stiffnesses.Add(k.Stiffness ?? DefaultStiffness);
            var tied = loadBearingOnly && float.IsNaN(tensile); // a tie only where the cell bears no bending of its own
            passiveTies.Add(tied ? MathF.Max(0f, k.PassiveTie ?? 0f) : 0f);
            activeTies.Add(tied ? MathF.Max(0f, k.ActiveTie ?? 0f) : 0f);
            total += mass;
            moment += at * mass;
        }

        if (cells.Count == 0 || total <= 0f || !cells.Exists(c => !float.IsNaN(c.Tensile)))
        {
            return null;
        }

        var com = moment / total;
        var inertia = InertiaAbout(cells, com, grid.CellSize);
        if (!Matrix4x4.Invert(inertia, out var inverseInertia))
        {
            return null;
        }

        var bySlice = new List<int>[slices];
        for (var s = 0; s < slices; s++)
        {
            bySlice[s] = [];
        }

        for (var c = 0; c < cells.Count; c++)
        {
            bySlice[cells[c].Slice].Add(c);
        }

        var sections = new int[slices][];
        var origins = new Vector3[slices];
        var ties = new int[slices][];
        for (var s = 0; s < slices; s++)
        {
            ties[s] = bySlice[s].FindAll(c => passiveTies[c] > 0f || activeTies[c] > 0f).ToArray();
            sections[s] = loadBearingOnly ? bySlice[s].FindAll(c => !float.IsNaN(cells[c].Tensile)).ToArray() : bySlice[s].ToArray();
            // 断面の中心 = 曲げの剛性（E）で重みを付けた重心（合成断面の中立軸の通る所。1 種類の材料ならセルの重心）。
            var weighted = Vector3.Zero;
            var weight = 0f;
            foreach (var c in sections[s])
            {
                weighted += cells[c].At * stiffnesses[c];
                weight += stiffnesses[c];
            }

            origins[s] = weight > 0f ? weighted / weight : Vector3.Zero;
        }

        return new BeamGeometry(cells, total, com, inverseInertia, bySlice)
        {
            CellSlice = cells.Select(c => c.Slice).ToArray(),
            CellAt = cells.Select(c => c.At).ToArray(),
            CellMass = cells.Select(c => c.Mass).ToArray(),
            CellTensile = cells.Select(c => c.Tensile).ToArray(),
            CellStiffness = stiffnesses.ToArray(),
            Sections = sections,
            Origins = origins,
            Ties = ties,
            CellPassiveTie = passiveTies.ToArray(),
            CellActiveTie = activeTies.ToArray(),
        };
    }

    private readonly record struct SectionLoad(Vector3 Force, Vector3 Moment)
    {
        public static SectionLoad operator +(SectionLoad a, SectionLoad b) =>
            new(a.Force + b.Force, a.Moment + b.Moment);
    }

    /// <summary>The inertia tensor [kg·m²] of the cells about <paramref name="com"/> (each cell a small box).</summary>
    private static Matrix4x4 InertiaAbout(List<(int Slice, Vector3 At, float Mass, float Tensile)> cells, Vector3 com, Vector3 cell)
    {
        float xx = 0f, yy = 0f, zz = 0f, xy = 0f, xz = 0f, yz = 0f;
        var own = new Vector3((cell.Y * cell.Y) + (cell.Z * cell.Z), (cell.X * cell.X) + (cell.Z * cell.Z), (cell.X * cell.X) + (cell.Y * cell.Y)) / 12f;
        foreach (var c in cells)
        {
            var r = c.At - com;
            xx += c.Mass * ((r.Y * r.Y) + (r.Z * r.Z) + own.X);
            yy += c.Mass * ((r.X * r.X) + (r.Z * r.Z) + own.Y);
            zz += c.Mass * ((r.X * r.X) + (r.Y * r.Y) + own.Z);
            xy -= c.Mass * r.X * r.Y;
            xz -= c.Mass * r.X * r.Z;
            yz -= c.Mass * r.Y * r.Z;
        }

        return new Matrix4x4(xx, xy, xz, 0f, xy, yy, yz, 0f, xz, yz, zz, 0f, 0f, 0f, 0f, 1f);
    }
}
