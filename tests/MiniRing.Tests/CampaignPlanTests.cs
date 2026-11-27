using MiniRing.Arena;
using MiniRing.Arena.Campaign;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// 2026-11-26 キャンペーンの UI の作り直し: 閉じた大会の「あと何で開くか」が判定と同じ値（戦績・席）から出ること、
/// 本拠地の「次の試合」が一番先の段へ移ること、試合の前後の差から喜びの札（解放・席・王座・勝敗）が出ること。
/// </summary>
[TestClass]
public sealed class CampaignPlanTests
{
    private static readonly IReadOnlyList<FighterCharacter> Roster = CharacterRoster.All;

    private static CampaignBoxer NewBoxer()
    {
        var draft = CharacterMaker.NewDraft("standard") with { Id = "test-boxer", Name = "テスト", Country = "japan", Order = 1 };
        var (character, _) = CharacterMaker.ToFile(draft);
        return new CampaignBoxer(character, CampaignCareer.Empty);
    }

    private static string IvfDivision(CampaignBoxer b) => Organizations.Get("ivf").DivisionOf(b.WeightKg).Id;

    private static CampaignBoxer WithBouts(CampaignBoxer boxer, string org, string division, int wins, int losses = 0)
    {
        var bouts = Enumerable.Range(0, wins + losses)
            .Select(i => new CampaignBout($"{org}-rookie", org, division, "x", i < wins ? BoutResult.Win : BoutResult.Loss, false, boxer.WeightKg, null, null));
        return boxer with { Career = boxer.Career with { Bouts = [.. boxer.Career.Bouts, .. bouts] } };
    }

    private static CampaignStop Stop(CampaignBoxer boxer, string tier) =>
        CampaignPlan.Stops(boxer, Roster).Single(s => s.Event.Id == $"ivf-{tier}-{IvfDivision(boxer)}");

    [TestMethod]
    public void ClosedEvents_SayWhatIsLeft_FromTheSameCountsAsTheCheck()
    {
        var boxer = NewBoxer();
        var division = IvfDivision(boxer);
        var one = WithBouts(boxer, "ivf", division, wins: 1);
        Assert.AreEqual("あと 2 勝で開く", CampaignPlan.GateText(Stop(one, "ranking")));
        Assert.AreEqual(1f / 3f, CampaignPlan.WinProgress(Stop(one, "ranking"))!.Value, 1e-5f);
        Assert.AreEqual("あと 1 勝で開く", CampaignPlan.GateText(Stop(WithBouts(boxer, "ivf", division, wins: 2), "ranking")));
        Assert.AreEqual("あと 3 勝で開く", CampaignPlan.GateText(Stop(WithBouts(boxer, "ivf", "other", wins: 2), "ranking")), "wins elsewhere do not count, same as the check");

        var three = WithBouts(boxer, "ivf", division, wins: 3);
        Assert.IsTrue(Stop(three, "ranking").Open);
        Assert.AreEqual("出られる！", CampaignPlan.GateText(Stop(three, "ranking")));
        Assert.AreEqual("ランキング 1 位で挑戦", CampaignPlan.GateText(Stop(three, "title")));
        var fifth = three with { Career = three.Career.WithSeat("ivf", division, 5) };
        Assert.AreEqual("5 位 → 1 位で挑戦", CampaignPlan.GateText(Stop(fifth, "title")));
        Assert.AreEqual("新人の枠は卒業", CampaignPlan.GateText(Stop(WithBouts(boxer, "ivf", division, wins: 2, losses: 2), "rookie")));

        // 言葉の元の数は判定と食い違わない: 開いていると言う大会は Check でも開いている。
        foreach (var stop in CampaignPlan.Stops(fifth, Roster))
        {
            Assert.AreEqual(CampaignLadder.Check(stop.Event, fifth, Roster).Entry is not null, stop.Gate.IsOpen, stop.Event.Id);
        }
    }

    [TestMethod]
    public void NextMatch_MovesToTheFurthestOpenStep_IvfOwnDivisionFirst()
    {
        var boxer = NewBoxer();
        var division = IvfDivision(boxer);
        Assert.AreEqual($"ivf-rookie-{division}", CampaignPlan.AutoNext(CampaignPlan.Stops(boxer, Roster))!.Event.Id, "same step: IVF own division before WVC and VFC");
        var three = WithBouts(boxer, "ivf", division, wins: 3);
        Assert.AreEqual($"ivf-ranking-{division}", CampaignPlan.AutoNext(CampaignPlan.Stops(three, Roster))!.Event.Id, "a newly opened ranking bout becomes the next match");
        var first = three with { Career = three.Career.WithSeat("ivf", division, 1) };
        Assert.AreEqual($"ivf-title-{division}", CampaignPlan.AutoNext(CampaignPlan.Stops(first, Roster))!.Event.Id);
    }

    [TestMethod]
    public void Cards_ComeFromTheDifferenceBeforeAndAfter()
    {
        var boxer = NewBoxer();
        var division = IvfDivision(boxer);

        // 3 勝目 → ランキング戦が開く。
        var two = WithBouts(boxer, "ivf", division, wins: 2);
        var rookie = CampaignLadder.Check(CampaignEvents.Get($"ivf-rookie-{division}"), two, Roster).Entry!;
        var three = two.AfterBout(rookie, BoutResult.Win, finish: true, weapon: null, rankedCount: 10);
        var cards = CampaignCard.Between(two, three, rookie, BoutResult.Win, true, Roster);
        var result = (CampaignCard.Result)cards[0];
        Assert.AreEqual((2, 0, 0), result.Before);
        Assert.AreEqual((3, 0, 0), result.After);
        var unlocked = cards.OfType<CampaignCard.Unlocked>().Single();
        Assert.IsTrue(unlocked.Events.Any(e => e.Id == $"ivf-ranking-{division}"));
        Assert.IsFalse(cards.OfType<CampaignCard.RankUp>().Any(), "a rookie win does not move the seat");

        // ランキング戦に勝つ → 席が上がる（追い越したのは相手）。
        var ranking = CampaignLadder.Check(CampaignEvents.Get($"ivf-ranking-{division}"), three, Roster).Entry!;
        var ranked = OrgStandings.Of(Organizations.Get("ivf")).For(ranking.Division).Ranking.Count;
        var up = three.AfterBout(ranking, BoutResult.Win, false, null, ranked);
        var rise = CampaignCard.Between(three, up, ranking, BoutResult.Win, false, Roster).OfType<CampaignCard.RankUp>().Single();
        Assert.IsNull(rise.From);
        Assert.AreEqual(up.Career.SeatIn("ivf", division), rise.To);
        Assert.AreEqual(ranking.Opponent.HudName, rise.Passed);

        // 1 位からタイトルに勝つ → 王座の札。
        var first = three with { Career = three.Career.WithSeat("ivf", division, 1) };
        var title = CampaignLadder.Check(CampaignEvents.Get($"ivf-title-{division}"), first, Roster).Entry;
        if (title is not null)
        {
            var champ = first.AfterBout(title, BoutResult.Win, true, null, ranked);
            var belt = CampaignCard.Between(first, champ, title, BoutResult.Win, true, Roster).OfType<CampaignCard.Title>().Single();
            Assert.IsFalse(belt.Defense);
        }

        // 負け: 落ち着いた札 1 枚と次の目標。席も大会も増えない。
        var lost = three.AfterBout(ranking, BoutResult.Loss, true, null, ranked);
        var calm = CampaignCard.Between(three, lost, ranking, BoutResult.Loss, true, Roster);
        Assert.AreEqual(1, calm.Count);
        Assert.IsFalse(string.IsNullOrEmpty(((CampaignCard.Result)calm[0]).NextGoal));
    }
}
