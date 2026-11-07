using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;

namespace MiniRing.Tests;

/// <summary>
/// GPU（Vulkan）で試合を数秒進めて PNG に描く。Aigis と同じく visual はローカル専用・CI 外。
/// 出力: リポジトリ直下の <c>.visualcheck/miniring_*.png</c>。PNG があること ≠ 正しい（Aigis anti-pattern）なので、
/// 人／レビュー役が中身を見て判断する。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class SnapshotTests
{
    private const int Width = 960;
    private const int Height = 540;

    [TestMethod]
    public void Match_RendersFromPlayerViewpoint()
    {
        var outDir = Path.Combine(FindRepoRoot(), ".visualcheck");
        Directory.CreateDirectory(outDir);
        using var game = new BoxingGame(seed: 3);
        using var renderer = new HeadlessRenderer(Width, Height);

        // VR で立ったプレイヤーの目線（原点・目の高さ 1.55m）からリングを見下ろす。
        var eye = new Vector3(0f, 1.55f, 0.05f);
        var target = BoxingGame.RingCenter with { Y = BoxingGame.RingSurfaceHeight + 0.15f };
        var viewProjection = Camera.ViewProjection(eye, target, Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.02f, 50f);

        var time = 0f;
        foreach (var at in new[] { 0.1f, 2f, 6f })
        {
            while (time < at)
            {
                var player = game.Player;
                game.Step(new BoxingInput(new FighterCommand(Vector3.Zero, game.Cpu.ChestPosition - player.ChestPosition,
                    new HandCommand(AiControl.Guard(Hand.Left, player.ArmReach), false),
                    new HandCommand(new Vector3(0.05f, 0.05f, MathF.Sin(time * 3f) > 0.6f ? 1f : 0.5f) * player.ArmReach, false))), 1f / 60f);
                time += 1f / 60f;
            }

            var (instances, lines) = game.BuildRenderables();
            var text = game.BuildOverlayText(viewProjection, new Vector2(Width, Height));
            var rgba = renderer.RenderPrimitiveScene(IGameClear, viewProjection, instances, lines, text, cameraPosition: eye);
            var path = Path.Combine(outDir, $"miniring_t{at:0.0}.png");
            PngWriter.WriteRgba(path, Width, Height, rgba);
            Assert.IsTrue(File.Exists(path));
        }
    }

    /// <summary>ローポリの顔・胴・腕と、打撃のへこみ（白 → 赤）を間近で見る。顔と左腕と胸を正面から決め打ちでへこませる。</summary>
    [TestMethod]
    public void CloseUp_ShowsLowPolyBodyAndDents()
    {
        var outDir = Path.Combine(FindRepoRoot(), ".visualcheck");
        Directory.CreateDirectory(outDir);
        using var game = new BoxingGame(seed: 3);
        using var renderer = new HeadlessRenderer(Width, Height);
        for (var i = 0; i < 30; i++)
        {
            game.Step(new BoxingInput(new FighterCommand(Vector3.Zero, Vector3.UnitZ,
                new HandCommand(AiControl.Guard(Hand.Left, game.Player.ArmReach), false),
                new HandCommand(AiControl.Guard(Hand.Right, game.Player.ArmReach), false))), 1f / 60f);
        }

        var cpu = game.Cpu;
        var head = game.World.GetState(cpu.Parts.Head);
        var toCamera = Vector3.Normalize(game.Player.HeadPosition - head.Position) with { Y = 0f };
        var eye = head.Position + (Vector3.Normalize(toCamera) * 0.22f) + new Vector3(0.04f, 0.02f, 0f);
        var viewProjection = Camera.ViewProjection(eye, head.Position - new Vector3(0f, 0.05f, 0f), Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.01f, 20f);

        void Shoot(string name)
        {
            var (instances, lines) = game.BuildRenderables();
            var rgba = renderer.RenderPrimitiveScene(IGameClear, viewProjection, instances, lines, [], cameraPosition: eye);
            PngWriter.WriteRgba(Path.Combine(outDir, $"miniring_closeup_{name}.png"), Width, Height, rgba);
        }

        Shoot("before");
        foreach (var (part, side, damage) in new[] { (cpu.Parts.Head, 0.3f, 18f), (cpu.Parts.Head, -0.5f, 9f), (cpu.Parts.Chest, 0.4f, 20f), (cpu.Parts.Pelvis, -0.3f, 6f) })
        {
            var s = game.World.GetState(part);
            var forward = Vector3.Transform(Vector3.UnitZ, s.Orientation);
            var left = Vector3.Transform(Vector3.UnitX, s.Orientation);
            var half = cpu.HalfExtentsOf(part).Z;
            var fist = s.Position + (forward * (half + FighterTuning.Default.GloveRadius)) + (left * side * half);
            game.CpuSkin.Dent(part, s, fist, -forward, damage);
        }

        Shoot("after");
        Assert.IsTrue(game.CpuSkin.Part(cpu.Parts.Head).IsDented);
    }

    private static readonly Vector4 IGameClear = new(0.10f, 0.11f, 0.14f, 1f);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MiniRing.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
