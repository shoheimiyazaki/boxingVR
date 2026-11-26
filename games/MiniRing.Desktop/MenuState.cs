using System.Numerics;
using MiniRing.Arena;
using MiniRing.Characters;

namespace MiniRing.Desktop;

/// <summary>メニューの画面（ページ）。</summary>
internal enum MenuPage { Top, Settings, Mode, Select, Replays, Free, Campaign, Create, Boxer, Orgs, Path, Weight, Result }

/// <summary>
/// 最初の画面のボタン（表示の並び: 2 列 × 3 行、最後の行は「終了」だけ。2026-11-26）: ゲームモード（フリーバトル・キャンペーン）と、リプレイ・設定・終了。
/// 今までの「シングル・対戦・観戦」はフリーバトルの中（<see cref="MenuPage.Free"/>）へ移した（挙動は同じ）。
/// </summary>
internal enum TopItem { FreeBattle, Campaign, Replays, Settings, Quit }

/// <summary>メニューが外へ頼むこと。</summary>
internal enum MenuAction { None, BrowseReplays, OpenReplay, StartMatch, Quit, Campaign, Rematch }

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
/// <paramref name="Notice"/> は画面の下に出す知らせ（大会に出られない人を選んだ・階級が違う など）。
/// <paramref name="Request"/> はキャンペーンのボクサーの置き場への頼み（作る・消す・体重を変える。<see cref="MenuAction.Campaign"/> のとき）。
internal readonly record struct MenuOutcome(MenuAction Action = MenuAction.None, MatchSelection? Match = null,
    int ReplayIndex = -1, MenuCue Cue = MenuCue.None, bool SettingsChanged = false, string? Notice = null, CampaignRequest? Request = null);

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

    /// <summary>ゲームモードの行（左の列）。行の数は練習試合 + 団体の数 + 戻る。</summary>
    public static readonly Vector2 ModeRowSize = new(250f, 46f);
    public static Vector2 ModeRowCenter(int index) => new(160f, 118f + (index * 54f));

    /// <summary>キャンペーンの行（左の列。一覧・作る・ボクサーのページ）。</summary>
    public static readonly Vector2 CampaignRowSize = new(280f, 22f);
    public static Vector2 CampaignRowCenter(int index) => new(162f, 104f + (index * 25f));

    public static readonly Vector2 SettingsRowSize = new(466f, 44f);
    public static Vector2 SettingsRowCenter(int index) => new(320f, 160f + (index * 58f));

    public const float ListFirstY = 125f;
    public const float ListStep = 49f;
    public static readonly Vector2 ListRowSize = new(466f, 38f);
    public static Vector2 ListRowCenter(int row) => new(320f, ListFirstY + (row * ListStep));

    /// <summary>結果の画面のボタン（横に並べる。マウスで押しやすい大きさ）。</summary>
    public static readonly Vector2 ResultButtonSize = new(170f, 48f);
    public static Vector2 ResultButtonCenter(int index, int count) => new(320f + ((index - ((count - 1) / 2f)) * 186f), 340f);

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
/// 決定で選びを固め、戻るで 1 段戻る（青を解く → 赤を解く → ゲームモード）。両方が決まったら試合を始める。</item>
/// <item>最初の画面で誰が操るか（シングル・対戦・観戦）を選んだあと、ゲームモード（<see cref="MenuPage.Mode"/>: 練習試合か団体の大会、<see cref="Competition"/>）を選ぶ。
/// 大会では出場の条件に合わない人・赤と階級の違う人は選べない（カーソルが飛ばし、決定すると理由を知らせる）。</item>
/// </list>
/// </summary>
internal sealed partial class MenuState
{
    public const int VisibleRecords = 5;

    /// <param name="roster">名簿。</param>
    /// <param name="settings">設定。</param>
    /// <param name="defaultRed">赤の最初のカーソル（名簿の添字）。</param>
    /// <param name="defaultBlue">青の最初のカーソル。</param>
    /// <param name="organizations">選べる団体（null なら組み込みの全団体: <see cref="Competition.Organizations"/>）。</param>
    public MenuState(IReadOnlyList<FighterCharacter> roster, MenuSettings settings, int defaultRed = 0, int defaultBlue = 0,
        IReadOnlyList<Organization>? organizations = null)
    {
        Roster = roster ?? throw new ArgumentNullException(nameof(roster));
        Settings = settings;
        RedCursor = defaultRed;
        BlueCursor = defaultBlue;
        Organizations = organizations ?? Competition.Organizations();
        Grid = new SelectGrid(roster);
        Competition = Competition.Practice(null);
    }

    public IReadOnlyList<FighterCharacter> Roster { get; }
    public IReadOnlyList<Organization> Organizations { get; }

    /// <summary>キャラ選択の格子（分け方ごとの節・送り）。</summary>
    public SelectGrid Grid { get; }
    public MenuPage Page { get; private set; }
    public int TopIndex { get; private set; }
    public int SettingsIndex { get; private set; }
    public MenuSettings Settings { get; private set; }
    public MatchMode Mode { get; private set; }

    /// <summary>ゲームモードの行の位置（0 = 練習試合、1.. = 団体の大会、最後 = 戻る）。</summary>
    public int ModeIndex { get; private set; }

    /// <summary>練習試合の決まり（0 = 標準、1.. = <see cref="Organizations"/> の団体の決まり）。</summary>
    public int PracticeRules { get; private set; }

    /// <summary>選んだゲームモード（キャラ選択と試合に渡る）。</summary>
    public Competition Competition { get; private set; }
    public int RedCursor { get; private set; }
    public int BlueCursor { get; private set; }
    public bool RedLocked { get; private set; }
    public bool BlueLocked { get; private set; }
    public int ReplayCount { get; private set; }
    public int ReplayFocus { get; private set; }
    public int FirstRecord { get; private set; }
    public bool QuitRequested { get; private set; }

    /// <summary>設定の項目数（レフェリー・戻る）。</summary>
    public const int SettingsRows = 2;

    /// <summary>ゲームモードの行の数（練習試合・団体の大会・戻る）。</summary>
    public int ModeRows => Organizations.Count + 2;

    /// <summary>ゲームモードの行 <paramref name="row"/> の中身（戻るの行なら null）。</summary>
    public Competition? ModeAt(int row) => row == 0
        ? Competition.Practice(PracticeRules == 0 ? null : Organizations[PracticeRules - 1])
        : row <= Organizations.Count ? Competition.Event(Organizations[row - 1]) : null;

    /// <summary>キャラ選択でいま動かしている側（赤が決まるまでは赤、決まったら青）。</summary>
    public bool BlueActive => RedLocked;

    /// <summary>この人を赤に選べるか（大会の出場の条件）。</summary>
    public bool RedAllowed(int index) => Competition.Ineligible(Roster[index]) is null;

    /// <summary>この人を青に選べるか（出場の条件。赤が決まっていれば、赤と同じ階級か）。</summary>
    public bool BlueAllowed(int index) => RedLocked
        ? Competition.CannotFace(Roster[RedCursor], Roster[index]) is null
        : Competition.Ineligible(Roster[index]) is null;

    /// <summary>一覧（リプレイ）を読み込んだ後に、件数を知らせる（カーソルは先頭へ）。</summary>
    public void SetReplayCount(int count)
    {
        ReplayCount = Math.Max(0, count);
        ReplayFocus = 0;
        FirstRecord = 0;
    }

    /// <summary>試合・一覧から戻る（選びは解く。カーソルの位置は残す）。キャンペーンの試合からはそのボクサーのページへ、ほかは最初の画面へ。</summary>
    public void Return()
    {
        Page = Page == MenuPage.Boxer && Boxer is not null ? MenuPage.Boxer : MenuPage.Top;
        RedLocked = false;
        BlueLocked = false;
    }

    public MenuOutcome Apply(MenuInput input) => Page switch
    {
        MenuPage.Top => ApplyTop(input),
        MenuPage.Settings => ApplySettings(input),
        MenuPage.Mode => ApplyMode(input),
        MenuPage.Select => ApplySelect(input),
        MenuPage.Free => ApplyFree(input),
        MenuPage.Campaign => ApplyCampaign(input),
        MenuPage.Create => ApplyCreate(input),
        MenuPage.Boxer => Cards.Count > 0 ? ApplyCard(input) : ApplyBoxer(input),
        MenuPage.Orgs => ApplyOrgs(input),
        MenuPage.Path => ApplyPath(input),
        MenuPage.Weight => ApplyWeight(input),
        MenuPage.Result => ApplyResult(input),
        _ => ApplyReplays(input),
    };

    private static int Wrap(int index, int count) => ((index % count) + count) % count;

    private MenuOutcome ApplyTop(MenuInput input)
    {
        const int Count = TopCount;
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
            var rows = (Count + MenuLayout.TopColumns - 1) / MenuLayout.TopColumns;
            TopIndex = (Wrap((TopIndex / MenuLayout.TopColumns) + input.Y, rows) * MenuLayout.TopColumns) + (TopIndex % MenuLayout.TopColumns);
        }

        TopIndex = Math.Min(TopIndex, Count - 1); // 最後の行は「終了」だけ（空いた所へは行かない）

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
            case TopItem.Campaign:
                EnterCampaign();
                return new(Cue: MenuCue.Confirm);
            default:
                Page = MenuPage.Free;
                return new(Cue: MenuCue.Confirm);
        }
    }

    /// <summary>最初の画面のボタンの数。</summary>
    public const int TopCount = 5;

    /// <summary>フリーバトルの行の数（シングル・対戦・観戦・戻る）。</summary>
    public const int FreeRows = 4;

    /// <summary>フリーバトルの行の位置（0〜2 = <see cref="MatchMode"/>、3 = 戻る）。</summary>
    public int FreeIndex { get; private set; }

    /// <summary>
    /// フリーバトル（今までのモード選択そのまま）: 誰が操るか（シングル・対戦・観戦）を選んで、ゲームモード（練習試合・団体の大会）→ キャラ選択へ。
    /// </summary>
    private MenuOutcome ApplyFree(MenuInput input)
    {
        if (input.Cancel)
        {
            Page = MenuPage.Top;
            return new(Cue: MenuCue.Confirm);
        }

        var old = FreeIndex;
        var confirm = input.Confirm;
        if (input.Pointer is { } point && MenuLayout.HitCenters(point, FreeRows, MenuLayout.ModeRowCenter, MenuLayout.ModeRowSize) is { } hit)
        {
            FreeIndex = hit;
            confirm |= input.Click;
        }

        FreeIndex = Wrap(FreeIndex + input.Y, FreeRows);
        if (!confirm)
        {
            return new(Cue: FreeIndex != old ? MenuCue.Move : MenuCue.None);
        }

        if (FreeIndex == FreeRows - 1)
        {
            Page = MenuPage.Top;
            return new(Cue: MenuCue.Confirm);
        }

        Mode = (MatchMode)FreeIndex; // Single / Versus / Spectate は MatchMode と同じ並び
        Page = MenuPage.Mode;
        return new(Cue: MenuCue.Confirm);
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

    /// <summary>
    /// ゲームモード: 上下で行を選び、練習試合の行では左右で決まり（標準・団体）を切り替える。決定でキャラ選択へ（戻るの行・戻るで最初の画面）。
    /// </summary>
    private MenuOutcome ApplyMode(MenuInput input)
    {
        if (input.Cancel)
        {
            Page = MenuPage.Free;
            return new(Cue: MenuCue.Confirm);
        }

        var (oldRow, oldRules) = (ModeIndex, PracticeRules);
        var confirm = input.Confirm;
        if (input.Pointer is { } point && MenuLayout.HitCenters(point, ModeRows, MenuLayout.ModeRowCenter, MenuLayout.ModeRowSize) is { } hit)
        {
            ModeIndex = hit;
            confirm |= input.Click;
        }

        ModeIndex = Wrap(ModeIndex + input.Y, ModeRows);
        if (ModeIndex == 0 && input.X != 0)
        {
            PracticeRules = Wrap(PracticeRules + input.X, Organizations.Count + 1);
        }

        if (!confirm)
        {
            return new(Cue: ModeIndex != oldRow || PracticeRules != oldRules ? MenuCue.Move : MenuCue.None);
        }

        if (ModeAt(ModeIndex) is not { } competition)
        {
            Page = MenuPage.Free;
            return new(Cue: MenuCue.Confirm);
        }

        EnterSelect(competition);
        return new(Cue: MenuCue.Confirm);
    }

    /// <summary>キャラ選択へ入る: 選びを解き、出られない人を指していれば出られる最初の人へ移す。</summary>
    private void EnterSelect(Competition competition)
    {
        Competition = competition;
        Page = MenuPage.Select;
        RedLocked = false;
        BlueLocked = false;
        RedCursor = RedAllowed(RedCursor) ? RedCursor : Grid.First(RedAllowed) ?? RedCursor;
        BlueCursor = BlueAllowed(BlueCursor) ? BlueCursor : Grid.First(BlueAllowed) ?? BlueCursor;
        Grid.Reveal(RedCursor);
    }

    private MenuOutcome ApplySelect(MenuInput input)
    {
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
                Page = MenuPage.Mode;
            }

            return new(Cue: MenuCue.Confirm);
        }

        if (input.Cancel2 && BlueLocked)
        {
            BlueLocked = false;
            return new(Cue: MenuCue.Confirm);
        }

        var (oldRed, oldBlue, oldLocked) = (RedCursor, BlueCursor, RedLocked);
        var confirm = input.Confirm;
        string? notice = null;
        var pointed = false;
        if (input.Scroll != 0)
        {
            Grid.Scroll(Math.Sign(input.Scroll));
        }

        // 動かす側: 対戦で 2 つ目のパッドがあるときは、主の入力は赤、2 つ目は青。赤が決まったあとは主の入力も青へ（キーボードの順）。
        var secondPadBlue = Mode == MatchMode.Versus && input.HasSecondPad;
        if (secondPadBlue)
        {
            if (!BlueLocked)
            {
                BlueCursor = Grid.Move(BlueCursor, input.X2, input.Y2, BlueAllowed);
            }

            if (input.Confirm2 && !BlueLocked)
            {
                notice = TryLockBlue();
            }
        }

        if (input.Pointer is { } point && input.Scroll == 0 && Grid.Hit(point) is { } hit)
        {
            // 2 つ目のパッドが青のときは、青の選びは 2 つ目のパッドだけ（1P が 2P のキャラを選べてしまわない）。
            pointed = true;
            var blueByPointer = BlueActive && !secondPadBlue;
            if (blueByPointer) { BlueCursor = hit; } else if (!BlueActive) { RedCursor = hit; }
            confirm |= input.Click && (blueByPointer || !BlueActive);
        }

        var driveBlue = BlueActive && !BlueLocked && !secondPadBlue;
        if (!RedLocked)
        {
            RedCursor = Grid.Move(RedCursor, input.X, input.Y, RedAllowed);
        }
        else if (driveBlue)
        {
            BlueCursor = Grid.Move(BlueCursor, input.X, input.Y, BlueAllowed);
        }

        if (confirm)
        {
            if (!RedLocked)
            {
                notice = TryLockRed() ?? notice;
            }
            else if (!BlueLocked && !secondPadBlue)
            {
                notice = TryLockBlue() ?? notice;
            }
        }

        if (RedLocked && BlueLocked)
        {
            // 2 つ目のパッドが先に青を決めていたとき、赤が決まった後で組めるかを確かめ直す（階級が違えば青を解く）。
            if (Competition.CannotFace(Roster[RedCursor], Roster[BlueCursor]) is { } why)
            {
                BlueLocked = false;
                return new(Cue: MenuCue.Move, Notice: why);
            }

            var match = new MatchSelection(Mode, Roster[RedCursor], Roster[BlueCursor], Settings.Referee, Competition);
            return new(MenuAction.StartMatch, match, Cue: MenuCue.Confirm);
        }

        var moved = RedCursor != oldRed || BlueCursor != oldBlue;
        // 動かした（か赤が決まって青へ移った）カーソルが見えるように送る。札を指して動かしたときは送らない（指した札は見えている）。
        if ((moved || RedLocked != oldLocked) && !pointed)
        {
            Grid.Reveal(RedLocked ? BlueCursor : RedCursor);
            if (secondPadBlue && BlueCursor != oldBlue) { Grid.Reveal(BlueCursor); }
        }

        var cue = notice is not null ? MenuCue.Move : confirm || input.Confirm2 ? MenuCue.Confirm : moved ? MenuCue.Move : MenuCue.None;
        return new(Cue: cue, Notice: notice);
    }

    /// <summary>赤を決める（出られない人なら決めずに理由を返す）。決まれば、青のカーソルを赤と組める人へ移す。</summary>
    private string? TryLockRed()
    {
        if (Competition.Ineligible(Roster[RedCursor]) is { } why) { return why; }
        RedLocked = true;
        if (!BlueLocked && !BlueAllowed(BlueCursor))
        {
            BlueCursor = Grid.First(BlueAllowed) ?? BlueCursor;
        }

        return null;
    }

    private string? TryLockBlue()
    {
        var why = RedLocked ? Competition.CannotFace(Roster[RedCursor], Roster[BlueCursor]) : Competition.Ineligible(Roster[BlueCursor]);
        if (why is null) { BlueLocked = true; }
        return why;
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
