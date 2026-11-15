using Aigis.Foundation.Voxels;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>武器・はめる物の中身をボクセル編集ソフト（Vengi・MagicaVox）の .vox と行き来する。</summary>
[TestClass]
public sealed class WeaponVoxelModelFileTests
{
    private static readonly Lazy<IReadOnlyDictionary<WeaponKind, VoxelGrid>> Generated = new(() => WeaponVoxelModel.Parts(overrides: null));

    [TestMethod]
    public void EveryEditableWeapon_ExportedAndReadBack_BuildsTheSameCells()
    {
        var overrides = WeaponVoxelModel.FromVox(RoundTrip(WeaponVoxelModel.ToVox(Generated.Value)));

        Assert.HasCount(WeaponVoxelModel.Editable.Count, overrides.Kinds);
        foreach (var kind in WeaponVoxelModel.Editable)
        {
            AssertSameCells(Generated.Value[kind], Weapon.Build(kind, BoxerAnatomy.RedGlove, overrides), kind.ToString());
        }
    }

    [TestMethod]
    public void Fist_IsNotExported_AndNotRead()
    {
        Assert.DoesNotContain(WeaponKind.Fist, WeaponVoxelModel.Editable);
        Assert.DoesNotContain(WeaponKind.Fist, Generated.Value.Keys);

        var scene = WeaponVoxelModel.ToVox(Generated.Value);
        var withFist = new VoxScene([.. scene.Models, new VoxModel("Fist", (1, 1, 1), [1])], scene.Palette);
        var error = Assert.Throws<InvalidDataException>(() => WeaponVoxelModel.FromVox(withFist));
        StringAssert.Contains(error.Message, "Fist cannot be edited");
        StringAssert.Contains(error.Message, "BareHandState");
    }

    [TestMethod]
    public void WeaponsLongerThan256Cells_AreSplitIntoChunks_AndJoinedAgain()
    {
        var scene = WeaponVoxelModel.ToVox(Generated.Value);
        foreach (var kind in new[] { WeaponKind.IronSword, WeaponKind.Longsword })
        {
            var chunks = scene.Models.Where(m => m.Name.StartsWith(kind + "#", StringComparison.Ordinal)).ToList();
            Assert.IsGreaterThanOrEqualTo(2, chunks.Count, $"{kind} is longer than {MagicaVox.MaxSize} cells");
            Assert.IsTrue(chunks.All(c => c.Size.X <= MagicaVox.MaxSize && c.Size.Y <= MagicaVox.MaxSize && c.Size.Z <= MagicaVox.MaxSize), kind.ToString());
            Assert.IsFalse(scene.Models.Any(m => m.Name == kind.ToString()), "split weapons have no whole model");
        }

        // 256 以下の武器は分けず、名前は種類だけ。
        Assert.IsTrue(scene.Models.Any(m => m.Name == nameof(WeaponKind.Glove)));
        Assert.IsFalse(scene.Models.Any(m => m.Name.StartsWith("Glove#", StringComparison.Ordinal)));

        var read = RoundTrip(scene);
        var overrides = WeaponVoxelModel.FromVox(read);
        foreach (var kind in new[] { WeaponKind.IronSword, WeaponKind.Longsword })
        {
            Assert.AreEqual(Generated.Value[kind].Dimensions, overrides[kind]!.Dimensions, kind.ToString());
        }
    }

    [TestMethod]
    public void Chunks_AreLaidOutNextToEachOtherInTheScene()
    {
        var read = RoundTrip(WeaponVoxelModel.ToVox(Generated.Value));
        var chunks = read.Models.Where(m => m.Name.StartsWith("IronSword#", StringComparison.Ordinal)).OrderBy(m => m.Name).ToList();

        Assert.IsGreaterThanOrEqualTo(2, chunks.Count);
        Assert.AreEqual(chunks[0].Position!.Value.X + chunks[0].Size.X, chunks[1].Position!.Value.X, "touching along the length");
        Assert.AreEqual(chunks[0].Position!.Value.Y, chunks[1].Position!.Value.Y);
        Assert.AreEqual(chunks[0].Position!.Value.Z, chunks[1].Position!.Value.Z);
    }

    [TestMethod]
    public void BrokenChunks_AreReported()
    {
        var scene = WeaponVoxelModel.ToVox(Generated.Value);
        var read = RoundTrip(scene);

        var missing = new VoxScene(read.Models.Where(m => m.Name != "IronSword#0").ToList(), read.Palette);
        StringAssert.Contains(Assert.Throws<InvalidDataException>(() => WeaponVoxelModel.FromVox(missing)).Message, "IronSword#0");

        var twice = new VoxScene([.. read.Models, read.Models.First(m => m.Name == "IronSword#1")], read.Palette);
        StringAssert.Contains(Assert.Throws<InvalidDataException>(() => WeaponVoxelModel.FromVox(twice)).Message, "chunk 'IronSword#1' appears twice");

        var moved = new VoxScene(read.Models.Select(m => m.Name == "IronSword#1" ? m with { Position = (m.Position!.Value.X + 5, m.Position.Value.Y, m.Position.Value.Z) } : m).ToList(), read.Palette);
        Assert.Throws<InvalidDataException>(() => WeaponVoxelModel.FromVox(moved));

        var both = new VoxScene([.. read.Models, new VoxModel("IronSword", (1, 1, 1), [3])], read.Palette);
        StringAssert.Contains(Assert.Throws<InvalidDataException>(() => WeaponVoxelModel.FromVox(both)).Message, "both whole and as chunks");
    }

    [TestMethod]
    public void BlueLeather_IsReadAsRed_AndBuildsBlueForTheBlueCorner()
    {
        var scene = WeaponVoxelModel.ToVox(Generated.Value);
        var glove = scene.Models.First(m => m.Name == nameof(WeaponKind.Glove));
        var cells = glove.Cells.Select(c => c == BoxerAnatomy.RedGlove ? (byte)BoxerAnatomy.BlueGlove : c).ToArray();
        Assert.Contains((byte)BoxerAnatomy.BlueGlove, cells);
        var painted = new VoxScene(scene.Models.Select(m => m == glove ? m with { Cells = cells } : m).ToList(), scene.Palette);

        var overrides = WeaponVoxelModel.FromVox(RoundTrip(painted));

        var materials = overrides[WeaponKind.Glove]!.Materials;
        Assert.Contains(BoxerAnatomy.RedGlove, materials);
        Assert.DoesNotContain(BoxerAnatomy.BlueGlove, materials);
        var red = Weapon.Build(WeaponKind.Glove, BoxerAnatomy.RedGlove, overrides);
        var blue = Weapon.Build(WeaponKind.Glove, BoxerAnatomy.BlueGlove, overrides);
        Assert.IsGreaterThan(0, red.Initial(BoxerAnatomy.RedGlove));
        Assert.AreEqual(0, red.Initial(BoxerAnatomy.BlueGlove));
        Assert.IsGreaterThan(0, blue.Initial(BoxerAnatomy.BlueGlove));
        Assert.AreEqual(0, blue.Initial(BoxerAnatomy.RedGlove));
        AssertSameCells(Weapon.Build(WeaponKind.Glove, BoxerAnatomy.BlueGlove, overrides: null), blue, "blue glove");
    }

    [TestMethod]
    public void EditedPart_OfTheWrongSize_FallsBackToTheGeneratedWeapon()
    {
        var wrong = new WeaponVoxelOverrides(new Dictionary<WeaponKind, WeaponVoxelOverrides.Cells>
        {
            [WeaponKind.WoodenClub] = new((2, 2, 2), new byte[8]),
        });

        Assert.IsNull(wrong.For(WeaponKind.WoodenClub, Generated.Value[WeaponKind.WoodenClub].Dimensions));
        AssertSameCells(Generated.Value[WeaponKind.WoodenClub], Weapon.Build(WeaponKind.WoodenClub, BoxerAnatomy.RedGlove, wrong), "club");
    }

    [TestMethod]
    public void EditedCells_AreUsedByBuild()
    {
        var overrides = WeaponVoxelModel.FromVox(RoundTrip(WeaponVoxelModel.ToVox(Generated.Value)));
        var sword = overrides[WeaponKind.IronSword]!;
        var index = Array.IndexOf(sword.Materials, BoxerAnatomy.Iron);
        Assert.IsGreaterThanOrEqualTo(0, index);
        sword.Materials[index] = BoxerAnatomy.Wood;

        var built = Weapon.Build(WeaponKind.IronSword, BoxerAnatomy.RedGlove, overrides);

        Assert.AreEqual(Generated.Value[WeaponKind.IronSword].Initial(BoxerAnatomy.Iron) - 1, built.Initial(BoxerAnatomy.Iron));
        Assert.AreEqual(Generated.Value[WeaponKind.IronSword].Initial(BoxerAnatomy.Wood) + 1, built.Initial(BoxerAnatomy.Wood));
        var (dx, dy, _) = built.Dimensions;
        Assert.AreEqual(BoxerAnatomy.Wood, built.At(index % dx, (index / dx) % dy, index / (dx * dy)));
    }

    [TestMethod]
    public void UnknownNamesAndColours_AreReported()
    {
        var scene = WeaponVoxelModel.ToVox(Generated.Value);
        var palette = (uint[])scene.Palette.Clone();
        palette[200] = MagicaVox.Rgba(0, 255, 0);
        var club = scene.Models.First(m => m.Name == nameof(WeaponKind.WoodenClub));
        var cells = (byte[])club.Cells.Clone();
        cells[Array.FindIndex(cells, c => c != 0)] = 200;
        var models = scene.Models.Select(m => m == club ? m with { Cells = cells } : m)
            .Append(new VoxModel("Spear", (1, 1, 1), [1]))
            .Append(new VoxModel("3", (1, 1, 1), [1]))
            .Select(m => m.Name == "Glove" ? m with { Rotated = true } : m)
            .ToList();

        var error = Assert.Throws<InvalidDataException>(() => WeaponVoxelModel.FromVox(new VoxScene(models, palette)));

        StringAssert.Contains(error.Message, "'WoodenClub' has colours that are not a material: #00FF00 × 1");
        StringAssert.Contains(error.Message, "model 'Spear' is not a weapon");
        StringAssert.Contains(error.Message, "model '3' is not a weapon");
        StringAssert.Contains(error.Message, "'Glove' is rotated or mirrored");
    }

    [TestMethod]
    public void ShippedModel_IfAny_FitsTheGeneratedWeapons()
    {
        var shipped = WeaponVoxelOverrides.Shipped;
        if (shipped is null)
        {
            return;
        }

        foreach (var kind in shipped.Kinds)
        {
            Assert.AreEqual(Generated.Value[kind].Dimensions, shipped[kind]!.Dimensions, $"{kind}: re-export with `miniring-arena weaponexport` and edit again");
        }
    }

    private static VoxScene RoundTrip(VoxScene scene)
    {
        using var stream = new MemoryStream();
        MagicaVox.Write(stream, scene);
        stream.Position = 0;
        return MagicaVox.Read(stream);
    }

    private static void AssertSameCells(VoxelGrid expected, VoxelGrid actual, string name)
    {
        Assert.AreEqual(expected.Dimensions, actual.Dimensions, name);
        Assert.AreEqual(expected.HalfExtents, actual.HalfExtents, name);
        var (dx, dy, dz) = expected.Dimensions;
        for (var z = 0; z < dz; z++)
        {
            for (var y = 0; y < dy; y++)
            {
                for (var x = 0; x < dx; x++)
                {
                    Assert.AreEqual(expected.At(x, y, z), actual.At(x, y, z), $"{name} ({x}, {y}, {z})");
                    Assert.AreEqual(expected.IsOriginalSurface(x, y, z), actual.IsOriginalSurface(x, y, z), $"{name} surface ({x}, {y}, {z})");
                }
            }
        }
    }
}
