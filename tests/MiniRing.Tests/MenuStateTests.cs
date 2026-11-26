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

    /// <summary>赤（主の入力）のカーソルを、画面の並びで右へ送って <paramref name="id"/> の人まで動かす。</summary>
    private static void RedTo(MenuState state, string id)
    {
        for (var i = 0; i < state.Roster.Count && state.Roster[state.RedCursor].Id != id; i++) { state.Apply(Dir(x: 1)); }
        Assert.AreEqual(id, state.Roster[state.RedCursor].Id);
    }

    /// <summary>ゲームモードの行（既定は練習試合、<paramref name="tournament"/> なら団体の大会）を決めてキャラ選択へ。</summary>
    private static MenuState AtSelect(MatchMode mode, MenuSettings? settings = null, string? tournament = null)
    {
        var state = NewState(settings);
        // 最初の画面: 並び（フリーバトル・キャンペーン / リプレイ・設定 / 終了）の先頭（フリーバトル）を決定し、誰が操るか（mode）の行を選ぶ。
        Assert.AreEqual((int)TopItem.FreeBattle, state.TopIndex);
        state.Apply(Confirm);
        Assert.AreEqual(MenuPage.Free, state.Page);
        for (var i = 0; i < (int)mode; i++) { state.Apply(Dir(y: 1)); }
        Assert.AreEqual((int)mode, state.FreeIndex);
        state.Apply(Confirm);
        Assert.AreEqual(MenuPage.Mode, state.Page);
        if (tournament is not null)
        {
            while (state.ModeAt(state.ModeIndex)?.Org?.Id != tournament) { state.Apply(Dir(y: 1)); }
        }

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
        Assert.AreEqual((int)TopItem.Campaign, state.TopIndex);
        state.Apply(Dir(x: 1));
        Assert.AreEqual((int)TopItem.FreeBattle, state.TopIndex, "wraps within the row");
        state.Apply(Dir(y: -1));
        Assert.AreEqual((int)TopItem.Quit, state.TopIndex, "wraps to the last row (only Quit there)");
        state.Apply(Dir(x: 1));
        Assert.AreEqual((int)TopItem.Quit, state.TopIndex, "the empty cell next to Quit is never selected");
        state.Apply(Dir(y: 1));
        Assert.AreEqual((int)TopItem.FreeBattle, state.TopIndex);
    }

    [TestMethod]
    public void Single_PicksRedThenBlue_BackStepsOneAtATime_AndTheMatchGetsThePickedWeapons()
    {
        var state = AtSelect(MatchMode.Single);
        // 赤 = sean（こんぼう）。
        RedTo(state, "sean");
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
        Assert.AreEqual(MenuPage.Mode, state.Page, "back from the first pick returns to the game mode");
        state.Apply(Cancel);
        Assert.AreEqual(MenuPage.Free, state.Page, "then to the free battle page");
        Assert.AreEqual((int)MatchMode.Spectate, state.FreeIndex, "the cursor stays on the row we came from");
        state.Apply(Cancel);
        Assert.AreEqual(MenuPage.Top, state.Page);
        Assert.IsFalse(state.QuitRequested);
        Assert.AreEqual((int)TopItem.FreeBattle, state.TopIndex, "the cursor stays on the button we came from");
    }

    [TestMethod]
    public void Versus_ASecondPadMovesAndLocksBlueAtTheSameTime()
    {
        var state = AtSelect(MatchMode.Versus);
        // 赤: キーボード / 1 つ目のパッドで konrad。青: 2 つ目のパッドで 1 つ左へ（画面の並びで 1 つ前）。
        RedTo(state, "konrad");
        var expectedBlue = state.Grid.Move(state.BlueCursor, -1, 0);
        var pad2 = new MenuInput(HasSecondPad: true, X2: -1);
        state.Apply(pad2);
        Assert.AreEqual(expectedBlue, state.BlueCursor, "the second pad moved blue one to the left");
        Assert.AreEqual("konrad", state.Roster[state.RedCursor].Id, "red did not move with the second pad");
        state.Apply(new MenuInput(HasSecondPad: true, Confirm2: true));
        Assert.IsTrue(state.BlueLocked);
        Assert.IsFalse(state.RedLocked);
        var start = state.Apply(new MenuInput(Confirm: true, HasSecondPad: true));
        Assert.AreEqual(MenuAction.StartMatch, start.Action);
        Assert.AreEqual("konrad", start.Match!.Red.Id);
        Assert.AreEqual(state.Roster[expectedBlue].Id, start.Match.Blue.Id);
        Assert.IsTrue(start.Match.Corners.ExternalBlue, "versus: both sides are people");
        Assert.AreEqual(WeaponKind.Longsword, start.Match.Corners.PlayerWeapon);
    }

    [TestMethod]
    public void Versus_WithoutASecondPad_TheKeyboardPicksInOrder()
    {
        var state = AtSelect(MatchMode.Versus);
        state.Apply(Confirm);
        Assert.IsTrue(state.RedLocked);
        var expectedBlue = state.Grid.Move(state.BlueCursor, -1, 0);
        state.Apply(Dir(x: -1)); // 次の入力は青
        Assert.AreEqual(expectedBlue, state.BlueCursor);
        Assert.AreEqual(MenuAction.StartMatch, state.Apply(Confirm).Action);
    }

    [TestMethod]
    public void Select_ThePointerChoosesATile_AndAClickDecides()
    {
        var state = AtSelect(MatchMode.Single);
        var visible = state.Grid.VisibleRows().SelectMany(v => state.Grid.Rows[v.Row].Members).ToList();
        var (first, second) = (visible[4], visible[1]);
        var tile = state.Grid.TileCenter(first)!.Value;
        state.Apply(new MenuInput(Pointer: tile));
        Assert.AreEqual(first, state.RedCursor);
        Assert.IsFalse(state.RedLocked, "hovering alone only moves the cursor");
        state.Apply(new MenuInput(Pointer: tile, Click: true));
        Assert.IsTrue(state.RedLocked);
        var outcome = state.Apply(new MenuInput(Pointer: state.Grid.TileCenter(second)!.Value, Click: true));
        Assert.AreEqual(MenuAction.StartMatch, outcome.Action);
        Assert.AreEqual(state.Roster[first].Id, outcome.Match!.Red.Id);
        Assert.AreEqual(state.Roster[second].Id, outcome.Match.Blue.Id);
    }

    [TestMethod]
    public void Select_LeftRightFollowTheScreenOrder_UpDownChangeTheRow_AndTheCursorIsAlwaysOnScreen()
    {
        var state = AtSelect(MatchMode.Single);
        var grid = state.Grid;
        var order = grid.Order.ToList();
        var start = state.RedCursor;
        state.Apply(Dir(x: -1));
        Assert.AreEqual(order[(order.IndexOf(start) + order.Count - 1) % order.Count], state.RedCursor, "left steps back in the screen order (wrapping)");
        // 全員を順に回っても、カーソルの札はいつも見えている（送りが付いてくる）。
        for (var i = 0; i < order.Count; i++)
        {
            state.Apply(Dir(x: 1));
            Assert.IsNotNull(grid.TileCenter(state.RedCursor), $"{state.Roster[state.RedCursor].Id} is on screen");
        }

        // 上下: 別の札の行へ移り、そこでも見えている。
        var rowY = grid.TileCenter(state.RedCursor)!.Value.Y;
        state.Apply(Dir(y: 1));
        Assert.IsNotNull(grid.TileCenter(state.RedCursor));
        Assert.AreNotEqual(rowY, grid.TileCenter(state.RedCursor)!.Value.Y, "down changes the row");
    }

    [TestMethod]
    public void Settings_TogglingTheReferee_ChangesWhatTheMatchGets_AndReportsAChange()
    {
        var state = NewState();
        state.Apply(Dir(x: 1, y: 1)); // 設定のボタン（2 行目の右）
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
        state.Apply(Dir(x: -1, y: -1)); // フリーバトルへ（設定の 1 行上の左）
        Assert.AreEqual((int)TopItem.FreeBattle, state.TopIndex);
        state.Apply(Confirm); // フリーバトル（シングルの行）
        state.Apply(Confirm); // ゲームモードへ
        state.Apply(Confirm); // 練習試合
        state.Apply(Confirm);
        var start = state.Apply(Confirm);
        Assert.IsFalse(start.Match!.Referee);
        Assert.IsFalse(start.Match.Corners.Referee);
    }

    [TestMethod]
    public void Replays_BrowseThenMoveAndOpen_BackReturnsToTheTopMenu()
    {
        var state = NewState();
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
        Assert.Contains("フリーバトル", menu.Text(size).Select(t => t.Text).ToList());
        Assert.Contains("キャンペーン", menu.Text(size).Select(t => t.Text).ToList());
        menu.State.Apply(Confirm);
        Assert.AreEqual(MenuPage.Free, menu.State.Page);
        Assert.Contains("シングルプレイ", menu.Text(size).Select(t => t.Text).ToList());
        menu.State.Apply(Confirm);
        Assert.AreEqual(MenuPage.Mode, menu.State.Page);
        Assert.Contains("練習試合", menu.Text(size).Select(t => t.Text).ToList());
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
        var other = state.Grid.VisibleRows().SelectMany(v => state.Grid.Rows[v.Row].Members).First(i => i != blue);
        state.Apply(new MenuInput(Pointer: state.Grid.TileCenter(other)!.Value, Click: true, HasSecondPad: true));
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
        state.Apply(Confirm); // フリーバトル（対戦の行のまま）
        state.Apply(Confirm); // ゲームモードへ
        state.Apply(Confirm); // 練習試合
        state.Apply(new MenuInput(HasSecondPad: true, Confirm2: true));
        Assert.IsTrue(state.BlueLocked);
        state.Apply(new MenuInput(Cancel: true, HasSecondPad: true));
        Assert.IsTrue(state.BlueLocked, "P1's back must not undo P2's pick");
    }
}
