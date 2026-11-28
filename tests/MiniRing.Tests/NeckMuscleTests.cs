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
[TestCategory("L3a")]
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

    private static readonly (int Low, int High) BeamSlices = (3, 5);

    // 左右対称の骨と筋肉で、部位の形や断面ごとの質量の偏りを除いて張力を測る。
    private static VoxelGrid SymmetricBeam() => new(BoxerAnatomy.Palette, new Vector3(0.05f, 0.09f, 0.03f), (5, 9, 3),
        (point, _) => MathF.Abs(point.X) < 0.025f ? BoxerAnatomy.Bone : BoxerAnatomy.Muscle);

    /// <summary>あご側の端を +X から -X へ押す。</summary>
    private static (Vector3 At, Vector3 Dir) ChinSide(VoxelGrid grid) => (new Vector3(grid.HalfExtents.X, 0.9f * grid.HalfExtents.Y, 0f), -Vector3.UnitX);

    private static void Carve(VoxelGrid grid, Func<int, int, bool> where)
    {
        var (low, high) = BeamSlices;
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
        // 2026-11-27（基準の体）: 接触の高さは首の上の段（NeckSlices.High）の中心に置く（頭の格子は 7 mm 格子の顔の層で高さの刻みが変わり、前の -0.0487 は首の段の 1 つ上 = 頭の中になった。
        // 首の段の外に当てると曲げの梁にならず 20 万 N でも折れない）。横 6.5 cm（首の横の皮膚の端）はそのまま。
        var at = new Vector3(-0.0637f, grid.CellCenter(0, BoxerSpine.NeckSlices.High, 0).Y, -0.00394f);
        var dir = Vector3.Normalize(new Vector3(19.97f, 20.90f, 15.16f));
        var braced = Threshold(grid, at, dir, tone: 1f);
        var slack = Threshold(grid, at, dir, tone: 0f);
        Assert.IsGreaterThan(slack, braced, $"braced {braced:0} N vs slack {slack:0} N");
    }

    [TestMethod]
    public void GreaterActivation_IncreasesBendingStrength()
    {
        var grid = SymmetricBeam();
        var (at, dir) = ChinSide(grid);
        var braced = Threshold(grid, at, dir, tone: 1f, slices: BeamSlices);
        var half = Threshold(grid, at, dir, tone: 0.5f, slices: BeamSlices);
        var slack = Threshold(grid, at, dir, tone: 0f, slices: BeamSlices);
        Assert.IsGreaterThan(slack, braced, $"braced {braced:0} N vs slack {slack:0} N");
        Assert.IsTrue(half > slack && half < braced, $"tone in between gives a neck in between: {slack:0} < {half:0} < {braced:0}");
    }

    [TestMethod]
    public void CarvingAwayMuscle_ReducesTheContributionOfBracing()
    {
        var grid = SymmetricBeam();
        var (at, dir) = ChinSide(grid);
        float BracingContribution() => Threshold(grid, at, dir, tone: 1f, slices: BeamSlices)
            - Threshold(grid, at, dir, tone: 0f, slices: BeamSlices);
        var whole = BracingContribution();
        Carve(grid, (_, _) => true);
        Assert.IsGreaterThan(0f, whole);
        Assert.AreEqual(0f, BracingContribution(), 0.01f * whole);
    }

    [TestMethod]
    public void OnlyOneSidesMuscle_Holds_AndTheSideFlipsWithThePush()
    {
        // 筋肉は引っぱるだけで押せない: 曲げを受けるのは伸ばされる側の筋肉だけ。押す向きを逆にすると、効く側も逆になる。
        float ActiveContribution(float sign, Func<int, int, bool>? carve)
        {
            var grid = SymmetricBeam();
            if (carve is not null)
            {
                Carve(grid, carve);
            }

            var at = new Vector3(sign * grid.HalfExtents.X, 0.9f * grid.HalfExtents.Y, 0f);
            var forceDirection = -sign * Vector3.UnitX;
            return Threshold(grid, at, forceDirection, tone: 1f, slices: BeamSlices)
                - Threshold(grid, at, forceDirection, tone: 0f, slices: BeamSlices);
        }

        static bool Minus(int x, int nx) => x < nx / 2;
        static bool Plus(int x, int nx) => x >= (nx + 1) / 2;
        var whole = ActiveContribution(1f, null);
        Assert.IsGreaterThan(0f, whole);
        var removedLeft = ActiveContribution(1f, Minus);
        var removedRight = ActiveContribution(1f, Plus);
        Assert.IsLessThan(0.1f * whole, MathF.Min(removedLeft, removedRight));
        Assert.IsGreaterThan(0.5f * whole, MathF.Max(removedLeft, removedRight));
        Assert.AreEqual(removedRight, ActiveContribution(-1f, Minus), 0.01f * whole);
        Assert.AreEqual(removedLeft, ActiveContribution(-1f, Plus), 0.01f * whole);
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
        Assert.IsGreaterThan(slack, braced, $"one model for every beam: the braced forearm holds more ({braced:0} N vs {slack:0} N)");
    }
}
