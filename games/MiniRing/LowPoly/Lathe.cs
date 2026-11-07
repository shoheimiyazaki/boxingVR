using System.Collections.Immutable;
using System.Numerics;

namespace MiniRing.LowPoly;

/// <summary>
/// 輪（Y 軸まわりの点列）を上から順に重ね、上下の極で閉じたメッシュを作る（ろくろ型）。
/// 輪の点を個別に押し引きすれば、鼻・頬・顎のような前後非対称の形も作れる。
/// </summary>
public static class Lathe
{
    /// <summary>
    /// <paramref name="rings"/> は上から下へ。各輪は同じ点数で、角度 θ（+Z から +X へ回る向き）の昇順に並べる。
    /// この順なら三角形は外向きになる。
    /// </summary>
    public static LowPolyMesh Build(Vector3 top, IReadOnlyList<IReadOnlyList<Vector3>> rings, Vector3 bottom)
    {
        ArgumentNullException.ThrowIfNull(rings);
        if (rings.Count == 0)
        {
            throw new ArgumentException("輪が 1 つもありません。", nameof(rings));
        }

        var n = rings[0].Count;
        if (n < 3 || rings.Any(r => r.Count != n))
        {
            throw new ArgumentException("輪はどれも同じ点数（3 点以上）にしてください。", nameof(rings));
        }

        var vertices = ImmutableArray.CreateBuilder<Vector3>();
        vertices.Add(top);
        foreach (var ring in rings)
        {
            vertices.AddRange(ring);
        }

        vertices.Add(bottom);
        var bottomIndex = vertices.Count - 1;
        int At(int ring, int k) => 1 + (ring * n) + (k % n);

        var triangles = ImmutableArray.CreateBuilder<int>();
        for (var k = 0; k < n; k++)
        {
            triangles.AddRange(0, At(0, k), At(0, k + 1));
        }

        for (var r = 0; r + 1 < rings.Count; r++)
        {
            for (var k = 0; k < n; k++)
            {
                var (a, b) = (At(r, k), At(r, k + 1));
                var (c, d) = (At(r + 1, k), At(r + 1, k + 1));
                triangles.AddRange(a, c, b);
                triangles.AddRange(b, c, d);
            }
        }

        var last = rings.Count - 1;
        for (var k = 0; k < n; k++)
        {
            triangles.AddRange(bottomIndex, At(last, k + 1), At(last, k));
        }

        return new LowPolyMesh(vertices.ToImmutable(), triangles.ToImmutable());
    }

    /// <summary>角度 θ [度]（+Z から +X へ）・半径・高さの点。</summary>
    public static Vector3 Point(float degrees, float radius, float y)
    {
        var a = degrees * MathF.PI / 180f;
        return new Vector3(MathF.Sin(a) * radius, y, MathF.Cos(a) * radius);
    }

    /// <summary>
    /// 左右対称の輪。<paramref name="profile"/> は正面（0°）から真後ろ（180°）までの片側の (角度, 半径, 高さのずれ)。
    /// 反対側は鏡写しにして、θ の昇順に並べて返す。
    /// </summary>
    public static IReadOnlyList<Vector3> MirroredRing(float y, params (float Degrees, float Radius, float Dy)[] profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Length < 2 || profile[0].Degrees != 0f || profile[^1].Degrees != 180f)
        {
            throw new ArgumentException("片側の輪は 0° から始めて 180° で終えてください。", nameof(profile));
        }

        var ring = profile.Select(p => Point(p.Degrees, p.Radius, y + p.Dy)).ToList();
        for (var i = profile.Length - 2; i >= 1; i--)
        {
            var p = profile[i];
            ring.Add(Point(360f - p.Degrees, p.Radius, y + p.Dy));
        }

        return ring;
    }

    /// <summary>半径が一定の輪（腕・脚）。<paramref name="sides"/> 角形。</summary>
    public static IReadOnlyList<Vector3> RoundRing(float y, float radius, int sides)
        => [.. Enumerable.Range(0, sides).Select(k => Point(k * 360f / sides, radius, y))];
}
