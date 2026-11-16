using System.Text.Json;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 拳のドリルの口（攻め手の差し替え・打ち返しのダメージ）と、cmatune（CMA-ES をドリルにつなぐ）の確かめ。
/// 点の良し悪しは見ない（長い測定は別）。決定的であること（並列の数・再開）と、引数の検査を確かめる。
/// </summary>
[TestClass]
public sealed class CmaTuneTests
{
    private static BotStyle Defender(bool counters) => DefenseDrill.Defender(BotCatalog.Champion.Defenses, defend: true, DefenseDrill.DefaultRange, counters);

    [TestMethod]
    public void Punch_WithoutAttackerStyle_IsTheSameAsPassingTheDefaultAttacker()
    {
        var plain = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: true), seed: 5, seconds: 6f, cue: 0.04f);
        var explicitAttacker = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: true), seed: 5, seconds: 6f, cue: 0.04f,
            attackerStyle: DefenseDrill.PunchAttacker("2", DefenseDrill.DefaultRange));
        Assert.AreEqual(plain, explicitAttacker, "攻め手を渡さない呼び出しは、既定の攻め手を渡した呼び出しと同じ結果");
        Assert.IsGreaterThan(0, plain.Punches);
    }

    [TestMethod]
    public void Punch_ChangingTheAttackersPunchBook_ChangesTheResult()
    {
        var cross = PunchBook.Default.Cross;
        var changed = PunchBook.Default.With(Technique.Cross, cross with { Duration = 0.25f, Lateral = 0.4f });
        var attacker = DefenseDrill.PunchAttacker("2", DefenseDrill.DefaultRange) with { Punches = changed };
        var plain = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: true), seed: 5, seconds: 8f, cue: 0.04f);
        var other = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: true), seed: 5, seconds: 8f, cue: 0.04f, attackerStyle: attacker);
        Assert.AreNotEqual(plain, other, "攻め手の技の表が違えば、同じ種でも軌道が分かれて結果が変わる");
    }

    [TestMethod]
    public void Punch_CounterDamage_IsPositiveWithCountersAndZeroWithout()
    {
        var withCounters = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: true), seed: 5, seconds: 14f, cue: 0.04f);
        var without = DefenseDrill.Punch("2", Defense.Slip, Defender(counters: false), seed: 5, seconds: 14f, cue: 0.04f);
        Assert.IsGreaterThan(0f, withCounters.CounterDamage, "守って打ち返す受け手は、攻め手にダメージを与える");
        Assert.AreEqual(0f, without.CounterDamage, "打ち返さない受け手は 0");
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
        DefenseDrill.Punch("2", Defense.Slip, Defender(counters: false), seed: 1, seconds: 2f, cue: 0.04f);
        DefenseDrill.MeasureBaseline(PunchKind.Straight, 1, 2f);
        var after = DrillMeter.Total;
        // 他のテストと並列に動くので、少なくとも自分の分は増えている（等号は主張しない）。
        Assert.IsGreaterThanOrEqualTo(2L, after.Calls - before.Calls);
        Assert.IsGreaterThanOrEqualTo(4d, after.Seconds - before.Seconds);
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
        var target = CmaTune.MakeTarget("defense:Slip", new CmaTuneConfig(["defense:Slip"], null, Seconds: 3f), BotCatalog.Champion);
        var point = target.Point(target.Initial(), 1000);
        Assert.IsTrue(double.IsFinite(point));
        Assert.AreEqual(target.Genes.Count, target.Initial().Length);
    }

    private static CmaTuneConfig Small(int generations, int? parallel = null) => new(["punch:2"], [Defense.Block], Generations: generations, Lambda: 4, Sigma: 0.15,
        Samples: 1, Seconds: 5f, Seed: 3, ConfirmN: 0, MatchN: 0, MaxParallel: parallel);

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
