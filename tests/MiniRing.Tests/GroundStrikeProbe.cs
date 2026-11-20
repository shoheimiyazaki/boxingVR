using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調査用（スイートの外）: レフェリーなしで倒れた相手への攻めを、武器・種・スタイルごとに書く（GROUND_TRACE=1 で途中も）。2026-11-20 に <see cref="GroundStrikeTests"/> から移した。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class GroundStrikeProbe
{
    [TestMethod]
    public void Probe_Weapons()
    {
        GroundStrikeTests.Trace = Environment.GetEnvironmentVariable("GROUND_TRACE") == "1";
        foreach (var weapon in new[] { WeaponKind.Katana, WeaponKind.Longsword, WeaponKind.WoodenClub })
        {
            Console.WriteLine(GroundStrikeTests.Line(weapon.ToString(), GroundStrikeTests.Drill(BotCatalog.Champion, referee: false, weapon, useWeaponBots: true)));
        }
    }

    [TestMethod]
    public void Probe_WeaponsSeeds()
    {
        foreach (var weapon in new[] { WeaponKind.Katana, WeaponKind.Longsword, WeaponKind.WoodenClub })
        {
            for (var seed = 1; seed <= 6; seed++)
            {
                Console.WriteLine(GroundStrikeTests.Line($"{weapon} seed {seed} adopted", GroundStrikeTests.Drill(BotCatalog.Champion, referee: false, weapon, useWeaponBots: true, seed: seed)));
                Console.WriteLine(GroundStrikeTests.Line($"{weapon} seed {seed} legacy ", GroundStrikeTests.Drill(BotCatalog.Champion, referee: false, weapon, useWeaponBots: true, seed: seed, legacyWeapon: true)));
            }
        }
    }

    [TestMethod]
    public void Probe_Styles()
    {
        GroundStrikeTests.Trace = Environment.GetEnvironmentVariable("GROUND_TRACE") == "1";
        foreach (var style in new[] { BotCatalog.Boxer, BotCatalog.Champion, BotCatalog.Swarmer, BotCatalog.Karate })
        {
            Console.WriteLine(GroundStrikeTests.Line(style.Name, GroundStrikeTests.Drill(style, referee: false)));
        }
    }
}
