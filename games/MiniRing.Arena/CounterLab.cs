using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 回避からのカウンターの研究。回避技 × 来る拳の種類ごとに、打ち返しの型と打ち始め（回避の途中か、終えてからか）を総当たりで試す。
/// <list type="number">
/// <item>Plan: 候補の型（<see cref="Candidates"/>）× 打ち始め（<see cref="Timings"/>）と、いまの決まった返し（守り終えてから）。</item>
/// <item>Do: 同じ種のドリルで、打ち返す受け手と打ち返さない受け手を比べる。</item>
/// <item>Check: 点 = (カウンターのダメージ − 打ち返したせいで余計に受けたダメージ) ÷ 攻め手の拳の数。上位 3 つを、別の種・倍の本数で測り直す。</item>
/// <item>Act: 測り直しでも、いまの決まった返しを上回ったときだけ本に書く。</item>
/// </list>
/// </summary>
internal sealed class CounterLab(int bouts, float seconds, int seed, IReadOnlyList<float> ranges, Action<string> log, bool combos = false)
{
    /// <summary>打ち返しの候補（単発と 2 連打）。</summary>
    public static IReadOnlyList<string> Candidates { get; } = ["1", "2", "3", "4", "5", "6", "1b", "3b", "1-2", "2-3", "3-2", "6-3"];

    /// <summary>打ち始め（回避の進み具合。1 は回避を終えてから）。</summary>
    public static IReadOnlyList<float> Timings { get; } = [0.35f, 0.65f, 1f];

    private const string Legacy = "（決まった返し）";

    private readonly ConcurrentDictionary<(Defense, PunchKind, int, float), float> _noCounterDamage = new();
    private readonly ConcurrentDictionary<(PunchKind, int, float), DefenseDrill.Baseline> _baselines = new();

    public DefenseBook Run(DefenseBook book, IReadOnlyList<Defense> evasions)
    {
        var updated = book;
        foreach (var evasion in evasions)
        {
            foreach (var kind in DefenseTuner.Targets(evasion).Where(k => k != PunchKind.Body))
            {
                updated = Study(updated, evasion, kind);
            }
        }

        return updated;
    }

    private DefenseBook Study(DefenseBook book, Defense evasion, PunchKind kind)
    {
        var options = Candidates.SelectMany(c => Timings.Select(t => (Combo: c, At: t))).Prepend((Combo: Legacy, At: 1f)).ToList();
        var scored = options.Select(o => (o.Combo, o.At, Result: Measure(book, evasion, kind, o.Combo, o.At, seed, bouts)))
            .OrderByDescending(o => o.Result.Score).ToList();
        var legacy = scored.Single(o => o.Combo == Legacy);

        // 上位 3 つを別の種・倍の本数で測り直して 1 つ選び、選んだものと決まった返しを、さらに別の種で比べる
        // （同じ測り直しで選んで比べると、3 つの最良を取るぶん偶然が有利になる。レビュー指摘）。
        var checkSeed = seed + 31337;
        var finalists = scored.Where(o => o.Combo != Legacy).Take(3)
            .Select(o => (o.Combo, o.At, Result: Measure(book, evasion, kind, o.Combo, o.At, checkSeed, bouts * 2))).ToList();
        var best = finalists.OrderByDescending(o => o.Result.Score).First();
        var confirmSeed = seed + 62417;
        var bestConfirm = Measure(book, evasion, kind, best.Combo, best.At, confirmSeed, bouts * 2);
        var legacyCheck = Measure(book, evasion, kind, Legacy, 1f, confirmSeed, bouts * 2);
        var adopt = bestConfirm.Score > legacyCheck.Score;

        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"#### {evasion} × {kind}"));
        sb.AppendLine();
        sb.AppendLine("| 順位 | 打ち返し | 打ち始め | 点 | カウンター/回 | 余計な被ダメージ/回 | カウンターの命中率 |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        for (var i = 0; i < Math.Min(6, scored.Count); i++)
        {
            Row(sb, (i + 1).ToString(CultureInfo.InvariantCulture), scored[i].Combo, scored[i].At, scored[i].Result);
        }

        Row(sb, "基準", Legacy, 1f, legacy.Result);
        sb.AppendLine();
        var finalistText = string.Join(" / ", finalists.Select(f => string.Create(CultureInfo.InvariantCulture, $"{f.Combo}@{f.At:0.00} {f.Result.Score:0.00}")));
        var verdict = adopt ? $"**{best.Combo} を{Timing(best.At)}採用**" : "決まった返しのまま";
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"選抜（別の種・{bouts * 2} 本）: {finalistText} → 確認（さらに別の種）: {best.Combo} {bestConfirm.Score:0.00} 対 決まった返し {legacyCheck.Score:0.00} → {verdict}"));
        sb.AppendLine();
        log(sb.ToString());

        if (!adopt)
        {
            return book;
        }

        var move = book.Of(evasion);
        move = kind switch
        {
            PunchKind.Straight => move with { CounterStraight = best.Combo, CounterStraightAt = best.At },
            PunchKind.Hook => move with { CounterHook = best.Combo, CounterHookAt = best.At },
            _ => move with { CounterUppercut = best.Combo, CounterUppercutAt = best.At },
        };
        return book.With(evasion, move.Validated());
    }

    private static void Row(StringBuilder sb, string rank, string combo, float at, Outcome r)
        => sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| {rank} | {combo} | {Timing(at)} | {r.Score:0.00} | {r.CounterPerEvasion:0.0} | {r.ExtraTakenPerEvasion:0.0} | {r.LandedShare:P0} |"));

    private static string Timing(float at) => at >= 1f ? "回避の後で" : string.Create(CultureInfo.InvariantCulture, $"回避の {at:P0} で");

    /// <summary>1 つの打ち返しの成績。<paramref name="combo"/> が <see cref="Legacy"/> なら、いまの決まった返し（守り終えてから）。</summary>
    private Outcome Measure(DefenseBook book, Defense evasion, PunchKind kind, string combo, float at, int evalSeed, int count)
    {
        var move = book.Of(evasion);
        // 調べている拳の種類の欄だけを差し替える（ほかの種類で採用済みの返しは、候補にも基準にも同じように残す）。
        var chosen = combo == Legacy ? "" : combo;
        var plan = kind switch
        {
            PunchKind.Straight => move with { CounterStraight = chosen, CounterStraightAt = at },
            PunchKind.Hook => move with { CounterHook = chosen, CounterHookAt = at },
            _ => move with { CounterUppercut = chosen, CounterUppercutAt = at },
        };
        var trial = book.With(evasion, plan);
        var runs = new (DrillResult Result, float NoCounter)[count * ranges.Count];
        Parallel.For(0, runs.Length, j =>
        {
            var range = ranges[j / count];
            var s = evalSeed + ((j % count) * 7919) + ((int)kind * 31);
            var baseline = _baselines.GetOrAdd((kind, s, range), key => DefenseDrill.MeasureBaseline(key.Item1, key.Item2, seconds, key.Item3, combos));
            var noCounter = _noCounterDamage.GetOrAdd((evasion, kind, s, range),
                key => DefenseDrill.Run(key.Item1, key.Item2, book, key.Item3, seconds, baseline, key.Item4, counters: false, combos).Damage);
            runs[j] = (DefenseDrill.Run(evasion, kind, trial, s, seconds, baseline, range, counters: true, combos), noCounter);
        });

        var punches = Math.Max(1, runs.Sum(r => r.Result.Punches));
        var evasions = Math.Max(1, runs.Sum(r => r.Result.Defenses));
        var counter = runs.Sum(r => r.Result.CounterDamage);
        var extra = runs.Sum(r => r.Result.Damage - r.NoCounter);
        var thrown = runs.Sum(r => r.Result.CountersThrown);
        var landed = runs.Sum(r => r.Result.CountersLanded);
        return new Outcome((counter - extra) / punches, counter / evasions, extra / evasions, thrown > 0 ? (float)landed / thrown : 0f);
    }

    /// <param name="Score">(カウンターのダメージ − 余計に受けたダメージ) ÷ 攻め手の拳の数。</param>
    /// <param name="CounterPerEvasion">回避 1 回あたりのカウンターのダメージ。</param>
    /// <param name="ExtraTakenPerEvasion">回避 1 回あたり、打ち返したせいで余計に受けたダメージ（負なら、打ち返したほうが打たれなかった）。</param>
    /// <param name="LandedShare">打ったカウンターの拳のうち当たった割合。</param>
    private readonly record struct Outcome(float Score, float CounterPerEvasion, float ExtraTakenPerEvasion, float LandedShare);
}
