using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

[TestClass]
public sealed class SplitTorsoTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void SplitTorso_PreservesMassAndRespondsToHandTargetsWithoutTwistBias()
    {
        using var world = NewWorld();
        var tuning = FighterTuning.Default with { SplitTorso = true };
        var fighter = Fighter.Spawn(world, tuning, "split", Vector3.Zero, 0f);
        Assert.IsTrue(fighter.Parts.Abdomen.HasValue);
        var abdomen = fighter.Parts.Abdomen.GetValueOrDefault();
        Assert.AreEqual(tuning.Masses.Chest, world.GetMass(abdomen) + world.GetMass(fighter.Parts.Chest), 0.001f);
        Assert.AreEqual(BodyPartKind.Abdomen, fighter.KindOf(abdomen));

        var forward = fighter.Frame.Forward;
        var left = new HandCommand(new Vector3(0.15f, 0.1f, 0.35f), false);
        var right = new HandCommand(new Vector3(-0.15f, 0.1f, 0.1f), false);
        var load = new FighterCommand(Vector3.Zero, forward, left, right);
        for (var i = 0; i < 480; i++)
        {
            fighter.ApplyControl(load, Dt);
            world.Step(Dt);
        }

        float Yaw(int part) => BodyFrame.YawOf(
            Vector3.Transform(Vector3.UnitZ, world.GetState(part).Orientation), 0f);
        var loadedPelvis = Yaw(fighter.Parts.Pelvis);
        var loadedAbdomen = Yaw(abdomen);
        var loadedChest = Yaw(fighter.Parts.Chest);
        var footEnergyBefore = fighter.StepEnergyReleased;
        var release = load with { Right = right with { TargetLocal = right.TargetLocal with { Z = 0.65f } } };
        for (var i = 0; i < 38; i++)
        {
            fighter.ApplyControl(release, Dt);
            world.Step(Dt);
        }

        Console.WriteLine($"pelvis={loadedPelvis:F3}->{Yaw(fighter.Parts.Pelvis):F3}, abdomen={loadedAbdomen:F3}->{Yaw(abdomen):F3}, chest={loadedChest:F3}->{Yaw(fighter.Parts.Chest):F3}");
        Assert.IsGreaterThan(footEnergyBefore, fighter.StepEnergyReleased, "拳の目標が前へ動くと脚のばねを放つ");
        Assert.IsGreaterThan(loadedPelvis + 0.04f, Yaw(fighter.Parts.Pelvis));
        Assert.IsGreaterThan(loadedAbdomen + 0.04f, Yaw(abdomen));
        Assert.IsGreaterThan(loadedChest + 0.04f, Yaw(fighter.Parts.Chest));
        Assert.AreEqual(FighterState.Standing, fighter.State);
    }

    [TestMethod]
    public void AdaptivePunch_FormWeightFollowsEitherHandTarget()
    {
        float Punch(float reach, bool leftHand)
        {
            using var world = NewWorld();
            var fighter = Fighter.Spawn(world, FighterTuning.AdaptivePunch, "adaptive", Vector3.Zero, 0f);
            var forward = fighter.Frame.Forward;
            var left = new HandCommand(new Vector3(0.15f, 0.08f, 0.24f), false);
            var right = new HandCommand(new Vector3(-0.15f, 0.08f, 0.24f), false);
            var guard = new FighterCommand(Vector3.Zero, forward, left, right);
            void Step(FighterCommand command)
            {
                fighter.ApplyControl(command, Dt);
                world.Step(Dt);
            }

            for (var i = 0; i < 480; i++)
            {
                Step(guard);
            }

            for (var i = 1; i <= 48; i++)
            {
                var z = 0.24f + (i / 48f * ((fighter.ArmReach * reach) - 0.24f));
                var command = leftHand
                    ? guard with { Left = left with { TargetLocal = left.TargetLocal with { Z = z } } }
                    : guard with { Right = right with { TargetLocal = right.TargetLocal with { Z = z } } };
                Step(command);
            }

            return fighter.PunchForm;
        }

        var close = Punch(1.15f, leftHand: false);
        var middle = Punch(1.35f, leftHand: false);
        var far = Punch(1.55f, leftHand: false);
        var left = Punch(1.35f, leftHand: true);
        Assert.AreEqual(0f, close, 1e-4f);
        Assert.AreEqual(0.5f, middle, 1e-4f);
        Assert.AreEqual(1f, far, 1e-4f);
        Assert.AreEqual(middle, left, 1e-4f);
    }

    [TestMethod]
    public void SplitTorso_MatchSpawnsVoxelBodiesAndRuns()
    {
        var tuning = FighterTuning.Default with { SplitTorso = true };
        using var game = new BoxingGame(tuning, 1);
        var player = game.Player;
        var abdomen = player.Parts.Abdomen.GetValueOrDefault();
        Assert.IsTrue(player.Parts.Abdomen.HasValue);
        Assert.IsTrue(player.Voxels!.Layers.Any(layer => layer.Kind == BodyPartKind.Abdomen));
        Assert.AreEqual(tuning.Masses.Chest, game.World.GetMass(abdomen) + game.World.GetMass(player.Parts.Chest), 0.1f);
        var command = new FighterCommand(Vector3.Zero, player.Frame.Forward,
            new HandCommand(new Vector3(0.15f, 0.1f, 0.30f), false),
            new HandCommand(new Vector3(-0.15f, 0.1f, 0.24f), false));
        for (var i = 0; i < 120; i++)
        {
            game.Step(new BoxingInput(command), Dt);
        }

        Assert.IsTrue(float.IsFinite(game.World.GetState(abdomen).Position.Y));
        Assert.AreEqual(FighterState.Standing, player.State);
    }

    private static PhysicsWorld NewWorld()
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        return world;
    }
}
