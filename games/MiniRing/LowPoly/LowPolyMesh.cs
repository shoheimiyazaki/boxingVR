using System.Collections.Immutable;
using System.Numerics;
using Aigis.Render;

namespace MiniRing.LowPoly;

/// <summary>
/// 頂点（ノード）を共有する三角形メッシュ（不変）。ローポリの形の定義と、変形の単位（ノード）を兼ねる。
/// <para>三角形は外から見て反時計回り（法線 = (b − a) × (c − a) が外向き）。</para>
/// </summary>
public sealed class LowPolyMesh
{
    public LowPolyMesh(ImmutableArray<Vector3> vertices, ImmutableArray<int> triangles)
    {
        if (triangles.Length % 3 != 0)
        {
            throw new ArgumentException($"三角形のインデックス数 {triangles.Length} が 3 の倍数ではありません。", nameof(triangles));
        }

        foreach (var index in triangles)
        {
            if ((uint)index >= (uint)vertices.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(triangles), index, $"頂点数 {vertices.Length} の範囲外のインデックスです。");
            }
        }

        Vertices = vertices;
        Triangles = triangles;
    }

    public ImmutableArray<Vector3> Vertices { get; }

    /// <summary>3 つずつで 1 枚の三角形。</summary>
    public ImmutableArray<int> Triangles { get; }

    public int TriangleCount => Triangles.Length / 3;

    public (int A, int B, int C) Triangle(int index) => (Triangles[index * 3], Triangles[(index * 3) + 1], Triangles[(index * 3) + 2]);

    /// <summary>
    /// 各三角形を辺の中点で 4 枚に割る（中点は隣の三角形と共有）。頂点を動かさないので形は元のローポリのまま
    /// （平らな面は平らなまま）で、ノードだけが細かくなる。元の頂点は同じインデックスのまま先頭に残る。
    /// </summary>
    public LowPolyMesh Subdivide()
    {
        var vertices = Vertices.ToBuilder();
        var midpoints = new Dictionary<(int, int), int>();
        var triangles = ImmutableArray.CreateBuilder<int>(Triangles.Length * 4);

        int Midpoint(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (!midpoints.TryGetValue(key, out var index))
            {
                index = vertices.Count;
                vertices.Add((Vertices[a] + Vertices[b]) * 0.5f);
                midpoints.Add(key, index);
            }

            return index;
        }

        for (var t = 0; t < TriangleCount; t++)
        {
            var (a, b, c) = Triangle(t);
            var ab = Midpoint(a, b);
            var bc = Midpoint(b, c);
            var ca = Midpoint(c, a);
            triangles.AddRange(a, ab, ca);
            triangles.AddRange(ab, b, bc);
            triangles.AddRange(ca, bc, c);
            triangles.AddRange(ab, bc, ca);
        }

        return new LowPolyMesh(vertices.ToImmutable(), triangles.MoveToImmutable());
    }

    /// <summary>軸ごとに拡大した新しいメッシュ（正規化した形を部位の半径 [m] に合わせる）。</summary>
    public LowPolyMesh Scaled(Vector3 scale)
    {
        if (scale.X <= 0f || scale.Y <= 0f || scale.Z <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "裏返らないよう、拡大率は正の値にしてください。");
        }

        return new LowPolyMesh([.. Vertices.Select(v => v * scale)], Triangles);
    }

    /// <summary>回転した新しいメッシュ（回転は巻き順＝面の表裏を保つ）。</summary>
    public LowPolyMesh Rotated(Quaternion rotation) => new([.. Vertices.Select(v => Vector3.Transform(v, rotation))], Triangles);

    /// <summary>
    /// フラットシェーディングの描画用メッシュ（面ごとの法線。ローポリの面がそのまま見える）。
    /// MeshData は参照ごとに GPU へ 1 回だけ上がるので、呼び出し側で使い回すこと。
    /// </summary>
    public MeshData ToMeshData(string name)
    {
        var data = new float[TriangleCount * 3 * MeshData.FloatsPerVertex];
        var w = 0;
        for (var t = 0; t < TriangleCount; t++)
        {
            var (a, b, c) = Triangle(t);
            var normal = FaceNormal(Vertices[a], Vertices[b], Vertices[c]);
            foreach (var p in new[] { Vertices[a], Vertices[b], Vertices[c] })
            {
                data[w++] = p.X;
                data[w++] = p.Y;
                data[w++] = p.Z;
                data[w++] = normal.X;
                data[w++] = normal.Y;
                data[w++] = normal.Z;
            }
        }

        return new MeshData(data, name);
    }

    /// <summary>閉じた面の符号付き体積。外向きの巻き順なら正。</summary>
    public float SignedVolume()
    {
        var volume = 0f;
        for (var t = 0; t < TriangleCount; t++)
        {
            var (a, b, c) = Triangle(t);
            volume += Vector3.Dot(Vertices[a], Vector3.Cross(Vertices[b], Vertices[c])) / 6f;
        }

        return volume;
    }

    public float SurfaceArea()
    {
        var area = 0f;
        for (var t = 0; t < TriangleCount; t++)
        {
            var (a, b, c) = Triangle(t);
            area += Vector3.Cross(Vertices[b] - Vertices[a], Vertices[c] - Vertices[a]).Length() * 0.5f;
        }

        return area;
    }

    public static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        return n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitY;
    }
}
