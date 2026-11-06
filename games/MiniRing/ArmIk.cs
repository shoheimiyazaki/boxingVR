using System.Numerics;

namespace MiniRing;

/// <summary>
/// 腕の解析的 2 ボーン IK と、その結果を関節サーボ目標（上腕のワールド回転・肘の曲げ角）へ
/// 変換する純関数群。
/// <para>
/// Aigis の HumanoidRagdoll は T ポーズ（左腕 −X・右腕 +X に伸び、全部位が単位回転）で組まれる。
/// 上腕のローカル軸は「腕の長さ方向 = side·X」「肘のヒンジ軸 = Y（右腕は Mirrored 基底なので −Y）」。
/// 正の肘角は前腕を「上腕→前腕」へ回す向きなので、ヒンジ軸を曲げ平面の法線 (u × f) に合わせれば
/// 曲げ角そのものをサーボ目標に渡せる。
/// </para>
/// </summary>
public static class ArmIk
{
    /// <summary>肩・目標・ポール（肘を向けたい点）から肘と手の位置を求める。届かない目標は伸び切り、近すぎる目標は最小折り畳みへクランプ。</summary>
    public static (Vector3 Elbow, Vector3 Hand) SolveTwoBone(Vector3 shoulder, Vector3 target, Vector3 pole, float upperLength, float lowerLength)
    {
        var toTarget = target - shoulder;
        var rawDistance = toTarget.Length();
        var dir = rawDistance > 1e-5f ? toTarget / rawDistance : -Vector3.UnitY;
        var distance = Math.Clamp(rawDistance, MathF.Abs(upperLength - lowerLength) + 1e-4f, (upperLength + lowerLength) * 0.9999f);
        var hand = shoulder + (dir * distance);

        // 余弦定理: 肩から肘の、腕軸方向の投影長 a と軸からの高さ h。
        var a = ((upperLength * upperLength) - (lowerLength * lowerLength) + (distance * distance)) / (2f * distance);
        var h = MathF.Sqrt(MathF.Max(0f, (upperLength * upperLength) - (a * a)));
        var bend = BendDirection(dir, pole - shoulder);
        return (shoulder + (dir * a) + (bend * h), hand);
    }

    /// <summary>
    /// 上腕のワールド回転と肘の曲げ角（ラジアン、0 = 伸展）。
    /// <paramref name="side"/> は左 −1 / 右 +1。<paramref name="bendNormal"/> は曲げ平面の法線
    /// （腕が伸び切っていても退化しないよう、IK の目標とポールから求めたものを渡す）。
    /// </summary>
    public static (Quaternion UpperArm, float ElbowAngle) JointTargets(Vector3 shoulder, Vector3 elbow, Vector3 hand, Vector3 bendNormal, float side)
    {
        var u = SafeNormalize(elbow - shoulder, side * Vector3.UnitX);
        var f = SafeNormalize(hand - elbow, u);
        var n = SafeNormalize(bendNormal - (u * Vector3.Dot(bendNormal, u)), Perpendicular(u));

        // ローカル X = side·u（X 軸の像）、ローカル Y = ヒンジ軸（右腕は −Y がヒンジ）。
        var xAxis = side * u;
        var yAxis = side < 0f ? n : -n;
        var zAxis = Vector3.Cross(xAxis, yAxis);
        var basis = new Matrix4x4(
            xAxis.X, xAxis.Y, xAxis.Z, 0f,
            yAxis.X, yAxis.Y, yAxis.Z, 0f,
            zAxis.X, zAxis.Y, zAxis.Z, 0f,
            0f, 0f, 0f, 1f);
        var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis));
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(u, f), -1f, 1f));
        return (rotation, angle);
    }

    /// <summary>肩→目標の軸と肩→ポールから、曲げ平面の法線を求める（肘がポール側へ出る向き）。</summary>
    public static Vector3 BendNormal(Vector3 shoulder, Vector3 target, Vector3 pole)
    {
        var d = target - shoulder;
        var p = pole - shoulder;
        var n = Vector3.Cross(p, d);
        return n.LengthSquared() > 1e-10f ? Vector3.Normalize(n) : Perpendicular(SafeNormalize(d, -Vector3.UnitY));
    }

    /// <summary>目標を肩から <paramref name="maxReach"/> 以内に収める。</summary>
    public static Vector3 ClampReach(Vector3 shoulder, Vector3 target, float maxReach)
    {
        var offset = target - shoulder;
        var length = offset.Length();
        return length > maxReach ? shoulder + (offset * (maxReach / length)) : target;
    }

    private static Vector3 BendDirection(Vector3 axis, Vector3 poleOffset)
    {
        var perpendicular = poleOffset - (axis * Vector3.Dot(poleOffset, axis));
        return perpendicular.LengthSquared() > 1e-10f ? Vector3.Normalize(perpendicular) : Perpendicular(axis);
    }

    private static Vector3 Perpendicular(Vector3 v)
        => Vector3.Normalize(Vector3.Cross(v, MathF.Abs(v.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
        => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : fallback;
}
