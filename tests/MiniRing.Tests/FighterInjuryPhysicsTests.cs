using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// ダメージによる性能低下と体の軸・力学が、ヘッドレスの物理で実際に起きることを確かめる。
/// </summary>
[TestClass]
public sealed class FighterInjuryPhysicsTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void InjuredShoulder_CannotRaiseThatArmAboveTheHead()
    {
        float GloveHeightAfterReachingUp(Action<Fighter> injure)
        {
            using var world = NewWorld();
            var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
            injure(fighter);
            var up = new Vector3(-0.1f, 0.6f, 0.3f) * fighter.ArmReach;
            Run(world, fighter, Idle(fighter) with { Right = new HandCommand(up, false) }, 1.5f);
            return fighter.GloveCenter(Hand.Right).Y - fighter.ShoulderCenter.Y;
        }

        var healthy = GloveHeightAfterReachingUp(_ => { });
        var injured = GloveHeightAfterReachingUp(f => f.Condition.ApplyHit(HitZone.Shoulder, Hand.Right, 40f));

        Assert.IsGreaterThan(0f, healthy, "a healthy arm gets the glove above the shoulder");
        Assert.IsLessThan(healthy - 0.05f, injured, $"healthy {healthy:0.000} injured {injured:0.000}");
    }

    [TestMethod]
    public void BrokenLegs_CannotStandOrGetUp()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        Run(world, fighter, Idle(fighter), 0.5f);
        fighter.Condition.ApplyHit(HitZone.Knee, Hand.Left, 40f);
        fighter.Condition.ApplyHit(HitZone.Knee, Hand.Left, 40f);

        Run(world, fighter, Idle(fighter), 4f);

        Assert.AreNotEqual(FighterState.Standing, fighter.State);
        Assert.IsGreaterThan(3f, fighter.DownTime, "the count keeps running");
    }

    [TestMethod]
    public void DazedFighter_ReactsLate()
    {
        float TimeToMoveGlove(Action<Fighter> injure)
        {
            using var world = NewWorld();
            var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
            Run(world, fighter, Idle(fighter), 1f);
            injure(fighter);
            var start = fighter.GloveCenter(Hand.Right);
            var punch = Idle(fighter) with { Right = new HandCommand(new Vector3(-0.05f, 0f, 0.95f) * fighter.ArmReach, false) };
            for (var i = 0; i < 240; i++)
            {
                fighter.ApplyControl(punch, Dt);
                world.Step(Dt);
                if (Vector3.Distance(fighter.GloveCenter(Hand.Right), start) > 0.02f)
                {
                    return i * Dt;
                }
            }

            return 1f;
        }

        var sharp = TimeToMoveGlove(_ => { });
        var dazed = TimeToMoveGlove(f => f.Condition.ApplyHit(HitZone.Chin, Hand.Left, 30f));

        Assert.IsGreaterThan(sharp + 0.1f, dazed, $"sharp {sharp:0.000}s dazed {dazed:0.000}s");
    }

    [TestMethod]
    public void HeartStun_FreezesTheBody_ItDoesNotReplayOldCommands()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var reach = fighter.ArmReach;
        var old = Idle(fighter) with { Left = new HandCommand(new Vector3(0.3f, 0.2f, 0.3f) * reach, false) };
        var current = Idle(fighter);
        Run(world, fighter, old, 0.5f);
        Run(world, fighter, current, 0.5f);

        fighter.Condition.ApplyHit(HitZone.Heart, Hand.Left, 30f);
        for (var i = 0; i < (int)(1.5f / Dt); i++)
        {
            fighter.ApplyControl(current, Dt);
            world.Step(Dt);
            Assert.AreNotEqual(old, fighter.LastCommand, $"replayed an old command at {i * Dt:0.000}s");
        }
    }

    [TestMethod]
    public void HandsDecideTheTorsoTwist()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var straight = Idle(fighter) with { Right = new HandCommand(new Vector3(-0.05f, 0f, 1f) * fighter.ArmReach, false) };

        Run(world, fighter, straight, 1f);

        // 右ストレートで右肩（−X）が前へ → 胸の前方は +X 側へ回る（+Y まわり正）。
        var chestForward = Vector3.Transform(Vector3.UnitZ, fighter.TorsoOrientation);
        Assert.IsGreaterThan(0.15f, chestForward.X, $"chest forward {chestForward}");
        Assert.AreEqual(FighterState.Standing, fighter.State);
    }

    [TestMethod]
    public void HeadPosition_LeansAndLowersTheBody()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        Run(world, fighter, Idle(fighter), 1f);
        var standing = fighter.PelvisPosition.Y;

        var reach = fighter.ArmReach;
        Run(world, fighter, Idle(fighter) with { HeadOffset = new Vector3(-0.35f, -0.4f, 0f) * reach }, 1.5f);

        Assert.IsLessThan(standing - 0.01f, fighter.PelvisPosition.Y, "ducking lowers the body");
        var up = Vector3.Transform(Vector3.UnitY, fighter.TorsoOrientation);
        Assert.IsLessThan(-0.08f, up.X, $"slipping right (−X) tilts the torso right: up {up}");
        Assert.AreEqual(FighterState.Standing, fighter.State);
    }

    [TestMethod]
    public void ExtendedStraight_CarriesMoreOfTheBodyThanAnArmSwatSideways()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var straight = Idle(fighter) with { Right = new HandCommand(new Vector3(0f, 0f, 1f) * fighter.ArmReach, false) };
        Run(world, fighter, straight, 1.2f);

        var forward = fighter.Frame.Forward;
        var throughTheBody = fighter.ImpactMass(Hand.Right, forward, out var straightShare);
        var swat = fighter.ImpactMass(Hand.Right, fighter.Frame.Right, out var swatShare);

        Assert.IsGreaterThan(swat * 1.5f, throughTheBody, $"straight {throughTheBody:0.00}kg ({straightShare:P0}) swat {swat:0.00}kg ({swatShare:P0})");
        Assert.IsGreaterThan(fighter.ArmMass(Hand.Right) * 3f, throughTheBody, "the straight is mostly body, not arm");
    }

    [TestMethod]
    public void InjuredShoulder_TurnsTheSamePunchIntoAnArmPunch()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        Run(world, fighter, Idle(fighter) with { Right = new HandCommand(new Vector3(0f, 0f, 1f) * fighter.ArmReach, false) }, 1.2f);
        var forward = fighter.Frame.Forward;
        var healthy = fighter.ImpactMass(Hand.Right, forward);

        fighter.Condition.ApplyHit(HitZone.Shoulder, Hand.Right, 40f);

        Assert.IsLessThan(healthy * 0.6f, fighter.ImpactMass(Hand.Right, forward));
    }

    private static PhysicsWorld NewWorld()
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        return world;
    }

    private static FighterCommand Idle(Fighter f) => new(Vector3.Zero, f.Frame.Forward,
        new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
        new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));

    private static void Run(PhysicsWorld world, Fighter fighter, FighterCommand command, float seconds)
    {
        for (var i = 0; i < (int)(seconds / Dt); i++)
        {
            fighter.ApplyControl(command, Dt);
            world.Step(Dt);
        }
    }
}
