using System.Numerics;
using Aigis.Foundation;
using Aigis.Physics;
using Aigis.Render;
using MiniRing.Bots;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>
/// VFC の試合の芯（2026-11-18）: 六角形の金網の檻（<see cref="ArenaShape.HexCage"/>）と、ルールの値（ダウンを数えない・判定しない・休みに傷が前線の再生で少しずつ戻る）。
/// 既定の道（四角いリング・今のルール）は別のテスト（ReplayTests ほか）が変わらないことを守る。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class VfcCoreTests
{
    private const float Dt = 1f / 240f;

    // ── 檻の形 ──

    [TestMethod]
    public void CageGeometry_MatchesTheSquareRingsArea_AndKeepsTheSquaresValuesUnchanged()
    {
        // 正六角形の面積 = (√3 / 2) · Size²。檻を作った時の四角いリング（3.38 m・6.76 m。人形の単位で 1.2 m・2.4 m。2026-11-18 からはドリルの場 DrillBoxing・DrillWeapons）と同程度の広さにしてある。
        // WVC の試合のリングは 2026-11-18 に 5.3 m 換算（RingLayout.Boxing）へ広げたが、檻（VFC）の大きさは変えていない。
        static float Area(RingLayout l) => l.IsCage ? MathF.Sqrt(3f) / 2f * l.Size * l.Size : l.Size * l.Size;
        Assert.AreEqual(Area(RingLayout.DrillBoxing), Area(RingLayout.Cage), 0.3968f);
        Assert.AreEqual(Area(RingLayout.DrillWeapons), Area(RingLayout.WeaponsCage), 1.190f);
        // 外接円の半径 R = Size / √3 は頂点までの距離（X 軸上）、台座と天板はその外へ出る。
        Assert.AreEqual(RingLayout.Cage.Size / MathF.Sqrt(3f), RingLayout.Cage.Circumradius, 1e-6f);
        Assert.IsGreaterThan(RingLayout.Cage.Circumradius, RingLayout.Cage.PlinthHalf);
        Assert.IsGreaterThan(RingLayout.Cage.PlinthHalf, RingLayout.Cage.TableHalf);
        // 四角の値は今のまま。
        Assert.AreEqual(1.69f + 0.4225f, RingLayout.DrillBoxing.TableHalf);
        Assert.AreEqual(1.69f + 0.2254f, RingLayout.DrillBoxing.PlinthHalf);
        Assert.AreEqual(ArenaShape.RopedSquare, RingLayout.Boxing.Shape);
    }

    [TestMethod]
    public void Cage_StartAndRestCorners_AreInsideTheHexagon_ForBothSizes()
    {
        foreach (var layout in new[] { RingLayout.Cage, RingLayout.WeaponsCage })
        {
            // 休むコーナーは体と持ち物の輪郭から決まる（RingLayout.CornerFor）: 肩幅ほどの輪郭のどの点も、金網から隙間（CornerClearance）だけ内側。
            Vector2[] outline = [new(0.3f, 0.3f), new(-0.3f, 0.3f), new(0.3f, -0.3f), new(-0.3f, -0.3f)];
            foreach (var red in new[] { true, false })
            {
                var corner = layout.RestCorner(red, outline);
                var closest = outline.Min(q => layout.EdgeDistance(corner + new Vector3(q.X, 0f, q.Y)));
                Assert.IsGreaterThan(RingLayout.CornerClearance - 1e-4f, closest, $"{layout.Size} m cage, red={red}: the outline at the rest corner is inside the walls");
                Assert.IsGreaterThan(0.3f, layout.EdgeDistance(corner), $"{layout.Size} m cage, red={red}: the rest corner stands at least the outline's half width inside");
            }

            var apart = layout.StartGap * 0.5f;
            Assert.IsGreaterThan(0.2817f, layout.EdgeDistance(layout.Center + new Vector3(0f, 0f, apart)), "start position (blue)");
            Assert.IsGreaterThan(0.2817f, layout.EdgeDistance(layout.Center - new Vector3(0f, 0f, apart)), "start position (red)");
        }

        // 檻の試合の中で、始めの体も、休みに歩いたあとの体も六角形の内側。
        using var game = CageGame(MatchRules.Vfc with { RoundSeconds = 1.678f, Rounds = 2, BreakSeconds = 5.035f });
        Assert.IsGreaterThan(0.2817f, game.Layout.EdgeDistance(game.Player.PelvisPosition));
        Assert.IsGreaterThan(0.2817f, game.Layout.EdgeDistance(game.Cpu.PelvisPosition));
        var minEdge = float.MaxValue;
        for (var t = 0f; t < 6.714f; t += Dt)
        {
            game.Step(default, Dt);
            minEdge = MathF.Min(minEdge, MathF.Min(game.Layout.EdgeDistance(game.Player.PelvisPosition), game.Layout.EdgeDistance(game.Cpu.PelvisPosition)));
        }

        Console.WriteLine($"min edge distance during start + break walk: {minEdge:0.000} m, in break {game.InBreak}");
        Assert.IsGreaterThan(0.05634f, minEdge);
    }

    [TestMethod]
    public void Cage_WallsHoldABodyWalkingIntoThem_FromEveryFaceAndVertex()
    {
        // 12 方向（6 つの面と 6 つの頂点）へ 6.7 秒（人形の単位で 4 秒）歩かせ続けても、骨盤は六角形の内側（辺までの距離 > 0）に残る。
        var layout = RingLayout.Cage;
        var worst = float.MaxValue;
        for (var i = 0; i < 12; i++)
        {
            var angle = (MathF.PI / 6f) * i; // 0° は頂点、30° は面、…
            var direction = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            using var world = new PhysicsWorld();
            LivingRoom.AddColliders(world, layout);
            var ring = new Ring(world, layout, BoxingGame.RingSurfaceHeight, LivingRoom.PhysicsStageBase);
            var f = Fighter.Spawn(world, FighterTuning.Default, "walker", ring.SurfaceCenter, 0f);
            var min = float.MaxValue;
            for (var s = 0; s < (int)(6.714f / Dt); s++)
            {
                f.ApplyControl(new FighterCommand(direction, Vector3.UnitZ,
                    new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
                    new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false)), Dt);
                world.Step(Dt);
                min = MathF.Min(min, layout.EdgeDistance(f.PelvisPosition));
            }

            Console.WriteLine($"direction {i * 30,3}°: closest the pelvis got to the wall {min:0.000} m, final y {f.PelvisPosition.Y:0.000}");
            Assert.IsGreaterThan(0f, min, $"direction {i * 30}° went through the wall");
            Assert.IsGreaterThan(BoxingGame.RingSurfaceHeight, f.PelvisPosition.Y, "did not fall off the canvas");
            worst = MathF.Min(worst, min);
        }

        Console.WriteLine($"worst: {worst:0.000} m");
    }

    [TestMethod]
    public void Cage_WallsAreThickerThanTheFastestStepOfAFistOrABlade()
    {
        // 240 Hz で拳（10 m/s）・剣の先（16.8 m/s）が 1 歩で進む距離（4.2 cm・7.0 cm）より、壁の箱は厚い。
        Assert.IsGreaterThan(16.78f * Dt, HexCage.WallThickness);
        Assert.IsGreaterThan(0.8451f, HexCage.WallHeight);
    }

    // ── ルールの値 ──

    [TestMethod]
    public void Vfc_DownsAreNotCounted_ButDeathStillEndsTheMatch()
    {
        using var game = CageGame(MatchRules.Vfc, referee: true);
        Settle(game);
        game.Cpu.Concussion = 3f; // 意識 0 が 40 秒以上続く深さ（立てず動かない）
        var longestDown = 0f;
        var longestStill = 0f;
        for (var t = 0f; t < 26.85f; t += Dt)
        {
            game.Step(default, Dt);
            longestDown = MathF.Max(longestDown, game.Cpu.DownTime);
            longestStill = MathF.Max(longestStill, game.Cpu.StillTime);
            Assert.IsFalse(game.RoundOver, $"ended by a count at {t:0.0}s: {game.Message}");
        }

        Console.WriteLine($"VFC: down {longestDown:0.0}s, still {longestStill:0.0}s, not ended");
        Assert.IsGreaterThan(BoxingGame.CountSeconds, longestDown, "lay longer than the ten count");
        Assert.IsGreaterThan(BoxingGame.StillKoSeconds, longestStill, "lay still longer than the out-cold time");

        game.Cpu.Die("test");
        for (var i = 0; i < 34 && !game.MatchOver; i++)
        {
            game.Step(default, Dt);
        }

        Assert.IsTrue(game.MatchOver, "death ends it");
        StringAssert.Contains(game.Message, "DESTROYED");
    }

    [TestMethod]
    public void TvRules_StillCountTheDownedOut_Unchanged()
    {
        using var game = StillGame(MatchRules.Tv, null);
        Settle(game);
        game.Cpu.Concussion = 3f;
        for (var t = 0f; t < 20.14f && !game.RoundOver; t += Dt)
        {
            game.Step(default, Dt);
        }

        Assert.IsTrue(game.RoundOver, "the WVC rules count it out");
    }

    [TestMethod]
    public void Vfc_FiveRoundsWithBothAlive_IsADraw_AndNobodyWins()
    {
        using var game = CageGame(MatchRules.Vfc with { RoundSeconds = 1.678f, Rounds = 2, BreakSeconds = 0.8392f });
        for (var t = 0f; t < 6.714f && !game.MatchOver; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
        }

        Assert.IsTrue(game.MatchOver);
        Assert.AreEqual(2, game.MatchRound);
        Assert.AreEqual("DRAW — BOTH SURVIVE", game.Decision);
        Assert.AreEqual(0, game.PlayerWins);
        Assert.AreEqual(0, game.CpuWins);
        Assert.AreEqual("DRAW — BOTH SURVIVE", game.Message);
    }

    [TestMethod]
    public void VfcPreset_HasTheAgreedValues()
    {
        var v = MatchRules.Vfc;
        Assert.AreEqual(50.35f, v.RoundSeconds);
        Assert.AreEqual(5, v.Rounds);
        Assert.AreEqual(41.96f, v.BreakSeconds);
        Assert.AreEqual(0f, v.RegenVoxelsPerSecond);
        Assert.IsFalse(v.CountsDowns);
        Assert.IsFalse(v.JudgesDecision);
        Assert.IsTrue(MatchRules.Tv.CountsDowns && MatchRules.Tv.JudgesDecision, "WVC defaults are unchanged");
    }

    [TestMethod]
    public void Vfc_BreakRegrowsVoxelsFromTheLivingEdge_AndWithoutRegrowthNothingComesBack()
    {
        // 2026-11-18（指示書 F）: VFC の休みは前線の再生で傷が少しずつ戻る（以前の「VFC の休みは治らない」は決まりが変わった）。
        var vfc = FilledAcrossABreak(MatchRules.Vfc with { RoundSeconds = 1.678f, Rounds = 2, BreakSeconds = 1.678f });
        var none = FilledAcrossABreak(MatchRules.Vfc with { RoundSeconds = 1.678f, Rounds = 2, BreakSeconds = 1.678f, RegrowthSpeed = 0f });
        var tv = FilledAcrossABreak(MatchRules.Tv with { RoundSeconds = 1.678f, Rounds = 2, BreakSeconds = 1.678f });
        Console.WriteLine($"VFC {vfc.Start} -> {vfc.End}; no regrowth {none.Start} -> {none.End}; TV {tv.Start} -> {tv.End}");
        Assert.IsGreaterThan(vfc.Start, vfc.End, "the VFC break grows carved voxels back from the edge");
        Assert.AreEqual(none.Start, none.End, "with no regrowth, carved voxels stay carved");
        Assert.IsGreaterThan(tv.Start, tv.End, "the WVC break grows them back");
    }

    [TestMethod]
    public void NoCount_NoReferee_NeverHoldsTheFightForABreath()
    {
        using var game = CageGame(MatchRules.Vfc with { RoundSeconds = 8.392f, Rounds = 1 });
        Settle(game);
        game.Cpu.Concussion = 0.7f; // 倒れて、しばらくして意識が戻る
        for (var t = 0f; t < 10.07f; t += Dt)
        {
            game.Step(default, Dt);
            Assert.IsFalse(game.Holding, "no referee, no breath");
        }
    }

    // ── 記録・再生 ──

    [TestMethod]
    public void CageWithVfcRules_RecordsAndReplaysBitForBit()
    {
        var corners = BotCatalog.Corners("boxer") with { RedBotStyle = BotCatalog.Swarmer, Referee = false, Ring = RingLayout.Cage };
        using var game = new BoxingGame(corners: corners, rules: MatchRules.Vfc);
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < ReplayRecorder.ChecksumInterval * 12; i++)
            {
                recorder.Step(game, default, 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.AreEqual(ArenaShape.HexCage, data.Header.Corners.Ring?.Shape, "the cage is in the replay");
        Assert.IsFalse(data.Header.MatchSettings!.Rules!.CountsDowns);
        Assert.IsFalse(data.Header.MatchSettings.Rules.JudgesDecision);
        var result = ReplayPlayer.Verify(data);
        Assert.IsTrue(result.Reproduced);
        Assert.IsGreaterThan(1, result.ChecksumsMatched);
    }

    [TestMethod]
    public void OldSettingsJson_WithoutTheNewFields_ReadsAsTheSquareRingAndTheTvRules()
    {
        var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        var layout = System.Text.Json.JsonSerializer.Deserialize<RingLayout>("{\"Size\":1.2,\"StartGap\":0.44}", options);
        Assert.AreEqual(ArenaShape.RopedSquare, layout!.Shape);
        var rules = System.Text.Json.JsonSerializer.Deserialize<MatchRules>("{\"RoundSeconds\":30,\"Rounds\":5,\"BreakSeconds\":10,\"RegenVoxelsPerSecond\":150}", options);
        Assert.IsTrue(rules!.CountsDowns);
        Assert.IsTrue(rules.JudgesDecision);
    }

// ── 目で見る ──

    [TestMethod]
    [TestCategory("visual")]
    public void Cage_RendersFromAboveAndThroughTheFence()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MiniRing.slnx")))
        {
            dir = dir.Parent;
        }

        var outDir = Path.Combine(dir!.FullName, ".visualcheck");
        Directory.CreateDirectory(outDir);
        foreach (var layout in new[] { RingLayout.Cage, RingLayout.WeaponsCage })
        {
            var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
            var corners = new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)) with { Ring = layout, Referee = false };
            using var game = new BoxingGame(seed: 1, corners: corners, rules: MatchRules.Vfc);
            for (var i = 0; i < 201; i++)
            {
                game.Step(default, 1f / 60f);
            }

            using var renderer = new HeadlessRenderer(960, 540);
            var screen = new Vector2(960, 540);
            var center = layout.Center with { Y = BoxingGame.RingSurfaceHeight + 0.4225f };
            var size = layout.Size;
            foreach (var (name, eye) in new[]
            {
                ("above", center + new Vector3(0f, size * 1.1f, -size * 0.7f)),
                ("fence", center + new Vector3(size * 1.1f, 0.2817f, -size * 0.15f)),
            })
            {
                var (vp, displayEye) = game.Display.Camera(eye, center, Vector3.UnitY, 50f * MathF.PI / 180f, 960f / 540f, 0.02f, 50f);
                var (instances, lines) = game.BuildRenderables();
                var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, vp, instances, lines, [],
                    particles: game.BuildParticles(), lighting: game.BuildLighting(), shadow: game.BuildShadow(), cameraPosition: displayEye,
                    postProcess: game.BuildPostProcess(), voxels: game.BuildVoxelVolumes());
                PngWriter.WriteRgba(Path.Combine(outDir, $"vfc_cage_{size:0.0}_{name}.png"), 960, 540, rgba);
            }
        }
    }

    // ── 下ごしらえ ──

    private static BoxingGame StillGame(MatchRules rules, RingLayout? ring)
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        var corners = new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)) with { Ring = ring };
        return new BoxingGame(seed: 1, corners: corners, rules: rules);
    }

    /// <summary>檻の中、レフェリーなし、立っているだけのボット同士。</summary>
    private static BoxingGame CageGame(MatchRules rules, bool referee = false)
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        var corners = new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)) with { Ring = RingLayout.Cage, Referee = referee };
        return new BoxingGame(seed: 1, corners: corners, rules: rules);
    }

    private static void Settle(BoxingGame game)
    {
        for (var t = 0f; t < 0.8392f; t += Dt)
        {
            game.Step(default, Dt);
        }
    }

    private static (int Start, int End) FilledAcrossABreak(MatchRules rules)
    {
        using var game = StillGame(rules, null);
        Settle(game);
        var f = game.Cpu;
        int Filled() => f.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var half = f.HalfExtents.Chest;
        var point = f.ChestPosition + (f.Frame.Forward * half.Z) + (Vector3.UnitY * (0.2f * half.Y));
        for (var i = 0; i < 4; i++)
        {
            const float Mass = 45f, Speed = 3.692f;
            var damage = PunchMechanics.Damage(Mass * Speed) * PunchMechanics.Sharpness(Speed);
            game.ApplyHit(new HitEvent(game.Player, Hand.Right, f, HitZone.Body, point, Speed, Mass, 0.7f, damage), f.Parts.Chest, -f.Frame.Forward * Speed);
        }

        while (!game.InBreak)
        {
            game.Step(default, Dt);
        }

        var start = Filled();
        while (game.InBreak)
        {
            game.Step(default, Dt);
        }

        return (start, Filled());
    }
}
