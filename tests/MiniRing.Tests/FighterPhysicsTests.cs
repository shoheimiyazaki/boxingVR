using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// ヘッドレスで物理を回し、「立てる・歩ける・手が届く・掴める」を数値で確かめる。
/// 手触りの良し悪しは測れないが、発散・転倒・符号ミスはここで捕まえる。
/// </summary>
[TestClass]
public sealed class FighterPhysicsTests
{
    private const float Dt = 1f / 240f;
    private static readonly Vector3 Floor = new(0f, 0f, 0f);

    [TestMethod]
    public void Standing_StaysUprightAtStandHeight()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Floor, 0f);
        var startPelvis = fighter.PelvisPosition;

        Run(world, fighter, Idle(fighter), seconds: 3f);

        Assert.AreEqual(FighterState.Standing, fighter.State);
        Assert.IsGreaterThan(0.9f, fighter.Uprightness, $"upright {fighter.Uprightness}");
        // 足の運びの構え（足を前後に開き、膝を曲げる）で骨盤は生成時（脚がまっすぐ）より下がる。下がり過ぎない（2 割以内）。
        Assert.IsLessThan(startPelvis.Y, fighter.PelvisPosition.Y, $"pelvis {fighter.PelvisPosition}");
        Assert.IsGreaterThan(0.8f * startPelvis.Y, fighter.PelvisPosition.Y, $"pelvis {fighter.PelvisPosition}");
        Assert.IsLessThan(0.05f, Horizontal(fighter.PelvisPosition - startPelvis));
    }

    [TestMethod]
    public void Walking_MovesInCommandedDirectionWithoutFalling()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Floor, 0f);
        var start = fighter.PelvisPosition;
        var command = Idle(fighter) with { MoveWorld = Vector3.UnitX, FaceDirection = Vector3.UnitZ };

        Run(world, fighter, command, seconds: 2f);

        Assert.AreEqual(FighterState.Standing, fighter.State);
        Assert.IsGreaterThan(0.5f, fighter.PelvisPosition.X - start.X, $"moved {fighter.PelvisPosition - start}");
        Assert.IsGreaterThan(0.85f, fighter.Uprightness);
    }

    [TestMethod]
    public void Turning_FacesCommandedDirection()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Floor, 0f);

        Run(world, fighter, Idle(fighter) with { FaceDirection = -Vector3.UnitZ }, seconds: 2f);

        var chestForward = Vector3.Transform(Vector3.UnitZ, world.GetState(fighter.Parts.Chest).Orientation);
        Assert.IsLessThan(-0.9f, chestForward.Z, $"chest forward {chestForward}");
        Assert.AreEqual(FighterState.Standing, fighter.State);
    }

    [TestMethod]
    public void SpawnedFacingBackward_StandsFacingBackward()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Floor, MathF.PI);

        Run(world, fighter, Idle(fighter) with { FaceDirection = -Vector3.UnitZ }, seconds: 1.5f);

        var chestForward = Vector3.Transform(Vector3.UnitZ, world.GetState(fighter.Parts.Chest).Orientation);
        Assert.IsLessThan(-0.9f, chestForward.Z);
        Assert.AreEqual(FighterState.Standing, fighter.State);
    }

    [TestMethod]
    [DataRow(Hand.Left)]
    [DataRow(Hand.Right)]
    public void Hand_ReachesStraightPunchTarget(Hand hand)
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Floor, 0f);
        var reach = fighter.ArmReach;
        var side = hand.LocalSide();
        var target = new Vector3(side * 0.08f, 0f, 0.85f) * reach;
        var command = Idle(fighter) with
        {
            Left = new HandCommand(hand == Hand.Left ? target : AiControl.Guard(Hand.Left, reach), false),
            Right = new HandCommand(hand == Hand.Right ? target : AiControl.Guard(Hand.Right, reach), false),
        };

        Run(world, fighter, command, seconds: 1.5f);

        var error = Vector3.Distance(fighter.GloveCenter(hand), fighter.Frame.LocalToWorld(target));
        Assert.IsLessThan(reach * 0.15f, error, $"glove {fighter.GloveCenter(hand)} target {fighter.Frame.LocalToWorld(target)} error {error}");
        // +Z を向いた体の右手は −X 側（右手系）。VR の右コントローラ（−X 側に来る）と同じ側の腕が動くこと。
        var shoulderSide = MathF.Sign(fighter.GloveCenter(hand).X - fighter.PelvisPosition.X);
        Assert.AreEqual(hand == Hand.Right ? -1f : 1f, shoulderSide, $"{hand} glove on the wrong side");
        Assert.AreEqual(FighterState.Standing, fighter.State);
    }

    [TestMethod]
    public void Hand_TracksGuardPoseWithBentElbow()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Floor, 0f);
        var reach = fighter.ArmReach;

        Run(world, fighter, Idle(fighter), seconds: 1.5f);

        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var target = fighter.Frame.LocalToWorld(AiControl.Guard(hand, reach));
            var error = Vector3.Distance(fighter.GloveCenter(hand), target);
            Assert.IsLessThan(reach * 0.2f, error, $"{hand}: glove {fighter.GloveCenter(hand)} target {target}");
        }
    }

    [TestMethod]
    public void Grab_HoldsOpponentAndReleases()
    {
        using var world = NewWorld();
        var a = Fighter.Spawn(world, FighterTuning.Default, "a", Floor, 0f);
        var b = Fighter.Spawn(world, FighterTuning.Default, "b", Floor + new Vector3(0f, 0f, 0.2f), MathF.PI);
        Run(world, [a, b], seconds: 0.5f, (f, opp) => Idle(f) with { FaceDirection = opp.ChestPosition - f.ChestPosition });

        // 右手を相手の胸へ伸ばして握る。
        FighterCommand Reach(Fighter f, Fighter opp, bool grip) => Idle(f) with
        {
            FaceDirection = opp.ChestPosition - f.ChestPosition,
            Right = new HandCommand(f.Frame.WorldToLocal(opp.ChestPosition + new Vector3(0.03f, 0f, 0f)), grip),
        };

        Run(world, [a, b], seconds: 1f, (f, opp) => f == a ? Reach(a, b, false) : Idle(f) with { FaceDirection = -Vector3.UnitZ });
        Run(world, [a, b], seconds: 0.5f, (f, opp) => f == a ? Reach(a, b, true) : Idle(f) with { FaceDirection = -Vector3.UnitZ });
        Assert.IsTrue(a.IsHolding(Hand.Right), $"glove {a.GloveCenter(Hand.Right)} chest {b.ChestPosition}");
        Assert.IsTrue(b.IsGrabbed);

        Run(world, [a, b], seconds: 0.2f, (f, opp) => f == a ? Reach(a, b, false) : Idle(f));
        Assert.IsFalse(a.IsHolding(Hand.Right));
        Assert.IsFalse(b.IsGrabbed);
    }

    [TestMethod]
    public void Knockdown_FallsThenGetsBackUp()
    {
        using var world = NewWorld();
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "f", Floor, 0f);
        Run(world, fighter, Idle(fighter), seconds: 0.5f);

        fighter.Knockdown(Vector3.UnitZ, permanent: false);
        Run(world, fighter, Idle(fighter), seconds: 1f);
        Assert.AreEqual(FighterState.Down, fighter.State);
        Assert.IsLessThan(0.6f, fighter.Uprightness, "should actually be on the floor");

        Run(world, fighter, Idle(fighter), seconds: 5f);
        Assert.AreEqual(FighterState.Standing, fighter.State, $"upright {fighter.Uprightness}");
    }

    [TestMethod]
    public void Match_AiVersusIdlePlayer_RunsFiniteAndLandsPunches()
    {
        using var game = new BoxingGame(seed: 7);
        var hits = 0;
        for (var i = 0; i < 60 * 20; i++)
        {
            var guard = new FighterCommand(Vector3.Zero, game.Cpu.ChestPosition - game.Player.ChestPosition,
                new HandCommand(AiControl.Guard(Hand.Left, game.Player.ArmReach), false),
                new HandCommand(AiControl.Guard(Hand.Right, game.Player.ArmReach), false));
            game.Step(new BoxingInput(guard), 1f / 60f);
            hits += game.RecentHits.Count;
            foreach (var body in game.World.SnapshotDynamics())
            {
                Assert.IsTrue(float.IsFinite(body.Position.X) && float.IsFinite(body.Position.Y) && float.IsFinite(body.Position.Z), $"step {i} body {body.Id}");
            }
        }

        Assert.IsGreaterThan(0, hits, game.StatusLine);
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

    private static void Run(PhysicsWorld world, Fighter[] fighters, float seconds, Func<Fighter, Fighter, FighterCommand> commandFor)
    {
        for (var i = 0; i < (int)(seconds / Dt); i++)
        {
            foreach (var f in fighters)
            {
                var opponent = f == fighters[0] ? fighters[1] : fighters[0];
                var command = commandFor(f, opponent);
                f.UpdateGrab(Hand.Left, command.Left.Grip, opponent);
                f.UpdateGrab(Hand.Right, command.Right.Grip, opponent);
                f.ApplyControl(command, Dt);
            }

            world.Step(Dt);
        }
    }

    private static float Horizontal(Vector3 v) => new Vector2(v.X, v.Z).Length();
}
