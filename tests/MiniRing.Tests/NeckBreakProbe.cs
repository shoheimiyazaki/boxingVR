using System.Globalization;
using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 調べもの: 首を曲げて折る力の閾値（頭の格子への 1 点の力）。あごの高さ・首の段・首の段から斜め上へ（グローブがあごの下・首の側面を下から押した
/// 種 4 の首の骨折の向き）を、張り 1（力の入った首）と張り 0（気を失った首）で。首の段の材質の数も書く。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class NeckBreakProbe
{
    public TestContext TestContext { get; set; } = null!;

    internal static float Threshold(VoxelGrid grid, Vector3 point, Vector3 dir, float tone)
    {
        float lo = 0f, hi = 200000f;
        for (var i = 0; i < 40; i++)
        {
            var mid = 0.5f * (lo + hi);
            var broke = VoxelBending.FindBreak(grid, point, dir * mid, 1, loadBearingOnly: true, BoxerSpine.NeckSlices, tone) is not null;
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

    /// <summary>首の段の高さの点（各段の真ん中）と、あごの高さの点、首の柱の中心から 5.6 cm 横（種 4 の接触のずれ）。</summary>
    internal static (string Name, Vector3 At, Vector3 Dir)[] Points(VoxelGrid grid)
    {
        var half = grid.HalfExtents;
        var cell = grid.CellSize;
        float SliceY(float slice) => -half.Y + ((slice + 0.5f) * cell.Y);
        var oblique = Vector3.Normalize(new Vector3(-0.36f, 0.88f, -0.31f));
        var (low, high) = BoxerSpine.NeckSlices;
        // 種 15（champion – swarmer、4.94 s）: 首の上の段の横の皮膚（顔の層）を、グローブが下から斜め上へ滑って押した接触（首の柱から 6.5 cm 横）。
        var seed15 = Vector3.Normalize(new Vector3(19.97f, 20.90f, 15.16f));
        var seed15At = new Vector3(-0.0637f, -0.0487f, -0.00394f);
        return
        [
            ("chin side", new Vector3(half.X, -0.5f * half.Y, 0f), -Vector3.UnitX),
            ("chin front", new Vector3(0f, -0.5f * half.Y, half.Z), -Vector3.UnitZ),
            ("neck(low) side", new Vector3(half.X, SliceY(low), 0f), -Vector3.UnitX),
            ("neck(low) front", new Vector3(0f, SliceY(low), half.Z), -Vector3.UnitZ),
            ("neck(low) oblique", new Vector3(0.0563f, SliceY(low), 0f), oblique),
            ("neck(high) oblique", new Vector3(0.0563f, SliceY(high), 0f), oblique),
            ("skull base oblique", new Vector3(0.0563f, SliceY(high + 1), 0f), oblique),
            ("seed15 neck skin", seed15At, seed15),
            // 同じ接触を、面が押す力（法線）と摩擦の範囲の引きずり（VoxelContact.PressedForce、μ 0.5・切っていない）で。力は打撃の力の大きさ。
            ("neck(low) obl. pressed", new Vector3(0.0563f, SliceY(low), 0f), VoxelContact.PressedForce(oblique, -Vector3.UnitX, 0f)),
            ("neck(high) obl. pressed", new Vector3(0.0563f, SliceY(high), 0f), VoxelContact.PressedForce(oblique, -Vector3.UnitX, 0f)),
            ("seed15 pressed", seed15At, VoxelContact.PressedForce(seed15, Vector3.UnitX, 0f)),
        ];
    }

    [TestMethod]
    public void NeckBreakingForce()
    {
        using var game = new BoxingGame(seed: 1);
        var grid = game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Head)!.Body.Grid;
        var (nx, _, nz) = grid.Dimensions;
        TestContext.WriteLine($"head half {grid.HalfExtents}, cell {grid.CellSize}, dims {grid.Dimensions}, mass {game.World.GetMass(game.Cpu.Parts.Head):0.000} kg");
        for (var y = 0; y <= BoxerSpine.NeckSlices.High + 1; y++)
        {
            var counts = new SortedDictionary<string, int>();
            for (var x = 0; x < nx; x++)
            {
                for (var z = 0; z < nz; z++)
                {
                    var m = grid.MaterialAt(grid.Index(x, y, z));
                    if (m != 0)
                    {
                        counts[grid.Palette[m].Name] = counts.GetValueOrDefault(grid.Palette[m].Name) + 1;
                    }
                }
            }

            TestContext.WriteLine($"  slice {y}: {string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}"))}");
        }

        TestContext.WriteLine("  point                 tone 1     tone 0   [N]");
        foreach (var (name, at, dir) in Points(grid))
        {
            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name,-20} {Threshold(grid, at, dir, 1f),8:0.0} {Threshold(grid, at, dir, 0f),10:0.0}"));
        }
    }
}
