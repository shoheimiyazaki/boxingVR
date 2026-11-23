using System.Text.Json;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 拳のドリルの口（攻め手の差し替え・打ち返しのダメージ）と、cmatune（CMA-ES をドリルにつなぐ）の確かめ。
/// 点の良し悪しは見ない（長い測定は別）。決定的であること（並列の数・再開）と、引数の検査を確かめる。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class CmaTuneTests
{
    private static BotStyle Defender(bool counters) => DefenseDrill.Defender(BotCatalog.Champion.Defenses, defend: true, DefenseDrill.DefaultRange, counters);

    [TestMethod]
    public void Punch_WithoutAttackerStyle_IsTheSameAsPassingTheDefaultAttacker()
    {
        var plain = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: true), seed: 5, seconds: 10.07f, cue: 0.06714f);
        var explicitAttacker = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: true), seed: 5, seconds: 10.07f, cue: 0.06714f,
            attackerStyle: DefenseDrill.PunchAttacker("2", DefenseDrill.DefaultRange));
        Assert.AreEqual(plain, explicitAttacker, "攻め手を渡さない呼び出しは、既定の攻め手を渡した呼び出しと同じ結果");
        Assert.IsGreaterThan(0, plain.Punches);
    }

    [TestMethod]
    public void Punch_ChangingTheAttackersPunchBook_ChangesTheResult()
    {
        var cross = PunchBook.Default.Cross;
        var changed = PunchBook.Default.With(Technique.Cross, cross with { Duration = 0.4196f, Lateral = 0.4f });
        var attacker = DefenseDrill.PunchAttacker("2", DefenseDrill.DefaultRange) with { Punches = changed };
        var plain = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: true), seed: 5, seconds: 13.43f, cue: 0.06714f);
        var other = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: true), seed: 5, seconds: 13.43f, cue: 0.06714f, attackerStyle: attacker);
        Assert.AreNotEqual(plain, other, "攻め手の技の表が違えば、同じ種でも軌道が分かれて結果が変わる");
    }

    [TestMethod]
    public void Punch_CounterDamage_IsPositiveWithCountersAndZeroWithout()
    {
        // 種 6: スタミナの容量 120 J（PhysicsVersion 37）の後、種 5 では打ち返しが一度も出なくなった（両方 1.3 = 体の触れ合いだけ）。
        // 打ち返さない受け手でも、踏み込んだ攻め手の顔が受け手の構えた拳に触れて小さなダメージが出ることがある（種 2・3・7 で 1.3〜1.8）ので、
        // 「0」ではなく打ち返しより桁で小さいことを確かめる。
        // 体の運び（PhysicsVersion 45、2026-11-18）: 当たる速さが相対速度になったので、構えた受け手の拳へ攻め手が踏み込む接触
        // （攻め手のガード・肘に 1 回 1.0〜1.4 点、押し出し 0 で腕だけの質量 0.33 kg）がきちんと数えられる。種 6 の 14 秒（人形の単位。いまは 23.5 秒）で
        // 版 43 は 打ち返す受け手 120.4 点・打ち返さない受け手 0.0 点（歩の後の拳の速さで数えていたので動かない拳は 0）、
        // いまは 打ち返す 68.9 点・打ち返さない 9.3 点（9 回）。比は 0.135（7.4 倍）なので、
        // 「桁で小さい」の 0.05 を「打ち返さない受け手は打ち返す受け手の 5 分の 1 未満」（0.2）にした。受け手が拳を打たなければ、
        // 点は攻め手が自分から踏み込んだ接触の分だけで、打ち返しの分は増えない。
        // 拳の経路（LogicVersion 58、2026-11-20）: 攻め手の右ストレートが速く重くなり（打ち出しの最高速 2.0 → 3.4 m/s）、ドリルの 14 秒のうちに受け手が倒れて
        // 終わる種が増えた（打った本数 4〜14）。打ち返しの機会が減り、種 6 だけでは 打ち返す 17.0 点・打ち返さない 6.9 点（比 0.41）になった。
        // 種 1〜8 では、打ち返す受け手の点は打ち返さない受け手以上（種 1 は 4 本で倒れて同点）で、合計 213.9 点 対 78.2 点（比 0.37）。
        // 1 つの種の比は倒れる時刻で揺れるので、種 1〜8 の合計で「打ち返さない受け手は打ち返す受け手の半分未満」を見る。
        var seeds = Enumerable.Range(1, 8).ToArray();
        var withRuns = new PunchDrillResult[seeds.Length];
        var withoutRuns = new PunchDrillResult[seeds.Length];
        Parallel.For(0, seeds.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            withRuns[i] = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: true), seed: seeds[i], seconds: 23.5f, cue: 0.06714f);
            withoutRuns[i] = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: false), seed: seeds[i], seconds: 23.5f, cue: 0.06714f);
        });
        var withCounters = withRuns.Sum(r => r.CounterDamage);
        var without = withoutRuns.Sum(r => r.CounterDamage);
        Console.WriteLine($"counter damage (seeds 1-8): with {withCounters:0.0}, without {without:0.0}");
        Assert.IsGreaterThan(40f, withCounters, "守って打ち返す受け手は、攻め手にダメージを与える");
        Assert.IsTrue(Enumerable.Range(0, seeds.Length).All(i => withRuns[i].CounterDamage >= withoutRuns[i].CounterDamage), "どの種でも、打ち返す受け手の点は打ち返さない受け手以上");
        Assert.IsLessThan(0.5f * withCounters, without, "打ち返さない受け手は、踏み込まれた触れ合いの分だけ（打ち返しの半分未満）");
    }

    [TestMethod]
    public void PunchDrillResult_AddsCounterAndTorsoDamage_AndEmptyIsZero()
    {
        var a = new PunchDrillResult(1, 1, 0, 0, 0, 0, 0, 2f, 3f, 4f);
        var b = new PunchDrillResult(2, 1, 1, 0, 0, 0, 0, 1f, 1f, 1f);
        var sum = a + b;
        Assert.AreEqual((4f, 5f), (sum.CounterDamage, sum.TorsoDamage));
        Assert.AreEqual(a, PunchDrillResult.Empty + a);
        Assert.AreEqual((2f + 4f - 3f) / 2f, a.AttackPoint(2f), 1e-6f);
    }

    [TestMethod]
    public void DrillMeter_CountsPunchAndBaseline()
    {
        var before = DrillMeter.Total;
        DefenseDrill.Punch("2", Defense.Slip, Defender(counters: false), seed: 1, seconds: 3.357f, cue: 0.06714f);
        DefenseDrill.MeasureBaseline(PunchKind.Straight, 1, 3.357f);
        var after = DrillMeter.Total;
        // 他のテストと並列に動くので、少なくとも自分の分は増えている（等号は主張しない）。
        Assert.IsGreaterThanOrEqualTo(2L, after.Calls - before.Calls);
        const double RoundingToleranceSeconds = 1e-9;
        Assert.IsGreaterThanOrEqualTo(6.714d - RoundingToleranceSeconds, after.Seconds - before.Seconds);
    }

    [TestMethod]
    public void DefenseSet_KnownPunchesHaveFixedSets_OthersNeedExplicitDefenses()
    {
        CollectionAssert.AreEqual(new[] { Defense.Block, Defense.Slip, Defense.Duck }, CmaTune.DefenseSet("2", null).ToArray());
        CollectionAssert.AreEqual(new[] { Defense.Block, Defense.Weave, Defense.Duck }, CmaTune.DefenseSet("3", null).ToArray());
        CollectionAssert.AreEqual(new[] { Defense.Block, Defense.StepBack }, CmaTune.DefenseSet("1b", null).ToArray());
        CollectionAssert.AreEqual(new[] { Defense.Sway }, CmaTune.DefenseSet("2", [Defense.Sway]).ToArray(), "明示すれば表より優先");

        var error = Assert.Throws<ArgumentException>(() => CmaTune.DefenseSet("5", null));
        StringAssert.Contains(error.Message, "--defenses");
        Assert.Throws<ArgumentException>(() => CmaTune.DefenseSet("5", [Defense.None]));
        var config = new CmaTuneConfig(["punch:5"], null);
        Assert.Throws<ArgumentException>(() => CmaTune.MakeTarget("punch:5", config, BotCatalog.Champion));
        Assert.IsNotNull(CmaTune.MakeTarget("punch:5", config with { Defenses = [Defense.Block] }, BotCatalog.Champion));
    }

    [TestMethod]
    public void MakeTarget_RejectsBadTargets()
    {
        var config = new CmaTuneConfig(["x"], null);
        Assert.Throws<ArgumentException>(() => CmaTune.MakeTarget("punch", config, BotCatalog.Champion));
        Assert.Throws<ArgumentException>(() => CmaTune.MakeTarget("kick:2", config, BotCatalog.Champion));
        Assert.Throws<ArgumentException>(() => CmaTune.MakeTarget("defense:None", config, BotCatalog.Champion));
        Assert.Throws<ArgumentException>(() => CmaTune.MakeTarget("punch:1-2", config with { Defenses = [Defense.Block] }, BotCatalog.Champion), "連打は単発でない");
        Assert.AreEqual("defense:Slip", CmaTune.MakeTarget("defense:slip", config, BotCatalog.Champion).Name);
    }

    [TestMethod]
    public void DefenseTarget_PointIsFinite()
    {
        var target = CmaTune.MakeTarget("defense:Slip", new CmaTuneConfig(["defense:Slip"], null, Seconds: 5.035f), BotCatalog.Champion);
        var point = target.Point(target.Initial(), 1000);
        Assert.IsTrue(double.IsFinite(point));
        Assert.AreEqual(target.Genes.Count, target.Initial().Length);
    }

    private static CmaTuneConfig Small(int generations, int? parallel = null) => new(["punch:2"], [Defense.Block], Generations: generations, Lambda: 4, Sigma: 0.15,
        Samples: 1, Seconds: 8.392f, Seed: 3, ConfirmN: 0, MatchN: 0, MaxParallel: parallel);

    private static CmaTuneState Read(string path) => JsonSerializer.Deserialize<CmaTuneState>(File.ReadAllText(path))!;

    [TestMethod]
    public void CmaTune_ResumeGivesTheSameResultAsRunningStraight()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cmatune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var straight = Path.Combine(dir, "straight.json");
            var resumed = Path.Combine(dir, "resumed.json");
            new CmaTune(Small(2), _ => { }).Run(straight, null);
            new CmaTune(Small(1), _ => { }).Run(resumed, null);
            Assert.AreEqual(1, Read(resumed).Rows.Count);
            new CmaTune(Small(2), _ => { }).Run(resumed, Path.Combine(dir, "book.json"));

            var a = Read(straight);
            var b = Read(resumed);
            Assert.AreEqual(2, a.Rows.Count);
            Assert.AreEqual(a.Cma, b.Cma, "CMA-ES の状態が JSON まで一致");
            Assert.AreEqual(a.Rows.Count, b.Rows.Count);
            for (var i = 0; i < a.Rows.Count; i++)
            {
                Assert.AreEqual(a.Rows[i].Generation, b.Rows[i].Generation);
                Assert.AreEqual(a.Rows[i].Best, b.Rows[i].Best);
                Assert.AreEqual(a.Rows[i].Mean, b.Rows[i].Mean);
                Assert.AreEqual(a.Rows[i].Worst, b.Rows[i].Worst);
                Assert.AreEqual(a.Rows[i].Sigma, b.Rows[i].Sigma);
                CollectionAssert.AreEqual(a.Rows[i].M, b.Rows[i].M);
            }

            // 候補の本とスタイルが出る（champion の他の技は変わらない）。
            var book = JsonSerializer.Deserialize<PunchBook>(File.ReadAllText(Path.Combine(dir, "book.json")))!;
            Assert.AreEqual(BotCatalog.Champion.Punches.Jab, book.Jab);
            var style = BotStyle.FromJson(File.ReadAllText(Path.Combine(dir, "book.style.json")));
            Assert.AreEqual(book.Cross, style.Punches.Cross);

            // 別の設定の状態では再開しない。
            Assert.Throws<InvalidOperationException>(() => new CmaTune(Small(2) with { Seed = 4 }, _ => { }).Run(resumed, null));
            // 回帰（レビュー指摘）: λ・σ が違う再開も拒む（前は状態の λ で黙って続いた）。
            Assert.Throws<InvalidOperationException>(() => new CmaTune(Small(2) with { Lambda = 6 }, _ => { }).Run(resumed, null));
            Assert.Throws<InvalidOperationException>(() => new CmaTune(Small(2) with { Sigma = 0.2 }, _ => { }).Run(resumed, null));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void CmaTune_ResultDoesNotDependOnParallelism()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cmatune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var one = Path.Combine(dir, "one.json");
            var many = Path.Combine(dir, "many.json");
            new CmaTune(Small(1, parallel: 1), _ => { }).Run(one, null);
            new CmaTune(Small(1, parallel: 3), _ => { }).Run(many, null);
            Assert.AreEqual(Read(one).Cma, Read(many).Cma);
            Assert.AreEqual(Read(one).Rows[0].Best, Read(many).Rows[0].Best);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void GenerationSeed_DiffersPerGenerationAndSampleByAtLeast1000()
    {
        var seeds = new List<int>();
        for (var g = 0; g < 3; g++)
        {
            for (var k = 0; k < 4; k++)
            {
                seeds.Add(CmaTune.GenerationSeed(7, g, 4, k));
            }
        }

        Assert.AreEqual(12, seeds.Distinct().Count());
        Assert.IsGreaterThanOrEqualTo(1000, seeds.Zip(seeds.Skip(1), (x, y) => y - x).Min());
    }
}
