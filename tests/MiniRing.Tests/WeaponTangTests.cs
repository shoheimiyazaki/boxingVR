using System.Globalization;
using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 剣・刀・ロングソードの柄に鉄の芯（茎）を通した作り（docs/VOXEL_WEAPONS.md「柄の芯」）。柄の断面が木だけでなく鉄を含み、
/// 刀身から柄頭まで鉄が切れずにつながり、曲げで柄が折れる力が大きくなっていること。
/// </summary>
[TestClass]
public sealed class WeaponTangTests
{
    public TestContext TestContext { get; set; } = null!;

    internal static readonly WeaponKind[] Swords = [WeaponKind.IronSword, WeaponKind.Katana, WeaponKind.Longsword];

    /// <summary>握り（鍔より柄頭側）の x の上限 [m]（格子の座標）。鍔の位置から取る。</summary>
    internal static float GripEndX(WeaponKind kind) => kind switch
    {
        WeaponKind.IronSword => -0.2986f,
        WeaponKind.Katana => -0.4479f + 0.2394f - 0.003521f,
        _ => -0.5352f + 0.02113f + 0.02113f + 0.1831f - 0.004507f,
    };

    private static int CountSlice(VoxelGrid grid, int sliceX, byte material)
    {
        var n = 0;
        for (var z = 0; z < grid.Dimensions.Z; z++)
        {
            for (var y = 0; y < grid.Dimensions.Y; y++)
            {
                if (grid.MaterialAt(grid.Index(sliceX, y, z)) == material)
                {
                    n++;
                }
            }
        }

        return n;
    }

    /// <summary>刃の先を横から押して、柄（鍔の手前）で折れる最小の力 [N]。柄で折れなければ無限大。</summary>
    internal static float GripBreakForce(WeaponKind kind, Vector3 across)
    {
        var grid = Weapon.Build(kind, BoxerAnatomy.RedGlove, null);
        var tip = new Vector3(grid.HalfExtents.X - 0.02817f, 0f, 0f);
        // 折れてよい断面を握り（柄頭から鍔の手前）に限る: 刀身が先に折れる力に隠れず、柄の断面そのものの強さが測れる。
        var lastGrip = (int)MathF.Floor((GripEndX(kind) + grid.HalfExtents.X) / grid.CellSize.X) - 1;
        for (var force = 5f; force < 2e6f; force *= 1.03f)
        {
            if (VoxelBending.FindBreak(grid, tip, across * force, candidates: (0, lastGrip)) is not null)
            {
                return force;
            }
        }

        return float.PositiveInfinity;
    }

    [TestMethod]
    [DataRow(WeaponKind.IronSword)]
    [DataRow(WeaponKind.Katana)]
    [DataRow(WeaponKind.Longsword)]
    public void Tang_IronRunsThroughTheGrip_FromBladeToPommel(WeaponKind kind)
    {
        var grid = Weapon.Build(kind, BoxerAnatomy.RedGlove, null);
        var woodSlices = 0;
        for (var x = 0; x < grid.Dimensions.X; x++)
        {
            var wood = CountSlice(grid, x, BoxerAnatomy.Wood);
            var iron = CountSlice(grid, x, BoxerAnatomy.Iron);
            if (wood > 0)
            {
                woodSlices++;
                Assert.IsTrue(iron > 0, $"{kind}: 木のある柄の断面 {x} に鉄の芯がある");
            }

            if (wood + iron > 0)
            {
                Assert.IsTrue(iron > 0, $"{kind}: 柄頭から切っ先までの断面 {x} で鉄が途切れない");
            }
        }

        Assert.IsTrue(woodSlices > 20, $"{kind}: 握りの木はそのまま残る（{woodSlices} 断面）");
    }

    [TestMethod]
    [DataRow(WeaponKind.IronSword)]
    [DataRow(WeaponKind.Katana)]
    [DataRow(WeaponKind.Longsword)]
    public void Tang_IronIsOneConnectedPiece_BladeGuardTangPommel(WeaponKind kind)
    {
        var grid = Weapon.Build(kind, BoxerAnatomy.RedGlove, null);
        Assert.AreEqual(1, grid.ConnectedComponents(m => m == BoxerAnatomy.Iron).Count, $"{kind}: 鉄のセルは 1 つにつながっている（鍔・刀身・芯・柄頭）");
        Assert.AreEqual(1, grid.ConnectedComponents().Count, $"{kind}: 武器全体も 1 つ");
    }

    [TestMethod]
    [DataRow(WeaponKind.IronSword)]
    [DataRow(WeaponKind.Katana)]
    [DataRow(WeaponKind.Longsword)]
    public void Tang_GripIsStrongerThanWoodAlone(WeaponKind kind)
    {
        var withTang = GripBreakForce(kind, Vector3.UnitZ);
        TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{kind}: grip break {withTang:0.0} N (Z)"));
        Assert.IsTrue(withTang > BaselineWoodOnly[kind] * 1.4f, $"{kind}: 芯入りの柄は木だけの柄（{BaselineWoodOnly[kind]:0.0} N）より 1.4 倍以上の力で折れる（実測 {withTang:0.0} N）");
    }

    /// <summary>芯を入れる前に測った、柄で折れる最小の力（Z 向き、刃の先を押す）[N]。</summary>
    private static readonly Dictionary<WeaponKind, float> BaselineWoodOnly = new()
    {
        [WeaponKind.IronSword] = 1053f,
        [WeaponKind.Katana] = 1641f,
        [WeaponKind.Longsword] = 619f,
    };
}
