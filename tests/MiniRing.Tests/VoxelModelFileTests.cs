using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>ボクサーの中身をボクセル編集ソフト（Vengi・MagicaVoxel）の .vox と行き来する。</summary>
[TestClass]
public sealed class VoxelModelFileTests
{
    private static readonly Lazy<IReadOnlyDictionary<BodyPartKind, Aigis.Foundation.Voxels.VoxelGrid>> Generated =
        new(() => BoxerVoxelModel.Parts(FighterTuning.Default, overrides: null));

    [TestMethod]
    public void VoxFile_RoundTripsModelsNamesAndPalette()
    {
        var palette = Enumerable.Range(0, 256).Select(i => MagicaVox.Rgba((byte)i, (byte)(255 - i), 7)).ToArray();
        var a = new VoxModel("first", (2, 3, 4), Enumerable.Range(0, 24).Select(i => (byte)(i % 5)).ToArray());
        var b = new VoxModel("second", (1, 1, 1), [9]);

        var read = RoundTrip(new VoxScene([a, b], palette));

        Assert.HasCount(2, read.Models);
        Assert.AreEqual("first", read.Models[0].Name);
        Assert.AreEqual("second", read.Models[1].Name);
        Assert.AreEqual((2, 3, 4), read.Models[0].Size);
        CollectionAssert.AreEqual(a.Cells, read.Models[0].Cells);
        CollectionAssert.AreEqual(b.Cells, read.Models[1].Cells);
        CollectionAssert.AreEqual(palette[1..], read.Palette[1..], "colour index i keeps its colour");
    }

    [TestMethod]
    public void MaterialColours_AreAllDifferent()
    {
        // 読み込みは色で材質に戻すので、材質の色は全部違わないといけない。
        var colours = Enumerable.Range(1, BoxerAnatomy.Palette.Count - 1).Select(m => BoxerVoxelModel.ColorOf((byte)m)).ToList();
        Assert.HasCount(colours.Count, colours.Distinct());
    }

    [TestMethod]
    public void BoxerModel_ExportedAndReadBack_BuildsTheSameBody()
    {
        var overrides = BoxerVoxelModel.FromVox(RoundTrip(BoxerVoxelModel.ToVox(Generated.Value)));
        var rebuilt = BoxerVoxelModel.Parts(FighterTuning.Default, overrides);

        Assert.HasCount(Generated.Value.Count, overrides.Kinds);
        foreach (var (kind, grid) in Generated.Value)
        {
            AssertSameCells(grid, rebuilt[kind], kind.ToString());
        }
    }

    [TestMethod]
    public void BoxerModel_ReadsByColour_EvenIfTheEditorReordersThePalette()
    {
        // 編集ソフトがパレットの並びを変えても（色番号 m → 256 − m）、色が同じなら同じ材質に戻る。
        var scene = BoxerVoxelModel.ToVox(Generated.Value);
        var palette = new uint[256];
        for (var i = 1; i < 256; i++)
        {
            palette[256 - i] = scene.Palette[i];
        }

        var models = scene.Models.Select(m => m with { Cells = m.Cells.Select(c => c == 0 ? (byte)0 : (byte)(256 - c)).ToArray() }).ToList();
        var overrides = BoxerVoxelModel.FromVox(new VoxScene(models, palette));

        var head = overrides[BodyPartKind.Head]!;
        var grid = Generated.Value[BodyPartKind.Head];
        var (dx, dy, dz) = grid.Dimensions;
        Assert.AreEqual(grid.Dimensions, head.Dimensions);
        for (var z = 0; z < dz; z++)
        {
            for (var y = 0; y < dy; y++)
            {
                for (var x = 0; x < dx; x++)
                {
                    Assert.AreEqual(grid.At(x, y, z), head.Materials[x + (dx * (y + (dy * z)))]);
                }
            }
        }
    }

    [TestMethod]
    public void EditedCells_AreUsed_AndTrunksTakeTheCornerColour()
    {
        var scene = BoxerVoxelModel.ToVox(Generated.Value);
        var overrides = BoxerVoxelModel.FromVox(scene);
        var head = overrides[BodyPartKind.Head]!;
        // 頭の真ん中（脳）を 1 つ骨に塗り替える。
        var (hx, hy, hz) = head.Dimensions;
        var center = (hx / 2) + (hx * ((hy / 2) + (hy * (hz / 2))));
        Assert.AreEqual(BoxerAnatomy.Brain, head.Materials[center]);
        head.Materials[center] = BoxerAnatomy.Bone;

        var edited = BoxerVoxelModel.Parts(FighterTuning.Default, overrides);
        Assert.AreEqual(BoxerAnatomy.Bone, edited[BodyPartKind.Head].At(hx / 2, hy / 2, hz / 2));

        // 腰のトランクスは赤で入っていて、青コーナーで読むと青になる。
        var pelvis = overrides[BodyPartKind.Pelvis]!;
        Assert.Contains(BoxerAnatomy.RedTrunks, pelvis.Materials);
        Assert.DoesNotContain(BoxerAnatomy.BlueTrunks, pelvis.Materials);
        var blue = BoxerAnatomy.Build(BodyPartKind.Pelvis, Generated.Value[BodyPartKind.Pelvis].HalfExtents, FighterTuning.Default.VoxelSize, outfit: Outfit.BlueCorner, overrides: overrides);
        Assert.IsGreaterThan(0, blue.Initial(BoxerAnatomy.BlueTrunks));
        Assert.AreEqual(0, blue.Initial(BoxerAnatomy.RedTrunks));
    }

    [TestMethod]
    public void EditedPart_OfTheWrongSize_FallsBackToTheGeneratedBody()
    {
        var overrides = BoxerVoxelModel.FromVox(BoxerVoxelModel.ToVox(Generated.Value));
        var bigger = FighterTuning.Default with { VoxelSize = FighterTuning.Default.VoxelSize * 0.8f };

        var parts = BoxerVoxelModel.Parts(bigger, overrides);

        Assert.AreNotEqual(Generated.Value[BodyPartKind.Head].Dimensions, parts[BodyPartKind.Head].Dimensions, "other settings, other cell count: the edit does not fit");
        Assert.IsGreaterThan(0, parts[BodyPartKind.Head].Initial(BoxerAnatomy.Brain), "the generated head is used instead");
    }

    [TestMethod]
    public void UnknownColoursAndNames_AreReportedWithTheirPart()
    {
        var scene = BoxerVoxelModel.ToVox(Generated.Value);
        var palette = (uint[])scene.Palette.Clone();
        palette[200] = MagicaVox.Rgba(0, 255, 0);
        var head = scene.Models.First(m => m.Name == "Head");
        var cells = (byte[])head.Cells.Clone();
        cells[Array.FindIndex(cells, c => c != 0)] = 200;
        var models = scene.Models.Select(m => m == head ? m with { Cells = cells } : m)
            .Append(new VoxModel("Tail", (1, 1, 1), [1]))
            .Append(new VoxModel("3", (1, 1, 1), [1])) // 番号は部位の名前ではない（UpperArm にしない）
            .Select(m => m.Name == "Chest" ? m with { Rotated = true } : m)
            .ToList();

        var error = Assert.Throws<InvalidDataException>(() => BoxerVoxelModel.FromVox(new VoxScene(models, palette)));

        StringAssert.Contains(error.Message, "'Head' has colours that are not a material: #00FF00 × 1");
        StringAssert.Contains(error.Message, "model 'Tail' is not a body part");
        StringAssert.Contains(error.Message, "model '3' is not a body part");
        StringAssert.Contains(error.Message, "'Chest' is rotated or mirrored");
    }

    [TestMethod]
    public void TruncatedFile_IsReportedAsABadVoxFile()
    {
        using var full = new MemoryStream();
        MagicaVox.Write(full, BoxerVoxelModel.ToVox(Generated.Value));
        using var cut = new MemoryStream(full.ToArray()[..(int)(full.Length / 2)]);

        Assert.Throws<InvalidDataException>(() => MagicaVox.Read(cut));
    }

    [TestMethod]
    public void ShippedModel_IfAny_FitsTheDefaultSettings()
    {
        // 組み込んだ直しが今の設定の大きさと合わなければ、黙って生成に戻ってしまう: 設定を変えたら書き出し直して直し直す。
        var shipped = BoxerVoxelOverrides.Shipped;
        if (shipped is null)
        {
            return;
        }

        foreach (var kind in shipped.Kinds)
        {
            Assert.AreEqual(Generated.Value[kind].Dimensions, shipped[kind]!.Dimensions, $"{kind}: re-export with `miniring-arena voxexport` and edit again");
        }
    }

    private static VoxScene RoundTrip(VoxScene scene)
    {
        using var stream = new MemoryStream();
        MagicaVox.Write(stream, scene);
        stream.Position = 0;
        return MagicaVox.Read(stream);
    }

    private static void AssertSameCells(Aigis.Foundation.Voxels.VoxelGrid expected, Aigis.Foundation.Voxels.VoxelGrid actual, string part)
    {
        Assert.AreEqual(expected.Dimensions, actual.Dimensions, part);
        var (dx, dy, dz) = expected.Dimensions;
        for (var z = 0; z < dz; z++)
        {
            for (var y = 0; y < dy; y++)
            {
                for (var x = 0; x < dx; x++)
                {
                    Assert.AreEqual(expected.At(x, y, z), actual.At(x, y, z), $"{part} ({x}, {y}, {z})");
                    Assert.AreEqual(expected.IsOriginalSurface(x, y, z), actual.IsOriginalSurface(x, y, z), $"{part} surface ({x}, {y}, {z})");
                }
            }
        }
    }
}
