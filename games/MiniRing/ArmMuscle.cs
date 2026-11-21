using System.Numerics;

namespace MiniRing;

/// <summary>
/// 拳を目標へ引く筋肉（Hill 型、docs/BOXING.md「拳を引く筋肉」）: 神経の指令（興奮）→ 活性化（一次遅れ）→ 力 = 活性化 × 最大の力 × 長さの関係 × 速さの関係。
/// <list type="bullet">
/// <item>指令は PD（脳が出す指令）が欲しい力を最大の力で割った 0..1（向き付き）。目標が遠いほど全力に近い。</item>
/// <item>筋肉のまとまりは胸のフレームの ±X・±Y・±Z の 6 つ（主動筋と拮抗筋の対）。指令の各成分の正の部分がそのまとまりの興奮で、
/// 活性化は立ち上がり <see cref="FighterTuning.MuscleActivationTime"/>・緩み <see cref="FighterTuning.MuscleDeactivationTime"/> で追う。
/// だから全力で出している主動筋はすぐには抜けず、止める（拮抗筋）のも遅れる。</item>
/// <item>まとまりの引く向きを、肩から拳への向き（肘の伸び縮み）とそれに直交する向き（肩の振り）に分け、関節ごとの力の出しやすさ（肘のトルクと角度の関係 ×
/// てこ = 拳の向きへのヤコビアン）と、関節の縮む速さの Hill の関係を掛ける。体の向き・踏ん張り（体を借りる倍率）は最大の力の側（PunchMechanics）で数える。</item>
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
    /// 肘のトルクと角度の関係（最大に対する割合）: 肘の角度 <paramref name="bend"/> [rad]（0 = 伸びきり）が <paramref name="peak"/> で 1、
    /// 伸びきり（0）と曲げきり（<paramref name="limit"/>）で <paramref name="endTorque"/> へ放物線で下がる。筋の長さと力の関係 × てこの長さの
    /// 変わり方をまとめた人の測定の形（Kulig 1984、Murray 1995: 曲げ・伸ばしとも 80〜100° で最大、両端で 5〜6 割）。
    /// </summary>
    public static float ElbowTorque(float bend, float peak, float limit, float endTorque)
    {
        var x = Math.Clamp(float.IsFinite(bend) ? bend : peak, 0f, MathF.Max(limit, peak));
        var span = x < peak ? peak : limit - peak;
        var t = span > 1e-4f ? MathF.Min(1f, MathF.Abs(x - peak) / span) : 0f;
        return 1f - ((1f - Math.Clamp(endTorque, 0f, 1f)) * t * t);
    }

    /// <summary>
    /// 肩から拳の距離 <paramref name="distance"/> から肘の角度 [rad]（0 = 伸びきり）: r² = L1² + L2² + 2 L1 L2 cos(角度)。
    /// </summary>
    public static float ElbowBend(float distance, float upper, float lower)
    {
        var c = ((distance * distance) - (upper * upper) - (lower * lower)) / MathF.Max(1e-8f, 2f * upper * lower);
        return MathF.Acos(Math.Clamp(c, -1f, 1f));
    }

    /// <summary>
    /// 肘の角度を変えたとき肩から拳の距離が変わる割合 |dr/dθ| = L1 L2 sin θ / r [m/rad]（肘のトルクが拳を肩から押し出す・引き寄せる力に
    /// 変わるてこ: 力 = トルク / これ、拳の速さ = 角速度 × これ）。伸びきりで 0 になる（特異姿勢）ので、角度を <paramref name="minBend"/> 以上で数える。
    /// </summary>
    public static float ElbowLever(float bend, float upper, float lower, float minBend)
    {
        var b = MathF.Max(bend, minBend);
        var r = MathF.Sqrt(MathF.Max(1e-8f, (upper * upper) + (lower * lower) + (2f * upper * lower * MathF.Cos(b))));
        return upper * lower * MathF.Sin(b) / r;
    }

    /// <summary>腕の今の形と動き（肩に対する拳）。</summary>
    /// <param name="Radial">肩から拳への単位ベクトル（ワールド）。</param>
    /// <param name="Distance">肩から拳の距離 [m]。</param>
    /// <param name="Velocity">肩に対する拳の速度（ワールド）。</param>
    /// <param name="Upper">肩から肘の長さ [m]。</param>
    /// <param name="Lower">肘から拳の長さ [m]。</param>
    public readonly record struct Pose(Vector3 Radial, float Distance, Vector3 Velocity, float Upper, float Lower);

    /// <summary>筋肉の値（<see cref="FighterTuning"/> から）。</summary>
    /// <param name="ElbowSpeed">肘の筋が力を出せなくなる角速度 [rad/s]。</param>
    /// <param name="ShoulderSpeed">肩の筋が力を出せなくなる角速度 [rad/s]。</param>
    /// <param name="Slowness">疲れ [s/m]: 拳の速さに直した最大の縮む速さ v_max を 1 / v_max' = 1 / v_max + これ、に下げる（力と速さの関係は 1 つ）。</param>
    /// <param name="Curvature">Hill の式の a/F0。</param>
    /// <param name="EccentricMax">伸ばされながら出せる力の上限（等尺の倍）。</param>
    /// <param name="EccentricSlope">伸ばされる側の傾きの、縮む側に対する倍。</param>
    /// <param name="ElbowPeak">肘のトルクが最大の角度 [rad]（構え）。</param>
    /// <param name="ElbowLimit">肘の曲げきりの角度 [rad]。</param>
    /// <param name="ElbowEndTorque">伸びきり・曲げきりでの肘のトルクの割合。</param>
    /// <param name="MinBend">肘のてこを数える角度の下限 [rad]。</param>
    public readonly record struct Constants(float ElbowSpeed, float ShoulderSpeed, float Slowness, float Curvature, float EccentricMax, float EccentricSlope,
        float ElbowPeak, float ElbowLimit, float ElbowEndTorque, float MinBend)
    {
        public static Constants From(FighterTuning tuning, float slowness) => new(
            tuning.ElbowMuscleMaxSpeed, tuning.ShoulderMuscleMaxSpeed, MathF.Max(0f, slowness),
            tuning.HandMuscleCurvature, tuning.HandMuscleEccentricMax, tuning.HandMuscleEccentricSlope,
            tuning.ElbowPeakRadians, tuning.ElbowFlexLimitRadians, tuning.ElbowEndTorque, tuning.ElbowMinLeverRadians);

        /// <summary>関節の角速度の上限 × 構えのてこ [m/s] を、疲れで下げた拳の速さの上限。</summary>
        public float Tired(float angularSpeed, float lever) => 1f / ((1f / MathF.Max(1e-3f, angularSpeed * lever)) + Slowness);
    }

    /// <summary>
    /// 肩・肘の関節ごとの、拳の向きへの力の出しやすさ（構え = 肘 <see cref="Constants.ElbowPeak"/> で 1）と、縮む速さの尺度。
    /// 肘（肩から拳への向き）: トルクと角度の関係 × 構えのてこ / 今のてこ、を 1 で頭打ち（伸びきりに近いほど、同じトルクで拳を押し出す力は
    /// 大きいので、肘のトルクが落ちても拳を押す力は抜けない。構えを超える分は骨と関節が受け持つ）。同じ拳の速さに要る肘の角速度は
    /// てこの比だけ大きい（速さの尺度は頭打ちにしない）。肩（直交する向き）: 構えの肩から拳の距離 / 今の距離。
    /// </summary>
    public static (float Elbow, float ElbowSpeed, float Shoulder, float ElbowReference, float ShoulderReference) Leverage(in Pose pose, in Constants c)
        => Leverage(pose, c, out _);

    private static (float Elbow, float ElbowSpeed, float Shoulder, float ElbowReference, float ShoulderReference) Leverage(
        in Pose pose, in Constants c, out float bend)
    {
        bend = ElbowBend(pose.Distance, pose.Upper, pose.Lower);
        var reference = ElbowLever(c.ElbowPeak, pose.Upper, pose.Lower, c.MinBend);
        var lever = reference / MathF.Max(1e-6f, ElbowLever(bend, pose.Upper, pose.Lower, c.MinBend));
        var referenceDistance = MathF.Sqrt((pose.Upper * pose.Upper) + (pose.Lower * pose.Lower) + (2f * pose.Upper * pose.Lower * MathF.Cos(c.ElbowPeak)));
        // 拳が肩に近いほど肩の筋のてこは短い（同じトルクで大きな力・速い角速度）。曲げきりの拳（構えの距離の約 0.4 倍）までで数える。
        var shoulder = referenceDistance / MathF.Max(0.4f * referenceDistance, pose.Distance);
        // 拳を押し出す力の上限は構えの力まで: てこで増える分（伸びきりに近い腕を骨で突っ張る力）は、剛体の関節の制限が解く拘束の力として
        // 物理エンジンがもう出している（筋の力として足すと二重になり、押し付けた拳が相手の頭を数倍の力で押して回した）。
        return (MathF.Min(1f, ElbowTorque(bend, c.ElbowPeak, c.ElbowLimit, c.ElbowEndTorque) * lever), lever, shoulder, reference, referenceDistance);
    }

    /// <summary>
    /// <see cref="ForceVelocity"/> の傾き d(力の割合)/d(縮む速さ / 最大の縮む速さ)（いつも 0 以下: 速く縮むほど弱く、速く伸ばされるほど強い）。
    /// </summary>
    public static float ForceVelocitySlope(float shortening, float curvature, float eccentricMax, float eccentricSlope)
    {
        if (!float.IsFinite(shortening))
        {
            return 0f;
        }

        var k = MathF.Max(1e-3f, curvature);
        if (shortening >= 0f)
        {
            var q = 1f + (shortening / k);
            return shortening >= 1f ? 0f : -(1f + (1f / k)) / (q * q);
        }

        var w = -shortening;
        var rise = MathF.Max(0f, eccentricMax - 1f);
        var c = rise / MathF.Max(1e-3f, eccentricSlope * (1f + (1f / k)));
        return c <= 0f ? 0f : -rise * c / ((w + c) * (w + c));
    }

    /// <summary>
    /// 活性化した 6 つのまとまりが拳に出す力（ワールド）。まとまりの引く向き d を、肩から拳への向き（肘の伸ばす・曲げる筋）と直交する向き
    /// （肩の筋）に分け、それぞれに関節の力の出しやすさ（<see cref="Leverage(in Pose, in Constants)"/>）と、関節の縮む速さ（拳の速さ × てこの比 / 最大の縮む速さ）の
    /// 速さの関係を掛けて足す。
    /// <para>速さの関係は拳の速さに対する粘り（傾き b = −dF/dv ≥ 0）を持つ。伸びきりの近くでは関節の角速度へのてこが大きく、粘りはてこの比の 2 乗で
    /// 固くなるので、歩ごとに力が振れないよう、粘りの分は 1 歩先の速さで解く（後退オイラー: F / (1 + b dt / m)、m は拳が引く質量）。
    /// 歩が細かい極限では元の式と同じ。</para>
    /// </summary>
    public static Vector3 Force(ReadOnlySpan<float> activation, Quaternion chest, float maxForce, in Pose pose, in Constants c, float dt, float mass)
        => Force(activation, chest, maxForce, pose, c, dt, mass, out _, out _);

    internal static Vector3 Force(ReadOnlySpan<float> activation, Quaternion chest, float maxForce, in Pose pose, in Constants c, float dt, float mass,
        out float elbowBend, out float elbowShortening)
    {
        var radialForce = 0f;
        var radialDamping = 0f;
        var tangentForce = Vector3.Zero;
        var tangentDamping = 0f;
        var radialSpeed = Vector3.Dot(pose.Velocity, pose.Radial);
        var (elbowStrength, elbowSpeed, shoulderStrength, elbowLever, shoulderLever) = Leverage(pose, c, out elbowBend);
        var elbowMax = c.Tired(c.ElbowSpeed, elbowLever);
        elbowShortening = radialSpeed * elbowSpeed / elbowMax;
        var shoulderMax = c.Tired(c.ShoulderSpeed, shoulderLever);
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
            // 肘の分: 外へ押す筋（s > 0）は拳が外へ動くと縮む。肘の角速度 = 拳の向きの速さ / てこ。
            var ur = MathF.Sign(s) * radialSpeed * elbowSpeed / elbowMax;
            var gain = a * maxForce;
            radialForce += gain * s * elbowStrength * ForceVelocity(ur, c.Curvature, c.EccentricMax, c.EccentricSlope);
            radialDamping -= gain * MathF.Abs(s) * elbowStrength * ForceVelocitySlope(ur, c.Curvature, c.EccentricMax, c.EccentricSlope) * elbowSpeed / elbowMax;
            // 肩の分: 直交する向きへ拳が動けば縮む。肩の角速度 = その速さ / 肩から拳の距離。
            var t = tangent.Length();
            if (t > 1e-6f)
            {
                var ut = Vector3.Dot(pose.Velocity, tangent / t) * shoulderStrength / shoulderMax;
                tangentForce += tangent * (gain * shoulderStrength * ForceVelocity(ut, c.Curvature, c.EccentricMax, c.EccentricSlope));
                tangentDamping -= gain * t * shoulderStrength * ForceVelocitySlope(ut, c.Curvature, c.EccentricMax, c.EccentricSlope) * shoulderStrength / shoulderMax;
            }
        }

        var h = MathF.Max(0f, dt) / MathF.Max(1e-4f, mass);
        return (pose.Radial * (radialForce / (1f + (radialDamping * h)))) + (tangentForce / (1f + (tangentDamping * h)));
    }
}
