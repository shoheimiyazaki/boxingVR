using MiniRing.Arena.Campaign;
using MiniRing.Bots;
using BoutResult = MiniRing.Arena.Campaign.BoutResult;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// 2026-11-26: 試合が決着したら、そのまま次へ進む（「相手を KO してもそのままゲームが進行しません」）。
/// 決着（<see cref="MatchFinish"/>）→ 決着の瞬間を見せる（<see cref="FinishGate"/>）→ 結果の画面（<see cref="MenuPage.Result"/>）→
/// キャンペーンなら戦績を書いて本拠地の札、フリーバトルならもう一度・キャラ選択・メニュー。決着の前に抜けた試合は記録しない。
/// </summary>
[TestClass]
public sealed class MatchFlowTests
{
    private static readonly MenuInput Confirm = new(Confirm: true);
    private static MenuInput Dir(int x = 0, int y = 0) => new(x, y);

    /// <summary>ボタン <paramref name="index"/> をマウスでクリックする入力。</summary>
    private static MenuInput ClickResult(int index, int count) => new(Pointer: MenuLayout.ResultButtonCenter(index, count), Click: true);

    private static (MatchMenu Menu, string Dir) NewMenu()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"miniring-flow-{Guid.NewGuid():N}");
        var menu = new MatchMenu("none", Path.Combine(dir, "settings.json"), loadPortraits: false, campaignDirectory: Path.Combine(dir, "campaign"));
        return (menu, dir);
    }

    private static void Cleanup(MatchMenu menu, string dir)
    {
        menu.Dispose();
        if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>キャンペーンでボクサーを 1 人作り、次の試合に入る（試合の札を返す）。</summary>
    private static MatchSelection EnterCampaignBout(MatchMenu menu)
    {
        menu.Handle(Dir(x: 1));
        menu.Handle(Confirm); // キャンペーン
        menu.Handle(Confirm); // 新しく作る
        menu.Handle(Dir(y: 3));
        menu.Handle(Confirm); // 作る → 本拠地
        Assert.AreEqual(MenuPage.Boxer, menu.State.Page);
        var (match, _) = menu.Handle(Confirm); // 次の試合へ
        Assert.IsNotNull(match?.Campaign);
        Assert.IsTrue(menu.InCampaignBout);
        return match;
    }

    private static BoutSummary Summary(int? winner, string method, bool campaign) => new("RED", "BLUE", winner, method, 2, "1:07", campaign);

    [TestMethod]
    public void Gate_ShowsTheFinish_ThenAdvancesOnAPressAfterTheLock_OrByItself()
    {
        var gate = new FinishGate();
        Assert.AreEqual(FinishStage.Playing, gate.Update(settled: false, pressed: true, now: 10.0), "a press before the finish is the fight's, not the menu's");
        Assert.AreEqual(FinishStage.Showing, gate.Update(true, pressed: true, now: 20.0), "the punch that ended it does not skip the finish");
        Assert.AreEqual(FinishStage.Showing, gate.Update(true, true, 20.0 + FinishGate.LockSeconds - 0.01));
        Assert.AreEqual(FinishStage.Ready, gate.Update(true, false, 20.0 + FinishGate.LockSeconds));
        Assert.AreEqual(FinishStage.Advance, gate.Update(true, true, 20.0 + FinishGate.LockSeconds + 0.1));

        var idle = new FinishGate();
        idle.Update(true, false, 0.0);
        Assert.AreEqual(FinishStage.Ready, idle.Update(true, false, FinishGate.AutoSeconds - 0.01));
        Assert.AreEqual(FinishStage.Advance, idle.Update(true, false, FinishGate.AutoSeconds), "nobody has to press: the game goes on by itself");
        idle.Reset();
        Assert.AreEqual(FinishStage.Playing, idle.Update(false, false, 100.0));
    }

    [TestMethod]
    public void CampaignKo_RecordsTheWin_ShowsTheResult_AndTheClickGoesHomeToTheCards()
    {
        var (menu, dir) = NewMenu();
        try
        {
            var match = EnterCampaignBout(menu);
            menu.Finish(Summary(0, "K.O. — OUT COLD", campaign: true), new BoutOutcome(BoutResult.Win, Finish: true));
            Assert.AreEqual(MenuPage.Result, menu.State.Page);
            Assert.AreEqual(1, menu.State.Result!.Choices.Count, "a campaign bout has one way on: home");
            var boxer = menu.State.Boxers.Single(b => b.Id == match.Campaign!.BoxerId);
            Assert.AreEqual(1, boxer.Career.Wins, "recorded before the result screen (closing the game there keeps it)");
            Assert.IsTrue(boxer.Career.Bouts[0].Finish);
            Assert.IsGreaterThan(0, menu.State.Cards.Count, "the cards wait on the home page");

            menu.Handle(ClickResult(0, 1)); // マウスだけで進める
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page);
            Assert.IsInstanceOfType<CampaignCard.Result>(menu.State.Cards[0]);
            Assert.AreEqual(BoutResult.Win, ((CampaignCard.Result)menu.State.Cards[0]).Outcome);
            Assert.IsFalse(menu.InCampaignBout);
        }
        finally { Cleanup(menu, dir); }
    }

    [TestMethod]
    public void CampaignDecisionLoss_AndDraw_AreRecorded_AndEnterGoesHome()
    {
        var (menu, dir) = NewMenu();
        try
        {
            var match = EnterCampaignBout(menu);
            menu.Finish(Summary(1, "判定", campaign: true), new BoutOutcome(BoutResult.Loss, Finish: false));
            Assert.AreEqual(MenuPage.Result, menu.State.Page);
            menu.Handle(Confirm);
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page);
            while (menu.State.Cards.Count > 0) { menu.Handle(Confirm); }
            var boxer = menu.State.Boxers.Single(b => b.Id == match.Campaign!.BoxerId);
            Assert.AreEqual(1, boxer.Career.Losses);
            Assert.IsFalse(boxer.Career.Bouts[0].Finish);

            var (again, _) = menu.Handle(Confirm); // 本拠地から次の試合
            Assert.IsNotNull(again?.Campaign);
            menu.Finish(Summary(null, "引き分け（DRAW）", campaign: true), new BoutOutcome(BoutResult.Draw, Finish: false));
            menu.Handle(new MenuInput(Cancel: true)); // 戻るでも本拠地へ
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page);
            boxer = menu.State.Boxers.Single(b => b.Id == match.Campaign!.BoxerId);
            Assert.AreEqual(2, boxer.Career.Bouts.Count);
            Assert.AreEqual(BoutResult.Draw, boxer.Career.Bouts[1].Result);
        }
        finally { Cleanup(menu, dir); }
    }

    [TestMethod]
    public void LeavingBeforeTheFinish_RecordsNothing_AndShowsNoResult()
    {
        var (menu, dir) = NewMenu();
        try
        {
            var match = EnterCampaignBout(menu);
            menu.Return(outcome: null);
            Assert.AreEqual(MenuPage.Boxer, menu.State.Page, "straight home, no result screen");
            Assert.IsNull(menu.State.Result);
            Assert.AreEqual(0, menu.State.Boxers.Single(b => b.Id == match.Campaign!.BoxerId).Career.Bouts.Count);
            Assert.AreEqual(0, menu.State.Cards.Count);
        }
        finally { Cleanup(menu, dir); }
    }

    [TestMethod]
    public void FreeBattle_Result_OffersRematch_Select_AndMenu_ByClick()
    {
        var (menu, dir) = NewMenu();
        try
        {
            MatchSelection? first = null;
            for (var i = 0; i < 6 && first is null; i++) { first = menu.Handle(Confirm).Match; } // フリーバトル → シングル → 練習試合 → 赤 → 青
            Assert.IsNotNull(first);
            Assert.IsNull(first.Campaign);
            Assert.IsFalse(menu.InCampaignBout);

            menu.Finish(Summary(0, "10 COUNT K.O.", campaign: false), outcome: null);
            Assert.AreEqual(MenuPage.Result, menu.State.Page);
            CollectionAssert.AreEqual(new[] { ResultChoice.Again, ResultChoice.Select, ResultChoice.Menu }, menu.State.Result!.Choices.ToArray());

            var (again, _) = menu.Handle(ClickResult(0, 3));
            Assert.AreSame(first, again, "rematch: same pair, same rules");

            menu.Finish(Summary(1, "判定", campaign: false), outcome: null);
            menu.Handle(ClickResult(1, 3));
            Assert.AreEqual(MenuPage.Select, menu.State.Page);
            Assert.IsFalse(menu.State.RedLocked, "pick again from scratch");

            var next = menu.Handle(Confirm).Match ?? menu.Handle(Confirm).Match;
            Assert.IsNotNull(next);
            menu.Finish(Summary(null, "引き分け", campaign: false), outcome: null);
            menu.Handle(Dir(x: 1)); // キーでも選べる
            Assert.AreEqual(1, menu.State.ResultIndex);
            menu.Handle(ClickResult(2, 3));
            Assert.AreEqual(MenuPage.Top, menu.State.Page);
        }
        finally { Cleanup(menu, dir); }
    }
}

/// <summary>決着を BoxingGame の状態から読む（新しい判定を作らない）。試合を回すので slow。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class MatchFinishTests
{
    [TestMethod]
    public void ADecidedMatch_IsSettled_AndSummarisedFromTheGamesOwnVerdict()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)),
            rules: new MatchRules(RoundSeconds: 1.678f, Rounds: 1, BreakSeconds: 0.8392f));
        Assert.IsFalse(MatchFinish.Settled(game));
        for (var t = 0f; t < 4f && !game.MatchOver; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }

        Assert.IsTrue(MatchFinish.Settled(game));
        var summary = MatchFinish.Summary(game, campaign: true);
        Assert.AreEqual(1, summary.Round);
        Assert.AreEqual(game.RedLabel, summary.Red);
        var outcome = BoutOutcome.Of(game.MatchOver, game.PlayerWins, game.CpuWins, game.Decision)!.Value;
        Assert.IsFalse(outcome.Finish, "the clock ran out: a decision, not a finish");
        Assert.AreEqual(outcome.Result switch { BoutResult.Win => (int?)0, BoutResult.Loss => 1, _ => null }, summary.Winner, "screen and record agree");
        Assert.IsTrue(summary.Method.Contains("判定", StringComparison.Ordinal) || summary.Method.Contains("引き分け", StringComparison.Ordinal), summary.Method);
    }

    [TestMethod]
    public void PracticeWithoutRules_SettlesOnTheFirstDecidedRound()
    {
        using var game = new BoxingGame(seed: 2);
        Assert.IsNull(game.Rules);
        Assert.IsFalse(MatchFinish.Settled(game), "no rules: the bout is open until a round is decided");
        Assert.AreEqual("0:00", MatchFinish.Clock(0.4f));
        Assert.AreEqual("1:07", MatchFinish.Clock(67.9f));
    }
}
