using System.Numerics;

namespace Aigis.Foundation.Geometry;

/// <summary>
/// CPU Marching Cubes (Lorensen-Cline, 1987) for mask volume mesh extraction.
///
/// Coordinate convention (CLAUDE.md Hard Rules 3/4):
///   - Voxel index (i,j,k) = (x, y, z) in local volume space.
///   - Output vertices are in world units: world = Vector4.Transform(voxel, VoxelToWorld).
///     Aigis's canonical world unit is the <b>metre (SI)</b> — <see cref="Build"/>'s
///     <c>voxelToWorld</c> must map voxel indices to metres, not e.g. millimetres.
///   - VoxelToWorld uses row-vector convention (System.Numerics standard).
///   - No handedness conversion. All frames are right-handed: <c>voxelToWorld</c> must be
///     orientation-preserving (determinant &gt; 0 — a proper rotation/scale/translation,
///     never a reflection) or the per-vertex normals computed from world-space cross products
///     will point inward instead of outward.
/// </summary>
public static class MarchingCubes
{
    /// <summary>
    /// Triangular mesh in world-space units.
    /// <paramref name="SkippedTriangles"/> is a diagnostic count of triangles the
    /// table-consistency guard dropped during this <see cref="Build"/> call (should
    /// be 0 for a self-consistent triangle table; non-zero signals a table/edge-mask
    /// mismatch worth investigating, not a normal occurrence).
    /// </summary>
    public readonly record struct Mesh(
        Vector3[] Vertices,
        int[] Indices,
        Vector3[] Normals,
        int SkippedTriangles = 0);

    /// <summary>Iso-value: halfway between 0 (outside) and 255 (inside).</summary>
    private const float IsoValue = 127.5f;

    // ── Cube corner offsets in voxel space ──────────────────────────────────
    // Standard Lorensen-Cline corner layout:
    //  v0=(0,0,0), v1=(1,0,0), v2=(1,1,0), v3=(0,1,0)
    //  v4=(0,0,1), v5=(1,0,1), v6=(1,1,1), v7=(0,1,1)
    private static readonly (int dx, int dy, int dz)[] CornerOffsets =
    {
        (0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0),
        (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1),
    };

    // ── Edge vertex pairs (each edge connects two corners) ──────────────────
    private static readonly (int a, int b)[] EdgePairs =
    {
        (0, 1), (1, 2), (2, 3), (3, 0),   // bottom face (z=0)
        (4, 5), (5, 6), (6, 7), (7, 4),   // top face (z=1)
        (0, 4), (1, 5), (2, 6), (3, 7),   // vertical edges
    };

    /// <summary>
    /// Builds a triangular surface mesh from a binary mask volume.
    /// </summary>
    /// <param name="mask">
    /// Flat array of bytes, length = width*height*depth.
    /// Index = x + y*width + z*width*height.
    /// Non-zero = inside, 0 = outside.
    /// </param>
    /// <param name="width">Volume width (X).</param>
    /// <param name="height">Volume height (Y).</param>
    /// <param name="depth">Volume depth (Z).</param>
    /// <param name="voxelToWorld">
    /// Transforms integer voxel coordinates to world units.
    /// Row-vector convention: world = Vector4.Transform(voxelPos4, voxelToWorld).
    /// </param>
    /// <returns>Mesh with world-space vertices and per-vertex averaged normals.</returns>
    public static Mesh Build(
        byte[] mask,
        int width,
        int height,
        int depth,
        Matrix4x4 voxelToWorld)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (mask.Length != width * height * depth)
        {
            throw new ArgumentException(
                $"Mask length {mask.Length} != {width}*{height}*{depth}={width * height * depth}.");
        }

        var vertices = new List<Vector3>(4096);
        var indices = new List<int>(8192);
        int skipped = 0;

        // Allocate per-cube working buffers outside the loop to avoid CA2014.
        float[] cv = new float[8];
        Vector3[] edgeVerts = new Vector3[12];

        // Marching-cubes: iterate over all unit cubes.
        // A cube at (x, y, z) has its 8 corners at the CornerOffset positions.
        // We stop one short of each axis edge so corner (x+1, y+1, z+1) stays valid.
        for (int z = 0; z < depth - 1; z++)
        {
            for (int y = 0; y < height - 1; y++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    BuildCube(mask, width, height, x, y, z, voxelToWorld, cv, edgeVerts, vertices, indices, ref skipped);
                }
            }
        }

        if (vertices.Count == 0)
        {
            return new Mesh(Array.Empty<Vector3>(), Array.Empty<int>(), Array.Empty<Vector3>(), skipped);
        }

        Vector3[] vArray = vertices.ToArray();
        int[] iArray = indices.ToArray();
        Vector3[] normals = ComputeNormals(vArray, iArray);

        return new Mesh(vArray, iArray, normals, skipped);
    }

    private static void BuildCube(
        byte[] mask,
        int width,
        int height,
        int x,
        int y,
        int z,
        Matrix4x4 voxelToWorld,
        float[] cv,
        Vector3[] edgeVerts,
        List<Vector3> vertices,
        List<int> indices,
        ref int skipped)
    {
        // Sample corner values.
        for (int c = 0; c < 8; c++)
        {
            var (dx, dy, dz) = CornerOffsets[c];
            int idx = (x + dx) + (y + dy) * width + (z + dz) * width * height;
            cv[c] = mask[idx];
        }

        // Build cube index (8-bit: bit c set if corner c is INSIDE, value >= iso).
        // This matches the canonical Lorensen-Cline / Bourke TriTable convention
        // (bit set = inside) used by MarchingCubesTables.
        int cubeIndex = 0;
        for (int c = 0; c < 8; c++)
        {
            if (cv[c] >= IsoValue)
            {
                cubeIndex |= 1 << c;
            }
        }

        // Derive the cut-edge mask directly from the triangle table row, which is
        // self-consistent by construction (a standalone EdgeTable can drift out of
        // sync with TriTable and silently drop triangles via the guard below).
        int[] triRow = MarchingCubesTables.TriTable[cubeIndex];
        int edgeMask = 0;
        for (int ti = 0; ti < triRow.Length; ti++)
        {
            int e = triRow[ti];
            if (e < 0)
            {
                break;
            }

            edgeMask |= 1 << e;
        }

        if (edgeMask == 0)
        {
            return;
        }

        // Clear stale edge vertices from the previous cube iteration.
        Array.Clear(edgeVerts, 0, 12);

        // Compute the interpolated vertex position on each cut edge.
        for (int e = 0; e < 12; e++)
        {
            if ((edgeMask & (1 << e)) == 0)
            {
                continue;
            }

            var (ca, cb) = EdgePairs[e];
            float va = cv[ca], vb = cv[cb];
            float t = (va == vb) ? 0.5f : (IsoValue - va) / (vb - va);
            t = Math.Clamp(t, 0f, 1f);

            var (dxa, dya, dza) = CornerOffsets[ca];
            var (dxb, dyb, dzb) = CornerOffsets[cb];
            var posA = new Vector3(x + dxa, y + dya, z + dza);
            var posB = new Vector3(x + dxb, y + dyb, z + dzb);
            var posVoxel = Vector3.Lerp(posA, posB, t);

            // Transform to world space (row-vector: world = pos * M).
            var w = Vector4.Transform(new Vector4(posVoxel, 1f), voxelToWorld);
            edgeVerts[e] = new Vector3(w.X, w.Y, w.Z);
        }

        // Emit triangles from the triangle table.
        // Loop bound `triRow.Length - 2` safely handles rows shorter than
        // 3 (degenerates to no iterations); the body's `triRow[ti] < 0`
        // check is the canonical terminator for variable-length rows.
        for (int ti = 0; ti + 2 < triRow.Length; ti += 3)
        {
            if (triRow[ti] < 0)
            {
                break;
            }

            int e0 = triRow[ti];
            int e1 = triRow[ti + 1];
            int e2 = triRow[ti + 2];

            // Skip degenerate or table-inconsistent triangles.
            if ((edgeMask & (1 << e0)) == 0)
            {
                skipped++;
                continue;
            }

            if ((edgeMask & (1 << e1)) == 0)
            {
                skipped++;
                continue;
            }

            if ((edgeMask & (1 << e2)) == 0)
            {
                skipped++;
                continue;
            }

            int baseIdx = vertices.Count;
            vertices.Add(edgeVerts[e0]);
            vertices.Add(edgeVerts[e1]);
            vertices.Add(edgeVerts[e2]);
            indices.Add(baseIdx);
            indices.Add(baseIdx + 1);
            indices.Add(baseIdx + 2);
        }
    }

    // ── Normals: average of incident face normals per vertex ─────────────────
    private static Vector3[] ComputeNormals(Vector3[] vertices, int[] indices)
    {
        var normals = new Vector3[vertices.Length];
        int triCount = indices.Length / 3;

        for (int t = 0; t < triCount; t++)
        {
            int i0 = indices[t * 3];
            int i1 = indices[t * 3 + 1];
            int i2 = indices[t * 3 + 2];

            Vector3 ab = vertices[i1] - vertices[i0];
            Vector3 ac = vertices[i2] - vertices[i0];
            // Right-handed cross product; points outward only under the class doc's
            // preconditions (canonical TriTable winding + det(voxelToWorld) > 0).
            Vector3 fn = Vector3.Cross(ab, ac);

            normals[i0] += fn;
            normals[i1] += fn;
            normals[i2] += fn;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            float len = normals[i].Length();
            if (len > 1e-6f)
            {
                normals[i] /= len;
            }
            else
            {
                normals[i] = Vector3.UnitZ;
            }
        }

        return normals;
    }
}
