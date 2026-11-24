using System.Numerics;

namespace MiniRing;

/// <summary>
/// 脚の指令（肩中心の体フレーム）。足の運びの外で脚を動かす入力（蹴り・踏み下ろし）の契約。姿勢・位置は直接書き込まない。
/// <para>
/// 2 つの制御の形がある。<see cref="KneeLocal"/> が null なら足首の目標（<see cref="TargetLocal"/>。踏み下ろしなど）。
/// null でなければ <b>膝の制御</b>（蹴り。2026-11-17）: 膝の位置にバネの制御点を置き、
/// 膝の位置（<see cref="KneeLocal"/>）・腿の軸（股 → 膝）まわりのねじり（<see cref="KneeTwist"/>）・膝の曲げ伸ばし（<see cref="KneeBend"/>）の 3 つだけで脚を動かす。
/// すねは膝の曲げ伸ばしの 1 次元しか入力できず、足首は引かない（すねの先は膝の曲げと慣性で振られる）。
/// </para>
/// <para>
/// <see cref="KneeTwist"/> [rad]: 0 = すねを曲げると足が体の後ろ側へ行く向き（体フレームの後ろを腿の軸に垂直な面へ落とした向きが曲げの法線）。
/// 腿の軸まわりに右手系で正。null = フリー（股のサーボはねじりにトルクを掛けない）。
/// <see cref="KneeBend"/> [rad]: 0 = 伸ばしきり、<see cref="LegIk.MaxKnee"/> まで。null = フリー（膝のサーボは今の角度を目標にし、バネは効かずダンパだけ）。
/// 膝の制御のとき <see cref="TargetLocal"/> は膝の位置（読む側の便宜）、<see cref="KneeTargetLocal"/> は使わない。
/// </para>
/// </summary>
public readonly record struct LegCommand(Vector3 TargetLocal, Vector3? KneeTargetLocal = null, Quaternion? Orientation = null, float Spring = 1f,
    Vector3? KneeLocal = null, float? KneeTwist = null, float? KneeBend = null)
{
    /// <summary>膝の制御の指令（足の向きの目標 <paramref name="foot"/> は足首の曲げだけが使う）。</summary>
    public static LegCommand Knee(Vector3 knee, float? twist, float? bend, Quaternion? foot = null)
        => new(knee, null, foot, 1f, knee, twist, bend);

    /// <summary>膝の制御か（false なら足首の目標）。</summary>
    public bool IsKneeControl => KneeLocal.HasValue;
}

/// <summary>
/// 体フレーム（肩中心・向きヨー）での、片脚の寸法と立った形の膝の位置。蹴りの経路を、体の寸法から出すための読み取り専用の情報
/// （人が自分の脚の長さを知っているのと同じ）。<paramref name="Hip"/> は腿が回る股の中心、<paramref name="RestKnee"/> は脚を立てた形での膝。
/// </summary>
public readonly record struct LegBody(Vector3 Hip, Vector3 RestKnee, float Thigh, float Shin, bool Available = true)
{
    /// <summary>
    /// 体の寸法が分からないとき（<see cref="FighterSense.SelfLeg"/> が null）の見積もり: 腕の長さ <paramref name="reach"/> に対する比
    /// （基準体格の脚を測った値: <c>LegBodyTests</c>）。
    /// </summary>
    public static LegBody FromReach(Hand leg, float reach)
        => new(new Vector3(0f, -0.80f * reach, -0.04f * reach), new Vector3(leg.LocalSide() * 0.15f * reach, -1.50f * reach, -0.05f * reach), 0.72f * reach, 0.66f * reach);
}

/// <summary>膝の制御の幾何（純関数。入力元と体が同じ定義を使う）。</summary>
public static class LegKnee
{
    /// <summary>腿の軸 <paramref name="axis"/>（単位）まわりのねじり 0 のときの、すねが曲がる向き（体の後ろを軸に垂直な面へ落とした向き）。</summary>
    public static Vector3 BendNormalRef(Vector3 axis, Vector3 forward)
    {
        var back = -forward;
        var n = back - (Vector3.Dot(back, axis) * axis);
        if (n.LengthSquared() < 1e-8f)
        {
            n = Vector3.UnitY - (Vector3.Dot(Vector3.UnitY, axis) * axis);
        }

        return n.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(n);
    }

    /// <summary>曲げの法線 <paramref name="normal"/>（軸に垂直）のねじり角 [rad]（<see cref="BendNormalRef"/> から軸まわりに右手系で測る）。</summary>
    public static float TwistOf(Vector3 axis, Vector3 forward, Vector3 normal)
    {
        var reference = BendNormalRef(axis, forward);
        var n = normal - (Vector3.Dot(normal, axis) * axis);
        if (n.LengthSquared() < 1e-10f)
        {
            return 0f;
        }

        n = Vector3.Normalize(n);
        return MathF.Atan2(Vector3.Dot(axis, Vector3.Cross(reference, n)), Vector3.Dot(reference, n));
    }

    /// <summary>
    /// すねの向きが <paramref name="shinDirection"/>（単位）になるための、腿の軸 <paramref name="axis"/> のねじり角（曲げの面がその向きを含む）と、膝の曲げ。
    /// 曲げが 0 なら向きは決まらない（ねじり 0）。
    /// </summary>
    public static (float Twist, float Bend) ForShin(Vector3 axis, Vector3 forward, Vector3 shinDirection)
    {
        var along = Math.Clamp(Vector3.Dot(axis, shinDirection), -1f, 1f);
        var bend = MathF.Acos(along);
        var normal = shinDirection - (along * axis);
        return (normal.LengthSquared() < 1e-10f ? 0f : TwistOf(axis, forward, normal), bend);
    }

    /// <summary>角度を (−π, π] へ。</summary>
    public static float Wrap(float angle)
    {
        var a = (angle + MathF.PI) % (2f * MathF.PI);
        if (a < 0f)
        {
            a += 2f * MathF.PI;
        }

        return a - MathF.PI;
    }
}
