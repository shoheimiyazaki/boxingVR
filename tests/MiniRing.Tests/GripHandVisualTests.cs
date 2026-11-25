using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>
/// 武器を握る手（<see cref="Weapons.GripHand"/>）を近くから撮る: 剣と盾・刀（両手）・ロングソード（両手）・棒・棍棒。球ではなく、素手と同じボクセルの
/// 握り拳が柄を握っていることを目で見る（<c>.visualcheck/grip_hand_*.png</c>）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class GripHandVisualTests
{
    private const uint Width = 800;
    private const uint Height = 600;

    [TestMethod]
    [TestCategory("visual")]
    public void TheHandsHoldingWeapons_AreVoxelFists_FromCloseUp()
    {
        var output = Path.Combine(RepoRoot(), ".visualcheck");
        Directory.CreateDirectory(output);
        using var renderer = new HeadlessRenderer(Width, Height);
        foreach (var (red, blue) in new[] { ("edgar", "jin"), ("jin", "edgar"), ("konrad", "edgar"), ("daichi", "edgar"), ("sean", "edgar") })
        {
            var corners = new MatchSelection(MatchMode.Spectate, CharacterRoster.Get(red), CharacterRoster.Get(blue)).Corners with { UseWeaponBots = true };
            using var game = new BoxingGame(seed: 1, corners: corners);
            for (var i = 0; i < 150; i++)
            {
                game.Step(default, 1f / 60f);
            }

            var fighter = game.Player;
            var hand = fighter.GloveCenter(Hand.Right);
            var toward = game.Cpu.ChestPosition - fighter.ChestPosition;
            toward.Y = 0f;
            toward = Vector3.Normalize(toward);
            var outward = hand - fighter.ChestPosition;
            outward -= Vector3.Dot(outward, toward) * toward;
            outward.Y = 0f;
            outward = outward.LengthSquared() > 1e-4f ? Vector3.Normalize(outward) : Vector3.Normalize(Vector3.Cross(toward, Vector3.UnitY));
            foreach (var (name, offset) in new[]
            {
                ("side", (outward * 0.4f) + new Vector3(0f, 0.1f, 0f)),
                ("front", (toward * 0.35f) + (outward * 0.2f) + new Vector3(0f, 0.12f, 0f)),
                ("above", new Vector3(0f, 0.4f, 0f) + (outward * 0.15f)),
            })
            {
                var eye = hand + offset;
                var (vp, displayEye) = game.Display.Camera(eye, hand, name == "above" ? toward : Vector3.UnitY, 50f * MathF.PI / 180f, (float)Width / Height, 0.02f, 30f);
                var (instances, lines) = (new List<PrimitiveInstance>(), new List<DebugLine>());
                BoxingRenderer.Fill(game, showTargetGhosts: false, instances, lines); // 手の目標の玉（デバッグの印）は描かない
                // 赤（撮る選手）の体と、赤が持っている物だけ（相手の体が手の前に入らないように）。
                var voxels = fighter.Voxels!.Layers.Select(p => p.Draw()).ToList();
                foreach (var weapon in game.Weapons.Where(w => game.WielderOf(w) == fighter))
                {
                    weapon.AddDraws(voxels, fighter.Voxels.SkinColor);
                }

                game.Display.Voxels(voxels);
                var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, [],
                    lighting: game.BuildLighting(), cameraPosition: displayEye, voxels: voxels);
                PngWriter.WriteRgba(Path.Combine(output, $"grip_hand_{red}_{name}.png"), (int)Width, (int)Height, rgba);
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MiniRing.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
