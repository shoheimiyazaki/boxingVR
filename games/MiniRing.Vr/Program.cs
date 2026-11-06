using System.Numerics;
using Aigis.Render;
using MiniRing;
using MiniRing.Vr;

// Mini Ring Boxing（VR 版）: OpenXR ランタイム（SteamVR / Quest Link / PICO Connect 等）経由で HMD に描く。
// ウィンドウは無い（状態はこのコンソールに出る）。Ctrl+C で終了。
//   --scale <0.3..1.0>  目の解像度倍率（既定 0.8。重いときは下げる）
//   --selftest          HMD なしで VR 描画経路（外部デバイス・GPU コピー）を検証して終了

if (args.Contains("--selftest"))
{
    return SelfTest.Run(Path.Combine(Environment.CurrentDirectory, ".visualcheck"));
}

var renderScale = 0.8f;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--scale" && float.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var s))
    {
        renderScale = Math.Clamp(s, 0.3f, 1f);
    }
}

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
    Console.Error.WriteLine("HMD を接続し、OpenXR ランタイム（SteamVR / Meta Quest Link / PICO Connect）を有効にしてから再実行してください。");
    Console.Error.WriteLine("HMD なしで遊ぶ場合は miniring-desktop を使ってください。");
    return 1;
}

using (xr)
using (var game = new BoxingGame())
using (var renderer = new HeadlessRenderer(xr.EyeWidth, xr.EyeHeight, xr.Vulkan.External))
{
    Console.WriteLine($"[vr] eye {xr.EyeWidth}x{xr.EyeHeight}, sRGB={xr.IsSrgb}. Put the headset on.");
    var control = new VrPlayerControl();
    var clear = new Vector4(0.10f, 0.11f, 0.14f, 1f);
    IReadOnlyList<PrimitiveInstance> instances = [];
    IReadOnlyList<DebugLine> lines = [];
    var lastStatus = "";

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
                game.Step(control.Read(frame, game.PlayerSense, dt), dt);
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
            },
            renderEye: eye =>
            {
                var viewProjection = Camera.OffCenterViewProjection(eye.View, eye.Fov, 0.02f, 50f);
                renderer.RenderPrimitiveSceneToImage(eye.TargetImage, clear, viewProjection, instances, lines, cameraPosition: eye.Position);
            });

        var status = $"{xr.StateName} {xr.DisplayHz:0}Hz | {game.StatusLine} {game.Message}";
        if (status != lastStatus)
        {
            Console.WriteLine(status);
            lastStatus = status;
        }
    }
}

return 0;
