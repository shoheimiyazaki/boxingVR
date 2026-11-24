using Aigis.Render;
using MiniRing;
using MiniRing.Audio;
using MiniRing.Replay;
using MiniRing.Vr;

// Mini Ring Boxing（VR 版）: OpenXR で HMD に描く。既定は SteamVR を使わず Quest に直接つなぐ
// （Meta Quest Link → Virtual Desktop の順にランタイムを探して、このプロセスだけ固定する）。
// ウィンドウは無い（状態はこのコンソールに出る）。Ctrl+C で終了。
//   --runtime <auto|meta|vd|steamvr|system|path.json>  使う OpenXR ランタイム（既定 auto）
//   --compact-punch    分割胴のコンパクトな回旋を使う試合設定
//   --hybrid-punch           関節駆動のパンチと従来の立位・打撃処理を組み合わせた試合設定
//   --adaptive-punch        拳の目標距離から回旋と踏み込みを連続に配分する試合設定
//   --dynamic-punch         脚の踏み込みを拳に乗せる分割胴の試合設定
//   --scale <0.3..1.0>  目の解像度倍率（既定 0.8。重いときは下げる）
//   --break-sound <glass|crumble|crunch|circuit|metal>  ボクセルが砕ける音の方式（既定 glass）
//   --hit-sound <on|off>  パンチが当たる音（既定 on。ダメージを知らせる音）
//   --selftest          HMD なしで VR 描画経路（外部デバイス・GPU コピー）を検証して終了
//   --two-hand <right|both>  両手持ちの武器の左手（既定 right = ① 右のコントローラが両手を動かす。左はグリップを押しているだけ）
//   --first-person      ② 一人称: カメラは HMD の姿勢のまま（VR の空間 → ワールドの変換を身長の合わせで固定）、体の頭を HMD の位置へ。自分の頭は描かない
//   --first-person-selftest  HMD なしで一人称の目の描画を 1 枚の PNG にして終了（.visualcheck/vr_first_person.png）
//   --profile-headless [--profile-katana]  HMD なしで両目を12秒計測（各640x360・VSyncなし）
//   --weapon <staff|katana|longsword|ironsword|woodenclub>  自分が武器を持つ（グリップボタンで持つ: 離すと放す。柄の近くでグリップを握ると拾う・もう一方の手を添える）
//   --cpu-weapon <kind|none>  相手の武器（既定は自分と同じ。none で素手）  --cpu <style>  相手のスタイル（既定 champion）
//   --record-motion      起動から終了まで VR の動きを motions/<日時>_<武器>.vrmotion に記録（B/Y ボタンでも開始・停止。docs/VR_MOTION.md）
//   --training <katana|longsword|jo|woodenclub|ironsword|...>  お手本づくりの稽古: 正眼に構えて動かない相手へ切り込む動きを型ごとに常に記録（右スティック 左右 = 型、A+B+X+Y 同時 = 作り直す、X・Y = 蹴り、A 単独 = 身長の合わせ。docs/VR_MOTION.md §5）  --shield  左手に盾（片手剣）

if (args.Contains("--selftest"))
{
    return SelfTest.Run(Path.Combine(Environment.CurrentDirectory, ".visualcheck"));
}

if (args.Contains("--first-person-selftest"))
{
    return SelfTest.FirstPerson(Path.Combine(Environment.CurrentDirectory, ".visualcheck"));
}

if (args.Contains("--profile-headless"))
{
    return SelfTest.Profile(args.Contains("--profile-katana"), Path.Combine(Environment.CurrentDirectory, ".visualcheck"));
}

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

var renderScale = 0.8f;
var runtimeRequest = "auto";
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--scale" && float.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var s))
    {
        renderScale = Math.Clamp(s, 0.3f, 1f);
    }
    else if (args[i] == "--runtime")
    {
        runtimeRequest = args[i + 1];
    }
}

if (!VrMatchOptions.TryParse(args, out var match, out var matchError))
{
    Console.Error.WriteLine(matchError);
    return 1;
}

// OpenXR ローダーは xrCreateInstance の時点で XR_RUNTIME_JSON を読むので、XrSession を作る前に固定する。
var runtime = XrRuntimeSelector.Choose(runtimeRequest, XrRuntimeSelector.Discover(),
    Environment.GetEnvironmentVariable(XrRuntimeSelector.RuntimeEnvVar));
if (runtime.Error is not null)
{
    Console.Error.WriteLine(runtime.Error);
    Console.Error.WriteLine("HMD なしで遊ぶ場合は miniring-desktop を使ってください。");
    return 1;
}

XrRuntimeSelector.Pin(runtime);
Console.WriteLine(runtime.Manifest is null ? "[xr] runtime: system default" : $"[xr] runtime: {runtime.Kind} ({runtime.Manifest})");

var quit = false;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    quit = true;
};

XrSession xr;
try
{
    xr = XrSession.Create(renderScale);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"VR を開始できませんでした: {ex.Message}");
    Console.Error.WriteLine(runtime.Kind switch
    {
        XrRuntimeKind.Meta => "Quest で Link（USB ケーブル）または Air Link を開始し、PC の Meta Quest Link アプリでヘッドセットが「接続済み」になってから再実行してください。"
            + " 初回は Meta Quest Link アプリの「設定 > 一般 > OpenXR ランタイム」は変更しなくて構いません（このアプリは自分で Meta のランタイムを指定します）。",
        XrRuntimeKind.VirtualDesktop => "Quest で Virtual Desktop を起動して PC に接続してから再実行してください。",
        _ => "HMD を接続し、選んだ OpenXR ランタイムが動いている状態で再実行してください。",
    });
    Console.Error.WriteLine("HMD なしで遊ぶ場合は miniring-desktop を使ってください。");
    return 1;
}

// 試合は replays/ に自動で記録する（デスクトップ版の --replay で再生、--verify で照合できる）。
var matchTuning = args.Contains("--hybrid-punch")
    ? FighterTuning.HybridPunch
    : args.Contains("--adaptive-punch")
    ? FighterTuning.AdaptivePunch
    : args.Contains("--dynamic-punch") ? FighterTuning.DynamicPunch
    : args.Contains("--compact-punch") ? FighterTuning.CompactPunch : FighterTuning.Default;
var vrTuning = matchTuning with { PlayerFatigue = false }; // VR の手触りの調整が終わるまで、自分の腕は疲れない
var recordingMessage = "training: replays are not recorded";
using (xr)
using (var training = match.Training is { } trainingWeapon ? new VrTrainingSession(trainingWeapon, vrTuning, Path.Combine(Environment.CurrentDirectory, "motions"), match.FirstPerson, match.Wear) : null)
using (var matchGame = training is null ? new BoxingGame(vrTuning, corners: match.Corners.StartingAtCorners()) { HidePlayerHead = match.FirstPerson, PlayerBodyOpacity = match.FirstPerson ? BoxingRenderer.FirstPersonBodyOpacity : 1f } : null)
using (var recorder = matchGame is null ? null : ReplayRecorder.TryStartFile(Path.Combine(Environment.CurrentDirectory, "replays"), matchGame, "vr", out recordingMessage))
using (var renderer = new HeadlessRenderer(xr.EyeWidth, xr.EyeHeight, xr.Vulkan.External))
using (var sounds = HitSoundPlayer.CreateDefault(breakStyle, hitSounds))
{
    Console.WriteLine($"[vr] eye {xr.EyeWidth}x{xr.EyeHeight}, sRGB={xr.IsSrgb}. Put the headset on.");
    Console.WriteLine($"[vr] {recordingMessage}");
    BoxingGame Game() => training?.Game ?? matchGame!;
    xr.FaceButtonMode = training is not null; // 稽古: A 単独 = 合わせ（離したとき）、4 つ同時 = 作り直し、X・Y = 蹴り。B/Y の記録は使わない（常に記録）
    var control = new VrPlayerControl(match.PlayerWeapon, match.TwoHand, match.FirstPerson, faceButtons: training is not null);
    using var motion = new VrMotionSession(match.PlayerWeapon, Path.Combine(Environment.CurrentDirectory, "motions"), training is null && args.Contains("--record-motion"));
    if (match.PlayerWeapon is { } held)
    {
        Console.WriteLine($"[vr] weapon: {held} (grip button = hold: letting go drops it; a free hand with the grip held near the hilt picks it up or adds the second hand)");
        Console.WriteLine(match.TwoHand == MiniRing.Motion.VrTwoHand.Right
            ? "[vr] two-hand right: with both grips held the right controller moves both hands (left hand = the hilt point behind the right hand); release the left grip to use the left controller"
            : "[vr] two-hand both: each controller moves its own hand");
        if (match.FirstPerson)
        {
            Console.WriteLine("[vr] first person: the camera stays at your head in VR space; the body's head is driven to it. Press A/X once to align (scale + position + facing). Left stick moves the VR origin.");
        }
    }
    if (training is null) { Console.WriteLine("[vr] B/Y button: start/stop recording the motion (motions/*.vrmotion)"); }
    var clear = ArenaLook.Background;
    // VR の仕上げ: 画面の隅の陰りは付けない（頭を動かしても付いてくる黒い縁は不自然）。
    var finish = ArenaLook.Finish(vignette: 0f);
    var capture = new VrSceneCapture();
    var voxelConsumer = new Aigis.Render.Voxels.VoxelSnapshotConsumer();
    var scene = capture.Capture(Game());
    var renderVoxels = voxelConsumer.Resolve(scene.VoxelSnapshot!);
    using var audio = new VrAudioPlayback(sounds);
    using var simulation = new VrSimulation(scene, (frame, dt) =>
    {
        var game = Game();
        var input = control.Read(frame, game.PlayerSense, dt);
        if (training is not null)
        {
            training.Step(frame, input, control.Mapper, dt);
            game = training.Game;
            audio.Submit(VrAudioFrame.Capture(game, dt, hitSounds));
            var trained = capture.Capture(game) with { Placement = control.Mapper.Placement };
            var signals = training.DrainHaptics();
            return signals.Length == 0 ? trained : trained with { Haptics = [.. trained.Haptics, .. signals] };
        }

        motion.Step(frame, input.Player, control.Mapper, game.PlayerSense.ArmReach, dt);
        if (recorder is null)
        {
            game.Step(input, dt);
        }
        else
        {
            recorder.Step(game, input, dt);
        }
        audio.Submit(VrAudioFrame.Capture(game, dt, hitSounds));
        return capture.Capture(game) with { Placement = control.Mapper.Placement };
    });
    var frameTiming = args.Contains("--profile-frames") ? new VrFrameTiming() : null;
    var lastStatus = "";
    var idleHintAt = DateTime.UtcNow.AddSeconds(8);
    var idleHintShown = false;

    // Ctrl+C ではセッションを壊さず、ランタイムに終了を頼んで STOPPING → EXITING まで回す（最大 3 秒）。
    var quitDeadline = DateTime.MaxValue;
    while (!xr.ExitRequested && DateTime.UtcNow < quitDeadline && !(quit && !xr.Running))
    {
        if (quit && quitDeadline == DateTime.MaxValue)
        {
            xr.RequestExit();
            quitDeadline = DateTime.UtcNow.AddSeconds(3);
        }

        var rendered = false;
        var timingStart = 0L;
        xr.RunFrame(
            update: (frame, dt) =>
            {
                timingStart = System.Diagnostics.Stopwatch.GetTimestamp();
                audio.ThrowIfFaulted();
                simulation.Submit(frame, dt);
                var latest = simulation.Latest;
                if (!ReferenceEquals(scene, latest))
                {
                    // 一人称では歩ごとに変換だけが変わった別の VrScene になる。ボクセルの解決は元の写しが変わったときだけ。
                    var newVoxels = !ReferenceEquals(scene.VoxelSnapshot, latest.VoxelSnapshot);
                    scene = latest;
                    if (newVoxels)
                    {
                        renderVoxels = voxelConsumer.Resolve(scene.VoxelSnapshot!);
                    }
                }
                foreach (var haptic in simulation.DrainHaptics())
                {
                    xr.Haptic(haptic.Hand, haptic.Amplitude, haptic.Duration);
                }
            },
            renderEye: eye =>
            {
                rendered = true;
                // 一人称: 目の位置・間隔・向きに「VR の空間 → ワールド」の変換（縮尺つき）を掛ける。近い面も縮尺どおりに縮める。
                var placement = scene.Placement?.Displayed(DisplaySpace.Standard); // VR の空間 → 物理の空間 → 表示の空間（描画は表示の空間）
                var view = placement?.View(eye.View) ?? eye.View;
                var position = placement?.Point(eye.Position) ?? eye.Position;
                var near = 0.02f * MathF.Min(1f, placement?.Scale ?? 1f);
                var viewProjection = Camera.OffCenterViewProjection(view, eye.Fov, near, 50f);
                renderer.RenderPrimitiveSceneToImage(eye.TargetImage, clear, viewProjection, scene.Instances, scene.Lines, scene.Lighting, scene.Shadow,
                    position, renderVoxels, scene.Particles, finish);
            });

        if (rendered)
        {
            frameTiming?.Record(timingStart, xr.DisplayHz, scene, simulation.MaximumStepMilliseconds);
        }

        // ランタイムがセッションを READY にしない（ヘッドセットに表示されない）まま待ち続けるときの手がかり。
        if (!xr.Running && !idleHintShown && DateTime.UtcNow > idleHintAt)
        {
            idleHintShown = true;
            Console.WriteLine("[vr] Still waiting for the headset (session Idle). Check:");
            Console.WriteLine("[vr]   1. The Quest is in Link (cable or Air Link) and shows the Link home, not the standalone Quest home.");
            Console.WriteLine("[vr]   2. Meta Horizon Link app on the PC > Settings > General > \"Unknown sources\" is ON (required for apps not from the Meta store).");
            Console.WriteLine("[vr]   3. The headset is on your head (the proximity sensor wakes it). Ctrl+C to quit.");
        }

        var status = $"{xr.StateName} {xr.DisplayHz:0}Hz | {scene.Status}";
        if (status != lastStatus)
        {
            Console.WriteLine(status);
            lastStatus = status;
        }
    }
}

return 0;
