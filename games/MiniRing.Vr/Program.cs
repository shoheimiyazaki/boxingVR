using Aigis.Render;
using MiniRing;
using MiniRing.Audio;
using MiniRing.Replay;
using MiniRing.Vr;

// Mini Ring Boxing（VR 版）: OpenXR で HMD に描く。既定は SteamVR を使わず Quest に直接つなぐ
// （Meta Quest Link → Virtual Desktop の順にランタイムを探して、このプロセスだけ固定する）。
// ウィンドウは無い（状態はこのコンソールに出る）。Ctrl+C で終了。
//   --runtime <auto|meta|vd|steamvr|system|path.json>  使う OpenXR ランタイム（既定 auto）
//   --scale <0.3..1.0>  目の解像度倍率（既定 0.8。重いときは下げる）
//   --break-sound <glass|crumble|crunch|circuit|metal>  ボクセルが砕ける音の方式（既定 glass）
//   --hit-sound <on|off>  パンチが当たる音（既定 off。風切り音とボクセルの音だけ）
//   --selftest          HMD なしで VR 描画経路（外部デバイス・GPU コピー）を検証して終了

if (args.Contains("--selftest"))
{
    return SelfTest.Run(Path.Combine(Environment.CurrentDirectory, ".visualcheck"));
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
using (xr)
using (var game = new BoxingGame(FighterTuning.Default with { PlayerFatigue = false })) // VR の手触りの調整が終わるまで、自分の腕は疲れない
using (var recorder = ReplayRecorder.TryStartFile(Path.Combine(Environment.CurrentDirectory, "replays"), game, "vr", out var recordingMessage))
using (var renderer = new HeadlessRenderer(xr.EyeWidth, xr.EyeHeight, xr.Vulkan.External))
using (var sounds = HitSoundPlayer.CreateDefault(breakStyle, hitSounds))
{
    Console.WriteLine($"[vr] eye {xr.EyeWidth}x{xr.EyeHeight}, sRGB={xr.IsSrgb}. Put the headset on.");
    Console.WriteLine($"[vr] {recordingMessage}");
    var control = new VrPlayerControl();
    var clear = ArenaLook.Background;
    // VR の仕上げ: 画面の隅の陰りは付けない（頭を動かしても付いてくる黒い縁は不自然）。
    var finish = ArenaLook.Finish(vignette: 0f);
    IReadOnlyList<PrimitiveInstance> instances = [];
    IReadOnlyList<DebugLine> lines = [];
    IReadOnlyList<Aigis.Render.Voxels.VoxelVolumeDraw> voxels = [];
    IReadOnlyList<ParticleInstance> particles = [];
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

        xr.RunFrame(
            update: (frame, dt) =>
            {
                var input = control.Read(frame, game.PlayerSense, dt);
                if (recorder is null)
                {
                    game.Step(input, dt);
                }
                else
                {
                    recorder.Step(game, input, dt);
                }

                sounds.PlayAll(game.RecentHits);
                sounds.PlayShatters(game.RecentShatters);
                sounds.PlayLandings(game.RecentLandings);
                sounds.PlaySwings(game.RecentSwings);
                sounds.PlayBell(game.RecentBell);
                sounds.PlayGrazes(game.RecentGrazes);
                sounds.PlaySevers(game.RecentSevers);
                sounds.PlayContacts(game.RecentContacts);

                foreach (var hit in game.RecentHits)
                {
                    if (hit.Attacker == game.Player)
                    {
                        xr.Haptic(hit.Hand, 0.3f + (hit.Damage / 25f), 0.06f + (hit.Damage / 300f));
                    }
                    else if (hit.Victim == game.Player)
                    {
                        xr.Haptic(Hand.Left, 0.5f, 0.1f);
                        xr.Haptic(Hand.Right, 0.5f, 0.1f);
                    }
                }

                (instances, lines) = game.BuildRenderables();
                voxels = game.BuildVoxelVolumes();
                particles = game.BuildParticles();
            },
            renderEye: eye =>
            {
                var viewProjection = Camera.OffCenterViewProjection(eye.View, eye.Fov, 0.02f, 50f);
                renderer.RenderPrimitiveSceneToImage(eye.TargetImage, clear, viewProjection, instances, lines, game.BuildLighting(), game.BuildShadow(),
                    eye.Position, voxels, particles, finish);
            });

        // ランタイムがセッションを READY にしない（ヘッドセットに表示されない）まま待ち続けるときの手がかり。
        if (!xr.Running && !idleHintShown && DateTime.UtcNow > idleHintAt)
        {
            idleHintShown = true;
            Console.WriteLine("[vr] Still waiting for the headset (session Idle). Check:");
            Console.WriteLine("[vr]   1. The Quest is in Link (cable or Air Link) and shows the Link home, not the standalone Quest home.");
            Console.WriteLine("[vr]   2. Meta Horizon Link app on the PC > Settings > General > \"Unknown sources\" is ON (required for apps not from the Meta store).");
            Console.WriteLine("[vr]   3. The headset is on your head (the proximity sensor wakes it). Ctrl+C to quit.");
        }

        var status = $"{xr.StateName} {xr.DisplayHz:0}Hz | {game.StatusLine} {game.Message}";
        if (status != lastStatus)
        {
            Console.WriteLine(status);
            lastStatus = status;
        }
    }
}

return 0;
