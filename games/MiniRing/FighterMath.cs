using System.Numerics;

namespace MiniRing;

/// <summary>ラグドール制御の純関数群（物理ワールド非依存・テスト可能）。</summary>
public static class FighterMath
{
    /// <summary>
    /// 骨盤高さを保つ上向き加速度。重力は常に満額で打ち消し、<paramref name="scale"/> は PD 補正だけに掛ける
    /// （被弾・掴まれで弱っても沈み込まない）。
    /// <para><paramref name="release"/>（0..1）は「脚の力を抜いて腰を落とす」度合い。目標が今の腰よりずっと低いとき、脚は伸ばす力を抜いて
    /// 重力に任せる（ブレーキの粘り kd を <paramref name="brakeKeep"/> まで減らす）うえ、股・腹の筋肉が腰を下へ引く
    /// （最大 <paramref name="pull"/> [m/s²]）。0 なら従来どおり: 下向きには引かず、落ちる速さには kd のブレーキが掛かる。</para>
    /// </summary>
    public static float HoverAccel(float gravity, float heightError, float verticalVelocity, float kp, float kd, float maxAccel, float scale,
        float release = 0f, float brakeKeep = 1f, float pull = 0f)
    {
        var r = Math.Clamp(release, 0f, 1f);
        var damping = kd * (1f - (r * (1f - Math.Clamp(brakeKeep, 0f, 1f))));
        var correction = ((kp * heightError) - (damping * verticalVelocity)) * MathF.Max(0f, scale);
        return Math.Clamp(gravity + correction, -MathF.Max(0f, pull) * r * MathF.Max(0f, scale), maxAccel);
    }

    /// <summary>水平速度を目標へ寄せる加速度（鉛直成分は無視・上限付き）。</summary>
    public static Vector3 MoveAccel(Vector3 currentVelocity, Vector3 desiredVelocity, float gain, float maxAccel)
    {
        var error = new Vector3(desiredVelocity.X - currentVelocity.X, 0f, desiredVelocity.Z - currentVelocity.Z) * gain;
        var length = error.Length();
        return length > maxAccel ? error * (maxAccel / length) : error;
    }

    /// <summary>拳を目標へ引く PD 加速度。目標速度をフィードフォワードに使う（速いパンチでも遅れない）。</summary>
    public static Vector3 HandAccel(Vector3 position, Vector3 velocity, Vector3 target, Vector3 targetVelocity, float kp, float kd)
        => ((target - position) * kp) + ((targetVelocity - velocity) * kd);

    /// <summary>現在姿勢から目標姿勢への回転ベクトル（軸 × 角度[rad]、最短経路）。</summary>
    public static Vector3 RotationError(Quaternion current, Quaternion target)
    {
        var delta = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Conjugate(current), target));
        if (delta.W < 0f)
        {
            delta = Quaternion.Negate(delta); // 最短経路（360° 回りを避ける）
        }

        var sinHalf = new Vector3(delta.X, delta.Y, delta.Z).Length();
        if (sinHalf < 1e-6f)
        {
            return Vector3.Zero;
        }

        var angle = 2f * MathF.Atan2(sinHalf, delta.W);
        return new Vector3(delta.X, delta.Y, delta.Z) / sinHalf * angle;
    }

    /// <summary>
    /// 歩行の脚角度 [rad]。左右逆位相で股を振り（股は −X = 前）、膝は股が前へ振られている最中
    /// （角度が減少中 = −cos &gt; 0）だけ持ち上げる。<paramref name="blend"/> = 0 で静止姿勢。
    /// </summary>
    public static LegPose Gait(float phase, float blend, float neutralHip, float neutralKnee, float hipSwing, float kneeLift)
    {
        var b = Math.Clamp(blend, 0f, 1f);
        var (sin, cos) = MathF.SinCos(phase);
        return new LegPose(
            neutralHip + (sin * hipSwing * b),
            neutralHip - (sin * hipSwing * b),
            neutralKnee + (MathF.Max(0f, -cos) * kneeLift * b),
            neutralKnee + (MathF.Max(0f, cos) * kneeLift * b));
    }
}

public readonly record struct LegPose(float LeftHip, float RightHip, float LeftKnee, float RightKnee);
