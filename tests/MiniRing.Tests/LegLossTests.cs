using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 脚が折れた・切れた体は、残った脚の長さで立って（膝立ち・座り）戦い続ける（PhysicsVersion 39）。
/// 腰の高さは短い脚の端が床に着く高さまで下がり、長い脚はその分曲げる。倒れたままの 10 カウントにはならない。
/// </summary>
[TestClass]
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

        StrikeUntil(game, cpu, cpu.Parts.LeftShin, () => cpu.LegSevered);
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
        float Hips(Func<Fighter, int> part)
        {
            using var game = new BoxingGame(seed: 2);
            var cpu = game.Cpu;
            Play(game, 1f);
            StrikeUntil(game, cpu, part(cpu), () => cpu.LegSevered);
            Play(game, 8f);
            TestContext.WriteLine($"hips {cpu.PelvisPosition.Y:F3}  shortfall {cpu.LegShortfall:F3}  state {cpu.State}  cond {cpu.LegCondition(true):F2} thighStr {cpu.Strength(cpu.Parts.LeftThigh):F2} muscle {cpu.Voxels!.Muscle(cpu.Parts.LeftThigh):F2} support {cpu.LegSupportLength(true):F3}/{cpu.LegSupportLength(false):F3} thighPos {game.World.GetState(cpu.Parts.LeftThigh).Position} pelvis {cpu.PelvisPosition}");
            Assert.IsTrue(cpu.CanStand);
            return cpu.PelvisPosition.Y;
        }

        var shin = Hips(f => f.Parts.LeftShin);
        var thigh = Hips(f => f.Parts.LeftThigh);
        Assert.IsLessThan(shin, thigh, "the shorter the stump, the lower the body");
    }

    [TestMethod]
    public void TheStumpKeepsTheMuscleAboveTheCut()
    {
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        Play(game, 0.5f);
        StrikeUntil(game, cpu, cpu.Parts.LeftShin, () => cpu.LegSevered);
        // 切れ目の先（破片へ移ったスライス）を断面に数えると、切り株の筋肉は 0 近くになって立てなかった。
        Assert.IsGreaterThan(0.35f, cpu.LegCondition(true), $"stump condition {cpu.LegCondition(true):F2}");
    }

    [TestMethod]
    public void BothLegsGone_SitsOnThePelvis_AndIsNotCountedDown()
    {
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        Play(game, 0.5f);
        var full = cpu.LegSupportLength(true);
        StrikeUntil(game, cpu, cpu.Parts.LeftThigh, () => cpu.LegSupportLength(true) < 0.5f * full);
        StrikeUntil(game, cpu, cpu.Parts.RightThigh, () => cpu.LegSupportLength(false) < 0.5f * full);

        Play(game, 8f);
        TestContext.WriteLine($"support {cpu.LegSupportLength(true):F3}/{cpu.LegSupportLength(false):F3}  hover {cpu.HoverHeight:F3}  state {cpu.State}  up {cpu.Uprightness:F2}");
        Assert.IsTrue(cpu.CanStand, "sitting needs no leg strength");
        Assert.AreEqual(FighterState.Standing, cpu.State, "sitting up on the pelvis is not a knockdown");
        Assert.IsGreaterThan(0.6f, cpu.Uprightness, "the trunk is held upright");
    }

    public TestContext TestContext { get; set; } = null!;

    private static void Play(BoxingGame game, float seconds)
    {
        for (var i = 0; i < (int)(seconds / Dt); i++)
        {
            game.Step(default, Dt);
        }
    }

    private static void StrikeUntil(BoxingGame game, Fighter victim, int part, Func<bool> done)
    {
        var directions = new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitY, -Vector3.UnitY };
        for (var i = 0; i < 200 && !done(); i++)
        {
            var s = game.World.GetState(part);
            var from = directions[i % directions.Length];
            var point = s.Position + (Vector3.Transform(from, s.Orientation) * 0.08f);
            game.Strike(victim, part, point, s.Position - point, 25f);
        }

        Assert.IsTrue(done(), "the blows got through");
    }
}
