using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 対にした評価（<see cref="PairedEvaluation"/>）・符号検定・t 分布の臨界値・試合の標本（<see cref="MatchSampler"/>）・
/// 点の式の共通化（<see cref="MatchScore"/>、<see cref="DefenseTuner.EvaluateBySeed"/>）の確かめ。
/// </summary>
[TestClass]
public sealed class PairedEvaluationTests
{
    [TestMethod]
    public void SignTest_TenWinsTwoLosses_IsTwoTimesSeventyNineOver4096()
    {
        Assert.AreEqual(2d * 79d / 4096d, SignTest.TwoSidedP(10, 2), 1e-12);
        Assert.AreEqual(0.0386, SignTest.TwoSidedP(10, 2), 5e-4);
        Assert.AreEqual(SignTest.TwoSidedP(10, 2), SignTest.TwoSidedP(2, 10), 1e-15, "両側なので勝ちと負けを入れ替えても同じ");
        Assert.AreEqual(1d, SignTest.TwoSidedP(6, 6), 1e-12);
        Assert.AreEqual(1d, SignTest.TwoSidedP(0, 0), 1e-12, "差のある種が無ければ判断できない");
    }

    [TestMethod]
    public void SignTest_IgnoresTies()
    {
        // 10 勝 2 敗に、引き分けの 5 種を足しても p は同じ。
        var a = Enumerable.Repeat(1d, 10).Concat(Enumerable.Repeat(0d, 2)).Concat(Enumerable.Repeat(0.5d, 5)).ToList();
        var b = Enumerable.Repeat(0d, 10).Concat(Enumerable.Repeat(1d, 2)).Concat(Enumerable.Repeat(0.5d, 5)).ToList();
        var r = PairedEvaluation.Compute(a, b);
        Assert.AreEqual((10, 2, 5), (r.Plus, r.Minus, r.Ties));
        Assert.AreEqual(2d * 79d / 4096d, r.SignP, 1e-12);
    }

    [TestMethod]
    public void StudentT_CriticalValues_MatchTheTable()
    {
        Assert.AreEqual(2.3646, StudentT.Critical(7, 0.05), 1e-3, "n = 8");
        Assert.AreEqual(2.0452, StudentT.Critical(29, 0.05), 1e-3, "n = 30");
        Assert.AreEqual(12.706, StudentT.Critical(1, 0.05), 5e-3);
        Assert.AreEqual(3.4995, StudentT.Critical(7, 0.01), 1e-3);
    }

    [TestMethod]
    public void Compute_KnownValues()
    {
        // d = 1, 2, 3, 4, 5, 6, 7, 8（A − B）: 平均 4.5、標準偏差 √6、標準誤差 √6 / √8。
        var b = Enumerable.Repeat(0d, 8).ToList();
        var a = Enumerable.Range(1, 8).Select(i => (double)i).ToList();
        var r = PairedEvaluation.Compute(a, b);
        Assert.AreEqual(8, r.N);
        Assert.AreEqual(4.5d, r.MeanDiff, 1e-12);
        Assert.AreEqual(Math.Sqrt(6d), r.SdDiff, 1e-12);
        var se = Math.Sqrt(6d) / Math.Sqrt(8d);
        Assert.AreEqual(se, r.StdErr, 1e-12);
        Assert.AreEqual(4.5d - (2.3646d * se), r.CiLow, 2e-3);
        Assert.AreEqual(4.5d + (2.3646d * se), r.CiHigh, 2e-3);
        Assert.IsTrue(r.Significant);
        Assert.AreEqual("A が上", r.Verdict);
        Assert.AreEqual(2d / 256d, r.SignP, 1e-12, "8 種とも A が上: 2 × (1 / 2^8)");
        Assert.AreEqual(0d, r.Rho, "B が一定で相関は定義できない（同じ並びでもない）ので 0");
        Assert.AreEqual((int)Math.Ceiling(Math.Pow(2.5d * Math.Sqrt(6d) / 4.5d, 2d)), r.SamplesFor(2.5d));
    }

    [TestMethod]
    public void Compute_Correlation()
    {
        var a = new[] { 1d, 2d, 3d, 4d };
        Assert.AreEqual(1d, PairedEvaluation.Correlation(a, a.Select(x => (2d * x) + 5d).ToList()), 1e-12);
        Assert.AreEqual(-1d, PairedEvaluation.Correlation(a, a.Select(x => -x).ToList()), 1e-12);
    }

    [TestMethod]
    public void Compute_IdenticalValues_AreNoDifference()
    {
        var points = new[] { 1.5d, -0.25d, 3d, 0.75d, 2d };
        var r = PairedEvaluation.Compute(points, points);
        Assert.AreEqual(5, r.Ties);
        Assert.AreEqual(0d, r.MeanDiff);
        Assert.AreEqual(0d, r.SdDiff);
        Assert.AreEqual(0d, r.CiLow);
        Assert.AreEqual(0d, r.CiHigh);
        Assert.IsFalse(r.Significant);
        Assert.AreEqual("差なし", r.Verdict);
        Assert.AreEqual(0d, r.TValue);
        Assert.IsNull(r.SamplesFor(2.5d));
        Assert.AreEqual(1d, r.Rho, 1e-12);
    }

    [TestMethod]
    public void Race_StopsWhenIntervalExcludesZero_AndSplitsAlphaAcrossLooks()
    {
        // 点 = 種 + (A なら 3)。差は常に 3 に近く（わずかに揺らす）、最初の覗きで止まる。
        double[] A(IReadOnlyList<int> seeds) => seeds.Select(s => (s % 7) + 3d).ToArray();
        double[] B(IReadOnlyList<int> seeds) => seeds.Select(s => (s % 7) + (0.01d * (s % 3))).ToArray();
        var race = PairedEvaluation.Race(A, B, i => i, nMin: 4, k: 4, nMax: 20, boutsPerSample: 12, cores: 12);
        Assert.IsTrue(race.StoppedEarly);
        Assert.AreEqual(1, race.Looks);
        Assert.AreEqual(5, race.PlannedLooks, "4 から 20 まで 4 ずつ = 1 + 4 回");
        Assert.AreEqual(0.05d / 5d, race.Result.Alpha, 1e-15, "各回の有意水準は α / L");
        Assert.AreEqual(4, race.Result.N);

        // 差が無い（同じ点）なら n_max まで測って「差なし」。
        var flat = PairedEvaluation.Race(A, A, i => i, nMin: 4, k: 4, nMax: 12, boutsPerSample: 12, cores: 12);
        Assert.IsFalse(flat.StoppedEarly);
        Assert.AreEqual(12, flat.Result.N);
        Assert.AreEqual(3, flat.Looks);
        Assert.IsFalse(flat.Result.Significant);
    }

    [TestMethod]
    public void MinBatch_KeepsAllCoresBusy()
    {
        Assert.AreEqual(1, PairedEvaluation.MinBatch(12, 12), "試合 12 本の標本なら 1 標本で 12 コアが埋まる");
        Assert.AreEqual(12, PairedEvaluation.MinBatch(1, 12), "ドリルは 1 標本 1 本なので 12 標本ずつ");
        Assert.AreEqual(2, PairedEvaluation.MinBatch(6, 12));
        Assert.AreEqual(2, PairedEvaluation.MinBatch(7, 12));
        var seen = new List<int>();
        double[] Record(IReadOnlyList<int> seeds)
        {
            seen.Add(seeds.Count);
            return seeds.Select(s => (double)s).ToArray();
        }

        PairedEvaluation.Race(Record, Record, i => i, nMin: 4, k: 1, nMax: 40, boutsPerSample: 1, cores: 12);
        Assert.IsTrue(seen.Skip(2).All(c => c >= 12), "k は 12 未満にならない（A と B で 2 回ずつ記録される。最初の n_min は別）");
    }

    private static readonly string[] OpponentNames = ["boxer", "swarmer"];

    private static List<Contender> Opponents() => OpponentNames.Select(Contender.Named).ToList();

    [TestMethod]
    public void MatchSampler_SameResultWhetherSerialOrParallel_AndSameValueIsNoDifference()
    {
        var mine = Contender.Of(BotCatalog.Champion);
        IReadOnlyList<int> seeds = [11, 1011];
        var parallel = new MatchSampler(Opponents(), 2f).Score(mine, seeds);
        var serial = new MatchSampler(Opponents(), 2f, maxParallel: 1).Score(mine, seeds);
        CollectionAssert.AreEqual(parallel, serial, "並列の数に依らず同じ（結果は添字の位置に書く）");

        var r = PairedEvaluation.Compute(parallel, serial);
        Assert.AreEqual(2, r.Ties);
        Assert.AreEqual("差なし", r.Verdict, "同じ値を同じ種で測れば差は必ず 0（決定的）");
    }

    [TestMethod]
    public void MatchSampler_PointIsSharedFormulaOfOneSeed_AndMatchesTrainerEvaluate()
    {
        var mine = Contender.Of(BotCatalog.Champion);
        var opponents = Opponents();
        const int Seed = 23;
        var point = new MatchSampler(opponents, 2f).Score(mine, [Seed])[0];

        // MatchTrainer.Evaluate は同じ種・2 試合なら、同じ点（式を 1 か所にしても結果は変わらない）。
        var trainer = MatchTrainer<Contender>.Evaluate(c => c, mine, opponents, 2, 2f, Seed);
        Assert.AreEqual(trainer.Value, (float)point, 0f);

        // 式そのものを、元の MatchTrainer に書いてあった形で組み直しても同じ。
        var expected = (trainer.Wins - trainer.Losses) + (0.9f * MathF.Tanh((trainer.Dealt - trainer.Taken) / 400f));
        Assert.AreEqual(expected, trainer.Value, 0f);
        Assert.AreEqual(expected, MatchScore.Of(trainer.Wins, trainer.Losses, trainer.Dealt, trainer.Taken), 0f);
    }

    [TestMethod]
    public void MatchTrainerEvaluate_MarksTheName_SoASameNamedOpponentDoesNotMix()
    {
        // 自分も相手も champion（名前が同じ）。印があれば集計が混ざらず、相手は 1 つの行（"champion"）になる。
        var champion = Contender.Of(BotCatalog.Champion);
        var score = MatchTrainer<Contender>.Evaluate(c => c, champion, [champion], 2, 2f, 5);
        Assert.AreEqual(2, score.Wins + score.Losses + score.Draws);
        StringAssert.StartsWith(score.Detail, "champion ");
    }

    [TestMethod]
    public void DefenseTuner_Score_IsTheSameAsSummingDrillRunsDirectly()
    {
        const int Bouts = 2;
        const float Seconds = 2f;
        const int EvalSeed = 7;
        var tuner = new DefenseTuner(Bouts, Seconds, 1, _ => { });
        var book = DefenseBook.Default;
        var kind = PunchKind.Straight;
        var evaluated = tuner.Evaluate(book, Defense.Slip, [kind], EvalSeed);

        // 元の Aggregate と同じ手順（種 = 評価の種 + i × 7919 + 種類 × 31、合計から比）で組み直す。
        var runs = Enumerable.Range(0, Bouts).Select(i =>
        {
            var s = EvalSeed + (i * 7919) + ((int)kind * 31);
            var baseline = DefenseDrill.MeasureBaseline(kind, s, Seconds, DefenseDrill.DefaultRange, false);
            return DefenseDrill.Run(Defense.Slip, kind, book, s, Seconds, baseline, DefenseDrill.DefaultRange, true, false);
        }).ToList();
        var sum = new DrillResult(Defense.Slip, kind, runs.Sum(r => r.Punches), runs.Sum(r => r.Damage), runs.Sum(r => r.BaselinePunches),
            runs.Sum(r => r.BaselineDamage), runs.Sum(r => r.CounterDamage), runs.Sum(r => r.Defenses), runs.Sum(r => r.Failed));
        Assert.AreEqual(sum.Score, evaluated.Score, 0f);
        Assert.AreEqual(sum.Reduction, evaluated.Reduction, 0f);
        Assert.AreEqual(sum.CounterReturn, evaluated.Counter, 0f);

        // 種ごとの結果の配列は、足すと同じ合計になり、種ごとの点は差（比でない）。
        var bySeed = tuner.EvaluateBySeed(book, Defense.Slip, [kind], EvalSeed);
        Assert.AreEqual(1, bySeed.Count);
        Assert.AreEqual(Bouts, bySeed[0].Length);
        Assert.AreEqual(sum.Damage, bySeed[0].Sum(r => r.Damage), 0f);
        var points = DefenseTuner.SeedPoints(bySeed);
        for (var i = 0; i < Bouts; i++)
        {
            Assert.AreEqual(runs[i].BaselineDamage - runs[i].Damage + (0.25d * runs[i].CounterDamage), points[i], 1e-9);
        }
    }
}
