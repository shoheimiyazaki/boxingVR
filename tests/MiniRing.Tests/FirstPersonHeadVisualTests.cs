using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;

namespace MiniRing.Tests;

/// <summary>
/// 目で見る用（2026-11-19、PhysicsVersion 59）: 一人称（VR）では、自分の頭の格子と顔の層の両方を描かない（カメラが頭の中にある）。
/// 自分の目の位置から相手を見た絵を、自分の頭を描く（左）・描かない（右）で並べる。.visualcheck/first_person_head.png。
/// 左は頭の内側（顔の層・頭蓋骨）が視界をふさぎ、右は相手がはっきり見える。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class FirstPersonHeadVisualTests
{
    [TestMethod]
    public void OwnHead_HiddenInFirstPerson()
    {
        const int Size = 480;
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));
        for (var i = 0; i < 90; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var head = game.World.GetState(game.Player.Parts.Head);
        var eye = head.Position + (Vector3.UnitY * 0.02f);
        var target = game.Cpu.ChestPosition;
        var lighting = game.BuildLighting();
        var sheet = new byte[Size * 2 * Size * 4];
        using var renderer = new HeadlessRenderer(Size, Size);
        for (var v = 0; v < 2; v++)
        {
            game.HidePlayerHead = v == 1;
            var vp = Camera.ViewProjection(eye, target, Vector3.UnitY, 80f * MathF.PI / 180f, 1f, 0.01f, 30f);
            var shot = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [], lighting: lighting, cameraPosition: eye, voxels: game.BuildVoxelVolumes());
            for (var y = 0; y < Size; y++)
            {
                Buffer.BlockCopy(shot, y * Size * 4, sheet, ((y * Size * 2) + (v * Size)) * 4, Size * 4);
            }
        }

        PngWriter.WriteRgba(Path.Combine(VisualDir(), "first_person_head.png"), Size * 2, Size, sheet);
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
