using MiniRing.Arena.Campaign;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>2026-11-26: メニューのキャンペーン（作る・大会に出る・武器を選び直す・減量・消す・試合の後に戦績を書く）。</summary>
[TestClass]
public sealed class CampaignMenuTests
{
    private static readonly MenuInput Confirm = new(Confirm: true);
    private static readonly MenuInput Cancel = new(Cancel: true);
    private static MenuInput Dir(int x = 0, int y = 0) => new(x, y);

    private static (MatchMenu Menu, string Dir) NewMenu()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"miniring-campaign-menu-{Guid.NewGuid():N}");
        var menu = new MatchMenu("none", Path.Combine(dir, "settings.json"), loadPortraits: false, campaignDirectory: Path.Combine(dir, "campaign"));
        return (menu, dir);
    }

    private static void Create(MatchMenu menu)
    {
        while (menu.State.CampaignIndex != menu.State.Boxers.Count) { menu.Handle(Dir(y: 1)); }
        menu.Handle(Confirm);
        Assert.AreEqual(MenuPage.Create, menu.State.Page);
        menu.Handle(Dir(y: 3));
        menu.Handle(Confirm);
        Assert.AreEqual(MenuPage.Boxer, menu.State.Page);
    }

    [TestMethod]
    public void CreateEnterRecord_WritesTheBoutIntoTheBoxersFile_OnlyWhenTheMatchFinished()
    {
        var (menu, dir) = NewMenu();
        try
        {
            menu.Handle(Dir(x: 1));
            Assert.AreEqual((int)TopItem.Campaign, menu.State.TopIndex);
            menu.Handle(Confirm);
            Assert.AreEqual(MenuPage.Campaign, menu.State.Page);
            Create(menu);
            menu.Handle(Cancel);
            Create(menu);
            Assert.AreEqual(2, menu.State.Boxers.Count);

            // 出られる新人戦へ（標準の体: 何かの新人戦には出られる）。
            var row = menu.State.Events.ToList().FindIndex(e => e.Entry is not null);
            Assert.IsTrue(row >= 0);
            while (menu.State.BoxerIndex != row) { menu.Handle(Dir(y: 1)); }
            var (match, _) = menu.Handle(Confirm);
            Assert.IsNotNull(match);
            Assert.IsNotNull(match.Campaign);
            Assert.IsTrue(match.Event.Tournament, "campaign bouts go through the org's weigh-in and gear");
            var boxerId = match.Campaign.BoxerId;

            menu.Return(outcome: null);
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page, "back to the boxer's page");
            Assert.AreEqual(0, menu.State.Boxers.Single(b => b.Id == boxerId).Career.Bouts.Count, "an unfinished bout is not recorded");

            (match, _) = menu.Handle(Confirm);
            menu.Return(outcome: new BoutOutcome(BoutResult.Win, Finish: true));
            var boxer = menu.State.Boxers.Single(b => b.Id == boxerId);
            Assert.AreEqual(1, boxer.Career.Wins);
            Assert.AreEqual(match!.Blue.Id, boxer.Career.Bouts[0].Opponent);
            var reloaded = new CampaignStore(Path.Combine(dir, "campaign")).LoadAll().Single(b => b.Id == boxerId);
            Assert.AreEqual(1, reloaded.Career.Wins, "saved to the file");
        }
        finally
        {
            menu.Dispose();
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
    }

    [TestMethod]
    public void WeaponsAreChosenPerEvent_WeightCutIsSaved_DeleteNeedsTwoConfirms()
    {
        var (menu, dir) = NewMenu();
        try
        {
            menu.Handle(Dir(x: 1));
            menu.Handle(Confirm);
            Create(menu);
            var vfc = menu.State.Events.ToList().FindIndex(e => e.Event.Org == "vfc" && e.Entry is not null);
            if (vfc >= 0)
            {
                while (menu.State.BoxerIndex != vfc) { menu.Handle(Dir(y: 1)); }
                menu.Handle(Dir(x: 1));
                var (match, _) = menu.Handle(Confirm);
                Assert.IsNotNull(match!.Red.Weapon, "the picked weapon goes to this event's bout");
                Assert.IsNotNull(match.Campaign!.Weapon);
                menu.Return();
            }

            while (menu.State.BoxerIndex != MenuState.WeightRow) { menu.Handle(Dir(y: 1)); }
            var before = menu.State.Boxer!.WeightKg;
            menu.Handle(Dir(x: -3));
            menu.Handle(Confirm);
            Assert.AreEqual(before - 3, menu.State.Boxer!.WeightKg);
            Assert.IsNull(menu.State.Boxer.Character.Weapon, "the body file never keeps an event's weapon");

            menu.Handle(Dir(y: 1));
            menu.Handle(Confirm);
            Assert.AreEqual(1, menu.State.Boxers.Count, "first confirm only asks");
            menu.Handle(Confirm);
            Assert.AreEqual(0, menu.State.Boxers.Count);
            Assert.AreEqual(MenuPage.Campaign, menu.State.Page);
        }
        finally
        {
            menu.Dispose();
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
    }
}
