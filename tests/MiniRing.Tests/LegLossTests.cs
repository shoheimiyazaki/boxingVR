using System.Numerics;
using Aigis.Foundation.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 脚が折れた・切れた体は、残った脚の長さで立って（膝立ち・座り）戦い続ける（PhysicsVersion 39）。
/// 腰の高さは短い脚の端が床に着く高さまで下がり、長い脚はその分曲げる。倒れたままの 10 カウントにはならない。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class LegLossTests
{
    private const float Dt = 1f / 60f;

    [TestMethod]
    public void ACutShin_KneelsOnTheStump_AndFightsOn()
    {
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        Play(game, 1f);
        var standing = cpu.PelvisPosition.Y;
        var full = cpu.LegSupportLength(true);

        Cut(game, cpu, cpu.Parts.LeftShin, 0.4f);
        Assert.IsLessThan(full, cpu.LegSupportLength(true), "the cut leg bears weight only down to the cut");
        Assert.AreEqual(full, cpu.LegSupportLength(false), "the other leg is whole");
        Assert.IsTrue(cpu.CanStand, "a stump still holds the body up");

        Play(game, 8f);
        TestContext.WriteLine($"standing {standing:F3}  now {cpu.PelvisPosition.Y:F3}  shortfall {cpu.LegShortfall:F3}  state {cpu.State}  hover {cpu.HoverHeight:F3}");
        Assert.AreEqual(FighterState.Standing, cpu.State, "back on the stump and fighting, not counted out");
        Assert.IsFalse(game.RoundOver && game.PlayerWins > 0, "the round does not end on the lost leg");
        var drop = standing - cpu.PelvisPosition.Y;
        Assert.IsGreaterThan(0.5f * cpu.LegShortfall, drop, "the hips come down to where the stump meets the floor");
        Assert.IsLessThan(1.6f * cpu.LegShortfall, drop, "but not down to the canvas");
    }

    [TestMethod]
    public void ACutThigh_SitsLowerThanACutShin()
    {
        float Hips(Func<Fighter, (int Part, float At)> part)
        {
            using var game = new BoxingGame(seed: 2);
            var cpu = game.Cpu;
            Play(game, 1f);
            Cut(game, cpu, part(cpu).Part, part(cpu).At);
            Play(game, 8f);
            TestContext.WriteLine($"hips {cpu.PelvisPosition.Y:F3}  shortfall {cpu.LegShortfall:F3}  state {cpu.State}  cond {cpu.LegCondition(true):F2} thighStr {cpu.Strength(cpu.Parts.LeftThigh):F2} muscle {cpu.Voxels!.Muscle(cpu.Parts.LeftThigh):F2} support {cpu.LegSupportLength(true):F3}/{cpu.LegSupportLength(false):F3} thighPos {game.World.GetState(cpu.Parts.LeftThigh).Position} pelvis {cpu.PelvisPosition}");
            Assert.IsTrue(cpu.CanStand);
            return cpu.PelvisPosition.Y;
        }

        var shin = Hips(f => (f.Parts.LeftShin, 0.4f));
        var thigh = Hips(f => (f.Parts.LeftThigh, 0.25f));
        Assert.IsLessThan(shin, thigh, "the shorter the stump, the lower the body");
    }

    [TestMethod]
    public void TheStumpKeepsTheMuscleAboveTheCut()
    {
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        Play(game, 0.5f);
        Cut(game, cpu, cpu.Parts.LeftShin, 0.4f);
        // 切れ目の先（破片へ移ったスライス）を断面に数えると、切り株の筋肉は 0 近くになって立てなかった。
        Assert.IsGreaterThan(0.35f, cpu.LegCondition(true), $"stump condition {cpu.LegCondition(true):F2}");
    }

    [TestMethod]
    public void BothLegsGone_SitsOnThePelvis_AndIsNotCountedDown()
    {
        // 近道あり（FighterTuning.LegLossSupportShortcut）の比べもの。既定（近道なし）は下の WithoutTheShortcut_BothLegsGone_*。
        using var game = new BoxingGame(Shortcut, seed: 2);
        var cpu = game.Cpu;
        Play(game, 0.5f);
        var full = cpu.LegSupportLength(true);
        var standingHips = cpu.PelvisPosition.Y;
        Cut(game, cpu, cpu.Parts.LeftThigh, 0.2f);
        Cut(game, cpu, cpu.Parts.RightThigh, 0.2f);
        Assert.IsLessThan(0.5f * full, MathF.Max(cpu.LegSupportLength(true), cpu.LegSupportLength(false)), "both legs are cut near the hip");
        Assert.IsTrue(cpu.CanStand, "sitting needs no leg strength");

        // 骨盤で座ったまま相手へ寄って打つので、ときどき倒れる。倒れても座り直して戻り、10 カウントになるほど床にいない。
        // 体を支える力が失った脚の分まで押し上げて跳ね上がる（55 cm 浮いて裏返った）ことも、手を床に着いたまま立ち直れないことも無い。
        var standing = 0;
        var down = 0f;
        var longestDown = 0f;
        var highest = 0f;
        const int Steps = 16 * 60;
        for (var t = 0; t < Steps; t++)
        {
            game.Step(default, Dt);
            standing += cpu.State == FighterState.Standing ? 1 : 0;
            down = cpu.State == FighterState.Standing ? 0f : down + Dt;
            longestDown = MathF.Max(longestDown, down);
            highest = MathF.Max(highest, cpu.PelvisPosition.Y);
        }

        var share = standing / (float)Steps;
        TestContext.WriteLine($"standing {share:P0}  longest down {longestDown:F1}s  support {cpu.LegSupportLength(true):F3}/{cpu.LegSupportLength(false):F3}  state {cpu.State}");
        Assert.IsGreaterThan(0.5f, share, "sitting up on the pelvis and fighting more than half the time (measured 58 %)");
        Assert.IsLessThan(5f, longestDown, "never down long enough to be counted out");
        Assert.IsLessThan(standingHips, highest, "the hips never go above where they stood on whole legs (no launch)");
    }

    private static readonly FighterTuning Shortcut = FighterTuning.Default with { LegLossSupportShortcut = true };

    [TestMethod]
    public void WithoutTheShortcut_BothLegsGone_CannotSitBackUp_AndIsCountedOut()
    {
        // 2026-11-17（ユーザーの指定: 近道なしを採る）: 両脚を付け根近くで失った体は、支えのモデルの中では骨盤の上へ上体を起こせず
        // （腹筋で起き上がる仕組みが無い）、床でもがいたまま 10 カウントになる。体は浮き上がらない。
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        Play(game, 0.5f);
        var standingHips = cpu.PelvisPosition.Y;
        Cut(game, cpu, cpu.Parts.LeftThigh, 0.2f);
        Cut(game, cpu, cpu.Parts.RightThigh, 0.2f);
        var highest = 0f;
        var standing = 0;
        var steps = 0;
        for (; steps < 16 * 60 && !game.RoundOver; steps++)
        {
            game.Step(default, Dt);
            highest = MathF.Max(highest, cpu.PelvisPosition.Y);
            standing += cpu.State == FighterState.Standing ? 1 : 0;
        }

        TestContext.WriteLine($"round over {game.RoundOver} after {steps * Dt:F1}s  standing {standing * Dt:F1}s  {game.Message}");
        Assert.IsTrue(game.RoundOver && game.PlayerWins > 0, "counted out on the canvas");
        Assert.IsLessThan(2f, standing * Dt, "never back up for long");
        Assert.IsLessThan(standingHips, highest, "the hips never go above where they stood on whole legs (no launch)");
    }

    [TestMethod]
    public void WithoutTheShortcut_ACutThigh_CannotStepToKeepItsBalance_AndGoesDown()
    {
        // 片方の腿を失った体は足の運びで踏み直せないので、短い脚と長い脚の支えの外へ重心が出ると倒れる（近道ありなら立ち続けた）。
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        Play(game, 1f);
        var standingHips = cpu.PelvisPosition.Y;
        Cut(game, cpu, cpu.Parts.LeftThigh, 0.5f);
        var fell = false;
        var highest = 0f;
        for (var t = 0; t < 8 * 60 && !game.RoundOver; t++)
        {
            game.Step(default, Dt);
            fell |= cpu.State != FighterState.Standing;
            highest = MathF.Max(highest, cpu.PelvisPosition.Y);
        }

        TestContext.WriteLine($"fell {fell}  state {cpu.State}  shortfall {cpu.LegShortfall:F3}");
        Assert.IsTrue(cpu.CanStand, "the stump still has the strength to bear weight");
        Assert.IsTrue(fell, "without stepping, the body falls off its stump");
        Assert.IsLessThan(standingHips, highest, "and is never lifted from nowhere");
    }

    [TestMethod]
    public void TheLegBones_AreAsThickAtTheJointsAsInTheShaft_SoATouchNearTheKneeDoesNotSnapThem()
    {
        // 2026-11-17（PhysicsVersion 40）: 脚の外形が膝・股で絞れていて、骨が端の段で 1〜2 セルまで細り、30〜60 N の当たりでそこが折れて
        // 脚が丸ごと落ちていた。骨の端（骨端）を骨の胴と同じ断面にした。
        using var game = new BoxingGame(seed: 1);
        var f = game.Cpu;
        foreach (var body in new[] { f.Parts.LeftThigh, f.Parts.LeftShin })
        {
            var part = f.Voxels!.PartOf(body)!;
            var grid = part.Body.Grid;
            var ny = grid.Dimensions.Y;
            var bone = new int[ny];
            for (var i = 0; i < grid.CellCount; i++)
            {
                var m = grid.AtIndex(i);
                if (m != 0 && grid.Palette[m].Tissue == VoxelTissue.Bone)
                {
                    bone[grid.Coordinates(i).Y]++;
                }
            }

            var withBone = bone.Where(b => b > 0).ToArray();
            Assert.AreEqual(withBone.Max(), withBone.Min(), $"every slice of the bone has the shaft's section: [{string.Join(",", bone)}]");

            // 端の近く（長さの 45 % の所）を直角に押して折れる力は、端を細く作っていた頃の腿 138〜301 N・すね 43〜98 N を下回らず、
            // 折れる所は端の段ではない。
            var half = grid.HalfExtents.Y;
            var force = 1f;
            Aigis.Physics.Voxels.VoxelBreak? found = null;
            while (force < 1e5f && (found = Aigis.Physics.Voxels.VoxelBending.FindBreak(grid, new Vector3(0f, -0.9f * half, 0f), -Vector3.UnitZ * force, 1, loadBearingOnly: true, part.Body.BeamSlices)) is null)
            {
                force *= 1.05f;
            }

            var ends = (Array.IndexOf(bone, withBone[0]), Array.LastIndexOf(bone, withBone[^1]));
            TestContext.WriteLine($"{(body == f.Parts.LeftThigh ? "thigh" : "shin")} bone [{string.Join(",", bone)}] breaks at {force:F0} N in slice {found?.Slice}");
            Assert.IsGreaterThan(150f, force);
            Assert.IsTrue(found is { } b && b.Slice != ends.Item1 && b.Slice != ends.Item2, "it does not snap at the end of the bone");
        }
    }

    public TestContext TestContext { get; set; } = null!;

    private static void Play(BoxingGame game, float seconds)
    {
        for (var i = 0; i < (int)(seconds / Dt); i++)
        {
            game.Step(default, Dt);
        }
    }

    /// <summary>脚の部位 <paramref name="part"/> を、体に近い端から長さの <paramref name="fromNear"/> の所で、骨の軸に直角な 1 枚の段ごと断つ（切れた結果だけを起こす）。</summary>
    private static void Cut(BoxingGame game, Fighter fighter, int part, float fromNear)
    {
        var voxel = fighter.Voxels!.PartOf(part)!;
        var grid = voxel.Body.Grid;
        var n = grid.Dimensions.Y;
        var k = Math.Clamp((int)MathF.Round(fromNear * (n - 1)), 1, n - 2);
        var slice = voxel.DistalHigh ? k : n - 1 - k;
        var removed = new List<RemovedVoxel>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.Coordinates(i).Y == slice && grid.MaterialAt(i) != 0)
            {
                grid.Break(i, removed);
            }
        }

        var broken = fighter.Voxels.BreakIfSevered(part);
        Assert.IsNotNull(broken, "the cut goes through the bone");
        game.BreakBone(fighter, broken, 1f);
    }
}
