using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 防御技ごとの最適化（防御ドリルで PDCA を回す）。1 つの技について、その技が受け持つ拳の種類だけで点を測り、
/// 動き（<see cref="DefenseMove"/>）の値を少しずつ変えた候補から良いものを選ぶ。
/// <list type="number">
/// <item>Plan: いまの動きから、値を少しずつ変えた候補を作る（変えた値を記録）。</item>
/// <item>Do: 全候補に同じ種で同じドリルをさせる。</item>
/// <item>Check: いちばん良い候補を、<b>別の種</b>で今の動きと比べ直す。</item>
/// <item>Act: 別の種でも上回ったときだけ採用する。</item>
/// </list>
/// 最後に、全技 × 全種類の表（効き目）を測り、守りを選ぶときの重み（<see cref="DefenseMove.VsStraight"/> など）にする。
/// </summary>
/// <param name="bouts">1 つの評価で行うドリルの本数（間合いごと・種を変えて）。</param>
/// <param name="seconds">1 本のドリルの長さ [s]。</param>
/// <param name="seed">乱数の種（変異と評価の種の元）。</param>
/// <param name="log">PDCA の記録の書き出し先。</param>
/// <param name="ranges">ドリルの間合い（腕の長さ倍）。複数なら全部で測って合わせる。</param>
/// <param name="counters">受け手が守り切ったら打ち返すか。偽なら回避そのものの性能（被ダメージの減り）だけで点を付ける。</param>
/// <param name="combos">ドリルの攻め手が連打を打つか（偽なら単発）。</param>
internal sealed class DefenseTuner(int bouts, float seconds, int seed, Action<string> log, IReadOnlyList<float>? ranges = null, bool counters = true, bool combos = false)
{
    private readonly Mutation _mutation = new(seed);
    private readonly IReadOnlyList<float> _ranges = ranges ?? [DefenseDrill.DefaultRange];
    private readonly ConcurrentDictionary<(PunchKind, int, float), DefenseDrill.Baseline> _baselines = new();

    /// <summary>
    /// 技が受け持つ拳の種類（ボクシングでの使いどころ）。最適化はこの種類の点の平均で行う。
    /// ボディ打ちは受け持たせない: 構えたままの拳がもともと胴を覆っていて、どの技でも動けば覆いが外れて被ダメージが増えた
    /// （防御ドリル D3: 守らない受け手は 181 発中 136 発をグローブで受けた。ブロックで 1.6 倍、バックステップで 2 倍に増えた）。
    /// </summary>
    public static IReadOnlyList<PunchKind> Targets(Defense defense) => defense switch
    {
        Defense.Duck => [PunchKind.Straight, PunchKind.Hook],
        Defense.Weave => [PunchKind.Hook, PunchKind.Straight],
        Defense.Slip => [PunchKind.Straight],
        Defense.SlipIn => [PunchKind.Straight],
        Defense.Roll => [PunchKind.Hook, PunchKind.Straight],
        _ => [PunchKind.Straight, PunchKind.Hook, PunchKind.Uppercut],
    };

    public DefenseBook Optimize(DefenseBook start, IReadOnlyList<Defense> techniques, int generations, int population)
    {
        var book = start;
        foreach (var defense in techniques)
        {
            book = book.With(defense, OptimizeOne(book, defense, generations, population));
        }

        return book;
    }

    private DefenseMove OptimizeOne(DefenseBook book, Defense defense, int generations, int population)
    {
        var kinds = Targets(defense);
        var current = book.Of(defense);
        var startScore = Evaluate(book, defense, kinds, seed);
        log(string.Create(CultureInfo.InvariantCulture,
            $"### {defense}（相手の拳: {string.Join(", ", kinds)}）\n\n開始の点 {startScore.Score:0.000}（被ダメージ減 {startScore.Reduction:P0}、カウンター {startScore.Counter:P0}）\n"));
        for (var generation = 1; generation <= generations; generation++)
        {
            var genSeed = seed + (generation * 211);
            var incumbent = Evaluate(book.With(defense, current), defense, kinds, genSeed);
            var candidates = Enumerable.Range(0, population).Select(_ => Mutate(current, defense)).ToList();
            var scored = candidates.Select(c => (c.Move, c.Changes, Result: Evaluate(book.With(defense, c.Move), defense, kinds, genSeed)))
                .OrderByDescending(c => c.Result.Score).ToList();
            var best = scored[0];
            var sb = new StringBuilder();
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"- 世代 {generation}: 今 {incumbent.Score:0.000} / 最良の候補 {best.Result.Score:0.000}（{best.Changes}）"));
            if (best.Result.Score > incumbent.Score)
            {
                var checkSeed = seed + 90000 + (generation * 17);
                var recheck = Evaluate(book.With(defense, best.Move), defense, kinds, checkSeed);
                var recheckIncumbent = Evaluate(book.With(defense, current), defense, kinds, checkSeed);
                sb.Append(string.Create(CultureInfo.InvariantCulture, $" → 別の種で {recheck.Score:0.000} 対 今 {recheckIncumbent.Score:0.000}"));
                if (recheck.Score > recheckIncumbent.Score)
                {
                    current = best.Move;
                    sb.Append(" → 採用");
                }
                else
                {
                    sb.Append(" → 不採用");
                }
            }
            else
            {
                sb.Append(" → 据え置き");
            }

            log(sb.ToString());
        }

        var final = Evaluate(book.With(defense, current), defense, kinds, seed);
        log(string.Create(CultureInfo.InvariantCulture,
            $"\n最終の点 {final.Score:0.000}（被ダメージ減 {final.Reduction:P0}、カウンター {final.Counter:P0}）。動き: {Describe(current)}"));

        // 最後の確認: 採用を重ねた結果が、まだ見ていない種でも出発点を上回るか。世代ごとの確認を通っても、
        // 小さな改善の積み重ねが偶然だけで出来ていることがある（防御編の作り直しで、スリップが 0.30 → 0.18 に悪化した）。
        var finalSeed = seed + 777_000;
        var startCheck = Evaluate(book, defense, kinds, finalSeed);
        var endCheck = Evaluate(book.With(defense, current), defense, kinds, finalSeed);
        var keep = endCheck.Score > startCheck.Score;
        log(string.Create(CultureInfo.InvariantCulture,
            $"最後の確認（別の種）: 出発点 {startCheck.Score:0.000} 対 最適化後 {endCheck.Score:0.000} → {(keep ? "採用" : "出発点に戻す")}。\n"));
        return keep ? current : book.Of(defense);
    }

    /// <summary>全技 × 全種類のドリル。効き目の表として、本の Vs* に書き戻す（被ダメージがほとんど減らない組は 0 = 使わない）。</summary>
    public (DefenseBook Book, IReadOnlyList<DrillResult> Results) Matrix(DefenseBook book, int matrixSeed)
    {
        var cells = DefenseBook.Techniques.SelectMany(d => Enum.GetValues<PunchKind>().Select(k => (d, k))).ToList();
        var results = new DrillResult[cells.Count];
        Parallel.For(0, cells.Count, i => results[i] = Aggregate(book, cells[i].d, cells[i].k, matrixSeed));
        var updated = book;
        foreach (var d in DefenseBook.Techniques)
        {
            float Vs(PunchKind k)
            {
                var r = results.Single(x => x.Defense == d && x.Kind == k);
                return r.Reduction < 0.05f ? 0f : Math.Clamp(r.Score, 0.05f, 1f);
            }

            updated = updated.With(d, updated.Of(d) with
            {
                VsStraight = Vs(PunchKind.Straight),
                VsHook = Vs(PunchKind.Hook),
                VsUppercut = Vs(PunchKind.Uppercut),
                VsBody = Vs(PunchKind.Body),
            });
        }

        return (updated, results);
    }

    public static string MatrixTable(IReadOnlyList<DrillResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| 防御技 | ストレート | フック | アッパー | ボディ |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var d in DefenseBook.Techniques)
        {
            sb.Append(CultureInfo.InvariantCulture, $"| {d} |");
            foreach (var k in Enum.GetValues<PunchKind>())
            {
                var r = results.Single(x => x.Defense == d && x.Kind == k);
                sb.Append(CultureInfo.InvariantCulture, $" {r.Reduction:P0} / {r.CounterReturn:P0} |");
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("各欄は「被ダメージの減り / カウンターの取り返し」（守らない受け手と同じ種で比べた値）。");
        return sb.ToString();
    }

    private (float Score, float Reduction, float Counter) Evaluate(DefenseBook book, Defense defense, IReadOnlyList<PunchKind> kinds, int evalSeed)
    {
        var results = kinds.Select(k => Aggregate(book, defense, k, evalSeed)).ToList();
        return (results.Average(r => r.Score), results.Average(r => r.Reduction), results.Average(r => r.CounterReturn));
    }

    /// <summary>同じ技・種類のドリルを <c>bouts</c> 本（種を変えて）並列に行い、合計した結果。</summary>
    private DrillResult Aggregate(DefenseBook book, Defense defense, PunchKind kind, int evalSeed)
    {
        var runs = new DrillResult[bouts * _ranges.Count];
        Parallel.For(0, runs.Length, j =>
        {
            var i = j % bouts;
            var range = _ranges[j / bouts];
            var s = evalSeed + (i * 7919) + ((int)kind * 31);
            var baseline = _baselines.GetOrAdd((kind, s, range), key => DefenseDrill.MeasureBaseline(key.Item1, key.Item2, seconds, key.Item3, combos));
            runs[j] = DefenseDrill.Run(defense, kind, book, s, seconds, baseline, range, counters, combos);
        });
        return new DrillResult(defense, kind, runs.Sum(r => r.Punches), runs.Sum(r => r.Damage), runs.Sum(r => r.BaselinePunches),
            runs.Sum(r => r.BaselineDamage), runs.Sum(r => r.CounterDamage), runs.Sum(r => r.Defenses), runs.Sum(r => r.Failed));
    }

    private (DefenseMove Move, string Changes) Mutate(DefenseMove parent, Defense defense)
    {
        _mutation.Begin();
        var move = _mutation.Move("", parent, scale: 1f, chance: 0.4f, handsFixed: defense == Defense.Block);
        return (move, _mutation.Changes);
    }

    public static string Describe(DefenseMove m) => string.Create(CultureInfo.InvariantCulture,
        $"頭 ({m.HeadX:0.00}→{m.EndX:0.00}, {m.HeadY:0.00}, {m.HeadZ:0.00}) 時間 {m.Duration:0.00}s 足 (前後 {m.Move:0.00}, 横 {m.Step:0.00}) 頭の速さ {m.Rate:0} 拳を上げる {m.HandsUp:0.00}");
}
