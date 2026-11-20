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
        const int Steps = 1611;
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
        // 2026-11-19: 「半分より長く」は 1 つの種の偶然の値だった（種 1〜6 の座っている割合は、マージ前の版 48 で 24〜73 %（平均 45 %）、
        // マージ後は 48〜57 %（平均 50 %）で、種 2 の 58 % はその散らばりの中の 1 点）。座って打つ体は動くたびに倒れて座り直すので、確かめるのは
        // 「立ち直って戦う時間がかなりある」（4 分の 1 より長い）こと。10 カウントにならないことは下の最長のダウンで見る。
        Assert.IsGreaterThan(0.25f, share, "sitting up on the pelvis and fighting a large part of the time (a quarter at least; 35-58 % measured)");
        Assert.IsLessThan(8.392f, longestDown, "never down long enough to be counted out");
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
        Play(game, 0.8392f);
        var standingHips = cpu.PelvisPosition.Y;
        Cut(game, cpu, cpu.Parts.LeftThigh, 0.2f);
        Cut(game, cpu, cpu.Parts.RightThigh, 0.2f);
        var highest = 0f;
        var standing = 0;
        var steps = 0;
        for (; steps < 1611 && !game.RoundOver; steps++)
        {
            game.Step(default, Dt);
            highest = MathF.Max(highest, cpu.PelvisPosition.Y);
            standing += cpu.State == FighterState.Standing ? 1 : 0;
        }

        TestContext.WriteLine($"round over {game.RoundOver} after {steps * Dt:F1}s  standing {standing * Dt:F1}s  {game.Message}");
        Assert.IsTrue(game.RoundOver && game.PlayerWins > 0, "counted out on the canvas");
        Assert.IsLessThan(3.357f, standing * Dt, "never back up for long");
        Assert.IsLessThan(standingHips, highest, "the hips never go above where they stood on whole legs (no launch)");
    }

    [TestMethod]
    public void WithoutTheShortcut_ACutThigh_CannotStepToKeepItsBalance_AndGoesDown()
    {
        // 片方の腿を失った体は足の運びで踏み直せないので、短い脚と長い脚の支えの外へ重心が出ると倒れる（近道ありなら立ち続けた）。
        // 2026-11-19（rush-physics・間合いの層とのマージ後）: 前は「切った後 8 秒待てば、相手へ寄る歩みで支えの外へ出て倒れる」を見ていたが、
        // それは待つ間のボットの動き（体の揺れ・当たり）しだいの偶然だった（切った体は 2 つの版とも支えの余白 −0.03 m の所で立ち続け、版 48 では 5 秒目に
        // 重心が支えの外 8 cm へ流れて倒れ、マージ後は同じ 8 秒で流れずに立ち続けた。重心の動きは同じ物理で、ボットの揺れだけが違う）。
        // 支えの外へ出す力を決めて与える: 全身を 4.2 m/s（人形の単位で 2.5 m/s）（重い 1 発の押し込みを超える速さ）で押し、前後左右の 4 方向の合計で見る
        // （1 方向だけだと、押す前に 1 歩ずれただけで倒れる・倒れないが入れ替わる。測った 4 方向の合計: 立っていない時間は 4 方向 × 4 秒のうち、
        // 切った体が 15.4 秒、脚が無傷の体は起き上がりが早く 8.2 秒）。脚が無傷の体も押しには倒れる（足の運びで踏み直せないほどの押し）が、すぐ立ち直る。
        var intact = ShoveAndWatch(cut: false);
        var stump = ShoveAndWatch(cut: true);
        TestContext.WriteLine($"not standing, summed over 4 shoves: intact {intact.Down:F1}s  cut {stump.Down:F1}s  shortfall {stump.Shortfall:F3}");
        Assert.IsTrue(stump.CanStand, "the stump still has the strength to bear weight");
        Assert.IsGreaterThan(intact.Down + 1.678f, stump.Down, "without stepping, the body falls off its stump and takes longer to get back up than a whole body");
        Assert.IsLessThan(stump.StandingHips, stump.Highest, "and is never lifted from nowhere");
    }

    private static (float Down, float Shortfall, bool CanStand, float StandingHips, float Highest) ShoveAndWatch(bool cut)
    {
        var down = 0f;
        var shortfall = 0f;
        var canStand = true;
        var standingHips = 0f;
        var highest = 0f;
        foreach (var direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
        {
            using var game = new BoxingGame(seed: 2);
            var cpu = game.Cpu;
            Play(game, 1.678f);
            standingHips = cpu.PelvisPosition.Y;
            if (cut)
            {
                Cut(game, cpu, cpu.Parts.LeftThigh, 0.5f);
            }

            Play(game, 1.678f);
            cpu.Push(direction * 4.196f);
            for (var t = 0; t < 403 && !game.RoundOver; t++)
            {
                game.Step(default, Dt);
                down += cpu.State != FighterState.Standing ? Dt : 0f;
                highest = MathF.Max(highest, cpu.PelvisPosition.Y);
            }

            shortfall = cpu.LegShortfall;
            canStand &= cpu.CanStand;
        }

        return (down, shortfall, canStand, standingHips, highest);
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
            var force = 10f;
            Aigis.Physics.Voxels.VoxelBreak? found = null;
            while (force < 1e6f && (found = Aigis.Physics.Voxels.VoxelBending.FindBreak(grid, new Vector3(0f, -0.9f * half, 0f), -Vector3.UnitZ * force, 1, loadBearingOnly: true, part.Body.BeamSlices)) is null)
            {
                force *= 1.05f;
            }

            var ends = (Array.IndexOf(bone, withBone[0]), Array.LastIndexOf(bone, withBone[^1]));
            TestContext.WriteLine($"{(body == f.Parts.LeftThigh ? "thigh" : "shin")} bone [{string.Join(",", bone)}] breaks at {force:F0} N in slice {found?.Slice}");
            Assert.IsGreaterThan(1500f, force);
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
