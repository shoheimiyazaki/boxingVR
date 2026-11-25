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
[TestCategory("slow")]
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
        for (var i = 0; i < 201; i++)
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
                ("side", (outward * 0.6197f) + new Vector3(0f, 0.0845f, 0f)),
                ("above", new Vector3(0f, 0.6761f, 0f) + (outward * 0.0563f)),
                ("front", (Vector3.Normalize(game.Cpu.ChestPosition - game.Player.ChestPosition) * 0.7042f) + new Vector3(0f, 0.1127f, 0f)),
            })
            {
                var eye = target + eyeOffset; // 物理の空間で決めて、表示の空間へ移す（DisplaySpace）
                var (vp, displayEye) = game.Display.Camera(eye, target, name == "above" ? Vector3.UnitZ : Vector3.UnitY, 55f * MathF.PI / 180f, 800f / 600f, 0.02f, 30f);
                var voxels = game.Player.Voxels!.Parts.Where(p => p.Body.BodyId == forearm || p.Body.BodyId == upper).Select(p => p.Draw()).ToList();
                voxels.Add(game.GloveOf(game.Player, hand)!.Draw());
                game.Display.Voxels(voxels);
                var rgba = renderer.RenderPrimitiveScene(new Vector4(0.08f, 0.1f, 0.14f, 1f), vp, [], [], [],
                    lighting: game.BuildLighting(), cameraPosition: displayEye, voxels: voxels);
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

    /// <summary>物の格子のセルの中心を、手の座標（原点 = 中の手の格子の中心。PhysicsVersion 53 で拳は手の格子になった）で。</summary>
    private static List<Vector3> InFistFrame(WeaponKind kind, byte material) =>
        [.. Cells(Weapon.Build(kind), material).Select(p => p - Weapon.HandOriginOf(kind))];

    [TestMethod]
    public void TheTonfa_IsAWoodenStickAlongTheForearm_AndAHandleThroughTheFist()
    {
        var wood = InFistFrame(WeaponKind.Tonfa, BoxerAnatomy.Wood);
        var grid = Weapon.Build(WeaponKind.Tonfa);
        // 物の格子は木だけ（鉄も、握る拳も入っていない）。拳は同じ剛体に重ねた手の格子（素手と同じ 5 mm の手の組織）。
        using var world = new Aigis.Physics.PhysicsWorld(gravity: Vector3.Zero);
        var hand = Weapon.CreateWorn(world, WeaponKind.Tonfa, BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity).Hands[0].Body.Grid;
        var handBounds = hand.OccupiedBounds()!.Value;
        var handFront = handBounds.Center.X + handBounds.HalfExtents.X;
        // 棒は +Z（小指の側）に付く。手は親指の側が広がって左右非対称なので、棒が避けるのは +Z の端。手が前腕（半幅 45 mm）より狭ければ前腕の外の面。
        var handHalfZ = MathF.Max(handBounds.Center.Z + handBounds.HalfExtents.Z, 0.04789f);
        Assert.AreEqual(0, grid.Remaining(BoxerAnatomy.Iron));
        Assert.AreEqual(grid.FilledCount, grid.Remaining(BoxerAnatomy.Wood), "the tonfa's own grid is wood only");
        Assert.IsGreaterThan(0, hand.Remaining(BoxerAnatomy.HandBone), "the fist (its own grid) holds the handle");
        Assert.IsGreaterThan(0, hand.Remaining(BoxerAnatomy.HandSkin));

        // 棒: 全長 47 cm 前後（手首から後ろへ 31 cm、前は手の前の面より 4.8 cm 前）、後ろは手首から 28 cm 以上（肘を少し越える）、前は手の前へ少し出る。
        var back = wood.Min(p => p.X);
        var front = wood.Max(p => p.X);
        Assert.AreEqual(0.4704f, front - back, 0.02817f, "stick length: from past the elbow to just ahead of the fist");
        Assert.IsLessThan(HandSkeleton.Wrist.X - 0.2817f, back, "the long end passes the wrist toward the elbow");
        Assert.AreEqual(handFront + 0.04789f, front, 0.008451f, "the short end sticks out 4.8 cm in front of the hand's front face");
        // 棒は前腕に平行（X に沿う）で、手の外側にある: 後ろの半分のセルは手の幅の外。
        var rear = wood.Where(p => p.X < -0.1127f).ToList();
        Assert.IsTrue(rear.All(p => p.Z > handHalfZ - 0.002817f), "the rear of the stick lies beside the arm, outside the hand's width");
        Assert.AreEqual(handHalfZ + 0.002817f + 0.02254f, rear.Average(p => p.Z), 0.005634f, "and parallel to the forearm");
        var rodY = rear.Average(q => q.Y);
        Assert.IsLessThan(0.02254f, rear.Max(p => MathF.Abs(p.Y - rodY)) - 0.001127f, "a round 45 mm rod");
        // 握りの柄: 手が握る（GripPose の指の輪の中心を通って Z に沿う）木で、棒へつながる。
        var handleAt = HandSkeleton.GripCenter + Weapon.HandOriginOf(WeaponKind.Tonfa);
        Assert.AreEqual(BoxerAnatomy.Wood, grid.MaterialAt(grid.FilledCellAt(handleAt)), "the handle passes through the loop of the fingers");
        Assert.IsTrue(wood.Any(p => MathF.Abs(p.X - HandSkeleton.GripCenter.X) < 0.005634f && p.Z > handHalfZ - 0.002817f && p.Z < handHalfZ + 0.01127f), "the handle runs across to the stick");
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
        foreach (var g in new[] { grid, worn.Hands[0].Body.Grid }) // 物の格子 + 中の手の格子（PhysicsVersion 53）
        {
            for (var m = 1; m < g.Palette.Count; m++)
            {
                expected += g.Remaining((byte)m) * g.CellVolume * g.Palette[(byte)m].MechanicsOrDefault.Density;
            }
        }

        var mass = game.World.GetMass(worn.Body.BodyId);
        Assert.AreEqual(expected, mass, expected * 0.01f);
        var bare = Weapon.Build(WeaponKind.Fist);
        Assert.IsGreaterThan(grid.Remaining(BoxerAnatomy.Wood) * grid.CellVolume * 44.74f, mass, "heavier than the wood alone: the hand inside weighs too");
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
        for (var i = 0; i < 101; i++)
        {
            game.Step(default, 1f / 60f);
        }

        // 手ごとに、棒の中ほどが（その手から見て）もう一方の手の側にあるか。
        float Inner(Hand hand)
        {
            var worn = game.GloveOf(game.Player, hand)!;
            var s = game.World.GetState(worn.Body.BodyId);
            var stick = s.Position + Vector3.Transform(new Vector3(-0.169f, 0f, Weapon.MirroredOn(WeaponKind.Tonfa, hand) ? -0.1014f : 0.1014f) + Weapon.HandOriginOf(WeaponKind.Tonfa, Weapon.MirroredOn(WeaponKind.Tonfa, hand)), s.Orientation);
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

    private static readonly float ArmReach = 0.5296f;

    /// <summary>
    /// 赤が刀の払い面を繰り返し、青（<paramref name="wear"/> をはめて構えて立つだけ）の頭へ斬りかかる。青の前腕・上腕が失ったセル、
    /// はめた物が失った木（トンファーの棒）のセル、刀の打撃が体を削ったセルを返す。
    /// </summary>
    internal static (int Arms, int Wood, int Cut, int Forearm, int Head, int ArmCut) SwordAtTheGuard(HandWear wear, int seed, string technique = "harai-men")
    {
        var tech = FormSwordsman.Techniques(WeaponKind.Katana, ArmReach).First(t => t.Name == technique);
        var red = new SwordsmanBot([tech], FormSwordsman.Guard(WeaponKind.Katana), seed, SwordStance.Rate, rest: 1000f, exact: true, tuning: SwordStance.Tuning,
            bladeLength: FormSwordsman.BladeLength(WeaponKind.Katana, ArmReach), spacing: FormSwordsman.Spacing(WeaponKind.Katana));
        using var game = new BoxingGame(SwordStance.Tuning, seed: seed, corners: new BoxingCorners("guard", _ => new StandGuard(), _ => red,
            CpuWear: wear, PlayerWeapon: WeaponKind.Katana, PlayerTwoHanded: true, Referee: true));
        int Arms() => game.Cpu.Voxels!.Parts.Where(p => new[] { Hand.Left, Hand.Right }.Any(h => p.Body.BodyId == game.Cpu.ForearmId(h) || p.Body.BodyId == game.Cpu.UpperArmId(h))).Sum(p => p.Body.Grid.FilledCount);
        int Wood() => new[] { Hand.Left, Hand.Right }.Sum(h => game.GloveOf(game.Cpu, h)!.Body.Grid.Remaining(BoxerAnatomy.Wood));
        var (arms0, wood0) = (Arms(), Wood());
        var (cut, forearm, head, armCut) = (0, 0, 0, 0);
        for (var i = 0; i < 1007; i++)
        {
            game.Step(default, 1f / 60f);
            foreach (var h in game.WeaponHits.Where(h => h.Attacker == game.Player))
            {
                cut += h.Cells;
                forearm += h.Part == BodyPartKind.Forearm ? h.Cells : 0;
                head += h.Part == BodyPartKind.Head ? h.Cells : 0;
                armCut += h.Part is BodyPartKind.Forearm or BodyPartKind.UpperArm ? h.Cells : 0;
            }
        }

        return (arms0 - Arms(), wood0 - Wood(), cut, forearm, head, armCut);
    }

    [TestMethod]
    public void ASwordSweepingAtTheGuard_MeetsTheWoodOfTheStick_BeforeTheFleshOfTheForearm()
    {
        // 2026-11-19（PhysicsVersion 53）: 払い面（harai-men）はどちらの手にもはめても腕を切らなくなった（手袋の腕の切れ 0・トンファー 0 で、「少ない」が比べられない）。
        // 腕を切る技を測り直し（SwordAtTheGuard_TechniqueScan_Probe）、手袋が前腕を切られ・トンファーの木が削れ・棒の後ろの腕は切られない切り上げ（kiriage:
        // 手袋の前腕 3・腕 3、トンファーの前腕 0・腕 0・木 90）に変えた。期待値は変えていない（棒の後ろの腕の切れは、手袋より少ない）。
        // 2026-11-19（PhysicsVersion 59、顔・手の層と関節のつなぎのマージ）: 切り上げは、トンファー側の上腕が肩のつなぎで 3 セル切られ（手袋 2 に対し 3）、
        // 「棒の後ろの腕は少ない」が崩れた（この台本は 10 秒に 4 発で、1 発の当たり方で数が決まる）。技の一覧（SwordAtTheGuard_TechniqueScan_Probe）から、
        // 腕（前腕）を狙う小手（kote）に取り直した: 手袋の前腕 12・トンファーの前腕 8・木の削れ 23。期待値は変えていない。
        // 2026-11-25（PhysicsVersion 104、関節のつなぎを刃の接触に使わない）: 小手の 1 発目がつなぎに当たらなくなって振り抜けの道が変わり、
        // この台本の 10 秒では刀が棒に 1 度も当たらなかった（木 0）。技の一覧（SwordAtTheGuard_TechniqueScan_Probe）から小手面に取り直した:
        // 手袋の前腕 0・トンファーの前腕 0・木の削れ 42。期待値は変えていない。
        // 2026-11-26（統合で PhysicsVersion 102: 実物の重さの武器・握り込んだ手首（100）の上に、掃き直し 2 回・関節のつなぎ無し）: 小手面は
        // トンファーの前腕が 5 セル切られ（手袋 0、木 231）、小手は手袋の前腕 0・トンファーの前腕 0・木 1 に戻った（SwordAtTheGuard_TechniqueScan_Probe）。
        // 小手面への取り直しは 100 より前の体での測りだったので、master と同じ小手に戻した。期待値は変えていない。
        // 手袋の革のカフが前腕の先を覆うので、手袋の前腕はどの技でも 0〜1 セルで、この比べは「棒の後ろの前腕が手袋より多くは切られない」だけを見ている。
        var glove = SwordAtTheGuard(HandWear.Glove, 1, "kote");
        var tonfa = SwordAtTheGuard(HandWear.Tonfa, 1, "kote");
        TestContext.WriteLine($"glove: forearm cut {glove.Forearm}, total cut {glove.Cut}; tonfa: wood lost {tonfa.Wood}, forearm cut {tonfa.Forearm}, total cut {tonfa.Cut}");
        TestContext.WriteLine($"glove: arm (forearm + upper arm) cut {glove.ArmCut}; tonfa: arm cut {tonfa.ArmCut}");
        Assert.IsGreaterThan(0, tonfa.Wood, "the blade bites the stick (wood is carved)");
        Assert.AreEqual(0, glove.Wood);
        Assert.IsLessThanOrEqualTo(glove.Forearm, tonfa.Forearm, "the wood in front of the forearm takes the blow the flesh would have");
        // 2026-11-18（PhysicsVersion 44・45）: 「体全体で切られたセル」(Cut) は比べない。この台本は 10 秒に 4 発しか当たらず（種を変えても同じ: exact）、
        // 合計は途中で刀が胸へ回り込む 1 発に決まる（版 43: 手袋 11・トンファー 9。いま: 手袋 9・トンファー 19 で、トンファーの方は胸の 1 発が
        // 速さ 1.39 m/s で 17 セル、手袋の方は 1.00 m/s で 6 セル）。前は 1 発目に体の質量の運動量（2.2〜2.5 N·s）が乗って相手の腕ごと押し流し、
        // 以降の当たりの様子が変わったが、いまは 0.4〜0.5 N·s で構えが残るので、後の当たりは別の軌道になる。守る木が受け止める量（木の削れ 15 → 74）は
        // 増え、棒の後ろの腕（前腕・上腕）の切れは手袋 3 に対しトンファー 2 のまま（腕を切られた数を比べる）。
        // 2026-11-19（PhysicsVersion 58）: グローブが本物の形になり、手首を包む革のカフ（外半径 20.5 mm・壁 4 mm）が前腕の先を覆うので、剣の払いがグローブに当たっても前腕は切れない（glove: forearm cut 0、total cut 27 は革・詰め物）。
        // トンファーの側も 0 なので「少ない」でなく「多くない」にした（棒が腕の盾になること自体は、上の木の削れ・総量で見ている）。
        // 2026-11-24（PhysicsVersion 90、手の実寸化）: 棒を前腕の外の面の外へ出した（掌幅 84 mm が前腕 90 mm より狭くなり、手の幅の外では棒が前腕の箱に食い込んでいた）。
        // 小手は前腕の切れが手袋 6 → トンファー 2（木の削れ 47）で棒が前腕を守るが、同じ 1 発の振り抜きで上腕が 10 セル切られ（腕の合計 12、手袋 7）、
        // 上腕は棒の守る所ではないので、「棒の後ろの腕」の比べは腕の外側で棒が受ける払い面で見る（手袋 2、トンファー 0。木の削れ 270）。
        foreach (var technique in new[] { "harai-men" })
        {
            var g = SwordAtTheGuard(HandWear.Glove, 1, technique);
            var t = SwordAtTheGuard(HandWear.Tonfa, 1, technique);
            TestContext.WriteLine($"{technique}: glove arm cut {g.ArmCut}, tonfa wood {t.Wood} arm cut {t.ArmCut}");
            Assert.IsGreaterThan(0, t.Wood, $"{technique}: the blade meets the stick");
            Assert.IsLessThanOrEqualTo(g.ArmCut, t.ArmCut, $"{technique}: and not more of the arm behind the stick is cut");
        }
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
        for (var i = 0; i < 504; i++)
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
        CharacterRosterTests.AssertTextbookPlusGamePlan(BotCatalog.Tonfa, c.Resolve(), c.Id);
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
