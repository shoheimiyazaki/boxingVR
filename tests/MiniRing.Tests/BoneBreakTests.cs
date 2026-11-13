using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 骨が断たれる・折れる（<see cref="BoneBreak"/>、2026-11-13）: 関節の端の骨が削り取られても切れたと分かる、肉が残れば折れた先がぶら下がる、
/// グローブの強い 1 発は骨を曲げで折る、使えなくなった腕の手は握っていた武器をしばらくして放す。
/// </summary>
[TestClass]
public sealed class BoneBreakTests
{
    private const byte Flesh = 1;
    private const byte Bone = 2;

    private static readonly VoxelPalette Palette = new(
    [
        new VoxelMaterial("empty", VoxelTissue.Empty, 1f, false, Vector4.Zero, Vector4.Zero),
        new VoxelMaterial("flesh", VoxelTissue.Flesh, 1f, false, Vector4.One, Vector4.One),
        new VoxelMaterial("bone", VoxelTissue.Bone, 8f, false, Vector4.One, Vector4.One),
    ]);

    /// <summary>10 × 4 × 4 の棒: 真ん中の 2 × 2 が骨、まわりが肉。</summary>
    private static VoxelGrid Rod() => new(Palette, new Vector3(0.05f, 0.02f, 0.02f), (10, 4, 4), (_, n) =>
        MathF.Abs(n.Y) < 0.5f && MathF.Abs(n.Z) < 0.5f ? Bone : Flesh);

    private static void Clear(VoxelGrid grid, int slice, bool boneOnly)
    {
        var removed = new List<RemovedVoxel>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.Coordinates(i).X == slice && grid.MaterialAt(i) != 0 && (!boneOnly || grid.MaterialAt(i) == Bone))
            {
                grid.Break(i, removed);
            }
        }
    }

    // ── エンジン: 格子 ──

    [TestMethod]
    public void BoneCarvedAwayAtItsEnd_IsABreak_OnlyWhereAJointHoldsThatEnd()
    {
        var grid = Rod();
        Clear(grid, 9, boneOnly: true);

        Assert.IsNull(grid.FindBoneBreak(axis: 0, fromHigh: false), "without a joint, the bone only got shorter");
        Assert.AreEqual(9, grid.FindBoneBreak(axis: 0, fromHigh: false, lowJoint: -1, highJoint: 10), "the joint beyond slice 9 has lost the bone it held");
        Assert.IsNull(Rod().FindBoneBreak(axis: 0, fromHigh: false, lowJoint: -1, highJoint: 10), "a whole bone between two joints has no break");
    }

    [TestMethod]
    public void Bridge_IsTheFleshLeftAcrossTheBrokenBone_AndNothingWhenCutClean()
    {
        var grid = Rod();
        Clear(grid, 4, boneOnly: true);
        Assert.HasCount(12, grid.Bridge(axis: 0, slice: 4), "the ring of flesh around the gap");

        var bent = Rod(); // 曲げで折れた骨: 骨のセルは残っているが、骨は橋にならない
        Assert.HasCount(12, bent.Bridge(axis: 0, slice: 4));

        var clean = Rod();
        Clear(clean, 4, boneOnly: false);
        Assert.IsEmpty(clean.Bridge(axis: 0, slice: 4));
    }

    [TestMethod]
    public void Bending_OnlyTheBoneCarriesTheSection_InALimb()
    {
        var bone = new VoxelMaterial("bone", VoxelTissue.Bone, 8f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(1900f, 8e6f, 0.0005f, Tensile: 3e6f, Stiffness: 1e9f));
        var flesh = new VoxelMaterial("flesh", VoxelTissue.Flesh, 1f, false, Vector4.One, Vector4.One, Mechanics: new VoxelMechanics(1060f, 0.1e6f, 0.005f));
        var palette = new VoxelPalette([Palette[0], flesh, bone]);
        var limb = new VoxelGrid(palette, new Vector3(0.05f, 0.02f, 0.02f), (10, 4, 4), (_, n) => MathF.Abs(n.Y) < 0.5f && MathF.Abs(n.Z) < 0.5f ? Bone : Flesh);

        Assert.IsNull(VoxelBending.FindBreak(limb, Vector3.Zero, new Vector3(0f, 0f, -400f)), "a weapon's rule: flesh never snaps, so nothing does");
        Assert.IsNull(VoxelBending.FindBreak(limb, Vector3.Zero, new Vector3(0f, 0f, -5f), beamAxis: 0, loadBearingOnly: true), "a light push holds");
        var broke = VoxelBending.FindBreak(limb, Vector3.Zero, new Vector3(0f, 0f, -400f), beamAxis: 0, loadBearingOnly: true);
        Assert.IsNotNull(broke, "a hard push in the middle snaps the bone");
        Assert.IsInRange(3, 6, broke.Value.Slice, "near the middle");
    }

    [TestMethod]
    public void AGloveBlow_BreaksTheForearmBone_OnlyWhenItIsHardAndHasTheArmBehindIt()
    {
        static bool Bends(float speed, float behind)
        {
            var tuning = FighterTuning.Default;
            using var world = new PhysicsWorld(gravity: Vector3.Zero);
            var half = tuning.HalfExtents.Forearm;
            var id = world.AddDynamicBox(Vector3.Zero, Quaternion.Identity, half, tuning.Masses.Forearm);
            var limb = new VoxelBody(world, id, BoxerAnatomy.Build(BodyPartKind.Forearm, half, tuning.VoxelSize)) { BeamAxis = 0 };
            var glove = Weapon.CreateWorn(world, WeaponKind.Glove, BoxerAnatomy.RedGlove, new Vector3(0f, 0f, half.Z + 0.03f), Quaternion.Identity);
            world.SetLinearVelocity(glove.Body.BodyId, new Vector3(0f, 0f, -speed));
            world.SetPairCollisionEnabled(id, glove.Body.BodyId, false);
            for (var i = 0; i < 60; i++)
            {
                var sg = world.GetState(glove.Body.BodyId);
                var sl = world.GetState(id);
                world.Step(1f / 240f);
                if (VoxelContact.Resolve(glove.Body, sg, limb, sl, massBehindA: behind).BentFor(limb) is not null)
                {
                    return true;
                }
            }

            return false;
        }

        Assert.IsFalse(Bends(2f, 2f), "an ordinary punch (2 m/s with the arm behind it) does not break the bone");
        Assert.IsFalse(Bends(4f, 0f), "the glove alone, without the arm behind it, does not either");
        Assert.IsTrue(Bends(4f, 2f), "a hard punch with the arm and body behind it does");
    }

    // ── 試合の中: 腕が断たれる・折れる ──

    private sealed class Scripted(Func<FighterSense, FighterCommand> read) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => read(sense);
    }

    private static FighterCommand Guard(in FighterSense s)
    {
        var face = s.OpponentChest - s.Self.Origin;
        face.Y = 0f;
        var reach = s.ArmReach;
        var left = new HandCommand(new Vector3(Hand.Left.LocalSide() * 0.15f * reach, 0.1f * reach, 0.45f * reach), false);
        return new FighterCommand(Vector3.Zero, Vector3.Normalize(face), left, WeaponSwing.GuardCommand(reach, SwingFamily.Bat));
    }

    private static BoxingGame TwoHandedClub() =>
        new(seed: 1, corners: new BoxingCorners("bat", _ => new Scripted(s => Guard(s)), _ => new StandGuard(), CpuWeapon: WeaponKind.WoodenClub, CpuTwoHanded: true));

    private static void Play(BoxingGame game, float seconds)
    {
        for (var t = 0f; t < seconds; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }
    }

    /// <summary>部位 <paramref name="part"/> の格子の、骨の軸に沿ったスライス <paramref name="slices"/> を消し、骨が切れたら試合に伝える。</summary>
    private static BoneBreak? Cut(BoxingGame game, Fighter fighter, int part, IEnumerable<int> slices, bool boneOnly = false)
    {
        var body = fighter.Voxels!;
        var grid = body.PartOf(part)!.Body.Grid;
        var removed = new List<RemovedVoxel>();
        foreach (var slice in slices)
        {
            for (var i = 0; i < grid.CellCount; i++)
            {
                if (grid.Coordinates(i).X == slice && grid.MaterialAt(i) != 0 && (!boneOnly || grid.Palette[grid.MaterialAt(i)].Tissue == VoxelTissue.Bone))
                {
                    grid.Break(i, removed);
                }
            }
        }

        var broken = body.BreakIfSevered(part);
        if (broken is not null)
        {
            game.BreakBone(fighter, broken, 1f);
        }

        return broken;
    }

    private static int Slices(BoxingGame game, int part) => game.Cpu.Voxels!.PartOf(part)!.Body.Grid.Dimensions.X;

    [TestMethod]
    public void TwoHanded_ASeveredSupportingArm_HoldsTheClubForAMoment_ThenLetsGo_AndTheOtherHandHoldsOn()
    {
        using var game = TwoHandedClub();
        Play(game, 1f);
        Assert.AreEqual(BoxingGame.SupportHand, game.Cpu.SupportOf(BoxingGame.WeaponHand), "both hands on the handle");

        var upper = game.Cpu.UpperArmId(BoxingGame.SupportHand);
        var cut = Cut(game, game.Cpu, upper, [Slices(game, upper) / 2]);
        Assert.IsNotNull(cut);
        Assert.IsFalse(cut.Hangs, "cut clean through");
        Assert.IsTrue(game.Cpu.ArmSevered(BoxingGame.SupportHand));
        Assert.IsNull(game.Cpu.SupportOf(BoxingGame.WeaponHand), "the cut-off hand no longer supports the club");
        Assert.IsTrue(game.Cpu.Wields(BoxingGame.WeaponHand), "the right hand holds on, one-handed");
        Assert.IsTrue(game.Cpu.HoldsOneHanded(BoxingGame.WeaponHand));
        Assert.AreEqual(1, game.Cpu.DeadGrips, "the dead hand still grips the handle");

        Play(game, 0.5f);
        Assert.AreEqual(1, game.Cpu.DeadGrips, "for a moment");
        Play(game, 4.5f);
        Assert.AreEqual(0, game.Cpu.DeadGrips, "within a few seconds its grip has gone");
    }

    [TestMethod]
    public void TheElbowEndCarvedAway_TheForearmIsNoLongerHeld_AndDropsTheClub()
    {
        using var game = TwoHandedClub();
        Play(game, 1f);
        var upper = game.Cpu.UpperArmId(BoxingGame.WeaponHand);
        var n = Slices(game, upper);
        // 肘の側の端の 2 スライスを、肉ごと削り取る（骨が両側に残る切れ目はできない）。
        // 肘の側の端から、骨の端のスライスまでを肉ごと削り取る（骨は格子の端の少し手前で終わっている）。
        var grid = game.Cpu.Voxels!.PartOf(upper)!.Body.Grid;
        bool HasBone(int s) => Enumerable.Range(0, grid.CellCount).Any(i => grid.Coordinates(i).X == s && grid.MaterialAt(i) != 0 && grid.Palette[grid.MaterialAt(i)].Tissue == VoxelTissue.Bone);
        var distalHigh = game.Cpu.Voxels!.PartOf(upper)!.DistalHigh;
        var elbowEnd = new List<int>();
        for (var s = distalHigh ? n - 1 : 0; s >= 0 && s < n; s += distalHigh ? -1 : 1)
        {
            elbowEnd.Add(s);
            if (HasBone(s))
            {
                break;
            }
        }

        var cut = Cut(game, game.Cpu, upper, elbowEnd);

        Assert.IsNotNull(cut, "the bone no longer reaches the elbow: a break");
        Assert.IsNull(cut.Fragment, "nothing left beyond it");
        Assert.IsTrue(game.Cpu.ArmSevered(BoxingGame.WeaponHand));
        Assert.IsFalse(game.Cpu.Owns(game.Cpu.ForearmId(BoxingGame.WeaponHand)), "the forearm is no longer part of the body");
        Assert.IsFalse(game.Cpu.Wields(BoxingGame.SupportHand), "the left hand lets go of a club its main hand lost");

        var elbowBefore = game.World.GetState(game.Cpu.ForearmId(BoxingGame.WeaponHand)).Position;
        var shoulder = game.World.GetState(upper).Position;
        Play(game, 1f);
        var forearm = game.World.GetState(game.Cpu.ForearmId(BoxingGame.WeaponHand)).Position;
        Assert.IsGreaterThan(0.1f, Vector3.Distance(forearm, elbowBefore), "the forearm fell away");
        Assert.IsGreaterThan(Vector3.Distance(elbowBefore, shoulder) + 0.05f, Vector3.Distance(forearm, game.World.GetState(upper).Position), "away from the stump");
    }

    [TestMethod]
    public void TheElbowsFleshCutAway_BeyondTheEndOfTheBone_TheForearmFalls()
    {
        using var game = TwoHandedClub();
        Play(game, 1f);
        var upper = game.Cpu.UpperArmId(BoxingGame.WeaponHand);
        var part = game.Cpu.Voxels!.PartOf(upper)!;
        var grid = part.Body.Grid;
        var n = Slices(game, upper);
        bool HasBone(int s) => Enumerable.Range(0, grid.CellCount).Any(i => grid.Coordinates(i).X == s && grid.MaterialAt(i) != 0 && grid.Palette[grid.MaterialAt(i)].Tissue == VoxelTissue.Bone);
        // 骨の端より先（肘を包む肉）の 1 スライスだけを、まるごと斬り取る。骨には触れない。

        var beyondBone = part.DistalHigh ? Enumerable.Range(0, n).Last(HasBone) + 1 : Enumerable.Range(0, n).First(HasBone) - 1;
        Assert.AreNotEqual(part.DistalHigh ? n : -1, beyondBone, "the bone stops short of the elbow end");

        var cut = Cut(game, game.Cpu, upper, [beyondBone]);
        Assert.IsNotNull(cut, "nothing joins the bone to the elbow any more");
        Assert.IsFalse(cut.Hangs);
        Assert.IsTrue(game.Cpu.ArmSevered(BoxingGame.WeaponHand));
        Assert.IsFalse(game.Cpu.Owns(game.Cpu.ForearmId(BoxingGame.WeaponHand)));

    }

    [TestMethod]
    public void AFistBlowThatOnlyGrazesTheElbowsFlesh_LeavesTheArmOn()
    {
        using var game = TwoHandedClub();
        Play(game, 1f);
        var upper = game.Cpu.UpperArmId(BoxingGame.WeaponHand);
        var part = game.Cpu.Voxels!.PartOf(upper)!;
        var grid = part.Body.Grid;
        var edge = part.DistalHigh ? Slices(game, upper) - 1 : 0;
        // 端のスライスの半分だけを削る（まだ肉が残っている）。
        var removed = new List<RemovedVoxel>();
        var inSlice = Enumerable.Range(0, grid.CellCount).Where(i => grid.Coordinates(i).X == edge && grid.MaterialAt(i) != 0).ToList();
        foreach (var i in inSlice.Take(inSlice.Count / 2))
        {
            grid.Break(i, removed);
        }

        Assert.IsNull(game.Cpu.Voxels!.BreakIfSevered(upper), "flesh is still there to hold the joint");
        Assert.IsFalse(game.Cpu.ArmSevered(BoxingGame.WeaponHand));
    }

    [TestMethod]
    public void AGloveOnABrokenArm_StaysOnTheHand_AndNeverHitsItsOwner()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));
        Play(game, 1f);
        var forearm = game.Cpu.ForearmId(Hand.Left);
        var glove = game.GloveOf(game.Cpu, Hand.Left)!;
        var n = Slices(game, forearm);
        game.BreakBone(game.Cpu, game.Cpu.Voxels!.BreakBent(forearm, new VoxelBreak(0, n / 2, true, 2f))!, 1f);
        Assert.IsNotNull(game.Cpu.DeadHandOn(glove.Body.BodyId), "the glove is still on the dangling hand");
        Assert.AreEqual(0, game.Cpu.DeadGrips, "a glove is worn, not gripped: it never slips off");

        var own = game.Cpu.Voxels!.Parts.Concat(game.Cpu.Voxels!.Fragments).Select(p => p.Body).ToHashSet();
        for (var t = 0f; t < 3f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            Assert.IsFalse(game.RecentCuts.Any(c => (c.A == glove.Body && own.Contains(c.B)) || (c.B == glove.Body && own.Contains(c.A))), "the dangling glove does not strike its own body");
        }

        Assert.IsNotNull(game.Cpu.DeadHandOn(glove.Body.BodyId), "still on after 3 s");
    }

    [TestMethod]
    public void ASecondBreak_TakesTheDeadHandsGripAlongWithTheHand()
    {
        using var game = TwoHandedClub();
        Play(game, 1f);
        var club = game.WeaponOf(game.Cpu)!;
        var upper = game.Cpu.UpperArmId(BoxingGame.SupportHand);
        game.BreakBone(game.Cpu, game.Cpu.Voxels!.BreakBent(upper, new VoxelBreak(0, Slices(game, upper) / 2, true, 2f))!, 1f);
        Assert.AreEqual(1, game.Cpu.DeadGrips);
        var forearm = game.Cpu.ForearmId(BoxingGame.SupportHand);
        Assert.AreEqual(forearm, game.Cpu.DeadHandOn(club.Body.BodyId)!.Value.Hand, "the hand is on the forearm");

        var cut = Cut(game, game.Cpu, forearm, [Slices(game, forearm) / 2]);
        Assert.IsNotNull(cut?.Fragment);
        Assert.AreEqual(cut.Fragment.Body.BodyId, game.Cpu.DeadHandOn(club.Body.BodyId)!.Value.Hand, "the grip went with the hand to the cut-off piece");
    }

    [TestMethod]
    public void ABentBone_LeavesThePieceHangingByTheFlesh_UndrivenAndLimp()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));
        Play(game, 1f);
        var forearm = game.Cpu.ForearmId(Hand.Left);
        var n = Slices(game, forearm);
        var broken = game.Cpu.Voxels!.BreakBent(forearm, new VoxelBreak(0, n / 2, true, 2f));
        Assert.IsNotNull(broken);
        game.BreakBone(game.Cpu, broken, 1f);
        Assert.IsTrue(broken.Hangs, "the flesh around the snapped bone holds the two pieces");
        Assert.IsGreaterThan(0f, broken.TearForce);
        Assert.HasCount(1, game.BrokenBones);
        Assert.IsTrue(game.Cpu.ArmSevered(Hand.Left), "a broken arm no longer punches");

        var piece = broken.Fragment!.Body.BodyId;
        Play(game, 2f);
        Assert.IsTrue(game.Cpu.HangsByFlesh(piece), "it still hangs on");
        var stub = game.World.GetState(forearm);
        var hanging = game.World.GetState(piece);
        Assert.IsLessThan(0.1f, Vector3.Distance(stub.Position, hanging.Position), "hanging close to where it broke");
    }

    /// <summary>骨の軸（部位のローカル X）のワールドの向き。</summary>
    private static Vector3 Axis(BoxingGame game, int body) => Vector3.Transform(Vector3.UnitX, game.World.GetState(body).Orientation);

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void FleshAtTheBreak_RemovedLater_ReleasesThePiece(bool fragmentSide, bool leg)
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));
        Play(game, 1f);
        var part = leg ? game.Cpu.Parts.LeftShin : game.Cpu.ForearmId(Hand.Left);
        var axis = leg ? 1 : 0;
        var dimensions = game.Cpu.Voxels!.PartOf(part)!.Body.Grid.Dimensions;
        var broken = game.Cpu.Voxels.BreakBent(part, new VoxelBreak(axis, (leg ? dimensions.Y : dimensions.X) / 2, true, 2f))!;
        game.BreakBone(game.Cpu, broken, 1f);
        var bridge = broken.Bridge!;
        Assert.IsTrue(game.Cpu.HangsByFlesh(broken.Fragment!.Body.BodyId));
        var removed = new List<RemovedVoxel>();
        foreach (var cell in bridge.Cells)
        {
            (fragmentSide ? bridge.Fragment.Grid : bridge.Stub.Grid).Break(fragmentSide ? cell.FragmentIndex : cell.StubIndex, removed);
        }
        Play(game, 0.1f);
        Assert.IsFalse(game.Cpu.HangsByFlesh(broken.Fragment.Body.BodyId), "no invisible link may survive missing flesh");
    }

    [TestMethod]
    public void ASecondProximalBreak_MovesTheBridge_AndStillTracksItsFlesh()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));
        Play(game, 1f);
        var forearm = game.Cpu.ForearmId(Hand.Left);
        var n = Slices(game, forearm);
        var first = game.Cpu.Voxels!.BreakBent(forearm, new VoxelBreak(0, n / 2, true, 2f))!;
        game.BreakBone(game.Cpu, first, 1f);
        var proximal = first.Part.DistalHigh ? n / 4 : n * 3 / 4;
        var second = game.Cpu.Voxels.BreakBent(forearm, new VoxelBreak(0, proximal, true, 2f))!;
        game.BreakBone(game.Cpu, second, 1f);
        Assert.IsTrue(game.Cpu.HangsByFlesh(first.Fragment!.Body.BodyId));
        var grid = second.Fragment!.Body.Grid;
        var removed = new List<RemovedVoxel>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.Palette[grid.MaterialAt(i)].Tissue == VoxelTissue.Flesh)
            {
                grid.Break(i, removed);
            }
        }
        Play(game, 0.1f);
        Assert.IsFalse(game.Cpu.HangsByFlesh(first.Fragment.Body.BodyId));
    }

    [TestMethod]
    public void RemainingSection_ContinuouslyReducesStiffnessAndTearStrength()
    {
        using var game = new BoxingGame(seed: 1);
        var forearm = game.Cpu.ForearmId(Hand.Left);
        var broken = game.Cpu.Voxels!.BreakBent(forearm, new VoxelBreak(0, Slices(game, forearm) / 2, true, 2f))!;
        var bridge = broken.Bridge!;
        var full = bridge.Measure();
        Assert.IsInRange(0.95f, 1f, full.Fraction);
        var removed = new List<RemovedVoxel>();
        foreach (var cell in bridge.Cells.Take(bridge.Cells.Count / 2))
        {
            bridge.Fragment.Grid.Break(cell.FragmentIndex, removed);
        }
        var half = bridge.Measure();
        Assert.IsGreaterThan(0f, half.Fraction);
        Assert.IsLessThan(full.Fraction, half.Fraction);
        Assert.IsLessThan(full.Tear, half.Tear);
        Assert.IsLessThan(full.Bend, half.Bend);
    }

    [TestMethod]
    public void NoFleshAtTheBreak_CreatesNoLink_AndThePieceFalls()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));
        Play(game, 1f);
        var forearm = game.Cpu.ForearmId(Hand.Left);
        var broken = Cut(game, game.Cpu, forearm, [Slices(game, forearm) / 2])!;
        Assert.IsFalse(broken.Hangs);
        var id = broken.Fragment!.Body.BodyId;
        Assert.IsFalse(game.Cpu.HangsByFlesh(id));
        var start = game.World.GetState(id).Position;
        Play(game, 0.4f);
        Assert.IsLessThan(start.Y - 0.05f, game.World.GetState(id).Position.Y);
    }

    [TestMethod]
    public void LessFlesh_AllowsMoreSagUnderTheSameWeight()
    {
        static float Sag(float fraction)
        {
            using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("reach", _ => new Scripted(s => Reach(s, high: false)), _ => new StandGuard()));
            Play(game, 1f);
            var forearm = game.Cpu.ForearmId(Hand.Left);
            var broken = game.Cpu.Voxels!.BreakBent(forearm, new VoxelBreak(0, Slices(game, forearm) / 2, true, 2f))!;
            var cells = broken.Bridge!.Cells;
            var removed = new List<RemovedVoxel>();
            foreach (var cell in cells.Skip((int)(cells.Count * fraction)))
            {
                broken.Bridge.Stub.Grid.Break(cell.StubIndex, removed);
            }
            game.BreakBone(game.Cpu, broken, 1f);
            Play(game, 3f);
            Assert.IsTrue(game.Cpu.HangsByFlesh(broken.Fragment!.Body.BodyId));
            var sum = 0f;
            for (var i = 0; i < 120; i++)
            {
                Play(game, 1f / 60f);
                sum += MathF.Acos(Math.Clamp(Vector3.Dot(Axis(game, forearm), Axis(game, broken.Fragment.Body.BodyId)), -1f, 1f));
            }
            return sum / 120f;
        }

        var full = Sag(1f);
        var half = Sag(0.5f);
        var thin = Sag(0.125f);
        Console.WriteLine($"Sag [rad]: full={full:F4}, half={half:F4}, thin={thin:F4}");
        Assert.IsInRange(0.3f, 0.9f, full, "full flesh retains the 0.6 rad calibration under gravity");
        Assert.IsGreaterThan(full, half);
        Assert.IsGreaterThan(half, thin);
    }

    [TestMethod]
    public void TheBrokenPiece_SagsUnderItsWeight_ButDoesNotHangStraightDown()
    {
        // 拳を前へ突き出したまま、前腕の真ん中で折る: 折れた先は下へたわむが、真下へは垂れない（肉のバネが重さの多くを支える）。
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("reach", _ => new Scripted(s => Reach(s, high: false)), _ => new StandGuard()));
        Play(game, 1f);
        var forearm = game.Cpu.ForearmId(Hand.Left);
        var broken = game.Cpu.Voxels!.BreakBent(forearm, new VoxelBreak(0, Slices(game, forearm) / 2, true, 2f))!;
        game.BreakBone(game.Cpu, broken, 1f);
        var piece = broken.Fragment!.Body.BodyId;
        Play(game, 2f);

        var stub = Axis(game, forearm);
        var hanging = Axis(game, piece);
        var bend = MathF.Acos(Math.Clamp(MathF.Abs(Vector3.Dot(stub, hanging)), -1f, 1f));
        var down = MathF.Abs(Vector3.Dot(hanging, -Vector3.UnitY));
        Assert.IsGreaterThan(10f * MathF.PI / 180f, bend, "it gives at the break (no bone holds it straight)");
        Assert.IsLessThan(0.9f, down, "but it does not hang straight down");
    }

    private static bool _high;

    private static FighterCommand Reach(in FighterSense s, bool high)
    {
        var face = s.OpponentChest - s.Self.Origin;
        face.Y = 0f;
        var r = s.ArmReach;
        var left = new HandCommand(new Vector3(0.15f * r, (high ? 0.7f : 0.1f) * r, 0.7f * r), false);
        var right = new HandCommand(new Vector3(-0.15f * r, 0.1f * r, 0.45f * r), false);
        return new FighterCommand(Vector3.Zero, Vector3.Normalize(face), left, right);
    }

    [TestMethod]
    public void TheArmAboveTheBreak_StillMovesToWhereTheInputPutsIt()
    {
        _high = false;
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("reach", _ => new Scripted(s => Reach(s, _high)), _ => new StandGuard()));
        Play(game, 1f);
        var forearm = game.Cpu.ForearmId(Hand.Left);
        var upper = game.Cpu.UpperArmId(Hand.Left);
        var broken = game.Cpu.Voxels!.BreakBent(forearm, new VoxelBreak(0, Slices(game, forearm) / 2, true, 2f))!;
        game.BreakBone(game.Cpu, broken, 1f);
        Play(game, 1f);
        var lowUpper = Axis(game, upper);
        var lowStub = game.World.GetState(forearm).Position.Y;

        _high = true; // 拳の目標を顔の高さへ上げる
        Play(game, 1f);
        var highUpper = Axis(game, upper);
        var highStub = game.World.GetState(forearm).Position.Y;

        Assert.IsGreaterThan(15f * MathF.PI / 180f, MathF.Acos(Math.Clamp(Vector3.Dot(lowUpper, highUpper), -1f, 1f)), "the shoulder still turns the upper arm to the input");
        Assert.IsGreaterThan(0.02f, highStub - lowStub, "and the forearm above the break rises with it");
        Assert.AreEqual(Vector3.Zero, game.Cpu.HandPush(Hand.Left), "nothing pulls the dangling hand");
    }
}
