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

            // 本拠地は「次の試合」に合っている（標準の体: 何かの新人戦には出られる）。
            Assert.AreEqual(MenuState.HomeNext, menu.State.HomeIndex);
            Assert.IsNotNull(menu.State.Next?.Entry);
            var (match, _) = menu.Handle(Confirm);
            Assert.IsNotNull(match);
            Assert.IsNotNull(match.Campaign);
            Assert.IsTrue(match.Event.Tournament, "campaign bouts go through the org's weigh-in and gear");
            var boxerId = match.Campaign.BoxerId;

            menu.Return(outcome: null);
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page, "back to the boxer's page");
            Assert.AreEqual(0, menu.State.Boxers.Single(b => b.Id == boxerId).Career.Bouts.Count, "an unfinished bout is not recorded");
            Assert.AreEqual(0, menu.State.Cards.Count, "no celebration for an unfinished bout");

            (match, _) = menu.Handle(Confirm);
            menu.Return(outcome: new BoutOutcome(BoutResult.Win, Finish: true));
            var boxer = menu.State.Boxers.Single(b => b.Id == boxerId);
            Assert.AreEqual(1, boxer.Career.Wins);
            Assert.AreEqual(match!.Blue.Id, boxer.Career.Bouts[0].Opponent);
            var reloaded = new CampaignStore(Path.Combine(dir, "campaign")).LoadAll().Single(b => b.Id == boxerId);
            Assert.AreEqual(1, reloaded.Career.Wins, "saved to the file");

            // 試合の後の札: 1 回だけ出て、Enter で送り、最後で消える。札の間は本拠地の操作を受けない。
            Assert.IsInstanceOfType<CampaignCard.Result>(menu.State.Cards[0]);
            var count = menu.State.Cards.Count;
            Assert.IsNull(menu.Handle(Dir(y: 1)).Match);
            Assert.AreEqual(count, menu.State.Cards.Count, "moving does not skip a card");
            for (var i = 0; i < count; i++) { Assert.IsNull(menu.Handle(Confirm).Match, "Enter only turns the card"); }
            Assert.AreEqual(0, menu.State.Cards.Count);
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page);
            (match, _) = menu.Handle(Confirm);
            menu.Return(outcome: null);
            Assert.AreEqual(0, menu.State.Cards.Count, "shown once: an unfinished bout afterwards brings nothing back");
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
            // 本拠地 → 団体と大会 → VFC → 道の新人戦 → 次の試合にする → 武器を選んで出る。
            menu.Handle(Dir(y: 1));
            Assert.AreEqual(MenuState.HomeOrgs, menu.State.HomeIndex);
            menu.Handle(Confirm);
            Assert.AreEqual(MenuPage.Orgs, menu.State.Page);
            while (menu.State.PathOrg.Id != "vfc") { menu.Handle(Dir(y: 1)); }
            menu.Handle(Confirm);
            Assert.AreEqual(MenuPage.Path, menu.State.Page);
            Assert.AreEqual(3, menu.State.PathStops.Count, "rookie, ranking, title");
            while (menu.State.PathIndex != 0) { menu.Handle(Dir(x: -1)); }
            menu.Handle(Confirm);
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page);
            Assert.AreEqual("vfc-rookie", menu.State.Next!.Event.Id, "the path's pick becomes the next match");
            menu.Handle(Dir(x: 1));
            var (match, _) = menu.Handle(Confirm);
            Assert.IsNotNull(match!.Red.Weapon, "the picked weapon goes to this event's bout");
            Assert.IsNotNull(match.Campaign!.Weapon);
            menu.Return();

            // 閉じた段を選ぶと理由を知らせ、ページは変わらない。戻るで 1 段ずつ。
            menu.Handle(Dir(y: 1));
            menu.Handle(Confirm);
            menu.Handle(Confirm);
            while (menu.State.PathIndex != 2) { menu.Handle(Dir(x: 1)); }
            menu.Handle(Confirm);
            Assert.AreEqual(MenuPage.Path, menu.State.Page, "a closed step does not start anything");
            menu.Handle(Cancel);
            Assert.AreEqual(MenuPage.Orgs, menu.State.Page);
            menu.Handle(Cancel);
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page);

            Assert.AreEqual(MenuState.HomeOrgs, menu.State.HomeIndex, "back on the button that opened the orgs");
            menu.Handle(Dir(x: 1));
            Assert.AreEqual(MenuState.HomeWeight, menu.State.HomeIndex);
            menu.Handle(Confirm);
            Assert.AreEqual(MenuPage.Weight, menu.State.Page);
            var before = menu.State.Boxer!.WeightKg;
            menu.Handle(Dir(x: -3));
            menu.Handle(Dir(y: 1));
            menu.Handle(Confirm);
            Assert.AreEqual(before - 3, menu.State.Boxer!.WeightKg);
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page);
            Assert.IsNull(menu.State.Boxer.Character.Weapon, "the body file never keeps an event's weapon");

            Assert.AreEqual(MenuState.HomeWeight, menu.State.HomeIndex, "back on the weight button");
            while (menu.State.HomeIndex != MenuState.HomeDelete) { menu.Handle(Dir(x: 1)); }
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
