using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Foundation;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using Aigis.Render;

namespace MiniRing.Tests;

/// <summary>
/// 診断用（VFC の骨折の治り方、2026-11-18 ユーザーの指定「曲がったままつながるように。先に肉を直して、バネが硬くなってやや真っすぐなってから最後に骨が
/// なおって骨折による関節は固定」）: 右前腕と左上腕を打撃の曲げで折り、肉の橋を 1/8 だけ残してぶら下げる。VFC の決まり（短いラウンド・休み 25 秒）・檻で回し、
/// 肉が戻る → バネが固くなってやや真っすぐ → 骨がつながって固定、を正面から撮る（ゲームの 0.5 秒 = 動画の 1 コマの早回し）。
/// docs/video/vfc_fracture_healing.mp4 と、10 コマごとの PNG を書く。
/// </summary>
[TestClass]
[TestCategory("Probe")]
[TestCategory("visual")]
public sealed class FractureHealingVisualProbe
{
    [TestMethod]
    public void BentArms_HealBent_Closeup()
    {
        const int Width = 960, Height = 540, Fps = 30;
        const float GamePerFrame = 0.5f, Dt = 1f / 120f;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root);
        var output = Path.Combine(root.FullName, "docs", "video", "vfc_fracture_healing.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var rules = MatchRules.Vfc with { RoundSeconds = 3f, Rounds = 6, BreakSeconds = 25f };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard(), Referee: false, Ring: RingLayout.Cage), rules: rules);
        using var renderer = new HeadlessRenderer(Width, Height);
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg", $"-y -loglevel error -f rawvideo -pix_fmt rgba -s {Width}x{Height} -r {Fps} -i - -c:v libx264 -pix_fmt yuv420p -crf 20 -preset fast \"{output}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        })!;
        var f = game.Cpu;
        (int Stub, int Piece, string Name)[] breaks = [];
        var time = 0f;
        var round = 0;
        for (var frame = 0; time < rules.TotalSeconds - 1f; frame++)
        {
            for (var t = 0f; t < GamePerFrame; t += Dt)
            {
                game.Step(default, Dt);
                time += Dt;
            }

            if (breaks.Length == 0 && time > 1f)
            {
                breaks = [Bend(game, f, f.ForearmId(Hand.Right), "右前腕"), Bend(game, f, f.UpperArmId(Hand.Left), "左上腕")];
            }

            round = game.InBreak ? round : game.MatchRound;
            var chest = f.ChestPosition;
            var target = chest - new Vector3(0f, 0.05f, 0f);
            var eye = target + (f.Frame.Forward * 0.36f) + new Vector3(0f, 0.03f, 0f);
            var vp = Camera.ViewProjection(eye, target, Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
            var voxels = f.Voxels!.Parts.Where(p => f.Owns(p.Body.BodyId)).Concat(f.Voxels.Fragments.Where(p => f.HangsByFlesh(p.Body.BodyId) || f.IsKnit(p.Body.BodyId)))
                .Select(p => p.Draw()).ToList();
            var lines = new List<ScreenTextDraw>
            {
                new("VFC の骨折の治り方: 曲げで折り、肉を 1/8 だけ残してぶら下げる（ゲーム 0.5 秒 = 1 コマ）", new Vector2(20f, 28f), 19f, Vector4.One, TextAnchor.Left),
                new(game.InBreak ? $"休み（残り {game.BreakLeft:0} 秒）" : $"ラウンド {game.MatchRound}", new Vector2(20f, 56f), 20f, Vector4.One, TextAnchor.Left),
            };
            var y = 84f;
            foreach (var (stub, piece, name) in breaks)
            {
                var angle = Angle(game, stub, piece);
                var state = f.IsKnit(piece) ? $"骨がつながった（固定）  角度 {angle:0}°"
                    : f.FractureState(piece) is { } s ? $"肉の断面 {s.Fraction:P0}  バネ {s.SpringHz:0.0} Hz  角度 {angle:0}°" : "落ちた";
                lines.Add(new($"{name}: {state}", new Vector2(20f, y), 20f, new Vector4(1f, 0.85f, 0.3f, 1f), TextAnchor.Left));
                y += 26f;
            }

            lines.Add(new($"右腕 {(f.ArmSevered(Hand.Right) ? "駆動しない" : "駆動する")}  左腕 {(f.ArmSevered(Hand.Left) ? "駆動しない" : "駆動する")}", new Vector2(20f, y), 20f, Vector4.One, TextAnchor.Left));
            lines.Add(new(time.ToString("0.0", CultureInfo.InvariantCulture) + " s", new Vector2(20f, Height - 30f), 20f, Vector4.One, TextAnchor.Left));
            var rgba = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [.. lines], lighting: game.BuildLighting(), cameraPosition: eye, voxels: voxels);
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

    /// <summary>部位 <paramref name="part"/> の骨を真ん中で曲げで折り、肉の橋を 1/8 だけ残す。（切り株, 破片, 名前）。</summary>
    private static (int, int, string) Bend(BoxingGame game, Fighter f, int part, string name)
    {
        var voxelPart = f.Voxels!.PartOf(part)!;
        var broken = f.Voxels.BreakBent(part, new VoxelBreak(0, voxelPart.Body.Grid.Dimensions.X / 2, voxelPart.DistalHigh, 2f))!;
        game.BreakBone(f, broken, 1f);
        var bridge = broken.Bridge!;
        var removed = new List<RemovedVoxel>();
        foreach (var cell in bridge.Cells.Skip(Math.Max(1, bridge.Cells.Count / 8)))
        {
            bridge.Stub.Grid.Break(cell.StubIndex, removed);
        }

        bridge.Stub.UpdateMass();
        return (part, broken.Fragment!.Body.BodyId, name);
    }

    private static float Angle(BoxingGame game, int a, int b)
    {
        var x = Vector3.Transform(Vector3.UnitX, game.World.GetState(a).Orientation);
        var y = Vector3.Transform(Vector3.UnitX, game.World.GetState(b).Orientation);
        return MathF.Acos(Math.Clamp(Vector3.Dot(x, y), -1f, 1f)) * 180f / MathF.PI;
    }
}
