using Aigis.Foundation.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 左右の腕・脚は 1 つの格子（−X 側の向き）を共有し、体の +X 側（<see cref="Fighter.OnPlusXSide"/>）は X で鏡に写して使う（CT の体の腕は、肩と肘・
/// 内と外で中身が違うので、写さないと片側が逆さになる。docs/plans/CT_BODY_PLAN.md 段 2 (a)）。
/// </summary>
[TestClass]
public sealed class LimbMirrorTests
{
    private static BoxingGame Game() =>
        new(FighterTuning.Default, 1, new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));

    [TestMethod]
    public void ThePlusXLimbs_AreTheMirrorImagesOfTheMinusXOnes_CellForCell()
    {
        using var game = Game();
        var fighter = game.Cpu;
        var p = fighter.Parts;
        var toes = fighter.AllParts.Where(id => fighter.KindOf(id) == BodyPartKind.Toe).ToArray();
        var minusToe = toes.Single(id => !fighter.OnPlusXSide(id));
        var plusToe = toes.Single(fighter.OnPlusXSide);
        var pairs = new[]
        {
            (p.LeftUpperArm, p.RightUpperArm), (p.LeftForearm, p.RightForearm), (p.LeftThigh, p.RightThigh),
            (p.LeftShin, p.RightShin), (p.LeftFoot, p.RightFoot), (minusToe, plusToe),
        };
        foreach (var (minus, plus) in pairs)
        {
            Assert.IsFalse(fighter.OnPlusXSide(minus));
            Assert.IsTrue(fighter.OnPlusXSide(plus));
            var a = fighter.Voxels!.PartOf(minus)!.Body.Grid;
            var b = fighter.Voxels.PartOf(plus)!.Body.Grid;
            Assert.AreEqual(a.Dimensions, b.Dimensions);
            var (nx, ny, nz) = a.Dimensions;
            var differ = 0;
            for (var z = 0; z < nz; z++)
            {
                for (var y = 0; y < ny; y++)
                {
                    for (var x = 0; x < nx; x++)
                    {
                        differ += a.At(x, y, z) != b.At(nx - 1 - x, y, z) ? 1 : 0;
                    }
                }
            }

            Assert.AreEqual(0, differ, $"{fighter.KindOf(minus)}: the +X side is the X mirror of the -X side");
        }

        foreach (var id in new[] { p.Head, p.Chest, p.Pelvis })
        {
            Assert.IsFalse(fighter.OnPlusXSide(id), "the trunk and head are one part each and are not mirrored");
        }
    }

    [TestMethod]
    public void Mirroring_LeavesAnXSymmetricGridAsItIs_AndTwiceGivesTheOriginalBack()
    {
        using var game = Game();
        var grid = game.Cpu.Voxels!.PartOf(game.Cpu.Parts.LeftUpperArm)!.Body.Grid;
        var twice = BoxerBody.MirroredX(BoxerBody.MirroredX(grid));
        var (nx, ny, nz) = grid.Dimensions;
        // 対称な格子: 左半分を右へ写したもの。
        var half = new VoxelGrid(grid.Palette, grid.HalfExtents, grid.Dimensions, (point, _) =>
        {
            var x = Math.Clamp((int)MathF.Floor((point.X + grid.HalfExtents.X) / grid.CellSize.X), 0, nx - 1);
            var y = Math.Clamp((int)MathF.Floor((point.Y + grid.HalfExtents.Y) / grid.CellSize.Y), 0, ny - 1);
            var z = Math.Clamp((int)MathF.Floor((point.Z + grid.HalfExtents.Z) / grid.CellSize.Z), 0, nz - 1);
            return grid.At(Math.Min(x, nx - 1 - x), y, z);
        });
        var mirroredHalf = BoxerBody.MirroredX(half);
        for (var i = 0; i < grid.CellCount; i++)
        {
            Assert.AreEqual(grid.MaterialAt(i), twice.MaterialAt(i), "mirrored twice is the original");
            Assert.AreEqual(half.MaterialAt(i), mirroredHalf.MaterialAt(i), "an X-symmetric grid is unchanged");
        }
    }

    [TestMethod]
    public void AHalo_IsMirroredTheSameWayAsItsPart()
    {
        var dims = (4, 2, 3);
        var cells = new byte[4 * 2 * 3];
        cells[1 + (4 * (1 + (2 * 2)))] = BoxerAnatomy.Artery; // (1, 1, 2)
        var mirrored = new BoxerVoxelOverrides.PartCells(dims, cells).MirroredX();
        Assert.AreEqual(BoxerAnatomy.Artery, mirrored.Materials[2 + (4 * (1 + (2 * 2)))], "(1, 1, 2) goes to (4 - 1 - 1, 1, 2)");
        Assert.AreEqual(1, mirrored.Materials.Count(c => c != 0));
        CollectionAssert.AreEqual(cells, mirrored.MirroredX().Materials);
    }
}
