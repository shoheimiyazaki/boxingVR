using System.Numerics;

namespace Aigis.Foundation.Geometry;

/// <summary>
/// Solid voxelisation of a triangle soup. Fills the interior of a closed surface
/// using even-odd ray parity along +Z.
///
/// <para>Input triangles are in <b>voxel index space</b> (the caller transforms
/// world-unit vertices — Aigis's canonical world unit is the <b>metre (SI)</b> —
/// with <c>WorldToVoxel = invert(VoxelToWorld)</c> using the row-vector convention
/// <c>voxel = Vector4.Transform((world,1), WorldToVoxel)</c>. Check
/// <see cref="Matrix4x4.Invert(Matrix4x4, out Matrix4x4)"/>'s <c>bool</c> return —
/// a singular <c>VoxelToWorld</c> produces garbage, not an exception, on the invert
/// side). For each voxel column (x,y) a ray is cast through the voxel centre
/// <c>(x+0.5, y+0.5)</c>; the sorted Z crossings are paired (in/out) and voxels
/// whose centre Z falls inside a pair are marked occupied (255).</para>
///
/// <para>Output layout is <c>x + y*W + z*W*H</c>, matching <see cref="MarchingCubes"/>'s
/// mask input layout.</para>
///
/// <para><b>Assumes a closed, manifold surface.</b> Even-odd parity pairs the
/// sorted Z crossings two at a time; a column with an odd number of crossings
/// (an open mesh, or a residual ray-through-vertex case the XY jitter did not
/// avoid) drops its last unpaired crossing, leaving that column partly unfilled.
/// Marching-cubes output is closed; arbitrary third-party STL is not guaranteed.</para>
/// </summary>
public static class MeshVoxelizer
{
    /// <summary>
    /// Voxelise <paramref name="trianglesVoxel"/> (already in voxel space) into a
    /// W×H×D occupancy grid. Returns a 0/255 byte array of length W*H*D.
    /// </summary>
    public static byte[] SolidVoxelize(IReadOnlyList<MeshTriangle> trianglesVoxel, int w, int h, int d)
    {
        ArgumentNullException.ThrowIfNull(trianglesVoxel);
        if (w <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(w), w, "Grid dimensions must be positive.");
        }

        if (h <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(h), h, "Grid dimensions must be positive.");
        }

        if (d <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(d), d, "Grid dimensions must be positive.");
        }

        var grid = new byte[(long)w * h * d];

        // One reusable crossing buffer per column.
        var crossings = new List<float>(16);

        // Sample each column slightly off the exact voxel centre with distinct
        // x/y offsets. This keeps the vertical ray off shared triangulation
        // edges (e.g. the x==y diagonal of a quad split into two triangles) and
        // off mesh vertices, which would otherwise be double-counted and break
        // even-odd parity. The shift is < 1/1000 of a voxel — negligible for
        // mask occupancy.
        const float ox = 1.0e-4f;
        const float oy = 0.37e-4f;

        for (int y = 0; y < h; y++)
        {
            float cy = y + 0.5f + oy;
            for (int x = 0; x < w; x++)
            {
                float cx = x + 0.5f + ox;
                crossings.Clear();

                for (int t = 0; t < trianglesVoxel.Count; t++)
                {
                    var tri = trianglesVoxel[t];
                    if (RayZHit(tri, cx, cy, out float z))
                    {
                        crossings.Add(z);
                    }
                }

                if (crossings.Count < 2)
                {
                    continue;
                }

                crossings.Sort();

                // Pair consecutive crossings as (enter, exit) spans.
                for (int i = 0; i + 1 < crossings.Count; i += 2)
                {
                    float zEnter = crossings[i];
                    float zExit = crossings[i + 1];

                    // Voxel z is inside when its centre (z+0.5) is within the span.
                    int z0 = (int)MathF.Ceiling(zEnter - 0.5f);
                    int z1 = (int)MathF.Floor(zExit - 0.5f);
                    if (z0 < 0)
                    {
                        z0 = 0;
                    }

                    if (z1 > d - 1)
                    {
                        z1 = d - 1;
                    }

                    for (int z = z0; z <= z1; z++)
                    {
                        grid[((long)z * h + y) * w + x] = 255;
                    }
                }
            }
        }

        return grid;
    }

    /// <summary>
    /// Convenience overload: transform world-unit triangles into voxel space with
    /// <paramref name="worldToVoxel"/> (row-vector convention) then voxelise.
    /// </summary>
    public static byte[] SolidVoxelize(IReadOnlyList<MeshTriangle> trianglesWorld,
                                        Matrix4x4 worldToVoxel, int w, int h, int d)
    {
        ArgumentNullException.ThrowIfNull(trianglesWorld);
        var inVoxel = new MeshTriangle[trianglesWorld.Count];
        for (int i = 0; i < trianglesWorld.Count; i++)
        {
            var tri = trianglesWorld[i];
            inVoxel[i] = new MeshTriangle(
                ToVoxel(tri.V0, worldToVoxel),
                ToVoxel(tri.V1, worldToVoxel),
                ToVoxel(tri.V2, worldToVoxel));
        }

        return SolidVoxelize(inVoxel, w, h, d);
    }

    private static Vector3 ToVoxel(Vector3 world, Matrix4x4 worldToVoxel)
    {
        var v = Vector4.Transform(new Vector4(world, 1f), worldToVoxel);
        return new Vector3(v.X, v.Y, v.Z);
    }

    /// <summary>
    /// Intersect the vertical ray through (px,py) along +Z with the triangle.
    /// Returns the hit Z via barycentric interpolation when (px,py) is inside the
    /// triangle's XY projection. Triangles whose XY projection is degenerate
    /// (parallel to the ray) contribute no crossing and are skipped.
    /// </summary>
    private static bool RayZHit(in MeshTriangle tri, float px, float py, out float z)
    {
        z = 0f;
        float ax = tri.V0.X, ay = tri.V0.Y;
        float bx = tri.V1.X, by = tri.V1.Y;
        float cx = tri.V2.X, cy = tri.V2.Y;

        // Barycentric in the XY plane.
        float det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
        if (MathF.Abs(det) < 1e-9f)
        {
            return false; // degenerate / vertical face
        }

        float inv = 1f / det;
        float l0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) * inv;
        float l1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) * inv;
        float l2 = 1f - l0 - l1;

        // Inside test. Half-open epsilon keeps shared edges from double-counting.
        const float e = -1e-6f;
        if (l0 < e || l1 < e || l2 < e)
        {
            return false;
        }

        z = l0 * tri.V0.Z + l1 * tri.V1.Z + l2 * tri.V2.Z;
        return true;
    }
}
