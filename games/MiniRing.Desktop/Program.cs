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
//   （引数なし）            ゲームセレクトを開く（--quick で直接シングルプレイ）
//   --replay <file.mrrep>   記録した試合を実時間で再生する（VR の記録も可）
//   --verify <file.mrrep>   ウィンドウを開かず最速で再生し、記録どおりに再現できるかを照合して終了
//   --break-sound <glass|crumble|crunch|circuit|metal>  ボクセルが砕ける音の方式（既定 glass）
//   --hit-sound <on|off>  パンチが当たる音（既定 on。ダメージを知らせる音）
//   --tv                    テレビ中継風: 30 秒 × 5 ラウンド・休み 10 秒（休みに体が治る）、得点表と時計、会場の歓声
//   --no-facecam            画面の両脇の顔の窓を出さない（窓の分の描画が要らない）
//   --speed <0.5|0.75|1>    最初の速さ（既定 0.75 倍）。遊んでいる間・再生中も 1 / 2 / 3 キーで 0.5 / 0.75 / 1.0 倍に切り替わる
//   --wear <glove|none|knuckles|katar|gauntlet>  両コーナーが手にはめる物（既定 glove）

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
        : "Arrows/WASD or left stick: move; J/RMB or X: light; K/LMB or Y: heavy; Space or A: evade/guard; R or Start/Back: reset; middle-drag or right stick: orbit; 1/2/3: speed; Escape: quit.",
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

var replayDirectory = Path.Combine(Environment.CurrentDirectory, "replays");
if (Option("--replay") is { } replayPath)
{
    using var session = OpenReplay(ReplayFile.Read(replayPath));
    GameHost.Run(profiler, options with { DynamicViews = faceViews(session.Game) }, session.Host,
        (input, _, _) => session.Read(input));
}
else if (args.Contains("--quick"))
{
    using var session = OpenMatch(BoxingCorners.Default with { PlayerWear = wear, CpuWear = wear }, MatchMode.Single);
    GameHost.Run(profiler, options with { DynamicViews = faceViews(session.Game) }, session.Host,
        (input, _, _) => session.Read(input));
}
else
{
    using var menuGame = new MenuGame(new MatchMenu(wear, replayDirectory),
        selection => OpenMatch(selection.Corners, selection.Mode), OpenReplay);
    GameHost.Run(profiler, options with
    {
        HelpText = "Menu: arrows or D-pad, Enter/A to select, Escape/B to go back. In a match, Escape/B returns to Game Select.",
        DynamicViews = size => menuGame.ActiveGame is { } game ? faceViews(game)?.Invoke(size) : null,
    }, menuGame, (input, _, _) => menuGame.Read(input));
}
return 0;

MatchSession OpenReplay(ReplayData data)
{
    var replay = new ReplayGame(data);
    try
    {
        replay.Game.FaceCam = faceCam;
        replay.Game.Speed = speed;
        camera.Target = replay.Game.Layout.Center with { Y = BoxingGame.RingSurfaceHeight + 0.2f };
        var sound = new HitSoundGame(replay, () => replay.FrameHits, HitSoundPlayer.CreateDefault(breakStyle, hitSounds),
            () => replay.FrameShatters, () => replay.FrameLandings, () => replay.FrameSwings, () => replay.FrameBell,
            () => replay.FrameGrazes, () => replay.FrameSevers, contacts: () => replay.FrameContacts,
            weaponHits: () => replay.FrameWeaponHits);
        return new MatchSession(replay.Game, sound, input =>
        {
            ReadSpeed(input);
            return default;
        }, new SessionResources(replay, null, sound));
    }
    catch
    {
        replay.Dispose();
        throw;
    }
}

MatchSession OpenMatch(BoxingCorners corners, MatchMode mode)
{
    var game = new BoxingGame(corners: corners, rules: tv ? MatchRules.Tv : null) { FaceCam = faceCam, Speed = speed };
    ReplayRecorder? recorder = null;
    try
    {
        camera.Target = game.Layout.Center with { Y = BoxingGame.RingSurfaceHeight + 0.2f };
        var keyboard = new KeyboardControl();
        var intentKeyboard = new IntentKeyboardControl(corners.PlayerWeapon,
            LocalVersusControl.IsKarate(corners.PlayerName, corners.PlayerWear));
        var local = new LocalVersusControl(corners);
        var followCamera = new FollowCameraRig(camera, BoxingGame.RingSurfaceHeight + 0.2f);
        if (!args.Contains("--no-record"))
        {
            recorder = ReplayRecorder.TryStartFile(replayDirectory, game, "desktop", out var message);
            Console.WriteLine(message);
        }

        IGame<BoxingInput> recorded = recorder is null ? game : new RecordingGame(game, recorder);
        var sound = new HitSoundGame(recorded, () => game.RecentHits, HitSoundPlayer.CreateDefault(breakStyle, hitSounds),
            () => game.RecentShatters, () => game.RecentLandings, () => game.RecentSwings, () => game.RecentBell,
            () => game.RecentGrazes, () => game.RecentSevers, tv ? CrowdFor(game) : null,
            () => game.RecentContacts, () => [.. game.WeaponHits, .. game.SelfWeaponHits]);
        return new MatchSession(game, sound, input =>
        {
            ReadSpeed(input);
            var dt = FixedStep.Default.Dt * speed.Value;
            if (mode == MatchMode.Spectate)
            {
                return new BoxingInput(default, input.WasKeyPressed(Key.R));
            }
            if (mode == MatchMode.Versus)
            {
                return local.Read(input, game, dt, camera.YawRadians);
            }
            if (classic && corners.PlayerWeapon is null && !LocalVersusControl.IsKarate(corners.PlayerName, corners.PlayerWear))
            {
                return keyboard.Read(input, camera.YawRadians, game.PlayerSense, dt);
            }
            followCamera.Update(input, game.PlayerSense);
            return intentKeyboard.Read(input, game.PlayerSense, dt, game.Round, camera.YawRadians);
        }, new SessionResources(game, recorded == game ? null : recorded, sound));
    }
    catch
    {
        try { recorder?.Dispose(); }
        finally { game.Dispose(); }
        throw;
    }
}
string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// テレビ中継のモードの会場: ざわめきを流し続け、試合を見て反応（歓声・どよめき・カウントの掛け声）を鳴らす。
static Action<HitSoundPlayer, float> CrowdFor(BoxingGame game)
{
    var broadcast = new Broadcast(game.RedLabel, game.BlueLabel);
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
