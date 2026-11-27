using System.Globalization;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>調べ物（2026-11-27）: 首のテストの接触点（頭の格子の局所座標）が新しい頭でどこに当たるか。首の段の位置と、その高さの皮膚の横の端を出す。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class NeckPointProbe
{
    [TestMethod]
    public void Where()
    {
        using var game = new BoxingGame(seed: 1);
        var grid = game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Head)!.Body.Grid;
        var inv = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Create(inv, $"NECK dims {grid.Dimensions} half {grid.HalfExtents} cell {grid.CellSize}"));
        var (low, high) = BoxerSpine.NeckSlices;
        for (var y = 0; y <= high + 2; y++)
        {
            int minX = int.MaxValue, maxX = -1, bone = 0, filled = 0;
            for (var z = 0; z < grid.Dimensions.Z; z++)
            {
                for (var x = 0; x < grid.Dimensions.X; x++)
                {
                    var m = grid.MaterialAt(grid.Index(x, y, z));
                    if (m == 0)
                    {
                        continue;
                    }

                    filled++;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    if (m == BoxerAnatomy.Bone)
                    {
                        bone++;
                    }
                }
            }

            Console.WriteLine(string.Create(inv, $"NECK row {y} y {grid.CellCenter(0, y, 0).Y:F4} filled {filled} bone {bone} x {(maxX >= 0 ? grid.CellCenter(minX, y, 0).X : 0):F4}..{(maxX >= 0 ? grid.CellCenter(maxX, y, 0).X : 0):F4}"));
        }
    }
}
