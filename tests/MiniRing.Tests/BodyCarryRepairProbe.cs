using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>体の運び（PhysicsVersion 45）で外れたテストの原因調べ（通常の実行から外す Probe）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class BodyCarryRepairProbe
{
    public TestContext TestContext { get; set; } = null!;

    private static BotStyle Defender(bool counters) => DefenseDrill.Defender(BotCatalog.Champion.Defenses, defend: true, DefenseDrill.DefaultRange, counters);

    [TestMethod]
    [TestCategory("Probe")]
    public void CounterHitsOfTheNonCounteringDefender()
    {
        foreach (var seed in new[] { 2, 3, 5, 6, 7 })
        {
            foreach (var counters in new[] { true, false })
            {
                var tot = 0f;
                var r = DefenseDrill.Punch("2", Defense.Slip, Defender(counters), seed: seed, seconds: 14f, cue: 0.04f,
                    observe: (g, _) =>
                    {
                        foreach (var h in g.RecentHits.Where(h => h.Attacker == g.Player))
                        {
                            tot += h.Damage;
                            if (!counters)
                            {
                                TestContext.WriteLine($"   seed {seed} {h.Zone} {h.Hand} kick={h.Kick} elbow={h.Elbow} speed {h.Speed:0.00} mass {h.ImpactMass:0.00} share {h.BodyShare:0.00} dmg {h.Damage:0.0}");
                            }
                        }
                    });
                TestContext.WriteLine($"seed {seed} counters={counters}: counterDamage {r.CounterDamage:0.0} (probe {tot:0.0})");
            }
        }
    }
}
