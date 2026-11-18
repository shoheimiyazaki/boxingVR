using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// VFC の再生（2026-11-18、指示書 F）: 壊れたボクセルは生きているセルに接する所から確率で戻る（前線の再生）。欠損した先の部位は、切り株が
/// 遠い端まで戻ると芽から生え直す。休みの間と、両者が倒れて意識だけでは立てないときに再生し、休みには失った武器を支給する。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class VfcRegrowthTests
{
    private const float Dt = 1f / 240f;

    /// <summary>休み 1 回（25 秒）ぶんの抽選: VFC の速さ・間隔で 100 回。</summary>
    private static readonly int TicksPerBreak = (int)(25f / MatchRules.Vfc.RegrowthTickSeconds);

    private static float TickAdvance => MatchRules.Vfc.RegrowthSpeed * MatchRules.Vfc.RegrowthTickSeconds;

    private sealed class Scripted(Func<FighterSense, FighterCommand> read) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => read(sense);
    }

    private static FighterCommand Guard(in FighterSense s, bool letGo = false)
    {
        var face = s.OpponentChest - s.Self.Origin;
        face.Y = 0f;
        var reach = s.ArmReach;
        var left = new HandCommand(new Vector3(0.15f * reach, 0.1f * reach, 0.45f * reach), false);
        var right = new HandCommand(new Vector3(-0.15f * reach, 0.1f * reach, 0.45f * reach), false, LetGo: letGo);
        return new FighterCommand(Vector3.Zero, Vector3.Normalize(face), left, right);
    }

    private static BoxingGame StillGame(MatchRules? rules = null) =>
        new(seed: 1, corners: new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard()), rules: rules);

    private static void Play(BoxingGame game, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
        {
            game.Step(default, Dt);
        }
    }

    private static void Blow(BoxingGame game, Fighter f, float height)
    {
        var half = f.HalfExtents.Chest;
        var point = f.ChestPosition + (f.Frame.Forward * half.Z) + (Vector3.UnitY * (height * half.Y));
        const float Mass = 4.5f, Speed = 2.2f;
        var damage = PunchMechanics.Damage(Mass * Speed) * PunchMechanics.Sharpness(Speed);
        game.ApplyHit(new HitEvent(game.Player, Hand.Right, f, HitZone.Body, point, Speed, Mass, 0.7f, damage), f.Parts.Chest, -f.Frame.Forward * Speed);
    }

    /// <summary>部位 <paramref name="part"/> の、骨の軸に沿ったスライス <paramref name="slices"/> を消し、骨が切れたら試合に伝える。</summary>
    private static BoneBreak? Cut(BoxingGame game, Fighter fighter, int part, IEnumerable<int> slices)
    {
        var body = fighter.Voxels!;
        var voxelPart = body.PartOf(part)!;
        var grid = voxelPart.Body.Grid;
        var axis = voxelPart.SeverAxis ?? 0;
        var removed = new List<RemovedVoxel>();
        foreach (var slice in slices)
        {
            for (var i = 0; i < grid.CellCount; i++)
            {
                var c = grid.Coordinates(i);
                if ((axis == 0 ? c.X : axis == 1 ? c.Y : c.Z) == slice && grid.MaterialAt(i) != 0)
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

    /// <summary>部位の格子の、セルのある所の、骨の軸に沿った長さ [スライス]。</summary>
    private static int Length(VoxelPart part)
    {
        var grid = part.Body.Grid;
        var axis = part.SeverAxis ?? 0;
        int low = int.MaxValue, high = -1;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != 0)
            {
                var c = grid.Coordinates(i);
                var s = axis == 0 ? c.X : axis == 1 ? c.Y : c.Z;
                low = Math.Min(low, s);
                high = Math.Max(high, s);
            }
        }

        return high < low ? 0 : high - low + 1;
    }

    private static int Filled(Fighter f) => f.Voxels!.Parts.Where(p => f.Owns(p.Body.BodyId)).Sum(p => p.Body.Grid.FilledCount);

    [TestMethod]
    public void AShallowWound_IsMostlyBack_AfterOneBreak()
    {
        using var game = StillGame();
        Play(game, 1.5f);
        var f = game.Cpu;
        var full = Filled(f);
        for (var i = 0; i < 4; i++)
        {
            Blow(game, f, height: 0.2f);
        }

        var carved = full - Filled(f);
        Assert.IsGreaterThan(20, carved, "the blows carved the chest");
        var random = new Random(7);
        for (var i = 0; i < TicksPerBreak; i++)
        {
            f.Regrow(random, TickAdvance);
        }

        var back = (float)(Filled(f) - (full - carved)) / carved;
        Console.WriteLine($"fist craters: {carved} cells carved, {back:P0} back after one 25 s break");
        Assert.IsGreaterThanOrEqualTo(0.8f, back, "a fist's crater is mostly back after one break");
    }

    [TestMethod]
    public void ACutForearm_GrowsOnPastTheCut_ButIsNotWholeAfterOneBreak()
    {
        using var game = StillGame();
        Play(game, 1f);
        var f = game.Cpu;
        var forearm = f.ForearmId(Hand.Right);
        var part = f.Voxels!.PartOf(forearm)!;
        var full = Length(part);
        var n = part.Body.Grid.Dimensions.X;
        // 肘から 3 割の所で切り落とす（前腕の 7 割を失う）。
        var cutAt = part.DistalHigh ? (int)(n * 0.3f) : n - 1 - (int)(n * 0.3f);
        var cut = Cut(game, f, forearm, [cutAt]);
        Assert.IsNotNull(cut);
        Assert.IsTrue(f.ArmSevered(Hand.Right));
        var stump = Length(part);
        var random = new Random(7);
        for (var i = 0; i < TicksPerBreak; i++)
        {
            f.Regrow(random, TickAdvance);
        }

        var grown = Length(part);
        var cell = part.Body.Grid.CellSize.X;
        Console.WriteLine($"forearm {full} slices ({full * cell * 100f:0.0} cm), stump {stump}, after one break {grown} (+{(grown - stump) * cell * 100f:0.0} cm)");
        Assert.IsGreaterThan(stump + 2, grown, "the stump grows on past the cut");
        Assert.IsLessThan(full, grown, "but a lost forearm does not come back in one break");
        Assert.IsTrue(f.ArmSevered(Hand.Right), "and the arm is not driven yet");
    }

    [TestMethod]
    public void OnePass_NeverRestoresACellThatTouchesNoLivingCell()
    {
        using var game = StillGame();
        Play(game, 1f);
        var f = game.Cpu;
        var forearm = f.ForearmId(Hand.Right);
        var part = f.Voxels!.PartOf(forearm)!;
        var n = part.Body.Grid.Dimensions.X;
        Cut(game, f, forearm, [part.DistalHigh ? n / 2 : n - 1 - (n / 2)]);
        var grid = part.Body.Grid;
        var before = Enumerable.Range(0, grid.CellCount).Select(grid.MaterialAt).ToArray();
        // 1 層ぶん（確率 1）の 1 回の pass: 戻ったセルはどれも、戻す前に生きている 6 近傍があった。
        f.Regrow(new Random(3), grid.CellSize.X * 0.999f);
        var (nx, ny, _) = grid.Dimensions;
        var restored = 0;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (before[i] != 0 || grid.MaterialAt(i) == 0)
            {
                continue;
            }

            restored++;
            var (x, y, z) = grid.Coordinates(i);
            bool Alive(int dx, int dy, int dz)
            {
                var (a, b, c) = (x + dx, y + dy, z + dz);
                return a >= 0 && b >= 0 && c >= 0 && a < nx && b < ny && c < grid.Dimensions.Z && before[grid.Index(a, b, c)] != 0;
            }

            Assert.IsTrue(Alive(1, 0, 0) || Alive(-1, 0, 0) || Alive(0, 1, 0) || Alive(0, -1, 0) || Alive(0, 0, 1) || Alive(0, 0, -1),
                $"cell ({x},{y},{z}) came back with no living neighbour: the front jumped");
        }

        Assert.IsGreaterThan(0, restored);
    }

    [TestMethod]
    public void TheSameSeed_RegrowsTheSameCells()
    {
        byte[] Run()
        {
            using var game = StillGame();
            Play(game, 1f);
            var f = game.Cpu;
            var forearm = f.ForearmId(Hand.Right);
            var part = f.Voxels!.PartOf(forearm)!;
            var n = part.Body.Grid.Dimensions.X;
            Cut(game, f, forearm, [part.DistalHigh ? n / 2 : n - 1 - (n / 2)]);
            var random = new Random(11);
            for (var i = 0; i < 20; i++)
            {
                f.Regrow(random, TickAdvance);
            }

            var grid = part.Body.Grid;
            return [.. Enumerable.Range(0, grid.CellCount).Select(grid.MaterialAt)];
        }

        CollectionAssert.AreEqual(Run(), Run());
    }

    /// <summary>頭の脳のセルを <paramref name="share"/> だけ削る（意識だけでは立てない体にする）。</summary>
    private static void CarveBrain(Fighter f, float share)
    {
        var grid = f.Voxels!.PartOf(f.Parts.Head)!.Body.Grid;
        var brain = Enumerable.Range(0, grid.CellCount).Where(i => grid.MaterialAt(i) == BoxerAnatomy.Brain).ToList();
        var removed = new List<RemovedVoxel>();
        foreach (var i in brain.Take((int)(brain.Count * share)))
        {
            grid.Break(i, removed);
        }
    }

    [TestMethod]
    public void InARound_OnlyBothDownAndUnableToStand_Regrows_FasterTheLongerItLasts_AndStopsOnceOneCanStand()
    {
        var rules = MatchRules.Vfc with { RoundSeconds = 60f };
        using var game = StillGame(rules);
        Play(game, 1f);
        foreach (var f in new[] { game.Player, game.Cpu })
        {
            CarveBrain(f, 0.4f);
            Assert.IsFalse(f.IsDead);
            Assert.IsFalse(f.BodyLetsStand, "a third of the brain gone: consciousness alone will not stand him up");
        }

        // 片方だけ倒れている: 再生しない（相手が攻める場面を消さない）。
        game.Cpu.Knockdown(-game.Cpu.Frame.Forward, permanent: false);
        var brainBefore = game.Cpu.Voxels!.BrainFunction(0.1f);
        for (var t = 0f; t < 2f; t += Dt)
        {
            game.Step(default, Dt);
            if (game.Player.State != FighterState.Standing)
            {
                break;
            }

            Assert.IsFalse(game.RegrowingWhileStuck, "one fighter down: no regrowth");
        }

        // 両者が倒れて、どちらも意識だけでは立てない: 再生し、続くほど速くなり、立てる体に戻ったら止まる。
        game.Player.Knockdown(-game.Player.Frame.Forward, permanent: false);
        var rates = new List<float>();
        var stoppedAt = -1f;
        for (var t = 0f; t < 30f; t += Dt)
        {
            game.Step(default, Dt);
            if (game.RegrowingWhileStuck)
            {
                rates.Add(game.RegrowthRate);
            }
            else if (rates.Count > 0)
            {
                stoppedAt = t;
                break;
            }
        }

        Console.WriteLine($"regrew for {rates.Count * Dt:0.0} s, rate {rates.FirstOrDefault():0.00} → {rates.LastOrDefault():0.0}, stopped at {stoppedAt:0.0} s, " +
            $"brain {brainBefore:0.000} → {game.Cpu.Voxels.BrainFunction(0.1f):0.000}, lets stand: red {game.Player.BodyLetsStand} blue {game.Cpu.BodyLetsStand}");
        Assert.IsNotEmpty(rates, "both down and unable to stand: the bodies regrow");
        Assert.IsGreaterThan(rates[0] * 2f, rates[^1], "the longer it lasts, the faster");
        Assert.IsGreaterThan(brainBefore, game.Cpu.Voxels.BrainFunction(0.1f), "the brain grew back");
        Assert.IsGreaterThan(0f, stoppedAt, "it stopped");
        Assert.IsTrue(game.Player.BodyLetsStand || game.Cpu.BodyLetsStand || game.Player.State == FighterState.Standing || game.Cpu.State == FighterState.Standing,
            "it stopped because one of them can stand again");
    }

    [TestMethod]
    public void AStumpGrownBackToTheElbow_SproutsANewForearm_AndTheOldOneStaysOnTheFloor()
    {
        using var game = StillGame();
        Play(game, 1f);
        var f = game.Cpu;
        var upper = f.UpperArmId(Hand.Right);
        var forearm = f.ForearmId(Hand.Right);
        var n = f.Voxels!.PartOf(upper)!.Body.Grid.Dimensions.X;
        var cut = Cut(game, f, upper, [n / 2]);
        Assert.IsNotNull(cut);
        Assert.IsFalse(f.Owns(forearm), "the forearm fell with the cut");
        var oldForearmCells = f.Voxels.PartOf(forearm)!.Body.Grid.FilledCount;
        Play(game, 1f);

        // 速く回す（1 回の抽選で 2 層）: 上腕が肘まで戻ると、前腕が芽から生え直す。
        var random = new Random(5);
        var sprouted = false;
        for (var i = 0; i < 400 && !sprouted; i++)
        {
            var report = f.Regrow(random, 0.01f);
            sprouted = report.Rebudded.Contains(forearm);
            game.Step(default, Dt);
        }

        Assert.IsTrue(sprouted, "the forearm sprouts again once the upper arm is back to the elbow");
        Assert.IsTrue(f.Owns(forearm), "the new forearm is part of the body");
        Assert.IsLessThan(oldForearmCells / 4, f.Voxels.PartOf(forearm)!.Body.Grid.FilledCount, "it starts as a bud");
        var floor = f.Voxels.Fragments.Where(p => p.FragmentOf == forearm).ToList();
        Assert.IsNotEmpty(floor, "the old forearm stays on the floor as a fragment");
        Assert.IsGreaterThan(oldForearmCells / 2, floor.Sum(p => p.Body.Grid.FilledCount), "with its voxels");

        // 前腕が育ちきると、腕はまた駆動する。
        for (var i = 0; i < 800 && f.ArmSevered(Hand.Right); i++)
        {
            f.Regrow(random, 0.01f);
            game.Step(default, Dt);
        }

        Assert.IsFalse(f.ArmSevered(Hand.Right), "a whole arm again");
        Play(game, 1f);
        var elbow = Vector3.Distance(game.World.GetState(forearm).Position, game.World.GetState(upper).Position);
        var rest = f.HalfExtents.UpperArm.X + f.HalfExtents.Forearm.X;
        Console.WriteLine($"upper arm to forearm centre {elbow * 100f:0.0} cm (rest {rest * 100f:0.0} cm)");
        Assert.IsLessThan(rest * 1.15f, elbow, "held at the elbow by the rebuilt joint");
    }

    private static BoxingGame ClubMatch(Func<bool> drop, float breakSeconds = 2f) =>
        new(seed: 1, corners: new BoxingCorners("club", _ => new Scripted(s => Guard(s, drop())), _ => new StandGuard(), CpuWeapon: WeaponKind.WoodenClub),
            rules: MatchRules.Vfc with { RoundSeconds = 2f, BreakSeconds = breakSeconds });

    [TestMethod]
    public void ADroppedWeapon_IsReplacedInTheBreak_NotDuringTheRound()
    {
        var drop = false;
        using var game = ClubMatch(() => drop);
        Play(game, 0.5f);
        var old = game.WeaponOf(game.Cpu)!;
        drop = true;
        Play(game, 0.1f);
        drop = false;
        Assert.IsFalse(game.Cpu.Wields(BoxingGame.WeaponHand));
        while (!game.InBreak)
        {
            game.Step(default, Dt);
            Assert.IsTrue(game.InBreak || !game.Cpu.Wields(BoxingGame.WeaponHand), "no new weapon during the round");
        }

        var supplied = false;
        while (game.InBreak)
        {
            game.Step(default, Dt);
            supplied |= game.RecentSupplies.Any(s => s.Fighter == game.Cpu && s.Kind == WeaponKind.WoodenClub);
        }

        Assert.IsTrue(supplied, "the break hands him a new club");
        var now = game.WeaponOf(game.Cpu);
        Assert.IsNotNull(now);
        Assert.AreEqual(WeaponKind.WoodenClub, now.Kind);
        Assert.AreNotEqual(old.Body.BodyId, now.Body.BodyId, "a new one; the old club stays on the floor");
        Assert.AreEqual(now.Body.BodyId, game.Cpu.WieldedBody(BoxingGame.WeaponHand));
        Assert.Contains(old, game.Weapons.ToList(), "the old club is still in the cage");
    }

    [TestMethod]
    public void ARegrownArm_GetsANewWeapon_AndTheOldFistStaysOnTheOldHand()
    {
        var drop = false;
        using var game = ClubMatch(() => drop, breakSeconds: 3f);
        Play(game, 0.3f);
        drop = true;
        Play(game, 0.1f);
        drop = false;
        Play(game, 0.6f);
        var oldFist = game.GloveOf(game.Cpu, BoxingGame.WeaponHand);
        Assert.IsNotNull(oldFist, "the empty hand is a bare fist");
        var upper = game.Cpu.UpperArmId(BoxingGame.WeaponHand);
        var n = game.Cpu.Voxels!.PartOf(upper)!.Body.Grid.Dimensions.X;
        Assert.IsNotNull(Cut(game, game.Cpu, upper, [n / 2]));

        // 腕が生え直すまで速く回す（試合の外から）。
        var random = new Random(9);
        for (var i = 0; i < 1500 && game.Cpu.ArmSevered(BoxingGame.WeaponHand); i++)
        {
            game.Cpu.Regrow(random, 0.01f);
            game.Step(default, Dt);
        }

        Assert.IsFalse(game.Cpu.ArmSevered(BoxingGame.WeaponHand), "the arm grew back");
        var supplied = false;
        while (!game.MatchOver && !supplied)
        {
            game.Step(default, Dt);
            supplied = game.RecentSupplies.Any(s => s.Fighter == game.Cpu);
        }

        Assert.IsTrue(supplied, "the regrown hand is handed a new club in the break");
        Assert.AreEqual(WeaponKind.WoodenClub, game.WeaponOf(game.Cpu)!.Kind);
        Assert.IsGreaterThan(-1f, game.World.GetState(oldFist.Body.BodyId).Position.Y, "the old fist stays with the old hand, not packed away");
    }

    [TestMethod]
    public void AHandThatIsGone_GetsNoWeapon()
    {
        using var game = ClubMatch(() => false);
        Play(game, 0.5f);
        var upper = game.Cpu.UpperArmId(BoxingGame.WeaponHand);
        var n = game.Cpu.Voxels!.PartOf(upper)!.Body.Grid.Dimensions.X;
        Assert.IsNotNull(Cut(game, game.Cpu, upper, [n / 2]));
        while (!game.MatchOver && game.MatchRound < 2)
        {
            game.Step(default, Dt);
            Assert.IsEmpty(game.RecentSupplies, "a cut-off arm cannot hold a weapon");
        }

        Assert.IsTrue(game.Cpu.ArmSevered(BoxingGame.WeaponHand));
    }
}
