using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using Aigis.Render;
using Aigis.Render.Voxels;
using MiniRing.Audio;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// ボクセルの体: エンジン（格子・削る・骨の断裂・切り離し・剛体の形の変更・破片）と、
/// ゲーム（肉が削れると弱る・骨が断たれると落ちる・臓器が壊れると死ぬ）。
/// </summary>
[TestClass]
public sealed class VoxelDamageTests
{
    private const byte Flesh = 1;
    private const byte Bone = 2;

    private static readonly VoxelPalette Palette = new(
    [
        new VoxelMaterial("empty", VoxelTissue.Empty, 1f, false, Vector4.Zero, Vector4.Zero),
        new VoxelMaterial("flesh", VoxelTissue.Flesh, 1f, false, Vector4.One, Vector4.One),
        new VoxelMaterial("bone", VoxelTissue.Bone, 8f, false, Vector4.One, Vector4.One),
    ]);

    /// <summary>X に沿った 10 × 4 × 4 の棒（1cm 角）。芯の 2 × 2 が骨、<paramref name="gapAt"/> の断面だけ骨が無い。</summary>
    private static VoxelGrid Rod(int gapAt = -1) => new(Palette, new Vector3(0.05f, 0.02f, 0.02f), (10, 4, 4), (p, n) =>
    {
        var x = (int)MathF.Floor((p.X + 0.05f) / 0.01f);
        return MathF.Abs(n.Y) < 0.5f && MathF.Abs(n.Z) < 0.5f && x != gapAt ? Bone : Flesh;
    });

    // ── エンジン: 格子 ──

    [TestMethod]
    public void Grid_FillsItsBoxExactly_AndCountsEachMaterial()
    {
        var grid = Rod();

        Assert.AreEqual(new Vector3(0.01f), grid.CellSize);
        Assert.AreEqual(40, grid.Initial(Bone), "a 2 x 2 core along 10 cells");
        Assert.AreEqual(120, grid.Initial(Flesh));
        Assert.AreEqual(160, grid.FilledCount);
        Assert.AreEqual(1f, grid.Fraction(VoxelTissue.Flesh));
        Assert.IsTrue(grid.IsOriginalSurface(0, 0, 0));
        Assert.IsFalse(grid.IsOriginalSurface(4, 1, 1), "the core is not skin");
    }

    [TestMethod]
    public void Carve_DigsFromTheSurfaceIn_AndBoneStopsAWeakBlow()
    {
        var grid = Rod();
        var removed = new List<RemovedVoxel>();

        // 上から下へ（−Y）の弱い 1 発: 表面の肉だけが削れ、骨の手前で止まる。
        grid.Carve(new Vector3(0f, 0.02f, 0f), -Vector3.UnitY, 4f, removed);

        Assert.HasCount(4, removed);
        Assert.IsTrue(removed.All(r => r.Material == Flesh), "a light blow does not reach the bone");
        Assert.IsTrue(removed[0].WasSurface, "the skin goes first");
        Assert.AreEqual(40, grid.Remaining(Bone));
        Assert.AreEqual(116, grid.Remaining(Flesh));
    }

    [TestMethod]
    public void Carve_BoneGivesWayOnlyAfterEnoughBlows_AndTheWoundIsStained()
    {
        var grid = Rod();
        var removed = new List<RemovedVoxel>();
        for (var i = 0; i < 6; i++)
        {
            grid.Carve(new Vector3(0.005f, 0.02f, 0f), -Vector3.UnitY, 6f, removed);
        }

        Assert.IsGreaterThan(0, removed.Count(r => r.Material == Bone), "repeated blows break into the bone");
        Assert.IsGreaterThan(0f, grid.Stain(8, 3, 2), "the skin next to the wound is bloodied");
        Assert.IsLessThan(grid.Stain(8, 3, 2), grid.Stain(0, 0, 3), "the stain fades with distance from the wound");
    }

    [TestMethod]
    public void BoneBreak_IsFound_AndTheFarSideSplitsOff()
    {
        var grid = Rod(gapAt: 4);

        Assert.AreEqual(4, grid.FindBoneBreak(axis: 0, fromHigh: false));
        Assert.IsNull(Rod().FindBoneBreak(axis: 0, fromHigh: false), "a whole bone has no break");

        var split = grid.Split(axis: 0, slice: 4, takeHigh: true);

        Assert.IsNotNull(split);
        var (piece, offset) = split.Value;
        Assert.AreEqual((5, 4, 4), piece.Dimensions, "slices 5..9 move");
        Assert.AreEqual(0.025f, offset.X, 1e-6f, "the piece's centre sits halfway along the moved slices");
        Assert.AreEqual(80, piece.FilledCount);
        Assert.AreEqual(80, grid.FilledCount, "slices 0..4 stay");
        var bounds = grid.OccupiedBounds()!.Value;
        Assert.AreEqual(-0.025f, bounds.Center.X, 1e-6f, "what is left is the near half");
    }

    // ── エンジン: 剛体 ──

    [TestMethod]
    public void Reshape_KeepsTheBodyAndItsJoint_WithANewColliderAndMass()
    {
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        var a = world.AddDynamicBox(Vector3.Zero, new Vector3(0.05f, 0.02f, 0.02f), 1f);
        var b = world.AddDynamicBox(new Vector3(0.1f, 0f, 0f), new Vector3(0.05f, 0.02f, 0.02f), 1f);
        var joint = world.AddBallSocket(a, b, new Vector3(0.05f, 0f, 0f), new Vector3(-0.05f, 0f, 0f));

        world.ReshapeDynamicBox(a, new Vector3(-0.025f, 0f, 0f), new Vector3(0.025f, 0.02f, 0.02f), 0.5f);
        world.ApplyLinearImpulse(b, new Vector3(0f, 0.2f, 0f));
        world.Step(120, 1f / 120f);

        Assert.AreEqual(0.5f, world.GetMass(a));
        Assert.IsTrue(world.HasConstraint(joint));
        var anchors = world.GetJointAnchors(joint)!.Value;
        Assert.IsLessThan(0.005f, Vector3.Distance(anchors.AnchorA, anchors.AnchorB), "the joint still holds after the reshape");
        Assert.IsTrue(float.IsFinite(world.GetState(a).Position.X));
    }

    [TestMethod]
    public void Sever_BreaksOffTheFarPiece_AsANewBody_WithItsShareOfTheMass()
    {
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        var id = world.AddDynamicBox(Vector3.Zero, new Vector3(0.05f, 0.02f, 0.02f), 1.6f);
        world.SetLinearVelocity(id, new Vector3(0f, 0f, 1f));
        var body = new VoxelBody(world, id, Rod(gapAt: 4));

        var fragment = body.Sever(axis: 0, distalHigh: true);

        Assert.IsNotNull(fragment);
        Assert.AreNotEqual(id, fragment.BodyId);
        Assert.AreEqual(0.025f, world.GetState(fragment.BodyId).Position.X, 1e-5f, "the piece stays where its cells were");
        Assert.AreEqual(0.8f, world.GetMass(fragment.BodyId), 1e-4f, "half the cells, half the mass");
        Assert.AreEqual(0.8f, world.GetMass(id), 1e-4f);
        Assert.AreEqual(1f, world.GetState(fragment.BodyId).LinearVelocity.Z, 1e-5f, "it flies on with the body");
        Assert.IsNull(body.Sever(axis: 0, distalHigh: true), "nothing more to break");
    }

    [TestMethod]
    public void Debris_FallsBouncesAndRests_ThenFadesAway()
    {
        var debris = new VoxelDebris(capacity: 4, groundY: 0f) { LifeSeconds = 2f };
        debris.Spawn(new Vector3(0f, 0.5f, 0f), new Vector3(0.3f, 1f, 0f), Quaternion.Identity, new Vector3(0.01f), new Vector3(0f, 0f, 10f), Vector4.One);
        for (var i = 0; i < 90; i++)
        {
            debris.Step(1f / 60f);
        }

        var instances = new List<PrimitiveInstance>();
        debris.WriteInstances(instances);
        Assert.HasCount(1, instances);
        Assert.AreEqual(0.005f, instances[0].Model.Translation.Y, 1e-3f, "resting on the floor");

        for (var i = 0; i < 40; i++)
        {
            debris.Step(1f / 60f);
        }

        Assert.AreEqual(0, debris.Count, "gone after its life");
    }

    [TestMethod]
    public void Volume_IsTheGridAsA3DTexture_FilledOpaqueAndEmptyClear_AndFollowsChanges()
    {
        var grid = Rod();
        var volume = VoxelVolume.For(grid);
        int Opaque() => Enumerable.Range(0, volume.CellCount).Count(i => volume.Rgba[(4 * i) + 3] == 255);

        Assert.AreEqual((10, 4, 4), (volume.Width, volume.Height, volume.Depth));
        Assert.AreEqual(160, Opaque());
        var revision = volume.Revision;
        volume.Sync(grid);
        Assert.AreEqual(revision, volume.Revision, "an unchanged grid is not copied again");

        grid.Carve(new Vector3(0f, 0.02f, 0f), -Vector3.UnitY, 4f, []);
        volume.Sync(grid);

        Assert.AreEqual(revision + 1, volume.Revision, "a carved grid is a new revision to upload");
        Assert.AreEqual(156, Opaque(), "carved cells are clear: the GPU puts a face where they meet filled ones");
        var corner = Vector3.Transform(Vector3.Zero, VoxelVolume.CellToWorld(grid, Matrix4x4.CreateTranslation(1f, 2f, 3f)));
        Assert.AreEqual(new Vector3(0.95f, 1.98f, 2.98f), corner, "cell (0,0,0)'s corner is the box's corner");
    }

    // ── ゲーム ──

    [TestMethod]
    public void BlowsToTheForearm_CutItOff_AndTheGloveFallsWithIt()
    {
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        var forearm = cpu.ForearmId(Hand.Left);
        StrikeUntil(game, cpu, forearm, () => cpu.ArmSevered(Hand.Left), damage: 20f);

        Assert.IsTrue(cpu.ArmSevered(Hand.Left));
        Assert.AreNotEqual(forearm, cpu.GloveBody(Hand.Left), "the glove is on the piece that came off");
        Assert.HasCount(1, cpu.Voxels!.Fragments);
        Assert.AreEqual(0f, cpu.ArmStrength(Hand.Left));
        var glove = cpu.GloveCenter(Hand.Left).Y;
        for (var i = 0; i < 60; i++)
        {
            game.Step(default, 1f / 60f);
        }

        Assert.IsLessThan(glove - 0.05f, cpu.GloveCenter(Hand.Left).Y, "the cut-off hand drops");
        Assert.IsFalse(cpu.ArmSevered(Hand.Right));
    }

    [TestMethod]
    public void TheCutOffPiece_IsTheFarSide_WithTheGlove()
    {
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        var forearm = cpu.ForearmId(Hand.Right);
        StrikeUntil(game, cpu, forearm, () => cpu.ArmSevered(Hand.Right), damage: 20f);

        var glove = cpu.GloveCenter(Hand.Right);
        var piece = game.World.GetState(cpu.GloveBody(Hand.Right)).Position;
        var stub = game.World.GetState(forearm).Position;
        Assert.IsLessThan(Vector3.Distance(stub, glove), Vector3.Distance(piece, glove), "the piece that fell is the hand end");
    }

    [TestMethod]
    public void ACutOffArm_CannotGrab_AndItsLooseForearmIsNoLongerPartOfTheBody()
    {
        using var game = new BoxingGame(seed: 2);
        var player = game.Player;
        StrikeUntil(game, player, player.Parts.RightUpperArm, () => player.ArmSevered(Hand.Left), damage: 20f);

        Assert.IsFalse(player.Owns(player.ForearmId(Hand.Left)), "the forearm hangs from the cut-off piece now");
        Assert.IsTrue(player.Owns(player.Parts.RightUpperArm), "the stub stays on the body");
        var reach = player.ArmReach;
        for (var i = 0; i < 90; i++)
        {
            game.Step(new BoxingInput(new FighterCommand(Vector3.Zero, Vector3.UnitZ,
                new HandCommand(new Vector3(0f, 0f, reach), Grip: true), new HandCommand(new Vector3(0f, 0f, 0.5f * reach), false))), 1f / 60f);
        }

        Assert.IsFalse(player.IsHolding(Hand.Left));
    }

    [TestMethod]
    public void CarvedFlesh_WeakensTheArm_ButDoesNotDisableIt()
    {
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        var upper = cpu.Parts.RightUpperArm; // Hand.Left の上腕（Aigis の Right 側）
        for (var i = 0; i < 4; i++)
        {
            Strike(game, cpu, upper, damage: 8f, Vector3.UnitZ);
        }

        Assert.IsLessThan(1f, cpu.ArmStrength(Hand.Left));
        Assert.IsGreaterThan(0.5f, cpu.ArmStrength(Hand.Left));
        Assert.AreEqual(1f, cpu.ArmStrength(Hand.Right));
    }

    [TestMethod]
    public void DestroyingTheBrain_Kills_AndEndsTheRound()
    {
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        StrikeUntil(game, cpu, cpu.Parts.Head, () => cpu.IsDead, damage: 30f);

        Assert.AreEqual("brain", cpu.DeathCause);
        game.Step(default, 1f / 60f);
        Assert.IsTrue(game.RoundOver);
        Assert.AreEqual(1, game.PlayerWins);
        Assert.Contains("BRAIN", game.Message);
    }

    [TestMethod]
    public void ALegCutOff_LeavesTheFighterDown_UnableToStand()
    {
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        StrikeUntil(game, cpu, cpu.Parts.LeftThigh, () => cpu.LegSevered, damage: 25f);

        Assert.IsFalse(cpu.CanStand);
        for (var i = 0; i < 60 * 4; i++)
        {
            game.Step(default, 1f / 60f);
        }

        Assert.AreEqual(FighterState.Down, cpu.State, "down and not getting up");
    }

    [TestMethod]
    public void EveryOrganIsInsideItsPart_AndBonesRunAlongTheLimbs()
    {
        var tuning = FighterTuning.Default;
        var half = tuning.HalfExtents;
        var head = BoxerAnatomy.Build(BodyPartKind.Head, half.Head, tuning.VoxelSize);
        var chest = BoxerAnatomy.Build(BodyPartKind.Chest, half.Chest, tuning.VoxelSize);
        var forearm = BoxerAnatomy.Build(BodyPartKind.Forearm, half.Forearm, tuning.VoxelSize);

        Assert.IsGreaterThan(30, head.Initial(BoxerAnatomy.Brain));
        Assert.IsGreaterThan(10, chest.Initial(BoxerAnatomy.Heart));
        Assert.IsGreaterThan(10, chest.Initial(BoxerAnatomy.Liver));
        Assert.IsNull(forearm.FindBoneBreak(axis: 0, fromHigh: false), "the forearm bone runs its whole length");
        foreach (var grid in new[] { head, chest })
        {
            for (var i = 0; i < grid.CellCount; i++)
            {
                var (x, y, z) = grid.Coordinates(i);
                var material = grid.Palette[grid.At(x, y, z)];
                if (material.Tissue == VoxelTissue.Organ && grid.At(x, y, z) != BoxerAnatomy.Eye)
                {
                    Assert.IsFalse(grid.IsOriginalSurface(x, y, z), $"{material.Name} is never on the skin");
                }
            }
        }
    }

    [TestMethod]
    public void Remap_MovesCellsToAnotherMaterial_WithCountsAndToughness()
    {
        var grid = Rod();

        grid.Remap((_, m, surface) => surface ? Bone : m);
        var removed = new List<RemovedVoxel>();
        grid.Carve(new Vector3(0f, 0.02f, 0f), -Vector3.UnitY, 4f, removed);

        // 4 x 4 の断面では外周の 12 個が全部表面（肉はすべて表面にあった）。
        Assert.AreEqual(160, grid.Initial(Bone), "the whole outer layer is bone now, on top of the core");
        Assert.AreEqual(0, grid.Initial(Flesh));
        Assert.IsEmpty(removed, "4 units do not break one bone cell (toughness 8)");
    }

    [TestMethod]
    public void TheBody_IsCoveredInSkin_AndTheEyesAreOrgansThatDoNotKill()
    {
        var tuning = FighterTuning.Default;
        var head = BoxerAnatomy.Build(BodyPartKind.Head, tuning.HalfExtents.Head, tuning.VoxelSize,
            LowPoly.BoxerModel.Build(tuning.HalfExtents, tuning.ModelSubdivisions).Mesh(BodyPartKind.Head));
        var eye = BoxerAnatomy.Palette[BoxerAnatomy.Eye];

        Assert.AreEqual(VoxelTissue.Organ, eye.Tissue);
        Assert.IsFalse(eye.Vital);
        Assert.IsGreaterThan(0, head.Initial(BoxerAnatomy.Eye), "the face has eyes");
        Assert.IsGreaterThan(0, head.Initial(BoxerAnatomy.Skin));
        for (var i = 0; i < head.CellCount; i++)
        {
            var (x, y, z) = head.Coordinates(i);
            if (head.IsOriginalSurface(x, y, z))
            {
                Assert.IsTrue(head.At(x, y, z) is BoxerAnatomy.Skin or BoxerAnatomy.Eye, "only skin and eyes show before any damage");
            }
        }
    }

    [TestMethod]
    public void EachVoxelBreaksWithItsOwnGrain_AndTheGrainsFollowEachOther()
    {
        var skin = VoxelBreakSound.Grain(GrainKind.Skin, 1);
        var bone = VoxelBreakSound.Grain(GrainKind.Bone, 1);
        var brain = VoxelBreakSound.Grain(GrainKind.Brain, 1);
        Assert.IsLessThan(bone.Length, skin.Length, "a skin tick is shorter than a bone crack");
        Assert.IsLessThan(brain.Length, bone.Length, "and an organ breaks longest");
        Assert.AreEqual(GrainKind.Organ, VoxelBreakSound.KindOf(BoxerAnatomy.Heart));
        Assert.AreEqual(GrainKind.Eye, VoxelBreakSound.KindOf(BoxerAnatomy.Eye));

        // 3 粒を 0・50・100ms に: それぞれの粒の頭で音が立ち上がる。
        var mix = VoxelBreakSound.Mix([new ShatterGrain(BoxerAnatomy.Bone, 0f), new ShatterGrain(BoxerAnatomy.Bone, 0.05f), new ShatterGrain(BoxerAnatomy.Bone, 0.1f)], seed: 3);
        float Energy(float from, float to) => mix.Skip((int)(from * 44100)).Take((int)((to - from) * 44100)).Sum(v => v * v);
        Assert.IsGreaterThan(Energy(0.035f, 0.049f) * 2f, Energy(0.05f, 0.064f), "the second grain starts at 50 ms");

        var fatal = VoxelBreakSound.Fatal(seed: 7);
        Assert.AreEqual((int)(1.8f * 44100) * 2, fatal.Length);
    }

    [TestMethod]
    public void BreakingVoxels_IsHeard_AndTheKillingBlowIsFatal()
    {
        using var game = new BoxingGame(seed: 2);
        var cpu = game.Cpu;
        var heard = new List<ShatterEvent>();
        var directions = new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitY };
        for (var i = 0; i < 200 && !cpu.IsDead; i++)
        {
            game.Step(default, 1f / 60f); // 前の 1 発の事象を消す
            Strike(game, cpu, cpu.Parts.Head, 30f, directions[i % 3]);
            heard.AddRange(game.RecentShatters);
        }

        Assert.IsTrue(cpu.IsDead);
        Assert.IsTrue(heard.Any(e => e.Grains.Any(g => g.Material == BoxerAnatomy.Skin)), "skin breaks first");
        Assert.IsTrue(heard.Any(e => e.Grains.Any(g => g.Material == BoxerAnatomy.Brain)));
        Assert.IsTrue(heard[^1].Fatal, "the blow that killed is the fatal one");
        Assert.AreEqual(1, heard.Count(e => e.Fatal));
        Assert.IsTrue(heard[^1].Grains.Zip(heard[^1].Grains.Skip(1)).All(p => p.Second.Delay >= p.First.Delay - 0.005f), "grains follow one after another");
    }

    [TestMethod]
    public void Debris_ReportsEachHardLanding_WithItsTag_AndNotTheRestingPieces()
    {
        var debris = new VoxelDebris(capacity: 4, groundY: 0f);
        debris.Spawn(new Vector3(0f, 0.5f, 0f), Vector3.Zero, Quaternion.Identity, new Vector3(0.01f), Vector3.Zero, Vector4.One, tag: 7);
        debris.Spawn(new Vector3(0.2f, 0.006f, 0f), Vector3.Zero, Quaternion.Identity, new Vector3(0.01f), Vector3.Zero, Vector4.One, tag: 8);
        var landed = new List<DebrisLanding>();
        for (var i = 0; i < 120; i++)
        {
            debris.Step(1f / 60f, landed);
        }

        Assert.IsNotEmpty(landed, "the falling piece hits the floor");
        Assert.IsTrue(landed.All(l => l.Tag == 7), "the piece that starts at rest on the floor is never heard");
        Assert.IsGreaterThan(2.5f, landed[0].Speed, "a 0.5 m drop lands at about 3 m/s");
        Assert.AreEqual(0.01f, landed[0].Size, 1e-4f);
    }

    [TestMethod]
    public void EveryBreakStyle_SoundsEveryVoxel_AndEveryLanding_ButSparksAreSilent()
    {
        foreach (var style in Enum.GetValues<BreakStyle>())
        {
            foreach (var kind in Enum.GetValues<GrainKind>())
            {
                var grain = VoxelBreakSound.Grain(kind, 1, style);
                Assert.IsGreaterThan(0.01f, grain.Max(MathF.Abs), $"{style} {kind} is audible");
            }

            foreach (var tag in new[] { BoxerAnatomy.Skin, BoxerAnatomy.Bone, VoxelBreakSound.BloodTag, VoxelBreakSound.DustTag })
            {
                Assert.IsNotNull(VoxelBreakSound.Landings([new DebrisLanding(default, 2f, 0.005f, tag)], 1, style), $"{style} landing {tag}");
            }

            Assert.IsNull(VoxelBreakSound.Landings([new DebrisLanding(default, 2f, 0.005f, VoxelBreakSound.SparkTag)], 1, style));
            Assert.AreEqual((int)(1.8f * 44100) * 2, VoxelBreakSound.Fatal(7, style).Length, $"{style} fatal");
        }

        // 既定のガラスでも、臓器と脳は金属の粒。皮膚と骨はガラスのまま。
        CollectionAssert.AreEqual(VoxelBreakSound.Grain(GrainKind.Brain, 3, BreakStyle.Metal), VoxelBreakSound.Grain(GrainKind.Brain, 3, BreakStyle.Glass));
        CollectionAssert.AreEqual(VoxelBreakSound.Grain(GrainKind.Organ, 3, BreakStyle.Metal), VoxelBreakSound.Grain(GrainKind.Organ, 3, BreakStyle.Glass));
        CollectionAssert.AreNotEqual(VoxelBreakSound.Grain(GrainKind.Bone, 3, BreakStyle.Metal), VoxelBreakSound.Grain(GrainKind.Bone, 3, BreakStyle.Glass));

        // 床に落ちる音は小さい: 24 個が一度に強く落ちても、ピークは砕ける音の 1 割ほど。
        static float Peak(byte[] pcm) => Enumerable.Range(0, pcm.Length / 2).Max(i => Math.Abs((int)BitConverter.ToInt16(pcm, 2 * i))) / 32768f;
        var rain = VoxelBreakSound.Landings([.. Enumerable.Repeat(new DebrisLanding(default, 3f, 0.006f, BoxerAnatomy.Bone), 24)], 1)!;
        var gentle = VoxelBreakSound.Landings([new DebrisLanding(default, 0.4f, 0.003f, BoxerAnatomy.Skin)], 1)!;
        Assert.IsLessThanOrEqualTo(VoxelBreakSound.LandingLoudness + 0.01f, Peak(rain));
        Assert.IsLessThan(Peak(rain), Peak(gentle), "a gentle landing is quieter still");

        Assert.AreEqual(LandingKind.Hard, VoxelBreakSound.LandingKindOf(BoxerAnatomy.Bone));
        Assert.AreEqual(LandingKind.Chunk, VoxelBreakSound.LandingKindOf(BoxerAnatomy.Heart));
    }

    [TestMethod]
    public void BreakSoundOption_PicksTheStyle_DefaultsToGlass_AndRejectsUnknownNames()
    {
        Assert.IsTrue(BreakSoundOption.TryParse([], out var style, out _));
        Assert.AreEqual(BreakStyle.Glass, style);
        Assert.IsTrue(BreakSoundOption.TryParse(["--replay", "x", "--break-sound", "Crunch"], out style, out _));
        Assert.AreEqual(BreakStyle.Crunch, style);
        Assert.IsFalse(BreakSoundOption.TryParse(["--break-sound", "bell"], out _, out var error));
        StringAssert.Contains(error, "glass|crumble|crunch|circuit|metal");
        Assert.IsFalse(BreakSoundOption.TryParse(["--break-sound"], out _, out _));
        Assert.IsFalse(BreakSoundOption.TryParse(["--break-sound", "7"], out _, out _), "numbers are not names");
        Assert.IsFalse(BreakSoundOption.TryParse(["--break-sound", "0"], out _, out _), "not even a defined one");
        Assert.IsFalse(BreakSoundOption.TryParse(["--break-sound", "glass,crunch"], out _, out _), "one style at a time");
    }

    [TestMethod]
    public void Stains_TintTheSkin_ButLeaveBoneAndOrgansTheirOwnColor()
    {
        var palette = BoxerAnatomy.Palette;
        Assert.AreEqual(1f, palette[BoxerAnatomy.Skin].Stainable);
        Assert.AreEqual(0f, palette[BoxerAnatomy.Bone].Stainable);
        Assert.AreEqual(0f, palette[BoxerAnatomy.Brain].Stainable);

        // 隣り合う組織の色がはっきり違う（RGB の距離）。
        var tissues = new[] { BoxerAnatomy.Skin, BoxerAnatomy.Muscle, BoxerAnatomy.Bone, BoxerAnatomy.Brain, BoxerAnatomy.Heart, BoxerAnatomy.Lung, BoxerAnatomy.Liver, BoxerAnatomy.Gut };
        foreach (var a in tissues)
        {
            foreach (var b in tissues.Where(b => b > a))
            {
                var d = Vector3.Distance(new Vector3(palette[a].Color.X, palette[a].Color.Y, palette[a].Color.Z), new Vector3(palette[b].Color.X, palette[b].Color.Y, palette[b].Color.Z));
                Assert.IsGreaterThan(0.2f, d, $"{palette[a].Name} vs {palette[b].Name}");
            }
        }
    }

    private static void StrikeUntil(BoxingGame game, Fighter victim, int part, Func<bool> done, float damage)
    {
        var directions = new[] { Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitY, -Vector3.UnitY };
        for (var i = 0; i < 200 && !done(); i++)
        {
            Strike(game, victim, part, damage, directions[i % directions.Length]);
        }

        Assert.IsTrue(done(), "the blows got through");
    }

    /// <summary>部位の中心へ、<paramref name="from"/> の側の外から 1 発。</summary>
    private static void Strike(BoxingGame game, Fighter victim, int part, float damage, Vector3 from)
    {
        var s = game.World.GetState(part);
        var point = s.Position + (Vector3.Transform(from, s.Orientation) * 0.08f);
        game.Strike(victim, part, point, s.Position - point, damage);
    }
}
