using MiniRing.Bots;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("Probe")]
public sealed class ZzGroundSeedsProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void LongswordGroundStrike_Seeds()
    {
        var rows = Enumerable.Range(1, 6).Select(seed => GroundStrikeTests.Drill(BotCatalog.Champion, referee: false, MiniRing.Weapons.WeaponKind.Longsword, useWeaponBots: true, seed: seed)).ToList();
        Console.WriteLine($"longsword wounded {string.Join(",", rows.Select(r => r.Wounded.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))} started {string.Join(",", rows.Select(r => r.Started))}");
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void SwarmerGroundStrike_FinishOnOff()
    {
        foreach (var on in new[] { true, false })
        {
            var style = on ? BotCatalog.Swarmer : BotCatalog.Swarmer with { FinishTempo = 0f, FinishPress = 0f, FinishSpend = 0f };
            var rows = Enumerable.Range(1, 6).Select(seed => GroundStrikeTests.Drill(style, referee: false, seed: seed)).ToList();
            Console.WriteLine($"finish {on}: hits {string.Join(",", rows.Select(r => r.Hits))} started {string.Join(",", rows.Select(r => r.Started))} damage {string.Join(",", rows.Select(r => r.Damage.ToString("0", System.Globalization.CultureInfo.InvariantCulture)))}");
        }
    }
}
