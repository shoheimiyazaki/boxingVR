
using MiniRing.Bots;
namespace MiniRing.Tests;
[TestClass]
public sealed class KarateBotTests
{
    [TestMethod]
    public void KarateStyle_PrefersParry_AndBoxerKeepsItsDefaults()
    {
        Assert.IsGreaterThan(BotCatalog.Karate.BlockWeight, BotCatalog.Karate.ParryWeight);
        Assert.AreEqual(0f, BotCatalog.Boxer.ParryWeight);
        Assert.AreEqual(0f, BotCatalog.Boxer.KickRate);
        Assert.IsGreaterThan(0f, BotCatalog.Karate.KickRate);
    }
    [TestMethod]
    public void ParryBook_IsValidatedAndHasACounter()
    {
        var move = DefenseBook.Default.Of(Defense.Parry).Validated();
        Assert.IsGreaterThan(0f, move.VsStraight);
        Assert.IsNotNull(move.CounterFor(PunchKind.Straight));
    }
}
