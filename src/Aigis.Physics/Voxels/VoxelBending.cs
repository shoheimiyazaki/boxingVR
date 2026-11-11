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
    public static VoxelBreak? FindBreak(VoxelGrid grid, Vector3 point, Vector3 force)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var (nx, ny, nz) = grid.Dimensions;
        var axis = nx >= ny && nx >= nz ? 0 : ny >= nz ? 1 : 2;
        var slices = axis == 0 ? nx : axis == 1 ? ny : nz;
        if (slices < 3 || !(force.LengthSquared() > 0f))
        {
            return null;
        }

        // The body's mass, centre and inertia from its cells.
        var volume = grid.CellVolume;
        var cells = new List<(int Slice, Vector3 At, float Mass, float Tensile)>(grid.FilledCount);
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
                return null;
            }

            var (x, y, z) = grid.Coordinates(i);
            var at = grid.CellCenter(x, y, z);
            var mass = k.Density * volume;
            cells.Add((axis == 0 ? x : axis == 1 ? y : z, at, mass, tensile));
            total += mass;
            moment += at * mass;
        }

        if (cells.Count == 0 || total <= 0f)
        {
            return null;
        }

        var com = moment / total;
        var inertia = InertiaAbout(cells, com, grid.CellSize);
        if (!Matrix4x4.Invert(inertia, out var inverseInertia))
        {
            return null;
        }

        // The blow's push and turn on the free body.
        var accel = force / total;
        var spin = Vector3.Transform(Vector3.Cross(point - com, force), inverseInertia);
        var lengthAxis = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
        var cell = grid.CellSize;
        var cellAlong = axis == 0 ? cell.X : axis == 1 ? cell.Y : cell.Z;
        var sectionCellArea = grid.CellVolume / cellAlong;
        var pointSlice = (int)MathF.Floor((Vector3.Dot(point, lengthAxis) + Vector3.Dot(grid.HalfExtents, lengthAxis)) / cellAlong);

        var bySlice = new List<int>[slices];
        for (var s = 0; s < slices; s++)
        {
            bySlice[s] = [];
        }

        for (var c = 0; c < cells.Count; c++)
        {
            bySlice[cells[c].Slice].Add(c);
        }

        VoxelBreak? worst = null;
        for (var s = 1; s < slices - 1; s++)
        {
            var section = bySlice[s];
            if (section.Count == 0)
            {
                continue;
            }

            // The far piece: the side the blow lands on. It needs material on both sides to be a break.
            var high = pointSlice > s;
            int farCount = 0, nearCount = 0;
            for (var c = 0; c < cells.Count; c++)
            {
                if (cells[c].Slice == s)
                {
                    continue;
                }

                if ((cells[c].Slice > s) == high)
                {
                    farCount++;
                }
                else
                {
                    nearCount++;
                }
            }

            if (farCount == 0 || nearCount == 0)
            {
                continue;
            }

            var origin = section.Aggregate(Vector3.Zero, (sum, c) => sum + cells[c].At) / section.Count;
            // What the section carries: the blow's moment, less what the far piece's own inertia takes.
            var carried = Vector3.Cross(point - origin, force);
            foreach (var c in cells.Where(c => c.Slice != s && (c.Slice > s) == high))
            {
                var r = c.At - origin;
                var a = accel + Vector3.Cross(spin, c.At - com);
                carried -= Vector3.Cross(r, a * c.Mass);
            }

            var bending = carried - (lengthAxis * Vector3.Dot(carried, lengthAxis));
            var magnitude = bending.Length();
            if (magnitude <= 1e-9f)
            {
                continue;
            }

            // Stress varies across the section along e (square to the length and to the bending axis).
            var e = Vector3.Normalize(Vector3.Cross(lengthAxis, bending / magnitude));
            var depth = MathF.Sqrt(Square(e.X * cell.X) + Square(e.Y * cell.Y) + Square(e.Z * cell.Z));
            float secondMoment = 0f, reach = 0f, tensileMin = float.MaxValue;
            foreach (var c in section)
            {
                var y = Vector3.Dot(cells[c].At - origin, e);
                secondMoment += sectionCellArea * ((y * y) + (depth * depth / 12f));
                reach = MathF.Max(reach, MathF.Abs(y) + (0.5f * depth));
                tensileMin = MathF.Min(tensileMin, cells[c].Tensile);
            }

            var ratio = magnitude * reach / secondMoment / tensileMin;
            if (ratio > 1f && (worst is null || ratio > worst.Value.Ratio))
            {
                // The smaller piece breaks off.
                var highCount = cells.Count(c => c.Slice > s);
                var lowCount = cells.Count(c => c.Slice < s);
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
    public static float? BeamStiffness(VoxelGrid grid, Vector3 direction)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var (nx, ny, nz) = grid.Dimensions;
        var axis = nx >= ny && nx >= nz ? 0 : ny >= nz ? 1 : 2;
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
        var sum = new (float Y, float YY, int N)[slices];
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
                return null;
            }

            var (x, y, z) = grid.Coordinates(i);
            var s = axis == 0 ? x : axis == 1 ? y : z;
            var along = Vector3.Dot(grid.CellCenter(x, y, z), e);
            sum[s] = (sum[s].Y + along, sum[s].YY + (along * along), sum[s].N + 1);
            modulus += stiffness;
            count++;
        }

        var filled = sum.Count(s => s.N > 0);
        if (count == 0 || filled == 0)
        {
            return null;
        }

        // Each section's second moment about its own centroid: Σ a (y − ȳ)² = a (Σy² − (Σy)²/n), plus each cell's own depth.
        var secondMoment = sum.Where(s => s.N > 0)
            .Average(s => sectionCellArea * ((s.YY - (s.Y * s.Y / s.N)) + (s.N * depth * depth / 12f)));
        var length = filled * cellAlong;
        return 48f * (modulus / count) * secondMoment / (length * length * length);
    }

    private static float Square(float v) => v * v;

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
