using System.Numerics;
using MiniRing.Arena.Campaign;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// 2026-11-26: メニューをマウスだけで操作できる（ホバーで選ぶ・クリックで決める・◀ ▶ のクリック・押しっぱなしの連続・ドラッグ・右クリック / 隅のボタンで戻る）。
/// ページごとに、主な操作がマウスの入力だけで通ることを確かめる。キーボード・パッドの操作は <see cref="MenuStateTests"/>・<see cref="CampaignMenuTests"/>。
/// </summary>
[TestClass]
public sealed class MenuMouseTests
{
    private const float Frame = 1f / 60f;

    private static MenuInput Hover(Vector2 at) => new(Pointer: at);
    private static MenuInput Click(Vector2 at) => new(Pointer: at, Click: true, Held: true, Dt: Frame);
    private static MenuInput Hold(Vector2 at, float dt = Frame) => new(Pointer: at, Held: true, Dt: dt);
    private static readonly MenuInput Up = new(Dt: Frame);

    /// <summary>右クリック（殻が戻るに直す）。</summary>
    private static readonly MenuInput RightClick = new(Cancel: true);

    /// <summary>押して放す（1 回のクリック）。</summary>
    private static MenuOutcome Tap(MenuState state, Vector2 at)
    {
        var outcome = state.Apply(Click(at));
        state.Apply(Up);
        return outcome;
    }

    private static (MatchMenu Menu, string Dir) NewMenu()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"miniring-mouse-menu-{Guid.NewGuid():N}");
        var menu = new MatchMenu("none", Path.Combine(dir, "settings.json"), loadPortraits: false, campaignDirectory: Path.Combine(dir, "campaign"));
        return (menu, dir);
    }

    private static MenuOutcome Tap(MatchMenu menu, Vector2 at)
    {
        var (match, _) = menu.Handle(Click(at));
        menu.Handle(Up);
        return new MenuOutcome(Match: match);
    }

    [TestMethod]
    public void Top_HoverMovesTheCursor_ClickOpens_RightClickComesBack()
    {
        var state = new MenuState(CharacterRoster.All, MenuSettings.Default);
        state.Apply(Hover(MenuLayout.TopButtonCenter((int)TopItem.Settings)));
        Assert.AreEqual((int)TopItem.Settings, state.TopIndex, "hovering moves the cursor");
        Assert.AreEqual(MenuPage.Top, state.Page, "hovering alone does not open");
        Tap(state, MenuLayout.TopButtonCenter((int)TopItem.Settings));
        Assert.AreEqual(MenuPage.Settings, state.Page);
        state.Apply(RightClick);
        Assert.AreEqual(MenuPage.Top, state.Page);
        Tap(state, MenuLayout.TopButtonCenter((int)TopItem.Quit));
        Assert.IsTrue(state.QuitRequested);
    }

    [TestMethod]
    public void Settings_TheOptionPillsSetTheValueDirectly_AndTheBackRowReturns()
    {
        var state = new MenuState(CharacterRoster.All, MenuSettings.Default);
        Tap(state, MenuLayout.TopButtonCenter((int)TopItem.Settings));
        Assert.IsTrue(state.Settings.Referee);
        var off = state.Apply(Click(MouseLayout.RefereeOption(1)));
        state.Apply(Up);
        Assert.IsFalse(state.Settings.Referee);
        Assert.IsTrue(off.SettingsChanged);
        Assert.IsFalse(state.Apply(Click(MouseLayout.RefereeOption(1))).SettingsChanged, "pressing the lit pill again keeps it");
        state.Apply(Up);
        Assert.IsFalse(state.Settings.Referee);
        Tap(state, MouseLayout.RefereeOption(0));
        Assert.IsTrue(state.Settings.Referee);
        Tap(state, MenuLayout.SettingsRowCenter(1));
        Assert.AreEqual(MenuPage.Top, state.Page);
    }

    [TestMethod]
    public void FreeAndMode_RowsAreClicked_TheRulesArrowsChangeTheValue_AndHoldingRepeats()
    {
        var state = new MenuState(CharacterRoster.All, MenuSettings.Default);
        Tap(state, MenuLayout.TopButtonCenter((int)TopItem.FreeBattle));
        Assert.AreEqual(MenuPage.Free, state.Page);
        Tap(state, MenuLayout.ModeRowCenter((int)MatchMode.Spectate));
        Assert.AreEqual(MenuPage.Mode, state.Page);
        Assert.AreEqual(MatchMode.Spectate, state.Mode);

        // ▶ を 1 回押す: 決まりだけが変わり、ページは変わらない（行の決定に数えない）。
        Tap(state, MouseLayout.ModeArrow(1));
        Assert.AreEqual(MenuPage.Mode, state.Page);
        Assert.AreEqual(1, state.PracticeRules);
        Tap(state, MouseLayout.ModeArrow(-1));
        Assert.AreEqual(0, state.PracticeRules);

        // 押しっぱなし: 遅れ（RepeatDelay）の間は 1 つだけ、過ぎたら連続する。
        var rules = state.Organizations.Count + 1;
        state.Apply(Click(MouseLayout.ModeArrow(1)));
        state.Apply(Hold(MouseLayout.ModeArrow(1), MouseLayout.RepeatDelay * 0.9f));
        Assert.AreEqual(1, state.PracticeRules, "one step per press until the repeat delay passes");
        state.Apply(Hold(MouseLayout.ModeArrow(1), (MouseLayout.RepeatDelay * 0.1f) + 0.001f));
        Assert.AreEqual(2 % rules, state.PracticeRules, "the repeat starts after the delay");
        state.Apply(Up);
        state.Apply(Hold(MouseLayout.ModeArrow(1), 1f));
        Assert.AreEqual(2 % rules, state.PracticeRules, "holding without a press on the arrow does nothing");

        Tap(state, MenuLayout.ModeRowCenter(state.ModeRows - 1));
        Assert.AreEqual(MenuPage.Free, state.Page, "the back row");
        Tap(state, MenuLayout.ModeRowCenter(MenuState.FreeRows - 1));
        Assert.AreEqual(MenuPage.Top, state.Page);
    }

    [TestMethod]
    public void Select_ClickLocks_TheCornerBackUnlocksThenLeaves()
    {
        var state = new MenuState(CharacterRoster.All, MenuSettings.Default, 0, 7);
        Tap(state, MenuLayout.TopButtonCenter((int)TopItem.FreeBattle));
        Tap(state, MenuLayout.ModeRowCenter(0));
        Tap(state, MenuLayout.ModeRowCenter(0));
        Assert.AreEqual(MenuPage.Select, state.Page);
        var visible = state.Grid.VisibleRows().SelectMany(v => state.Grid.Rows[v.Row].Members).ToList();
        Tap(state, state.Grid.TileCenter(visible[2])!.Value);
        Assert.IsTrue(state.RedLocked);
        Tap(state, MouseLayout.CornerBackCenter);
        Assert.IsFalse(state.RedLocked, "the corner back undoes the last choice first");
        Assert.AreEqual(MenuPage.Select, state.Page);
        Tap(state, MouseLayout.CornerBackCenter);
        Assert.AreEqual(MenuPage.Mode, state.Page);
    }

    [TestMethod]
    public void Replays_RowsAreClicked_WheelScrolls_TheCornerBackLeaves()
    {
        var state = new MenuState(CharacterRoster.All, MenuSettings.Default);
        Assert.AreEqual(MenuAction.BrowseReplays, Tap(state, MenuLayout.TopButtonCenter((int)TopItem.Replays)).Action);
        state.SetReplayCount(8);
        state.Apply(new MenuInput(Scroll: 1));
        Assert.AreEqual(1, state.ReplayFocus);
        var open = Tap(state, MenuLayout.ListRowCenter(2));
        Assert.AreEqual(MenuAction.OpenReplay, open.Action);
        Assert.AreEqual(state.FirstRecord + 2, open.ReplayIndex);
        Tap(state, MouseLayout.CornerBackCenter);
        Assert.AreEqual(MenuPage.Top, state.Page);
    }

    [TestMethod]
    public void Result_TheButtonsAreClicked()
    {
        var state = new MenuState(CharacterRoster.All, MenuSettings.Default);
        state.ShowResult(new BoutSummary("RED", "BLUE", 0, "判定", 3, "3:00", Campaign: false));
        state.Apply(Hover(MenuLayout.ResultButtonCenter(1, 3)));
        Assert.AreEqual(1, state.ResultIndex);
        Tap(state, MenuLayout.ResultButtonCenter(1, 3));
        Assert.AreEqual(MenuPage.Select, state.Page);
    }

    /// <summary>キャンペーン: 作る（◀ ▶ と押しっぱなし）→ 本拠地 → 団体と大会 → 道 → 本拠地 → 減量（スライダー・±kg・階級の表）→ 決める を、マウスだけで。</summary>
    [TestMethod]
    public void Campaign_CreateThenCutWeightWithTheSlider_MouseOnly()
    {
        var (menu, dir) = NewMenu();
        try
        {
            var state = menu.State;
            Tap(menu, MenuLayout.TopButtonCenter((int)TopItem.Campaign));
            Assert.AreEqual(MenuPage.Campaign, state.Page);
            Tap(menu, MenuLayout.CampaignRowCenter(state.Boxers.Count)); // ＋ 新しく作る
            Assert.AreEqual(MenuPage.Create, state.Page);

            // 体型: ▶ を押す。身長: ▶ を押し続けると連続して伸びる。
            var type = state.BodyTypeIndex;
            Tap(menu, MouseLayout.CreateArrow(0, 1));
            Assert.AreEqual((type + 1) % CharacterMaker.BodyTypes.Count, state.BodyTypeIndex);
            Assert.AreEqual(MenuPage.Create, state.Page, "an arrow never confirms the row");
            var height = state.HeightCm;
            menu.Handle(Click(MouseLayout.CreateArrow(1, 1)));
            for (var i = 0; i < 60; i++) { menu.Handle(Hold(MouseLayout.CreateArrow(1, 1))); }
            menu.Handle(Up);
            Assert.IsGreaterThan(height + 3, state.HeightCm, "one second of holding grows the height several steps");
            Assert.AreEqual(1, state.CreateIndex, "the hovered row follows the arrow");

            Tap(menu, MenuLayout.CampaignRowCenter(3)); // このボクサーで作る
            Assert.AreEqual(MenuPage.Boxer, state.Page);
            Assert.AreEqual(1, state.Boxers.Count);

            // 団体と大会 → IVF の道 → 段を押す（出られれば次の試合になって本拠地へ）→ 戻るのボタン。
            Tap(menu, CampaignLayout.HomeCenter(MenuState.HomeOrgs));
            Assert.AreEqual(MenuPage.Orgs, state.Page);
            Tap(menu, CampaignLayout.OrgCenter(0));
            Assert.AreEqual(MenuPage.Path, state.Page);
            Tap(menu, CampaignLayout.PathBackCenter);
            Assert.AreEqual(MenuPage.Orgs, state.Page);
            Tap(menu, CampaignLayout.OrgBackCenter);
            Assert.AreEqual(MenuPage.Boxer, state.Page);

            // 減量・増量。
            Tap(menu, CampaignLayout.HomeCenter(MenuState.HomeWeight));
            Assert.AreEqual(MenuPage.Weight, state.Page);
            var boxer = state.Boxer!;
            var (lo, hi) = boxer.WeightRange;

            // スライダー: 左の端を押す = 作れる一番軽い体重。押したまま右へ動かすと付いてくる。押しても決めない。
            menu.Handle(Click(new Vector2(CampaignLayout.SliderLeft, CampaignLayout.SliderY)));
            Assert.AreEqual(lo, state.TargetKg);
            Assert.AreEqual(MenuPage.Weight, state.Page, "pressing the slider does not confirm");
            var wanted = boxer.WeightKg - 4;
            menu.Handle(Hold(new Vector2(CampaignLayout.SliderX(wanted, lo, hi), CampaignLayout.SliderY - 30f))); // ドラッグ中は溝から外れても付いてくる
            Assert.AreEqual(wanted, state.TargetKg);
            menu.Handle(Up);
            menu.Handle(Hover(new Vector2(CampaignLayout.SliderX(hi, lo, hi), CampaignLayout.SliderY)));
            Assert.AreEqual(wanted, state.TargetKg, "after release, hovering the slider does not move it");

            // ±kg のボタン（−1 を 1 回・+1 を押しっぱなし）。
            var minusOne = CampaignLayout.WeightSteps.ToList().IndexOf(-1);
            var plusOne = CampaignLayout.WeightSteps.ToList().IndexOf(1);
            Tap(menu, CampaignLayout.StepCenter(minusOne));
            Assert.AreEqual(wanted - 1, state.TargetKg);
            menu.Handle(Click(CampaignLayout.StepCenter(plusOne)));
            for (var i = 0; i < 30; i++) { menu.Handle(Hold(CampaignLayout.StepCenter(plusOne))); }
            menu.Handle(Up);
            Assert.IsGreaterThan(wanted, state.TargetKg, "holding +1 repeats");

            // 階級の表: 今より軽い階級の行を押すと、その階級の上限へ。
            var org = MenuState.WeightOrg;
            var lighter = org.Divisions.Select((d, i) => (d, i)).LastOrDefault(p => p.d.MaxWholeKg is { } limit && limit < state.TargetKg && limit >= lo);
            if (lighter.d is { MaxWholeKg: { } top })
            {
                Tap(menu, CampaignLayout.DivisionRowCenter(lighter.i));
                Assert.AreEqual(top, state.TargetKg);
                Assert.AreEqual(lighter.d, org.DivisionOf(state.TargetKg));
            }

            var target = state.TargetKg;
            Assert.AreNotEqual(boxer.WeightKg, target);
            Tap(menu, CampaignLayout.WeightRowCenter(1)); // 決める
            Assert.AreEqual(MenuPage.Boxer, state.Page);
            Assert.AreEqual(target, state.Boxer!.WeightKg);
            Assert.AreEqual(target, new CampaignStore(Path.Combine(dir, "campaign")).LoadAll().Single().WeightKg, "saved to the file");

            // 戻るのボタン（変えずに戻る）・右クリックで一覧へ。
            Tap(menu, CampaignLayout.HomeCenter(MenuState.HomeWeight));
            Tap(menu, CampaignLayout.WeightRowCenter(2));
            Assert.AreEqual(MenuPage.Boxer, state.Page);
            Assert.AreEqual(target, state.Boxer!.WeightKg);
            menu.Handle(RightClick);
            Assert.AreEqual(MenuPage.Campaign, state.Page);
        }
        finally
        {
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
    }

    [TestMethod]
    public void Campaign_HomeWeaponArrows_AndDeleteTakesTwoClicks()
    {
        var (menu, dir) = NewMenu();
        try
        {
            var state = menu.State;
            Tap(menu, MenuLayout.TopButtonCenter((int)TopItem.Campaign));
            Tap(menu, MenuLayout.CampaignRowCenter(0));
            Tap(menu, MenuLayout.CampaignRowCenter(3));
            Assert.AreEqual(MenuPage.Boxer, state.Page);

            // 武器を選べる大会（VFC など）を次の試合にして、札の ◀ ▶ で武器を回す。
            var org = CampaignPlan.Orgs.ToList().FindIndex(o => state.Stops.Any(s => s.Open && s.Event.Org == o.Id && s.Entry is { Weapons.Count: > 1 }));
            if (org >= 0)
            {
                Tap(menu, CampaignLayout.HomeCenter(MenuState.HomeOrgs));
                Tap(menu, CampaignLayout.OrgCenter(org));
                var node = state.PathStops.ToList().FindIndex(s => s.Open && s.Entry is { Weapons.Count: > 1 });
                Tap(menu, CampaignLayout.NodeCenter(node));
                Assert.AreEqual(MenuPage.Boxer, state.Page);
                Assert.IsTrue(state.WeaponChoosable);
                var entry = state.Next!.Entry!;
                var before = state.WeaponFor(entry);
                Tap(menu, MouseLayout.WeaponArrow(1));
                Assert.AreEqual(MenuPage.Boxer, state.Page, "the weapon arrow does not start the bout");
                Assert.AreNotEqual(before, state.WeaponFor(entry));
            }

            Tap(menu, CampaignLayout.HomeCenter(MenuState.HomeDelete));
            Assert.AreEqual(1, state.Boxers.Count, "the first click only asks");
            Tap(menu, CampaignLayout.HomeCenter(MenuState.HomeDelete));
            Assert.AreEqual(0, state.Boxers.Count);
            Assert.AreEqual(MenuPage.Campaign, state.Page);
        }
        finally
        {
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
    }

    [TestMethod]
    public void KeyRepeat_HoldingAKeyRepeatsAfterTheDelay_ReleasingStops()
    {
        var repeat = new MatchMenu.KeyRepeat();
        repeat.Start(1);
        Assert.AreEqual(0, repeat.Step(1, MouseLayout.RepeatDelay * 0.9f));
        Assert.AreEqual(1, repeat.Step(1, MouseLayout.RepeatDelay * 0.2f));
        var count = 0;
        for (var i = 0; i < 60; i++) { count += repeat.Step(1, Frame); }
        Assert.IsGreaterThan(8, count, "about 14 steps per second");
        Assert.AreEqual(0, repeat.Step(0, Frame), "released");
        Assert.AreEqual(0, repeat.Step(1, 1f), "holding again without a fresh press does not repeat");
    }
}
