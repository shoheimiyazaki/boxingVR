using System.Numerics;
using Aigis.Render;
using Aigis.Foundation;

namespace MiniRing.Tests;

/// <summary>
/// 目で見る用（2026-11-18、ユーザーの指定: あざの色を息切れの青い顔と見分けられる暖色に）。左は打撲のあざ（古い・新しい・とても強い）、
/// 右は息切れで青くなった顔。.visualcheck/bruise_vs_cyanosis.png に書く。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class BruiseColorVisualProbe
{
    [TestMethod]
    public void BruisesAreWarm_TheWindedFaceIsBlue()
    {
        const int Width = 960, Height = 540;
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));
        for (var i = 0; i < 60; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var cpu = game.Cpu;
        var head = game.World.GetState(cpu.Parts.Head).Position;
        var chest = cpu.ChestPosition;
        var toPlayer = Vector3.Normalize((game.Player.ChestPosition - chest) with { Y = 0f });
        // 古いあざ（10 秒前）: 胸と頬。
        cpu.Voxels!.BruiseSkin(chest + (toPlayer * 0.04f) + new Vector3(0f, 0.01f, 0f), 6f);
        cpu.Voxels.BruiseSkin(head + (toPlayer * 0.025f) + new Vector3(0.012f, -0.004f, 0f), 3f);
        for (var i = 0; i < 600; i++)
        {
            game.Step(default, 1f / 60f);
        }

        // 新しいあざと、とても強いあざ。
        head = game.World.GetState(cpu.Parts.Head).Position;
        chest = cpu.ChestPosition;
        cpu.Voxels.BruiseSkin(chest + (toPlayer * 0.04f) + new Vector3(0.02f, -0.025f, 0f), 3f);
        cpu.Voxels.BruiseSkin(head + (toPlayer * 0.025f) + new Vector3(-0.012f, 0.008f, 0f), 12f);
        game.Step(default, 1f / 60f);

        using var renderer = new HeadlessRenderer(Width, Height);
        var lighting = game.BuildLighting();
        byte[] Shot(Fighter f, float cyanosis, Vector3 target)
        {
            var away = Vector3.Normalize(((f == cpu ? game.Player : cpu).ChestPosition - f.ChestPosition) with { Y = 0f });
            var eye = target + (away * 0.3f) + new Vector3(0f, 0.02f, 0f);
            var vp = Camera.ViewProjection(eye, target, Vector3.UnitY, 35f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
            var voxels = f.Voxels!.Parts.Select(p => p.Draw(cyanosis)).ToList();
            return renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [], lighting: lighting, cameraPosition: eye, voxels: voxels);
        }

        var left = Shot(cpu, 0f, (cpu.ChestPosition + game.World.GetState(cpu.Parts.Head).Position) * 0.5f);
        var right = Shot(game.Player, 1f, (game.Player.ChestPosition + game.World.GetState(game.Player.Parts.Head).Position) * 0.5f);
        var both = new byte[Width * 2 * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            Buffer.BlockCopy(left, y * Width * 4, both, y * Width * 8, Width * 4);
            Buffer.BlockCopy(right, y * Width * 4, both, (y * Width * 8) + (Width * 4), Width * 4);
        }

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root);
        var dir = Path.Combine(root.FullName, ".visualcheck");
        Directory.CreateDirectory(dir);
        PngWriter.WriteRgba(Path.Combine(dir, "bruise_vs_cyanosis.png"), Width * 2, Height, both);
    }
}
