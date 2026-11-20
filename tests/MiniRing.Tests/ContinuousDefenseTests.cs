using System.Numerics;
using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>受け・そらし・流しが同じ幾何のまま連続に変わることと、返しの安全を数値で確かめる。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class ContinuousDefenseTests
{
    private const float Reach = 0.4789f;

    private static FighterSense Sense(BladeSense blade = default, Vector3 hiltVelocity = default) =>
        new(new BodyFrame(Vector3.Zero, 0f), Reach, new Vector3(0f, 0.2817f, 0.9859f), new Vector3(0f, 0f, 0.9859f), OpponentAlive: true,
            SelfHead: new Vector3(0f, 0.2254f, 0f), OpponentBlade: blade, SelfArmed: true, Ring: RingLayout.Weapons,
            OpponentRight: new GloveSense(blade.Hilt, hiltVelocity),
            OpponentBody: new BodySense(new Vector3(0.1408f, 0.1408f, 0.8451f), new Vector3(-0.1408f, 0.1408f, 0.8451f), new Vector3(0f, -0.4225f, 0.9859f)));

    private static WeaponThreat Threat(float speed, float time = 0.2518f)
    {
        var hilt = new Vector3(0f, 0.7042f, 0.6197f);
        var tip = new Vector3(0f, 0.8451f, 0.1408f);
        var velocity = Vector3.Normalize(new Vector3(0f, -3f, -0.5f)) * (speed * Reach);
        var line = new WeaponLine(hilt, tip, velocity, velocity, hilt, Reach * WeaponDefense.ShoulderReach, default);
        return new WeaponThreat(tip, velocity, time, new Vector3(0f, 0.2254f, 0f), 1f, line);
    }

    [TestMethod]
    public void FasterBlows_YieldMonotonicallyAndContinuously_WithoutChangingGeometry()
    {
        var defense = SwordDefense.Continuous;
        var sense = Sense();
        var previous = WeaponDefense.Response(sense, Threat(0f), defense);
        Assert.AreEqual(defense.Deflect, previous.Angle);
        Assert.AreEqual(defense.Drive, previous.Drive);
        for (var i = 1; i <= 400; i++)
        {
            var response = WeaponDefense.Response(sense, Threat(i * 0.05958f), defense);
            Assert.IsTrue(response.Angle > previous.Angle);
            Assert.IsTrue(response.Drive < previous.Drive);
            Assert.IsTrue(response.Yield > previous.Yield);
            Assert.IsTrue(response.Angle - previous.Angle < 0.02f, "速度を少し変えただけで別の受けへ跳ばない");
            previous = response;
        }

        Assert.IsTrue(previous.YieldShare > 0.9f, "速い打ちは止めようとせず大きく流す");
        var urgent = WeaponDefense.Response(sense, Threat(7.15f, 0f), defense);
        var early = WeaponDefense.Response(sense, Threat(7.15f, defense.Horizon), defense);
        Assert.IsTrue(urgent.YieldShare > early.YieldShare);
        Assert.IsTrue(urgent.StepShare > early.StepShare, "間に合わないほど足と頭にも寄せる");
    }

    [TestMethod]
    public void OverheadBlow_IsMetByANearlyHorizontalBlade_AboveTheHead()
    {
        var hilt = new Vector3(0f, 0.7042f, 0.6197f);
        var tip = new Vector3(0f, 0.8451f, 0.1408f);
        var sense = Sense(new BladeSense(true, hilt, tip, new Vector3(0f, -5.035f, -0.8392f)), new Vector3(0f, -1.678f, 0f));
        var defense = SwordDefense.Continuous;
        var threat = WeaponDefense.Read(sense, WeaponDefense.Lines(sense, false), defense)!.Value;
        var frame = WeaponDefense.Parry(sense, threat, defense, 1.7f, SwordForms.KatanaSpacing, Vector3.UnitZ);
        var contact = frame.Right + (frame.Blade * (defense.Forte * 1.7f));
        Assert.IsTrue(MathF.Abs(frame.Blade.Y) < 0.25f, "上からという種類の分岐を使わず、幾何から刀が水平になる");
        Assert.IsTrue(contact.Y * Reach > sense.SelfHead.Y, "迎える所は頭の上");
    }

    [TestMethod]
    public void AThrust_IsPushedSideways_AtBothLowAndHighLoad()
    {
        var hilt = new Vector3(-0.1408f, 0f, 0.8451f);
        var tip = new Vector3(-0.1408f, 0f, 0.338f);
        foreach (var speed in new[] { 1.192f, 17.87f })
        {
            var velocity = new Vector3(0f, 0f, -speed * Reach);
            var sense = Sense(new BladeSense(true, hilt, tip, velocity), velocity);
            var line = WeaponDefense.Lines(sense, false)[0];
            var threat = new WeaponThreat(tip, velocity, 0.1678f, Vector3.Zero, 1f, line);
            var frame = WeaponDefense.Parry(sense, threat, SwordDefense.Continuous, 1.7f, SwordForms.KatanaSpacing, Vector3.UnitZ);
            Assert.IsTrue(frame.Edge.X < -0.9f, "突きは体の外側へ横に押す");
            Assert.IsTrue(MathF.Abs(frame.Edge.Z) < 0.01f);
        }
    }

    [TestMethod]
    public void Counter_WaitsForTheBladeToPointAndMoveAway_WithTheWholeLineClear()
    {
        var away = Vector3.UnitX * 1.678f;
        var blade = new BladeSense(true, new Vector3(0.507f, 0f, 0.2817f), new Vector3(0.9859f, 0f, 0.4225f), away);
        var sense = Sense(blade, away);
        Assert.IsTrue(WeaponDefense.CounterOpening(sense, SwordDefense.Continuous));
        Assert.IsFalse(WeaponDefense.CounterOpening(sense with { OpponentBlade = blade with { TipVelocity = -away } }, SwordDefense.Continuous));
        Assert.IsFalse(WeaponDefense.CounterOpening(Sense(blade with { Hilt = new Vector3(0.05634f, 0f, 0.02817f) }, away), SwordDefense.Continuous), "剣先だけ外れても鍔元が体の線に残る間は返さない");
    }

    [TestMethod]
    public void ContinuousGuard_ProducesTheSameCommands_ForTheSameSeedAndObservations()
    {
        var motion = SwordForms.Katana()[0];
        SwordsmanBot Make() => new([new MotionTechnique(motion.Name, WeaponKind.Katana, motion, 1.268f)], motion.Frames[0], 31, 1.77f,
            defense: SwordDefense.Continuous) { CounterOnly = true };
        var a = Make();
        var b = Make();
        var hilt = new Vector3(0f, 0.7042f, 0.6197f);
        var sense = Sense(new BladeSense(true, hilt, new Vector3(0f, 0.8451f, 0.1408f), new Vector3(0f, -5.035f, -0.8392f)), new Vector3(0f, -1.678f, 0f));
        _ = a.Read(sense, 1f / 240f);
        _ = b.Read(sense, 1f / 240f);
        Assert.IsTrue(a.Blocking);
        for (var i = 0; i < 403; i++)
        {
            Assert.AreEqual(a.Read(i < 50 ? sense : Sense(), 1f / 240f), b.Read(i < 50 ? sense : Sense(), 1f / 240f));
        }
    }

    [TestMethod]
    public void AnInterruptedCounterPlan_IsCancelled_InsteadOfAttackingWithoutAnOpening()
    {
        var motion = SwordForms.Katana()[0];
        var bot = new SwordsmanBot([new MotionTechnique(motion.Name, WeaponKind.Katana, motion, 0.2817f)], motion.Frames[0], 31, 1.77f,
            defense: SwordDefense.Continuous) { CounterOnly = true };
        var hilt = new Vector3(0f, 0.7042f, 0.6197f);
        var incoming = Sense(new BladeSense(true, hilt, new Vector3(0f, 0.8451f, 0.1408f), new Vector3(0f, -5.035f, -0.8392f)), new Vector3(0f, -1.678f, 0f));
        var opening = Sense(new BladeSense(true, new Vector3(0.507f, 0f, 0.2817f), new Vector3(0.9859f, 0f, 0.4225f), Vector3.UnitX * 1.678f), Vector3.UnitX * 1.678f);
        _ = bot.Read(incoming, 1f / 240f);
        Assert.IsTrue(bot.Blocking);
        Assert.AreNotEqual(Vector3.Zero, bot.Read(opening, 1f / 240f).MoveWorld, "返しの間合いを詰める");
        _ = bot.Read(incoming, 1f / 240f);
        var closed = Sense(new BladeSense(true, new Vector3(0f, 0f, 0.8451f), new Vector3(0f, 0f, 0.338f), Vector3.Zero));
        var command = bot.Read(closed, SwordDefense.Continuous.HoldTime + 0.01678f);
        Assert.AreEqual(Vector3.Zero, command.MoveWorld, "新しい打ちで隙が開かなければ、古い返しの計画で歩き出さない");
        Assert.AreEqual(0, bot.Counters);
    }

    [TestMethod]
    public void ACounterPlan_Expires_WhenItsRangeCannotBeReached()
    {
        var motion = SwordForms.Katana()[0];
        var bot = new SwordsmanBot([new MotionTechnique(motion.Name, WeaponKind.Katana, motion, 0.2817f)], motion.Frames[0], 31, 1.77f,
            defense: SwordDefense.Continuous) { CounterOnly = true };
        var hilt = new Vector3(0f, 0.7042f, 0.6197f);
        _ = bot.Read(Sense(new BladeSense(true, hilt, new Vector3(0f, 0.8451f, 0.1408f), new Vector3(0f, -5.035f, -0.8392f)), new Vector3(0f, -1.678f, 0f)), 1f / 240f);
        var opening = Sense(new BladeSense(true, new Vector3(0.507f, 0f, 0.2817f), new Vector3(0.9859f, 0f, 0.4225f), Vector3.UnitX * 1.678f), Vector3.UnitX * 1.678f);
        _ = bot.Read(opening, 1f / 240f);
        Assert.AreEqual(Vector3.Zero, bot.Read(opening, SwordsmanBot.PlanPatience + 0.01678f).MoveWorld);
        Assert.AreEqual(0, bot.Counters);
    }

    [TestMethod]
    public void AcceleratingWithinOneBlow_UpdatesTheHandTarget_WithoutStartingAnotherBlock()
    {
        var motion = SwordForms.Katana()[0];
        var bot = new SwordsmanBot([new MotionTechnique(motion.Name, WeaponKind.Katana, motion, 1.268f)], motion.Frames[0], 31, 1.77f,
            defense: SwordDefense.Continuous) { Passive = true };
        var hilt = new Vector3(0f, 0.7042f, 0.6197f);
        var incoming = Sense(new BladeSense(true, hilt, new Vector3(0f, 0.8451f, 0.1408f), new Vector3(0f, -5.035f, -0.8392f)), new Vector3(0f, -1.678f, 0f));
        var first = bot.Read(incoming, 1f / 240f);
        Assert.IsTrue(bot.Blocking);
        var faster = incoming with
        {
            OpponentBlade = incoming.OpponentBlade with { TipVelocity = incoming.OpponentBlade.TipVelocity * 2f },
            OpponentRight = incoming.OpponentRight with { Velocity = incoming.OpponentRight.Velocity * 2f },
        };
        var second = bot.Read(faster, 1f / 240f);
        Assert.AreEqual(1, bot.Blocks);
        Assert.IsTrue(Vector3.Distance(first.Right.TargetLocal, second.Right.TargetLocal) > 0.002817f, "同じ打ちでも加速に応じて押し込み・引きを追う");
    }

    [TestMethod]
    public void GuardTrial_RepeatsExactly_ForTheSameSeed()
    {
        var forms = SwordForms.Katana();
        var technique = new MotionTechnique(forms[0].Name, WeaponKind.Katana, forms[0], 1.127f);
        var library = new SwordLibraryLab.Library(new() { [WeaponKind.Katana] = forms[0].Frames[0] }, [technique]);
        var attack = new GuardLab.Attack("突き", false, WeaponKind.Katana, true,
            seed => new SwordsmanBot([technique], forms[0].Frames[0], seed, 1.77f, rest: 1000f));
        var variant = GuardLab.Comparison[^1];
        Assert.AreEqual(GuardLab.Run(attack, library, variant, 31, 3.357f), GuardLab.Run(attack, library, variant, 31, 3.357f));
    }
}
