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
    public void WvcRulesAddImpactCalibrationToTheStandardTelevisionRules()
    {
        var wvc = Organizations.Get("wvc");
        var baseline = TvShowRules.Rules(3, 33.57f);
        var calibrated = TvShowRules.Rules(3, 33.57f, wvc);
        Assert.AreEqual(48f, FighterTuning.Default.ConcussionSpin * calibrated.ConcussionSpinScale, 0.001f);
        Assert.AreEqual(0.60f, calibrated.LimbBendStrengthScale);
        Assert.AreEqual(baseline, calibrated with
        {
            ConcussionSpinScale = baseline.ConcussionSpinScale,
            LimbBendStrengthScale = baseline.LimbBendStrengthScale,
        });
    }

    [TestMethod]
    public void VfcRulesCountNoDownsJudgeNothingRegrowAndResupply()
    {
        var rules = TvShowRules.Rules(5, 41.96f, Organizations.Get("vfc"));
        Assert.IsFalse(rules.CountsDowns);
        Assert.IsFalse(rules.JudgesDecision);
        Assert.AreEqual(0f, rules.RegenVoxelsPerSecond, "no budgeted healing (the WVC model)");
        Assert.AreEqual(0.000839f, rules.RegrowthSpeed, "wounds grow back from the living edge in the breaks");
        Assert.AreEqual(0.4196f, rules.RegrowthTickSeconds);
        Assert.AreEqual(5.035f, rules.StuckDoublingSeconds);
        Assert.IsTrue(rules.ResupplyWeapons);
        Assert.AreEqual(33.57f, rules.BruiseHalfLifeSeconds, "skin bruises fade in the breaks, as in the WVC");
        Assert.AreEqual(5, rules.Rounds);
        Assert.AreEqual(41.96f, rules.BreakSeconds);
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

        // 剣と素手の組も広い檻（素手の 1.3 m（人形の単位。実物大で 3.66 m）の檻では剣の間合いが檻の半分を占めた。2026-11-19、ユーザー: 小さすぎる）。
        var mixed = TvShowRules.Stage(BotCatalog.Corners("boxer") with { PlayerWeapon = Weapons.WeaponKind.Longsword }, vfc);
        Assert.AreEqual(RingLayout.WeaponsCage, mixed.Layout);

        // 動画・中継の両コーナーは、コーナーから始める四角いリングを持って来る: それでも檻になり、コーナーから始める。
        var fromCorners = TvShowRules.Stage(BotCatalog.Corners("boxer").StartingAtCorners(), vfc);
        Assert.AreEqual(RingLayout.Cage.AtCorners(), fromCorners.Layout);
    }

    [TestMethod]
    public void VfcMatchFromTheOrganizationFileRunsInTheCage()
    {
        var vfc = Organizations.Get("vfc");
        using var game = new BoxingGame(seed: 3, corners: TvShowRules.Stage(BotCatalog.Corners("boxer"), vfc), rules: TvShowRules.Rules(5, 41.96f, vfc), mood: vfc.ArenaMood);
        for (var i = 0; i < 403; i++) // 約 1.68 秒
        {
            game.Step(default, 1f / 240f);
        }

        Assert.IsTrue(game.Layout.IsCage);
        Assert.IsFalse(game.Referee);
        Assert.IsFalse(game.CountsDowns);
    }
}
