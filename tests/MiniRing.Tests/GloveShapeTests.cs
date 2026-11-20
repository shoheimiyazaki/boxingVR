using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// ボクシンググローブ（PhysicsVersion 58）の形と質量: 大きさと質量は前の球（幅 48 mm・48 g）に近く、前の詰め物は甲より厚い。
/// </summary>
[TestClass]
public sealed class GloveShapeTests
{
    /// <summary>点 <paramref name="from"/> から <paramref name="step"/> ずつ進み、骨の芯か空にぶつかるまでの、革と詰め物のセルの数。</summary>
    private static int CoatRun(VoxelGrid grid, Vector3 from, Vector3 step)
    {
        var n = 0;
        for (var p = from; grid.CellAt(p) is var i && i >= 0 && grid.MaterialAt(i) is not 0 and not BoxerAnatomy.Bone; p += step)
        {
            n++;
        }

        return n;
    }

    [TestMethod]
    public void TheGlove_IsAboutTheSizeAndMassOfTheOldBall()
    {
        var grid = Weapon.Build(WeaponKind.Glove);
        var width = 2f * grid.HalfExtents.Z;
        Assert.IsInRange(0.1239f, 0.1521f, width, $"the glove is about as wide as the old ball (135 mm): {width * 1000f:F0} mm");
        Assert.IsInRange(0.05634f, 0.07887f, Weapon.CoverRadiusOf(WeaponKind.Glove), "the visible size is the old ball's radius, 68 mm");
        var mass = Weapon.MassOf(WeaponKind.Glove);
        Assert.IsInRange(0.40f, 0.56f, mass, $"the mass stays near the old ball (480 g): {mass * 1000f:F0} g");
        Assert.AreEqual(0.06761f, Weapon.TipOf(WeaponKind.Glove).X - Weapon.GripOf(WeaponKind.Glove).X, 2.817e-4f, "the striking face is 68 mm in front of the wrist, as the old ball's front was");
    }

    [TestMethod]
    public void TheGlovePadding_IsThickestInFront_AndThinnerOnTheBackOfTheHand()
    {
        var grid = Weapon.Build(WeaponKind.Glove);
        var cell = grid.CellSize.X;
        var tip = Weapon.TipOf(WeaponKind.Glove); // 拳頭の前の詰め物の外の面（物の格子のローカル）
        var front = CoatRun(grid, tip - new Vector3(0.5f * cell, 0f, 0f), new Vector3(-cell, 0f, 0f));
        var top = CoatRun(grid, new Vector3(tip.X - 0.0338f, grid.HalfExtents.Y - (0.5f * cell), 0f), new Vector3(0f, -cell, 0f));
        Assert.IsGreaterThan(top, front, $"front padding {front} cells vs back of the hand {top} cells");
    }
}
