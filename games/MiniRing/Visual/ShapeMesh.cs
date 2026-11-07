using System.Numerics;
using Aigis.Render;

namespace MiniRing.Visual;

/// <summary>ロフトの 1 断面: 軸方向のある位置での半幅・半奥行き・角の丸さ・中心のずれ。</summary>
/// <param name="HalfWidth">断面の横（ローカルの「幅」方向）の半径。</param>
/// <param name="HalfDepth">断面の奥行き方向の半径。</param>
/// <param name="Squareness">超楕円の指数 n（2 = 楕円、大きいほど角ばる）。</param>
/// <param name="Offset">断面中心のずれ（幅方向, 奥行き方向）。</param>
public readonly record struct Ring(float HalfWidth, float HalfDepth, float Squareness = 2f, Vector2 Offset = default);

/// <summary>
/// 見た目用のメッシュの手続き生成（Aigis <see cref="MeshData"/>: 位置＋法線・インデックス無し）。
/// 断面（超楕円）を軸に沿って並べる「ロフト」1 種類で、頭・胴・腕・脚・グローブ・靴を作る。
/// 両端は断面を 0 まで細らせて閉じるので、端の形（丸い・平ら）も断面の並びだけで決まる。
/// </summary>
public static class ShapeMesh
{
    /// <summary>
    /// <paramref name="from"/> から <paramref name="to"/> へ伸びるロフト。<paramref name="widthAxis"/> は断面の「幅」の向き
    /// （軸に直交化して使う）。<paramref name="profile"/> は 0..1 の位置 → 断面。
    /// 断面の奥行き方向（<see cref="Ring.Offset"/> の Y が正の向き）は depthHint に揃える。省略時は +Z（体の前）、
    /// 軸が Z に沿うときは +Y。軸の向き（上向き・下向き・左右）で奥行きが反転しないようにするため。
    /// </summary>
    public static List<Vector3[]> Loft(Vector3 from, Vector3 to, Vector3 widthAxis, Func<float, Ring> profile, int rings = 24, int around = 28, Vector3? depthHint = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var axis = to - from;
        var length = axis.Length();
        var dir = length > 1e-6f ? axis / length : Vector3.UnitY;
        var w = widthAxis - (dir * Vector3.Dot(widthAxis, dir));
        w = w.LengthSquared() > 1e-8f ? Vector3.Normalize(w) : Perpendicular(dir);
        var d = Vector3.Cross(dir, w);
        var hint = depthHint ?? (MathF.Abs(dir.Z) > 0.9f ? Vector3.UnitY : Vector3.UnitZ);
        if (Vector3.Dot(d, hint) < 0f)
        {
            d = -d;
        }

        var grid = new List<Vector3[]>(rings + 1);
        for (var i = 0; i <= rings; i++)
        {
            var s = (float)i / rings;
            var ring = profile(s);
            var center = from + (axis * s) + (w * ring.Offset.X) + (d * ring.Offset.Y);
            var points = new Vector3[around];
            for (var j = 0; j < around; j++)
            {
                var t = 2f * MathF.PI * j / around;
                var (sin, cos) = MathF.SinCos(t);
                var e = 2f / MathF.Max(0.5f, ring.Squareness);
                var x = MathF.Sign(cos) * MathF.Pow(MathF.Abs(cos), e) * ring.HalfWidth;
                var y = MathF.Sign(sin) * MathF.Pow(MathF.Abs(sin), e) * ring.HalfDepth;
                points[j] = center + (w * x) + (d * y);
            }

            grid.Add(points);
        }

        return grid;
    }

    /// <summary>ロフトの格子を三角形（位置＋法線）にしてリストへ足す。法線は格子の差分から（外向きに揃える）。</summary>
    public static void AppendLoft(List<float> vertices, List<Vector3[]> grid)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(grid);
        var rings = grid.Count;
        var around = grid[0].Length;
        var normals = new Vector3[rings][];
        for (var i = 0; i < rings; i++)
        {
            normals[i] = new Vector3[around];
            var center = Average(grid[i]);
            for (var j = 0; j < around; j++)
            {
                var along = grid[Math.Min(rings - 1, i + 1)][j] - grid[Math.Max(0, i - 1)][j];
                var tangent = grid[i][(j + 1) % around] - grid[i][(j + around - 1) % around];
                var n = Vector3.Cross(tangent, along);
                if (n.LengthSquared() < 1e-14f || tangent.LengthSquared() < 1e-12f)
                {
                    // 閉じた端（断面が点）: 軸方向へ向ける。
                    n = grid[i][j] - (i == 0 ? Average(grid[1]) : Average(grid[rings - 2]));
                }

                var outward = grid[i][j] - center;
                if (outward.LengthSquared() > 1e-12f && Vector3.Dot(n, outward) < 0f)
                {
                    n = -n;
                }

                normals[i][j] = n.LengthSquared() > 1e-14f ? Vector3.Normalize(n) : Vector3.UnitY;
            }
        }

        for (var i = 0; i < rings - 1; i++)
        {
            for (var j = 0; j < around; j++)
            {
                var k = (j + 1) % around;
                // 外から見て反時計回り（外向き法線と一致）になるよう、法線との向きで巻きを決める。
                AddTriangle(vertices, grid, normals, (i, j), (i + 1, j), (i + 1, k));
                AddTriangle(vertices, grid, normals, (i, j), (i + 1, k), (i, k));
            }
        }
    }

    public static MeshData Build(string name, params List<Vector3[]>[] lofts)
    {
        var vertices = new List<float>();
        foreach (var loft in lofts)
        {
            AppendLoft(vertices, loft);
        }

        return new MeshData([.. vertices], name);
    }

    /// <summary>丸く閉じる端のための断面の倍率（0 で点、1 で全幅）: s が端から <paramref name="cap"/> 以内なら円弧で細らせる。</summary>
    public static float Cap(float s, float cap)
    {
        var a = cap <= 0f ? 1f : Math.Clamp(s / cap, 0f, 1f);
        var b = cap <= 0f ? 1f : Math.Clamp((1f - s) / cap, 0f, 1f);
        return MathF.Sqrt(1f - ((1f - a) * (1f - a))) * MathF.Sqrt(1f - ((1f - b) * (1f - b)));
    }

    /// <summary>なめらかな補間（区分的な値をつなぐ）。</summary>
    public static float Smooth(float a, float b, float t)
    {
        var x = Math.Clamp(t, 0f, 1f);
        return a + ((b - a) * x * x * (3f - (2f * x)));
    }

    /// <summary>(位置, 値) の点列をなめらかにつないだ関数。</summary>
    public static float Curve(float s, params (float S, float V)[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (s <= keys[0].S)
        {
            return keys[0].V;
        }

        for (var i = 1; i < keys.Length; i++)
        {
            if (s <= keys[i].S)
            {
                var (s0, v0) = keys[i - 1];
                var (s1, v1) = keys[i];
                return Smooth(v0, v1, (s - s0) / MathF.Max(1e-6f, s1 - s0));
            }
        }

        return keys[^1].V;
    }

    private static void AddTriangle(List<float> vertices, List<Vector3[]> grid, Vector3[][] normals, (int I, int J) a, (int I, int J) b, (int I, int J) c)
    {
        var pa = grid[a.I][a.J];
        var pb = grid[b.I][b.J];
        var pc = grid[c.I][c.J];
        var face = Vector3.Cross(pb - pa, pc - pa);
        if (face.LengthSquared() < 1e-16f)
        {
            return; // 閉じた端の退化三角形
        }

        var n = normals[a.I][a.J] + normals[b.I][b.J] + normals[c.I][c.J];
        var flip = Vector3.Dot(face, n) < 0f;
        Emit(vertices, pa, normals[a.I][a.J]);
        if (flip)
        {
            Emit(vertices, pc, normals[c.I][c.J]);
            Emit(vertices, pb, normals[b.I][b.J]);
        }
        else
        {
            Emit(vertices, pb, normals[b.I][b.J]);
            Emit(vertices, pc, normals[c.I][c.J]);
        }
    }

    private static void Emit(List<float> vertices, Vector3 p, Vector3 n)
    {
        vertices.Add(p.X);
        vertices.Add(p.Y);
        vertices.Add(p.Z);
        vertices.Add(n.X);
        vertices.Add(n.Y);
        vertices.Add(n.Z);
    }

    private static Vector3 Average(Vector3[] points)
    {
        var sum = Vector3.Zero;
        foreach (var p in points)
        {
            sum += p;
        }

        return sum / points.Length;
    }

    private static Vector3 Perpendicular(Vector3 v)
        => Vector3.Normalize(Vector3.Cross(v, MathF.Abs(v.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
}
