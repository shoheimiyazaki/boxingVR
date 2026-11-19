using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 目で見る用（F2、2026-11-19）: 名簿の 3 人の頭（頭蓋骨の中の 5 mm の格子と、その上に重ねた顔の層の 2.5 mm の格子: FaceLayer）を、試合の描画
/// （ボクセルの描画・照明）で正面・横・上から写す。.visualcheck/face_layer_views.png に書く（1 人 1 行: 正面・横（本人の左）・上・斜め）。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class FaceLayerVisualTests
{
    [TestMethod]
    public void RosterHeads_FrontSideTop()
    {
        const int Size = 360;
        string[] ids = ["gaston", "narong", "yoandry"];
        var views = new (Vector3 Eye, Vector3 Up)[]
        {
            (new Vector3(0f, 0f, 1f), Vector3.UnitY),
            (new Vector3(1f, 0f, 0f), Vector3.UnitY),
            (new Vector3(0f, 1f, 0f), Vector3.UnitZ),
            (Vector3.Normalize(new Vector3(0.7f, 0.25f, 1f)), Vector3.UnitY),
        };
        var sheet = new byte[Size * views.Length * Size * ids.Length * 4];
        using var renderer = new HeadlessRenderer(Size, Size);
        for (var row = 0; row < ids.Length; row++)
        {
            var c = CharacterRoster.Get(ids[row]);
            using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard())
                with { CpuLook = c.Look, CpuFaceSpec = c.Face, CpuBody = c.Build, CpuFace = c.Look.Parts });
            for (var i = 0; i < 30; i++)
            {
                game.Step(default, 1f / 60f);
            }

            var voxels = game.Cpu.Voxels!;
            Assert.IsNotNull(voxels.Face, "the head carries a face layer");
            var head = voxels.PartOf(game.Cpu.Parts.Head)!;
            var toWorld = head.Body.GridToWorld();
            Vector3 World(Vector3 local) => Vector3.Transform(local, toWorld);
            Vector3 Direction(Vector3 local) => Vector3.Normalize(World(local) - World(Vector3.Zero));
            var lighting = game.BuildLighting();
            var draws = voxels.Layers.Select(p => p.Draw(0f, new Voxels.EyePose(0f, null))).ToList();
            for (var v = 0; v < views.Length; v++)
            {
                var eye = World(views[v].Eye * 0.3f);
                var vp = Camera.ViewProjection(eye, World(Vector3.Zero), Direction(views[v].Up), 18f * MathF.PI / 180f, 1f, 0.01f, 30f);
                var shot = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [], lighting: lighting, cameraPosition: eye, voxels: draws);
                for (var y = 0; y < Size; y++)
                {
                    Buffer.BlockCopy(shot, y * Size * 4, sheet, ((((row * Size) + y) * Size * views.Length) + (v * Size)) * 4, Size * 4);
                }
            }
        }

        PngWriter.WriteRgba(Path.Combine(VisualDir(), "face_layer_views.png"), Size * views.Length, Size * ids.Length, sheet);
    }

    /// <summary>名簿の 18 人の顔を正面から（試合の描画）。.visualcheck/face_layer_roster.png（6 列 × 3 行、名簿の順）。</summary>
    [TestMethod]
    public void Roster_Fronts()
    {
        const int Size = 300;
        const int Columns = 6;
        var all = CharacterRoster.All;
        var rows = (all.Count + Columns - 1) / Columns;
        var sheet = new byte[Size * Columns * Size * rows * 4];
        using var renderer = new HeadlessRenderer(Size, Size);
        for (var k = 0; k < all.Count; k++)
        {
            var c = all[k];
            using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard())
                with { CpuLook = c.Look, CpuFaceSpec = c.Face, CpuBody = c.Build, CpuFace = c.Look.Parts });
            for (var i = 0; i < 30; i++)
            {
                game.Step(default, 1f / 60f);
            }

            var voxels = game.Cpu.Voxels!;
            var toWorld = voxels.PartOf(game.Cpu.Parts.Head)!.Body.GridToWorld();
            Vector3 World(Vector3 local) => Vector3.Transform(local, toWorld);
            var eye = World(new Vector3(0f, 0f, 0.3f));
            var up = Vector3.Normalize(World(Vector3.UnitY) - World(Vector3.Zero));
            var vp = Camera.ViewProjection(eye, World(Vector3.Zero), up, 16f * MathF.PI / 180f, 1f, 0.01f, 30f);
            var draws = voxels.Layers.Select(p => p.Draw(0f, new Voxels.EyePose(0f, World(new Vector3(0f, 0f, 2f))))).ToList();
            var shot = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [], lighting: game.BuildLighting(), cameraPosition: eye, voxels: draws);
            var (ox, oy) = (k % Columns * Size, k / Columns * Size);
            for (var y = 0; y < Size; y++)
            {
                Buffer.BlockCopy(shot, y * Size * 4, sheet, ((((oy + y) * Size * Columns) + ox) * 4), Size * 4);
            }
        }

        PngWriter.WriteRgba(Path.Combine(VisualDir(), "face_layer_roster.png"), Size * Columns, Size * rows, sheet);
    }

    private static string VisualDir()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CLAUDE.md")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root);
        var dir = Path.Combine(root.FullName, ".visualcheck");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
