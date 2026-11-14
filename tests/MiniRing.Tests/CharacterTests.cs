using MiniRing.Bots;
using MiniRing.Voxels;

namespace MiniRing.Tests;

[TestClass]
public sealed class CharacterTests
{
    [TestMethod]
    public void Build_ChangesDimensionsWithoutChangingVoxelCounts_AndMassFollowsVolume()
    {
        using var normal = new BoxingGame(seed: 13);
        using var large = new BoxingGame(seed: 13, corners: BoxingCorners.Default with {
            CpuBody = new BodyBuild(Height: 1.15f, Arms: 1.2f, Legs: 1.1f, Muscle: 1.3f)
        });
        Assert.IsGreaterThan(normal.Cpu.ArmReach, large.Cpu.ArmReach);
        foreach (var (a, b) in normal.Cpu.Voxels!.Parts.Zip(large.Cpu.Voxels!.Parts))
        {
            Assert.AreEqual(a.Body.Grid.Dimensions, b.Body.Grid.Dimensions);
            Assert.AreEqual(a.Body.Grid.FilledCount, b.Body.Grid.FilledCount);
            // 質量は部位の体積に比例する（箱の体積 = 格子の体積。1 セルあたりの質量は同じ材質の配置のまま）。
            // 体の質量を材質の密度から直接出す案は、基準体格の質量・関節の力を変えて 9 件の既存テスト（棍棒・ダウン・運動量配分など）を
            // 壊したため採らない（CLAUDE.md §8 の近道）。体格を変えても、基準との質量比は体積比に一致する。
            var volumeRatio = (b.Body.Grid.CellSize.X * b.Body.Grid.CellSize.Y * b.Body.Grid.CellSize.Z)
                / (a.Body.Grid.CellSize.X * a.Body.Grid.CellSize.Y * a.Body.Grid.CellSize.Z);
            Assert.AreEqual(volumeRatio, large.World.GetMass(b.Body.BodyId) / normal.World.GetMass(a.Body.BodyId), volumeRatio * 0.001f);
        }
        Assert.IsGreaterThan(normal.Cpu.TotalMass, large.Cpu.TotalMass);
        Assert.IsGreaterThan(normal.Cpu.ArmStrength(Hand.Left), large.Cpu.ArmStrength(Hand.Left));
        Assert.AreEqual(large.Cpu.ArmReach, large.PlayerSense.OpponentArmReach);
    }

    [TestMethod]
    public void MuscleLoss_WeakensTheAffectedPart_AndRegenerationRestoresIt()
    {
        using var game = new BoxingGame(seed: 13);
        var f = game.Cpu;
        var id = f.ForearmId(Hand.Left);
        var grid = f.Voxels!.PartOf(id)!.Body.Grid;
        var full = f.Strength(id);
        // 力は肉（皮膚と筋肉）の残りから出て、肉が無くなっても MinMuscleStrength（腱・骨で動かせる分）は残る。
        var cells = Enumerable.Range(0, grid.CellCount).Where(i => grid.MaterialAt(i) is BoxerAnatomy.Muscle or BoxerAnatomy.Skin).ToArray();
        foreach (var i in cells.Where((_, n) => n % 2 == 0))
        {
            grid.SetCellState(i, (0, 0f, false, 0f));
        }
        var half = f.Strength(id);
        Assert.IsTrue(half > 0f && half < full);
        foreach (var i in cells)
        {
            grid.SetCellState(i, (0, 0f, false, 0f));
        }
        Assert.AreEqual(FighterTuning.Default.MinMuscleStrength, f.Strength(id), 0.0001f);
        f.Voxels.Regenerate(int.MaxValue);
        Assert.AreEqual(full, f.Strength(id), 0.0001f);
    }

    [TestMethod]
    public void FaceTemplates_PreserveTwoEyesAndBrain_AndChangeTheFace()
    {
        var grid = BoxerAnatomy.Build(BodyPartKind.Head, FighterTuning.Default.HalfExtents.Head, 0.005f);
        var look = FaceLook.For("test");
        var a = look with { Parts = FaceParts.Default };
        var b = look with { Parts = FaceParts.Default with { EyeSpacing = 1.25f, Nose = 1.3f, Jaw = 1.2f } };
        var ga = a.Restyle(BodyPartKind.Head, grid, look.Palette(grid.Palette));
        var gb = b.Restyle(BodyPartKind.Head, grid, look.Palette(grid.Palette));
        Assert.IsTrue(Enumerable.Range(0, ga.CellCount).Any(i => ga.MaterialAt(i) != gb.MaterialAt(i)));
        Assert.AreEqual(Enumerable.Range(0, ga.CellCount).Count(i => ga.MaterialAt(i) == BoxerAnatomy.Brain),
            Enumerable.Range(0, gb.CellCount).Count(i => gb.MaterialAt(i) == BoxerAnatomy.Brain));
        foreach (var g in new[] { ga, gb })
        {
            Assert.IsGreaterThan(0, Enumerable.Range(0, g.CellCount).Count(i => g.MaterialAt(i) == BoxerAnatomy.Pupil));
        }
    }

    [TestMethod]
    public void BodyBuild_RejectsNonFiniteAndExtremeParameters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BodyBuild(Height: float.NaN).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new BodyBuild(Muscle: 0f).Validate());
    }

    private static int Count(Aigis.Foundation.Voxels.VoxelGrid grid, byte material)
        => Enumerable.Range(0, grid.CellCount).Count(i => grid.MaterialAt(i) == material);

    /// <summary>ゲームと同じ、ローポリの頭の形で作った顔の無い頭。</summary>
    private static Aigis.Foundation.Voxels.VoxelGrid BaseHead()
    {
        var tuning = FighterTuning.Default;
        var shape = MiniRing.LowPoly.BoxerModel.Build(tuning.HalfExtents, tuning.ModelSubdivisions).Mesh(BodyPartKind.Head);
        return BoxerAnatomy.Build(BodyPartKind.Head, tuning.HalfExtents.Head, tuning.VoxelSize, shape);
    }

    private static Aigis.Foundation.Voxels.VoxelGrid Head(FaceParts parts, string name = "face-test")
    {
        var grid = BaseHead();
        var look = FaceLook.For(name) with { Parts = parts };
        return look.Restyle(BodyPartKind.Head, grid, look.Palette(grid.Palette));
    }

    [TestMethod]
    public void FaceTemplates_EachPartMovesItsOwnVoxels()
    {
        var plain = Head(FaceParts.Default);
        // 口: 幅を広げ唇を厚くすると唇のセルが増える。
        Assert.IsGreaterThan(0, Count(plain, BoxerAnatomy.Lip));
        Assert.IsGreaterThan(Count(plain, BoxerAnatomy.Lip), Count(Head(FaceParts.Default with { Lips = 1.8f, MouthWidth = 1.4f }), BoxerAnatomy.Lip));
        // 目: 大きくすると白目が増え、小さくすると減る（視力の材料の体積が目の寸法に従う）。
        var eyes = Count(plain, BoxerAnatomy.Eye);
        Assert.IsGreaterThan(eyes, Count(Head(FaceParts.Default with { EyeSize = 1.3f }), BoxerAnatomy.Eye));
        Assert.IsLessThan(eyes, Count(Head(FaceParts.Default with { EyeSize = 0.7f }), BoxerAnatomy.Eye));
        // 耳・鼻・あごは起伏として形（埋まったセルの数）を変える。
        var filled = plain.FilledCount;
        Assert.IsGreaterThan(filled, Head(FaceParts.Default with { EarSize = 1.5f, Nose = 1.3f, NoseLength = 1.4f }).FilledCount);
        Assert.IsLessThan(filled, Head(FaceParts.Default with { EarSize = 0.6f, Nose = 0.75f, NoseLength = 0.7f }).FilledCount);
        // 眉の太さ・傾きは髪の色のセルの配置を変える（頭は 10 × 14 × 10 セルなので、数は同じでも位置は変わる）。
        var brows = FaceLook.For("face-test") with { Brows = true, Beard = false, Style = Hairstyle.Bald };
        var thick = brows with { Parts = FaceParts.Default with { BrowThickness = 1.8f, BrowTilt = 0.4f } };
        var grid = BaseHead();
        var a = brows.Restyle(BodyPartKind.Head, grid, brows.Palette(grid.Palette));
        var b = thick.Restyle(BodyPartKind.Head, grid, thick.Palette(grid.Palette));
        Assert.IsGreaterThan(0, Enumerable.Range(0, a.CellCount).Count(i => a.MaterialAt(i) != b.MaterialAt(i)), "the brow band moves (the count of hair cells can stay the same)");
    }

    [TestMethod]
    public void FaceTemplates_NeverTouchBrain_AndKeepBothEyes()
    {
        var baseGrid = BaseHead();
        var brain = Count(baseGrid, BoxerAnatomy.Brain);
        foreach (var name in BotCatalog.Names)
        {
            var g = Head(FaceParts.For(name), name);
            Assert.AreEqual(brain, Count(g, BoxerAnatomy.Brain), name);
            foreach (var left in new[] { true, false })
            {
                var side = Enumerable.Range(0, g.CellCount).Where(i =>
                {
                    var (x, y, z) = g.Coordinates(i);
                    return g.MaterialAt(i) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil && (g.CellCenter(x, y, z).X > 0f) == left;
                }).ToList();
                Assert.IsGreaterThan(0, side.Count, $"{name} has an eye on the {(left ? "left" : "right")}");
                Assert.IsTrue(side.Any(i => g.MaterialAt(i) == BoxerAnatomy.Pupil), $"{name} pupil");
            }
        }
    }

    [TestMethod]
    public void EveryRosterFace_IsDistinguishable()
    {
        var grids = BotCatalog.Names.Select(n => (Name: n, Grid: BoxingGameHead(n))).ToList();
        for (var i = 0; i < grids.Count; i++)
        {
            for (var j = i + 1; j < grids.Count; j++)
            {
                var (a, b) = (grids[i].Grid, grids[j].Grid);
                var different = Enumerable.Range(0, a.CellCount).Count(k => a.MaterialAt(k) != b.MaterialAt(k) || a.Palette[a.MaterialAt(k)].Color != b.Palette[b.MaterialAt(k)].Color);
                Assert.IsGreaterThan(20, different, $"{grids[i].Name} vs {grids[j].Name}: only {different} of {a.CellCount} cells differ");
            }
        }
    }

    private static Aigis.Foundation.Voxels.VoxelGrid BoxingGameHead(string name)
    {
        using var game = new BoxingGame(seed: 1, corners: BotCatalog.Corners(name));
        return game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Head)!.Body.Grid;
    }

    [TestMethod]
    public void StyleJson_CarriesFaceAndBuild_IntoTheCorner()
    {
        var face = FaceParts.Default with { Nose = 1.25f, EyeTilt = 0.2f };
        var build = new BodyBuild(Height: 1.1f, Muscle: 1.2f);
        var style = BotCatalog.Style("boxer") with { Name = "big-face", Face = face, Build = build };
        var corners = BotCatalog.Corners(style);
        Assert.AreEqual(face, corners.CpuFace);
        Assert.AreEqual(build, corners.CpuBody);
        var reloaded = BotStyle.FromJson(style.Validated().ToJson());
        Assert.AreEqual(face, reloaded.Face);
        Assert.AreEqual(build, reloaded.Build);
        Assert.Throws<ArgumentOutOfRangeException>(() => (style with { Face = face with { Nose = 9f } }).Validated());
    }

    [TestMethod]
    public void MuscleStrength_FollowsTheWeakestCrossSection_NotTheVolume()
    {
        using var game = new BoxingGame(seed: 13);
        var f = game.Cpu;
        var id = f.ForearmId(Hand.Left);
        var part = f.Voxels!.PartOf(id)!;
        var grid = part.Body.Grid;
        var full = f.Strength(id);
        // 前腕の 1 つの薄切り（軸に沿った 1 層）だけ肉を全部削る: 体積では小さな割合だが、その断面は無くなる。
        var axis = part.Body.BeamAxis!.Value;
        var middle = (axis == 0 ? grid.Dimensions.X : axis == 1 ? grid.Dimensions.Y : grid.Dimensions.Z) / 2;
        var removed = 0;
        for (var i = 0; i < grid.CellCount; i++)
        {
            var (x, y, z) = grid.Coordinates(i);
            if ((axis == 0 ? x : axis == 1 ? y : z) == middle && grid.MaterialAt(i) is BoxerAnatomy.Muscle or BoxerAnatomy.Skin)
            {
                grid.SetCellState(i, (0, 0f, false, 0f));
                removed++;
            }
        }

        var volumeLeft = grid.Fraction(Aigis.Foundation.Voxels.VoxelTissue.Flesh);
        Assert.IsGreaterThan(0, removed);
        Assert.IsGreaterThan(0.8f, volumeLeft, "the cut is a small part of the volume");
        var m = FighterTuning.Default.MinMuscleStrength;
        var byVolume = m + ((1f - m) * volumeLeft);
        var strength = f.Strength(id);
        Assert.IsLessThan(byVolume - 0.02f, strength, "a severed section limits the force more than the lost volume says");
        Assert.IsLessThan(full, strength);
    }

    [TestMethod]
    public void LegStrength_ScalesWithSectionAndLegLength_ButStandingUsesTheCondition()
    {
        var bigBuild = new BodyBuild(Height: 1.1f, Legs: 1.1f, Muscle: 1.3f);
        using var normal = new BoxingGame(seed: 13);
        using var big = new BoxingGame(seed: 13, corners: BoxingCorners.Default with { CpuBody = bigBuild });
        using var thin = new BoxingGame(seed: 13, corners: BoxingCorners.Default with { CpuBody = new BodyBuild(Muscle: 0.7f) });
        Assert.AreEqual(1f, normal.Cpu.LegStrength(true), 1e-5f);
        Assert.IsGreaterThan(normal.Cpu.LegStrength(true), big.Cpu.LegStrength(true));
        Assert.IsLessThan(normal.Cpu.LegStrength(true), thin.Cpu.LegStrength(true));
        Assert.AreEqual(big.Cpu.LegCondition(true) * bigBuild.LegTorqueRatio, big.Cpu.LegStrength(true), 1e-5f);
        // 細い脚でも立てる（立てるかは筋肉の残り = 状態で決まる。細い脚は健康なまま）。
        Assert.IsTrue(thin.Cpu.CanStand);
        for (var i = 0; i < 4 * 240; i++)
        {
            thin.Step(default, 1f / 240f);
        }

        Assert.IsFalse(thin.Cpu.IsKnockedDown);
    }

    [TestMethod]
    public void AnyFace_KeepsABreakableEyeOnBothSides()
    {
        var parts = new List<FaceParts> { FaceParts.Default with { EyeSize = 0.7f, EyeSpacing = 1.25f, EyeTilt = 0.45f }, FaceParts.Default with { EyeSize = 0.7f, EyeSpacing = 0.75f, EyeTilt = -0.45f, EyeHeight = 0.1f } };
        parts.AddRange(Enumerable.Range(0, 300).Select(i => FaceParts.For($"random-{i}")));
        foreach (var p in parts)
        {
            var g = Head(p, "eyes");
            foreach (var left in new[] { true, false })
            {
                Assert.IsTrue(Enumerable.Range(0, g.CellCount).Any(i =>
                {
                    var (x, y, z) = g.Coordinates(i);
                    return g.MaterialAt(i) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil && (g.CellCenter(x, y, z).X > 0f) == left;
                }), $"no eye on the {(left ? "left" : "right")}: {p}");
            }
        }
    }
}
