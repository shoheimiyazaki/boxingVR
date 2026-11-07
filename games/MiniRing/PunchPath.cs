using System.Numerics;

namespace MiniRing;

/// <summary>
/// 拳の経路（体フレームのローカル、腕の長さ単位で指定）: 構え → 溜め → 狙いの奥、の 2 次ベジェ。
/// 溜めを横に取れば横から回り込み、下に取れば下から突き上げ、取らなければまっすぐ出る。
/// <b>経路を作るだけ</b>で、強さ（体重が乗るか）は決めない — それは <see cref="PunchMechanics"/> が物理で決める。
/// CPU とキーボード操作が共有する。
/// </summary>
public static class PunchPath
{
    /// <summary>溜めの位置。lateral: 0 = まっすぐ … 1 = 大きく横から ／ low: 0 = 構えの高さ … 1 = 大きく下から。</summary>
    public static Vector3 Windup(Hand hand, float lateral, float low, float reach)
        => new Vector3(hand.LocalSide() * (0.2f + (0.6f * lateral)), -0.1f - (0.5f * low), 0.45f - (0.15f * lateral)) * reach;

    /// <summary>狙い <paramref name="aim"/> を体の前から見て <paramref name="overshoot"/>（腕の長さ単位）だけ奥へ: 打ち抜く。</summary>
    public static Vector3 Through(Vector3 aim, float overshoot, float reach)
        => aim + ((aim.LengthSquared() > 1e-8f ? Vector3.Normalize(aim) : Vector3.UnitZ) * (overshoot * reach));

    /// <summary>経路上の点（<paramref name="t"/> = 0 で構え、1 で狙いの奥）。</summary>
    public static Vector3 Point(Vector3 guard, Vector3 windup, Vector3 through, float t)
    {
        var s = Math.Clamp(t, 0f, 1f);
        return ((1f - s) * (1f - s) * guard) + (2f * (1f - s) * s * windup) + (s * s * through);
    }
}
