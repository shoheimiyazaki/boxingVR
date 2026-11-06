using System.Numerics;
using Aigis.Foundation;
using Aigis.Player;
using MiniRing;
using MiniRing.Desktop;
using Silk.NET.Input;

// Mini Ring Boxing（デスクトップ版）: VR なしで同じ試合を遊ぶ／確かめるためのホスト。
// 操作は KeyboardControl を参照。中ボタンドラッグでカメラ回転、スクロールでズーム、Escape で終了。

var profiler = new StartupProfiler();
profiler.Mark("process start");

using var game = new BoxingGame();
profiler.Mark("BoxingGame constructed");

// 自分のファイターの真後ろ・少し上（リング手前に立って見下ろす大人の目線）から見る。
var camera = new OrbitCamera(initialDistance: 0.9f) { Target = BoxingGame.RingCenter with { Y = BoxingGame.RingSurfaceHeight + 0.2f } };
camera.ApplyDrag(new Vector2(-MathF.PI / 0.005f, -60f));
var keyboard = new KeyboardControl();

GameHost.Run(
    profiler,
    new GameHost.Options(
        "Mini Ring Boxing",
        "WASD: move, LMB/RMB: right/left punch (Shift: uppercut), Q/E: grab, R: reset round, middle-drag: orbit, Escape: quit.",
        camera,
        "miniring",
        OrbitButton: MouseButton.Middle),
    game,
    readInput: (input, _, _) => keyboard.Read(input, camera.YawRadians, game.PlayerSense, FixedStep.Default.Dt));
