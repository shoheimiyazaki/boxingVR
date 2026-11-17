using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 膝の制御（<see cref="LegCommand.KneeLocal"/>）で蹴る、練習で磨いた膝の通り道（2026-11-17、kneelab 段 2）。時間は 0..1（蹴りの長さに対する割合）。
/// 膝は 抱え（<see cref="ChamberAzimuth"/>・<see cref="ChamberElevation"/>）→ 途中の点（<see cref="MidAzimuth"/>・<see cref="MidElevation"/>）→ 当たる所
/// （<see cref="ImpactAzimuth"/>・<see cref="ImpactElevation"/>・<see cref="ImpactTwist"/>）を、<see cref="ImpactShare"/> の時刻に通り、その後は抱えへ戻る。
/// 膝の曲げは、当たる時刻の <see cref="Lead"/> 前から開き始め、当たる瞬間に最も開き（0）、それ以外は閉じている（<see cref="ClosedBend"/>）。当たった後はすぐ閉じる。
/// 方位は「相手の方（水平）から蹴る脚の外へ」の角、仰角は水平から上への角（股から腿の長さの所）。向きは相手を基準に測るので、体が回っても同じ所に当たる。
/// <see cref="Spin"/>・<see cref="Torso"/> は体の向き・胴のひねりの大きさ（<see cref="KickSpec.TurnProfile"/> の形で、脚の側の腰が前へ）。
/// すべて目標の時間変化で、威力の倍率・場合分けは無い。
/// </summary>
public sealed record KneePath(float ChamberAzimuth, float ChamberElevation, float MidAzimuth, float MidElevation, float ImpactAzimuth, float ImpactElevation,
    float ImpactTwist, float ChamberTwist, float ImpactShare, float Lead, float Spin, float Torso, float ClosedBend = 2.1f)
{
    /// <summary>当たったあと膝を閉じ直す時間（蹴りの長さに対する割合）。</summary>
    public const float CloseShare = 0.08f;

    private static float Smooth(float t) => t * t * (3f - (2f * t));

    private static float Ease(float t) => Smooth(Math.Clamp(t, 0f, 1f));

    /// <summary>膝の曲げの目標 [rad]（時刻 <paramref name="t"/> = 0..1）。</summary>
    public float Bend(float t)
    {
        var open = ImpactShare - Lead;
        if (t < open)
        {
            return ClosedBend;
        }

        if (t <= ImpactShare)
        {
            return ClosedBend * (1f - Ease((t - open) / MathF.Max(1e-3f, Lead)));
        }

        return ClosedBend * Ease((t - ImpactShare) / CloseShare);
    }

    /// <summary>
    /// 脚の指令。<paramref name="aim"/> は相手の狙いの点（体フレーム）。<paramref name="body"/> は自分の脚の寸法。
    /// 当たったあと（<see cref="ImpactShare"/> + 閉じ直す時間より後）はねじりをフリーにする。
    /// </summary>
    public LegCommand Evaluate(Hand leg, float t, Vector3 aim, LegBody body, float footPitch)
    {
        t = Math.Clamp(t, 0f, 1f);
        var side = leg.LocalSide();
        var forward = new Vector3(aim.X - body.Hip.X, 0f, aim.Z - body.Hip.Z);
        forward = forward.LengthSquared() > 1e-8f ? Vector3.Normalize(forward) : Vector3.UnitZ;
        var left = Vector3.Cross(Vector3.UnitY, forward);

        Vector3 Direction(float azimuth, float elevation)
            => Vector3.Normalize((forward * MathF.Cos(azimuth) * MathF.Cos(elevation)) + (left * side * MathF.Sin(azimuth) * MathF.Cos(elevation)) + (Vector3.UnitY * MathF.Sin(elevation)));

        var chamber = Direction(ChamberAzimuth, ChamberElevation);
        var mid = Direction(MidAzimuth, MidElevation);
        var impact = Direction(ImpactAzimuth, ImpactElevation);
        var half = ImpactShare * 0.5f;
        Vector3 axis;
        if (t <= half)
        {
            axis = Turn(chamber, mid, Ease(t / MathF.Max(1e-3f, half)));
        }
        else if (t <= ImpactShare)
        {
            axis = Turn(mid, impact, Ease((t - half) / MathF.Max(1e-3f, ImpactShare - half)));
        }
        else
        {
            axis = Turn(impact, chamber, Ease((t - ImpactShare) / (1f - ImpactShare)));
        }

        // ねじり: 相手の向きを基準に測った角を、体フレームの角へ直す（体が回っても、すねの曲がる面が同じ向きになる）。
        float? twist = null;
        if (t <= ImpactShare + CloseShare)
        {
            var rise = Ease(t / MathF.Max(1e-3f, ImpactShare));
            var angle = ChamberTwist + ((ImpactTwist - ChamberTwist) * rise);
            var normal = Vector3.Transform(LegKnee.BendNormalRef(axis, forward), Quaternion.CreateFromAxisAngle(axis, angle));
            twist = LegKnee.TwistOf(axis, Vector3.UnitZ, normal);
        }

        return LegCommand.Knee(body.Hip + (axis * body.Thigh), twist, Bend(t), Quaternion.CreateFromAxisAngle(Vector3.UnitX, footPitch));
    }

    private static Vector3 Turn(Vector3 from, Vector3 to, float t)
    {
        var v = Vector3.Lerp(from, to, t);
        return v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : from;
    }
}
