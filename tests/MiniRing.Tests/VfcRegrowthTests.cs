using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics.Voxels;
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

    /// <summary>休み 1 回（42 秒。人形の単位で 25 秒）ぶんの抽選: VFC の速さ・間隔で 100 回。</summary>
    private static readonly int TicksPerBreak = (int)(41.96f / MatchRules.Vfc.RegrowthTickSeconds);

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
        const float Mass = 45f, Speed = 3.692f;
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
        Play(game, 2.518f);
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
        Console.WriteLine($"fist craters: {carved} cells carved, {back:P0} back after one 42 s break");
        Assert.IsGreaterThanOrEqualTo(0.8f, back, "a fist's crater is mostly back after one break");
    }

    /// <summary>
    /// 呼吸と循環のモデル（2026-11-18）の回帰: 切られた腕も、体に近い側の肩・肘の筋肉は酸素の蓄えを持ち続け、切り株のサーボが動く予算がある
    /// （残った筋肉を 0 と数えると蓄えも予算も 0 になり、切り株がぶら下がるだけになった。レビューの指摘）。
    /// </summary>
    [TestMethod]
    public void ACutArm_KeepsOxygenForTheStump()
    {
        using var game = StillGame();
        Play(game, 1.678f);
        var f = game.Cpu;
        var forearm = f.ForearmId(Hand.Right);
        var part = f.Voxels!.PartOf(forearm)!;
        var n = part.Body.Grid.Dimensions.X;
        var cutAt = part.DistalHigh ? (int)(n * 0.3f) : n - 1 - (int)(n * 0.3f);
        Assert.IsNotNull(Cut(game, f, forearm, [cutAt]));
        Assert.IsTrue(f.ArmSevered(Hand.Right));
        Play(game, 0.8392f);
        var (capacity, _) = f.OxygenBudgetOf(MuscleRegion.RightArm);
        Assert.IsGreaterThan(28.17f, capacity, "切り株の筋肉にも酸素の蓄えがある");
        Assert.IsGreaterThan(0.5f, f.OxygenOf(MuscleRegion.RightArm), "構えているだけの切り株は尽きない");
        Assert.IsTrue(float.IsFinite(f.HandSlowness) && f.HandSlowness < 1f, "切られた腕は拳の遅さに数えない");
    }

    [TestMethod]
    public void ACutForearm_GrowsOnPastTheCut_ButIsNotWholeAfterOneBreak()
    {
        using var game = StillGame();
        Play(game, 1.678f);
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
        Play(game, 1.678f);
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
            Play(game, 1.678f);
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
        var rules = MatchRules.Vfc with { RoundSeconds = 100.7f };
        using var game = StillGame(rules);
        Play(game, 1.678f);
        foreach (var f in new[] { game.Player, game.Cpu })
        {
            CarveBrain(f, 0.4f);
            Assert.IsFalse(f.IsDead);
            Assert.IsFalse(f.BodyLetsStand, "a third of the brain gone: consciousness alone will not stand him up");
        }

        // 片方だけ倒れている: 再生しない（相手が攻める場面を消さない）。
        game.Cpu.Knockdown(-game.Cpu.Frame.Forward, permanent: false);
        var brainBefore = game.Cpu.Voxels!.BrainFunction(0.1f);
        for (var t = 0f; t < 3.357f; t += Dt)
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
        for (var t = 0f; t < 50.35f; t += Dt)
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
        // 倍率は続いた時間で 2^(t / StuckDoublingSeconds)。2026-11-24 に再生を 1.5 倍にしてから、脳はほぼ倍の時間（5 s）で戻るので、2 倍ちょうどには届かないことがある。
        var lasted = (rates.Count - 1) * Dt; // 最初の標本は続いた時間 0
        Assert.IsGreaterThan(rates[0] * 1.5f, rates[^1], "the longer it lasts, the faster");
        Assert.AreEqual(rates[0] * MathF.Pow(2f, lasted / rules.StuckDoublingSeconds), rates[^1], 0.02f * rates[^1], "the rate doubles every StuckDoublingSeconds");
        Assert.IsGreaterThan(brainBefore, game.Cpu.Voxels.BrainFunction(0.1f), "the brain grew back");
        Assert.IsGreaterThan(0f, stoppedAt, "it stopped");
        Assert.IsTrue(game.Player.BodyLetsStand || game.Cpu.BodyLetsStand || game.Player.State == FighterState.Standing || game.Cpu.State == FighterState.Standing,
            "it stopped because one of them can stand again");
    }

    [TestMethod]
    public void AStumpGrownBackToTheElbow_SproutsANewForearm_AndTheOldOneStaysOnTheFloor()
    {
        using var game = StillGame();
        Play(game, 1.678f);
        var f = game.Cpu;
        var upper = f.UpperArmId(Hand.Right);
        var forearm = f.ForearmId(Hand.Right);
        var n = f.Voxels!.PartOf(upper)!.Body.Grid.Dimensions.X;
        var cut = Cut(game, f, upper, [n / 2]);
        Assert.IsNotNull(cut);
        Assert.IsFalse(f.Owns(forearm), "the forearm fell with the cut");
        var oldForearmCells = f.Voxels.PartOf(forearm)!.Body.Grid.FilledCount;
        Play(game, 1.678f);

        // 速く回す（1 回の抽選で 2 層: 2 × 14 mm）: 上腕が肘まで戻ると、前腕が芽から生え直す。
        var random = new Random(5);
        var sprouted = false;
        for (var i = 0; i < 400 && !sprouted; i++)
        {
            var report = f.Regrow(random, 0.02817f);
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
            f.Regrow(random, 0.02817f);
            game.Step(default, Dt);
        }

        Assert.IsFalse(f.ArmSevered(Hand.Right), "a whole arm again");
        Play(game, 1.678f);
        var elbow = Vector3.Distance(game.World.GetState(forearm).Position, game.World.GetState(upper).Position);
        var rest = f.HalfExtents.UpperArm.X + f.HalfExtents.Forearm.X;
        Console.WriteLine($"upper arm to forearm centre {elbow * 100f:0.0} cm (rest {rest * 100f:0.0} cm)");
        Assert.IsLessThan(rest * 1.15f, elbow, "held at the elbow by the rebuilt joint");
    }

    /// <summary>前腕の骨を曲げで真ん中で折り、肉の橋を 1/8 だけ残して削る（ぶら下がる）。破片の剛体を返す。</summary>
    private static int BendBreakForearm(BoxingGame game, Fighter f)
    {
        var forearm = f.ForearmId(Hand.Right);
        var grid = f.Voxels!.PartOf(forearm)!.Body.Grid;
        var broken = f.Voxels.BreakBent(forearm, new VoxelBreak(0, grid.Dimensions.X / 2, f.Voxels.PartOf(forearm)!.DistalHigh, 2f))!;
        game.BreakBone(f, broken, 1f);
        var bridge = broken.Bridge!;
        var removed = new List<RemovedVoxel>();
        foreach (var cell in bridge.Cells.Skip(Math.Max(1, bridge.Cells.Count / 8)))
        {
            bridge.Stub.Grid.Break(cell.StubIndex, removed);
        }

        bridge.Stub.UpdateMass();
        return broken.Fragment!.Body.BodyId;
    }

    /// <summary>剛体 <paramref name="a"/> と <paramref name="b"/> の、骨の軸（ローカル X）のなす角 [度]。</summary>
    private static float AxisAngle(BoxingGame game, int a, int b)
    {
        var x = Vector3.Transform(Vector3.UnitX, game.World.GetState(a).Orientation);
        var y = Vector3.Transform(Vector3.UnitX, game.World.GetState(b).Orientation);
        return MathF.Acos(Math.Clamp(Vector3.Dot(x, y), -1f, 1f)) * 180f / MathF.PI;
    }

    [TestMethod]
    public void ABentForearm_HealsBentButJoined_FleshFirstThenTheSpringStiffensThenTheBoneKnitsAndHolds()
    {
        using var game = StillGame();
        Play(game, 1.678f);
        var f = game.Cpu;
        var forearm = f.ForearmId(Hand.Right);
        var fragment = BendBreakForearm(game, f);
        Assert.IsTrue(f.HangsByFlesh(fragment));
        Assert.IsTrue(f.ArmSevered(Hand.Right));
        Play(game, 3.357f);
        var hanging = f.FractureState(fragment)!.Value;
        var sag = AxisAngle(game, forearm, fragment);
        var random = new Random(4);
        var log = new List<string>();
        (float Fraction, float SpringHz)? stiff = null;
        var stiffAngle = float.NaN;
        var knitAt = -1;
        for (var tick = 0; tick < 2000 && knitAt < 0; tick++)
        {
            var report = f.Regrow(random, TickAdvance);
            Assert.IsFalse(report.Rebudded.Contains(forearm), "a bone hanging by its flesh is not replaced by a new bud");
            for (var i = 0; i < 101; i++)
            {
                game.Step(default, Dt);
            }

            if (f.IsKnit(fragment))
            {
                knitAt = tick;
            }
            else if (f.FractureState(fragment) is { } state)
            {
                if (tick % 20 == 0)
                {
                    log.Add($"{tick * 0.4196f:0}s flesh {state.Fraction:P0} {state.SpringHz:0.0} Hz angle {AxisAngle(game, forearm, fragment):0}°");
                }

                if (state.Fraction >= 1f && stiff is null)
                {
                    stiff = state;
                    stiffAngle = AxisAngle(game, forearm, fragment);
                }
            }
        }

        Console.WriteLine($"hanging: flesh {hanging.Fraction:P0} {hanging.SpringHz:0.0} Hz, angle {sag:0}°; {string.Join("; ", log)}; knit at {knitAt * 0.4196f:0} s");
        Assert.IsGreaterThan(hanging.SpringHz * 2f, stiff!.Value.SpringHz, "the flesh grew back and the spring is stiffer");
        Assert.IsLessThan(sag, stiffAngle, "the stiffer flesh lifts the piece a little straighter");
        Assert.IsGreaterThanOrEqualTo(0, knitAt, "at last the bone knits");
        Assert.IsFalse(f.HangsByFlesh(fragment), "no longer hanging by the flesh");
        Assert.IsFalse(f.ArmSevered(Hand.Right), "the arm is driven again");
        Assert.IsTrue(f.Voxels!.Fragments.Any(p => p.Body.BodyId == fragment), "the same piece, not a new bud");
        Play(game, 0.8392f);
        var knitAngle = AxisAngle(game, forearm, fragment);
        Play(game, 3.357f);
        var later = AxisAngle(game, forearm, fragment);
        Console.WriteLine($"knit angle {knitAngle:0.0}°, 3.4 s later {later:0.0}°");
        Assert.IsGreaterThan(1f, knitAngle, "it healed bent, not straight");
        Assert.IsLessThan(3f, MathF.Abs(later - knitAngle), "the bend is fixed once the bone has knit");
        Assert.IsGreaterThan(-2.817f, game.World.GetState(fragment).Position.Y - game.World.GetState(forearm).Position.Y + 0.5634f, "the piece did not fall to the floor");
    }

    private static BoxingGame ClubMatch(Func<bool> drop, float breakSeconds = 3.357f) =>
        new(seed: 1, corners: new BoxingCorners("club", _ => new Scripted(s => Guard(s, drop())), _ => new StandGuard(), CpuWeapon: WeaponKind.WoodenClub),
            rules: MatchRules.Vfc with { RoundSeconds = 3.357f, BreakSeconds = breakSeconds });

    [TestMethod]
    public void ADroppedWeapon_IsReplacedInTheBreak_NotDuringTheRound()
    {
        var drop = false;
        using var game = ClubMatch(() => drop);
        Play(game, 0.8392f);
        var old = game.WeaponOf(game.Cpu)!;
        drop = true;
        Play(game, 0.1678f);
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
        using var game = ClubMatch(() => drop, breakSeconds: 5.035f);
        Play(game, 0.5035f);
        drop = true;
        Play(game, 0.1678f);
        drop = false;
        Play(game, 1.007f);
        var oldFist = game.GloveOf(game.Cpu, BoxingGame.WeaponHand);
        Assert.IsNotNull(oldFist, "the empty hand is a bare fist");
        var upper = game.Cpu.UpperArmId(BoxingGame.WeaponHand);
        var n = game.Cpu.Voxels!.PartOf(upper)!.Body.Grid.Dimensions.X;
        Assert.IsNotNull(Cut(game, game.Cpu, upper, [n / 2]));

        // 腕が生え直すまで速く回す（試合の外から）。
        var random = new Random(9);
        for (var i = 0; i < 1500 && game.Cpu.ArmSevered(BoxingGame.WeaponHand); i++)
        {
            game.Cpu.Regrow(random, 0.02817f);
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
        Assert.IsGreaterThan(-2.817f, game.World.GetState(oldFist.Body.BodyId).Position.Y, "the old fist stays with the old hand, not packed away");
    }

    [TestMethod]
    public void AHandThatIsGone_GetsNoWeapon()
    {
        using var game = ClubMatch(() => false);
        Play(game, 0.8392f);
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
