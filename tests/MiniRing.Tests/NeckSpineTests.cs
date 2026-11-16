using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// 首の骨折・首の切断・脊椎（脊髄）の損傷（2026-11-16）: 首の骨は腕・脚の骨と同じ曲げの式（<see cref="VoxelBending"/>）で折れ、折れ目は肉のつなぎ
/// （<see cref="Fighter.Break"/> と同じ <c>Link</c>）で残り、肉まで断たれれば頭が離れて死ぬ。脊髄は削れた残りの断面の割合が、その下の筋の力を連続に決める。
/// </summary>
[TestClass]
public sealed class NeckSpineTests
{
    private const float Dt = 1f / 60f;

    private static BoxingGame Game() => new(seed: 1, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard()));

    private static void Play(BoxingGame game, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
        {
            game.Step(default, Dt);
        }
    }

    private static VoxelGrid Grid(BoxingGame game, int part) => game.Cpu.Voxels!.PartOf(part)!.Body.Grid;

    private static int[] Cells(VoxelGrid grid, Func<(int X, int Y, int Z), byte, bool> where)
    {
        var found = new List<int>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != 0 && where(grid.Coordinates(i), grid.MaterialAt(i)))
            {
                found.Add(i);
            }
        }

        return [.. found];
    }

    private static void Remove(VoxelGrid grid, IEnumerable<int> cells)
    {
        var removed = new List<RemovedVoxel>();
        foreach (var i in cells)
        {
            grid.Break(i, removed);
        }
    }

    // ── 首の骨折 ──

    [TestMethod]
    public void ThinnedNeckBone_BreaksUnderASideBlow_ThatAnIntactNeckHolds()
    {
        using var game = Game();
        var grid = Grid(game, game.Cpu.Parts.Head);
        // 頭の横（あごの高さ）から真横へ押す 1 点の力。
        var at = new Vector3(grid.HalfExtents.X, -0.5f * grid.HalfExtents.Y, 0f);
        var force = -Vector3.UnitX * 250f;
        Assert.IsNull(VoxelBending.FindBreak(grid, at, force, 1, loadBearingOnly: true, BoxerSpine.NeckSlices), "an intact neck holds 250 N");

        // 首の骨のセルを、各段 4 つのうち 3 つ削る（残った断面は 1/4）。
        var (low, high) = BoxerSpine.NeckSlices;
        var bone = Cells(grid, (c, m) => m == BoxerAnatomy.Bone && c.Y >= low && c.Y <= high);
        Assert.HasCount(8, bone, "four bone cells in each of the two neck slices");
        Remove(grid, bone.Where((_, k) => k % 4 != 0));

        var broke = VoxelBending.FindBreak(grid, at, force, 1, loadBearingOnly: true, BoxerSpine.NeckSlices);
        Assert.IsNotNull(broke, "the thinned neck snaps under the same blow");
        Assert.IsInRange(low, high, broke.Value.Slice, "in the neck, not the skull");
        Assert.IsFalse(broke.Value.TakeHigh, "the neck side is the piece that stays with the chest");
    }

    [TestMethod]
    public void TheSkull_NeverSnapsOnItsOwn_OnlyTheNeckDoes()
    {
        using var game = Game();
        var grid = Grid(game, game.Cpu.Parts.Head);
        var at = new Vector3(grid.HalfExtents.X, 0.7f * grid.HalfExtents.Y, 0f);
        // どんなに大きな力でも、折れる所の候補は首の段だけ。
        var broke = VoxelBending.FindBreak(grid, at, -Vector3.UnitX * 100000f, 1, loadBearingOnly: true, BoxerSpine.NeckSlices);
        Assert.IsNotNull(broke);
        Assert.IsInRange(BoxerSpine.NeckSlices.Low, BoxerSpine.NeckSlices.High, broke.Value.Slice);
    }

    [TestMethod]
    public void ABrokenNeck_LetsTheHeadHangOnTheFlesh_AndSagMoreThanAnIntactNeck()
    {
        static float Droop(bool broken)
        {
            using var game = Game();
            Play(game, 1f);
            var head = game.Cpu.Parts.Head;
            var chest = game.Cpu.Parts.Chest;
            if (broken)
            {
                var neck = game.Cpu.Voxels!.BreakBent(head, new VoxelBreak(1, BoxerSpine.NeckSlices.High, false, 2f));
                Assert.IsNotNull(neck);
                Assert.IsTrue(neck.Hangs, "the flesh around the snapped neck still holds the head");
                game.BreakBone(game.Cpu, neck, 1f);
                Assert.IsFalse(game.Cpu.IsDead, "a head hanging on the flesh is alive");
                Assert.IsTrue(game.Cpu.HangsByFlesh(head));
            }

            // 頭を前へ倒す回転を与え、少し経ってからの、頭と胴の上向きのずれを見る（重さで垂れるか）。
            game.World.ApplyAngularImpulse(head, Vector3.Transform(Vector3.UnitX, game.World.GetState(chest).Orientation) * 0.1f);
            var sum = 0f;
            var count = 0;
            for (var t = 0f; t < 1.5f; t += Dt)
            {
                game.Step(default, Dt);
                if (t > 0.5f)
                {
                    var up = Vector3.Transform(Vector3.UnitY, game.World.GetState(head).Orientation);
                    var chestUp = Vector3.Transform(Vector3.UnitY, game.World.GetState(chest).Orientation);
                    sum += MathF.Acos(Math.Clamp(Vector3.Dot(up, chestUp), -1f, 1f));
                    count++;
                }
            }

            return sum / count;
        }

        var intact = Droop(false);
        var snapped = Droop(true);
        Console.WriteLine($"mean angle of the head against the chest: intact {intact:F3} rad, broken neck {snapped:F3} rad");
        Assert.IsGreaterThan(intact + 0.3f, snapped, "the broken neck lets the head hang over (an intact neck brings it back)");
    }

    // ── 首の切断 ──

    [TestMethod]
    public void CuttingTheNeckThrough_DropsTheHead_AndTheBodyDies()
    {
        using var game = Game();
        Play(game, 1f);
        var head = game.Cpu.Parts.Head;
        var grid = Grid(game, head);
        var cut = BoxerSpine.NeckSlices.High;
        Remove(grid, Cells(grid, (c, _) => c.Y == cut)); // 皮膚・筋肉・骨・脊髄、その段のすべて
        var broken = game.Cpu.Voxels!.BreakIfSevered(head);
        Assert.IsNotNull(broken, "the bone no longer spans the neck");
        Assert.IsFalse(broken.Hangs, "no flesh is left across the cut");
        Assert.IsNotNull(broken.Fragment, "the neck's side stays with the chest");

        var before = game.World.GetState(head).Position;
        game.BreakBone(game.Cpu, broken, 1f);
        Assert.IsTrue(game.Cpu.IsDead, "the head is off: the body dies");
        Assert.AreEqual("neck", game.Cpu.DeathCause);
        Assert.IsFalse(game.Cpu.Owns(head), "the head is no longer part of the body");
        Assert.IsFalse(game.Cpu.HangsByFlesh(head));

        Play(game, 0.5f);
        Assert.IsTrue(game.RoundOver, "the match ends");
        var chestTop = game.World.GetState(game.Cpu.Parts.Chest).Position.Y + game.Cpu.HalfExtentsOf(game.Cpu.Parts.Chest).Y;
        Assert.IsLessThan(before.Y - 0.25f, game.World.GetState(head).Position.Y, "the head fell to the mat (it did not stay asleep in the air)");
        Assert.IsLessThan(chestTop, game.World.GetState(head).Position.Y);
    }

    [TestMethod]
    public void TheFleshAcrossTheNeck_TornAway_LaterOnLetsTheHeadGo()
    {
        using var game = Game();
        Play(game, 1f);
        var head = game.Cpu.Parts.Head;
        var broken = game.Cpu.Voxels!.BreakBent(head, new VoxelBreak(1, BoxerSpine.NeckSlices.High, false, 2f))!;
        game.BreakBone(game.Cpu, broken, 1f);
        Assert.IsTrue(game.Cpu.HangsByFlesh(head));
        var bridge = broken.Bridge!;
        var removed = new List<RemovedVoxel>();
        foreach (var cell in bridge.Cells)
        {
            bridge.Fragment.Grid.Break(cell.FragmentIndex, removed);
        }

        Play(game, 0.1f);
        Assert.IsFalse(game.Cpu.HangsByFlesh(head), "no invisible link may survive the missing flesh");
        Assert.IsTrue(game.Cpu.IsDead, "the head came off");
    }

    // ── 脊髄 ──

    private static void CutCord(BoxingGame game, int part, CordCut cut)
    {
        var grid = Grid(game, part);
        var cord = Cells(grid, (_, m) => m == BoxerAnatomy.Cord);
        Assert.IsNotEmpty(cord, "the part has a cord");
        var rows = cord.Select(i => grid.Coordinates(i).Y).Distinct().Order().ToList();
        var row = rows[rows.Count / 2];
        var inRow = cord.Where(i => grid.Coordinates(i).Y == row).ToList();
        Remove(grid, cut == CordCut.Half ? inRow.Take(inRow.Count / 2) : inRow);
    }

    private enum CordCut { Half, Whole }

    [TestMethod]
    public void HalfTheCervicalCord_WeakensArmsAndLegs_AllOfItLeavesTheBodyUnableToStand()
    {
        using var game = Game();
        Play(game, 1f);
        var head = game.Cpu.Parts.Head;
        var arm = game.Cpu.ArmStrength(Hand.Left);
        var legs = game.Cpu.LegCondition(true);
        Assert.IsTrue(game.Cpu.CanStand);

        CutCord(game, head, CordCut.Half);
        Assert.AreEqual(0.5f, game.Cpu.Voxels!.CordFunction(CordLevel.Cervical), 1e-4f, "half the cord's section is left");
        Play(game, 3f);
        Assert.IsLessThan(0.75f * arm, game.Cpu.ArmStrength(Hand.Left), "the arms are weaker");
        Assert.IsLessThan(0.75f * legs, game.Cpu.LegCondition(true), "so are the legs");
        Assert.IsTrue(game.Cpu.CanStand, "still standing with half the cord");

        CutCord(game, head, CordCut.Whole);
        Assert.AreEqual(0f, game.Cpu.Voxels.CordFunction(CordLevel.Cervical), 1e-4f);
        Play(game, 3f);
        Assert.IsFalse(game.Cpu.CanStand, "no signal below the neck: the body cannot stand");
        Assert.IsLessThan(0.25f, game.Cpu.ArmStrength(Hand.Left), "the arms hang (only the floor of the muscles' strength is left)");
        Assert.IsFalse(game.Cpu.IsDead, "a cut cord alone is not death");
    }

    [TestMethod]
    public void TheLumbarCordAlone_LeavesTheArmsStrong_AndTakesTheLegs()
    {
        using var game = Game();
        Play(game, 1f);
        var arm = game.Cpu.ArmStrength(Hand.Left);
        CutCord(game, game.Cpu.Parts.Pelvis, CordCut.Whole);
        Assert.AreEqual(1f, game.Cpu.Voxels!.CordFunction(CordLevel.Cervical), 1e-4f);
        Assert.AreEqual(0f, game.Cpu.Voxels.CordFunction(CordLevel.Lumbar), 1e-4f);
        Play(game, 3f);
        Assert.AreEqual(arm, game.Cpu.ArmStrength(Hand.Left), 0.02f * arm, "the arms are untouched");
        Assert.IsFalse(game.Cpu.CanStand, "but the legs are gone");
    }

    [TestMethod]
    public void TheThoracicCord_TakesTheChestAndLegs_NotTheArms()
    {
        using var game = Game();
        Play(game, 1f);
        var arm = game.Cpu.ArmStrength(Hand.Left);
        var chest = game.Cpu.Strength(game.Cpu.Parts.Chest);
        CutCord(game, game.Cpu.Parts.Chest, CordCut.Whole);
        Play(game, 3f);
        Assert.AreEqual(arm, game.Cpu.ArmStrength(Hand.Left), 0.02f * arm);
        Assert.IsLessThan(0.5f * chest, game.Cpu.Strength(game.Cpu.Parts.Chest));
        Assert.IsFalse(game.Cpu.CanStand);
    }

    [TestMethod]
    public void TheCordIsFeltByTheBreath_AndTheNeckMuscles()
    {
        using var game = Game();
        Play(game, 1f);
        Assert.AreEqual(1f, game.Cpu.Breath, 1e-4f);
        CutCord(game, game.Cpu.Parts.Head, CordCut.Whole);
        Play(game, 3f);
        Assert.IsLessThan(0.1f, game.Cpu.Breath, "the muscles that breathe have no signal");
    }

    [TestMethod]
    public void TheCordNeverDropsToZeroAtOnce_ItRelaxesLikeADeadHand()
    {
        using var game = Game();
        Play(game, 1f);
        CutCord(game, game.Cpu.Parts.Head, CordCut.Whole);
        Play(game, 0.1f);
        var early = game.Cpu.CervicalCord;
        Play(game, 3f);
        Assert.IsGreaterThan(0.3f, early, "after 0.1 s the signal has not yet gone");
        Assert.IsLessThan(0.05f, game.Cpu.CervicalCord, "after 3 s it has");
    }

    // ── 構造 ──

    [TestMethod]
    public void EveryCharacter_HasANeckBoneAndACord_AndNothingBreaksAtTheStart()
    {
        foreach (var character in CharacterRoster.All)
        {
            using var game = new BoxingGame(seed: 1, corners: new MatchSelection(MatchMode.Versus, character, character).Corners with { Cpu = _ => new StandGuard(), Player = _ => new StandGuard() });
            Play(game, 0.5f);
            foreach (var fighter in new[] { game.Player, game.Cpu })
            {
                var grid = fighter.Voxels!.PartOf(fighter.Parts.Head)!.Body.Grid;
                Assert.IsNotNull(BoxerBody.NeckBoneSlices(grid), $"{character.Id}: the neck has bone in its slices");
                Assert.IsGreaterThan(0, grid.Initial(BoxerAnatomy.Cord), $"{character.Id}: the neck has a cord");
                Assert.IsFalse(fighter.Voxels!.NeckBroken, $"{character.Id}: the neck is whole at the start");
                Assert.IsFalse(fighter.IsDead);
                Assert.AreEqual(1f, fighter.Voxels.CordFunction(CordLevel.Cervical), 1e-5f, character.Id);
            }
        }
    }

    [TestMethod]
    public void TheSpine_AddsNoCellsAndNoMass_OnlyReplacesInnerMaterials()
    {
        using var game = Game();
        var head = Grid(game, game.Cpu.Parts.Head);
        var chest = Grid(game, game.Cpu.Parts.Chest);
        var pelvis = Grid(game, game.Cpu.Parts.Pelvis);
        Assert.AreEqual(8, head.Initial(BoxerAnatomy.Cord), "a 2 x 2 cord in each of the two neck slices");
        Assert.IsGreaterThan(0, chest.Initial(BoxerAnatomy.Cord));
        Assert.IsGreaterThan(0, pelvis.Initial(BoxerAnatomy.Cord));
        Assert.AreEqual(1f, game.Cpu.Voxels!.CordFunction(CordLevel.Cervical));
        Assert.AreEqual(1f, game.Cpu.Voxels.CordFunction(CordLevel.Thoracic));
        Assert.AreEqual(1f, game.Cpu.Voxels.CordFunction(CordLevel.Lumbar));
    }
}
