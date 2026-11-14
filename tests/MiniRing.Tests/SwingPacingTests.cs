using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 技の時計の進め方（<see cref="SwingPacing"/>）: 体は目標をバネで追うので速い振りは目標から遅れ、遅れた剣が自分の体を通る。
/// 経路は変えず、体が付いてこられない間は技の時計を遅らせる（体が追えた分だけ振りが進む）。
/// </summary>
[TestClass]
public sealed class SwingPacingTests
{
    private const float Reach = 0.17f;
    private const float Dt = 1f / 60f;

    private static readonly MotionFrame Guard = new(new Vector3(-0.2f, -0.57f, 0.78f), new Vector3(0.03f, -0.86f, 0.5f), Vector3.Normalize(new Vector3(0f, 0.6f, 0.8f)),
        Vector3.Normalize(new Vector3(0f, -0.8f, 0.6f)), Vector3.Zero, 0f, Vector3.Zero);

    private static readonly MotionTechnique Men = new("men", WeaponKind.Katana, SwordForms.Katana()[0], 0.45f);

    private static FighterSense Sense(WeaponSense weapon)
    {
        var at = RingLayout.Boxing.Center with { Y = 1f };
        var chest = at + new Vector3(0f, 0f, 0.45f);
        return new(new BodyFrame(at, 0f), Reach, chest with { Y = 1.1f }, chest, OpponentAlive: true,
            OpponentLeft: new GloveSense(chest + new Vector3(0.03f, 0.02f, -0.08f), Vector3.Zero),
            OpponentRight: new GloveSense(chest + new Vector3(-0.03f, 0.02f, -0.08f), Vector3.Zero),
            OpponentBody: new BodySense(chest + new Vector3(0.05f, 0.03f, 0f), chest + new Vector3(-0.05f, 0.03f, 0f), chest - new Vector3(0f, 0.15f, 0f)),
            SelfArmed: true, SelfWeapon: weapon);
    }

    /// <summary>技を出し始めてから出し終えるまでの読みの回数。</summary>
    private static int Steps(SwingPacing? pacing, WeaponSense weapon)
    {
        var bot = new SwordsmanBot([Men], Guard, seed: 1, rate: 1.77f, exact: true) { Pacing = pacing };
        var sense = Sense(weapon);
        for (var i = 0; i < 20 && bot.Current is null; i++)
        {
            _ = bot.Read(sense, Dt);
        }

        Assert.IsNotNull(bot.Current, "the swordsman starts the technique");
        var steps = 0;
        while (bot.Current is not null && steps < 2000)
        {
            _ = bot.Read(sense, Dt);
            steps++;
        }

        return steps;
    }

    [TestMethod]
    public void TheClockRunsAtFullSpeed_WhenTheBodyIsOnTarget_AndHalfSpeedAtTheSlack()
    {
        var pacing = new SwingPacing(GripSlack: 0.25f, AngleSlack: 0.6f, Floor: 0.15f);
        Assert.AreEqual(1f, pacing.Gate(0f, 0f), 1e-6f);
        Assert.AreEqual(0.5f, pacing.Gate(0.25f, 0f), 1e-6f);
        Assert.AreEqual(0.5f, pacing.Gate(0f, 0.6f), 1e-6f);
    }

    [TestMethod]
    public void TheClock_SlowsAsTheBodyFallsBehind_ButNeverStops()
    {
        var pacing = new SwingPacing();
        var last = 1f;
        foreach (var error in new[] { 0f, 0.1f, 0.25f, 0.5f, 1f, 3f })
        {
            var gate = pacing.Gate(error, 0f);
            Assert.IsLessThanOrEqualTo(last, gate, "the farther behind, the slower");
            last = gate;
        }

        Assert.AreEqual(pacing.Floor, pacing.Gate(100f, 100f), 1e-6f, "the swing is never held still");
    }

    [TestMethod]
    public void ABodyFarFromItsTarget_FinishesTheTechniqueLater_WithTheSamePath()
    {
        // 握りが体から遠く離れた所にある（目標に付いてきていない）剣。
        var lagging = new WeaponSense(true, 0.4f, 0.4f, new Vector3(0.6f, 1.6f, 0.2f), Vector3.UnitY);
        var free = Steps(null, lagging);
        var paced = Steps(new SwingPacing(), lagging);
        Assert.IsGreaterThan(free * 1.5f, paced, "the clock waits for the body");
    }

    [TestMethod]
    public void WithoutASwordInHand_PacingChangesNothing()
    {
        Assert.AreEqual(Steps(null, default), Steps(new SwingPacing(), default));
    }
}
