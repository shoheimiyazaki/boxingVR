using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>2026-11-16: テコンドーの教科書（Bots/taekwondo.json）とテコンドー家（roster/taeyun.json）。蹴りの経路・接触は KickContactTests。</summary>
[TestClass]
public sealed class TaekwondoTests
{
    private static readonly KickTechnique[] TurningKicks = [KickTechnique.Roundhouse, KickTechnique.Side, KickTechnique.Back, KickTechnique.Axe];

    [TestMethod]
    public void Textbook_IsKickFirst_WithTheFourNewKicks_AndIsInTheCatalog()
    {
        var s = BotCatalog.Taekwondo;
        Assert.AreEqual("taekwondo", s.Name);
        Assert.IsTrue(BotCatalog.Styles.Contains(s));
        Assert.AreSame(s, BotCatalog.Style("taekwondo"));
        Assert.IsGreaterThan(BotCatalog.Karate.KickRate, s.KickRate, "kicks are the main weapon");
        foreach (var kind in TurningKicks)
        {
            Assert.IsGreaterThan(0f, s.KickWeights.GetValueOrDefault(kind), kind.ToString());
        }

        Assert.IsGreaterThan(0f, s.KickRangeFit);
        Assert.AreEqual(0f, BotCatalog.Karate.KickRangeFit, "existing styles pick kicks by weight only, as before");
        Assert.IsTrue(s.ComboWeights.Values.Sum() <= 8f);
    }

    [TestMethod]
    public void Textbook_RejectsNegativeRangeFit_ByClamping()
    {
        Assert.AreEqual(0f, (BotCatalog.Taekwondo with { KickRangeFit = -1f }).Validated().KickRangeFit);
        Assert.AreEqual(4f, (BotCatalog.Taekwondo with { KickRangeFit = 99f }).Validated().KickRangeFit);
    }

    [TestMethod]
    public void Taeyun_IsATallLongLeggedBareHandedFighter_OfTheTaekwondoTextbook()
    {
        var c = CharacterRoster.Get("taeyun");
        Assert.AreEqual("taekwondo", c.StyleName);
        Assert.AreEqual(HandWear.None, c.Wear);
        Assert.IsNull(c.Weapon);
        Assert.IsFalse(c.IsKarate);
        Assert.IsGreaterThan(1.05f, c.Build.Height);
        Assert.IsGreaterThan(1.0f, c.Build.Legs);
        Assert.IsLessThan(1.0f, c.Build.Muscle, "slim");
        Assert.AreSame(BotCatalog.Taekwondo, c.Resolve(), "an empty education plays the textbook itself");
        Assert.IsTrue(CharacterRoster.Countries.ContainsKey(c.Country.Id));
    }

    [TestMethod]
    public void TheBot_ThrowsTheNewKicks_AndLandsKicks_AgainstAStandingOpponent()
    {
        BoxerBot? bot = null;
        var corners = new BoxingCorners("taekwondo", s => bot = new BoxerBot(BotCatalog.Taekwondo, s), _ => new StandGuard(), PlayerWear: HandWear.None, CpuWear: HandWear.None);
        using var game = new BoxingGame(seed: 5, corners: corners);
        var used = new HashSet<KickTechnique>();
        var landed = 0;
        var seen = new HashSet<HitEvent>();
        for (var i = 0; i < 240 * 45; i++)
        {
            game.Step(default, 1f / 240f);
            if (bot?.CurrentKick is { } kind)
            {
                used.Add(kind);
            }

            foreach (var hit in game.RecentHits)
            {
                if (hit.Kick && hit.Attacker == game.Cpu && seen.Add(hit))
                {
                    landed++;
                }
            }
        }

        Console.WriteLine($"kicks used: {string.Join(',', used)}, landed {landed}");
        Assert.IsGreaterThanOrEqualTo(1, used.Count(TurningKicks.Contains), "the textbook uses at least one of the new kicks");
        Assert.IsGreaterThan(0, landed);
    }
}
