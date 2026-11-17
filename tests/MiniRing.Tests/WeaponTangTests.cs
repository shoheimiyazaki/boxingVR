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

    private static readonly WeaponKind[] Swords = [WeaponKind.IronSword, WeaponKind.Katana, WeaponKind.Longsword];

    /// <summary>握り（鍔より柄頭側）の x の上限 [m]（格子の座標）。鍔の位置から取る。</summary>
    private static float GripEndX(WeaponKind kind) => kind switch
    {
        WeaponKind.IronSword => -0.106f,
        WeaponKind.Katana => -0.159f + 0.085f - 0.00125f,
        _ => -0.19f + 0.0075f + 0.0075f + 0.065f - 0.0016f,
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
    private static float GripBreakForce(WeaponKind kind, Vector3 across)
    {
        var grid = Weapon.Build(kind, BoxerAnatomy.RedGlove, null);
        var tip = new Vector3(grid.HalfExtents.X - 0.01f, 0f, 0f);
        // 折れてよい断面を握り（柄頭から鍔の手前）に限る: 刀身が先に折れる力に隠れず、柄の断面そのものの強さが測れる。
        var lastGrip = (int)MathF.Floor((GripEndX(kind) + grid.HalfExtents.X) / grid.CellSize.X) - 1;
        for (var force = 0.5f; force < 2e5f; force *= 1.03f)
        {
            if (VoxelBending.FindBreak(grid, tip, across * force, candidates: (0, lastGrip)) is not null)
            {
                return force;
            }
        }

        return float.PositiveInfinity;
    }

    /// <summary>柄の断面（YZ 平面）を文字で書く（# = 鉄、w = 木）: 柄の真ん中と、鍔の手前、柄頭の手前。目で確かめる用。</summary>
    [TestMethod]
    public void Probe_GripCrossSections()
    {
        foreach (var kind in Swords)
        {
            var grid = Weapon.Build(kind, BoxerAnatomy.RedGlove, null);
            var last = (int)MathF.Floor((GripEndX(kind) + grid.HalfExtents.X) / grid.CellSize.X) - 1;
            foreach (var x in new[] { 3 * last / 4, last / 2, last / 4 })
            {
                TestContext.WriteLine($"{kind} slice {x} of grip (x {-grid.HalfExtents.X + ((x + 0.5f) * grid.CellSize.X):0.000} m):");
                for (var y = grid.Dimensions.Y - 1; y >= 0; y--)
                {
                    var row = new System.Text.StringBuilder();
                    for (var z = 0; z < grid.Dimensions.Z; z++)
                    {
                        var m = grid.At(x, y, z);
                        row.Append(m == BoxerAnatomy.Iron ? '#' : m == BoxerAnatomy.Wood ? 'w' : '.');
                    }

                    if (row.ToString().Any(c => c != '.'))
                    {
                        TestContext.WriteLine("  " + row);
                    }
                }
            }
        }
    }

    [TestMethod]
    public void Probe_GripBreakForce()
    {
        foreach (var kind in Swords)
        {
            var grid = Weapon.Build(kind, BoxerAnatomy.RedGlove, null);
            var z = GripBreakForce(kind, Vector3.UnitZ);
            var y = GripBreakForce(kind, Vector3.UnitY);
            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{kind}: cells wood {grid.Remaining(BoxerAnatomy.Wood)} iron {grid.Remaining(BoxerAnatomy.Iron)}; grip breaks at Z {z:0.0} N, Y {y:0.0} N"));
        }
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
        [WeaponKind.IronSword] = 105.3f,
        [WeaponKind.Katana] = 164.1f,
        [WeaponKind.Longsword] = 61.9f,
    };
}
