using System.Numerics;
using Aigis.Audio;
using Aigis.Player;
using MiniRing;
using MiniRing.Audio;
using MiniRing.Desktop;
using MiniRing.Replay;
using MiniRing.Presentation;
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
//   --compact-punch         分割胴のコンパクトな回旋を使う試合設定
//   --hybrid-punch           関節駆動のパンチと従来の立位・打撃処理を組み合わせた試合設定
//   --adaptive-punch        拳の目標距離から回旋と踏み込みを連続に配分する試合設定
//   --dynamic-punch         脚の踏み込みを拳に乗せる分割胴の試合設定
//   --no-facecam            画面の両脇の顔の窓を出さない（窓の分の描画が要らない）
//   --speed <0.5|0.75|1>    最初の速さ（既定 0.75 倍）。遊んでいる間・再生中も 1 / 2 / 3 キーで 0.5 / 0.75 / 1.0 倍に切り替わる
//   --menu-script <語,...>  メニューを決めた順に操作する（left/right/up/down/enter/back/wait/scroll-up/scroll-down。0.5 秒に 1 つ。画面の確かめ用）
//   --wear <glove|none|knuckles|katar|gauntlet>  両コーナーが手にはめる物（既定 glove。--quick のときだけ。メニューからの試合は、はめる物・武器をキャラクターが決める）

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
var camera = new OrbitCamera(initialDistance: 0.9f) { Target = DisplaySpace.Standard.Point(BoxingGame.RingCenter with { Y = BoxingGame.RingSurfaceHeight + 0.563f }) }; // カメラは表示の空間（部屋）にある
camera.ApplyDrag(new Vector2(-MathF.PI / 0.005f, -60f));
// リプレイは自分のカメラ（回転中心 / FPS）を持つ。その間だけホストのカメラをそれへ切り替える。
MatchSession? directSession = null;
MenuGame? menuGameRef = null;
var hostCamera = new CameraSwitch(camera, () => directSession?.Camera ?? menuGameRef?.Camera);
var options = new GameHost.Options(
    "Mini Ring Boxing",
    classic
        ? "WASD: move, LMB/RMB: right/left hand (Ctrl: hook, Shift: uppercut, Alt: body), arrows: head, Q/E: grab, R: reset, 1/2/3: speed, middle-drag: orbit, Escape: quit."
        : "Arrows/WASD or left stick: move; J/RMB or X: light; K/LMB or Y: heavy; Space or A: evade/guard; R or Start/Back: reset; middle-drag or right stick: orbit; 1/2/3: speed; Escape: quit.",
    hostCamera,
    "miniring",
    OrbitButton: MouseButton.Middle);

// 画面の両脇に両ボクサーの顔の窓（左が自分・右が CPU）（頭のボクセルの格子に固定したカメラで、リングの上の実際の顔を正面から撮る）。
var faceCam = args.Contains("--no-facecam") ? null : FaceCam.Default;
Func<IMatchPresentation, Func<Vector2, IReadOnlyList<Aigis.Render.CameraView>?>> faceViews =
    g => size => g.Views(hostCamera, size);

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

// Reserve part of the frame for drawing when an update is catching up.
options = options with { TimeScale = () => speed.Value };
VoxelBreakSound.Prewarm(breakStyle);

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

MenuScript? menuScript;
try
{
    menuScript = Option("--menu-script") is { } scriptText ? MenuScript.Parse(scriptText) : null;
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
    directSession = session;
    GameHost.Run(profiler, options with { DynamicViews = faceViews(session.Presentation), HelpText = ReplayHelpText() }, session.Host,
        (input, _, size) => session.Read(input, size));
}
else if (args.Contains("--profile-katana"))
{
    var corners = BoxingCorners.Default with
    {
        RedBotStyle = MiniRing.Bots.BotCatalog.Boxer,
        PlayerWeapon = MiniRing.Weapons.WeaponKind.Katana,
        CpuWeapon = MiniRing.Weapons.WeaponKind.Katana,
        PlayerTwoHanded = true,
        CpuTwoHanded = true,
        UseWeaponBots = true,
    };
    using var session = OpenMatch(corners, MatchMode.Spectate);
    GameHost.Run(profiler, options with { DynamicViews = faceViews(session.Presentation) },
        session.Host, (input, _, size) => session.Read(input, size));
}
else if (args.Contains("--quick"))
{
    using var session = OpenMatch(BoxingCorners.Default with { PlayerWear = wear, CpuWear = wear }, MatchMode.Single);
    GameHost.Run(profiler, options with { DynamicViews = faceViews(session.Presentation) }, session.Host,
        (input, _, size) => session.Read(input, size));
}
else
{
    using var menuAudio = new OpenAlAudioBackend();
    var moveCue = ToneGenerator.CreateToneClip("menu-move", 520, 680, 0.045f, 0.25f, attackSeconds: 0.003f, releaseSeconds: 0.02f);
    var confirmCue = ToneGenerator.CreateToneClip("menu-confirm", 660, 1100, 0.10f, 0.35f, attackSeconds: 0.004f, releaseSeconds: 0.04f);
    // 設定（レフェリーの有無など）は記録と同じ場所（作業フォルダー）の settings.json に残す。
    // キャンペーンのボクサーは %LOCALAPPDATA%\MiniRing\campaign（CampaignStore.DefaultDirectory）。前の版の置き場（作業フォルダーの campaign/）に
    // ボクサーがいれば、初回に写す（古い方は消さない）。
    var legacyCampaign = Path.Combine(Environment.CurrentDirectory, "campaign");
    var campaignDirectory = MiniRing.Arena.Campaign.CampaignStore.DefaultDirectory(legacyCampaign);
    try
    {
        var copied = MiniRing.Arena.Campaign.CampaignStore.CopyLegacy(legacyCampaign, campaignDirectory);
        if (copied > 0) { Console.Error.WriteLine($"キャンペーンのボクサー {copied} 人を {legacyCampaign} から {campaignDirectory} へ写しました（元のファイルは残っています）。"); }
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"キャンペーンのボクサーを {legacyCampaign} から {campaignDirectory} へ写せませんでした: {e.Message}");
    }

    using var menu = new MatchMenu(replayDirectory, Path.Combine(Environment.CurrentDirectory, "settings.json"),
        confirmed => menuAudio.Play(confirmed ? confirmCue : moveCue, 1f, shouldLoop: false), script: menuScript, campaignDirectory: campaignDirectory);
    // ゲームモード（練習試合・団体の大会）の決まりと場は選びから（団体の大会は中継と同じ決まり・会場の歓声つき）。
    using var menuGame = new MenuGame(menu, selection => OpenMatch(selection.Corners, selection.Mode,
        selection.RulesOr(tv ? MatchRules.Tv : null), crowd: tv || selection.Event.Tournament), OpenReplay);
    menuGameRef = menuGame;
    GameHost.Run(profiler, options with
    {
        HelpText = "Menu: WASD, arrows, D-pad or mouse; Enter/A/left click to select; Escape/B to go back. In a match, Escape returns to Game Select; when the bout is decided, Enter/A/click (or a few seconds) shows the result.",
        DynamicViews = size => menuGame.Presentation?.Views(hostCamera, size),
    }, menuGame, (input, _, size) => menuGame.Read(input, size));
}
return 0;

// リプレイ: 裏で記録を一度だけ走査して場面と試合の展開を貯め、再生ヘッドの時刻の場面を描く（巻き戻し・スロー・シーク・自由カメラ）。
// 操作は ReplayControls.Rows（画面の [H]、docs/REPLAY.md）。
MatchSession OpenReplay(ReplayData data)
{
    // 音の再生器は画面が持って破棄する（画面を作れなければ画面の側で破棄する）。
    var viewer = new ReplayViewerGame(data, HitSoundPlayer.CreateDefault(breakStyle, hitSounds), faceCam);
    try
    {
        var replayCamera = new ReplayCamera();
        var controls = new ReplayControls(viewer, replayCamera);
        return new MatchSession(viewer, controls.Read, viewer, viewer, replayCamera);
    }
    catch
    {
        viewer.Dispose();
        throw;
    }
}

MatchSession OpenMatch(BoxingCorners corners, MatchMode mode, MatchRules? rules = null, bool crowd = false)
{
    rules ??= tv ? MatchRules.Tv : null;
    crowd |= tv;
    var tuning = args.Contains("--hybrid-punch")
        ? FighterTuning.HybridPunch
        : args.Contains("--adaptive-punch")
        ? FighterTuning.AdaptivePunch
        : args.Contains("--dynamic-punch") ? FighterTuning.DynamicPunch
        : args.Contains("--compact-punch") ? FighterTuning.CompactPunch : FighterTuning.Default;
    var game = new BoxingGame(tuning, corners: corners.StartingAtCorners(), rules: rules) { FaceCam = faceCam, Speed = speed }; // 試合はコーナーから
    ReplayRecorder? recorder = null;
    try
    {
        var (eye, target) = CameraFollow.MatchOpeningView(game.Player.ChestPosition, game.Cpu.ChestPosition,
            game.Layout.Size / BoxingGame.RingSize);
        var placeCamera = true;
        var keyboard = new KeyboardControl();
        var intentKeyboard = new IntentKeyboardControl(corners.PlayerWeapon,
            LocalVersusControl.IsKarate(corners.PlayerName, corners.PlayerWear),
            corners.PlayerStyle);
        var local = new LocalVersusControl(corners);
        var followCamera = new FollowCameraRig(camera, BoxingGame.RingSurfaceHeight + 0.563f);
        if (!args.Contains("--no-record"))
        {
            recorder = ReplayRecorder.TryStartFile(replayDirectory, game, "desktop", out var message);
            Console.WriteLine(message);
        }

        IGame<BoxingInput> recorded = recorder is null ? game : new RecordingGame(game, recorder);
        var soundPlayer = HitSoundPlayer.CreateDefault(breakStyle, hitSounds);
        if (crowd) { soundPlayer.PrepareCrowd(seed: 7); }
        var sound = new HitSoundGame(recorded, () => game.RecentHits, soundPlayer,
            () => game.RecentShatters, () => game.RecentLandings, () => game.RecentSwings, () => game.RecentBell,
            () => game.RecentGrazes, () => game.RecentSevers, null,
            () => game.RecentContacts, () => [.. game.WeaponHits, .. game.SelfWeaponHits])
            { DeferredPlayback = true, CaptureCrowd = crowd ? CrowdFor(game) : null };
        var presentation = new SnapshotMatchGame(game, sound, () => speed.Value);
        var previousStep = -1L;
        return new MatchSession(presentation, (input, _) =>
        {
            if (placeCamera) { camera.SetView(game.Display.Point(eye), game.Display.Point(target)); placeCamera = false; } // 物理の空間 → 表示の空間
            ReadSpeed(input);
            var scene = presentation.Latest;
            var dt = Math.Min(ReplayFile.MaxDt, Math.Max(0, scene.Step - previousStep) / 240f);
            previousStep = scene.Step;
            if (mode == MatchMode.Spectate)
            {
                return new BoxingInput(default, input.WasKeyPressed(Key.R));
            }
            if (mode == MatchMode.Versus)
            {
                return local.Read(input, scene.Player, scene.Cpu, scene.Round, dt, camera.YawRadians);
            }
            if (classic && corners.PlayerWeapon is null && !LocalVersusControl.IsKarate(corners.PlayerName, corners.PlayerWear))
            {
                return keyboard.Read(input, camera.YawRadians, scene.Player, dt);
            }
            followCamera.Update(input, scene.Player);
            return intentKeyboard.Read(input, scene.Player, dt, scene.Round, camera.YawRadians);
        }, new SessionResources(game, recorded == game ? null : recorded, sound), presentation,
            Outcome: () => MiniRing.Arena.Campaign.BoutOutcome.Of(game.MatchOver, game.PlayerWins, game.CpuWins, game.Decision),
            Settled: () => MatchFinish.Settled(game),
            Summarize: campaign => MatchFinish.Summary(game, campaign));
    }
    catch
    {
        try { recorder?.Dispose(); }
        finally { game.Dispose(); }
        throw;
    }
}
static string ReplayHelpText() => "Replay: H/F1 shows every shortcut. Space play/pause, J/L rewind/forward, arrows seek/speed, Tab orbit/FPS camera, Escape quit.";

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// テレビ中継のモードの会場: ざわめきを流し続け、試合を見て反応（歓声・どよめき・カウントの掛け声）を鳴らす。
static Func<float, IReadOnlyList<CrowdCue>> CrowdFor(BoxingGame game)
{
    var broadcast = new Broadcast(game.RedLabel, game.BlueLabel);
    var clock = 0f;
    var step = 0;
    var played = 0;
    return dt =>
    {
        clock += dt;
        broadcast.Observe(game, clock, step++);
        var cues = new List<CrowdCue> { new(clock, null, 0f, 0) };
        // 反応は時刻が来たものから鳴らす（どよめきの後の悲鳴のように、少し後に置かれたものがある）。
        for (; played < broadcast.Cues.Count && broadcast.Cues[played].At <= clock; played++)
        {
            cues.Add(broadcast.Cues[played]);
        }
        return cues;
    };
}
