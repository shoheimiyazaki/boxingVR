using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 目で見る用（2026-11-18、指示書 D）: 顔のアップで、黒目が左・右・上・下を向いた顔と、まばたきの途中・閉じた顔を並べる。
/// .visualcheck/eyes_gaze_blink.png に書く（左上から 左・右・上 / 下・半開き・閉じた）。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class EyeLookVisualTests
{
    [TestMethod]
    public void FaceCloseUps_GazeAndBlink()
    {
        const int Size = 420;
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
        var eyeLine = new Vector3(0f, 0.0075f, 0f);
        var eye = World(eyeLine + new Vector3(0f, 0f, 0.14f));
        var aim = World(eyeLine + new Vector3(0f, 0f, 0.03f));
        var up = Vector3.Normalize(World(new Vector3(0f, 1f, 0f)) - World(Vector3.Zero));
        var vp = Camera.ViewProjection(eye, aim, up, 30f * MathF.PI / 180f, 1f, 0.01f, 30f);

        byte[] Shot(Vector3 localGaze, float closure)
        {
            var pose = new EyePose(closure, World(localGaze));
            var voxels = cpu.Voxels!.Parts.Select(p => p.Kind == BodyPartKind.Head ? p.Draw(0f, pose) : p.Draw(0f)).ToList();
            return renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [], lighting: lighting, cameraPosition: eye, voxels: voxels);
        }

        var shots = new[]
        {
            Shot(new Vector3(-1f, 0.0075f, 1f), 0f),
            Shot(new Vector3(1f, 0.0075f, 1f), 0f),
            Shot(new Vector3(0f, 1f, 1f), 0f),
            Shot(new Vector3(0f, -1f, 1f), 0f),
            Shot(new Vector3(0f, 0.0075f, 2f), 0.5f),
            Shot(new Vector3(0f, 0.0075f, 2f), 1f),
        };
        var sheet = new byte[Size * 3 * Size * 2 * 4];
        for (var s = 0; s < shots.Length; s++)
        {
            var ox = s % 3 * Size;
            var oy = s / 3 * Size;
            for (var y = 0; y < Size; y++)
            {
                Buffer.BlockCopy(shots[s], y * Size * 4, sheet, (((oy + y) * Size * 3) + ox) * 4, Size * 4);
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
        PngWriter.WriteRgba(Path.Combine(dir, "eyes_gaze_blink.png"), Size * 3, Size * 2, sheet);
    }
}
