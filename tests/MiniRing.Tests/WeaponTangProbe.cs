using System.Globalization;
using System.Numerics;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調査用（スイートの外）: 剣の柄の断面（鉄の芯と木）と、柄が折れる力を書く。2026-11-20 に <see cref="WeaponTangTests"/> から移した。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class WeaponTangProbe
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>柄の断面（YZ 平面）を文字で書く（# = 鉄、w = 木）: 柄の真ん中と、鍔の手前、柄頭の手前。目で確かめる用。</summary>
    [TestMethod]
    public void Probe_GripCrossSections()
    {
        foreach (var kind in WeaponTangTests.Swords)
        {
            var grid = Weapon.Build(kind, BoxerAnatomy.RedGlove, null);
            var last = (int)MathF.Floor((WeaponTangTests.GripEndX(kind) + grid.HalfExtents.X) / grid.CellSize.X) - 1;
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
        foreach (var kind in WeaponTangTests.Swords)
        {
            var grid = Weapon.Build(kind, BoxerAnatomy.RedGlove, null);
            var z = WeaponTangTests.GripBreakForce(kind, Vector3.UnitZ);
            var y = WeaponTangTests.GripBreakForce(kind, Vector3.UnitY);
            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{kind}: cells wood {grid.Remaining(BoxerAnatomy.Wood)} iron {grid.Remaining(BoxerAnatomy.Iron)}; grip breaks at Z {z:0.0} N, Y {y:0.0} N"));
        }
    }
}
