using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// バトルロイヤル（3 人以上）で誰を狙うかの重み（教科書・教育の値。<see cref="BotStyle.Rival"/>。docs/plans/VFC_ROYALE_PLAN.md）。
/// 相手の見える特徴の連続の量の重み付きの和（<see cref="RivalChoice.Score"/>）がいちばん大きい相手を狙う。種類での分岐は無い。
/// </summary>
/// <param name="Near">近さの重み（胸と胸の距離を自分の腕の長さで割った値に掛けて引く）。大きいほど近い相手を選ぶ。</param>
/// <param name="Facing">相手がこちらを向いている度合い（相手の向き · 相手から自分への向き、−1..1）の重み。正なら向かってくる相手（脅威）へ向き直り、負なら背中を向けた相手を好む。</param>
/// <param name="Weak">相手の見える傷み（<see cref="Fighter.Injury"/>(felt: false)、0..1）の重み。漁夫の利。</param>
/// <param name="Low">相手の頭の低さ（1 − 頭の高さ / 立ったときの頭の高さ、0..1）の重み。倒れた・崩れた相手を攻める。</param>
/// <param name="Revenge">自分を打ってきた相手への恨み（受けた打撃の強さの和、0..1。<see cref="RevengeHalfLife"/> で減る）の重み。</param>
/// <param name="RevengeHalfLife">恨みが半分に減る時間 [s]。</param>
/// <param name="Stick">今の狙いを続ける重み（選び直しで揺れないための、目を移す手間）。</param>
public sealed record RivalWeights(float Near = 1f, float Facing = 0.3f, float Weak = 1.5f, float Low = 0.6f, float Revenge = 2f, float RevengeHalfLife = 2.5f, float Stick = 0.5f)
{
    /// <summary>教科書の既定。</summary>
    public static RivalWeights Default { get; } = new();

    /// <summary>読めない値は既定へ、範囲の外は丸める。</summary>
    public RivalWeights Validated()
    {
        static float W(float v, float fallback) => float.IsFinite(v) ? Math.Clamp(v, -10f, 10f) : fallback;
        return new RivalWeights(W(Near, 1f), W(Facing, 0.3f), W(Weak, 1.5f), W(Low, 0.6f), W(Revenge, 2f),
            float.IsFinite(RevengeHalfLife) ? Math.Clamp(RevengeHalfLife, 0.1f, 60f) : 2.5f, W(Stick, 0.5f));
    }
}

/// <summary>
/// 狙う相手の候補の、見える特徴（試合が測って渡す。読むだけ）。
/// </summary>
/// <param name="Chest">胸の位置。</param>
/// <param name="Forward">体の向き（水平の単位ベクトル）。</param>
/// <param name="Injury">見える傷み 0..1。</param>
/// <param name="Lowness">頭の低さ 0..1。</param>
/// <param name="Grudge">その相手から受けた打撃の恨み 0..1。</param>
public readonly record struct RivalView(Vector3 Chest, Vector3 Forward, float Injury, float Lowness, float Grudge);

/// <summary>狙う相手を選ぶ（決定的: 同点は並びの前の方）。</summary>
public static class RivalChoice
{
    /// <summary>自分（胸 <paramref name="selfChest"/>・腕の長さ <paramref name="selfReach"/>）から見た候補 <paramref name="rival"/> の点。</summary>
    public static float Score(Vector3 selfChest, float selfReach, in RivalView rival, bool current, RivalWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var toMe = selfChest - rival.Chest;
        toMe.Y = 0f;
        var distance = toMe.Length();
        var facing = distance > 1e-5f ? Vector3.Dot(rival.Forward, toMe / distance) : 1f;
        return (-weights.Near * (distance / MathF.Max(1e-3f, selfReach)))
            + (weights.Facing * facing)
            + (weights.Weak * rival.Injury)
            + (weights.Low * rival.Lowness)
            + (weights.Revenge * rival.Grudge)
            + (current ? weights.Stick : 0f);
    }

    /// <summary>候補の中で点がいちばん大きい添字（候補が無ければ −1）。<paramref name="current"/> は今の狙いの添字（無ければ −1）。</summary>
    public static int Choose(Vector3 selfChest, float selfReach, ReadOnlySpan<RivalView> rivals, int current, RivalWeights weights)
    {
        var best = -1;
        var bestScore = float.NegativeInfinity;
        for (var i = 0; i < rivals.Length; i++)
        {
            var score = Score(selfChest, selfReach, rivals[i], i == current, weights);
            if (score > bestScore)
            {
                best = i;
                bestScore = score;
            }
        }

        return best;
    }
}
