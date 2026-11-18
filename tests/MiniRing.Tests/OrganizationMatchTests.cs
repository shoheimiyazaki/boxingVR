using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 団体の試合の決まり（団体のファイルの <c>match</c>・<c>countsDowns</c>）が、中継の撮影・下見の試合（<see cref="TvShowRules.Rules(int, float, Organization)"/>・
/// <see cref="TvShowRules.Stage"/>）に通ること。WVC は今の中継と同じ決まり・同じ場、VFC はダウンを数えず・判定せず・治らず・レフェリーなし・六角形の檻。
/// </summary>
[TestClass]
public sealed class OrganizationMatchTests
{
    [TestMethod]
    public void WvcRulesAreTheTelevisionRulesUnchanged()
    {
        var wvc = Organizations.Get("wvc");
        Assert.AreEqual(TvShowRules.Rules(3, 20f), TvShowRules.Rules(3, 20f, wvc));
    }

    [TestMethod]
    public void VfcRulesCountNoDownsJudgeNothingRegrowAndResupply()
    {
        var rules = TvShowRules.Rules(5, 25f, Organizations.Get("vfc"));
        Assert.IsFalse(rules.CountsDowns);
        Assert.IsFalse(rules.JudgesDecision);
        Assert.AreEqual(0f, rules.RegenVoxelsPerSecond, "no budgeted healing (the WVC model)");
        Assert.AreEqual(0.0005f, rules.RegrowthSpeed, "wounds grow back from the living edge in the breaks");
        Assert.AreEqual(0.25f, rules.RegrowthTickSeconds);
        Assert.AreEqual(3f, rules.StuckDoublingSeconds);
        Assert.IsTrue(rules.ResupplyWeapons);
        Assert.AreEqual(20f, rules.BruiseHalfLifeSeconds, "skin bruises fade in the breaks, as in the WVC");
        Assert.AreEqual(5, rules.Rounds);
        Assert.AreEqual(25f, rules.BreakSeconds);
        // MatchRules.Vfc（試合の芯の決まり）と同じ決まりになる。
        // 始め・終わりの待ち時間（エモート）は TV の撮影だけが足すので、それを除けば同じ。
        Assert.AreEqual(MatchRules.Vfc, rules with { IntroSeconds = 0f, EndSeconds = 0f });
    }

    [TestMethod]
    public void WvcStageKeepsTheRopedRingAndTheReferee()
    {
        var corners = BotCatalog.Corners("boxer");
        var staged = TvShowRules.Stage(corners, Organizations.Get("wvc"));
        Assert.AreEqual(corners with { SettleSeconds = TvShowRules.SettleSeconds }, staged); // 助走だけが足される
        Assert.AreEqual(ArenaShape.RopedSquare, staged.Layout.Shape);
    }

    [TestMethod]
    public void VfcStageIsARefereelessCageSizedByTheWeapons()
    {
        var vfc = Organizations.Get("vfc");
        var bare = TvShowRules.Stage(BotCatalog.Corners("boxer"), vfc);
        Assert.IsFalse(bare.Referee);
        Assert.AreEqual(RingLayout.Cage, bare.Layout);

        var armed = TvShowRules.Stage(BotCatalog.Corners("boxer") with { CpuWeapon = Weapons.WeaponKind.Katana, PlayerWeapon = Weapons.WeaponKind.Longsword }, vfc);
        Assert.AreEqual(RingLayout.WeaponsCage, armed.Layout);
    }

    [TestMethod]
    public void VfcMatchFromTheOrganizationFileRunsInTheCage()
    {
        var vfc = Organizations.Get("vfc");
        using var game = new BoxingGame(seed: 3, corners: TvShowRules.Stage(BotCatalog.Corners("boxer"), vfc), rules: TvShowRules.Rules(5, 25f, vfc), mood: vfc.ArenaMood);
        for (var i = 0; i < 240; i++)
        {
            game.Step(default, 1f / 240f);
        }

        Assert.IsTrue(game.Layout.IsCage);
        Assert.IsFalse(game.Referee);
        Assert.IsFalse(game.CountsDowns);
    }
}
