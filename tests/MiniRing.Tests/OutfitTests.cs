using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Audio;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>
/// ボクサーの身なり: トランクス（腰と太ももの上）とブーツ（足とすねの下）。外側の皮膚の層を布と革に替えるだけで、
/// 中身と強さ（削れにくさ・肉の量）は変わらない。
/// </summary>
[TestClass]
public sealed class OutfitTests
{
    private static readonly FighterTuning Tuning = FighterTuning.Default;
    private static readonly LowPoly.BoxerModel Model = LowPoly.BoxerModel.Build(Tuning.HalfExtents, Tuning.ModelSubdivisions);

    private static VoxelGrid Build(BodyPartKind kind, Outfit? outfit) =>
        BoxerAnatomy.Build(kind, LowPoly.BoxerModel.HalfExtentsOf(Tuning.HalfExtents, kind), Tuning.VoxelSize, Model.Mesh(kind), outfit);

    /// <summary>元の表面のセルの材質を、高さ（格子の Y、0 = 下）ごとに。</summary>
    private static List<(int Y, byte Material)> Surface(VoxelGrid grid)
    {
        var cells = new List<(int, byte)>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            var (x, y, z) = grid.Coordinates(i);
            if (grid.IsOriginalSurface(x, y, z))
            {
                cells.Add((y, grid.At(x, y, z)));
            }
        }

        return cells;
    }

    [TestMethod]
    public void TheHips_WearTrunks_WithAWhiteWaistbandOnTop()
    {
        var surface = Surface(Build(BodyPartKind.Pelvis, Outfit.RedCorner));

        Assert.IsTrue(surface.All(c => c.Material is BoxerAnatomy.RedTrunks or BoxerAnatomy.Waistband), "no bare skin on the hips");
        var top = surface.Max(c => c.Y);
        Assert.IsTrue(surface.Where(c => c.Y == top).All(c => c.Material == BoxerAnatomy.Waistband), "the top row is the waistband");
        Assert.IsTrue(surface.Where(c => c.Y == 0).All(c => c.Material == BoxerAnatomy.RedTrunks));
    }

    [TestMethod]
    public void TheTrunks_ReachHalfwayDownTheThigh()
    {
        var surface = Surface(Build(BodyPartKind.Thigh, Outfit.BlueCorner));
        var top = surface.Max(c => c.Y);

        Assert.IsTrue(surface.Where(c => c.Y == top).All(c => c.Material == BoxerAnatomy.BlueTrunks), "the hip end is covered");
        Assert.IsTrue(surface.Where(c => c.Y == 0).All(c => c.Material == BoxerAnatomy.Skin), "the knee is bare");
    }

    [TestMethod]
    public void TheBoots_CoverTheFeet_AndRiseAboveTheAnkle()
    {
        foreach (var kind in new[] { BodyPartKind.Foot, BodyPartKind.Toe })
        {
            Assert.IsTrue(Surface(Build(kind, Outfit.RedCorner)).All(c => c.Material == BoxerAnatomy.Boot), $"{kind} is all boot");
        }

        var shin = Surface(Build(BodyPartKind.Shin, Outfit.RedCorner));
        var top = shin.Max(c => c.Y);
        Assert.IsTrue(shin.Where(c => c.Y == 0).All(c => c.Material == BoxerAnatomy.Boot), "the ankle is in the boot");
        Assert.IsTrue(shin.Where(c => c.Y == top).All(c => c.Material == BoxerAnatomy.Skin), "the top of the shin is bare");
    }

    [TestMethod]
    public void TheOutfit_OnlyReplacesTheSkin_SoTheInsideAndTheStrengthStayTheSame()
    {
        foreach (var kind in new[] { BodyPartKind.Pelvis, BodyPartKind.Thigh, BodyPartKind.Shin, BodyPartKind.Foot, BodyPartKind.Toe })
        {
            var bare = Build(kind, null);
            var dressed = Build(kind, Outfit.BlueCorner);
            var clothes = new[] { BoxerAnatomy.BlueTrunks, BoxerAnatomy.Waistband, BoxerAnatomy.Boot };

            Assert.AreEqual(bare.Initial(BoxerAnatomy.Skin), dressed.Initial(BoxerAnatomy.Skin) + clothes.Sum(m => dressed.Initial(m)), kind.ToString());
            for (var m = BoxerAnatomy.Muscle; m <= BoxerAnatomy.Eye; m++)
            {
                Assert.AreEqual(bare.Initial(m), dressed.Initial(m), $"{kind}: {BoxerAnatomy.Palette[m].Name}");
            }
        }

        var skin = BoxerAnatomy.Palette[BoxerAnatomy.Skin];
        foreach (var m in new[] { BoxerAnatomy.RedTrunks, BoxerAnatomy.BlueTrunks, BoxerAnatomy.Waistband, BoxerAnatomy.Boot })
        {
            var cloth = BoxerAnatomy.Palette[m];
            Assert.AreEqual(VoxelTissue.Flesh, cloth.Tissue, $"{cloth.Name} counts toward the part's strength like the skin it covers");
            Assert.AreEqual(skin.Toughness, cloth.Toughness, $"{cloth.Name} breaks like skin");
            Assert.IsFalse(cloth.Vital);
            Assert.AreEqual(GrainKind.Skin, VoxelBreakSound.KindOf(m), $"{cloth.Name} sounds like the outer layer");
        }
    }

    [TestMethod]
    public void ACutInTheClothes_ShowsInAnotherColor()
    {
        var palette = BoxerAnatomy.Palette;
        Vector3 Rgb(byte m) => new(palette[m].Color.X, palette[m].Color.Y, palette[m].Color.Z);
        var clothes = Enumerable.Range(0, palette.Count).Select(m => (byte)m).Where(BoxerAnatomy.IsClothing).ToList();

        CollectionAssert.AreEquivalent(new[] { BoxerAnatomy.RedTrunks, BoxerAnatomy.BlueTrunks, BoxerAnatomy.Waistband, BoxerAnatomy.Boot }, clothes);
        foreach (var cloth in clothes)
        {
            Assert.IsGreaterThan(0.2f, Vector3.Distance(Rgb(cloth), Rgb(BoxerAnatomy.Muscle)), $"{palette[cloth].Name} vs the muscle under it");
            Assert.IsGreaterThan(0.2f, Vector3.Distance(Rgb(cloth), Rgb(BoxerAnatomy.Skin)), $"{palette[cloth].Name} vs bare skin");
        }
    }

    [TestMethod]
    public void WithoutAnOutfit_TheBodyIsBare()
    {
        foreach (var kind in Enum.GetValues<BodyPartKind>())
        {
            Assert.IsTrue(Surface(Build(kind, null)).All(c => c.Material is BoxerAnatomy.Skin or BoxerAnatomy.Eye or BoxerAnatomy.Pupil or BoxerAnatomy.Hair), kind.ToString()); // 顔の目と、頭の髪は身なりと別
        }
    }

    [TestMethod]
    public void InTheRing_ThePlayerWearsRed_AndTheCpuWearsBlue()
    {
        using var game = new BoxingGame(seed: 1);

        Assert.IsGreaterThan(0, game.Player.Voxels!.PartOf(game.Player.Parts.Pelvis)!.Body.Grid.Initial(BoxerAnatomy.RedTrunks));
        Assert.AreEqual(0, game.Player.Voxels!.PartOf(game.Player.Parts.Pelvis)!.Body.Grid.Initial(BoxerAnatomy.BlueTrunks));
        Assert.IsGreaterThan(0, game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Pelvis)!.Body.Grid.Initial(BoxerAnatomy.BlueTrunks));
        Assert.IsGreaterThan(0, game.Cpu.Voxels!.PartOf(game.Cpu.Parts.LeftFoot)!.Body.Grid.Initial(BoxerAnatomy.Boot));
    }
}
