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
    public void Galva_WeldSeamTracksTheChestAndPelvisVoxels()
    {
        using var game = CreateGame();
        var f = game.Player;
        var body = f.Voxels!;
        Assert.IsGreaterThan(0, body.JointContactCount(f.Parts.Chest, f.Parts.Pelvis).Total);
        Step(game, Guard(f));
        Assert.IsTrue(f.JointIntact(f.Parts.Chest, f.Parts.Pelvis));
        Assert.HasCount(0, body.Fragments);

        var chest = body.PartOf(f.Parts.Chest)!.Body.Grid;
        for (var i = 0; i < chest.CellCount; i++)
        {
            var (x, y, z) = chest.Coordinates(i);
            if (chest.CellCenter(x, y, z).Y < -0.36f * chest.HalfExtents.Y)
            {
                chest.SetCellState(i, (0, 0f, false, 0f));
            }
        }
        Assert.IsTrue(f.SplitDisconnected());
        Assert.IsFalse(f.JointIntact(f.Parts.Chest, f.Parts.Pelvis));
        Assert.IsTrue(f.JointIntact(f.Parts.Chest, f.Parts.Head));
    }

    [TestMethod]
    public void Galva_ExposesEightWhiteEyesWithBlackPupils()
    {
        using var game = CreateGame();
        var head = game.Player.Voxels!.PartOf(game.Player.Parts.Head)!.Body.Grid;
        var (nx, ny, nz) = head.Dimensions;
        var visible = new HashSet<(bool Left, bool Outer, bool Upper, byte Material)>();
        for (var x = 0; x < nx; x++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var z = nz - 1; z >= 0; z--)
                {
                    var material = head.At(x, y, z);
                    if (material == 0)
                    {
                        continue;
                    }
                    if (material is Voxels.BoxerAnatomy.Eye or Voxels.BoxerAnatomy.Pupil)
                    {
                        var n = head.CellCenter(x, y, z) / head.HalfExtents;
                        visible.Add((n.X > 0f, MathF.Abs(n.X) > 0.45f, n.Y > 0.26f, material));
                    }
                    break;
                }
            }
        }

        Assert.HasCount(16, visible);
        var eye = head.Palette[Voxels.BoxerAnatomy.Eye].SurfaceColor;
        var pupil = head.Palette[Voxels.BoxerAnatomy.Pupil].SurfaceColor;
        Assert.IsTrue(eye.X > 0.9f && eye.Y > 0.9f && eye.Z > 0.9f);
        Assert.IsTrue(pupil.X < 0.15f && pupil.Y < 0.15f && pupil.Z < 0.15f);
        Assert.IsGreaterThan(0f, game.Player.Voxels.Vision.Left);
        Assert.IsGreaterThan(0f, game.Player.Voxels.Vision.Right);
    }

    [TestMethod]
    public void Galva_DorsalPlatesRaiseTheSurfaceAndLeaveOrganCellsIntact()
    {
        var source = new Aigis.Foundation.Voxels.VoxelGrid(Voxels.BoxerAnatomy.Palette, new Vector3(0.12f, 0.08f, 0.34f), (21, 21, 51),
            (_, n) => n.LengthSquared() < 0.05f ? Voxels.BoxerAnatomy.Liver
                : (n.X * n.X) + (n.Y * n.Y) < 0.36f ? Voxels.BoxerAnatomy.Skin : (byte)0);
        var shell = Voxels.AlienAnatomy.Plate(BodyPartKind.Pelvis, source, Voxels.AlienAnatomy.Palette, mirrorX: false);
        var top = Enumerable.Range(0, 51).Select(z => Enumerable.Range(0, 21).Last(y => shell.At(10, y, z) != 0)).ToArray();
        Assert.IsGreaterThanOrEqualTo(4, top.Zip(top.Skip(1)).Count(pair => pair.First > pair.Second));
        Assert.AreEqual(source.Remaining(Voxels.BoxerAnatomy.Liver), shell.Remaining(Voxels.BoxerAnatomy.Liver));
        Assert.IsGreaterThan(source.FilledCount, shell.FilledCount);
    }

    [TestMethod]
    public void Galva_HasOneThoraxAndFourArticulatedPointedLegs()
    {
        using var game = CreateGame();
        var f = game.Player;
        var joints = game.World.JointStates();
        Assert.IsNull(f.Parts.Abdomen);
        Assert.IsTrue(joints.Any(j => j.Type == Aigis.Physics.JointType.Weld
            && ((j.BodyA == f.Parts.Pelvis && j.BodyB == f.Parts.Chest) || (j.BodyB == f.Parts.Pelvis && j.BodyA == f.Parts.Chest))));
        Assert.IsLessThan(0.12f, f.HalfExtents.Chest.X);

        var localChest = f.Frame.WorldToLocal(game.World.GetState(f.Parts.Chest).Position);
        var front = game.PlayerSense.SelfLeg(Hand.Left).Hip;
        Assert.IsTrue(MathF.Abs(localChest.Z - front.Z) < 0.06f);

        var roots = f.AllParts.Where(id => game.World.TryGetName(id)?.EndsWith("_coxa", StringComparison.Ordinal) == true).ToArray();
        Assert.HasCount(4, roots);
        foreach (var root in roots)
        {
            Assert.IsTrue(joints.Any(j => j.BodyA == f.Parts.Pelvis && j.BodyB == root));
            Assert.IsTrue(joints.Any(j => j.BodyA == root && f.KindOf(j.BodyB) == BodyPartKind.Thigh));
        }

        foreach (var tip in f.Voxels!.Parts.Where(p => p.Kind == BodyPartKind.Toe))
        {
            var grid = tip.Body.Grid;
            var half = grid.HalfExtents;
            Assert.IsGreaterThan(2f * MathF.Max(half.X, half.Z), half.Y);
            var (nx, ny, nz) = grid.Dimensions;
            int Area(int y) => Enumerable.Range(0, nx).Sum(x => Enumerable.Range(0, nz).Count(z => grid.At(x, y, z) != 0));
            Assert.IsGreaterThan(0, Area(0));
            Assert.IsGreaterThan(Area(0), Area(ny - 1));
        }

        for (var i = 0; i < 480; i++)
        {
            Step(game, Guard(f));
        }
        foreach (var joint in joints.Where(j => j.Type is Aigis.Physics.JointType.Hinge or Aigis.Physics.JointType.BallSocket
            && (roots.Contains(j.BodyA) || roots.Contains(j.BodyB))))
        {
            var anchors = game.World.GetJointAnchors(joint.Id)!.Value;
            Assert.IsLessThan(0.025f, Vector3.Distance(anchors.AnchorA, anchors.AnchorB));
        }
    }

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
        // 立っている間も足を 1 本ずつ置き直す（その間は 3 本で立つ）ので、足跡の前後の幅は歩ごとに 0.25〜0.8 m で揺れる。1 歩の瞬間でなく、
        // 落ち着いた後の 1 秒でいちばん広く 4 本を着いた足跡で見る（2026-11-26、ClawAimProbe.IdleStanceTrace）。
        var zSpan = 0f;
        for (var i = 0; i < 480; i++)
        {
            Step(game, Guard(f));
            if (i >= 240 && f.SupportHull.Count > 0)
            {
                zSpan = MathF.Max(zSpan, f.SupportHull.Max(v => v.Y) - f.SupportHull.Min(v => v.Y));
            }
        }

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
    public void Galva_CornerStanceKeepsAllFourTipsInsideTheRing()
    {
        using var game = CreateGame(fromCorners: true);
        var f = game.Player;
        var red = Guard(f) with { FaceDirection = f.Frame.Forward };
        var blue = Guard(game.Cpu) with { FaceDirection = game.Cpu.Frame.Forward };
        for (var i = 0; i < 480; i++)
        {
            game.Step(new BoxingInput(red, Blue: blue), Dt);
        }

        foreach (var tip in f.AllParts.Where(id => f.KindOf(id) == BodyPartKind.Toe))
        {
            Assert.IsGreaterThan(0f, game.Layout.EdgeDistance(game.World.GetState(tip).Position));
        }
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
        // 脚の酸素の蓄えは本人の左右に分かれている（PhysicsVersion 94）。四足の後ろ脚は前脚を写した対なので、各側の蓄えは前脚 1 本の分のちょうど 2 倍。
        // 蓄えは脚の質量に比例する（Fighter.LegSideMass）ので、前脚の質量 1 kg あたりの蓄えで二足と比べる。四足の脚は昆虫の細い脚の寸法
        // （BodyBuild.ScaleOf の FourLegged、2026-11-25 の a6cae71）で二足の脚と太さが違うため、同じ体格の二足の蓄えそのものとは比べない。
        var galva = CharacterRoster.Get("galva").Build;
        (float Left, float Right, float FrontLegs) Capacity(BodyBuild build)
        {
            using var game = CreateGame(build: build);
            Step(game, Guard(game.Player));
            var f = game.Player;
            var p = f.Parts;
            var front = new[] { p.LeftThigh, p.LeftShin, p.LeftFoot, p.RightThigh, p.RightShin, p.RightFoot, f.ToeId(0), f.ToeId(1) }.Sum(game.World.GetMass);
            // 四足はエイリアンの筋の材質（蓄えの係数が仮に極端: TissuePhysiology）、二足は人の材質で作るので、筋の量を比べるため材質の係数で割る（2026-11-25）。
            var store = MiniRing.Voxels.TissuePhysiology.Of(MiniRing.Voxels.BoxerBody.PaletteFor(build)).OxygenStore;
            return (f.OxygenBudgetOf(MuscleRegion.LeftLeg).Capacity / store, f.OxygenBudgetOf(MuscleRegion.RightLeg).Capacity / store, front);
        }

        var four = Capacity(galva);
        var two = Capacity(galva with { FourLegged = false });
        Assert.AreEqual(four.Left, four.Right, four.Left * 1e-3f, "左右の脚は同じ");
        var perKgFour = (four.Left + four.Right) / four.FrontLegs;
        var perKgTwo = (two.Left + two.Right) / two.FrontLegs;
        Assert.AreEqual(2f, perKgFour / perKgTwo, 0.02f, $"four {four.Left:0} J ({four.FrontLegs:0.0} kg of front legs) / two {two.Left:0} J ({two.FrontLegs:0.0} kg)");
    }

    [TestMethod]
    [DataRow(0f, 0f)]
    [DataRow(-0.12f, 0.08f)]
    public void PointedFrontLeg_CarvesWithItsTip(float aimX, float aimY)
    {
        using var game = CreateGame(0.67f);
        var f = game.Player;
        for (var i = 0; i < 240; i++)
        {
            Step(game, Guard(f));
        }

        const Hand side = Hand.Right;
        var toe = f.ToeId((int)side);
        var target = FrontLegStrike.Aim(game.PlayerSense) + new Vector3(aimX, aimY, 0f);
        var targets = game.Cpu.Voxels!.Parts.ToDictionary(p => p.Body.BodyId);
        var carved = new Dictionary<int, HashSet<int>>();
        for (var i = 0; i < 173; i++)
        {
            var sense = game.PlayerSense;
            var strike = FrontLegStrike.At(sense.SelfLeg(side), sense.Self.WorldToLocal(target), i / 172f, false, side);
            Step(game, Guard(f) with { RightLeg = strike });
            foreach (var contact in game.RecentCuts.Where(c => c.A.BodyId == toe && targets.ContainsKey(c.B.BodyId)))
            {
                if (!carved.TryGetValue(contact.B.BodyId, out var cells))
                {
                    carved[contact.B.BodyId] = cells = [];
                }
                foreach (var cell in contact.RemovedFor(contact.B))
                {
                    var grid = contact.B.Grid;
                    var at = (cell.LocalCenter + grid.HalfExtents) / grid.CellSize;
                    cells.Add(grid.Index((int)at.X, (int)at.Y, (int)at.Z));
                }
            }
        }

        var layers = carved.Select(entry => entry.Value.Select(i => targets[entry.Key].Body.Grid.Coordinates(i).Z).Distinct().Count()).DefaultIfEmpty().Max();
        Console.WriteLine($"layers {layers}, carved {string.Join(" ", carved.Select(e => $"{targets[e.Key].Kind}:{e.Value.Count}"))}");
        // 2026-11-26（統合、物理 101〜102）: 尖った跗節の突きは、どの狙いでも相手の部位を削る。ガルヴァの枝（abd1076）では 6 層まで掘り（腿 91 セル）、
        // 横へずらした狙いは前後に貫いた（「2 層以上・貫く列がある」を確かめていた）が、それは刃がセルを丸ごと壊していた物理 96 までの土台での値。
        // 物理 97（刃は進んだ奥行きの分だけ断つ VoxelContact.Sweep.CutInto、docs/BLADE_CUT_STUDY.md §8）の下では、この台本の先端は 1〜2 層で止まる
        // （腿 2 セル・すね 2 セル）。長い鎌が 1 振りで離さなくなった（ClawTrajectoryTests.LongClaw_OneStrokeCutsIntoEveryAim）のと同じ理由。
        // 深く貫くことを期待するのは、体の側（突きに乗る脚と胴の力）を直してから。
        Assert.IsTrue(carved.Values.Any(cells => cells.Count > 0), "the pointed tarsus carves the target");
    }

    private static BoxingGame CreateGame(float gap = 3f, BodyBuild? build = null, bool fromCorners = false)
    {
        var c = CharacterRoster.Get("galva");
        return new BoxingGame(corners: BotCatalog.Corners("boxer") with
        {
            PlayerBody = build ?? c.Build, PlayerWear = c.Wear, PlayerLook = c.Look, PlayerFaceSpec = c.Face,
            Ring = RingLayout.Boxing with { StartGap = gap, FromCorners = fromCorners }, ExternalBlue = true, Referee = false,
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
