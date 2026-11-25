using System.Numerics;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 木の強さ（2026-11-25、ユーザー「木が過剰に折れやすい」）: 木の武器は実物の樫の値を骨と同じ相似則で縮めたもの。ボクセルの丸棒は梁の理論どおりの力で折れ、
/// 手の組織（木の値の代用）は武器の木を変えても変わらない。
/// </summary>
[TestClass]
public sealed class WoodStrengthTests
{
    [TestMethod]
    public void OakAgainstCorticalBone_KeepsTheirRealRatioOfBendingStrength()
    {
        // 樫の曲げ強さ（Wood Handbook 表 5-3a: 87〜125 MPa、ホワイトオーク 105）は皮質骨（150 MPa）の 0.6〜0.8 倍。
        // 木と骨は同じ相似則（BoxerAnatomy.Doll）を通るので、ゲームの値の比も実物の比のまま。前の木（60 MPa）は 0.4 倍で、木だけ折れすぎた。
        var palette = BoxerAnatomy.Palette;
        var ratio = palette[BoxerAnatomy.Wood].Mechanics!.Tensile!.Value / palette[BoxerAnatomy.Bone].Mechanics!.Tensile!.Value;
        Assert.IsTrue(ratio is >= 0.6f and <= 0.8f, $"wood / bone bending strength {ratio:0.00}");
    }

    [TestMethod]
    public void AWoodenRod_SnapsUnderTheForceBeamTheoryGives()
    {
        // 自由な丸棒の端を横に押すと、押した端から L/3 で曲げモーメントが最大 4FL/27 になり（慣性が受ける）、F = σ (π r³ / 4) · 27 / (4 L) で折れる。
        // ボクセルの断面（7 mm のセル）で数える梁がこの力から大きく外れないこと（材料の値の換算・断面の数え方の誤りを見る）。
        foreach (var kind in new[] { WeaponKind.Staff, WeaponKind.Jo })
        {
            var grid = Weapon.Build(kind);
            var tensile = grid.Palette[BoxerAnatomy.Wood].MechanicsOrDefault.Tensile!.Value;
            var half = grid.HalfExtents;
            var theory = tensile * MathF.PI * half.Y * half.Y * half.Y / 4f * 27f / (4f * 2f * half.X);
            var measured = BreakingForce(grid, new Vector3(half.X * 0.95f, 0f, 0f));
            Assert.IsTrue(measured / theory is >= 0.8f and <= 1.6f, $"{kind}: breaks at {measured:0} N against {theory:0} N from beam theory");
        }
    }

    [TestMethod]
    public void TheHandTissue_KeepsItsOwnStandInValues_ApartFromTheWeaponWood()
    {
        // 手の組織は木の値の代用（docs/DEBT.md「手の物性を木の値で代用」）。武器の木を実物の樫へ強めても拳は変えないので、手は自分の値を持つ。
        var palette = BoxerAnatomy.Palette;
        foreach (var hand in new[] { BoxerAnatomy.HandSkin, BoxerAnatomy.HandMuscle, BoxerAnatomy.HandBone })
        {
            Assert.AreEqual(BoxerAnatomy.HandMechanics, palette[hand].Mechanics, palette[hand].Name);
        }

        Assert.IsLessThan(palette[BoxerAnatomy.Wood].Mechanics!.Tensile!.Value, BoxerAnatomy.HandMechanics.Tensile!.Value, "the weapon wood is the stronger, real oak");
        // 2026-11-25（PhysicsVersion 105）: 武器の樫は実物の密度 700 kg/m³ に戻した（BoxerAnatomy.WeaponDensity）。手の組織は前の相似の密度のまま（手の重さは変えない）。
        Assert.AreEqual(700f * BoxerAnatomy.DensitySimilitude, BoxerAnatomy.HandMechanics.Density, 1e-3f, "the hand weighs as before (the similitude density)");
        Assert.AreEqual(700f, palette[BoxerAnatomy.Wood].Mechanics!.Density, 1e-3f, "the weapon oak has the real density");
    }

    /// <summary>端を横（+Y）に押して、いちばん曲がった断面が折れる最小の力 [N]（二分探索）。</summary>
    private static float BreakingForce(Aigis.Foundation.Voxels.VoxelGrid grid, Vector3 at)
    {
        var lo = 1f;
        var hi = 1e6f;
        for (var i = 0; i < 60; i++)
        {
            var f = MathF.Sqrt(lo * hi);
            if (VoxelBending.WorstBend(grid, at, new Vector3(0f, f, 0f)) is { Ratio: >= 1f })
            {
                hi = f;
            }
            else
            {
                lo = f;
            }
        }

        return hi;
    }
}
