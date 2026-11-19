using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 目で見る用（2026-11-18、呼吸と循環のモデル）: 顔の色の合図を並べる。左から ふつう・青白い（心臓が送りきれない、<see cref="Fighter.Pallor"/> 1）・
/// 青紫（血の酸素が尽きた、<see cref="Fighter.Cyanosis"/> 1）・両方。.visualcheck/face_pallor_cyanosis.png に書く。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class FaceColorVisualTests
{
    [TestMethod]
    public void FaceCloseUps_PaleAndCyanotic()
    {
        const int Size = 360;
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));
        for (var i = 0; i < 30; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var cpu = game.Cpu;
        var head = cpu.Voxels!.Parts.Single(p => p.Kind == BodyPartKind.Head);
        var toWorld = head.Body.GridToWorld();
        Vector3 World(Vector3 local) => Vector3.Transform(local, toWorld);
        using var renderer = new HeadlessRenderer(Size, Size);
        var lighting = game.BuildLighting();
        var eye = World(new Vector3(0f, 0.01f, 0.22f));
        var aim = World(new Vector3(0f, 0f, 0f));
        var up = Vector3.Normalize(World(new Vector3(0f, 1f, 0f)) - World(Vector3.Zero));
        var vp = Camera.ViewProjection(eye, aim, up, 30f * MathF.PI / 180f, 1f, 0.01f, 30f);

        byte[] Shot(float cyanosis, float pallor)
        {
            var pose = new EyePose(0f, World(new Vector3(0f, 0f, 2f)));
            var voxels = cpu.Voxels!.Layers.Select(p => p.Kind == BodyPartKind.Head ? p.Draw(cyanosis, pose, pallor) : p.Draw(0f)).ToList();
            return renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [], lighting: lighting, cameraPosition: eye, voxels: voxels);
        }

        var shots = new[] { Shot(0f, 0f), Shot(0f, 1f), Shot(1f, 0f), Shot(1f, 1f) };
        var sheet = new byte[Size * shots.Length * Size * 4];
        for (var s = 0; s < shots.Length; s++)
        {
            for (var y = 0; y < Size; y++)
            {
                Buffer.BlockCopy(shots[s], y * Size * 4, sheet, ((y * Size * shots.Length) + (s * Size)) * 4, Size * 4);
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
        PngWriter.WriteRgba(Path.Combine(dir, "face_pallor_cyanosis.png"), Size * shots.Length, Size, sheet);
    }
}
