using System.Numerics;
using Aigis.Foundation;
using Aigis.Player;
using MiniRing;
using MiniRing.Audio;
using MiniRing.Desktop;
using MiniRing.Replay;
using Silk.NET.Input;

// Mini Ring Boxing（デスクトップ版）: VR なしで同じ試合を遊ぶ／確かめるためのホスト。
// 操作は KeyboardControl を参照。中ボタンドラッグでカメラ回転、スクロールでズーム、Escape で終了。
//   （引数なし）            遊ぶ。試合は replays/ に自動で記録される（--no-record で無効）
//   --replay <file.mrrep>   記録した試合を実時間で再生する（VR の記録も可）
//   --verify <file.mrrep>   ウィンドウを開かず最速で再生し、記録どおりに再現できるかを照合して終了

foreach (var flag in new[] { "--verify", "--replay" })
{
    if (args.Contains(flag) && Option(flag) is null)
    {
        Console.Error.WriteLine($"{flag} needs a recording file (.mrrep).");
        return 1;
    }
}

if (Option("--verify") is { } verifyPath)
{
    var data = ReplayFile.Read(verifyPath);
    var result = ReplayPlayer.Verify(data);
    Console.WriteLine($"{verifyPath}: {data.Steps.Count} steps ({data.Duration:0.0}s, {data.Header.Host}, recorded {data.Header.CreatedUtc:u}, build {data.Header.AppVersion})");
    Console.WriteLine(result.Reproduced
        ? $"REPRODUCED: all {result.ChecksumsMatched} checksums match."
        : $"DIVERGED at step {result.FirstDivergence} ({result.ChecksumsMatched} checksums matched before that). The recording was probably made with a different build or on a different CPU (SIMD width).");
    return result.Reproduced ? 0 : 2;
}

var profiler = new StartupProfiler();
profiler.Mark("process start");

// 自分のファイターの真後ろ・少し上（リング手前に立って見下ろす大人の目線）から見る。
var camera = new OrbitCamera(initialDistance: 0.9f) { Target = BoxingGame.RingCenter with { Y = BoxingGame.RingSurfaceHeight + 0.2f } };
camera.ApplyDrag(new Vector2(-MathF.PI / 0.005f, -60f));
var options = new GameHost.Options(
    "Mini Ring Boxing",
    "WASD: move, LMB/RMB: right/left hand (Ctrl: hook path, Shift: uppercut path, Alt: body), arrows: head, Q/E: grab, R: reset round, middle-drag: orbit, Escape: quit.",
    camera,
    "miniring",
    OrbitButton: MouseButton.Middle);

if (Option("--replay") is { } replayPath)
{
    using var replay = new ReplayGame(ReplayFile.Read(replayPath));
    using var replayWithSound = new HitSoundGame(replay, () => replay.FrameHits, HitSoundPlayer.CreateDefault());
    profiler.Mark("replay loaded");
    GameHost.Run(profiler, options, replayWithSound, readInput: (_, _, _) => default(BoxingInput));
    return 0;
}

using var game = new BoxingGame();
profiler.Mark("BoxingGame constructed");
var keyboard = new KeyboardControl();
ReplayRecorder? recorder = null;
if (!args.Contains("--no-record"))
{
    recorder = ReplayRecorder.TryStartFile(Path.Combine(Environment.CurrentDirectory, "replays"), game, "desktop", out var recordingMessage);
    Console.WriteLine(recordingMessage);
}

// 打撃音は試合の外側で鳴らす（記録・物理には触れない）。
using var recording = recorder is null ? null : new RecordingGame(game, recorder);
using var withSound = new HitSoundGame(recording ?? (IGame<BoxingInput>)game, () => game.RecentHits, HitSoundPlayer.CreateDefault());
GameHost.Run(profiler, options, withSound, readInput: (input, _, _) => keyboard.Read(input, camera.YawRadians, game.PlayerSense, FixedStep.Default.Dt));
return 0;

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
