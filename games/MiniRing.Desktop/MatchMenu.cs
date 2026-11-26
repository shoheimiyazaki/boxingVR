using System.Numerics;
using Aigis.Platform;
using Aigis.Render;
using MiniRing.Arena.Campaign;
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
    private readonly MenuScript? _script;
    private string[] _files = [];
    private Vector2 _pad;
    private Vector2 _pad2;
    private bool _confirm, _back, _confirm2, _back2;
    private readonly CampaignStore _campaign;
    private CampaignTicket? _pendingBout;
    private MatchSelection? _lastMatch;

    /// <param name="replayDirectory">記録（.mrrep）の置き場。</param>
    /// <param name="settingsPath">設定（JSON）のファイル。壊れていれば既定へ戻して標準エラーへ出す。</param>
    /// <param name="playCue">合図の音（true = 決定、false = カーソルの移動）。</param>
    /// <param name="roster">名簿（既定は組み込みの名簿の全員）。</param>
    /// <param name="loadPortraits">肖像（顔・全身の絵）を背景で作るか。テストで重い構築を避けるときだけ false。</param>
    /// <param name="script">決めた順の操作（<see cref="MenuScript"/>。null なら手の入力だけ）。</param>
    /// <param name="campaignDirectory">キャンペーンのボクサーの置き場（1 人 1 ファイル）。null なら設定のファイルと同じ場所の <c>campaign</c>（テスト用。ゲームは <see cref="CampaignStore.DefaultDirectory"/> の %LOCALAPPDATA% を渡す）。</param>
    public MatchMenu(string replayDirectory, string settingsPath, Action<bool>? playCue = null,
        IReadOnlyList<FighterCharacter>? roster = null, bool loadPortraits = true, MenuScript? script = null, string? campaignDirectory = null)
    {
        _script = script;
        _replayDirectory = replayDirectory;
        _settingsPath = settingsPath;
        _playCue = playCue;
        roster ??= CharacterRoster.All;
        var settings = MenuSettingsStore.Load(settingsPath, message => Console.Error.WriteLine(message));
        // 既定のカーソル: 赤 = 先頭の人、青 = ゲームの CPU のスタイル（champion）の人。
        var defaultBlue = Math.Max(0, ToList(roster).FindIndex(c => c.StyleName == Bots.BotCatalog.DefaultCpu && c.Id != roster[0].Id));
        State = new MenuState(roster, settings, 0, defaultBlue);
        _campaign = new CampaignStore(campaignDirectory ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ?? ".", "campaign"),
            [.. roster.Select(c => c.Id)]);
        _portraits = new PortraitSet(roster);
        if (loadPortraits) { _portraits.Start(); }
        _view = new MenuView(State, _portraits, () => _files.Select(Path.GetFileNameWithoutExtension).OfType<string>().ToArray());
        ReloadBoxers(); // 壊れたファイルは知らせの行へ（_view の後）
    }

    private static List<FighterCharacter> ToList(IReadOnlyList<FighterCharacter> roster) => [.. roster];

    public MenuState State { get; }

    public PortraitSet Portraits => _portraits;

    /// <summary>描画の時計 [s]（札の動き。試合の計算には入らない）。テストは決めた時刻を入れて、動き終わった絵を撮る。</summary>
    internal Func<double> Clock { set => _view.Clock = value; }

    public bool QuitRequested => State.QuitRequested;

    public (MatchSelection? Match, ReplayData? Replay) Read(InputSnapshot input, Vector2 size)
    {
        var real = Translate(input, size); // 押し下げの記憶（前のフレームの状態）は台本の間も進める
        return Handle(_script?.Next() ?? real);
    }

    /// <summary>入力 1 回分を状態へ渡し、結果（設定の保存・記録の一覧と再生・試合の開始・合図の音）を実行する。</summary>
    internal (MatchSelection? Match, ReplayData? Replay) Handle(MenuInput menuInput)
    {
        var outcome = State.Apply(menuInput);
        if (outcome.Cue != MenuCue.None)
        {
            _playCue?.Invoke(outcome.Cue == MenuCue.Confirm);
            _view.Notice = outcome.Notice;
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
                _pendingBout = outcome.Match?.Campaign;
                _lastMatch = outcome.Match;
                return (outcome.Match, null);
            case MenuAction.Rematch when _lastMatch is { Campaign: null } again:
                // フリーバトルの「もう一度」: 同じ組み合わせ・同じ決まりで始め直す（キャンペーンは本拠地から次の試合を選ぶ）。
                return (again, null);
            case MenuAction.Campaign when outcome.Request is { } request:
                RunCampaign(request);
                break;
            case MenuAction.OpenReplay:
                return OpenReplay(outcome.ReplayIndex);
        }

        return default;
    }

    /// <summary>試合・記録の再生から戻る（B を押したままの勢いは、次の 1 押しとして数えない）。</summary>
    /// <param name="backHeld">B を押したまま戻ったか。</param>
    /// <param name="outcome">終わった試合の結果（途中で抜けた・リプレイなら null）。キャンペーンの試合なら、終わっていれば戦績へ書く（試合と試合の間だけ変わる: §1.6）。</param>
    public void Return(bool backHeld = false, BoutOutcome? outcome = null)
    {
        _back = backHeld;
        State.Return();
        _view.Notice = null;
        if (_pendingBout is { } ticket)
        {
            _pendingBout = null;
            _view.Notice = outcome is { } result ? RecordBout(ticket, result) : "試合が終わる前に抜けたので、戦績には書きません。";
            if (State.Cards.Count > 0) { _view.Notice = null; } // 札が同じことを言う（閉じた後に古い 1 行を残さない）
        }
    }

    /// <summary>今の試合がキャンペーンの試合か（結果の画面のボタンが変わる）。</summary>
    public bool InCampaignBout => _pendingBout is not null;

    /// <summary>
    /// 決着した試合から戻る: キャンペーンなら先に戦績を書いて（札は本拠地に積む）、結果の画面（<see cref="MenuPage.Result"/>）を開く。
    /// 結果の画面のボタンで本拠地（札が出る）・もう一度・キャラ選択・最初の画面へ進む。
    /// </summary>
    /// <param name="summary">結果の画面の中身。</param>
    /// <param name="outcome">戦績に書く結果（キャンペーンの試合なら決着していれば null でない）。</param>
    public void Finish(BoutSummary summary, BoutOutcome? outcome)
    {
        ArgumentNullException.ThrowIfNull(summary);
        // A を押したまま結果へ来たかもしれない: 放すまでは次の 1 押しに数えない（結果の画面の先頭のボタンを勝手に押さない）。
        (_confirm, _back) = (true, false);
        _view.Notice = null;
        if (_pendingBout is { } ticket)
        {
            _pendingBout = null;
            // 戦績は結果の画面を見せる前に書く（結果の画面のままゲームを閉じても、決着した試合は残る）。
            var notice = outcome is { } result ? RecordBout(ticket, result) : "決着の結果を読めなかったので、戦績には書きません。";
            _view.Notice = State.Cards.Count > 0 ? null : notice;
        }

        State.ShowResult(summary);
    }

    /// <summary>キャンペーンの試合を戦績へ書いて保存し、知らせの 1 行を返す。</summary>
    internal string RecordBout(CampaignTicket ticket, BoutOutcome outcome)
    {
        try
        {
            var boxer = State.Boxers.FirstOrDefault(b => b.Id == ticket.BoxerId)
                ?? throw new InvalidOperationException($"ボクサー {ticket.BoxerId} が置き場にありません。");
            var org = ticket.Entry.Event.Organization;
            var ranked = MiniRing.Arena.OrgStandings.Of(org).For(ticket.Entry.Division).Ranking.Count;
            var after = boxer.AfterBout(ticket.Entry, outcome.Result, outcome.Finish, ticket.Weapon, ranked);
            _campaign.Save(after);
            ReloadBoxers();
            // 前後の差から喜びの札（新しい大会・席・王座・戦績）を作って本拠地の上に出す。札は Enter で送る。
            State.Celebrate(CampaignCard.Between(boxer, after, ticket.Entry, outcome.Result, outcome.Finish, State.Roster));
            _playCue?.Invoke(true);
            var word = outcome.Result switch { BoutResult.Win => outcome.Finish ? "勝ち（倒して）" : "勝ち（判定）", BoutResult.Loss => outcome.Finish ? "負け（倒されて）" : "負け（判定）", _ => "引き分け" };
            return $"{ticket.Entry.Event.Name}: {word}。{after.Career.RecordText}・{ticket.Entry.Division.Name}の席 {CampaignLadder.SeatName(after.Career.SeatIn(org.Id, ticket.Entry.Division.Id))}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex);
            return "戦績を保存できませんでした。";
        }
    }

    private void RunCampaign(CampaignRequest request)
    {
        try
        {
            switch (request)
            {
                case CampaignRequest.Create create:
                    var made = _campaign.Create(create.Draft);
                    ReloadBoxers();
                    State.OpenBoxer(made.Id);
                    _view.Notice = $"{made.Character.HudName} を作りました（{made.WeightKg} kg）。";
                    break;
                case CampaignRequest.Delete delete:
                    _campaign.Delete(delete.BoxerId);
                    ReloadBoxers();
                    _view.Notice = "消しました。";
                    break;
                case CampaignRequest.ChangeWeight change:
                    var boxer = State.Boxers.First(b => b.Id == change.BoxerId);
                    var changed = boxer.ChangeWeight(change.TargetKg);
                    _campaign.Save(changed);
                    ReloadBoxers();
                    _view.Notice = $"体重を {boxer.WeightKg} kg から {changed.WeightKg} kg にしました（筋肉の太さが変わり、力も同じ式で変わります）。";
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            Report("キャンペーンのボクサーを保存できません: " + ex.Message.Split(" (Parameter")[0], ex);
        }
    }

    private void ReloadBoxers()
    {
        try
        {
            State.SetBoxers(_campaign.LoadAll());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Report("キャンペーンのボクサーを読み込めません。", ex);
        }
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
