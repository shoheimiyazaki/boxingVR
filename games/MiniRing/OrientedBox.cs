using System.Numerics;

namespace MiniRing;

/// <summary>
/// 向きのある箱（OBB）どうしが重なっているか（分離軸の判定: 両方の箱の 3 軸と、軸どうしの外積の 9 軸の 15 本のどれかに投げた影が離れていれば重ならない）。
/// 接触を解かずに「重なっているか」だけを知りたいとき（生成で重ねて置いた武器と握った本人の体: <see cref="BoxingGame"/> の生成の重なり）に、格子を囲む箱で使う。
/// </summary>
internal static class OrientedBox
{
    /// <summary>平行な 2 軸の外積（長さ 0 に近い）は分離軸にしない（その向きは両方の箱の軸がすでに調べている）。</summary>
    private const float ParallelEpsilon = 1e-8f;

    /// <summary>中心 <paramref name="centerA"/>・向き <paramref name="orientationA"/>・半分の寸法 <paramref name="halfA"/> の箱と、B の箱が重なっているか（面が接するだけも重なりに数える）。</summary>
    public static bool Overlaps(Vector3 centerA, Quaternion orientationA, Vector3 halfA, Vector3 centerB, Quaternion orientationB, Vector3 halfB)
    {
        var ax = Vector3.Transform(Vector3.UnitX, orientationA);
        var ay = Vector3.Transform(Vector3.UnitY, orientationA);
        var az = Vector3.Transform(Vector3.UnitZ, orientationA);
        var bx = Vector3.Transform(Vector3.UnitX, orientationB);
        var by = Vector3.Transform(Vector3.UnitY, orientationB);
        var bz = Vector3.Transform(Vector3.UnitZ, orientationB);
        var d = centerB - centerA;
        ReadOnlySpan<Vector3> axes =
        [
            ax, ay, az, bx, by, bz,
            Vector3.Cross(ax, bx), Vector3.Cross(ax, by), Vector3.Cross(ax, bz),
            Vector3.Cross(ay, bx), Vector3.Cross(ay, by), Vector3.Cross(ay, bz),
            Vector3.Cross(az, bx), Vector3.Cross(az, by), Vector3.Cross(az, bz),
        ];
        foreach (var axis in axes)
        {
            if (axis.LengthSquared() < ParallelEpsilon)
            {
                continue;
            }

            var reachA = Reach(axis, ax, ay, az, halfA);
            var reachB = Reach(axis, bx, by, bz, halfB);
            if (MathF.Abs(Vector3.Dot(d, axis)) > reachA + reachB)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>箱の中心から、軸 <paramref name="axis"/> へ投げた影の半分の長さ。</summary>
    private static float Reach(Vector3 axis, Vector3 x, Vector3 y, Vector3 z, Vector3 half)
        => (MathF.Abs(Vector3.Dot(axis, x)) * half.X) + (MathF.Abs(Vector3.Dot(axis, y)) * half.Y) + (MathF.Abs(Vector3.Dot(axis, z)) * half.Z);
}
