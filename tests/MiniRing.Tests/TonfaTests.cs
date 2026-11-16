using System.Numerics;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Motion;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>2026-11-16: トンファー（<see cref="HandWear.Tonfa"/>）。形・質量・両手・当たり・受け・教科書・キャラ。docs/TONFA.md。</summary>
[TestClass]
public sealed class TonfaTests
{
    public TestContext TestContext { get; set; } = null!;

    private static string OutputDir()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MiniRing.slnx")))
        {
            root = root.Parent;
        }

        Assert.IsNotNull(root);
        var output = Path.Combine(root.FullName, ".visualcheck");
        Directory.CreateDirectory(output);
        return output;
    }

    [TestMethod]
    [TestCategory("visual")]
    public void Tonfa_OnTheForearm_RendersFromOutsideAndAbove()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard(), PlayerWear: HandWear.Tonfa, CpuWear: HandWear.Glove));
        for (var i = 0; i < 120; i++)
        {
            game.Step(default, 1f / 60f);
        }

        using var renderer = new HeadlessRenderer(800, 600);
        var output = OutputDir();
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var forearm = game.Player.ForearmId(hand);
            var upper = game.Player.UpperArmId(hand);
            var target = game.World.GetState(forearm).Position;
            var outward = target - game.Player.ChestPosition;
            outward.Y = 0f;
            outward = Vector3.Normalize(outward);
            foreach (var (name, eyeOffset) in new[]
            {
                ("side", (outward * 0.22f) + new Vector3(0f, 0.03f, 0f)),
                ("above", new Vector3(0f, 0.24f, 0f) + (outward * 0.02f)),
                ("front", (Vector3.Normalize(game.Cpu.ChestPosition - game.Player.ChestPosition) * 0.25f) + new Vector3(0f, 0.04f, 0f)),
            })
            {
                var eye = target + eyeOffset;
                var vp = Camera.ViewProjection(eye, target, name == "above" ? Vector3.UnitZ : Vector3.UnitY, 55f * MathF.PI / 180f, 800f / 600f, 0.02f, 30f);
                var voxels = game.Player.Voxels!.Parts.Where(p => p.Body.BodyId == forearm || p.Body.BodyId == upper).Select(p => p.Draw()).ToList();
                voxels.Add(game.GloveOf(game.Player, hand)!.Draw());
                var rgba = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [],
                    lighting: game.BuildLighting(), cameraPosition: eye, voxels: voxels);
                PngWriter.WriteRgba(Path.Combine(output, $"tonfa_{hand}_{name}.png"), 800, 600, rgba);
            }
        }
    }

    // ── 形 ──

    private static IEnumerable<Vector3> Cells(Aigis.Foundation.Voxels.VoxelGrid grid, byte material)
    {
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) == material)
            {
                var (x, y, z) = grid.Coordinates(i);
                yield return (new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * grid.CellSize) - grid.HalfExtents;
            }
        }
    }

    /// <summary>格子のセルの中心を、拳の座標（原点 = 拳の中心）で。</summary>
    private static List<Vector3> InFistFrame(WeaponKind kind, byte material) =>
        [.. Cells(Weapon.Build(kind), material).Select(p => p - Weapon.GripOf(kind))];

    [TestMethod]
    public void TheTonfa_IsAWoodenStickAlongTheForearm_AndAHandleThroughTheFist()
    {
        var wood = InFistFrame(WeaponKind.Tonfa, BoxerAnatomy.Wood);
        var grid = Weapon.Build(WeaponKind.Tonfa);
        // 全体は木と、握る拳（素手の骨・皮膚）だけ。鉄は無い。
        Assert.AreEqual(0, grid.Remaining(BoxerAnatomy.Iron));
        Assert.IsGreaterThan(0, grid.Remaining(BoxerAnatomy.Bone), "the fist holds the handle");
        Assert.IsGreaterThan(0, grid.Remaining(BoxerAnatomy.Skin));
        Assert.AreEqual(grid.FilledCount, grid.Remaining(BoxerAnatomy.Wood) + grid.Remaining(BoxerAnatomy.Bone) + grid.Remaining(BoxerAnatomy.Skin));

        // 棒: 全長 14 cm 前後（人の 47 cm の 0.3 倍）、後ろは拳から 10 cm 以上（肘を少し越える）、前は拳の前へ少し出る。
        var back = wood.Min(p => p.X);
        var front = wood.Max(p => p.X);
        Assert.AreEqual(0.14f, front - back, 0.01f, "stick length: about 47 cm at 0.3 scale");
        Assert.IsLessThan(-0.10f, back, "the long end passes the wrist toward the elbow");
        Assert.IsInRange(0.015f, 0.035f, front, "the short end sticks out a little in front of the fist centre");
        // 棒は前腕に平行（X に沿う）で、拳の外側にある: 後ろの半分のセルは拳の幅（半径 1.3 cm）の外。
        var rear = wood.Where(p => p.X < -0.04f).ToList();
        Assert.IsTrue(rear.All(p => p.Z > 0.013f), "the rear of the stick lies beside the arm, outside the fist's width");
        Assert.AreEqual(0.0185f, rear.Average(p => p.Z), 0.001f, "and parallel to the forearm");
        Assert.IsLessThan(0.0045f, rear.Max(p => MathF.Abs(p.Y)) - 0.0004f, "a round 9 mm rod (3 cm at 0.3 scale)");
        // 握りの柄: 拳の中心（原点）を通って Z に沿い、棒へつながる。
        Assert.AreEqual(BoxerAnatomy.Wood, grid.MaterialAt(grid.FilledCellAt(Weapon.GripOf(WeaponKind.Tonfa))), "the handle passes through the fist centre");
        Assert.IsTrue(wood.Any(p => MathF.Abs(p.X) < 0.002f && p.Z is > 0.003f and < 0.012f), "the handle runs across the fist toward the stick");
    }

    [TestMethod]
    public void TheTonfa_IsAWornBluntThing_WithAWoodenFrontFace_AndItsMassIsTheWoodTimesTheVolume()
    {
        Assert.IsTrue(Weapon.IsWorn(WeaponKind.Tonfa));
        Assert.IsTrue(Weapon.IsBlunt(WeaponKind.Tonfa));
        Assert.IsFalse(Weapon.CanTwoHand(WeaponKind.Tonfa));
        Assert.IsTrue(Weapon.HasRigidFace(WeaponKind.Tonfa), "the arm's push reaches the wood at the point of contact");
        Assert.AreEqual(WeaponKind.Tonfa, HandWear.Tonfa.Kind());
        Assert.AreEqual(HandWear.Tonfa, HandWears.Parse("Tonfa"));
        Assert.IsTrue(Voxels.WeaponVoxelModel.Editable.Contains(WeaponKind.Tonfa), "it shows up in weaponexport");
        Assert.IsNotNull(Voxels.WeaponVoxelModel.ToVox(new Dictionary<WeaponKind, Aigis.Foundation.Voxels.VoxelGrid> { [WeaponKind.Tonfa] = Weapon.Build(WeaponKind.Tonfa) }));

        using var game = new BoxingGame(seed: 1, corners: BoxingCorners.Default with { PlayerWear = HandWear.Tonfa });
        var worn = game.GloveOf(game.Player, Hand.Right)!;
        var grid = worn.Body.Grid;
        var expected = 0f;
        for (var m = 1; m < grid.Palette.Count; m++)
        {
            expected += grid.Remaining((byte)m) * grid.CellVolume * grid.Palette[(byte)m].MechanicsOrDefault.Density;
        }

        var mass = game.World.GetMass(worn.Body.BodyId);
        Assert.AreEqual(expected, mass, expected * 0.01f);
        var bare = Weapon.Build(WeaponKind.Fist);
        Assert.IsGreaterThan(grid.Remaining(BoxerAnatomy.Wood) * grid.CellVolume * 100f, mass, "heavier than the wood alone: the fist is inside too");
        TestContext.WriteLine($"tonfa {mass * 1000f:F1} g, wood cells {grid.Remaining(BoxerAnatomy.Wood)}, grid {grid.Dimensions}; bare-fist cells {bare.FilledCount}");
    }

    [TestMethod]
    public void TheTonfa_IsWornOnBothHands_AndTheStickLiesOnTheSameSideOfEachForearm()
    {
        using var game = new BoxingGame(seed: 1, corners: BoxingCorners.Default with { PlayerWear = HandWear.Tonfa, CpuWear = HandWear.Tonfa });
        foreach (var fighter in new[] { game.Player, game.Cpu })
        {
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                Assert.IsTrue(fighter.WearsGlove(hand));
                Assert.IsFalse(fighter.Wields(hand), "worn, not wielded");
                Assert.AreEqual(WeaponKind.Tonfa, game.GloveOf(fighter, hand)!.Kind);
            }
        }

        // 両手とも、棒が前腕の同じ側（体の中心から見て外か内か）に付く: 左右の手は鏡。
        for (var i = 0; i < 60; i++)
        {
            game.Step(default, 1f / 60f);
        }

        // 手ごとに、棒の中ほどが（その手から見て）もう一方の手の側にあるか。
        float Inner(Hand hand)
        {
            var worn = game.GloveOf(game.Player, hand)!;
            var s = game.World.GetState(worn.Body.BodyId);
            var stick = s.Position + Vector3.Transform(new Vector3(-0.06f, 0f, Weapon.MirroredOn(WeaponKind.Tonfa, hand) ? -0.0185f : 0.0185f) + Weapon.GripOf(WeaponKind.Tonfa, Weapon.MirroredOn(WeaponKind.Tonfa, hand)), s.Orientation);
            var fist = game.Player.GloveCenter(hand);
            var other = game.Player.GloveCenter(hand == Hand.Left ? Hand.Right : Hand.Left);
            return Vector3.Dot(stick - fist, Vector3.Normalize(other - fist));
        }

        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var w = game.GloveOf(game.Player, hand)!; var st = game.World.GetState(w.Body.BodyId);
            var z = Vector3.Transform(Vector3.UnitZ, st.Orientation);
            var fwd = Vector3.Normalize(game.Cpu.ChestPosition - game.Player.ChestPosition); fwd.Y = 0f; fwd = Vector3.Normalize(fwd);
            var right = Vector3.Cross(Vector3.UnitY, fwd);
            TestContext.WriteLine($"{hand} Zw: fwd {Vector3.Dot(z, fwd):F2} right {Vector3.Dot(z, right):F2} up {z.Y:F2}; fist offset right {Vector3.Dot(game.Player.GloveCenter(hand) - game.Player.ChestPosition, right):F2} inner {Inner(hand):F4}");
        }

        Assert.AreEqual(MathF.Sign(Inner(Hand.Left)), MathF.Sign(Inner(Hand.Right)), "mirrored: both sticks are on the same side (outer or inner) of their forearms");
    }

    // ── 打つ・受ける ──

    private static Vector3 LocalInFist(BoxingGame game, Fighter fighter, HitEvent hit)
    {
        var worn = game.GloveOf(fighter, hit.Hand)!;
        var s = game.World.GetState(worn.Body.BodyId);
        var mirrored = Weapon.MirroredOn(WeaponKind.Tonfa, hit.Hand);
        return Vector3.Transform(hit.Point - s.Position, Quaternion.Conjugate(s.Orientation)) - Weapon.GripOf(WeaponKind.Tonfa, mirrored);
    }

    [TestMethod]
    public void TonfaProbe_WhereDoTheBotsPunchesLand()
    {
        foreach (var seed in new[] { 1, 2, 3 })
        {
            using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("tonfa", _ => new StandGuard(), s => new BoxerBot(BotCatalog.Tonfa, s), PlayerWear: HandWear.Tonfa, CpuWear: HandWear.Glove));
            var n = 0;
            for (var i = 0; i < 20 * 60 && !game.RoundOver; i++)
            {
                game.Step(default, 1f / 60f);
                foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Player))
                {
                    var p = LocalInFist(game, game.Player, hit);
                    TestContext.WriteLine($"seed {seed} t={i / 60f:F2} {hit.Hand} face={hit.Face} speed={hit.Speed:F2} local x={p.X * 100f:F1} y={p.Y * 100f:F1} z={p.Z * 100f:F1} cm zone={hit.Zone}");
                    n++;
                }
            }

            TestContext.WriteLine($"seed {seed}: {n} hits");
        }
    }

    private sealed class Scripted(Func<FighterSense, float, FighterCommand> read) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => read(sense, dt);
    }

    /// <summary>
    /// 台本の指令で、右手の拳を相手の胸へまっすぐ突き出す（拳の位置の目標だけ。技の経路も当たり判定の指定も無い）。近づくまでは前へ歩く。
    /// 返すのは、当たった打撃ごとの「拳の座標での当たった点」。
    /// </summary>
    private static List<(HitEvent Hit, Vector3 Local)> ScriptedJabs(int seed, float seconds, HandWear wear = HandWear.Tonfa)
    {
        var hits = new List<(HitEvent, Vector3)>();
        var clock = 0f;
        var script = new Scripted((s, dt) =>
        {
            clock += dt;
            var toChest = s.OpponentChest - s.Self.Origin;
            toChest.Y = 0f;
            var distance = toChest.Length();
            var face = Vector3.Normalize(toChest);
            var guard = new Vector3(0.15f * s.ArmReach, 0.1f * s.ArmReach, 0.45f * s.ArmReach);
            var near = distance < 1.3f * s.ArmReach;
            var punching = near && (clock % 0.9f) < 0.3f; // 0.9 秒ごとに 0.3 秒、突き出す
            var reach = s.Self.WorldToLocal(s.OpponentChest);
            reach = reach with { X = -0.05f * s.ArmReach, Y = reach.Y - 0.05f };
            var jab = punching ? Vector3.Normalize(reach) * MathF.Min(reach.Length(), 0.95f * s.ArmReach) : guard;
            return new FighterCommand(near ? Vector3.Zero : face * 0.5f, face,
                new HandCommand(new Vector3(0.15f * s.ArmReach, 0.1f * s.ArmReach, 0.45f * s.ArmReach), false),
                new HandCommand(jab, false));
        });
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("jab", _ => new StandGuard(), _ => script, PlayerWear: wear, CpuWear: HandWear.Glove));
        for (var i = 0; i < seconds * 60f && !game.RoundOver; i++)
        {
            game.Step(default, 1f / 60f);
            foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Player))
            {
                hits.Add((hit, wear == HandWear.Tonfa ? LocalInFist(game, game.Player, hit) : default));
            }
        }

        return hits;
    }

    [TestMethod]
    public void ScriptedStraightPunch_LandsWithTheFrontOfTheStick_NotTheBackOfTheFist()
    {
        var all = ScriptedJabs(1, 12f);
        foreach (var (hit, p) in all)
        {
            TestContext.WriteLine($"t hit face={hit.Face} speed={hit.Speed:F2} local x={p.X * 100f:F1} y={p.Y * 100f:F1} z={p.Z * 100f:F1} cm zone={hit.Zone}");
        }

        Assert.IsGreaterThan(8, all.Count, "the straight punches land");
        Assert.IsTrue(all.All(h => h.Hit.Face == BoxerAnatomy.Wood), "what touches the opponent is wood, never the knuckles");
        Assert.IsTrue(all.All(h => h.Local.X > 0.013f), "and it is the wood in front of the fist (past its 1.3 cm radius): the short end of the stick");
        Assert.IsTrue(all.Any(h => h.Hit.Zone is not HitZone.Guard), "some reach the opponent's body, not only the guard");
    }

    private static readonly float ArmReach = 0.188f;

    /// <summary>
    /// 赤が刀の払い面を繰り返し、青（<paramref name="wear"/> をはめて構えて立つだけ）の頭へ斬りかかる。青の前腕・上腕が失ったセル、
    /// はめた物が失った木（トンファーの棒）のセル、刀の打撃が体を削ったセルを返す。
    /// </summary>
    private static (int Arms, int Wood, int Cut, int Forearm, int Head) SwordAtTheGuard(HandWear wear, int seed)
    {
        var tech = FormSwordsman.Techniques(WeaponKind.Katana, ArmReach).First(t => t.Name == "harai-men");
        var red = new SwordsmanBot([tech], FormSwordsman.Guard(WeaponKind.Katana), seed, SwordStance.Rate, rest: 1000f, exact: true, tuning: SwordStance.Tuning,
            bladeLength: FormSwordsman.BladeLength(WeaponKind.Katana, ArmReach), spacing: FormSwordsman.Spacing(WeaponKind.Katana));
        using var game = new BoxingGame(SwordStance.Tuning, seed: seed, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => red,
            CpuWear: wear, PlayerWeapon: WeaponKind.Katana, PlayerTwoHanded: true, Referee: true));
        int Arms() => game.Cpu.Voxels!.Parts.Where(p => new[] { Hand.Left, Hand.Right }.Any(h => p.Body.BodyId == game.Cpu.ForearmId(h) || p.Body.BodyId == game.Cpu.UpperArmId(h))).Sum(p => p.Body.Grid.FilledCount);
        int Wood() => new[] { Hand.Left, Hand.Right }.Sum(h => game.GloveOf(game.Cpu, h)!.Body.Grid.Remaining(BoxerAnatomy.Wood));
        var (arms0, wood0) = (Arms(), Wood());
        var (cut, forearm, head) = (0, 0, 0);
        for (var i = 0; i < 10 * 60; i++)
        {
            game.Step(default, 1f / 60f);
            foreach (var h in game.WeaponHits.Where(h => h.Attacker == game.Player))
            {
                cut += h.Cells;
                forearm += h.Part == BodyPartKind.Forearm ? h.Cells : 0;
                head += h.Part == BodyPartKind.Head ? h.Cells : 0;
            }
        }

        return (arms0 - Arms(), wood0 - Wood(), cut, forearm, head);
    }

    [TestMethod]
    public void ASwordSweepingAtTheGuard_MeetsTheWoodOfTheStick_BeforeTheFleshOfTheForearm()
    {
        var glove = SwordAtTheGuard(HandWear.Glove, 1);
        var tonfa = SwordAtTheGuard(HandWear.Tonfa, 1);
        TestContext.WriteLine($"glove: forearm cut {glove.Forearm}, total cut {glove.Cut}; tonfa: wood lost {tonfa.Wood}, forearm cut {tonfa.Forearm}, total cut {tonfa.Cut}");
        Assert.IsGreaterThan(0, tonfa.Wood, "the blade bites the stick (wood is carved)");
        Assert.AreEqual(0, glove.Wood);
        Assert.IsLessThanOrEqualTo(glove.Forearm, tonfa.Forearm, "the wood in front of the forearm takes the blow the flesh would have");
        Assert.IsLessThan(glove.Cut, tonfa.Cut, "and less of the body is cut in all");
    }

    // ── 教科書・キャラクター ──

    [TestMethod]
    public void TheTextbook_IsLoaded_ValidatesAndLeansOnTheForearmGuard()
    {
        var s = BotCatalog.Tonfa;
        Assert.AreEqual("tonfa", s.Name);
        Assert.AreSame(s, BotCatalog.Style("tonfa"));
        Assert.IsTrue(BotCatalog.Styles.Contains(s));
        Assert.AreEqual(s.Name, s.Validated().Name, "validation keeps a valid textbook as it is");
        Assert.IsGreaterThan(BotCatalog.Champion.ParryWeight + 2f, s.ParryWeight, "the forearm parry is weighted far above the champion's");
        Assert.IsGreaterThan(BotCatalog.Boxer.BlockWeight, s.BlockWeight, "and the raised guard above the boxer's");
        Assert.AreEqual(0f, s.KickRate, "tonfa fights with the arms");
        Assert.AreEqual(0f, s.ThrowRate);
        // 判断の式は拳のボットのまま（違いは JSON の値だけ）。
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("tonfa", _ => new StandGuard(), x => new BoxerBot(s, x), PlayerWear: HandWear.Tonfa));
        for (var i = 0; i < 5 * 60; i++)
        {
            game.Step(default, 1f / 60f);
        }
    }

    [TestMethod]
    public void Tsuyoshi_WearsTheTonfa_OfTheTonfaTextbook_AndIsInTheMenuRoster()
    {
        var c = CharacterRoster.Get("tsuyoshi");
        Assert.AreEqual(HandWear.Tonfa, c.Wear);
        Assert.IsNull(c.Weapon);
        Assert.AreEqual("tonfa", c.StyleName);
        Assert.AreEqual("トンファー", c.EquipmentName);
        Assert.AreSame(BotCatalog.Tonfa, c.Resolve(), "an empty education plays the textbook itself");
        Assert.IsTrue(CharacterRoster.Countries.ContainsKey(c.Country.Id));
        Assert.Contains(c, CharacterRoster.All);
        _ = c.Look.Validate();
        var corners = new MatchSelection(MatchMode.Spectate, c, CharacterRoster.Get("jin")).Corners;
        Assert.AreEqual(HandWear.Tonfa, corners.PlayerWear);
        using var game = new BoxingGame(seed: 1, corners: corners);
        Assert.AreEqual(WeaponKind.Tonfa, game.GloveOf(game.Player, Hand.Left)!.Kind);
        Assert.AreEqual(WeaponKind.Tonfa, game.GloveOf(game.Player, Hand.Right)!.Kind);
    }
}
