using System.Numerics;

namespace MiniRing;

/// <summary>
/// 足の 2 つの部位（かかと側の足とつま先）の形。かかと側は Aigis の足の箱（<see cref="FighterTuning.HalfExtents"/> の Foot）で、
/// つま先はその前に母指球の蝶番でつながる。足の長さのうち <see cref="ToeShare"/> がつま先。
/// </summary>
public static class FootGeometry
{
    /// <summary>足の長さのうち、つま先（母指球より前）の割合。</summary>
    public const float ToeShare = 0.4f;

    /// <summary>つま先の厚みのかかと側に対する割合。</summary>
    public const float ToeThickness = 0.7f;

    /// <summary>つま先の箱の半分の大きさ（かかと側の箱 <paramref name="heel"/> から）。</summary>
    public static Vector3 ToeHalf(Vector3 heel) => new(heel.X, heel.Y * ToeThickness, heel.Z * ToeShare / (1f - ToeShare));

    /// <summary>母指球（つま先の蝶番）の位置（かかと側のローカル: 前の下の縁）。</summary>
    public static Vector3 BallOnHeel(Vector3 heel) => new(0f, -heel.Y, heel.Z);

    /// <summary>母指球の位置（つま先のローカル: 後ろの下の縁）。</summary>
    public static Vector3 BallOnToe(Vector3 heel)
    {
        var toe = ToeHalf(heel);
        return new Vector3(0f, -toe.Y, -toe.Z);
    }

    /// <summary>つま先の中心（かかと側のローカル。足裏の高さをそろえる）。</summary>
    public static Vector3 ToeCenterOnHeel(Vector3 heel) => BallOnHeel(heel) - BallOnToe(heel);
}

/// <summary>
/// 脚の 2 ボーン IK（純関数）。股の回転の中心（骨盤のローカル）から足首の目標へ、腿の回転（骨盤に対する）と膝の曲げを解く。
/// <para>
/// Aigis の脚は、腿が骨盤の底の中心を軸に回り（股の中心は横へずれた腿の上にない）、膝は腿のローカル X 軸の蝶番。
/// 立った形（関節がすべて 0）での「股の軸 → 膝」<c>r0</c> と「膝 → 足首」（真下、長さ <c>b</c>）から、
/// |r0 + Rx(k)·s0| = 股から足首までの距離 になる膝の角度 k を求め、腿の向きは膝が <c>kneeForward</c> を向くように回す。
/// </para>
/// </summary>
public static class LegIk
{
    /// <summary>膝の曲げの上限 [rad]。</summary>
    public const float MaxKnee = 130f * MathF.PI / 180f;

    /// <summary>
    /// <paramref name="toAnkle"/>（股の軸 → 足首の目標、骨盤のローカル）に足首を届かせる腿の回転と膝の角度。
    /// 届かなければ脚を伸ばしきって目標の向きへ向ける（ReachRatio が 1 を超える）。
    /// </summary>
    /// <param name="toAnkle">股の軸 → 足首の目標（骨盤のローカル）。</param>
    /// <param name="thighToKnee">立った形での股の軸 → 膝（骨盤のローカル = 腿のローカル）。</param>
    /// <param name="shin">膝から足首までの長さ。</param>
    /// <param name="kneeForward">膝を向けたい向き（骨盤のローカル。つま先の向き）。</param>
    public static (Quaternion Thigh, float Knee, float ReachRatio) Solve(Vector3 toAnkle, Vector3 thighToKnee, float shin, Vector3 kneeForward)
    {
        var r0 = thighToKnee;
        var b = shin;
        var distance = toAnkle.Length();
        var max = MaxLength(r0, b);
        var min = Length(r0, b, MaxKnee);
        var d = Math.Clamp(distance, min, max);
        // |r0 + Rx(k) s0|² = |r0|² + b² − 2 b (r0.Y cos k + r0.Z sin k)、s0 = (0, −b, 0)。r0.Z は 0 なので cos k だけで決まる。
        var denominator = -2f * b * r0.Y;
        var cos = denominator > 1e-9f ? ((d * d) - r0.LengthSquared() - (b * b)) / denominator : 1f;
        var knee = MathF.Acos(Math.Clamp(cos, MathF.Cos(MaxKnee), 1f));
        var restAnkle = r0 + ShinVector(b, knee);

        // 立った形の（足首の向き, 膝の前）を、目標の（足首の向き, 膝を向けたい向き）へ移す回転。
        var from = Basis(Vector3.Normalize(restAnkle), Vector3.UnitZ);
        var direction = distance > 1e-6f ? toAnkle / distance : -Vector3.UnitY;
        var to = Basis(direction, kneeForward);
        var thigh = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Conjugate(from), to));
        return (thigh, knee, distance / MathF.Max(1e-6f, max));
    }

    /// <summary>膝の角度 <paramref name="knee"/> のときの股の軸から足首までの距離。</summary>
    public static float Length(Vector3 thighToKnee, float shin, float knee) => (thighToKnee + ShinVector(shin, knee)).Length();

    public static float MaxLength(Vector3 thighToKnee, float shin) => Length(thighToKnee, shin, 0f);

    /// <summary>膝を <paramref name="knee"/> 曲げたときの膝 → 足首（腿のローカル。正の角度ですねが後ろへ）。</summary>
    public static Vector3 ShinVector(float shin, float knee) => new(0f, -shin * MathF.Cos(knee), -shin * MathF.Sin(knee));

    /// <summary>
    /// ローカルの X を <paramref name="along"/>、Z を <paramref name="forward"/>（along に直交させる）へ向ける回転。
    /// </summary>
    internal static Quaternion Basis(Vector3 along, Vector3 forward)
    {
        var x = Vector3.Normalize(along);
        var z = forward - (Vector3.Dot(forward, x) * x);
        if (z.LengthSquared() < 1e-8f)
        {
            z = MathF.Abs(x.Y) < 0.9f ? Vector3.UnitY - (x.Y * x) : Vector3.UnitZ - (x.Z * x);
        }

        z = Vector3.Normalize(z);
        var y = Vector3.Cross(z, x);
        var m = new Matrix4x4(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    /// <summary>
    /// 足を <paramref name="foot"/>（ワールドの姿勢）にするための足首の曲げ（すねに対する X 軸回りの角度、正でつま先が下がる）。
    /// 足首のサーボは前後の曲げだけを持つ（横の傾きは受け身の関節）。
    /// </summary>
    public static float AnklePitch(Quaternion shin, Quaternion foot)
    {
        var relative = Quaternion.Concatenate(foot, Quaternion.Conjugate(shin));
        var forward = Vector3.Transform(Vector3.UnitZ, relative);
        return MathF.Atan2(-forward.Y, forward.Z);
    }
}
