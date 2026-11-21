using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 目で見る用（肌の色をそろえる、2026-11-21）: 名簿の全員の「頭と首のアップ」と「全身」を、試合の描画で並べる。
/// .visualcheck/skin_tone_{SKIN_TAG}.png（1 人 1 行: アップ・斜め・全身）。環境変数 SKIN_TAG（既定 now）でファイル名を変えて前後を比べる。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class SkinToneVisualTests
{
    [TestMethod]
    public void Roster_HeadNeckAndFullBody()
    {
        const int Size = 300;
        const int Columns = 3;
        var tag = Environment.GetEnvironmentVariable("SKIN_TAG") ?? "now";
        var all = CharacterRoster.All;
        var sheet = new byte[Size * Columns * Size * all.Count * 4];
        using var renderer = new HeadlessRenderer(Size, Size);
        for (var row = 0; row < all.Count; row++)
        {
            var c = all[row];
            using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard())
                with { CpuLook = c.Look, CpuFaceSpec = c.Face, CpuBody = c.Build, CpuFace = c.Look.Parts, CpuWear = c.Wear, CpuWeapon = c.Weapon });
            for (var i = 0; i < 50; i++)
            {
                game.Step(default, 1f / 60f);
            }

            var voxels = game.Cpu.Voxels!;
            var headToWorld = voxels.PartOf(game.Cpu.Parts.Head)!.Body.GridToWorld();
            Vector3 World(Vector3 local) => Vector3.Transform(local, headToWorld);
            var up = Vector3.Normalize(World(Vector3.UnitY) - World(Vector3.Zero));
            var draws = voxels.Layers.Select(p => p.Draw(0f, new Voxels.EyePose(0f, World(new Vector3(0f, 0f, 5.634f))))).ToList();
            foreach (var weapon in game.Weapons)
            {
                weapon.AddDraws(draws, tag == "before" ? null : game.WielderOf(weapon)?.Voxels?.SkinColor); // before: 手を既定の色で（修正前の描き方）
            }

            game.Display.Voxels(draws);
            var lighting = game.BuildLighting();
            var chest = game.Cpu.ChestPosition;
            var frame = game.Cpu.HeadOrientation;
            var views = new (Vector3 Eye, Vector3 Target, float Fov)[]
            {
                (World(new Vector3(0.1f, 0.05f, 1f) * 0.8451f), World(new Vector3(0f, -0.03f, 0f)), 24f),
                (World(Vector3.Normalize(new Vector3(0.8f, 0.1f, 0.7f)) * 0.8451f), World(new Vector3(0f, -0.03f, 0f)), 24f),
                (chest + Vector3.Transform(new Vector3(0.6f, 0.15f, 1f), frame) * 3.2f, chest - (Vector3.UnitY * 0.25f), 26f),
            };
            for (var v = 0; v < views.Length; v++)
            {
                var (vp, displayEye) = game.Display.Camera(views[v].Eye, views[v].Target, v == 2 ? Vector3.UnitY : up, views[v].Fov * MathF.PI / 180f, 1f, 0.01f, 40f);
                var shot = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [], lighting: lighting, cameraPosition: displayEye, voxels: draws);
                for (var y = 0; y < Size; y++)
                {
                    Buffer.BlockCopy(shot, y * Size * 4, sheet, ((((row * Size) + y) * Size * Columns) + (v * Size)) * 4, Size * 4);
                }
            }
        }

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root);
        var dir = Path.Combine(root.FullName, ".visualcheck");
        Directory.CreateDirectory(dir);
        PngWriter.WriteRgba(Path.Combine(dir, $"skin_tone_{tag}.png"), Size * Columns, Size * all.Count, sheet);
    }
}
