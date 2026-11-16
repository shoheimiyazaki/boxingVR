using System.Numerics;
using Aigis.Platform;
using Aigis.Render;
using MiniRing.Characters;
using MiniRing.Replay;
using Silk.NET.Input;

namespace MiniRing.Desktop;

/// <summary>
/// ゲームセレクト（メニュー）のホスト側の殻: <see cref="InputSnapshot"/>（キー・マウス・パッド）を <see cref="MenuInput"/> へ直し、
/// 状態の遷移（<see cref="MenuState"/>）の結果（設定の保存・記録の一覧と再生・試合の開始・合図の音）を実行し、描画（<see cref="MenuView"/>）へ渡す。
/// 画面の中身（どのページに何があるか）は <see cref="MenuState"/> と <see cref="MenuView"/> にあり、ここには無い。
/// </summary>
internal sealed class MatchMenu : IDisposable
{
    private readonly string _replayDirectory;
    private readonly string _settingsPath;
    private readonly Action<bool>? _playCue;
    private readonly PortraitSet _portraits;
    private readonly MenuView _view;
    private string[] _files = [];
    private Vector2 _pad;
    private Vector2 _pad2;
    private bool _confirm, _back, _confirm2, _back2;

    /// <param name="replayDirectory">記録（.mrrep）の置き場。</param>
    /// <param name="settingsPath">設定（JSON）のファイル。壊れていれば既定へ戻して標準エラーへ出す。</param>
    /// <param name="playCue">合図の音（true = 決定、false = カーソルの移動）。</param>
    /// <param name="roster">名簿（既定は組み込みの 12 人）。</param>
    /// <param name="loadPortraits">肖像（顔・全身の絵）を背景で作るか。テストで重い構築を避けるときだけ false。</param>
    public MatchMenu(string replayDirectory, string settingsPath, Action<bool>? playCue = null,
        IReadOnlyList<FighterCharacter>? roster = null, bool loadPortraits = true)
    {
        _replayDirectory = replayDirectory;
        _settingsPath = settingsPath;
        _playCue = playCue;
        roster ??= CharacterRoster.All;
        var settings = MenuSettingsStore.Load(settingsPath, message => Console.Error.WriteLine(message));
        // 既定のカーソル: 赤 = 先頭の人、青 = ゲームの CPU のスタイル（champion）の人。
        var defaultBlue = Math.Max(0, ToList(roster).FindIndex(c => c.StyleName == Bots.BotCatalog.DefaultCpu && c.Id != roster[0].Id));
        State = new MenuState(roster, settings, 0, defaultBlue);
        _portraits = new PortraitSet(roster);
        if (loadPortraits) { _portraits.Start(); }
        _view = new MenuView(State, _portraits, () => _files.Select(Path.GetFileNameWithoutExtension).OfType<string>().ToArray());
    }

    private static List<FighterCharacter> ToList(IReadOnlyList<FighterCharacter> roster) => [.. roster];

    public MenuState State { get; }

    public PortraitSet Portraits => _portraits;

    public bool QuitRequested => State.QuitRequested;

    public (MatchSelection? Match, ReplayData? Replay) Read(InputSnapshot input, Vector2 size) => Handle(Translate(input, size));

    /// <summary>入力 1 回分を状態へ渡し、結果（設定の保存・記録の一覧と再生・試合の開始・合図の音）を実行する。</summary>
    internal (MatchSelection? Match, ReplayData? Replay) Handle(MenuInput menuInput)
    {
        var outcome = State.Apply(menuInput);
        if (outcome.Cue != MenuCue.None)
        {
            _playCue?.Invoke(outcome.Cue == MenuCue.Confirm);
            _view.Notice = null;
        }

        if (outcome.SettingsChanged)
        {
            MenuSettingsStore.Save(_settingsPath, State.Settings, message => Console.Error.WriteLine(message));
        }

        switch (outcome.Action)
        {
            case MenuAction.BrowseReplays:
                Browse();
                break;
            case MenuAction.StartMatch:
                return (outcome.Match, null);
            case MenuAction.OpenReplay:
                return OpenReplay(outcome.ReplayIndex);
        }

        return default;
    }

    /// <summary>試合・記録の再生から戻る（B を押したままの勢いは、次の 1 押しとして数えない）。</summary>
    public void Return(bool backHeld = false)
    {
        _back = backHeld;
        State.Return();
        _view.Notice = null;
    }

    public void PlaybackFailed(Exception ex) => Report("この記録を再生できません。", ex);

    public IReadOnlyList<UiShapeDraw> Shapes(Vector2 size) => _view.Shapes(size);

    public IReadOnlyList<ScreenTextDraw> Text(Vector2 size) => _view.Text(size);

    public void Dispose() => _portraits.Dispose();

    private MenuInput Translate(InputSnapshot input, Vector2 size)
    {
        var pad = DirectionOf(input, 0);
        var confirm = input.IsGamepadButtonDown(GamepadButton.South);
        var back = input.IsGamepadButtonDown(GamepadButton.East);
        var x = (input.WasKeyPressed(Key.Right) || input.WasKeyPressed(Key.D) || (pad.X > 0 && _pad.X <= 0) ? 1 : 0)
            - (input.WasKeyPressed(Key.Left) || input.WasKeyPressed(Key.A) || (pad.X < 0 && _pad.X >= 0) ? 1 : 0);
        var y = (input.WasKeyPressed(Key.Down) || input.WasKeyPressed(Key.S) || (pad.Y < 0 && _pad.Y >= 0) ? 1 : 0)
            - (input.WasKeyPressed(Key.Up) || input.WasKeyPressed(Key.W) || (pad.Y > 0 && _pad.Y <= 0) ? 1 : 0);
        var select = input.WasKeyPressed(Key.Enter) || (confirm && !_confirm);
        var cancel = input.WasKeyPressed(Key.Escape) || (back && !_back);

        // 2 つ目のパッド（対戦のキャラ選択で青を同時に動かす）。
        var second = input.GamepadCount > 1;
        var (x2, y2, select2, cancel2) = (0, 0, false, false);
        if (second)
        {
            var pad2 = DirectionOf(input, 1);
            var confirm2 = input.IsGamepadButtonDown(1, GamepadButton.South);
            var back2 = input.IsGamepadButtonDown(1, GamepadButton.East);
            x2 = (pad2.X > 0 && _pad2.X <= 0 ? 1 : 0) - (pad2.X < 0 && _pad2.X >= 0 ? 1 : 0);
            y2 = (pad2.Y < 0 && _pad2.Y >= 0 ? 1 : 0) - (pad2.Y > 0 && _pad2.Y <= 0 ? 1 : 0);
            (select2, cancel2) = (confirm2 && !_confirm2, back2 && !_back2);
            (_pad2, _confirm2, _back2) = (pad2, confirm2, back2);
        }

        (_pad, _confirm, _back) = (pad, confirm, back);

        var click = input.WasMouseButtonPressed(MouseButton.Left);
        Vector2? pointer = click || input.MouseDelta != Vector2.Zero ? new MenuCanvas(size).ToDesign(input.MousePosition) : null;
        var scroll = input.ScrollDelta == 0f ? 0 : -Math.Sign(input.ScrollDelta);
        return new MenuInput(x, y, select, cancel, pointer, click, scroll, second, x2, y2, select2, cancel2);
    }

    /// <summary>十字キー、無ければスティックを、上が + の方向（上下左右の 4 方向）へ。</summary>
    private static Vector2 DirectionOf(InputSnapshot input, int pad)
    {
        var dpad = input.GetGamepadDPad(pad);
        if (dpad != Vector2.Zero) { return dpad; }
        // スティックの生の Y は下が正（GLFW）。十字キー（GetGamepadDPad）と同じ上が正へそろえる。
        var stick = input.GetGamepadLeftStick(pad);
        return new Vector2(StepAxis(stick.X), StepAxis(-stick.Y));
    }

    private static int StepAxis(float value) => value > 0.55f ? 1 : value < -0.55f ? -1 : 0;

    private void Browse()
    {
        _files = [];
        try
        {
            if (Directory.Exists(_replayDirectory))
            {
                _files = Directory.EnumerateFiles(_replayDirectory, "*" + ReplayFile.Extension)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ThenBy(path => path, StringComparer.Ordinal)
                    .ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("記録フォルダーを読み込めません。", ex);
        }

        State.SetReplayCount(_files.Length);
    }

    private (MatchSelection? Match, ReplayData? Replay) OpenReplay(int index)
    {
        try
        {
            var replay = ReplayFile.Read(_files[index]);
            if (replay.Steps.Count == 0)
            {
                _view.Notice = "この記録には再生できる試合がありません。";
                return default;
            }

            _ = replay.Header.Tuning;
            _ = replay.Header.Corners; // 見出しの条件（顔・名前・体格など）をここで検証する（壊れていれば再生の前に知らせる）
            return (null, replay);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException
            or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
        {
            Report("記録を開けません。形式・物理の版・アクセス権を確認してください。", ex);
            return default;
        }
    }

    private void Report(string message, Exception ex)
    {
        _view.Notice = message;
        Console.Error.WriteLine(ex);
    }
}
