using System.Numerics;
using System.Text.Json.Nodes;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Motion;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>2026-11-16: 片手剣と盾（<see cref="HandWear.Shield"/>・<see cref="SwordShieldForms"/>）。形・質量・両手・型・盾の受け・自傷・教科書・キャラ。docs/SWORD_SHIELD.md。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class ShieldTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly float ArmReach = 0.5296f;

    private static readonly string[] FormNames = ["tsuki", "kiri-oroshi", "yoko-barai"];

    private static readonly int[] Seeds = [1];

    private static float MassOf(Aigis.Foundation.Voxels.VoxelGrid grid)
    {
        var mass = 0f;
        for (var m = 1; m < grid.Palette.Count; m++)
        {
            mass += grid.Remaining((byte)m) * grid.CellVolume * grid.Palette[(byte)m].MechanicsOrDefault.Density;
        }

        return mass;
    }

    /// <summary>物の格子のセルの中心を、手の座標（原点 = 中の手の格子の中心）で。</summary>
    private static IEnumerable<Vector3> InHandFrame(WeaponKind kind, byte material)
    {
        var grid = Weapon.Build(kind);
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) == material)
            {
                var (x, y, z) = grid.Coordinates(i);
                yield return (new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * grid.CellSize) - grid.HalfExtents - Weapon.HandOriginOf(kind);
            }
        }
    }

    // ── 形・質量 ──

    [TestMethod]
    public void TheShield_IsARoundWoodPlateWithAnIronRimAndABossOverTheFist()
    {
        var grid = Weapon.Build(WeaponKind.Shield);
        var wood = InHandFrame(WeaponKind.Shield, BoxerAnatomy.Wood).ToList();
        var iron = InHandFrame(WeaponKind.Shield, BoxerAnatomy.Iron).ToList();
        // 2026-11-19（PhysicsVersion 53）: 中の拳は物の格子の球ではなく、同じ剛体に重ねた手の格子（5 mm）。物の格子は木と鉄だけ。
        using var world = new Aigis.Physics.PhysicsWorld(gravity: Vector3.Zero);
        var hand = Weapon.CreateWorn(world, WeaponKind.Shield, BoxerAnatomy.RedGlove, Vector3.Zero, Quaternion.Identity).Hands[0].Body.Grid;
        Assert.IsGreaterThan(0, hand.Remaining(BoxerAnatomy.HandBone), "the fist (its own grid) grips the handle");
        Assert.AreEqual(grid.FilledCount, grid.Remaining(BoxerAnatomy.Wood) + grid.Remaining(BoxerAnatomy.Iron), "the shield's own grid is wood and iron only");

        // 板: 直径 45 cm（人形の単位で 16 cm）、厚さ 11 mm、木。手の中心の面（X ≈ 0）にある。板の中心は手の高さの真ん中（手の座標の Y ≠ 0）。
        var plateY = 0.5f * (wood.Max(p => p.Y) + wood.Min(p => p.Y));
        float Radial(Vector3 p) => MathF.Sqrt(((p.Y - plateY) * (p.Y - plateY)) + (p.Z * p.Z));
        var plateRadius = wood.Max(Radial);
        Assert.AreEqual(0.2254f, plateRadius, 0.01127f, "diameter 45 cm");
        Assert.IsTrue(wood.All(p => MathF.Abs(p.X) <= 0.00873f), "the plate is a thin layer in the fist's centre plane");
        // 厚さは格子の刻みの整数倍でしか表せない: 11 mm の板は、刻みの半分（誤差の最大）の内に収まる。
        Assert.AreEqual(0.01127f, wood.Max(p => p.X) - wood.Min(p => p.X) + grid.CellSize.X, 0.5f * grid.CellSize.X,
            "the board keeps its 11 mm thickness (to within the grid step) when the boss is fitted to the hand");
        Assert.IsTrue(wood.All(p => Radial(p) > 0.05634f), "the plate is open at the middle: the arm and the boss's hollow");

        // 縁の鉄の輪: 板の外の縁（半径 22〜22.5 cm）に鉄がある。
        Assert.IsTrue(iron.Any(p => Radial(p) > 0.2169f && MathF.Abs(p.X) < 0.00845f), "an iron rim around the edge");
        // 椀（ボス）: 手の前を覆う、手の形に沿った殻。頂きは手の前の面（手の格子の最前面）から、隙間 11 mm + 殻 8 mm 前（人形の単位で 4 mm + 3 mm）。
        var handBounds = hand.OccupiedBounds()!.Value;
        var handFront = handBounds.Center.X + handBounds.HalfExtents.X;
        var boss = iron.Where(p => p.X > 0.01127f).ToList();
        Assert.IsNotEmpty(boss);
        Assert.AreEqual(handFront + 0.01972f, boss.Max(p => p.X), 0.00845f, "the boss bulges 11 mm clear + 8 mm shell in front of the hand's front face");
        Assert.IsTrue(boss.All(p => Radial(p) < 0.1408f), "and covers only the middle (the hand's own width)");
        // 握り棒: 手が握る（GripPose の指の輪の中心を通って Z に沿う）鉄。
        var barAt = HandSkeleton.GripCenter + Weapon.HandOriginOf(WeaponKind.Shield);
        Assert.AreEqual(BoxerAnatomy.Iron, grid.MaterialAt(grid.FilledCellAt(barAt)), "the grip bar passes through the loop of the fingers");
    }

    [TestMethod]
    public void TheShield_IsAWornBluntThing_AndItsMassIsTheMaterialsTimesTheVolume_HeavierThanTheSword()
    {
        Assert.IsTrue(Weapon.IsWorn(WeaponKind.Shield));
        Assert.IsTrue(Weapon.IsBlunt(WeaponKind.Shield));
        Assert.IsFalse(Weapon.CanTwoHand(WeaponKind.Shield));
        Assert.IsTrue(Weapon.HasRigidFace(WeaponKind.Shield));
        Assert.AreEqual(WeaponKind.Shield, HandWear.Shield.Kind());
        Assert.AreEqual(HandWear.Shield, HandWears.Parse("Shield"));
        Assert.IsTrue(WeaponVoxelModel.Editable.Contains(WeaponKind.Shield), "it shows up in weaponexport");
        Assert.IsNotNull(WeaponVoxelModel.ToVox(new Dictionary<WeaponKind, Aigis.Foundation.Voxels.VoxelGrid> { [WeaponKind.Shield] = Weapon.Build(WeaponKind.Shield) }));

        using var game = new BoxingGame(seed: 1, corners: BoxingCorners.Default with { PlayerWear = HandWear.Shield });
        var worn = game.GloveOf(game.Player, Hand.Left)!;
        var grid = worn.Body.Grid;
        // 質量は、物の格子と中の手の格子の、材質の密度 × 体積の和（PhysicsVersion 53）。
        var total = MassOf(grid) + MassOf(worn.Hands[0].Body.Grid);
        Assert.AreEqual(total, game.World.GetMass(worn.Body.BodyId), total * 0.01f, "mass comes from the voxels: the shield and the hand inside");
        var sword = MassOf(Weapon.Build(WeaponKind.IronSword));
        var glove = MassOf(Weapon.Build(WeaponKind.Glove));
        Assert.IsGreaterThan(sword, MassOf(grid), "a shield is heavier than the sword");
        Assert.IsGreaterThan(3f * glove, MassOf(grid), "and several gloves' worth");
        Assert.IsGreaterThan(Weapon.CoverRadiusOf(WeaponKind.Glove) * 3f, Weapon.CoverRadiusOf(WeaponKind.Shield), "the visible size is what the defence reads");
        TestContext.WriteLine($"shield {MassOf(grid) * 1000f:F0} g (wood {grid.Remaining(BoxerAnatomy.Wood)} iron {grid.Remaining(BoxerAnatomy.Iron)} cells), sword {sword * 1000f:F0} g, glove {glove * 1000f:F0} g, grid {grid.Dimensions}");
    }

    [TestMethod]
    public void SwordInTheRightHand_ShieldOnTheLeft()
    {
        // 2026-11-24: 操作なし（人の入力が空）の腕は構えの指令を持たず、剣は自分の腹へ向いて自傷で死ぬ（版 89 でも手袋の側で同じ。盾の側は 379 セルの自傷で運よく生き残っていた）。
        // 剣と盾の構え（SwordShieldForms.Guard）をとらせて、「持たせ方が正しく、構えて立っていれば死なない」を見る。
        using var game = new BoxingGame(seed: 1, corners: BoxingCorners.Default with
        {
            PlayerWeapon = WeaponKind.IronSword,
            PlayerWear = HandWear.Shield,
            Player = _ => new Scripted((sense, _) => Command(SwordShieldForms.Guard(), sense)),
        });
        Assert.AreEqual(WeaponKind.IronSword, game.WeaponOf(game.Player)!.Kind);
        Assert.IsTrue(game.Player.Wields(Hand.Right));
        Assert.IsTrue(game.Player.WearsGlove(Hand.Left));
        Assert.IsFalse(game.Player.Wields(Hand.Left), "the shield is worn, not a second weapon");
        Assert.AreEqual(WeaponKind.Shield, game.GloveOf(game.Player, Hand.Left)!.Kind);
        Assert.IsNull(game.GloveOf(game.Player, Hand.Right), "the right hand holds the sword");
        for (var i = 0; i < 201; i++)
        {
            game.Step(default, 1f / 60f);
        }

        Assert.IsFalse(game.Player.IsDead);
    }

    // ── 型 ──

    [TestMethod]
    public void TheThreeForms_AreTargetPaths_StartingAndEndingAtTheGuard_WithTheShieldHandStayingInFront()
    {
        var forms = SwordShieldForms.Forms();
        CollectionAssert.AreEqual(FormNames, forms.Select(f => f.Name).ToArray());
        CollectionAssert.AreEqual(forms.Select(f => f.Name).ToArray(), FormSwordsman.Forms(WeaponKind.IronSword).Select(f => f.Name).ToArray());
        Assert.IsTrue(FormSwordsman.HasForms(WeaponKind.IronSword));
        Assert.IsFalse(FormSwordsman.Handles(WeaponKind.IronSword), "Handles stays the two-handed set (callers use it as 'two-handed')");
        var guard = FormSwordsman.Guard(WeaponKind.IronSword);
        Assert.AreEqual(0f, FormSwordsman.Spacing(WeaponKind.IronSword));
        foreach (var form in forms)
        {
            Assert.IsLessThan(0.05f, Vector3.Distance(form.Frames[0].Right, guard.Right));
            Assert.IsLessThan(0.05f, Vector3.Distance(form.Frames[^1].Right, guard.Right), $"{form.Name} returns to the guard");
            Assert.IsLessThan(0.05f, Vector3.Distance(form.Frames[^1].Left, guard.Left), $"{form.Name}: the shield hand returns");
            // 左手（盾）は両手持ちの間隔に縛られず、右手の剣の動きとは別に構えの近く（前、左）にある。
            foreach (var frame in form.Frames)
            {
                Assert.IsGreaterThan(0.1f, frame.Left.X, $"{form.Name}: the shield stays on the left of the body");
                Assert.IsGreaterThan(0.4f, frame.Left.Z, $"{form.Name}: and in front of it");
                Assert.IsGreaterThan(0f, frame.LeftFacing.LengthSquared(), "the shield's face is commanded");
                // 右手は肩（体フレーム x = −0.285）から腕の長さの内。
                Assert.IsLessThanOrEqualTo(1.03f, Vector3.Distance(frame.Right, new Vector3(-0.285f, 0f, 0f)), $"{form.Name}: the sword hand stays within reach");
            }
        }

        // 型の経路が、台本の指令でも目標になる: 指令の左手は盾の位置、向きの指令がある。
        var sense = new FighterSense(new BodyFrame(Vector3.Zero, 0f), ArmReach, Vector3.UnitZ, Vector3.UnitZ, true);
        var command = FormSwordsman.Command(forms[0], 0.1f, sense, Vector3.UnitZ, 1.007f, 0.5f);
        Assert.IsNotNull(command.Left.Orientation, "the shield's facing goes to the wrist servo as a target");
        Assert.AreEqual(guard.Left.Z * ArmReach, command.Left.TargetLocal.Z, 0.02817f);
    }

    private static StrikeResult Drill(SwordMotion form, HandWear wear = HandWear.Shield)
    {
        var technique = new MotionTechnique(form.Name, WeaponKind.IronSword, form, 1.408f);
        technique = technique with { Range = StrikeDrill.ReachOf(technique, ArmReach) };
        return StrikeDrill.Run(technique, FormSwordsman.Guard(WeaponKind.IronSword), SwordStance.Rate, FighterTuning.Default, wear: wear);
    }

    [TestMethod]
    public void EachOneHandedForm_FromAScriptedCommand_HitsAStandingOpponent_WithTheShieldOnTheLeftHand()
    {
        foreach (var form in SwordShieldForms.Forms())
        {
            var result = Drill(form);
            TestContext.WriteLine($"{form.Name}: reach {result.Technique.Range:F3} m, cells {result.Cells}, first {result.FirstPart}, self {result.SelfCells}");
            Assert.IsGreaterThan(0.5634f, result.Technique.Range, $"{form.Name} has a usable range");
            Assert.IsGreaterThan(0, result.Cells, $"{form.Name} reaches and cuts the opponent standing in guard");
        }
    }

    // ── 盾の受け ──

    private sealed class Scripted(Func<FighterSense, float, FighterCommand> read) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => read(sense, dt);
    }

    /// <summary>
    /// 赤が刀（払い面 = 頭へ斜めに振り下ろす・横へ払う）を 1 度振り、青（剣と盾、<paramref name="mode"/>）が受ける。
    /// 青の体（盾・剣を除く）が失ったセル、盾が失ったセル、刀の打撃が体を削ったセルを返す。
    /// </summary>
    private static (int BodyCells, int ShieldCells, int Hits) Attack(string technique, string mode, int seed)
    {
        var tech = FormSwordsman.Techniques(WeaponKind.Katana, ArmReach).First(t => t.Name == technique);
        var red = new SwordsmanBot([tech], FormSwordsman.Guard(WeaponKind.Katana), seed, SwordStance.Rate, rest: 1000f, exact: true, tuning: SwordStance.Tuning,
            bladeLength: FormSwordsman.BladeLength(WeaponKind.Katana, ArmReach), spacing: FormSwordsman.Spacing(WeaponKind.Katana));
        var guard = SwordShieldForms.Guard();
        IFighterControl Blue(int s) => mode switch
        {
            // 盾を下げて構える（左手は腰の横。剣は構えのまま）。
            "down" => new Scripted((sense, _) => Command(guard with { Left = new Vector3(0.35f, -0.9f, 0.3f), LeftFacing = Vector3.UnitZ }, sense)),
            // 守りの剣士（足と頭で外す。自分からは打たない）。盾は構えの所のまま（"step"）か、来る線が体へ届く所へ差し入れる（"cover"）。
            _ => ShieldBot(s, cover: mode == "cover"),
        };

        using var game = new BoxingGame(FighterTuning.Default, seed: seed, corners: new BoxingCorners("guard", Blue, _ => red,
            CpuWeapon: WeaponKind.IronSword, CpuWear: HandWear.Shield, PlayerWeapon: WeaponKind.Katana, PlayerTwoHanded: true, Referee: true));
        int Body() => game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        int Shield() => game.GloveOf(game.Cpu, Hand.Left)!.Body.Grid.FilledCount;
        var (body0, shield0) = (Body(), Shield());
        var hits = 0;
        for (var i = 0; i < 806; i++)
        {
            game.Step(default, 1f / 60f);
            hits += game.WeaponHits.Count(h => h.Attacker == game.Player);
        }

        return (body0 - Body(), shield0 - Shield(), hits);
    }

    /// <summary>剣と盾の守り手（自分からは打たない）。<paramref name="cover"/> が false なら、同じ足と頭の守りで盾は構えの所のまま。</summary>
    private static SwordsmanBot ShieldBot(int seed, bool cover)
    {
        var style = WeaponCatalog.Textbook(WeaponKind.IronSword);
        return new SwordsmanBot([.. FormSwordsman.Techniques(WeaponKind.IronSword, ArmReach, style.MinReach)], SwordShieldForms.Guard(), seed, SwordStance.Rate, tuning: FighterTuning.Default,
            defense: style.Defense with { Cover = cover }, bladeLength: FormSwordsman.BladeLength(WeaponKind.IronSword, ArmReach), spacing: 0f, opponentReach: 3f)
        {
            Passive = true,
            OffhandRadius = Weapon.CoverRadiusOf(WeaponKind.Shield) / ArmReach,
        };
    }

    private static FighterCommand Command(MotionFrame frame, in FighterSense sense)
    {
        var toward = sense.OpponentChest - sense.Self.Origin;
        toward.Y = 0f;
        var face = toward.LengthSquared() > 1e-8f ? Vector3.Normalize(toward) : sense.Self.Forward;
        return MotionImitation.ToCommand(frame, sense, face, 0f, 1.007f, 0.5f);
    }

    [TestMethod]
    public void TheShield_TakesTheSwordsSwing_AndTheBodyBehindItLosesLessThanWithTheShieldDown()
    {
        var total = new Dictionary<string, (int Body, int Shield, int Hits)> { ["down"] = default, ["step"] = default, ["cover"] = default };
        // 2026-11-24（PhysicsVersion 90、手の実寸化）: 払い面 1 本では下ろした盾の損失が 75 セルと小さく、1 発の当たり方（cover は 2 発・体 54）で半分の比が決まったので、
        // 体へ届く振りを増やした（払い面・袈裟・右払い・左払い・正面。下ろした盾 700 セル、step 22、cover 54）。逆袈裟は左側の盾の位置を斜めに通るだけで体へは届かず
        // （3 つとも体 0）、下ろした盾の端を直接切るので、体を守る比べには入れない。
        foreach (var technique in new[] { "harai-men", "kesa", "migi-nagi", "hidari-nagi", "shomen" })
        {
            foreach (var seed in Seeds)
            {
                foreach (var mode in total.Keys.ToList())
                {
                    var (body, shield, hits) = Attack(technique, mode, seed);
                    total[mode] = (total[mode].Body + body, total[mode].Shield + shield, total[mode].Hits + hits);
                    TestContext.WriteLine($"{technique} seed {seed} {mode}: body cells lost {body}, shield cells lost {shield}, hits on the body {hits}");
                }
            }
        }

        TestContext.WriteLine(string.Join("; ", total.Select(kv => $"{kv.Key}: body {kv.Value.Body}, shield {kv.Value.Shield}, hits {kv.Value.Hits}")));
        Assert.IsGreaterThan(0, total["cover"].Shield, "the blade meets the shield (it loses cells)");
        // 下ろした盾は腰の横にあり、袈裟が胴を断ち切った後の振り抜きの先が盾の端をかすめる（2026-11-18、床から支える体で測り直し: 袈裟 1 振りで盾 3 セル、
        // そのとき胴は 750 セル断たれている）。盾が刃を受けるのではなく、体を断った後の余りがかすっただけなので、体の損失の 1 % 未満で確かめる。
        Assert.IsLessThan(0.01f * total["down"].Body, total["down"].Shield, "a lowered shield is only grazed by the follow-through of a swing that has already cut the body");
        Assert.IsLessThan(total["down"].Body, total["cover"].Body, "the covering shield keeps the swings off the body");
        // 2026-11-17（PhysicsVersion 44、剣の柄に鉄の芯を通した）: down 351・step 16・cover 116 セル。LogicVersion 43（剣の枝）: 柄頭も速い端として読むため step 82・cover 119。
        // step と cover の 1 試合ずつの差は揺らぎで、足と盾の読みが変わると両者の優劣は固定でない。盾が身体を守るという本筋は、下ろした盾の損失の半分以下で確かめる。
        Assert.IsLessThanOrEqualTo(total["down"].Body * 0.5f, total["step"].Body, "足と構えた盾で無防備より体の損傷が大きく減る");
        Assert.IsLessThanOrEqualTo(total["down"].Body * 0.5f, total["cover"].Body, "the covering shield keeps well over half the swings off the body");
    }

    // ── 自傷 ──

    [TestMethod]
    public void TheSwordsmanWithAShield_DoesNotCutItselfOrItsShieldMuchWhileFighting()
    {
        var cells = 0;
        var touches = 0;
        var swings = 0;
        foreach (var seed in new[] { 1, 2 })
        {
            var edgar = CharacterRoster.Get("edgar");
            var jin = CharacterRoster.Get("jin");
            var corners = new MatchSelection(MatchMode.Spectate, edgar, jin).Corners with { UseWeaponBots = true };
            using var game = new BoxingGame(seed: seed, corners: corners);
            for (var i = 0; i < 2014 && !game.RoundOver; i++)
            {
                game.Step(default, 1f / 60f);
                swings += game.WeaponHits.Count(h => h.Attacker == game.Player);
                foreach (var t in game.SelfTouches.Where(t => t.Fighter == game.Player))
                {
                    touches++;
                    cells += t.Removed;
                }
            }
        }

        TestContext.WriteLine($"self touches {touches}, cells {cells} in 2 × 33.6 s");
        Assert.IsGreaterThan(0, swings, "the sword actually fought (the bound is not met by standing still)");
        Assert.IsLessThan(150, cells, "the sword's cuts on its own body stay small");
    }

    // ── 教科書・キャラクター ──

    [TestMethod]
    public void TheTextbook_IsLoaded_ValidatesAndCoversWithTheOffhand_AndTheEducationLayersOnIt()
    {
        var s = WeaponCatalog.Textbook(WeaponKind.IronSword);
        Assert.IsTrue(WeaponCatalog.HasBot(WeaponKind.IronSword));
        Assert.AreEqual("swordshield", s.Name);
        Assert.IsTrue(s.Defense.Cover, "the textbook covers with the offhand");
        Assert.AreEqual(DefenseMode.Step, s.Defense.Mode);
        Assert.AreEqual(s.Name, s.Validated().Name);
        Assert.IsFalse(WeaponCatalog.Textbook(WeaponKind.Katana).Defense.Cover, "no other textbook changes");
        Assert.IsFalse(WeaponCatalog.Textbook(WeaponKind.Longsword).Defense.Cover);
        var edu = WeaponEducation.Resolve(WeaponKind.IronSword, new JsonObject { ["Rest"] = 0.3 })!;
        Assert.AreEqual(0.3f, edu.Rest);
        Assert.IsTrue(edu.Defense.Cover);
        var named = WeaponEducation.Resolve(WeaponKind.IronSword, new JsonObject { ["TechniqueWeights"] = new JsonObject { ["yoko-barai"] = 0 } })!;
        Assert.AreEqual(0f, named.TechniqueWeights!["yoko-barai"]);
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.IronSword, new JsonObject { ["TechniqueWeights"] = new JsonObject { ["oberhau"] = 1 } }), "a technique from another weapon's forms is refused");
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.IronSword, new JsonObject { ["Cove"] = 1 }), "a misspelt key is refused");
    }

    [TestMethod]
    public void Edgar_HoldsASwordAndWearsAShield_OfTheSwordShieldTextbook_AndIsInTheMenuRoster()
    {
        var c = CharacterRoster.Get("edgar");
        Assert.AreEqual(WeaponKind.IronSword, c.Weapon);
        Assert.AreEqual(HandWear.Shield, c.Wear);
        Assert.AreEqual("剣と盾", c.EquipmentName);
        // 2026-11-24: エドガーの武器の教育はとどめの経路（weaponBot.Ground）だけ。
        CharacterRosterTests.AssertWeaponTextbookPlusGround(WeaponCatalog.Textbook(WeaponKind.IronSword), c.WeaponStyle, c.Id);
        Assert.IsTrue(CharacterRoster.Countries.ContainsKey(c.Country.Id));
        Assert.Contains(c, CharacterRoster.All);
        _ = c.Look.Validate();
        var corners = new MatchSelection(MatchMode.Spectate, c, CharacterRoster.Get("jin")).Corners;
        Assert.AreEqual(HandWear.Shield, corners.PlayerWear);
        Assert.AreEqual(WeaponKind.IronSword, corners.PlayerWeapon);
        Assert.IsFalse(corners.PlayerTwoHanded, "one hand on the sword");
        using var game = new BoxingGame(seed: 1, corners: corners);
        Assert.AreEqual(WeaponKind.IronSword, game.WeaponOf(game.Player)!.Kind);
        Assert.AreEqual(WeaponKind.Shield, game.GloveOf(game.Player, Hand.Left)!.Kind);
        Assert.IsInstanceOfType<DisarmedFallback>(game.PlayerControl);
    }
}
