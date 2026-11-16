using System.Numerics;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>2026-11-16: メニューの状態遷移（<see cref="MenuState"/>。InputSnapshot に依らない純粋な部分）と設定のファイル。</summary>
[TestClass]
public sealed class MenuStateTests
{
    private static MenuState NewState(MenuSettings? settings = null) => new(CharacterRoster.All, settings ?? MenuSettings.Default, 0, 7);

    private static readonly MenuInput Confirm = new(Confirm: true);
    private static readonly MenuInput Cancel = new(Cancel: true);
    private static MenuInput Dir(int x = 0, int y = 0) => new(x, y);

    private static MenuState AtSelect(MatchMode mode, MenuSettings? settings = null)
    {
        var state = NewState(settings);
        // 最初の画面: 並び（シングル・対戦 / 観戦・リプレイ / 設定・終了）で、mode のボタンまで動かして決定する。
        var index = (int)mode;
        if (index % 2 == 1) { state.Apply(Dir(x: 1)); }
        for (var i = 0; i < index / 2; i++) { state.Apply(Dir(y: 1)); }
        Assert.AreEqual(index, state.TopIndex);
        state.Apply(Confirm);
        Assert.AreEqual(MenuPage.Select, state.Page);
        Assert.AreEqual(mode, state.Mode);
        return state;
    }

    [TestMethod]
    public void Top_BackNeverQuits_ItOnlyMovesToTheQuitButton_AndQuitNeedsADecision()
    {
        var state = NewState();
        var outcome = state.Apply(Cancel);
        Assert.AreEqual(MenuAction.None, outcome.Action);
        Assert.IsFalse(state.QuitRequested, "B on the top menu must not close the game (it did, right after returning from a match)");
        Assert.AreEqual((int)TopItem.Quit, state.TopIndex);
        Assert.IsFalse(state.Apply(Cancel).Action == MenuAction.Quit);
        Assert.IsFalse(state.QuitRequested);
        Assert.AreEqual(MenuAction.Quit, state.Apply(Confirm).Action);
        Assert.IsTrue(state.QuitRequested);
    }

    [TestMethod]
    public void Top_MovesAroundTheTwoByThreeGrid_AndWraps()
    {
        var state = NewState();
        state.Apply(Dir(x: 1));
        Assert.AreEqual((int)TopItem.Versus, state.TopIndex);
        state.Apply(Dir(x: 1));
        Assert.AreEqual((int)TopItem.Single, state.TopIndex, "wraps within the row");
        state.Apply(Dir(y: -1));
        Assert.AreEqual((int)TopItem.Settings, state.TopIndex, "wraps to the last row");
        state.Apply(Dir(y: 1));
        Assert.AreEqual((int)TopItem.Single, state.TopIndex);
    }

    [TestMethod]
    public void Single_PicksRedThenBlue_BackStepsOneAtATime_AndTheMatchGetsThePickedWeapons()
    {
        var state = AtSelect(MatchMode.Single);
        // 赤 = sean（こんぼう）: 先頭の jin から 3 つ右。
        state.Apply(Dir(x: 3));
        Assert.AreEqual("sean", state.Roster[state.RedCursor].Id);
        Assert.IsFalse(state.Apply(Confirm).Action == MenuAction.StartMatch, "red alone does not start");
        Assert.IsTrue(state.RedLocked);
        Assert.IsTrue(state.BlueActive);
        // 次の操作は青を動かす（赤は動かない）。
        var red = state.RedCursor;
        state.Apply(Dir(x: 1));
        Assert.AreEqual(red, state.RedCursor);

        // 戻る: 青は未決定なので、赤を解く。もう一度で最初の画面。選んだ人の位置は残る。
        state.Apply(Cancel);
        Assert.IsFalse(state.RedLocked);
        Assert.AreEqual(MenuPage.Select, state.Page);
        state.Apply(Confirm); // 赤を決め直す
        var start = state.Apply(Confirm);
        Assert.AreEqual(MenuAction.StartMatch, start.Action);
        Assert.AreEqual("sean", start.Match!.Red.Id);
        Assert.AreEqual(state.Roster[state.BlueCursor].Id, start.Match.Blue.Id);
        Assert.AreEqual(WeaponKind.WoodenClub, start.Match.Corners.PlayerWeapon, "the picked character's weapon goes to the match (no weapon row)");
        Assert.AreEqual(MatchMode.Single, start.Match.Mode);
        Assert.IsTrue(start.Match.Referee);
    }

    [TestMethod]
    public void Select_BackFromTheFirstPickGoesToTheTopMenu_WithoutQuitting()
    {
        var state = AtSelect(MatchMode.Spectate);
        state.Apply(Cancel);
        Assert.AreEqual(MenuPage.Top, state.Page);
        Assert.IsFalse(state.QuitRequested);
        Assert.AreEqual((int)TopItem.Spectate, state.TopIndex, "the cursor stays on the button we came from");
    }

    [TestMethod]
    public void Versus_ASecondPadMovesAndLocksBlueAtTheSameTime()
    {
        var state = AtSelect(MatchMode.Versus);
        // 赤: キーボード / 1 つ目のパッドで konrad（2 つ右）。青: 2 つ目のパッドで 1 つ左へ（青の初期 marcus = 7 → 6）。
        state.Apply(Dir(x: 2));
        var pad2 = new MenuInput(HasSecondPad: true, X2: -1);
        state.Apply(pad2);
        Assert.AreEqual(6, state.BlueCursor, "marcus (7) moved one to the left");
        Assert.AreEqual("konrad", state.Roster[state.RedCursor].Id, "red did not move with the second pad");
        state.Apply(new MenuInput(HasSecondPad: true, Confirm2: true));
        Assert.IsTrue(state.BlueLocked);
        Assert.IsFalse(state.RedLocked);
        var start = state.Apply(new MenuInput(Confirm: true, HasSecondPad: true));
        Assert.AreEqual(MenuAction.StartMatch, start.Action);
        Assert.AreEqual("konrad", start.Match!.Red.Id);
        Assert.AreEqual(state.Roster[6].Id, start.Match.Blue.Id);
        Assert.IsTrue(start.Match.Corners.ExternalBlue, "versus: both sides are people");
        Assert.AreEqual(WeaponKind.Longsword, start.Match.Corners.PlayerWeapon);
    }

    [TestMethod]
    public void Versus_WithoutASecondPad_TheKeyboardPicksInOrder()
    {
        var state = AtSelect(MatchMode.Versus);
        state.Apply(Confirm);
        Assert.IsTrue(state.RedLocked);
        state.Apply(Dir(x: -1)); // 次の入力は青
        Assert.AreEqual(6, state.BlueCursor);
        Assert.AreEqual(MenuAction.StartMatch, state.Apply(Confirm).Action);
    }

    [TestMethod]
    public void Select_ThePointerChoosesATile_AndAClickDecides()
    {
        var state = AtSelect(MatchMode.Single);
        var tile = MenuLayout.TileCenter(4);
        state.Apply(new MenuInput(Pointer: tile));
        Assert.AreEqual(4, state.RedCursor);
        Assert.IsFalse(state.RedLocked, "hovering alone only moves the cursor");
        state.Apply(new MenuInput(Pointer: tile, Click: true));
        Assert.IsTrue(state.RedLocked);
        var outcome = state.Apply(new MenuInput(Pointer: MenuLayout.TileCenter(10), Click: true));
        Assert.AreEqual(MenuAction.StartMatch, outcome.Action);
        Assert.AreEqual("vikram", outcome.Match!.Red.Id);
        Assert.AreEqual("joao", outcome.Match.Blue.Id);
    }

    [TestMethod]
    public void Select_MovesWithinARow_AndBetweenTheTwoRows()
    {
        var state = AtSelect(MatchMode.Single);
        state.Apply(Dir(x: -1));
        Assert.AreEqual(5, state.RedCursor, "wraps to the end of the first row");
        state.Apply(Dir(y: 1));
        Assert.AreEqual(11, state.RedCursor);
        state.Apply(Dir(x: 1));
        Assert.AreEqual(6, state.RedCursor, "wraps within the second row");
        state.Apply(Dir(y: -1));
        Assert.AreEqual(0, state.RedCursor);
    }

    [TestMethod]
    public void Settings_TogglingTheReferee_ChangesWhatTheMatchGets_AndReportsAChange()
    {
        var state = NewState();
        state.Apply(Dir(y: 2)); // 設定のボタン（3 行目の左）
        Assert.AreEqual((int)TopItem.Settings, state.TopIndex);
        state.Apply(Confirm);
        Assert.AreEqual(MenuPage.Settings, state.Page);
        Assert.IsTrue(state.Settings.Referee);
        var toggled = state.Apply(Confirm);
        Assert.IsTrue(toggled.SettingsChanged);
        Assert.IsFalse(state.Settings.Referee);
        Assert.IsTrue(state.Apply(Dir(x: 1)).SettingsChanged, "left/right toggles too");
        Assert.IsTrue(state.Settings.Referee);
        state.Apply(Confirm); // なしにする
        Assert.IsFalse(state.Settings.Referee);

        // 戻る行で最初の画面へ。Esc でも戻る（終了しない）。
        state.Apply(Dir(y: 1));
        state.Apply(Confirm);
        Assert.AreEqual(MenuPage.Top, state.Page);
        state.Apply(Confirm); // 設定のボタンのまま → 設定へ
        state.Apply(Cancel);
        Assert.AreEqual(MenuPage.Top, state.Page);
        Assert.IsFalse(state.QuitRequested);

        // 試合へ渡る。
        state.Apply(Dir(y: -1)); // シングルへ（設定の 1 行上 = 観戦。2 回で先頭）
        state.Apply(Dir(y: -1));
        Assert.AreEqual((int)TopItem.Single, state.TopIndex);
        state.Apply(Confirm);
        state.Apply(Confirm);
        var start = state.Apply(Confirm);
        Assert.IsFalse(start.Match!.Referee);
        Assert.IsFalse(start.Match.Corners.Referee);
    }

    [TestMethod]
    public void Replays_BrowseThenMoveAndOpen_BackReturnsToTheTopMenu()
    {
        var state = NewState();
        state.Apply(Dir(x: 1));
        state.Apply(Dir(y: 1));
        Assert.AreEqual((int)TopItem.Replays, state.TopIndex);
        var browse = state.Apply(Confirm);
        Assert.AreEqual(MenuAction.BrowseReplays, browse.Action);
        Assert.AreEqual(MenuPage.Replays, state.Page);
        Assert.AreEqual(MenuAction.None, state.Apply(Dir(y: 1)).Action, "nothing to move over while the list is empty");
        state.SetReplayCount(8);
        state.Apply(new MenuInput(Scroll: 1));
        state.Apply(Dir(y: 1));
        Assert.AreEqual(2, state.ReplayFocus);
        var open = state.Apply(Confirm);
        Assert.AreEqual(MenuAction.OpenReplay, open.Action);
        Assert.AreEqual(2, open.ReplayIndex);
        state.Apply(Dir(y: -1));
        state.Apply(Dir(y: -1));
        state.Apply(Dir(y: -1));
        Assert.AreEqual(7, state.ReplayFocus, "wraps to the last record");
        state.Apply(Cancel);
        Assert.AreEqual(MenuPage.Top, state.Page);
    }

    [TestMethod]
    public void Return_AfterAMatch_GoesToTheTopMenu_AndClearsThePicks()
    {
        var state = AtSelect(MatchMode.Single);
        state.Apply(Confirm);
        state.Apply(Confirm);
        state.Return();
        Assert.AreEqual(MenuPage.Top, state.Page);
        Assert.IsFalse(state.RedLocked);
        Assert.IsFalse(state.BlueLocked);
    }

    [TestMethod]
    public void Settings_AreStoredAsJson_AndABrokenFileFallsBackToTheDefaultWithALog()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"miniring-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "settings.json");
        var logs = new List<string>();
        try
        {
            Assert.AreEqual(MenuSettings.Default, MenuSettingsStore.Load(path, logs.Add), "no file yet is the first run");
            Assert.IsEmpty(logs);
            MenuSettingsStore.Save(path, new MenuSettings(Referee: false), logs.Add);
            Assert.IsEmpty(logs);
            Assert.IsFalse(MenuSettingsStore.Load(path, logs.Add).Referee, "survives a restart");

            foreach (var broken in new[] { "{ not json", "{\"referee\": \"yes\"}", "{\"referee\": false, \"extra\": 1}", "null" })
            {
                File.WriteAllText(path, broken);
                logs.Clear();
                Assert.AreEqual(MenuSettings.Default, MenuSettingsStore.Load(path, logs.Add), broken);
                Assert.HasCount(1, logs, broken);
                Assert.Contains("既定", logs[0]);
            }
        }
        finally
        {
            if (Directory.Exists(dir)) { Directory.Delete(dir, recursive: true); }
        }
    }

    [TestMethod]
    public void View_BuildsEveryPage_WithIconsAndText_WithoutPortraits()
    {
        using var menu = new MatchMenu("none", Path.Combine(Path.GetTempPath(), $"miniring-view-{Guid.NewGuid():N}.json"), loadPortraits: false);
        var size = new Vector2(960, 540);
        var topShapes = menu.Shapes(size).Count;
        Assert.IsGreaterThan(100, topShapes, "icons are many shapes");
        Assert.Contains("シングルプレイ", menu.Text(size).Select(t => t.Text).ToList());
        menu.State.Apply(Confirm);
        Assert.AreEqual(MenuPage.Select, menu.State.Page);
        Assert.Contains("キャラクターセレクト", menu.Text(size).Select(t => t.Text).ToList());
        Assert.IsGreaterThan(topShapes / 2, menu.Shapes(size).Count);
    }

    [TestMethod]
    public void Versus_WithASecondPad_BlueIsDrivenAndConfirmedOnlyByPadTwo()
    {
        var state = AtSelect(MatchMode.Versus);
        state.Apply(new MenuInput(Confirm: true, HasSecondPad: true)); // 赤を決める
        Assert.IsTrue(state.RedLocked);
        var blue = state.BlueCursor;
        // 1P の入力（移動・決定・ポインタのクリック）では青を動かせず、決められない。
        state.Apply(new MenuInput(X: 1, HasSecondPad: true));
        state.Apply(new MenuInput(Confirm: true, HasSecondPad: true));
        state.Apply(new MenuInput(Pointer: MenuLayout.TileCenter(2), Click: true, HasSecondPad: true));
        Assert.AreEqual(blue, state.BlueCursor);
        Assert.IsFalse(state.BlueLocked);
        // 2P のパッドなら動かせて、決められる。
        state.Apply(new MenuInput(HasSecondPad: true, X2: 1));
        Assert.AreNotEqual(blue, state.BlueCursor);
        Assert.AreEqual(MenuAction.None, state.Apply(new MenuInput(HasSecondPad: true, Confirm2: false)).Action);
        Assert.AreEqual(MenuAction.StartMatch, state.Apply(new MenuInput(HasSecondPad: true, Confirm2: true)).Action);
    }

    [TestMethod]
    public void Versus_WithASecondPad_PadOnesBackDoesNotUnlockBlue()
    {
        var state = AtSelect(MatchMode.Versus);
        state.Apply(new MenuInput(Confirm: true, HasSecondPad: true));
        state.Apply(new MenuInput(HasSecondPad: true, Confirm2: true, Confirm: false));
        // 青が決まれば試合が始まっているので、先に青だけ決めた状態を作り直す。
        state.Return();
        state.Apply(Confirm);
        state.Apply(new MenuInput(HasSecondPad: true, Confirm2: true));
        Assert.IsTrue(state.BlueLocked);
        state.Apply(new MenuInput(Cancel: true, HasSecondPad: true));
        Assert.IsTrue(state.BlueLocked, "P1's back must not undo P2's pick");
    }
}
