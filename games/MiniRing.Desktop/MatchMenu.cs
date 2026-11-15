using System.Numerics;
using Aigis.Platform;
using Aigis.Render;
using MiniRing.Bots;
using MiniRing.Replay;
using MiniRing.Weapons;
using Silk.NET.Input;

namespace MiniRing.Desktop;

internal sealed class MatchMenu(HandWear defaultWear, string replayDirectory, Action<bool>? playCue = null)
{
    private static readonly string[] Modes = ["シングルプレイ", "対戦", "観戦モード", "過去の試合のリプレイ", "終了"];
    private static readonly string[] ModeDetails = ["人 VS CPU", "人 VS 人", "CPU VS CPU", "試合の記録を再生", ""];
    private static readonly (HandWear Value, string Label, WeaponKind? Weapon)[] Equipment =
    [
        (HandWear.Glove, "グローブ", null), (HandWear.None, "素手", null),
        (HandWear.KnuckleDuster, "メリケンサック", null), (HandWear.Katar, "カタール", null),
        (HandWear.Gauntlet, "籠手", null), (HandWear.Glove, "日本刀", WeaponKind.Katana),
        (HandWear.Glove, "ロングソード", WeaponKind.Longsword), (HandWear.Glove, "こんぼう", WeaponKind.WoodenClub),
    ];

    private enum Page { Modes, Setup, Replays }
    private Page _page;
    private int _mode;
    private int _focus;
    private int _red;
    private int _blue = BotCatalog.Styles.ToList().FindIndex(s => s.Name == BotCatalog.DefaultCpu);
    private int _redWear = Array.FindIndex(Equipment, e => e.Value == defaultWear);
    private int _blueWear = Array.FindIndex(Equipment, e => e.Value == defaultWear);
    private string[] _files = [];
    private int _firstRecord;
    private string? _error;
    private Vector2 _pad;
    private bool _confirm, _back;

    public bool QuitRequested { get; private set; }

    public (MatchSelection? Match, ReplayData? Replay) Read(InputSnapshot input, Vector2 size)
    {
        var pad = input.GetGamepadDPad(0);
        if (pad == Vector2.Zero)
        {
            var stick = input.GamepadLeftStick;
            pad = new Vector2(StepAxis(stick.X), StepAxis(stick.Y));
        }
        var confirm = input.IsGamepadButtonDown(GamepadButton.South);
        var back = input.IsGamepadButtonDown(GamepadButton.East);
        var select = input.WasKeyPressed(Key.Enter) || (confirm && !_confirm);
        var cancel = input.WasKeyPressed(Key.Escape) || (back && !_back);
        var x = (input.WasKeyPressed(Key.Right) || input.WasKeyPressed(Key.D) || (pad.X > 0 && _pad.X <= 0) ? 1 : 0)
            - (input.WasKeyPressed(Key.Left) || input.WasKeyPressed(Key.A) || (pad.X < 0 && _pad.X >= 0) ? 1 : 0);
        var y = (input.WasKeyPressed(Key.Down) || input.WasKeyPressed(Key.S) || (pad.Y < 0 && _pad.Y >= 0) ? 1 : 0)
            - (input.WasKeyPressed(Key.Up) || input.WasKeyPressed(Key.W) || (pad.Y > 0 && _pad.Y <= 0) ? 1 : 0);
        (_pad, _confirm, _back) = (pad, confirm, back);

        if (cancel)
        {
            // 一番上で戻る（B・Escape）は終了しない: 試合から B で戻った勢いの 1 押しでゲームが閉じていた。
            // 「終了」の行へ移すだけにして、終了はその行を決定したときだけ。
            if (_page == Page.Modes)
            {
                _mode = Modes.Length - 1;
                playCue?.Invoke(false);
                return default;
            }
            Return(back);
            return default;
        }

        if (_page == Page.Replays && input.ScrollDelta != 0f)
        {
            y = -Math.Sign(input.ScrollDelta);
        }

        var oldMode = _mode;
        var oldFocus = _focus;
        var hit = HitRow(input.MousePosition, size, RowCount);
        var click = input.WasMouseButtonPressed(MouseButton.Left);
        if (hit is { } row && (click || input.MouseDelta != Vector2.Zero))
        {
            if (_page == Page.Modes) { _mode = row; }
            else { _focus = row + (_page == Page.Replays ? FirstRecord : 0); }
            y = 0;
            select |= click;
        }

        if (select && RowCount > 0)
        {
            playCue?.Invoke(true);
        }
        else if (_mode != oldMode || _focus != oldFocus || (y != 0 && RowCount > 1)
            || (_page == Page.Setup && _focus < 4 && x != 0))
        {
            playCue?.Invoke(false);
        }

        switch (_page)
        {
            case Page.Modes:
                _mode = Wrap(_mode + y, Modes.Length);
                if (select)
                {
                    if (_mode == Modes.Length - 1)
                    {
                        QuitRequested = true;
                    }
                    else if (_mode == Modes.Length - 2)
                    {
                        Browse();
                    }
                    else
                    {
                        _page = Page.Setup;
                        _focus = 0;
                    }
                }
                break;
            case Page.Setup:
                _focus = Wrap(_focus + y, 5);
                switch (_focus)
                {
                    case 0: SelectCharacter(ref _red, ref _redWear, x + (select ? 1 : 0)); break;
                    case 1: _redWear = Wrap(_redWear + x + (select ? 1 : 0), Equipment.Length); break;
                    case 2: SelectCharacter(ref _blue, ref _blueWear, x + (select ? 1 : 0)); break;
                    case 3: _blueWear = Wrap(_blueWear + x + (select ? 1 : 0), Equipment.Length); break;
                    case 4 when select:
                        return (new MatchSelection((MatchMode)_mode, BotCatalog.Styles[_red], BotCatalog.Styles[_blue],
                            Equipment[_redWear].Value, Equipment[_blueWear].Value,
                            Equipment[_redWear].Weapon, Equipment[_blueWear].Weapon), null);
                }
                break;
            case Page.Replays when _files.Length > 0:
                _focus = Wrap(_focus + y, _files.Length);
                if (y != 0)
                {
                    _firstRecord = Math.Clamp(_focus - VisibleRecords / 2, 0, Math.Max(0, _files.Length - VisibleRecords));
                }
                if (_focus != oldFocus) { _error = null; }
                if (select)
                {
                    try
                    {
                        var replay = ReplayFile.Read(_files[_focus]);
                        if (replay.Steps.Count == 0)
                        {
                            _error = "この記録には再生できる試合がありません。";
                        }
                        else
                        {
                            _ = replay.Header.Tuning;
                            return (null, replay);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                        or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException)
                    {
                        Report("記録を開けません。形式・物理の版・アクセス権を確認してください。", ex);
                    }
                }
                break;
        }
        return default;
    }

    public void Return(bool backHeld = false)
    {
        _back = backHeld;
        _page = Page.Modes;
        _focus = 0;
        _error = null;
    }

    public void PlaybackFailed(Exception ex) => Report("この記録を再生できません。", ex);

    private void Browse()
    {
        _page = Page.Replays;
        _firstRecord = 0;
        _focus = 0;
        _files = [];
        try
        {
            if (Directory.Exists(replayDirectory))
            {
                _files = Directory.EnumerateFiles(replayDirectory, "*" + ReplayFile.Extension)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ThenBy(path => path, StringComparer.Ordinal)
                    .ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("記録フォルダーを読み込めません。", ex);
        }
    }

    private void Report(string message, Exception ex)
    {
        _error = message;
        Console.Error.WriteLine(ex);
    }

    private static int StepAxis(float value) => value > 0.55f ? 1 : value < -0.55f ? -1 : 0;
    private static int Wrap(int index, int count) => (index + count) % count;

    private const int VisibleRecords = 5;
    private const int FirstRowY = 125;
    private const int RowStep = 49;
    private const int RowWidth = 466;
    private const int RowHeight = 38;
    private int RowCount => _page == Page.Replays ? Math.Min(VisibleRecords, _files.Length) : 5;

    internal static int? HitRow(Vector2 pointer, Vector2 size, int rows)
    {
        var canvas = new Canvas(size);
        for (var row = 0; row < rows; row++)
        {
            var delta = Vector2.Abs(pointer - canvas.At(320, FirstRowY + row * RowStep));
            if (canvas.Scale > 0f && delta.X <= RowWidth * canvas.Scale / 2 && delta.Y <= RowHeight * canvas.Scale / 2)
            {
                return row;
            }
        }
        return null;
    }
    private int FirstRecord => _firstRecord;

    public IReadOnlyList<ScreenTextDraw> Text(Vector2 size)
    {
        var layout = new Canvas(size);
        var text = new List<ScreenTextDraw>();
        void Add(string value, float x, float y, float height, Vector4 ink, TextAnchor anchor = TextAnchor.Left)
        {
            var position = layout.At(x, y);
            text.Add(new(value, position + new Vector2(2f) * layout.Scale, height * layout.Scale, Shadow, anchor));
            text.Add(new(value, position, height * layout.Scale, ink, anchor));
        }

        var heading = _page switch { Page.Setup => "対戦セットアップ", Page.Replays => "リプレイセレクト", _ => "ゲームセレクト" };
        Add("MINI RING BOXING", 320, 20, 19, Pale, TextAnchor.Center);
        Add(heading, 320, 51, 31, Gold, TextAnchor.Center);

        if (_page == Page.Modes)
        {
            for (var i = 0; i < Modes.Length; i++)
            {
                Add(Modes[i], 104, 108 + i * 49, 23, i == _mode ? Gold : Pale);
                Add(ModeDetails[i], 539, 114 + i * 49, 16, Pale, TextAnchor.Right);
            }
        }
        else if (_page == Page.Setup)
        {
            var labels = new[] { "赤  キャラクター", "赤  武器", "青  キャラクター", "青  武器", "試合開始" };
            var values = new[] { CharacterLabel(_red), Equipment[_redWear].Label, CharacterLabel(_blue), Equipment[_blueWear].Label, "" };
            for (var i = 0; i < labels.Length; i++)
            {
                Add(labels[i], 104, 111 + i * 49, 21, _focus == i ? Gold : Pale);
                Add(values[i], 539, 111 + i * 49, 21, _focus == i ? Gold : Pale, TextAnchor.Right);
            }
            Add("剣・こんぼうは弱／強で攻撃   空手（素手）は強で蹴り", 320, 384, 15, Pale, TextAnchor.Center);
            Add((MatchMode)_mode == MatchMode.Versus ? "赤 WASD / FGH     青 矢印 / JKL     またはパッド２台"
                : Modes[_mode], 320, 361, 15, Pale, TextAnchor.Center);
        }
        else
        {
            for (var row = 0; row < Math.Min(VisibleRecords, _files.Length); row++)
            {
                var index = FirstRecord + row;
                var name = Path.GetFileNameWithoutExtension(_files[index]);
                if (name.Length > 36) { name = string.Concat(name.AsSpan(0, 36), "…"); }
                Add(name, 104, 111 + row * 49, 20, index == _focus ? Gold : Pale);
            }
            if (_files.Length == 0)
            {
                Add("保存された試合はありません", 320, 185, 23, Pale, TextAnchor.Center);
            }
            else
            {
                Add($"{_focus + 1} / {_files.Length}", 535, 360, 16, Pale, TextAnchor.Right);
            }
        }

        if (_error is { } error) { Add(error, 320, 384, 16, Gold, TextAnchor.Center); }
        Add("WASD / 矢印   Enter / A / 左クリック   Esc / B", 320, 420, 16, Pale, TextAnchor.Center);
        return text;
    }

    private static void SelectCharacter(ref int character, ref int equipment, int delta)
    {
        if (delta == 0) { return; }
        character = Wrap(character + delta, BotCatalog.Styles.Count);
        if (BotCatalog.Styles[character].Name == BotCatalog.Karate.Name)
        {
            equipment = Array.FindIndex(Equipment, item => item.Value == HandWear.None);
        }
    }

    private static string CharacterLabel(int index)
    {
        var id = BotCatalog.Styles[index].Name;
        return $"{BotNames.RingNameOf(id)}   /   {(id == BotCatalog.Karate.Name ? "空手" : id.ToUpperInvariant())}";
    }

    private static readonly Vector4 Shadow = new(0.02f, 0.04f, 0.12f, 1f);
    private static readonly Vector4 Pale = new(0.85f, 0.92f, 0.95f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.79f, 0.30f, 1f);
    private static readonly Vector4 Cyan = new(0.26f, 0.8f, 1f, 1f);

    public IReadOnlyList<UiShapeDraw> Shapes(Vector2 size)
    {
        var canvas = new Canvas(size);
        var shapes = new List<UiShapeDraw> { UiShapeDraw.Rounded(size / 2, size, 0f, Shadow) };
        void Plate(float x, float y, float w, float h, Vector4 top, Vector4 bottom) =>
            shapes.Add(UiShapeDraw.VerticalGradient(canvas.At(x, y), new Vector2(w, h) * canvas.Scale, 0f, top, bottom));
        void Frame(float x, float y, float w, float h, Vector4 color, float thickness) =>
            shapes.Add(UiShapeDraw.Outline(canvas.At(x, y), new Vector2(w, h) * canvas.Scale, 0f, color, thickness * canvas.Scale));

        Plate(320, 232, 640, 464, new(0.06f, 0.24f, 0.34f, 1), new(0.02f, 0.07f, 0.16f, 1));
        for (var y = 4; y < 464; y += 8)
        {
            Plate(320, y, 640, 1, new(0.28f, 0.66f, 0.74f, 0.14f), new(0.28f, 0.66f, 0.74f, 0.14f));
        }
        Frame(320, 232, 614, 439, new(0.17f, 0.50f, 0.66f, 1), 2);
        Plate(320, 67, 532, 47, new(0.12f, 0.59f, 0.86f, 1), new(0.04f, 0.17f, 0.47f, 1));
        Frame(320, 67, 532, 47, Cyan, 2);
        var selected = _page == Page.Modes ? _mode : _page == Page.Replays ? _focus - FirstRecord : _focus;
        var rows = RowCount;
        for (var row = 0; row < rows; row++)
        {
            var y = FirstRowY + row * RowStep;
            Plate(324, y + 3, RowWidth, RowHeight, Shadow, Shadow);
            Plate(320, y, RowWidth, RowHeight, row == selected ? new(0.16f, 0.50f, 0.72f, 1) : new(0.10f, 0.28f, 0.40f, 1),
                new(0.04f, 0.13f, 0.26f, 1));
            Frame(320, y, RowWidth, RowHeight, row == selected ? Cyan : new(0.19f, 0.38f, 0.51f, 1), 2);
            if (row == selected) { Plate(94, y, 5, 24, Gold, Gold); }
        }
        Plate(320, 429, 532, 29, new(0.14f, 0.34f, 0.38f, 1), new(0.07f, 0.19f, 0.26f, 1));
        return shapes;
    }

    private readonly record struct Canvas(Vector2 Size)
    {
        private static readonly Vector2 DesignSize = new(640, 464);
        public float Scale => MathF.Min(Size.X / DesignSize.X, Size.Y / DesignSize.Y);
        public Vector2 At(float x, float y) => (Size - DesignSize * Scale) / 2 + new Vector2(x, y) * Scale;
    }
}
