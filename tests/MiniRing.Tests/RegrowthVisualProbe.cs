using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Foundation;
using Aigis.Foundation.Voxels;
using Aigis.Render;

namespace MiniRing.Tests;

/// <summary>
/// 診断用（VFC の再生、指示書 F）: 右上腕を真ん中で切り落とし、VFC の決まり（短いラウンド・休み 42 秒）の試合を回して、切り株が休みのたびに伸び、
/// 肘まで戻ると前腕が芽から生え直す様子を、切られた側から寄って撮る（ゲームの 0.84 秒 = 動画の 1 コマの早回し）。
/// docs/video/vfc_regrowth_closeup.mp4 と、休みごとの PNG を書く。
/// </summary>
[TestClass]
[TestCategory("Probe")]
[TestCategory("visual")]
public sealed class RegrowthVisualProbe
{
    [TestMethod]
    public void ACutArm_GrowsBackOverTheBreaks_Closeup()
    {
        const int Width = 960, Height = 540, Fps = 30;
        const float GamePerFrame = 0.839f, Dt = 1f / 120f;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root);
        var output = Path.Combine(root.FullName, "docs", "video", "vfc_regrowth_closeup.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var rules = MatchRules.Vfc with { RoundSeconds = 5.035f, Rounds = 5, BreakSeconds = 41.96f };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard(), Referee: false, Ring: RingLayout.Cage), rules: rules);
        using var renderer = new HeadlessRenderer(Width, Height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg", $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {Width}x{Height} -r {Fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 20 -preset fast \"{output}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        })!;
        var f = game.Cpu;
        var upper = f.UpperArmId(Hand.Right);
        var forearm = f.ForearmId(Hand.Right);
        var cut = false;
        var time = 0f;
        var breaks = 0;
        var wasBreak = false;
        for (var frame = 0; time < rules.TotalSeconds - 1.678f; frame++)
        {
            for (var t = 0f; t < GamePerFrame; t += Dt)
            {
                game.Step(default, Dt);
                time += Dt;
            }

            if (!cut && time > 1.678f)
            {
                var part = f.Voxels!.PartOf(upper)!;
                var grid = part.Body.Grid;
                var removed = new List<RemovedVoxel>();
                for (var i = 0; i < grid.CellCount; i++)
                {
                    if (grid.Coordinates(i).X == grid.Dimensions.X / 2 && grid.MaterialAt(i) != 0)
                    {
                        grid.Break(i, removed);
                    }
                }

                game.BreakBone(f, f.Voxels.BreakIfSevered(upper)!, 1f);
                cut = true;
            }

            breaks += game.InBreak && !wasBreak ? 1 : 0;
            wasBreak = game.InBreak;
            // 右肩の外側（体から右腕の向き）から、肩と肘のあたりを見る。
            var shoulder = game.World.GetState(upper).Position;
            var side = shoulder - f.ChestPosition;
            side.Y = 0f;
            side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : Vector3.UnitX;
            var target = shoulder + (side * 0.0845f) - new Vector3(0f, 0.0563f, 0f);
            var eye = target + (side * 0.9014f) + (f.Frame.Forward * 0.338f) + new Vector3(0f, 0.169f, 0f);
            // 目・注視点は物理の空間で決め、表示の空間へ移す（近い面・遠い面は表示の空間の長さ）。
            var (vp, displayEye) = game.Display.Camera(eye, target, Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
            var voxels = f.Voxels!.Parts.Where(p => f.Owns(p.Body.BodyId)).Select(p => game.Display.Voxel(p.Draw())).ToList();
            var upperCells = f.Voxels.PartOf(upper)!.Body.Grid.FilledCount;
            var foreCells = f.Owns(forearm) ? f.Voxels.PartOf(forearm)!.Body.Grid.FilledCount : 0;
            var phase = game.InBreak ? $"休み {breaks}（残り {game.BreakLeft:0} 秒）" : $"ラウンド {game.MatchRound}";
            ScreenTextDraw[] text =
            [
                new("VFC の再生: 右上腕を真ん中で切り落とし、休みのたびに切り株が伸びる（ゲーム 0.84 秒 = 1 コマ）", new Vector2(20f, 30f), 20f, Vector4.One, TextAnchor.Left),
                new($"{phase}   上腕 {upperCells} セル   前腕 {(f.Owns(forearm) ? foreCells + " セル（芽から）" : "なし")}   腕 {(f.ArmSevered(Hand.Right) ? "駆動しない" : "駆動する")}", new Vector2(20f, 62f), 22f, new Vector4(1f, 0.85f, 0.3f, 1f), TextAnchor.Left),
                new(time.ToString("0.0", CultureInfo.InvariantCulture) + " s", new Vector2(20f, Height - 30f), 22f, Vector4.One, TextAnchor.Left),
            ];
            var rgba = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], text, lighting: game.BuildLighting(), cameraPosition: displayEye, voxels: voxels);
            ffmpeg.StandardInput.BaseStream.Write(rgba);
            if (frame % 10 == 0)
            {
                PngWriter.WriteRgba(Path.ChangeExtension(output, $".f{frame:000}.png"), Width, Height, rgba);
            }
        }

        ffmpeg.StandardInput.Close();
        ffmpeg.WaitForExit();
        Assert.AreEqual(0, ffmpeg.ExitCode);
    }
}
