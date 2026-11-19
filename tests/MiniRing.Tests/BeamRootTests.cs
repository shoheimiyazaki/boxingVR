using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 梁の根元の支え（2026-11-20）: 腕・脚・首の梁は、関節（位置を留める玉継手）の所に、関節の向こうの部位の質量を固定した体として曲げる
/// （<see cref="VoxelBending.FindBreak"/> の root、<see cref="VoxelBody.BeamRoot"/>）。根元の近くを押した力は根元の質量へ逃げ、
/// 根元から遠い所を押した力は根元に逆らって梁を曲げる。
/// </summary>
[TestClass]
public sealed class BeamRootTests
{
    private static float Threshold(VoxelGrid grid, Vector3 point, Vector3 dir, (Vector3, float)? root)
    {
        float lo = 0f, hi = 50000f;
        for (var i = 0; i < 40; i++)
        {
            var mid = 0.5f * (lo + hi);
            if (VoxelBending.FindBreak(grid, point, dir * mid, 1, loadBearingOnly: true, BoxerSpine.NeckSlices, 1f, root) is not null)
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
    public void TheFighterHangsItsHead_FromItsChest_AtTheNeckJoint()
    {
        using var game = new BoxingGame(seed: 1);
        game.Step(default, 1f / 60f);
        var fighter = game.Cpu;
        var head = fighter.Voxels!.PartOf(fighter.Parts.Head)!.Body;
        Assert.IsNotNull(head.BeamRoot);
        Assert.AreEqual(game.World.GetMass(fighter.Parts.Chest), head.BeamRoot.Value.Mass, 1e-5f, "the head hangs from the chest");
        Assert.IsLessThan(0f, head.BeamRoot.Value.Point.Y, "at the bottom of the head");
        var forearm = fighter.Voxels.PartOf(fighter.Parts.LeftForearm)!.Body;
        Assert.AreEqual(game.World.GetMass(fighter.Parts.LeftUpperArm), forearm.BeamRoot!.Value.Mass, 1e-5f, "the forearm hangs from the upper arm");
    }

    [TestMethod]
    public void ABlowNearTheJoint_GoesIntoTheBody_AndABlowFarFromIt_BendsAgainstIt()
    {
        using var game = new BoxingGame(seed: 1);
        game.Step(default, 1f / 60f);
        var head = game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Head)!.Body;
        var grid = head.Grid;
        var root = head.BeamRoot;
        var half = grid.HalfExtents;
        var lowY = -half.Y + ((BoxerSpine.NeckSlices.Low + 0.5f) * grid.CellSize.Y);
        // 首の下の段の横を真横へ: 自由な頭なら頭全体を振らねばならず 263 N で折れたが、胴が受けて 1010 N（測った、2026-11-20）。
        var nearFree = Threshold(grid, new Vector3(half.X, lowY, 0f), -Vector3.UnitX, null);
        var nearRooted = Threshold(grid, new Vector3(half.X, lowY, 0f), -Vector3.UnitX, root);
        Assert.IsGreaterThan(nearFree * 2f, nearRooted, $"near the joint: free {nearFree:0} N, hung from the chest {nearRooted:0} N");
        // あごの高さの横を真横へ: 頭の慣性だけでは 593 N、胴に留まった首は片持ちで曲がり 172 N。
        var farFree = Threshold(grid, new Vector3(half.X, -0.5f * half.Y, 0f), -Vector3.UnitX, null);
        var farRooted = Threshold(grid, new Vector3(half.X, -0.5f * half.Y, 0f), -Vector3.UnitX, root);
        Assert.IsLessThan(farFree * 0.5f, farRooted, $"far from the joint: free {farFree:0} N, hung from the chest {farRooted:0} N");
    }
}
