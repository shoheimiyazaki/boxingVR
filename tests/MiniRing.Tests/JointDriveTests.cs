using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

[TestClass]
public sealed class JointDriveTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    [DataRow(240)]
    [DataRow(480)]
    [DataRow(960)]
    [DataRow(240, true)]
    public void InternalDrivePreservesMomentumWithoutContacts(int hz, bool hybridAboveFloor = false)
    {
        var dt = 1f / hz;
        using var world = new PhysicsWorld(Vector3.Zero, conserveAngularMomentum: true);
        if (hybridAboveFloor)
        {
            world.AddStaticBox(new Vector3(0f, -1.5f, 0f), new Vector3(7f, 1f, 7f));
        }

        var tuning = hybridAboveFloor ? FighterTuning.HybridPunch : FighterTuning.MomentumPunch;
        var fighter = Fighter.Spawn(world, tuning, "air", Vector3.Zero, 0f);
        foreach (var id in fighter.AllParts)
        {
            world.SetDamping(id, 0f, 0f);
        }

        var input = Command(fighter, rightReach: 1.4f) with
        {
            MoveWorld = hybridAboveFloor ? Vector3.UnitZ : Vector3.Zero,
        };
        for (var i = 0; i < hz / 10; i++)
        {
            fighter.ApplyControl(input, dt);
            world.Step(dt);
        }

        var p = Vector3.Zero;
        var l = Vector3.Zero;
        var angularActivity = 0f;
        foreach (var id in fighter.AllParts)
        {
            var body = world.GetState(id);
            var linear = world.GetMass(id) * body.LinearVelocity;
            var spin = Vector3.Transform(body.AngularVelocity, Quaternion.Conjugate(body.Orientation));
            p += linear;
            var angular = Vector3.Cross(body.Position, linear)
                + Vector3.Transform(world.GetLocalInertiaDiagonal(id) * spin, body.Orientation);
            l += angular;
            angularActivity += angular.Length();
        }

        Console.WriteLine(FormattableString.Invariant($"net P={p.Length():G6} kg m/s; net L={l.Length():G6} kg m²/s; sum |L|={angularActivity:G6} kg m²/s; fist={fighter.GloveVelocity(Hand.Right).Length():G6} m/s"));
        Assert.IsTrue(fighter.GloveVelocity(Hand.Right).Length() > 0.25f);
        Assert.IsTrue(p.Length() < 0.005f);
        Assert.IsTrue(l.Length() < 0.015f * angularActivity);
    }

    [TestMethod]
    public void JointDriveSupportsTheInitialGuard()
    {
        using var world = new PhysicsWorld(conserveAngularMomentum: true);
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        var fighter = Fighter.Spawn(world, FighterTuning.MomentumPunch, "floor", Vector3.Zero, 0f);
        var input = Command(fighter, rightReach: 0.48f);
        for (var i = 0; i < 120; i++)
        {
            fighter.ApplyControl(input, Dt);
            world.Step(Dt);
        }

        Console.WriteLine(FormattableString.Invariant($"floor state={fighter.State}, pelvis={fighter.PelvisPosition}, head={fighter.HeadPosition.Y:G6}"));
        Assert.AreEqual(FighterState.Standing, fighter.State);
        Assert.IsTrue(fighter.PelvisPosition.Y > 0.55f);
    }

    [TestMethod]
    public void JointBalanceControlExtendsTheInitialGuard()
    {
        var candidate = FighterTuning.MomentumPunch;
        var baseline = candidate with { JointStanceHipShare = 0f, JointStanceAnkleGain = 0f, Footwork = candidate.Footwork with { CapturePlacementShare = 0f } };
        Assert.IsGreaterThan(Hold(baseline), Hold(candidate));

        static int Hold(FighterTuning tuning)
        {
            using var world = new PhysicsWorld(conserveAngularMomentum: true);
            world.AddStaticBox(new Vector3(0f, -1f, 0f), new Vector3(7f, 1f, 7f));
            var fighter = Fighter.Spawn(world, tuning, "grounded hip", Vector3.Zero, 0f);
            var input = Command(fighter, rightReach: 0.48f);
            var steps = 0;
            for (; steps < 480 && fighter.State == FighterState.Standing; steps++)
            {
                fighter.ApplyControl(input, Dt);
                world.Step(Dt);
            }

            return steps;
        }
    }

    [TestMethod]
    public void HybridGuardCanStayUprightWithoutAnOpponent()
    {
        const int seconds = 6;
        using var world = new PhysicsWorld(conserveAngularMomentum: true);
        world.AddStaticBox(new Vector3(0f, -1f, 0f), new Vector3(7f, 1f, 7f));
        var fighter = Fighter.Spawn(world, FighterTuning.HybridPunch, "hybrid guard", Vector3.Zero, 0f);
        var input = Command(fighter, rightReach: 0.48f);
        for (var i = 0; i < seconds * 240; i++)
        {
            fighter.ApplyControl(input, Dt);
            world.Step(Dt);
            Assert.AreEqual(FighterState.Standing, fighter.State);
        }
    }

    [TestMethod]
    public void FloorContactExchangesMomentumDuringShortPunch()
    {
        using var world = new PhysicsWorld(conserveAngularMomentum: true);
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        var fighter = Fighter.Spawn(world, FighterTuning.MomentumPunch, "punch", Vector3.Zero, 0f);
        foreach (var id in fighter.AllParts)
        {
            world.SetDamping(id, 0f, 0f);
        }

        for (var i = 0; i < 120; i++)
        {
            fighter.ApplyControl(Command(fighter, 0.48f), Dt);
            world.Step(Dt);
        }

        var before = MomentumNow();
        var peak = 0f;
        var footContact = false;
        for (var i = 1; i <= 36; i++)
        {
            fighter.ApplyControl(Command(fighter, 0.48f + (i / 36f * 1.1f)), Dt);
            world.Step(Dt);
            peak = MathF.Max(peak, fighter.GloveVelocity(Hand.Right).Z);
            footContact |= world.Contacts().Any(contact =>
                contact.StaticA != contact.StaticB
                && fighter.KindOf(contact.StaticA ? contact.B : contact.A) is BodyPartKind.Foot or BodyPartKind.Toe);
        }

        var floorImpulse = MomentumNow().Z - before.Z;
        Console.WriteLine(FormattableString.Invariant($"fist forward={peak:F3} m/s, change in forward momentum={floorImpulse:F3} N s, foot contact={footContact}, state={fighter.State}"));
        Assert.IsTrue(footContact);
        Assert.IsTrue(peak > 1f);
        Assert.IsTrue(MathF.Abs(floorImpulse) > 0.25f);
        Assert.AreEqual(FighterState.Standing, fighter.State);
        return;

        Vector3 MomentumNow()
        {
            var momentum = Vector3.Zero;
            foreach (var id in fighter.AllParts)
            {
                momentum += world.GetMass(id) * world.GetState(id).LinearVelocity;
            }

            return momentum;
        }
    }

    [TestMethod]
    public void TorsoRotationMovesPassiveArmWithoutAnArmMotor()
    {
        using var world = new PhysicsWorld(Vector3.Zero, conserveAngularMomentum: true);
        var (parts, _, _) = HumanoidRagdoll.BuildDetachable(world, Vector3.UnitY, "passive", splitTorso: true);
        foreach (var id in parts.All)
        {
            world.SetDamping(id, 0f, 0f);
        }

        world.ApplyAngularImpulse(parts.Pelvis, -Vector3.UnitY);
        world.ApplyAngularImpulse(parts.Abdomen!.Value, Vector3.UnitY);
        // Track kinetic-energy changes of the distal groups, including their own passive
        // constraint work and losses. These are net changes, rather than joint power.
        var rightArm = new[] { parts.LeftUpperArm, parts.LeftForearm };
        var chestAndArms = new[]
        {
            parts.Chest, parts.Head, parts.LeftUpperArm, parts.LeftForearm, parts.RightUpperArm, parts.RightForearm,
        };
        var groups = new[] { new[] { parts.Abdomen.Value }.Concat(chestAndArms).ToArray(), chestAndArms, rightArm, new[] { parts.LeftForearm } };
        var initial = groups.Select(group => group.Sum(KineticEnergy)).ToArray();
        var maximum = new float[groups.Length];
        var final = new float[groups.Length];
        for (var i = 0; i < 60; i++)
        {
            world.Step(Dt);
            for (var j = 0; j < groups.Length; j++)
            {
                final[j] = groups[j].Sum(KineticEnergy) - initial[j];
                maximum[j] = MathF.Max(maximum[j], final[j]);
            }
        }

        Console.WriteLine(FormattableString.Invariant(
            $"distal kinetic-energy change at 0.25 s (waist / chest / shoulder / elbow): {final[0]:F5} / {final[1]:F5} / {final[2]:F5} / {final[3]:F5} J"));
        Console.WriteLine(FormattableString.Invariant(
            $"peak distal kinetic-energy gain (chest / shoulder / elbow): {maximum[1]:F5} / {maximum[2]:F5} / {maximum[3]:F5} J"));
        Assert.IsTrue(maximum[2] > 0.001f);
        Assert.IsTrue(maximum[3] > 0.001f);
        return;

        float KineticEnergy(int id)
        {
            var part = world.GetState(id);
            var spin = Vector3.Transform(part.AngularVelocity, Quaternion.Conjugate(part.Orientation));
            return 0.5f * (world.GetMass(id) * part.LinearVelocity.LengthSquared()
                + Vector3.Dot(spin, world.GetLocalInertiaDiagonal(id) * spin));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void JointDriveHitWithoutWornContactDoesNotAddASyntheticBodyImpulse(bool hybrid)
    {
        using var game = new BoxingGame(hybrid ? FighterTuning.HybridPunch : FighterTuning.MomentumPunch);
        var point = game.Cpu.ChestPosition;
        var before = game.World.SnapshotDynamics().ToDictionary(body => body.Id);
        var hit = new HitEvent(game.Player, Hand.Right, game.Cpu, HitZone.Body, point,
            Speed: 3f, ImpactMass: 12f, BodyShare: 1f, Damage: 1f);
        game.ApplyHit(hit, game.Cpu.Parts.Chest, Vector3.UnitZ, carve: false);

        foreach (var id in game.Player.AllParts.Concat(game.Cpu.AllParts))
        {
            var actual = game.World.GetState(id);
            Assert.AreEqual(before[id].LinearVelocity, actual.LinearVelocity);
            Assert.AreEqual(before[id].AngularVelocity, actual.AngularVelocity);
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void MeasureJointStanceTuning()
    {
        var heights = new[] { 0.80f, 0.86f, 0.91f, 0.97f };
        var servoRates = new[] { 12f, 18f, 22f, 30f };
        foreach (var height in heights)
        {
            foreach (var servo in servoRates)
            {
                using var world = new PhysicsWorld(conserveAngularMomentum: true);
                world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
                var tuning = FighterTuning.MomentumPunch with { StandHeightRatio = height, LegServoHz = servo };
                var fighter = Fighter.Spawn(world, tuning, "stance", Vector3.Zero, 0f);
                var guard = Command(fighter, 0.48f);
                var fallAt = 0f;
                for (var i = 0; i < 720 && fighter.State == FighterState.Standing; i++)
                {
                    fighter.ApplyControl(guard, Dt);
                    world.Step(Dt);
                    fallAt += Dt;
                }

                Console.WriteLine(FormattableString.Invariant(
                    $"stance height={height:F2} servo={servo:F0} Hz standing={fallAt:F3} s pelvis={fighter.PelvisPosition}"));
            }
        }
    }

    private static FighterCommand Command(Fighter fighter, float rightReach) =>
        new(Vector3.Zero, Vector3.UnitZ,
            new HandCommand(new Vector3(0.15f, 0.08f, 0.24f), false),
            new HandCommand(new Vector3(-0.15f, 0.08f, fighter.ArmReach * rightReach), false));
}
