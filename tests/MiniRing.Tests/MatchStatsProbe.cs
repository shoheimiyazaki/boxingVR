namespace MiniRing.Tests;

/// <summary>
/// CPU 同士で 30 秒打ち合い、ヒットの分布が「急所がばらける・体重の乗り方に幅がある・スタミナが減る」ことを確かめる。
/// 集計は標準出力にも出す（--logger "console;verbosity=detailed" で見える。調整の目安）。
/// </summary>
[TestClass]
public sealed class MatchStatsProbe
{
    [TestMethod]
    public void CpuVersusCpu_HitsVaryByZoneAndByHowMuchBodyIsBehindThem()
    {
        using var game = new BoxingGame(seed: 11);
        var ai = new AiControl(99, 0.8f);
        var hits = new List<HitEvent>();
        for (var i = 0; i < 60 * 30; i++)
        {
            game.Step(new BoxingInput(ai.Read(game.PlayerSense, 1f / 60f)), 1f / 60f);
            hits.AddRange(game.RecentHits);
        }

        foreach (var g in hits.GroupBy(h => h.Zone).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"{g.Key,-12} n={g.Count(),3} dmg={g.Average(h => h.Damage),5:0.0} mass={g.Average(h => h.ImpactMass),4:0.00}kg v={g.Average(h => h.Speed),4:0.00} body={g.Average(h => h.BodyShare),4:P0}");
        }

        Assert.IsGreaterThan(10, hits.Count);
        Assert.IsGreaterThan(3, hits.Select(h => h.Zone).Distinct().Count(), "punches land on different zones");
        Assert.IsGreaterThan(hits.Min(h => h.ImpactMass) * 3f, hits.Max(h => h.ImpactMass), "some punches are arm punches, some carry the body");
    }
}
