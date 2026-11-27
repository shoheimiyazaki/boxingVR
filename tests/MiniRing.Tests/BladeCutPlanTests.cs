using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("slow")]
public sealed class BladeCutPlanTests
{
    private static readonly BladeArm Right = new(new Vector3(-0.2f, 0, 0), 0.05f, 0.75f);
    private static readonly BladeArm Left = new(new Vector3(0.2f, 0, 0), 0.05f, 0.75f);

    [TestMethod]
    [DataRow(WeaponKind.Katana, Hand.Right, true)]
    [DataRow(WeaponKind.Longsword, Hand.Right, true)]
    [DataRow(WeaponKind.IronSword, Hand.Right, false)]
    [DataRow(WeaponKind.SickleClaw, Hand.Right, false)]
    [DataRow(WeaponKind.SickleClaw, Hand.Left, false)]
    public void EachWeaponCrossesTheRequestedPointWithItsEdgeLeading(WeaponKind kind, Hand hand, bool twoHanded)
    {
        Assert.IsTrue(BladeCutGeometry.TryFromWeapon(kind, hand, twoHanded, out var blade));
        var point = new Vector3(0, 0, 0.42f + blade.ContactOffset.Length());
        var main = hand == Hand.Right ? Right : Left;
        var across = -hand.LocalSide();
        var start = new BladeKeyPose(new Vector3(0.22f * hand.LocalSide(), 0, 0.34f), Quaternion.Identity);
        foreach (var travel in new[] { across * Vector3.UnitX, -Vector3.UnitY, Vector3.Normalize(new Vector3(across, -1, 0)) })
        {
            var request = new BladeCutRequest(point, travel, Vector3.Cross(Vector3.UnitZ, travel));
            Assert.IsTrue(BladeCutPlan.TryCreate(request, blade, start, main, twoHanded ? Left : null, out var plan),
                $"{kind}, {travel}");
            Near(point, plan.Contact.Point(blade.ContactOffset));
            var before = plan.Sample(plan.ContactTime - 0.001f).Point(blade.ContactOffset);
            var after = plan.Sample(plan.ContactTime + 0.001f).Point(blade.ContactOffset);
            Assert.IsTrue(Vector3.Dot(travel, Vector3.Normalize(after - before)) > 0.999f);
            var beforeHalf = (plan.PreparationSeconds + plan.ContactTime) / 2f;
            var afterHalf = (plan.ContactTime + plan.Duration) / 2f;
            float DistanceAcross(float seconds) => Vector3.Distance(
                plan.Sample(seconds - 0.001f).Point(blade.ContactOffset),
                plan.Sample(seconds + 0.001f).Point(blade.ContactOffset));
            var contactDistance = Vector3.Distance(before, after);
            Assert.IsTrue(contactDistance > DistanceAcross(beforeHalf) * 1.9f);
            Assert.IsTrue(contactDistance > DistanceAcross(afterHalf) * 1.9f);
            var chord = Vector3.Normalize(blade.Chord);
            var edge = Vector3.Normalize(blade.Edge - chord * Vector3.Dot(blade.Edge, chord));
            Assert.IsTrue(Vector3.Dot(travel, Vector3.Transform(edge, plan.Contact.Orientation)) > 0.999f);
            for (var i = 0; i <= 30; i++)
            {
                var pose = plan.Sample(plan.PreparationSeconds + i * (plan.Duration - plan.PreparationSeconds) / 30);
                var radial = Vector3.Normalize(pose.Point(blade.ContactOffset) - plan.ArcCenter);
                Near(Vector3.Cross(request.PlaneNormal, radial), Vector3.Transform(edge, pose.Orientation));
            }
            Near(plan.Windup.Grip, plan.Sample(plan.PreparationSeconds).Grip);
            Near(plan.FollowThrough.Grip, plan.Sample(plan.Duration).Grip);
        }
    }

    [TestMethod]
    public void CommandsKeepTheShieldAndPutTheSupportHandOnTheSameHandle()
    {
        var basis = new FighterCommand(Vector3.UnitX, Vector3.UnitZ, new HandCommand(Vector3.One, true), default);
        var anchor = new BodyFrame(new Vector3(1, 2, 3), 0.8f);
        var live = new BodyFrame(new Vector3(2, 1, 3), 0.4f);
        var start = new BladeKeyPose(new Vector3(-0.22f, 0, 0.34f), Quaternion.Identity);
        foreach (var kind in new[] { WeaponKind.IronSword, WeaponKind.Katana })
        {
            Assert.IsTrue(BladeCutGeometry.TryFromWeapon(kind, Hand.Right, kind == WeaponKind.Katana, out var blade));
            var point = new Vector3(0, 0, 0.4f + blade.ContactOffset.Length());
            Assert.IsTrue(BladeCutPlan.TryCreate(new BladeCutRequest(point, Vector3.UnitX, Vector3.UnitY),
                blade, start, Right, Left, out var plan));
            var command = plan.ToCommand(plan.ContactTime, basis, anchor, live);
            Near(anchor.LocalToWorld(plan.Contact.Grip), live.LocalToWorld(command.Right.TargetLocal));
            Near(anchor.Rotate(Vector3.Transform(blade.Edge, plan.Contact.Orientation)),
                live.Rotate(Vector3.Transform(blade.Edge, command.Right.Orientation!.Value)));
            if (blade.SupportOffset is { } offset)
            {
                Near(anchor.LocalToWorld(plan.Contact.Point(offset)), live.LocalToWorld(command.Left.TargetLocal));
            }
            else
            {
                Assert.AreEqual(basis.Left, command.Left);
            }
        }
    }

    [TestMethod]
    public void RejectsInvalidAndUnreachableInputs()
    {
        Assert.IsTrue(BladeCutGeometry.TryFromWeapon(WeaponKind.IronSword, Hand.Right, false, out var blade));
        var start = new BladeKeyPose(new Vector3(-0.22f, 0, 0.34f), Quaternion.Identity);
        Assert.IsFalse(BladeCutPlan.TryCreate(new BladeCutRequest(new Vector3(0, 0, 3), Vector3.UnitX, Vector3.UnitY),
            blade, start, Right, null, out _));
        Assert.IsFalse(BladeCutPlan.TryCreate(new BladeCutRequest(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitX),
            blade, start, Right, null, out _));
    }

    [TestMethod]
    [DataRow(WeaponKind.Katana, 0.83f)]
    [DataRow(WeaponKind.Longsword, 0.92f)]
    [DataRow(WeaponKind.IronSword, 0.81f)]
    [DataRow(WeaponKind.SickleClaw, 0.88f)]
    public void PlaysThroughThePhysicalGame(WeaponKind kind, float gap)
    {
        const float dt = 1f / 240f;
        var claw = kind == WeaponKind.SickleClaw;
        var galva = CharacterRoster.Get("galva");
        var corners = claw
            ? BotCatalog.Corners("boxer") with
            {
                PlayerBody = galva.Build, PlayerWear = galva.Wear, PlayerLook = galva.Look, PlayerFaceSpec = galva.Face,
                Ring = RingLayout.Boxing with { StartGap = gap, FromCorners = false }, Referee = false, ExternalBlue = true,
            }
            : SwordTraining.Corners(kind, gap, kind == WeaponKind.IronSword ? HandWear.Shield : HandWear.Glove,
                opponent: SwordTraining.Defenseless);
        using var game = new BoxingGame(corners: corners);
        var guard = new GuardHold(kind);
        FighterCommand Basis(in FighterSense sense) => claw
            ? new(Vector3.Zero, sense.Self.Forward, ClawStroke.Guard(Hand.Left, sense.ArmReach).ToCommand(Hand.Left),
                ClawStroke.Guard(Hand.Right, sense.ArmReach).ToCommand(Hand.Right))
            : guard.Read(sense, dt);
        for (var i = 0; i < 180; i++)
        {
            game.Step(new BoxingInput(Basis(game.PlayerSense)), dt);
        }

        var initial = game.PlayerSense;
        Assert.IsTrue(initial.BladeGrip(Hand.Right).HasValue);
        Assert.AreEqual(kind is WeaponKind.Katana or WeaponKind.Longsword, initial.BladeGrip(Hand.Right)!.Value.Support.HasValue);
        var point = initial.Self.WorldToLocal(initial.OpponentChest);
        var travel = Vector3.Normalize(new Vector3(1, -1, 0));
        var request = new BladeCutRequest(point, travel, Vector3.Cross(Vector3.UnitZ, travel));
        var playback = new BladeCutPlayback();
        Assert.IsTrue(playback.TryStart(Hand.Right, initial, request),
            $"{kind}: point {point}; grip {initial.BladeGrip(Hand.Right)}; arms {initial.BladeArm(Hand.Right)}, {initial.BladeArm(Hand.Left)}");
        var plan = playback.Plan!;
        var control = new DesktopFighterControl(claw ? null : kind, bladePlan: true);
        if (kind == WeaponKind.IronSword)
        {
            var idle = control.Read(default, initial, dt);
            Assert.AreEqual(Basis(initial).Left, idle.Left);
            Assert.AreEqual(Basis(initial).Right, idle.Right);
        }

        var worldPoint = initial.Self.LocalToWorld(point);
        var minimumDistance = float.PositiveInfinity;
        var samples = 0;
        while (playback.Elapsed <= plan.Duration)
        {
            var sense = game.PlayerSense;
            var basis = Basis(sense);
            Assert.IsTrue(playback.TryRead(sense, basis, dt, out var expected));
            var command = control.Read(new FighterIntent(Vector2.Zero, Heavy: true), sense, dt);
            Near(expected.Right.TargetLocal, command.Right.TargetLocal);
            Assert.AreEqual(expected.Right.Orientation, command.Right.Orientation);
            if (kind == WeaponKind.IronSword)
            {
                Assert.AreEqual(basis.Left, command.Left);
            }
            if (sense.BladeGrip(Hand.Right) is { } grip)
            {
                var physical = sense.Self.LocalToWorld(grip.Pose.Point(plan.Geometry.ContactOffset));
                minimumDistance = Math.Min(minimumDistance, Vector3.Distance(physical, worldPoint));
            }
            game.Step(new BoxingInput(command), dt);
            samples++;
        }

        Console.WriteLine($"{kind}: {samples} physical steps; closest blade point {minimumDistance:F3} m from target");
        Assert.IsTrue(samples > 90);
        Assert.IsTrue(minimumDistance < 0.25f);
    }

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.IsTrue(Vector3.Distance(expected, actual) < 0.001f, $"expected {expected}, actual {actual}");
}
