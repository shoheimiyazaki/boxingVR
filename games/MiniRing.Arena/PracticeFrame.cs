using System.Globalization;

namespace MiniRing.Arena;

/// <summary>
/// 練習の共通の枠（docs/plans/MOTION_TOOLS_PLAN.md §D8）: 候補（技の差分）を練習の条件（種・体・相手）で点を付けて選び、選んだ物を
/// 練習に使わなかった条件（held-out）で基準と試行ごとの対で比べ直し（<see cref="PairedEvaluation.Compute"/>）、採否と数字を
/// <see cref="ConfirmLog.Append"/> で docs/arena/confirm.jsonl に残し、前後の motioncard を撮る口を持つ。
/// 点の式は練習ごとに渡す（当たった量 − <see cref="FormCost.Of"/>、自然さの項 <see cref="FormCost.OfNaturalness"/> を足すかは練習が決める）。
/// 枠が決めるのは手順だけで、物理・ボットの判断には触れない（観測と評価の側）。
/// </summary>
/// <typeparam name="TCandidate">候補（例: 鎌の振りの時間と溜め）。</typeparam>
/// <typeparam name="TCase">1 つの条件（例: 相手・種・間合い・狙い）。</typeparam>
internal sealed class PracticeFrame<TCandidate, TCase>
{
    /// <summary>道具の名前（台帳の tool）。</summary>
    public required string Tool { get; init; }

    /// <summary>台帳に書く問い（何を何と比べたか）。</summary>
    public required string Question { get; init; }

    /// <summary>練習の条件（候補を選ぶのに使う）。</summary>
    public required IReadOnlyList<TCase> Train { get; init; }

    /// <summary>比べ直しの条件（選ぶのに使わない: CLAUDE.md §1.8）。</summary>
    public required IReadOnlyList<TCase> HeldOut { get; init; }

    /// <summary>1 つの条件の点（大きいほど良い）。形の代価を引いた後の値を返す。</summary>
    public required Func<TCandidate, TCase, bool, double> Score { get; init; }

    /// <summary>候補の 1 行の説明。</summary>
    public required Func<TCandidate, string> Describe { get; init; }

    /// <summary>台帳の seeds に書く、条件の種（無ければ添字）。</summary>
    public Func<TCase, int>? SeedOf { get; init; }

    /// <summary>前後の motioncard を撮る口（候補・名前の頭）。渡さなければ撮らない。</summary>
    public Action<TCandidate, string>? Card { get; init; }

    /// <summary>台帳に書くか（テストでは書かない）。</summary>
    public bool Log { get; init; } = true;

    /// <summary>
    /// 候補 <paramref name="candidates"/> から練習の条件の点の和で 1 つ選び、基準 <paramref name="baseline"/> と held-out で比べる。
    /// held-out の平均の差が正で、95% の区間が 0 を跨がなければ採る（<see cref="Outcome.Adopt"/>）。
    /// </summary>
    public Outcome Run(IReadOnlyList<TCandidate> candidates, TCandidate baseline, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(log);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("候補が 1 つもありません。", nameof(candidates));
        }

        var c = CultureInfo.InvariantCulture;
        var trained = candidates.Select(x => (Candidate: x, Total: Train.Sum(k => Score(x, k, false)))).ToList();
        foreach (var (x, total) in trained)
        {
            log.WriteLine(string.Create(c, $"practice {Describe(x),-28} score {total,8:F2}"));
        }

        var best = trained.MaxBy(x => x.Total).Candidate;
        var a = HeldOut.Select(k => Score(best, k, true)).ToArray();
        var b = HeldOut.Select(k => Score(baseline, k, true)).ToArray();
        var result = PairedEvaluation.Compute(a, b);
        var adopt = Decide(result);
        log.WriteLine(string.Create(c,
            $"selected {Describe(best)}: held-out {result.MeanA:F2} vs baseline {Describe(baseline)} {result.MeanB:F2} per trial -> {(adopt ? "adopt" : "keep the baseline")} ({result})"));
        if (Log)
        {
            var seeds = HeldOut.Select((k, i) => SeedOf?.Invoke(k) ?? i).ToArray();
            ConfirmLog.Append(Tool, Question, Describe(best), Describe(baseline), seeds, result,
                new Dictionary<string, string> { ["decision"] = adopt ? "adopt" : "keep", ["train"] = Train.Count.ToString(c), ["heldOut"] = HeldOut.Count.ToString(c) });
        }

        if (Card is { } card)
        {
            card(baseline, Tool + ".before");
            card(best, Tool + ".after");
        }

        return new Outcome(best, adopt, result);
    }

    /// <summary>採る条件: held-out の差の平均が正で、区間の下端も 0 以上（偶然の良さで採らない）。</summary>
    public static bool Decide(PairedResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.MeanDiff > 0d && result.CiLow >= 0d;
    }

    /// <summary>選んだ候補・採否・比べ直しの数字。</summary>
    internal sealed record Outcome(TCandidate Selected, bool Adopt, PairedResult HeldOut);
}
