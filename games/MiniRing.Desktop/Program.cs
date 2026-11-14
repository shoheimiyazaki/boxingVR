using System.Numerics;
using Aigis.Foundation;
using Aigis.Player;
using MiniRing;
using MiniRing.Audio;
using MiniRing.Desktop;
using MiniRing.Replay;
using Silk.NET.Input;

var classic = Option("--control") == "classic";
if (args.Contains("--control") && Option("--control") is not ("classic" or "intent"))
{
    Console.Error.WriteLine("--control は intent / classic のどちらかです。");
    return 1;
}

// Mini Ring Boxing（デスクトップ版）: VR なしで同じ試合を遊ぶ／確かめるためのホスト。
// 操作は KeyboardControl を参照。中ボタンドラッグでカメラ回転、スクロールでズーム、Escape で終了。
//   （引数なし）            遊ぶ。試合は replays/ に自動で記録される（--no-record で無効）
//   --replay <file.mrrep>   記録した試合を実時間で再生する（VR の記録も可）
//   --verify <file.mrrep>   ウィンドウを開かず最速で再生し、記録どおりに再現できるかを照合して終了
//   --break-sound <glass|crumble|crunch|circuit|metal>  ボクセルが砕ける音の方式（既定 glass）
//   --hit-sound <on|off>  パンチが当たる音（既定 on。ダメージを知らせる音）
//   --tv                    テレビ中継風: 30 秒 × 5 ラウンド・休み 10 秒（休みに体が治る）、得点表と時計、会場の歓声（このモードは記録しない）
//   --no-facecam            画面の両脇の顔の窓を出さない（窓の分の描画が要らない）
//   --speed <0.5|0.75|1>    最初の速さ（既定 0.75 倍）。遊んでいる間・再生中も 1 / 2 / 3 キーで 0.5 / 0.75 / 1.0 倍に切り替わる
//   --wear <glove|none|knuckles|katar|gauntlet>  両コーナーが手にはめる物（既定 glove。glove 以外は記録しない: 記録の形式にはめる物が入っていない）

if (!HitSoundOption.TryParse(args, out var hitSounds, out var hitError))
{
    Console.Error.WriteLine(hitError);
    return 1;
}

if (!BreakSoundOption.TryParse(args, out var breakStyle, out var breakError))
{
    Console.Error.WriteLine(breakError);
    return 1;
}

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
    classic
        ? "WASD: move, LMB/RMB: right/left hand (Ctrl: hook, Shift: uppercut, Alt: body), arrows: head, Q/E: grab, R: reset, 1/2/3: speed, middle-drag: orbit, Escape: quit."
        : "Arrows/WASD: move, J/RMB: light, K/LMB: heavy, hold/release: load/punch, Space+direction: evade, Space: guard, R: reset, 1/2/3: speed, middle-drag: orbit, Escape: quit.",
    camera,
    "miniring",
    OrbitButton: MouseButton.Middle);

// 画面の両脇に両ボクサーの顔の窓（左が自分・右が CPU）（頭のボクセルの格子に固定したカメラで、リングの上の実際の顔を正面から撮る）。
var faceCam = args.Contains("--no-facecam") ? null : FaceCam.Default;
Func<BoxingGame, Func<Vector2, IReadOnlyList<Aigis.Render.CameraView>?>?> faceViews =
    g => faceCam is null ? null : size => faceCam.Views(camera, g, size);

// 試合の速さ（標準 0.75 倍）。1 / 2 / 3 キーで切り替える。
if (args.Contains("--speed") && GameSpeed.Parse(Option("--speed")) is null)
{
    Console.Error.WriteLine("--speed は 0.5 / 0.75 / 1 のどれかです。");
    return 1;
}

var speed = new GameSpeed(GameSpeed.Parse(Option("--speed")) ?? GameSpeed.Standard);
var speedKeys = new[] { Key.Number1, Key.Number2, Key.Number3 };
void ReadSpeed(Aigis.Platform.InputSnapshot input)
{
    for (var i = 0; i < speedKeys.Length; i++)
    {
        if (input.WasKeyPressed(speedKeys[i]))
        {
            speed.SelectIndex(i);
        }
    }
}

options = options with { TimeScale = () => speed.Value };

if (Option("--replay") is { } replayPath)
{
    using var replay = new ReplayGame(ReplayFile.Read(replayPath));
    replay.Game.FaceCam = faceCam;
    replay.Game.Speed = speed;
    using var replayWithSound = new HitSoundGame(replay, () => replay.FrameHits, HitSoundPlayer.CreateDefault(breakStyle, hitSounds), () => replay.FrameShatters, () => replay.FrameLandings, () => replay.FrameSwings, () => replay.FrameBell, () => replay.FrameGrazes, () => replay.FrameSevers, contacts: () => replay.FrameContacts, weaponHits: () => replay.FrameWeaponHits);
    profiler.Mark("replay loaded");
    GameHost.Run(profiler, options with { DynamicViews = faceViews(replay.Game) }, replayWithSound, readInput: (input, _, _) =>
    {
        ReadSpeed(input);
        return default(BoxingInput);
    });
    return 0;
}

var tv = args.Contains("--tv");
MiniRing.Weapons.HandWear wear;
try
{
    wear = Option("--wear") is { } wearName ? MiniRing.Weapons.HandWears.Parse(wearName) : MiniRing.Weapons.HandWear.Glove;
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

using var game = new BoxingGame(corners: BoxingCorners.Default with { PlayerWear = wear, CpuWear = wear }, rules: tv ? MatchRules.Tv : null) { FaceCam = faceCam, Speed = speed };
profiler.Mark("BoxingGame constructed");
var keyboard = new KeyboardControl();
var intentKeyboard = new IntentKeyboardControl();
ReplayRecorder? recorder = null;
// テレビ中継の試合（ラウンドの時間・休み・体の再生）は記録の形式に入っていないので、記録しない。
if (!args.Contains("--no-record") && !tv && wear == MiniRing.Weapons.HandWear.Glove)
{
    recorder = ReplayRecorder.TryStartFile(Path.Combine(Environment.CurrentDirectory, "replays"), game, "desktop", out var recordingMessage);
    Console.WriteLine(recordingMessage);
}

// 打撃音は試合の外側で鳴らす（記録・物理には触れない）。
using var recording = recorder is null ? null : new RecordingGame(game, recorder);
using var withSound = new HitSoundGame(recording ?? (IGame<BoxingInput>)game, () => game.RecentHits, HitSoundPlayer.CreateDefault(breakStyle, hitSounds), () => game.RecentShatters, () => game.RecentLandings, () => game.RecentSwings, () => game.RecentBell, () => game.RecentGrazes, () => game.RecentSevers,
    tv ? CrowdFor(game) : null, () => game.RecentContacts, () => [.. game.WeaponHits, .. game.SelfWeaponHits]);
GameHost.Run(profiler, options with { DynamicViews = faceViews(game) }, withSound, readInput: (input, _, _) =>
{
    ReadSpeed(input);
    // 拳の経路は試合の時間で進める（遅くしても体と同じ速さで遅くなる）。
    return classic
        ? keyboard.Read(input, camera.YawRadians, game.PlayerSense, FixedStep.Default.Dt * speed.Value)
        : intentKeyboard.Read(input, game.PlayerSense, FixedStep.Default.Dt * speed.Value, game.Round, camera.YawRadians);
});
return 0;

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// テレビ中継のモードの会場: ざわめきを流し続け、試合を見て反応（歓声・どよめき・カウントの掛け声）を鳴らす。
static Action<HitSoundPlayer, float> CrowdFor(BoxingGame game)
{
    var broadcast = new Broadcast("YOU", "CPU");
    var clock = 0f;
    var step = 0;
    var played = 0;
    var started = false;
    return (sounds, dt) =>
    {
        if (!started)
        {
            sounds.PrepareCrowd(seed: 7); // 別のスレッドで作る（試合を止めない）
            started = true;
        }

        clock += dt;
        broadcast.Observe(game, clock, step++);
        sounds.PlayCrowd(new CrowdCue(clock, null, 0f, 0)); // ざわめきを（できていれば）流し始める
        // 反応は時刻が来たものから鳴らす（どよめきの後の悲鳴のように、少し後に置かれたものがある）。
        for (; played < broadcast.Cues.Count && broadcast.Cues[played].At <= clock; played++)
        {
            sounds.PlayCrowd(broadcast.Cues[played]);
        }
    };
}
