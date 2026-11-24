using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;

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
        Play(game, 1.678f);
        var standing = cpu.PelvisPosition.Y;
        var full = cpu.LegSupportLength(true);

        Cut(game, cpu, cpu.Parts.LeftShin, 0.4f);
        Assert.IsLessThan(full, cpu.LegSupportLength(true), "the cut leg bears weight only down to the cut");
        Assert.AreEqual(full, cpu.LegSupportLength(false), "the other leg is whole");
        Assert.IsTrue(cpu.CanStand, "a stump still holds the body up");

        // 切った直後は、残った脚と切り株で立って戦い続ける。
        // 8 秒後に立っているかは見ない: 切り株の体は足の運びで踏み直せず（脚を失った体も釣り合いのモデルを通る、CLAUDE.md §8「脚を失った体」(8)）、
        // 支えは短い切り株と片足の小さな多角形だけで、相手へ歩み寄る体は 2〜7 秒のうちに支えの外へ出て倒れる（種 1〜8 のどれも。
        // 足の運び・支えの変更の前の物理でも同じ）。この試験が前に通っていたのは、1 歩ずれると変わる偶然（種 2・60 歩の待ちで 1 歩ずらすと落ちる）。
        // 倒れた後に起き上がれないのは別の課題。この試験が確かめるのは切り株の高さ（腰が下りる量）と、切った直後に立てること。
        Play(game, 1.678f);
        Assert.AreEqual(FighterState.Standing, cpu.State, "back on the stump and fighting right after the cut");
        Play(game, 11.75f);
        TestContext.WriteLine($"standing {standing:F3}  now {cpu.PelvisPosition.Y:F3}  shortfall {cpu.LegShortfall:F3}  state {cpu.State}  hover {cpu.HoverHeight:F3}");
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
            Play(game, 1.678f);
            Cut(game, cpu, part(cpu).Part, part(cpu).At);
            Play(game, 13.43f);
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
        Play(game, 0.8392f);
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
        Play(game, 0.8392f);
        var full = cpu.LegSupportLength(true);
        Cut(game, cpu, cpu.Parts.LeftThigh, 0.2f);
        Cut(game, cpu, cpu.Parts.RightThigh, 0.2f);
        var unpoweredPeak = UnpoweredPeakAfterCut(game, cpu);
        Assert.IsLessThan(0.5f * full, MathF.Max(cpu.LegSupportLength(true), cpu.LegSupportLength(false)), "both legs are cut near the hip");
        Assert.IsTrue(cpu.CanStand, "sitting needs no leg strength");

        // 骨盤で座ったまま相手へ寄って打つので、ときどき倒れる。倒れても座り直して戻り、10 カウントになるほど床にいない。
        // 体を支える力が失った脚の分まで押し上げて跳ね上がる（55 cm 浮いて裏返った）ことも、手を床に着いたまま立ち直れないことも無い。
        var standing = 0;
        var down = 0f;
        var longestDown = 0f;
        var highest = 0f;
        const int Steps = 1611;
        for (var t = 0; t < Steps; t++)
        {
            game.Step(default, Dt);
            standing += cpu.State == FighterState.Standing ? 1 : 0;
            down = cpu.State == FighterState.Standing ? 0f : down + Dt;
            longestDown = MathF.Max(longestDown, down);
            highest = MathF.Max(highest, cpu.CenterOfMass.Y);
        }

        var share = standing / (float)Steps;
        TestContext.WriteLine($"standing {share:P0}  longest down {longestDown:F1}s  support {cpu.LegSupportLength(true):F3}/{cpu.LegSupportLength(false):F3}  state {cpu.State}");
        // 2026-11-19: 「半分より長く」は 1 つの種の偶然の値だった（種 1〜6 の座っている割合は、マージ前の版 48 で 24〜73 %（平均 45 %）、
        // マージ後は 48〜57 %（平均 50 %）で、種 2 の 58 % はその散らばりの中の 1 点）。座って打つ体は動くたびに倒れて座り直すので、確かめるのは
        // 「立ち直って戦う時間がかなりある」（4 分の 1 より長い）こと。10 カウントにならないことは下の最長のダウンで見る。
        Assert.IsGreaterThan(0.25f, share, "sitting up on the pelvis and fighting a large part of the time (a quarter at least; 35-58 % measured)");
        Assert.IsLessThan(8.392f, longestDown, "never down long enough to be counted out");
        Assert.IsLessThanOrEqualTo(unpoweredPeak, highest, "the remaining body is not launched above its unpowered peak");
    }

    private static readonly FighterTuning Shortcut = FighterTuning.Default with { LegLossSupportShortcut = true };

    [TestMethod]
    public void WithoutTheShortcut_BothLegsGone_CannotSitBackUp_AndIsCountedOut()
    {
        // 2026-11-17（ユーザーの指定: 近道なしを採る）: 両脚を付け根近くで失った体は、支えのモデルの中では骨盤の上へ上体を起こせず
        // （腹筋で起き上がる仕組みが無い）、床でもがいたまま 10 カウントになる。体は浮き上がらない。
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        Play(game, 0.8392f);
        Cut(game, cpu, cpu.Parts.LeftThigh, 0.2f);
        Cut(game, cpu, cpu.Parts.RightThigh, 0.2f);
        var unpoweredPeak = UnpoweredPeakAfterCut(game, cpu);
        var highest = 0f;
        var standing = 0;
        var steps = 0;
        for (; steps < 1611 && !game.RoundOver; steps++)
        {
            game.Step(default, Dt);
            highest = MathF.Max(highest, cpu.CenterOfMass.Y);
            standing += cpu.State == FighterState.Standing ? 1 : 0;
        }

        TestContext.WriteLine($"round over {game.RoundOver} after {steps * Dt:F1}s  standing {standing * Dt:F1}s  {game.Message}");
        Assert.IsTrue(game.RoundOver && game.PlayerWins > 0, "counted out on the canvas");
        Assert.IsLessThan(3.357f, standing * Dt, "never back up for long");
        Assert.IsLessThanOrEqualTo(unpoweredPeak, highest, "the remaining body is not launched above its unpowered peak");
    }

    [TestMethod]
    public void WithoutTheShortcut_ACutThigh_LosesSupportWithoutTakingRecoverySteps()
    {
        var intact = ShoveAndWatch(cut: false);
        var stump = ShoveAndWatch(cut: true);
        Assert.IsTrue(stump.CanStand, "the stump still has the strength to bear weight");
        Assert.IsGreaterThan(0, intact.Steps, "the intact fighter can reposition its feet");
        Assert.AreEqual(0, stump.Steps, "the short leg does not use intact footwork to recover");
        Assert.IsLessThan(1f, stump.MinimumBalance, "the shove moves the capture point beyond the available support");
    }

    private static float UnpoweredPeakAfterCut(BoxingGame game, Fighter fighter)
    {
        // 切断で質量の構成が変わるので、切断後の重心と上向き速度から自由落下の最高点を求める。
        var riseSpeed = MathF.Max(0f, fighter.CenterOfMassVelocity.Y);
        var ballisticRise = riseSpeed * riseSpeed / (2f * -game.World.Gravity.Y);
        return fighter.CenterOfMass.Y + ballisticRise + (riseSpeed * Dt); // 観測間隔 1 フレーム分
    }

    private static (int Steps, float MinimumBalance, bool CanStand) ShoveAndWatch(bool cut)
    {
        var steps = 0;
        var minimumBalance = 1f;
        var canStand = true;
        foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
        {
            using var game = new BoxingGame(seed: 2);
            var cpu = game.Cpu;
            Play(game, 1.678f);
            if (cut)
            {
                Cut(game, cpu, cpu.Parts.LeftThigh, 0.5f);
            }

            Play(game, 1.678f);
            Assert.AreEqual(FighterState.Standing, cpu.State);
            Assert.AreEqual(cut, cpu.LegSevered);
            var stepsBefore = cpu.Footwork.Steps;
            cpu.Push(direction * 4.196f);
            for (var t = 0; t < 403 && !game.RoundOver; t++)
            {
                game.Step(default, Dt);
                minimumBalance = MathF.Min(minimumBalance, cpu.Balance);
            }

            steps += cpu.Footwork.Steps - stepsBefore;
            canStand &= cpu.CanStand;
        }

        return (steps, minimumBalance, canStand);
    }

    [TestMethod]
    public void AMinorSideLoadNearTheKnee_DoesNotBreakTheLegBones()
    {
        // 旧モデルで膝付近の接触時に 30〜60 N で折れたため、100 N の横荷重に耐えることを確かめる。
        const float MinorSideLoadNewtons = 100f;
        using var game = new BoxingGame(seed: 1);
        var f = game.Cpu;
        foreach (var (body, end) in new[] { (f.Parts.LeftThigh, 1f), (f.Parts.LeftShin, -1f) })
        {
            var part = f.Voxels!.PartOf(body)!;
            var beam = part.Body;
            var grid = beam.Grid;
            // 膝は腿の遠位端とすねの近位端にある。格子の遠位側が逆なら位置も反転する。
            var knee = 0.9f * grid.HalfExtents.Y * end * (part.DistalHigh ? 1f : -1f);
            var point = new Vector3(0f, knee, grid.HalfExtents.Z);
            var bend = VoxelBending.WorstBend(grid, point, -Vector3.UnitZ * MinorSideLoadNewtons,
                beam.BeamAxis, loadBearingOnly: true, beam.BeamSlices, tensileScale: beam.BeamTensileScale);
            Assert.IsNotNull(bend, $"{part.Kind}: the bone carries a bending load");
            Assert.IsLessThan(1f, bend.Value.Ratio, $"{part.Kind}: the knee-side load stays below fracture stress");
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
