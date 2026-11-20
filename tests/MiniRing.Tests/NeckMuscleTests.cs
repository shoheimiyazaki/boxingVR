using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 曲げを支える肉（2026-11-20、PhysicsVersion 61）: 首・腕・脚の梁は、伸ばされる側の筋肉（体の張りに比例）・皮膚が紐として先に曲げを受け、骨は
/// 残りだけを受ける（<see cref="VoxelBending.FindBreak"/> の tieActivation）。値は最大等尺張力 0.35 MPa・受動 0.035 MPa（人の値 × 相似則）。
/// </summary>
[TestClass]
public sealed class NeckMuscleTests
{
    private static float Threshold(VoxelGrid grid, Vector3 point, Vector3 dir, float tone, int axis = 1, (int, int)? slices = null)
    {
        float lo = 0f, hi = 200000f;
        for (var i = 0; i < 40; i++)
        {
            var mid = 0.5f * (lo + hi);
            if (VoxelBending.FindBreak(grid, point, dir * mid, axis, loadBearingOnly: true, slices ?? BoxerSpine.NeckSlices, tone) is not null)
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

    private static VoxelGrid Head(BoxingGame game) => game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Head)!.Body.Grid;

    /// <summary>あごの高さの +X の横から -X へ押す（首の段は -X へ曲がり、+X の側が伸ばされる）。</summary>
    private static (Vector3 At, Vector3 Dir) ChinSide(VoxelGrid grid) => (new Vector3(grid.HalfExtents.X, -0.5f * grid.HalfExtents.Y, 0f), -Vector3.UnitX);

    private static void Carve(VoxelGrid grid, Func<int, int, bool> where)
    {
        var (low, high) = BoxerSpine.NeckSlices;
        var removed = new List<RemovedVoxel>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            var (x, y, _) = grid.Coordinates(i);
            if (grid.MaterialAt(i) == BoxerAnatomy.Muscle && y >= low && y <= high && where(x, grid.Dimensions.X))
            {
                grid.Break(i, removed);
            }
        }

        Assert.IsNotEmpty(removed, "the neck has muscle there to carve");
    }

    [TestMethod]
    public void ABracedNeck_HoldsMoreOfTheGloveBlowThatBentItClosest_ThanASlackOne()
    {
        using var game = new BoxingGame(seed: 1);
        var grid = Head(game);
        // 種 15（champion – swarmer、4.94 s）の接触: 首の上の段の横の皮膚を、グローブが首の柱から 6.5 cm 横で下から斜め上へ押した。
        // 骨だけでは 42 N で折れる（その接触は 33 N で骨の強さの 0.78 倍）。測った（2026-11-20、人形の単位。実物大で約 10 倍）: 張り 1 で 54 N、0 で 44 N。
        var at = new Vector3(-0.0637f, -0.0487f, -0.00394f);
        var dir = Vector3.Normalize(new Vector3(19.97f, 20.90f, 15.16f));
        var braced = Threshold(grid, at, dir, tone: 1f);
        var slack = Threshold(grid, at, dir, tone: 0f);
        Assert.IsGreaterThan(slack * 1.15f, braced, $"braced {braced:0} N vs slack {slack:0} N");
    }

    [TestMethod]
    public void AFaintedNeck_IsWeakerThanABracedOne()
    {
        using var game = new BoxingGame(seed: 1);
        var grid = Head(game);
        var (at, dir) = ChinSide(grid);
        var braced = Threshold(grid, at, dir, tone: 1f);
        var half = Threshold(grid, at, dir, tone: 0.5f);
        var slack = Threshold(grid, at, dir, tone: 0f);
        // 測った（2026-11-20、人形の単位。実物大で約 10 倍）: 張り 1 で 593 N、0 で 482 N（骨だけなら 460 N）。張りに連続に従う。
        Assert.IsGreaterThan(slack * 1.15f, braced, $"braced {braced:0} N vs slack {slack:0} N");
        Assert.IsTrue(half > slack && half < braced, $"tone in between gives a neck in between: {slack:0} < {half:0} < {braced:0}");
    }

    [TestMethod]
    public void ANeckWithItsMuscleCarvedAway_IsWeaker()
    {
        using var game = new BoxingGame(seed: 1);
        var grid = Head(game);
        var (at, dir) = ChinSide(grid);
        var whole = Threshold(grid, at, dir, tone: 1f);
        Carve(grid, (_, _) => true);
        var carved = Threshold(grid, at, dir, tone: 1f);
        Assert.IsLessThan(whole * 0.9f, carved, $"the carved neck is weaker: {carved:0} N vs {whole:0} N");
    }

    [TestMethod]
    public void OnlyOneSidesMuscle_Holds_AndTheSideFlipsWithThePush()
    {
        // 筋肉は引っぱるだけで押せない: 曲げを受けるのは伸ばされる側の筋肉だけ。押す向きを逆にすると、効く側も逆になる。
        // （自由な梁なので、頭を横から押すと首の根元の側の慣性が遅れ、首は押した向きの側が伸ばされる。測った（2026-11-20）: +X から -X へ押すと、
        // -X の側の筋肉を削って 593 → 449 N、+X の側を削って 565 N（削ったセルの質量の分だけ慣性が変わる）。）
        float Threshold2(bool fromPlusX, Func<int, int, bool>? carve)
        {
            using var game = new BoxingGame(seed: 1);
            var grid = Head(game);
            if (carve is not null)
            {
                Carve(grid, carve);
            }

            var side = fromPlusX ? 1f : -1f;
            return Threshold(grid, new Vector3(side * grid.HalfExtents.X, -0.5f * grid.HalfExtents.Y, 0f), -side * Vector3.UnitX, tone: 1f);
        }

        static bool Minus(int x, int nx) => x < nx / 2;
        static bool Plus(int x, int nx) => x >= nx / 2;
        foreach (var fromPlusX in new[] { true, false })
        {
            var whole = Threshold2(fromPlusX, null);
            var near = Threshold2(fromPlusX, fromPlusX ? Plus : Minus); // 押された側
            var far = Threshold2(fromPlusX, fromPlusX ? Minus : Plus); // 押した向きの側
            Assert.IsLessThan(whole * 0.85f, far, $"push from {(fromPlusX ? "+X" : "-X")}: carving the side it pushes toward weakens it ({far:0} N vs {whole:0} N)");
            Assert.IsGreaterThan(far + ((whole - far) * 0.6f), near, $"while carving the struck side matters far less ({near:0} N)");
        }
    }

    [TestMethod]
    public void TheFighterBracesItsBeams_WithItsTone_AndLetsThemGoSlackWhenItFaints()
    {
        using var game = new BoxingGame(seed: 1);
        for (var i = 0; i < 50; i++) // 約 0.84 秒
        {
            game.Step(default, 1f / 60f);
        }

        var fighter = game.Cpu;
        var head = fighter.Voxels!.PartOf(fighter.Parts.Head)!.Body;
        var forearm = fighter.Voxels.PartOf(fighter.Parts.LeftForearm)!.Body;
        Assert.IsGreaterThan(0.9f, head.TieActivation, "a standing, clear-headed fighter braces its neck");
        Assert.IsGreaterThan(0.9f, forearm.TieActivation, "and its arms");

        fighter.Concussion = 1f; // 意識が飛ぶ（張りが抜ける）
        game.Step(default, 0.028f);
        Assert.IsLessThan(0.05f, head.TieActivation, $"a fainted fighter's neck is slack: {head.TieActivation:0.000}");
        Assert.IsLessThan(0.05f, forearm.TieActivation, "and so are its arms");
    }

    [TestMethod]
    public void MuscleInTheForearm_HoldsSomeOfTheBending_LikeTheNeck()
    {
        var tuning = FighterTuning.Default;
        var grid = BoxerAnatomy.Build(BodyPartKind.Forearm, tuning.HalfExtents.Forearm, tuning.VoxelSize);
        var braced = Threshold(grid, Vector3.Zero, -Vector3.UnitZ, tone: 1f, axis: 0, slices: (0, int.MaxValue));
        var slack = Threshold(grid, Vector3.Zero, -Vector3.UnitZ, tone: 0f, axis: 0, slices: (0, int.MaxValue));
        // 測った（2026-11-20）: 前腕の真ん中を真横から押して折れる力（張り 1 / 0）。
        Assert.IsGreaterThan(slack * 1.1f, braced, $"one model for every beam: the braced forearm holds more ({braced:0} N vs {slack:0} N)");
    }
}
