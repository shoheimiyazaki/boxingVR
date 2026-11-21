using System.Numerics;
using MiniRing.Characters;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 顔と体の肌の色が同じ出どころ（名簿の顔の肌 = <see cref="FaceSpec.Skin"/>）から来ること（見た目だけ。物理・チェックサムには関わらない）。
/// </summary>
[TestClass]
public sealed class SkinToneTests
{
    private static BoxingGame Game(FighterCharacter c, HandWear wear)
        => new(seed: 1, corners: new BoxingCorners("g", _ => new StandGuard(), _ => new StandGuard())
            with { CpuLook = c.Look, CpuFaceSpec = c.Face, CpuBody = c.Build, CpuFace = c.Look.Parts, CpuWear = wear });

    [TestMethod]
    public void EveryRosterFighter_FaceAndBodyShareOneSkinColour()
    {
        foreach (var c in CharacterRoster.All)
        {
            using var game = Game(c, c.Wear);
            var voxels = game.Cpu.Voxels!;
            var expected = new Vector4(c.Face.Skin ?? c.Look.Skin, 1f);
            Assert.AreEqual(expected, voxels.SkinColor, $"{c.Id}: body skin is the roster face skin");
            Assert.AreEqual(expected, voxels.Face!.Body.Grid.Palette[BoxerAnatomy.Skin].Color, $"{c.Id}: face layer");
            foreach (var part in voxels.Parts)
            {
                Assert.AreEqual(expected, part.Body.Grid.Palette[BoxerAnatomy.Skin].Color, $"{c.Id}: {part.Kind}");
            }
        }
    }

    [TestMethod]
    public void BareFist_IsDrawnInTheOwnersSkinColour_AndOnlyTheHandSkinChanges()
    {
        var c = CharacterRoster.Get("marcus"); // 肌が濃い: 既定の手の色（淡い）との差が大きい
        using var game = Game(c, HandWear.None);
        var fist = game.GloveOf(game.Cpu, Hand.Left)!;
        var skin = game.Cpu.Voxels!.SkinColor;
        var tint = new HandSkinTint(skin);
        var handSkin = BoxerAnatomy.Palette[BoxerAnatomy.HandSkin].Color;
        Assert.AreNotEqual(skin, handSkin, "the default hand palette is not marcus' skin");
        Assert.AreEqual(skin, tint.Recolor(BoxerAnatomy.HandSkin, handSkin, 0, 0, 0));
        var muscle = BoxerAnatomy.Palette[BoxerAnatomy.HandMuscle].Color;
        Assert.AreEqual(muscle, tint.Recolor(BoxerAnatomy.HandMuscle, muscle, 0, 0, 0), "only the skin follows the owner");
        Assert.AreSame(game.Cpu, game.WielderOf(fist));
        Assert.AreNotEqual(new HandSkinTint(skin).Key, new HandSkinTint(new Vector4(0.9f, 0.7f, 0.5f, 1f)).Key);
    }
}
