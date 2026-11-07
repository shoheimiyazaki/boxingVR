using System.Numerics;

namespace MiniRing;

/// <summary>体の質量と、重心まわりの慣性（胴のローカル軸: X = 左右軸まわり＝前後の傾き, Y = 体軸まわり＝ひねり, Z = 前後軸まわり＝横の傾き）。</summary>
public readonly record struct BodyInertia(float Mass, Vector3 LocalInertia)
{
    /// <summary>体を高さ <paramref name="height"/>・半径 <paramref name="radius"/> の円柱で近似する。</summary>
    public static BodyInertia Cylinder(float mass, float height, float radius)
    {
        var axial = 0.5f * mass * radius * radius;
        var transverse = mass * ((3f * radius * radius) + (height * height)) / 12f;
        return new BodyInertia(mass, new Vector3(transverse, axial, transverse));
    }
}

/// <summary>
/// パンチの力学（純関数）。ジャブ・ストレート・フック・アッパーを<b>種類で場合分けせず</b>、
/// 拳と体軸の位置関係・力の向きだけで「体重が乗る／手打ちになる」を決める。
/// <para>
/// <b>1. 体の有効質量</b>（<see cref="BodyEffectiveMass"/>）: 自由な剛体の点 r（重心から）に方向 n の撃力を
/// 加えたときに相手が感じる質量 m = 1 / (1/M + (r×n)ᵀ I⁻¹ (r×n))。
/// 力の作用線が体軸（重心）を通れば r×n = 0 で体重 M がそのまま乗り、軸から外れるほど体が回って逃げて小さくなる。
/// 体軸まわりに回す動き（フック）は I_yaw / r² の回転慣性が乗り、軸に近い（コンパクトな）ほど大きい。
/// 体軸方向（突き上げ）は前後に倒す慣性が大きいので重く乗る。
/// </para>
/// <para>
/// <b>2. 腕の連結度</b>（<see cref="Coupling"/>）: 体の質量は腕を通ってしか拳に届かない。
/// 肩→拳の線に沿って押す・引く（骨で突っ張る）成分は丸ごと伝わり、線に直交する成分は肩・肘を固めて
/// 体の回転に乗せた分（てこ）だけ伝わる。関節を痛めているほど固められず手打ちになる。
/// </para>
/// </summary>
public static class PunchMechanics
{
    /// <summary>腕の線に直交する力を、肩・肘を固めて体へ伝えられる割合（健康な関節で）。</summary>
    public const float LeverTransfer = 0.55f;

    /// <summary>この運動量 [kg·m/s] 未満は「触れただけ」。</summary>
    public const float MomentumThreshold = 0.25f;
    public const float DamageScale = 11f;
    public const float MaxDamage = 40f;

    /// <summary>
    /// 体（剛体近似）の点 <paramref name="offsetFromCom"/> に方向 <paramref name="direction"/> の撃力を与えたときの有効質量。
    /// <paramref name="torso"/> は胴の向き（慣性の主軸）。
    /// </summary>
    public static float BodyEffectiveMass(in BodyInertia body, Quaternion torso, Vector3 offsetFromCom, Vector3 direction)
    {
        var n = SafeNormalize(direction);
        var inverse = Quaternion.Conjugate(torso);
        var c = Vector3.Cross(Vector3.Transform(offsetFromCom, inverse), Vector3.Transform(n, inverse));
        var i = body.LocalInertia;
        var rotational = (c.X * c.X / i.X) + (c.Y * c.Y / i.Y) + (c.Z * c.Z / i.Z);
        return 1f / ((1f / body.Mass) + rotational);
    }

    /// <summary>
    /// 腕が体の質量を拳へ伝える割合（0..1）。肩→拳の線との揃い具合 a で
    /// a² + LeverTransfer·(1 − a²)、それに関節の健全度を掛ける。押しても引いても同じ（a²）。
    /// </summary>
    public static float Coupling(Vector3 shoulderToGlove, Vector3 direction, float jointIntegrity)
    {
        var a = Vector3.Dot(SafeNormalize(shoulderToGlove), SafeNormalize(direction));
        var aligned = a * a;
        return (aligned + (LeverTransfer * (1f - aligned))) * Math.Clamp(jointIntegrity, 0f, 1f);
    }

    /// <summary>拳に効く質量 = 腕そのもの + 連結度 × 体の有効質量。</summary>
    public static float ImpactMass(float armMass, float bodyEffectiveMass, float coupling)
        => armMass + (Math.Clamp(coupling, 0f, 1f) * bodyEffectiveMass);

    /// <summary>
    /// 打撃の鋭さ（0..1）: 同じ運動量でも、ゆっくり押し当てたものは衝撃にならない（力が長い時間に分散する）。
    /// 踏み込む速さで滑らかに 0 → 1（smoothstep）。押し合い・寄りかかりをヒットにしないため。
    /// </summary>
    public static float Sharpness(float speed)
    {
        var t = Math.Clamp((speed - SharpnessSpeedLow) / (SharpnessSpeedHigh - SharpnessSpeedLow), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    public const float SharpnessSpeedLow = 0.35f;
    public const float SharpnessSpeedHigh = 0.8f;

    /// <summary>
    /// 腕が出せる力の上限の倍率。体の質量を借りられるほど（連結度 × 有効質量 / 体重）脚・腰の力が乗る。
    /// <paramref name="bodyDrive"/> は体全体で上乗せできる最大の割合。
    /// </summary>
    public static float ForceMultiplier(float coupling, float bodyEffectiveMass, float totalMass, float bodyDrive)
        => 1f + (bodyDrive * Math.Clamp(coupling, 0f, 1f) * Math.Clamp(bodyEffectiveMass / totalMass, 0f, 1f));

    /// <summary>
    /// 衝撃（運動量 p = 有効質量 × 踏み込み速度）からの基本ダメージ。√ で圧縮して、
    /// 手打ちと体重の乗った一撃の差を数倍に収める（10 倍の運動量 ≒ 3 倍強のダメージ）。
    /// </summary>
    public static float Damage(float momentum)
    {
        var over = MathF.Sqrt(MathF.Max(0f, momentum)) - MathF.Sqrt(MomentumThreshold);
        return Math.Clamp(over * DamageScale, 0f, MaxDamage);
    }

    /// <summary>
    /// 両手の位置（体フレームのローカル）から胴のひねり角 [rad]。前に出た手の側の肩が前へ（ストレート）、
    /// 体の中心を横切る手の側の肩が前へ（フック）。右手系なので左手側が +X、ひねり + は右肩が前。
    /// </summary>
    public static float TorsoTwist(Vector3 leftLocal, Vector3 rightLocal, float reach, float maxRadians)
    {
        var r = MathF.Max(1e-3f, reach);
        var reachDifference = (rightLocal.Z - leftLocal.Z) / r;   // 右が前に出る → 右肩を前へ（+）
        var crossing = (rightLocal.X + leftLocal.X) / r;         // 右手が左（+X）へ横切る → 右肩を前へ（+）
        return maxRadians * MathF.Tanh((0.9f * reachDifference) + (0.6f * crossing));
    }

    private static Vector3 SafeNormalize(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.UnitZ;
}
