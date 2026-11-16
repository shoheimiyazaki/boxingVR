using System.Numerics;
using MiniRing.Characters;

namespace MiniRing.Desktop;

/// <summary>メニューの画面（ページ）。</summary>
internal enum MenuPage { Top, Settings, Select, Replays }

/// <summary>最初の画面のボタン（表示の並び: 2 列 × 3 行）。</summary>
internal enum TopItem { Single, Versus, Spectate, Replays, Settings, Quit }

/// <summary>メニューが外へ頼むこと。</summary>
internal enum MenuAction { None, BrowseReplays, OpenReplay, StartMatch, Quit }

/// <summary>鳴らす合図（カーソルが動いた・決定）。</summary>
internal enum MenuCue { None, Move, Confirm }

/// <summary>
/// メニューへの入力（<c>InputSnapshot</c> に依らない最小の形）。上下左右は 1 回の押し下げを −1 / 0 / +1（右・下が +）。
/// <paramref name="Pointer"/> は指している位置（設計の画面 640 × 464 の座標。動いていない・指していなければ null）。
/// <paramref name="Scroll"/> は一覧の送り（+ が下）。2 つ目のパッド（<paramref name="X2"/> 以降）は、対戦のキャラ選択で青を同時に動かす。
/// </summary>
internal readonly record struct MenuInput(int X = 0, int Y = 0, bool Confirm = false, bool Cancel = false,
    Vector2? Pointer = null, bool Click = false, int Scroll = 0,
    bool HasSecondPad = false, int X2 = 0, int Y2 = 0, bool Confirm2 = false, bool Cancel2 = false);

/// <summary>1 回の入力の結果。</summary>
internal readonly record struct MenuOutcome(MenuAction Action = MenuAction.None, MatchSelection? Match = null,
    int ReplayIndex = -1, MenuCue Cue = MenuCue.None, bool SettingsChanged = false);

/// <summary>
/// 画面の配置（設計の画面 640 × 464 の座標）。描画（<see cref="MenuView"/>）と指す位置の判定（<see cref="MenuState"/>）が同じ値を使う。
/// </summary>
internal static class MenuLayout
{
    public const float Width = 640f;
    public const float Height = 464f;

    public const int TopColumns = 2;
    public static readonly Vector2 TopButtonSize = new(270f, 84f);
    public static Vector2 TopButtonCenter(int index) => new(175f + (index % TopColumns * 290f), 140f + (index / TopColumns * 102f));

    public const int GridColumns = 6;
    public static readonly Vector2 TileSize = new(44f, 60f);
    public static Vector2 TileCenter(int index) => new(195f + (index % GridColumns * 50f), 125f + (index / GridColumns * 70f));

    public static readonly Vector2 SettingsRowSize = new(466f, 44f);
    public static Vector2 SettingsRowCenter(int index) => new(320f, 160f + (index * 58f));

    public const float ListFirstY = 125f;
    public const float ListStep = 49f;
    public static readonly Vector2 ListRowSize = new(466f, 38f);
    public static Vector2 ListRowCenter(int row) => new(320f, ListFirstY + (row * ListStep));

    public static int? HitCenters(Vector2 point, int count, Func<int, Vector2> center, Vector2 size)
    {
        for (var i = 0; i < count; i++)
        {
            var delta = Vector2.Abs(point - center(i));
            if (delta.X <= size.X / 2 && delta.Y <= size.Y / 2)
            {
                return i;
            }
        }

        return null;
    }
}

/// <summary>
/// メニューの状態遷移（純粋: 描画・ファイル・音を持たない）。入力は <see cref="MenuInput"/>、結果は <see cref="MenuOutcome"/>。
/// <list type="bullet">
/// <item>最初の画面（<see cref="MenuPage.Top"/>）で戻る（B・Esc）は終了しない。「終了」のボタンへカーソルを移すだけで、終了は決定したときだけ
/// （試合から B で戻った勢いの 1 押しでゲームが閉じていたため）。</item>
/// <item>キャラ選択（<see cref="MenuPage.Select"/>）は、シングル・観戦が赤 → 青の順、対戦は同時に動かせる（2 つ目のパッドが青）。
/// 決定で選びを固め、戻るで 1 段戻る（青を解く → 赤を解く → 最初の画面）。両方が決まったら試合を始める。</item>
/// </list>
/// </summary>
internal sealed class MenuState(IReadOnlyList<FighterCharacter> roster, MenuSettings settings, int defaultRed = 0, int defaultBlue = 0)
{
    public const int VisibleRecords = 5;

    public IReadOnlyList<FighterCharacter> Roster { get; } = roster;
    public MenuPage Page { get; private set; }
    public int TopIndex { get; private set; }
    public int SettingsIndex { get; private set; }
    public MenuSettings Settings { get; private set; } = settings;
    public MatchMode Mode { get; private set; }
    public int RedCursor { get; private set; } = defaultRed;
    public int BlueCursor { get; private set; } = defaultBlue;
    public bool RedLocked { get; private set; }
    public bool BlueLocked { get; private set; }
    public int ReplayCount { get; private set; }
    public int ReplayFocus { get; private set; }
    public int FirstRecord { get; private set; }
    public bool QuitRequested { get; private set; }

    /// <summary>設定の項目数（レフェリー・戻る）。</summary>
    public const int SettingsRows = 2;

    /// <summary>キャラ選択でいま動かしている側（赤が決まるまでは赤、決まったら青）。</summary>
    public bool BlueActive => RedLocked;

    /// <summary>一覧（リプレイ）を読み込んだ後に、件数を知らせる（カーソルは先頭へ）。</summary>
    public void SetReplayCount(int count)
    {
        ReplayCount = Math.Max(0, count);
        ReplayFocus = 0;
        FirstRecord = 0;
    }

    /// <summary>試合・一覧から最初の画面へ戻る（選びは解く。カーソルの位置は残す）。</summary>
    public void Return()
    {
        Page = MenuPage.Top;
        RedLocked = false;
        BlueLocked = false;
    }

    public MenuOutcome Apply(MenuInput input) => Page switch
    {
        MenuPage.Top => ApplyTop(input),
        MenuPage.Settings => ApplySettings(input),
        MenuPage.Select => ApplySelect(input),
        _ => ApplyReplays(input),
    };

    private static int Wrap(int index, int count) => ((index % count) + count) % count;

    private MenuOutcome ApplyTop(MenuInput input)
    {
        const int Count = 6;
        if (input.Cancel)
        {
            TopIndex = (int)TopItem.Quit;
            return new(Cue: MenuCue.Move);
        }

        var old = TopIndex;
        var confirm = input.Confirm;
        if (input.Pointer is { } point && MenuLayout.HitCenters(point, Count, MenuLayout.TopButtonCenter, MenuLayout.TopButtonSize) is { } hit)
        {
            TopIndex = hit;
            confirm |= input.Click;
        }

        if (input.X != 0)
        {
            var row = TopIndex / MenuLayout.TopColumns;
            TopIndex = (row * MenuLayout.TopColumns) + Wrap((TopIndex % MenuLayout.TopColumns) + input.X, MenuLayout.TopColumns);
        }

        if (input.Y != 0)
        {
            var rows = Count / MenuLayout.TopColumns;
            TopIndex = (Wrap((TopIndex / MenuLayout.TopColumns) + input.Y, rows) * MenuLayout.TopColumns) + (TopIndex % MenuLayout.TopColumns);
        }

        if (!confirm)
        {
            return new(Cue: TopIndex != old ? MenuCue.Move : MenuCue.None);
        }

        switch ((TopItem)TopIndex)
        {
            case TopItem.Quit:
                QuitRequested = true;
                return new(MenuAction.Quit, Cue: MenuCue.Confirm);
            case TopItem.Replays:
                Page = MenuPage.Replays;
                SetReplayCount(0);
                return new(MenuAction.BrowseReplays, Cue: MenuCue.Confirm);
            case TopItem.Settings:
                Page = MenuPage.Settings;
                SettingsIndex = 0;
                return new(Cue: MenuCue.Confirm);
            default:
                Mode = (MatchMode)TopIndex; // Single / Versus / Spectate は MatchMode と同じ並び
                Page = MenuPage.Select;
                RedLocked = false;
                BlueLocked = false;
                return new(Cue: MenuCue.Confirm);
        }
    }

    private MenuOutcome ApplySettings(MenuInput input)
    {
        if (input.Cancel)
        {
            Page = MenuPage.Top;
            return new(Cue: MenuCue.Confirm);
        }

        var old = SettingsIndex;
        var confirm = input.Confirm;
        if (input.Pointer is { } point && MenuLayout.HitCenters(point, SettingsRows, MenuLayout.SettingsRowCenter, MenuLayout.SettingsRowSize) is { } hit)
        {
            SettingsIndex = hit;
            confirm |= input.Click;
        }

        SettingsIndex = Wrap(SettingsIndex + input.Y, SettingsRows);
        var toggle = SettingsIndex == 0 && (input.X != 0 || confirm);
        if (toggle)
        {
            Settings = Settings with { Referee = !Settings.Referee };
            return new(Cue: MenuCue.Confirm, SettingsChanged: true);
        }

        if (SettingsIndex == 1 && confirm)
        {
            Page = MenuPage.Top;
            return new(Cue: MenuCue.Confirm);
        }

        return new(Cue: SettingsIndex != old ? MenuCue.Move : MenuCue.None);
    }

    private MenuOutcome ApplySelect(MenuInput input)
    {
        var count = Roster.Count;
        if (input.Cancel)
        {
            if (BlueLocked && !(Mode == MatchMode.Versus && input.HasSecondPad))
            {
                BlueLocked = false;
            }
            else if (RedLocked)
            {
                RedLocked = false;
            }
            else
            {
                Page = MenuPage.Top;
            }

            return new(Cue: MenuCue.Confirm);
        }

        if (input.Cancel2 && BlueLocked)
        {
            BlueLocked = false;
            return new(Cue: MenuCue.Confirm);
        }

        var (oldRed, oldBlue) = (RedCursor, BlueCursor);
        var confirm = input.Confirm;

        // 動かす側: 対戦で 2 つ目のパッドがあるときは、主の入力は赤、2 つ目は青。赤が決まったあとは主の入力も青へ（キーボードの順）。
        var secondPadBlue = Mode == MatchMode.Versus && input.HasSecondPad;
        if (secondPadBlue)
        {
            if (!BlueLocked)
            {
                BlueCursor = Move(BlueCursor, input.X2, input.Y2, count);
            }

            if (input.Confirm2 && !BlueLocked)
            {
                BlueLocked = true;
            }
        }

        if (input.Pointer is { } point && MenuLayout.HitCenters(point, count, MenuLayout.TileCenter, MenuLayout.TileSize) is { } hit)
        {
            // 2 つ目のパッドが青のときは、青の選びは 2 つ目のパッドだけ（1P が 2P のキャラを選べてしまわない）。
            var blueByPointer = BlueActive && !secondPadBlue;
            if (blueByPointer) { BlueCursor = hit; } else if (!BlueActive) { RedCursor = hit; }
            confirm |= input.Click && (blueByPointer || !BlueActive);
        }

        var driveBlue = BlueActive && !BlueLocked && !secondPadBlue;
        if (!RedLocked)
        {
            RedCursor = Move(RedCursor, input.X, input.Y, count);
        }
        else if (driveBlue)
        {
            BlueCursor = Move(BlueCursor, input.X, input.Y, count);
        }

        if (confirm)
        {
            if (!RedLocked)
            {
                RedLocked = true;
            }
            else if (!BlueLocked && !secondPadBlue)
            {
                BlueLocked = true;
            }
        }

        if (RedLocked && BlueLocked)
        {
            var match = new MatchSelection(Mode, Roster[RedCursor], Roster[BlueCursor], Settings.Referee);
            return new(MenuAction.StartMatch, match, Cue: MenuCue.Confirm);
        }

        var moved = RedCursor != oldRed || BlueCursor != oldBlue;
        return new(Cue: confirm || input.Confirm2 ? MenuCue.Confirm : moved ? MenuCue.Move : MenuCue.None);
    }

    private static int Move(int index, int x, int y, int count)
    {
        if (x != 0)
        {
            // 同じ行の中で回る（6 人ずつ）。
            var row = index / MenuLayout.GridColumns;
            var start = row * MenuLayout.GridColumns;
            var width = Math.Min(MenuLayout.GridColumns, count - start);
            index = start + Wrap(index - start + x, width);
        }

        if (y != 0)
        {
            var rows = (count + MenuLayout.GridColumns - 1) / MenuLayout.GridColumns;
            var column = index % MenuLayout.GridColumns;
            var row = Wrap((index / MenuLayout.GridColumns) + y, rows);
            index = Math.Min((row * MenuLayout.GridColumns) + column, count - 1);
        }

        return index;
    }

    private MenuOutcome ApplyReplays(MenuInput input)
    {
        if (input.Cancel)
        {
            Page = MenuPage.Top;
            return new(Cue: MenuCue.Confirm);
        }

        var y = input.Scroll != 0 ? Math.Sign(input.Scroll) : input.Y;
        var old = ReplayFocus;
        var confirm = input.Confirm;
        if (input.Pointer is { } point && MenuLayout.HitCenters(point, Math.Min(VisibleRecords, ReplayCount),
            MenuLayout.ListRowCenter, MenuLayout.ListRowSize) is { } hit)
        {
            ReplayFocus = FirstRecord + hit;
            y = 0;
            confirm |= input.Click;
        }

        if (ReplayCount == 0)
        {
            return default;
        }

        ReplayFocus = Wrap(ReplayFocus + y, ReplayCount);
        if (y != 0)
        {
            FirstRecord = Math.Clamp(ReplayFocus - (VisibleRecords / 2), 0, Math.Max(0, ReplayCount - VisibleRecords));
        }

        return confirm
            ? new(MenuAction.OpenReplay, ReplayIndex: ReplayFocus, Cue: MenuCue.Confirm)
            : new(Cue: ReplayFocus != old ? MenuCue.Move : MenuCue.None);
    }
}
