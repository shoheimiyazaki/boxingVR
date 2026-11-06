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
