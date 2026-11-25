using System.Diagnostics;
using System.Numerics;
using Aigis.Platform;
using MiniRing.Presentation;
using MiniRing.Replay;
using Silk.NET.Input;

namespace MiniRing.Desktop;

/// <summary>
/// リプレイの操作: キーとマウスを、再生ヘッド（<see cref="ReplayTransport"/>）・カメラ（<see cref="ReplayCamera"/>）・表示の切り替えへ。
/// 割り当ては <see cref="Rows"/>（画面の [H] の一覧と docs/REPLAY.md の表）と同じ。
/// </summary>
internal sealed class ReplayControls(ReplayViewerGame viewer, ReplayCamera camera)
{
    /// <summary>見どころへ飛ぶとき、その少し前から見せる [s]（打ち出しから見えるように）。</summary>
    public const float HighlightLead = 1.5f;

    /// <summary>Shift を押して見どころへ飛ぶときの、大きな見どころの目立ちの下限（ダウン・骨折・切断・KO・一撃）。</summary>
    public const float BigHighlight = 6f;

    /// <summary>操作の一覧（キー, すること）。</summary>
    public static IReadOnlyList<(string Keys, string Action)> Rows { get; } =
    [
        ("Space / K", "再生 / 一時停止"),
        ("L / J", "前へ再生 / 巻き戻し（もう一度で速く）"),
        ("R", "再生の向きを反転"),
        ("↑ / ↓", "速く / 遅く（スロー）"),
        ("1〜8", "0.05 / 0.1 / 0.25 / 0.5 / 1 / 2 / 4 / 8 倍"),
        ("← / →", "5 秒戻る / 進む（Shift 1 秒・Ctrl 30 秒）"),
        (", / .", "1 コマ戻る / 進む（止まる）"),
        ("Home / End", "最初 / 最後へ"),
        ("PageUp / PageDown", "前 / 次のラウンドへ"),
        ("[ / ]", "前 / 次の見どころへ（Shift で大きな物だけ）"),
        ("I / O / U", "A を置く / B を置いて A-B を繰り返す / 消す"),
        ("バーをクリック・ドラッグ", "その時刻へ（印の上なら見どころへ）"),
        ("Tab", "カメラ: 回転中心 ⇔ FPS"),
        ("左ドラッグ（右ドラッグ）", "回す / 向きを変える"),
        ("中ドラッグ", "回転中心・目をずらす"),
        ("ホイール", "寄る・離れる / FPS は前後"),
        ("W A S D", "前後左右に動く（回転中心ごと）"),
        ("Q / E", "下 / 上へ動く（Shift 速く・Ctrl ゆっくり）"),
        ("F", "追う: なし → 両者 → 赤 → 青"),
        ("F5 / F6 / F7 / F8", "定位置: 中継 / 真上 / 赤の後ろ / 青の後ろ"),
        ("Z", "カメラを最初の視点へ"),
        ("Y", "マウスの上下を反転"),
        ("G / V / C", "試合の HUD / 下のパネル / 顔の窓"),
        ("H / F1", "この一覧"),
        ("Esc", "メニューへ戻る"),
    ];

    private static readonly Key[] SpeedKeys =
        [Key.Number1, Key.Number2, Key.Number3, Key.Number4, Key.Number5, Key.Number6, Key.Number7, Key.Number8];

    private long _last = Stopwatch.GetTimestamp();
    private bool _placed;
    private DragKind _drag;
    private bool _wasPlaying;

    private enum DragKind
    {
        None,
        Rotate,
        Pan,
        Scrub,
    }

    public BoxingInput Read(InputSnapshot input, Vector2 screen)
    {
        ArgumentNullException.ThrowIfNull(input);
        var now = Stopwatch.GetTimestamp();
        var dt = Math.Clamp((float)Stopwatch.GetElapsedTime(_last, now).TotalSeconds, 0f, 0.1f);
        _last = now;
        if (!_placed)
        {
            camera.Reset(viewer.Focus);
            _placed = true;
        }

        viewer.HelpRows = Rows;
        Keys(input);
        Mouse(input, screen);
        Fly(input, dt);
        viewer.Update(_drag == DragKind.Scrub ? 0f : dt);
        camera.Update(viewer.Focus, dt);
        viewer.CameraLabel = camera.Label;
        return default;
    }

    private float Available => viewer.Scanner.ScannedTime;

    private void Keys(InputSnapshot input)
    {
        var t = viewer.Transport;
        var shift = input.IsKeyDown(Key.ShiftLeft) || input.IsKeyDown(Key.ShiftRight);
        var ctrl = input.IsKeyDown(Key.ControlLeft) || input.IsKeyDown(Key.ControlRight);
        bool Pressed(Key key) => input.WasKeyPressed(key);

        if (Pressed(Key.Space)) { t.TogglePlay(); }
        if (Pressed(Key.K)) { t.Pause(); }
        if (Pressed(Key.L)) { t.ShuttleForward(); }
        if (Pressed(Key.J)) { t.ShuttleBackward(); }
        if (Pressed(Key.R)) { t.Reverse(); viewer.Toast(t.Direction > 0 ? "前へ再生" : "巻き戻し"); }
        if (Pressed(Key.Up)) { t.Faster(); viewer.Toast($"{t.Speed:0.##} 倍"); }
        if (Pressed(Key.Down)) { t.Slower(); viewer.Toast($"{t.Speed:0.##} 倍"); }
        for (var i = 0; i < SpeedKeys.Length; i++)
        {
            if (Pressed(SpeedKeys[i])) { t.SetSpeedIndex(i); viewer.Toast($"{t.Speed:0.##} 倍"); }
        }

        var jump = ctrl ? 30f : shift ? 1f : 5f;
        if (Pressed(Key.Left)) { t.SeekBy(-jump, Available); }
        if (Pressed(Key.Right)) { t.SeekBy(jump, Available); }
        if (Pressed(Key.Comma)) { t.StepFrames(-1, Available); }
        if (Pressed(Key.Period)) { t.StepFrames(1, Available); }
        if (Pressed(Key.Home)) { t.Seek(0f, Available); }
        if (Pressed(Key.End)) { t.Seek(t.Duration, Available); }
        if (Pressed(Key.PageUp)) { Round(-1); }
        if (Pressed(Key.PageDown)) { Round(+1); }
        if (Pressed(Key.LeftBracket)) { Highlight(-1, shift); }
        if (Pressed(Key.RightBracket)) { Highlight(+1, shift); }
        if (Pressed(Key.I)) { t.MarkLoopStart(); viewer.Toast($"A {ReplayHud.Clock(t.Time)}"); }
        if (Pressed(Key.O)) { t.MarkLoopEnd(); viewer.Toast(t.Looping ? "A-B を繰り返す" : $"B {ReplayHud.Clock(t.Time)}"); }
        if (Pressed(Key.U)) { t.ClearLoop(); viewer.Toast("区間を消した"); }

        if (Pressed(Key.Tab)) { camera.ToggleMode(); viewer.Toast(camera.Label); }
        if (Pressed(Key.F)) { camera.CycleFollow(); viewer.Toast(camera.Label); }
        if (Pressed(Key.F5)) { camera.Preset(0, viewer.Focus); viewer.Toast("中継の視点"); }
        if (Pressed(Key.F6)) { camera.Preset(1, viewer.Focus); viewer.Toast("真上"); }
        if (Pressed(Key.F7)) { camera.Preset(2, viewer.Focus); viewer.Toast("赤の後ろ"); }
        if (Pressed(Key.F8)) { camera.Preset(3, viewer.Focus); viewer.Toast("青の後ろ"); }
        if (Pressed(Key.Z)) { camera.Reset(viewer.Focus); viewer.Toast("最初の視点"); }
        if (Pressed(Key.Y)) { camera.InvertY = !camera.InvertY; viewer.Toast(camera.InvertY ? "マウスの上下を反転" : "マウスの上下を戻した"); }

        if (Pressed(Key.H) || Pressed(Key.F1)) { viewer.ShowHelp = !viewer.ShowHelp; }
        if (Pressed(Key.G)) { viewer.ShowGameHud = !viewer.ShowGameHud; }
        if (Pressed(Key.V)) { viewer.ShowPanel = !viewer.ShowPanel; }
        if (Pressed(Key.C)) { viewer.ShowFaces = !viewer.ShowFaces; }
    }

    private void Round(int direction)
    {
        var t = viewer.Transport;
        var starts = viewer.Timeline.RoundStarts();
        // 「前のラウンド」は今のラウンドの頭から 1 秒以上進んでいれば今のラウンドの頭（動画の前の章と同じ）。
        float? target = direction > 0
            ? starts.Where(s => s > t.Time + 0.05f).Select(s => (float?)s).FirstOrDefault()
            : starts.Where(s => s < t.Time - 1f).Select(s => (float?)s).LastOrDefault() ?? 0f;
        if (target is { } time)
        {
            t.Seek(time, Available);
            viewer.Toast($"R{starts.Count(s => s <= time + 1e-3f)}");
        }
    }

    private void Highlight(int direction, bool bigOnly)
    {
        var t = viewer.Transport;
        var minimum = bigOnly ? BigHighlight : 0f;
        // 見どころの少し前に飛ぶので、今の時刻はその分ずらして探す（続けて押すと次へ進む）。
        var mark = direction > 0
            ? viewer.Timeline.NextHighlight(t.Time + HighlightLead, minimum)
            : viewer.Timeline.PreviousHighlight(t.Time + HighlightLead - 0.05f, minimum);
        if (mark is not { } m)
        {
            viewer.Toast(direction > 0 ? "この先に見どころは無い（読み込み中なら待つ）" : "この前に見どころは無い");
            return;
        }

        JumpTo(m);
    }

    private void JumpTo(ReplayMark mark)
    {
        viewer.Transport.Seek(mark.Time - HighlightLead, Available);
        viewer.Toast($"{ReplayHud.KindLabel(mark.Highlight)}（{(mark.Red ? viewer.RedLabel : viewer.BlueLabel)}） {ReplayHud.Clock(mark.Time)}");
    }

    private void Mouse(InputSnapshot input, Vector2 screen)
    {
        var mouse = input.MousePosition;
        viewer.Hover = mouse;
        var t = viewer.Transport;
        var overBar = viewer.ShowPanel && ReplayHud.TimeAt(mouse, screen, t.Duration) is not null;
        var leftDown = input.IsMouseButtonDown(MouseButton.Left);
        var rightDown = input.IsMouseButtonDown(MouseButton.Right);
        var middleDown = input.IsMouseButtonDown(MouseButton.Middle);

        if (input.WasMouseButtonPressed(MouseButton.Left) && overBar)
        {
            var (_, marks, _) = viewer.Timeline.Snapshot();
            if (ReplayHud.MarkAt(mouse, screen, t.Duration, marks) is { } mark)
            {
                JumpTo(mark);
                _drag = DragKind.None;
                return;
            }

            _drag = DragKind.Scrub;
            _wasPlaying = t.Playing;
            t.Pause();
        }
        else if (input.WasMouseButtonPressed(MouseButton.Left) || input.WasMouseButtonPressed(MouseButton.Right))
        {
            _drag = DragKind.Rotate;
        }
        else if (input.WasMouseButtonPressed(MouseButton.Middle))
        {
            _drag = DragKind.Pan;
        }

        switch (_drag)
        {
            case DragKind.Scrub when leftDown:
                if (ReplayHud.Measure(screen) is var layout)
                {
                    t.Seek(layout.Time(mouse.X, t.Duration), Available);
                }

                break;
            case DragKind.Scrub:
                if (_wasPlaying) { t.Resume(); }
                _drag = DragKind.None;
                break;
            case DragKind.Rotate when leftDown || rightDown:
                camera.Rotate(input.MouseDelta);
                break;
            case DragKind.Pan when middleDown:
                camera.Pan(input.MouseDelta);
                break;
            case DragKind.Rotate or DragKind.Pan:
                _drag = DragKind.None;
                break;
        }

        if (!overBar || !viewer.ShowPanel)
        {
            camera.Scroll(input.ScrollDelta);
        }
        else if (input.ScrollDelta != 0f)
        {
            // バーの上のホイールはシーク（1 目盛り 1 秒）。
            t.SeekBy(-input.ScrollDelta, Available);
        }
    }

    private void Fly(InputSnapshot input, float dt)
    {
        var local = Vector3.Zero;
        if (input.IsKeyDown(Key.W)) { local.Z += 1f; }
        if (input.IsKeyDown(Key.S)) { local.Z -= 1f; }
        if (input.IsKeyDown(Key.D)) { local.X += 1f; }
        if (input.IsKeyDown(Key.A)) { local.X -= 1f; }
        if (input.IsKeyDown(Key.E)) { local.Y += 1f; }
        if (input.IsKeyDown(Key.Q)) { local.Y -= 1f; }
        var shift = input.IsKeyDown(Key.ShiftLeft) || input.IsKeyDown(Key.ShiftRight);
        var ctrl = input.IsKeyDown(Key.ControlLeft) || input.IsKeyDown(Key.ControlRight);
        camera.Move(local, dt, shift ? 3f : ctrl ? 0.3f : 1f);
    }
}
