using System.Numerics;

namespace MiniRing;

/// <summary>
/// 拳を目標へ引く筋肉（Hill 型、docs/BOXING.md「拳を引く筋肉」）: 神経の指令（興奮）→ 活性化（一次遅れ）→ 力 = 活性化 × 最大の力 × 長さの関係 × 速さの関係。
/// <list type="bullet">
/// <item>指令は PD（脳が出す指令）が欲しい力を最大の力で割った 0..1（向き付き）。目標が遠いほど全力に近い。</item>
/// <item>筋肉のまとまりは胸のフレームの ±X・±Y・±Z の 6 つ（主動筋と拮抗筋の対）。指令の各成分の正の部分がそのまとまりの興奮で、
/// 活性化は立ち上がり <see cref="FighterTuning.MuscleActivationTime"/>・緩み <see cref="FighterTuning.MuscleDeactivationTime"/> で追う。
/// だから全力で出している主動筋はすぐには抜けず、止める（拮抗筋）のも遅れる。</item>
/// <item>まとまりの引く向きを、肩から拳への向き（肘の伸び縮み）とそれに直交する向き（肩の振り）に分け、それぞれに長さと速さの関係を掛ける。</item>
/// </list>
/// 武器を握った手・ガード・押し付けも同じ式を通る（拳を目標へ引く力はこれだけ）。
/// </summary>
public static class ArmMuscle
{
    /// <summary>筋肉のまとまりの数（胸のフレームの ±X・±Y・±Z）。</summary>
    public const int Groups = 6;

    /// <summary>まとまり <paramref name="i"/> の引く向き（胸のフレーム）: 0 +X, 1 −X, 2 +Y, 3 −Y, 4 +Z, 5 −Z。</summary>
    public static Vector3 Axis(int i)
    {
        var sign = (i & 1) == 0 ? 1f : -1f;
        return (i >> 1) switch
        {
            0 => Vector3.UnitX * sign,
            1 => Vector3.UnitY * sign,
            _ => Vector3.UnitZ * sign,
        };
    }

    /// <summary>
    /// 指令（興奮）: 欲しい力 / 最大の力 を長さ 1 までに飽和させたベクトル。胸のフレームで返す。
    /// </summary>
    public static Vector3 Excitation(Vector3 desired, float maxForce, Quaternion chest)
    {
        if (!(maxForce > 1e-6f) || !float.IsFinite(desired.X + desired.Y + desired.Z))
        {
            return Vector3.Zero;
        }

        var e = desired / maxForce;
        var length = e.Length();
        e = length > 1f ? e / length : e;
        return Vector3.Transform(e, Quaternion.Conjugate(chest));
    }

    /// <summary>まとまり <paramref name="i"/> の興奮（指令の、その向きの正の成分）。</summary>
    public static float GroupExcitation(Vector3 excitationLocal, int i) => MathF.Max(0f, Vector3.Dot(excitationLocal, Axis(i)));

    /// <summary>
    /// 活性化の一次遅れ（厳密な指数の 1 歩）: 興奮が上なら立ち上がりの時定数、下なら緩みの時定数で追う。0..1。
    /// </summary>
    public static float Activate(float activation, float excitation, float dt, float riseTime, float fallTime)
    {
        var u = Math.Clamp(float.IsFinite(excitation) ? excitation : 0f, 0f, 1f);
        var tau = u > activation ? riseTime : fallTime;
        var k = tau > 1e-6f ? 1f - MathF.Exp(-dt / tau) : 1f;
        return Math.Clamp(activation + ((u - activation) * k), 0f, 1f);
    }

    /// <summary>
    /// Hill の力と速さの関係（等尺の力に対する割合）。<paramref name="shortening"/> は縮む速さ / 最大の縮む速さ（正 = 縮む、負 = 伸ばされる）。
    /// 縮む側は Hill の双曲線 (1 − v)/(1 + v / k)（k = a/F0、v = 1 で 0）。伸ばされる側は <paramref name="eccentricMax"/> へ近づく双曲線で、
    /// 0 での傾きは縮む側の <paramref name="eccentricSlope"/> 倍（Katz 1939: 伸ばされる側の方が急）。0 で連続。
    /// </summary>
    public static float ForceVelocity(float shortening, float curvature, float eccentricMax, float eccentricSlope)
    {
        if (!float.IsFinite(shortening))
        {
            return 0f;
        }

        var k = MathF.Max(1e-3f, curvature);
        if (shortening >= 0f)
        {
            return shortening >= 1f ? 0f : (1f - shortening) / (1f + (shortening / k));
        }

        // 伸ばされる速さ w（> 0）: 1 + (Fe − 1) w / (w + c)、0 での傾き (Fe − 1)/c = eccentricSlope × (1 + 1/k)
        var w = -shortening;
        var rise = MathF.Max(0f, eccentricMax - 1f);
        var c = rise / MathF.Max(1e-3f, eccentricSlope * (1f + (1f / k)));
        return c <= 0f ? 1f : 1f + (rise * w / (w + c));
    }

    /// <summary>
    /// 腕の伸び具合（肩から拳の距離 / 腕の長さ）による長さの関係（拳 1 点の近似）。拳を外へ押す（伸ばす）筋は伸びきりに近いほど、
    /// 内へ引く（曲げる）筋は曲げきりに近いほど縮みきって弱くなる。<paramref name="outward"/> = true が外へ押す筋。
    /// 1 − (1 − 端の値) × (最適の伸びからのずれ / 端までの幅)²（最適の伸びより手前（押す筋）・奥（引く筋）は 1）。
    /// </summary>
    public static float ForceLength(float extension, bool outward, float optimal, float endForce, float flexedEnd)
    {
        var x = Math.Clamp(float.IsFinite(extension) ? extension : optimal, 0f, 1f);
        var span = outward ? 1f - optimal : optimal - flexedEnd;
        var past = outward ? x - optimal : optimal - x;
        if (past <= 0f || span <= 1e-4f)
        {
            return 1f;
        }

        var t = MathF.Min(1f, past / span);
        return 1f - ((1f - Math.Clamp(endForce, 0f, 1f)) * t * t);
    }

    /// <summary>腕の今の形と動き（肩に対する拳）。</summary>
    /// <param name="Radial">肩から拳への単位ベクトル（ワールド）。</param>
    /// <param name="Extension">肩から拳の距離 / 腕の長さ。</param>
    /// <param name="Velocity">肩に対する拳の速度（ワールド）。</param>
    public readonly record struct Pose(Vector3 Radial, float Extension, Vector3 Velocity);

    /// <summary>筋肉の値（<see cref="FighterTuning"/> から）。</summary>
    public readonly record struct Constants(float MaxSpeed, float Curvature, float EccentricMax, float EccentricSlope,
        float OptimalExtension, float EndForce, float FlexedEnd)
    {
        public static Constants From(FighterTuning tuning, float slowness) => new(
            // 疲れ（slowness [s/m]）は最大の縮む速さを下げる: 1 / v_max' = 1 / v_max + slowness（力と速さの関係は 1 つ）。
            1f / ((1f / MathF.Max(1e-3f, tuning.HandMuscleMaxSpeed)) + MathF.Max(0f, slowness)),
            tuning.HandMuscleCurvature, tuning.HandMuscleEccentricMax, tuning.HandMuscleEccentricSlope,
            tuning.ArmMuscleOptimalExtension, tuning.ArmMuscleEndForce, tuning.ArmMuscleFlexedEnd);
    }

    /// <summary>
    /// 活性化した 6 つのまとまりが拳に出す力（ワールド）。まとまりの引く向き d を、肩から拳への向き（肘: 長さの関係・その向きの縮む速さ）と
    /// 直交する向き（肩: その向きの縮む速さ）に分けて、それぞれの割合を掛けて足す。
    /// </summary>
    public static Vector3 Force(ReadOnlySpan<float> activation, Quaternion chest, float maxForce, in Pose pose, in Constants c)
    {
        var total = Vector3.Zero;
        var radialSpeed = Vector3.Dot(pose.Velocity, pose.Radial);
        for (var i = 0; i < Groups; i++)
        {
            var a = activation[i];
            if (a <= 0f)
            {
                continue;
            }

            var d = Vector3.Transform(Axis(i), chest);
            var s = Vector3.Dot(d, pose.Radial);
            var tangent = d - (pose.Radial * s);
            var outward = s >= 0f;
            // 肘の分: 外へ押す筋は拳が外へ動くと縮む（s > 0 で radialSpeed > 0 が縮む）。
            var elbow = ForceLength(pose.Extension, outward, c.OptimalExtension, c.EndForce, c.FlexedEnd)
                * ForceVelocity(MathF.Sign(s) * radialSpeed / c.MaxSpeed, c.Curvature, c.EccentricMax, c.EccentricSlope);
            // 肩の分: 直交する向きへ拳が動けば縮む。
            var t = tangent.Length();
            var shoulder = t > 1e-6f
                ? ForceVelocity(Vector3.Dot(pose.Velocity, tangent / t) / c.MaxSpeed, c.Curvature, c.EccentricMax, c.EccentricSlope)
                : 0f;
            total += a * maxForce * ((pose.Radial * (s * elbow)) + (tangent * shoulder));
        }

        return total;
    }
}
