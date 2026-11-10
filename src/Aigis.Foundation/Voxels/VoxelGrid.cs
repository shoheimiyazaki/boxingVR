using System.Numerics;

namespace Aigis.Foundation.Voxels;

/// <summary>What a voxel is made of, as far as the engine's damage rules care.</summary>
public enum VoxelTissue : byte
{
    /// <summary>Nothing (carved away, or never there).</summary>
    Empty = 0,

    /// <summary>Soft tissue: skin and muscle. Cheap to carve; how much is left is how strong a part still is.</summary>
    Flesh = 1,

    /// <summary>Structural tissue. Tough to carve; a part whose bone no longer spans it falls apart there.</summary>
    Bone = 2,

    /// <summary>An organ. Destroying enough of a <see cref="VoxelMaterial.Vital"/> one kills.</summary>
    Organ = 3,
}

/// <summary>
/// One material a <see cref="VoxelGrid"/> cell can hold. The engine only reads <see cref="Tissue"/>,
/// <see cref="Toughness"/> and <see cref="Vital"/>; the colour is for whoever draws it.
/// </summary>
/// <param name="Name">Human-readable name (logs, tests).</param>
/// <param name="Tissue">How the damage rules treat it.</param>
/// <param name="Toughness">Damage needed to carve one voxel away (flesh ≈ 1). Partial damage is kept per voxel.</param>
/// <param name="Vital">Losing enough of this material kills (brain, heart).</param>
/// <param name="Color">RGBA when the voxel is exposed inside the body.</param>
/// <param name="SurfaceColor">RGBA when the voxel is still the body's original outer surface (skin).</param>
public sealed record VoxelMaterial(string Name, VoxelTissue Tissue, float Toughness, bool Vital, Vector4 Color, Vector4 SurfaceColor);

/// <summary>A material table: index 0 is always <see cref="VoxelTissue.Empty"/>; a grid cell stores an index.</summary>
public sealed class VoxelPalette
{
    private readonly VoxelMaterial[] _materials;

    public VoxelPalette(IReadOnlyList<VoxelMaterial> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        if (materials.Count == 0 || materials[0].Tissue != VoxelTissue.Empty)
        {
            throw new ArgumentException("Material 0 must be the empty material.", nameof(materials));
        }

        if (materials.Count > 256)
        {
            throw new ArgumentException("At most 256 materials (a cell is one byte).", nameof(materials));
        }

        foreach (var m in materials.Skip(1))
        {
            if (m.Tissue == VoxelTissue.Empty || !(m.Toughness > 0f) || !float.IsFinite(m.Toughness))
            {
                throw new ArgumentException($"Material '{m.Name}' needs a non-empty tissue and a positive toughness.", nameof(materials));
            }
        }

        _materials = [.. materials];
    }

    public int Count => _materials.Length;

    public VoxelMaterial this[byte index] => _materials[index];
}

/// <summary>One voxel taken away by <see cref="VoxelGrid.Carve"/>: where it was (grid-local, metres) and what it was.</summary>
public readonly record struct RemovedVoxel(Vector3 LocalCenter, byte Material, bool WasSurface);

/// <summary>
/// A box filled with voxels (pure data, no physics): the inside of one rigid part. The grid exactly fills a box of
/// <see cref="HalfExtents"/> centred on the grid's own origin; cell <c>(x, y, z)</c> spans
/// <c>-HalfExtents + (x, y, z) * CellSize</c> to the next corner.
/// <para>
/// Damage carves cells away (<see cref="Carve"/>). Each cell keeps its remaining health so tough tissue (bone) gives way
/// over several blows. The grid tracks what is left per material, and whether its bone still spans it along an axis
/// (<see cref="FindBoneBreak"/>), which is what tells a caller a limb has been broken through.
/// </para>
/// Deterministic: no randomness, and every iteration is in index order.
/// </summary>
public sealed class VoxelGrid
{
    private readonly byte[] _cells;
    private readonly float[] _health;
    private readonly bool[] _surface;
    private readonly float[] _stain;
    private readonly int[] _remaining;
    private readonly int[] _initial;

    public VoxelGrid(VoxelPalette palette, Vector3 halfExtents, float targetCellSize, Func<Vector3, Vector3, byte> fill)
        : this(palette, halfExtents, CellsFor(halfExtents, targetCellSize), fill)
    {
    }

    /// <param name="palette">The materials cells refer to.</param>
    /// <param name="halfExtents">Half the box's size [m].</param>
    /// <param name="dimensions">Cells along x, y, z (each ≥ 1).</param>
    /// <param name="fill">Material of the cell whose centre is at the first argument (grid-local, metres); the second
    /// argument is the centre in normalised box coordinates (−1..1 on each axis).</param>
    public VoxelGrid(VoxelPalette palette, Vector3 halfExtents, (int X, int Y, int Z) dimensions, Func<Vector3, Vector3, byte> fill)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(fill);
        if (!(halfExtents.X > 0f && halfExtents.Y > 0f && halfExtents.Z > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(halfExtents), halfExtents, "Half extents must be positive.");
        }

        if (dimensions.X < 1 || dimensions.Y < 1 || dimensions.Z < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "Every dimension needs at least one cell.");
        }

        Palette = palette;
        HalfExtents = halfExtents;
        Dimensions = dimensions;
        CellSize = 2f * halfExtents / new Vector3(dimensions.X, dimensions.Y, dimensions.Z);
        var count = dimensions.X * dimensions.Y * dimensions.Z;
        _cells = new byte[count];
        _health = new float[count];
        _surface = new bool[count];
        _stain = new float[count];
        _remaining = new int[palette.Count];
        _initial = new int[palette.Count];
        for (var z = 0; z < dimensions.Z; z++)
        {
            for (var y = 0; y < dimensions.Y; y++)
            {
                for (var x = 0; x < dimensions.X; x++)
                {
                    var i = Index(x, y, z);
                    var center = CellCenter(x, y, z);
                    var material = fill(center, center / halfExtents);
                    if (material >= palette.Count)
                    {
                        throw new ArgumentException($"Fill returned material {material}, but the palette has {palette.Count}.", nameof(fill));
                    }

                    _cells[i] = material;
                    _health[i] = material == 0 ? 0f : palette[material].Toughness;
                    _remaining[material]++;
                }
            }
        }

        // The original outer surface (skin): every filled cell that can be seen before any damage — on the box's
        // boundary, or next to a cell the fill left empty (a body shaped by a mesh inside its box).
        for (var i = 0; i < count; i++)
        {
            var (x, y, z) = Coordinates(i);
            _surface[i] = IsExposed(x, y, z);
        }

        _remaining[0] = 0;
        Array.Copy(_remaining, _initial, _remaining.Length);
    }

    private VoxelGrid(VoxelPalette palette, Vector3 halfExtents, (int X, int Y, int Z) dimensions)
    {
        Palette = palette;
        HalfExtents = halfExtents;
        Dimensions = dimensions;
        CellSize = 2f * halfExtents / new Vector3(dimensions.X, dimensions.Y, dimensions.Z);
        var count = dimensions.X * dimensions.Y * dimensions.Z;
        _cells = new byte[count];
        _health = new float[count];
        _surface = new bool[count];
        _stain = new float[count];
        _remaining = new int[palette.Count];
        _initial = new int[palette.Count];
    }

    public VoxelPalette Palette { get; }

    public Vector3 HalfExtents { get; }

    public (int X, int Y, int Z) Dimensions { get; }

    public Vector3 CellSize { get; }

    /// <summary>Bumped on every change, so a renderer can cache what it built from the grid.</summary>
    public int Version { get; private set; }

    public int CellCount => _cells.Length;

    /// <summary>Cells of this material left now.</summary>
    public int Remaining(byte material) => _remaining[material];

    /// <summary>Cells of this material the grid started with (after a split, what the piece started with).</summary>
    public int Initial(byte material) => _initial[material];

    /// <summary>Fraction of a tissue left, over every material of that tissue (1 if the grid never had any).</summary>
    public float Fraction(VoxelTissue tissue)
    {
        int left = 0, start = 0;
        for (var m = 1; m < Palette.Count; m++)
        {
            if (Palette[(byte)m].Tissue == tissue)
            {
                left += _remaining[m];
                start += _initial[m];
            }
        }

        return start == 0 ? 1f : (float)left / start;
    }

    /// <summary>Fraction of one material left (1 if the grid never had any).</summary>
    public float Fraction(byte material) => _initial[material] == 0 ? 1f : (float)_remaining[material] / _initial[material];

    public int Index(int x, int y, int z) => x + (Dimensions.X * (y + (Dimensions.Y * z)));

    public byte At(int x, int y, int z) => _cells[Index(x, y, z)];

    /// <summary>Whether the cell is still part of the original outer surface (skin) — drawn in its surface colour.</summary>
    public bool IsOriginalSurface(int x, int y, int z) => _surface[Index(x, y, z)];

    /// <summary>How much the cell is stained by the blows around it (0 = clean … 1 = soaked). For drawing (blood, scorch).</summary>
    public float Stain(int x, int y, int z) => _stain[Index(x, y, z)];

    public Vector3 CellCenter(int x, int y, int z) => -HalfExtents + (new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * CellSize);

    /// <summary>Whether a filled cell can be seen: on the grid's boundary, or next to an empty cell.</summary>
    public bool IsExposed(int x, int y, int z)
    {
        if (At(x, y, z) == 0)
        {
            return false;
        }

        var (nx, ny, nz) = Dimensions;
        return x == 0 || y == 0 || z == 0 || x == nx - 1 || y == ny - 1 || z == nz - 1
            || At(x - 1, y, z) == 0 || At(x + 1, y, z) == 0
            || At(x, y - 1, z) == 0 || At(x, y + 1, z) == 0
            || At(x, y, z - 1) == 0 || At(x, y, z + 1) == 0;
    }

    /// <summary>
    /// Carves a blow into the grid. The blow lands at <paramref name="localPoint"/> heading along
    /// <paramref name="localDirection"/> and carries <paramref name="energy"/> units of damage (one unit carves one
    /// flesh voxel). Cells are hit nearest-first along a short drill line from the contact point inward, so a blow
    /// digs a crater through skin before it reaches bone, and bone's toughness stops a weak blow at the bone.
    /// </summary>
    /// <param name="localPoint">Where the blow landed (grid-local, metres).</param>
    /// <param name="localDirection">Direction the blow travels (grid-local; normalised here).</param>
    /// <param name="energy">Damage to spend.</param>
    /// <param name="removed">Receives every cell carved away.</param>
    /// <returns>Damage actually spent (less than <paramref name="energy"/> only if the reachable cells ran out).</returns>
    public float Carve(Vector3 localPoint, Vector3 localDirection, float energy, ICollection<RemovedVoxel> removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        if (!(energy > 0f) || !float.IsFinite(energy))
        {
            return 0f;
        }

        var dir = localDirection.LengthSquared() > 1e-12f ? Vector3.Normalize(localDirection) : -Vector3.Normalize(localPoint + new Vector3(1e-6f));
        var cell = MathF.Max(CellSize.X, MathF.Max(CellSize.Y, CellSize.Z));
        // Reach grows with the energy: a light touch scuffs, a heavy blow digs (about the cube root of the cells it pays for).
        var reach = cell * (1.5f + (1.2f * MathF.Cbrt(energy)));
        var start = Vector3.Clamp(localPoint, -HalfExtents, HalfExtents);
        var end = start + (dir * reach);
        var candidates = new List<(float Score, int Index)>();
        ForEachCellNear(start, end, reach, (i, center) =>
        {
            var along = Math.Clamp(Vector3.Dot(center - start, dir), 0f, reach);
            var off = Vector3.Distance(center, start + (dir * along));
            if (off > reach * 0.75f)
            {
                return;
            }

            // Nearest to the drill line first, and shallower before deeper.
            candidates.Add((off + (0.6f * along), i));
        });
        candidates.Sort((a, b) => a.Score != b.Score ? a.Score.CompareTo(b.Score) : a.Index.CompareTo(b.Index));

        var spent = 0f;
        foreach (var (_, i) in candidates)
        {
            if (energy - spent <= 1e-4f)
            {
                break;
            }

            var hit = MathF.Min(_health[i], energy - spent);
            _health[i] -= hit;
            spent += hit;
            if (_health[i] <= 1e-4f)
            {
                var (x, y, z) = Coordinates(i);
                removed.Add(new RemovedVoxel(CellCenter(x, y, z), _cells[i], _surface[i]));
                Clear(i);
            }
        }

        // The cells around the crater are stained (a blow leaves more mark than the hole it digs): strongest at the
        // contact, fading out to about twice the crater's reach.
        var stainReach = reach * 2f;
        ForEachCellNear(start, start, stainReach, (i, center) =>
        {
            var d = Vector3.Distance(center, start);
            if (d < stainReach)
            {
                _stain[i] = MathF.Min(1f, _stain[i] + (0.7f * (1f - (d / stainReach))));
            }
        });

        Version++;
        return spent;
    }

    /// <summary>
    /// Along <paramref name="axis"/> (0 = x, 1 = y, 2 = z), the first slice (counted from the <paramref name="fromHigh"/>
    /// end) that has no bone left while both of its sides still do — i.e. where the bone has been broken through.
    /// Null if the bone still spans the grid, or the grid never had bone along that axis.
    /// </summary>
    public int? FindBoneBreak(int axis, bool fromHigh)
    {
        var slices = Slices(axis);
        var bone = new int[slices];
        var any = false;
        for (var i = 0; i < _cells.Length; i++)
        {
            if (_cells[i] != 0 && Palette[_cells[i]].Tissue == VoxelTissue.Bone)
            {
                var c = Coordinates(i);
                bone[axis == 0 ? c.X : axis == 1 ? c.Y : c.Z]++;
                any = true;
            }
        }

        if (!any)
        {
            return null;
        }

        int first = -1, last = -1;
        for (var s = 0; s < slices; s++)
        {
            if (bone[s] > 0)
            {
                first = first < 0 ? s : first;
                last = s;
            }
        }

        if (fromHigh)
        {
            for (var s = last; s > first; s--)
            {
                if (bone[s] == 0)
                {
                    return s;
                }
            }
        }
        else
        {
            for (var s = first; s < last; s++)
            {
                if (bone[s] == 0)
                {
                    return s;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Cuts the grid at slice <paramref name="slice"/> of <paramref name="axis"/>: every cell on the
    /// <paramref name="takeHigh"/> side of the slice (the slice itself excluded) moves into a new grid, which is returned
    /// with the offset of its centre from this grid's centre. The cells left behind are unchanged; this grid keeps its
    /// size (the moved cells are simply empty now). Null if nothing would move.
    /// </summary>
    public (VoxelGrid Piece, Vector3 CenterOffset)? Split(int axis, int slice, bool takeHigh)
    {
        var slices = Slices(axis);
        var from = takeHigh ? slice + 1 : 0;
        var to = takeHigh ? slices : slice; // exclusive
        if (to - from <= 0)
        {
            return null;
        }

        var dims = axis switch
        {
            0 => (to - from, Dimensions.Y, Dimensions.Z),
            1 => (Dimensions.X, to - from, Dimensions.Z),
            _ => (Dimensions.X, Dimensions.Y, to - from),
        };
        var half = HalfExtents;
        var axisHalf = 0.5f * (to - from) * Component(CellSize, axis);
        half = axis == 0 ? half with { X = axisHalf } : axis == 1 ? half with { Y = axisHalf } : half with { Z = axisHalf };
        var piece = new VoxelGrid(Palette, half, dims);
        var moved = 0;
        for (var i = 0; i < _cells.Length; i++)
        {
            var (x, y, z) = Coordinates(i);
            var s = axis == 0 ? x : axis == 1 ? y : z;
            if (s < from || s >= to)
            {
                continue;
            }

            var j = piece.Index(axis == 0 ? x - from : x, axis == 1 ? y - from : y, axis == 2 ? z - from : z);
            var m = _cells[i];
            piece._cells[j] = m;
            piece._health[j] = _health[i];
            piece._surface[j] = _surface[i];
            piece._stain[j] = _stain[i];
            piece._remaining[m] += m == 0 ? 0 : 1;
            piece._initial[m] += m == 0 ? 0 : 1;
            if (m != 0)
            {
                moved++;
                Clear(i);
            }
        }

        if (moved == 0)
        {
            return null;
        }

        Version++;
        var centerAxis = -Component(HalfExtents, axis) + (0.5f * (from + to) * Component(CellSize, axis));
        var offset = axis == 0 ? new Vector3(centerAxis, 0f, 0f) : axis == 1 ? new Vector3(0f, centerAxis, 0f) : new Vector3(0f, 0f, centerAxis);
        return (piece, offset);
    }

    /// <summary>The box (grid-local) around every filled cell, or null if the grid is empty.</summary>
    public (Vector3 Center, Vector3 HalfExtents)? OccupiedBounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < _cells.Length; i++)
        {
            if (_cells[i] == 0)
            {
                continue;
            }

            var (x, y, z) = Coordinates(i);
            var c = CellCenter(x, y, z);
            min = Vector3.Min(min, c - (CellSize * 0.5f));
            max = Vector3.Max(max, c + (CellSize * 0.5f));
        }

        return min.X > max.X ? null : ((min + max) * 0.5f, (max - min) * 0.5f);
    }

    /// <summary>Filled cells in total.</summary>
    public int FilledCount => _remaining.Sum();

    public (int X, int Y, int Z) Coordinates(int index)
    {
        var x = index % Dimensions.X;
        var rest = index / Dimensions.X;
        return (x, rest % Dimensions.Y, rest / Dimensions.Y);
    }

    private void Clear(int i)
    {
        _remaining[_cells[i]]--;
        _cells[i] = 0;
        _health[i] = 0f;
        _surface[i] = false;
        _stain[i] = 0f;
    }

    private void ForEachCellNear(Vector3 a, Vector3 b, float radius, Action<int, Vector3> visit)
    {
        var lo = Vector3.Min(a, b) - new Vector3(radius);
        var hi = Vector3.Max(a, b) + new Vector3(radius);
        var (x0, y0, z0) = CellOf(lo);
        var (x1, y1, z1) = CellOf(hi);
        for (var z = z0; z <= z1; z++)
        {
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var i = Index(x, y, z);
                    if (_cells[i] != 0)
                    {
                        visit(i, CellCenter(x, y, z));
                    }
                }
            }
        }
    }

    private (int X, int Y, int Z) CellOf(Vector3 local)
    {
        var f = (local + HalfExtents) / CellSize;
        return (Math.Clamp((int)MathF.Floor(f.X), 0, Dimensions.X - 1),
            Math.Clamp((int)MathF.Floor(f.Y), 0, Dimensions.Y - 1),
            Math.Clamp((int)MathF.Floor(f.Z), 0, Dimensions.Z - 1));
    }

    private int Slices(int axis) => axis switch
    {
        0 => Dimensions.X,
        1 => Dimensions.Y,
        2 => Dimensions.Z,
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Axis is 0, 1 or 2."),
    };

    private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>How many cells a box of <paramref name="halfExtents"/> gets for a target cell size (what the
    /// cell-size constructor uses).</summary>
    public static (int X, int Y, int Z) CellsFor(Vector3 halfExtents, float cellSize)
    {
        if (!(cellSize > 0f) || !float.IsFinite(cellSize))
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
        }

        static int N(float half, float size) => Math.Max(1, (int)MathF.Round(2f * half / size));
        return (N(halfExtents.X, cellSize), N(halfExtents.Y, cellSize), N(halfExtents.Z, cellSize));
    }
}
