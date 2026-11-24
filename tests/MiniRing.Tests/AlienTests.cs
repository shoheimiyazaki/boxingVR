using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Motion;
using MiniRing.Vr;
using MiniRing.Replay;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("slow")]
public sealed class AlienTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void Galva_HasFourLegsAndTwoClaws()
    {
        using var game = CreateGame();
        var f = game.Player;
        foreach (var kind in new[] { BodyPartKind.Thigh, BodyPartKind.Shin, BodyPartKind.Foot, BodyPartKind.Toe })
        {
            Assert.AreEqual(4, f.AllParts.Count(id => f.KindOf(id) == kind));
            Assert.AreEqual(4, f.Voxels!.Parts.Count(part => part.Kind == kind));
        }

        var head = f.Voxels!.PartOf(f.Parts.Head)!.Body.Grid;
        Assert.IsGreaterThan(0, head.Remaining(Voxels.BoxerAnatomy.Cord));
        Assert.IsGreaterThan(0, head.Remaining(Voxels.BoxerAnatomy.Brain));
        Assert.IsGreaterThan(0, head.Remaining(Voxels.BoxerAnatomy.Eye));
        Assert.AreEqual(WeaponKind.SickleClaw, game.GloveOf(f, Hand.Left)?.Kind);
        Assert.AreEqual(WeaponKind.SickleClaw, game.GloveOf(f, Hand.Right)?.Kind);
        var height = f.HeadPosition.Y + f.HalfExtents.Head.Y - BoxingGame.RingSurfaceHeight;
        Assert.IsTrue(height is > 1.36f and < 1.46f, $"spawn height: {height:F3} m");
        Assert.IsTrue(game.PlayerSense.SelfLeg(Hand.Left).Hip.X > game.PlayerSense.SelfLeg(Hand.Right).Hip.X);
    }

    [TestMethod]
    public void IdleAndWalking_UseTheFrontAndRearFootprint()
    {
        using var game = CreateGame();
        var f = game.Player;
        for (var i = 0; i < 480; i++)
        {
            Step(game, Guard(f));
        }

        var zSpan = f.SupportHull.Max(v => v.Y) - f.SupportHull.Min(v => v.Y);
        Assert.IsGreaterThan(0.42f, zSpan);
        Assert.AreEqual(FighterState.Standing, f.State);
        var before = f.PelvisPosition;
        for (var i = 0; i < 360; i++)
        {
            Step(game, Guard(f) with { MoveWorld = Vector3.UnitX });
        }

        Assert.AreEqual(FighterState.Standing, f.State);
        Assert.IsGreaterThan(0.20f, f.PelvisPosition.X - before.X);
        Assert.IsTrue(float.IsFinite(f.OxygenOf(MuscleRegion.LeftLeg)) && float.IsFinite(f.OxygenOf(MuscleRegion.RightLeg)));
        Assert.IsGreaterThan(0f, f.HeartCapacity);
        Assert.IsGreaterThan(0f, f.LungCapacity);
    }

    [TestMethod]
    [DataRow(Hand.Left)]
    [DataRow(Hand.Right)]
    public void FrontLegAndClaw_FollowTargetsOnEitherSide(Hand side)
    {
        using var game = CreateGame();
        var f = game.Player;
        for (var i = 0; i < 480; i++)
        {
            Step(game, Guard(f));
        }

        var reach = f.ArmReach;
        var aim = f.Frame.LocalToWorld(new Vector3(0.35f * reach * side.LocalSide(), -reach, reach));
        Assert.AreEqual(side, FrontLegStrike.Choose(game.PlayerSense, aim));
        var heel = f.HeelId((int)side);
        var before = game.World.GetState(heel).Position;
        var wristBefore = f.GloveCenter(side);
        var maxLift = 0f;
        var maxExtension = 0f;
        for (var i = 0; i < 168; i++)
        {
            var sense = game.PlayerSense;
            var ankle = FrontLegStrike.At(sense.SelfLeg(side), sense.Self.WorldToLocal(aim), i / 168f, false, side);
            var hand = new HandCommand(new Vector3(0.23f * reach * side.LocalSide(), -0.16f * reach, 0.95f * reach), false);
            var guard = Guard(f);
            Step(game, side == Hand.Left ? guard with { LeftLeg = ankle, Left = hand } : guard with { RightLeg = ankle, Right = hand });
            maxLift = MathF.Max(maxLift, game.World.GetState(heel).Position.Y - before.Y);
            maxExtension = MathF.Max(maxExtension, f.GloveCenter(side).Z - wristBefore.Z);
        }

        Assert.IsGreaterThan(0.12f, maxLift);
        Assert.IsGreaterThan(0.10f, maxExtension);
        Assert.AreEqual(FighterState.Standing, f.State);
    }

    [TestMethod]
    public void Replay_ReconstructsTheRigAndCommands()
    {
        using var game = CreateGame();
        using var stream = new MemoryStream();
        var blade = ClawStroke.Create(Hand.Left, game.Player.ArmReach, new Vector3(0.1f, 0.15f, 0.80f), true).Sample(0.41f);
        var player = Guard(game.Player) with
        {
            Left = blade.ToCommand(Hand.Left),
            LeftLeg = new LegCommand(new Vector3(0.1f, -0.60f, 0.45f)),
        };
        var blue = Guard(game.Cpu) with { FaceDirection = -Vector3.UnitZ };
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < ReplayRecorder.ChecksumInterval; i++)
            {
                recorder.Step(game, new BoxingInput(player, Blue: blue), 1f / 60f);
            }
        }

        using var recorded = new MemoryStream(stream.ToArray());
        var data = ReplayFile.Read(recorded);
        var corners = data.Header.Corners;
        Assert.IsTrue(corners.PlayerBody?.FourLegged);
        Assert.AreEqual(HandWear.SickleClaw, corners.PlayerWear);
        Assert.AreEqual(player.LeftLeg, data.Steps[0].Input.Player.LeftLeg);
        Assert.AreEqual(player.Left, data.Steps[0].Input.Player.Left);
        Assert.IsTrue(ReplayPlayer.Verify(data).Reproduced);
    }

    [TestMethod]
    public void DesktopAndVr_EmitIndependentClawTargetsAndChooseAFrontLeg()
    {
        using var game = CreateGame();
        var sense = game.PlayerSense;
        sense = sense with
        {
            OpponentBody = sense.OpponentBody with { Pelvis = sense.Self.LocalToWorld(new Vector3(-0.4f, -0.4f, 0.8f)) },
        };
        var desktop = new DesktopFighterControl();
        var left = desktop.Read(new FighterIntent(Vector2.Zero, Light: true), sense, 0.12f, kickPressed: true, sweep: true);
        Assert.IsNotNull(left.Left.Orientation);
        Assert.AreEqual(ClawStroke.Guard(Hand.Right, sense.ArmReach).Root, left.Right.TargetLocal);
        Assert.IsNull(left.LeftLeg);
        Assert.IsNotNull(left.RightLeg);

        Assert.IsTrue(VrMatchOptions.TryParse(["--player-char", "galva"], out var options, out var error), error);
        Assert.IsTrue(options.Corners.PlayerBody?.FourLegged);
        var vr = new VrPlayerMapper();
        var input = new VrRawFrame(new Vector3(0f, 1.65f, 0f), Quaternion.Identity,
            new Vector3(-0.25f, 1.36f, -0.35f), new Vector3(0.35f, 1.25f, -0.45f),
            true, true, Vector2.Zero, Vector2.Zero, 0f, 0f, false, true, RightTrigger: 1f);
        var mapped = vr.Read(input, sense, Dt).Player;
        Assert.AreNotEqual(mapped.Left.TargetLocal, mapped.Right.TargetLocal);
        Assert.IsNotNull(mapped.Left.Orientation);
        Assert.IsNotNull(mapped.Right.Orientation);
        Assert.IsNotNull(mapped.RightLeg);
    }

    [TestMethod]
    public void GalvaBot_ReportsTheClawStrokeItActuallyStarts()
    {
        using var game = CreateGame(0.84f);
        var galva = CharacterRoster.Get("galva");
        var bot = new BoxerBot(galva.Style with { KickRate = 0f }, seed: 77);
        var command = bot.Read(game.PlayerSense, Dt);
        Assert.AreEqual(1, bot.Stats.Combos.GetValueOrDefault("claw-sweep"));
        Assert.HasCount(1, bot.Stats.Combos);
        Assert.IsNotNull(command.Right.Orientation);
        Assert.IsNull(bot.CurrentTechnique(Hand.Right));
    }

    [TestMethod]
    public void AimedKick_UsesTheExistingContactPipeline()
    {
        using var game = CreateGame(0.80f);
        var fighter = game.Player;
        for (var i = 0; i < 240; i++)
        {
            Step(game, Guard(fighter));
        }

        HitEvent? landed = null;
        for (var i = 0; i < 160 && landed is null; i++)
        {
            var sense = game.PlayerSense;
            var side = Hand.Right;
            var basis = Guard(fighter);
            var strike = basis with { RightLeg = FrontLegStrike.At(sense.SelfLeg(side), sense.Self.WorldToLocal(FrontLegStrike.Aim(sense)), i / 160f, false, side) };
            Step(game, strike);
            foreach (var hit in game.RecentHits)
            {
                if (hit.Attacker == fighter && hit.Hand == side && hit.Kick)
                {
                    landed = hit;
                    break;
                }
            }
        }

        Assert.IsNotNull(landed, "front-leg hit");
        Assert.IsGreaterThan(0f, landed.Value.ImpactMass);
    }

    [TestMethod]
    public void RearLegs_AddTheirMuscleToTheirOwnSidesOxygenStore()
    {
        // 脚の酸素の蓄えは本人の左右に分かれている（PhysicsVersion 94）。四足の後ろ脚は前脚と同じ寸法なので、同じ体格の二足より各側の蓄えがちょうど 2 倍。
        var galva = CharacterRoster.Get("galva").Build;
        (float Left, float Right) Capacity(BodyBuild build)
        {
            using var game = CreateGame(build: build);
            Step(game, Guard(game.Player));
            return (game.Player.OxygenBudgetOf(MuscleRegion.LeftLeg).Capacity, game.Player.OxygenBudgetOf(MuscleRegion.RightLeg).Capacity);
        }

        var four = Capacity(galva);
        var two = Capacity(galva with { FourLegged = false });
        Assert.AreEqual(four.Left, four.Right, four.Left * 1e-3f, "左右の脚は同じ");
        Assert.AreEqual(2f, four.Left / two.Left, 0.02f, $"four {four.Left:0} J / two {two.Left:0} J");
    }

    private static BoxingGame CreateGame(float gap = 3f, BodyBuild? build = null)
    {
        var c = CharacterRoster.Get("galva");
        return new BoxingGame(corners: BotCatalog.Corners("boxer") with
        {
            PlayerBody = build ?? c.Build, PlayerWear = c.Wear, PlayerLook = c.Look, PlayerFaceSpec = c.Face,
            Ring = RingLayout.Boxing with { StartGap = gap, FromCorners = false }, ExternalBlue = true, Referee = false,
        });
    }

    private static FighterCommand Guard(Fighter f) => new(Vector3.Zero, Vector3.UnitZ,
        new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
        new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));

    private static void Step(BoxingGame game, FighterCommand command)
    {
        game.Step(new BoxingInput(command, Blue: Guard(game.Cpu) with { FaceDirection = -Vector3.UnitZ }), Dt);
    }
}
