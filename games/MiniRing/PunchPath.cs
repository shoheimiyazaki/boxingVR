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

    /// <summary>
    /// 拳で打てるいちばん手前 [腕の長さ]（肩の中心から前へ）: 自分の胸の前面（肩の中心から約 0.17 腕）にグローブの厚み（約 0.08 腕）を足した所。
    /// これより手前・後ろの点は、腕を伸ばしても拳の面が向かない（自分の体の中か背中側）。
    /// </summary>
    public const float NearestStrike = 0.25f;

    /// <summary>
    /// 狙いの点を、拳で打てる前の側へ寄せる（前後だけを <see cref="NearestStrike"/> より手前に来ないように。横・高さはそのまま）。
    /// 胸が付くほど詰まると、相手の頭・胴の狙いの点が自分の肩の横・後ろへ回る（頭が肩の上を越えて並ぶ）。その点をそのまま狙うと、経路の打ち抜き
    /// （<see cref="Through"/>: 肩の中心から狙いの向きへ奥へ）が背中側へ伸び、腕が後ろへ回って逆向きに伸びて見えた（2026-11-19、ユーザー指摘。
    /// 名簿の 28 試合 × 40 秒で、拳が肩より後ろへ回った 76 回のうち 61 回はボット自身が目標を背中側に置いていた。寄せた後は 0 回。<c>ArmBehindProbe</c>）。打てない点は狙わず、いちばん近い打てる点（同じ横・高さの胸の前）へ打つ。
    /// <see cref="Through"/> が必ず通すので、ボット・意図操作・古い CPU（<c>AiControl</c>）・従来のキーボード操作が同じ寄せ方になる。肘打ちの狙いも同じ（<c>TechniqueExecution.Elbow</c>）。
    /// </summary>
    public static Vector3 Strikable(Vector3 aim, float reach) => aim with { Z = MathF.Max(aim.Z, NearestStrike * reach) };

    /// <summary>狙い <paramref name="aim"/>（<see cref="Strikable"/> で前へ寄せた点）を体の前から見て <paramref name="overshoot"/>（腕の長さ単位）だけ奥へ: 打ち抜く。</summary>
    public static Vector3 Through(Vector3 aim, float overshoot, float reach)
    {
        var strikable = Strikable(aim, reach);
        return strikable + ((strikable.LengthSquared() > 1e-8f ? Vector3.Normalize(strikable) : Vector3.UnitZ) * (overshoot * reach));
    }

    /// <summary>
    /// 最小躍度の曲線 s(u) = 10u³ − 15u⁴ + 6u⁵（u = 0..1 → 0..1）: 始めと終わりで速さも加速度も 0、速さの山は真ん中（u = 0.5）で平均の 1.875 倍。
    /// 人が手を素早く的へ動かすときの速さの形（Flash &amp; Hogan 1985）。拳の目標をこの時間の関数で動かすと、拳は振り出しの途中で最も速くなる
    /// （等速で動かすと、始めに目標が急に動き出して終わりで急に止まる）。
    /// </summary>
    public static float MinimumJerk(float u)
    {
        var s = Math.Clamp(u, 0f, 1f);
        return s * s * s * (10f + (s * ((6f * s) - 15f)));
    }

    /// <summary>経路上の点（<paramref name="t"/> = 0 で構え、1 で狙いの奥）。</summary>
    public static Vector3 Point(Vector3 guard, Vector3 windup, Vector3 through, float t)
    {
        var s = Math.Clamp(t, 0f, 1f);
        return ((1f - s) * (1f - s) * guard) + (2f * (1f - s) * s * windup) + (s * s * through);
    }
}
