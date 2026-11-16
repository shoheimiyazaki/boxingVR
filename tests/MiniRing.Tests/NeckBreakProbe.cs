using System.Globalization;
using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>調べもの: 首を曲げる力の閾値（頭の格子への、真横・前後からの 1 点の力）。頸椎の曲げの実効値（VertebraSupport）を決める。</summary>
[TestClass]
public sealed class NeckBreakProbe
{
    public TestContext TestContext { get; set; } = null!;

    private static float Threshold(VoxelGrid grid, Vector3 point, Vector3 dir)
    {
        float lo = 0f, hi = 20000f;
        for (var i = 0; i < 40; i++)
        {
            var mid = 0.5f * (lo + hi);
            var broke = VoxelBending.FindBreak(grid, point, dir * mid, 1, loadBearingOnly: true, BoxerSpine.NeckSlices) is not null;
            if (broke)
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        return hi;
    }

    [TestMethod]
    public void NeckBreakingForce()
    {
        using var game = new BoxingGame(seed: 1);
        var grid = game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Head)!.Body.Grid;
        var half = grid.HalfExtents;
        TestContext.WriteLine($"head half {half}, cell {grid.CellSize}, mass {game.World.GetMass(game.Cpu.Parts.Head):0.000} kg");
        foreach (var (name, height) in new[] { ("chin", -0.5f), ("middle", 0f), ("top", 0.7f) })
        {
            var y = half.Y * height;
            foreach (var (label, dir) in new[] { ("side (-X)", -Vector3.UnitX), ("front (-Z)", -Vector3.UnitZ), ("back (+Z)", Vector3.UnitZ) })
            {
                var at = new Vector3(dir.X != 0f ? -MathF.Sign(dir.X) * half.X : 0f, y, dir.Z != 0f ? -MathF.Sign(dir.Z) * half.Z : 0f);
                TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name,-6} {label,-10} break force {Threshold(grid, at, dir),8:0.0} N"));
            }
        }
    }
}
