using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>新しい体でスリップが外せない原因調べ（BASE_BODY_PLAN §10.7、通常の実行から外す Probe）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class SlipRepairProbe
{
    public TestContext TestContext { get; set; } = null!;

    private static (float Counter, int Punches, int Defenses, int Failed, int HeadHits, int OpenHead, int Guard, int Other, float HeadDmg) Run(DefenseMove slip)
    {
        var book = BotCatalog.Champion.Defenses.With(Defense.Slip, slip);
        var style = DefenseDrill.Defender(book, defend: true, DefenseDrill.DefaultRange, counters: true);
        var rs = new PunchDrillResult[8];
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            rs[i] = DefenseDrill.Punch("2", Defense.Slip, style, seed: i + 1, seconds: 23.5f, cue: 0.06714f));
        return (rs.Sum(r => r.CounterDamage), rs.Sum(r => r.Punches), rs.Sum(r => r.Defenses), rs.Sum(r => r.Failed), rs.Sum(r => r.HeadHits),
            rs.Sum(r => r.OpenHeadHits), rs.Sum(r => r.GuardHits), rs.Sum(r => r.OtherHits), rs.Sum(r => r.HeadDamage));
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void Scan()
    {
        var baseSlip = BotCatalog.Champion.Defenses.Slip;
        TestContext.WriteLine($"champion slip: {baseSlip}");
        var variants = new List<(string, DefenseMove)> { ("default", baseSlip) };
        foreach (var hx in new[] { 0.3f, 0.5f, 0.6f })
        {
            variants.Add(($"HeadX {hx}", baseSlip with { HeadX = hx, EndX = hx }));
        }

        foreach (var hy in new[] { -0.2f, -0.3f })
        {
            variants.Add(($"HeadY {hy}", baseSlip with { HeadY = hy }));
        }

        foreach (var hz in new[] { -0.1f, 0f, 0.2f })
        {
            variants.Add(($"HeadZ {hz}", baseSlip with { HeadZ = hz }));
        }

        foreach (var (name, v) in variants)
        {
            var r = Run(v);
            TestContext.WriteLine($"{name,-12} counter {r.Counter:0.0} punches {r.Punches} defenses {r.Defenses} failed {r.Failed} headHits {r.HeadHits} openHead {r.OpenHead} guard {r.Guard} other {r.Other} headDmg {r.HeadDmg:0.0}");
        }
    }
}
