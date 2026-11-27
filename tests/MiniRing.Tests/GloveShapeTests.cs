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
        // 2026-11-27: 革・詰め物は前の質量のまま（0.40 kg）、中の拳の骨 78 セルが骨の実物の密度（1900 kg/m³、前は相似の × 0.4474）になって 0.21 kg。
        // 合わせて 0.61 kg（実物の 16 oz のグローブ 0.45 kg ＋ 手 0.45 kg より軽い）。
        Assert.IsInRange(0.55f, 0.70f, mass, $"the leather and padding of the old ball (400 g) and the fist's bone at bone density: {mass * 1000f:F0} g");
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
