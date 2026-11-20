using System.Text.Json;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>顔の部品の表（Codex の参考図 → JSON）と、組み立て（格子に依らない・混ぜられる・決定的）。docs/CHARACTERS.md §2。</summary>
[TestClass]
public sealed class FacePartLibraryTests
{
    private static FaceSpec Spec(params (string Category, string Id, float Weight)[] choice)
    {
        var parts = FacePartLibrary.Categories.ToDictionary(c => c, c => (IReadOnlyDictionary<string, float>)new Dictionary<string, float> { [$"{c}.1"] = 1f });
        parts["beard"] = new Dictionary<string, float>();
        foreach (var group in choice.GroupBy(c => c.Category))
        {
            parts[group.Key] = group.ToDictionary(c => c.Id, c => c.Weight);
        }

        return new FaceSpec(parts, FaceParts.Default);
    }

    private static readonly string[] Names = ["Taro", "Ren", "Gaston", "Narong", "Yoandry"];

    private static string Embedded(string name)
    {
        using var s = typeof(FacePartLibrary).Assembly.GetManifestResourceStream($"MiniRing.Voxels.FaceParts.{name}.json")!;
        return new StreamReader(s).ReadToEnd();
    }

    [TestMethod]
    public void Library_HasAtLeastSixPartsPerCategory_AndTheBaseHead()
    {
        var library = FacePartLibrary.Default;
        foreach (var category in FacePartLibrary.Categories)
        {
            Assert.IsGreaterThanOrEqualTo(6, library.Parts(category).Count, category);
        }

        Assert.AreEqual(20, library.BaseFront.Width);
        Assert.AreEqual(28, library.BaseFront.Height);
    }

    [TestMethod]
    public void Load_StopsOnBrokenFiles()
    {
        var all = FacePartLibrary.Categories.ToDictionary(c => c, Embedded);
        var baseJson = Embedded("base");
        _ = FacePartLibrary.Load(baseJson, all); // 壊す前は読める

        var unknownSymbol = all.ToDictionary(p => p.Key, p => p.Value);
        unknownSymbol["eyes"] = all["eyes"].Replace("\"ssssssss\"", "\"sssssssz\"", StringComparison.Ordinal);
        Assert.AreNotEqual(all["eyes"], unknownSymbol["eyes"]);
        Assert.ThrowsExactly<InvalidDataException>(() => FacePartLibrary.Load(baseJson, unknownSymbol));

        var missing = all.Where(p => p.Key != "nose").ToDictionary(p => p.Key, p => p.Value);
        Assert.ThrowsExactly<InvalidDataException>(() => FacePartLibrary.Load(baseJson, missing));

        var notJson = all.ToDictionary(p => p.Key, p => p.Value);
        notJson["mouth"] = "{ broken";
        Assert.ThrowsExactly<InvalidDataException>(() => FacePartLibrary.Load(baseJson, notJson));

        var badWidth = all.ToDictionary(p => p.Key, p => p.Value);
        badWidth["brows"] = all["brows"].Replace("\"width\": 8", "\"width\": 7", StringComparison.Ordinal);
        Assert.AreNotEqual(all["brows"], badWidth["brows"]);
        Assert.ThrowsExactly<InvalidDataException>(() => FacePartLibrary.Load(baseJson, badWidth));
    }

    [TestMethod]
    public void Spec_ValidationRejectsUnknownPartsAndBadWeights()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => Spec(("nose", "nose.9", 1f)).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => Spec(("nose", "nose.1", -1f)).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => Spec(("nose", "nose.1", float.NaN)).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => Spec(("nose", "eyes.1", 1f)).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => Spec(("cheeks", "cheeks.1", 1f)).Validate());
        var noNose = new FaceSpec(Spec().Parts.Where(p => p.Key != "nose").ToDictionary(p => p.Key, p => p.Value), FaceParts.Default);
        Assert.ThrowsExactly<InvalidDataException>(() => noNose.Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => (Spec() with { Values = FaceParts.Default with { EyeSize = 9f } }).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => (Spec() with { Skin = new System.Numerics.Vector3(2f, 0, 0) }).Validate());
        _ = Spec(("nose", "nose.2", 0.3f), ("nose", "nose.3", 0.7f)).Validate();
    }

    [TestMethod]
    public void FromJson_ReadsPartsWeightsValuesAndColours_AndFillsTheRestFromTheName()
    {
        using var doc = JsonDocument.Parse("""{ "parts": { "nose": { "nose.2": 0.6, "nose.3": 0.4 }, "beard": {} }, "values": { "NoseWidth": 1.2 }, "skin": [0.5, 0.4, 0.3] }""");
        var spec = FaceSpec.FromJson(doc.RootElement, "tester");
        Assert.AreEqual(0.6f, spec.Parts["nose"]["nose.2"]);
        Assert.IsEmpty(spec.Parts["beard"]);
        Assert.AreEqual(1.2f, spec.Values.NoseWidth);
        Assert.AreEqual(0.4f, spec.Skin!.Value.Y);
        Assert.AreEqual(FaceSpec.For("tester").Parts["eyes"].Keys.Single(), spec.Parts["eyes"].Keys.Single(), "parts the file does not name come from the name");

        using var bad = JsonDocument.Parse("""{ "parts": { "nose": { "nose.7": 1 } } }""");
        Assert.ThrowsExactly<InvalidDataException>(() => FaceSpec.FromJson(bad.RootElement, "tester"));
        using var unknown = JsonDocument.Parse("""{ "partz": {} }""");
        Assert.ThrowsExactly<InvalidDataException>(() => FaceSpec.FromJson(unknown.RootElement, "tester"));
    }

    [TestMethod]
    public void SameName_SameFace_AndDifferentNamesDiffer()
    {
        var a = FaceComposer.Compose(FaceSpec.For("Taro"), 20, 28, 20);
        var b = FaceComposer.Compose(FaceSpec.For("Taro"), 20, 28, 20);
        CollectionAssert.AreEqual(a.Cells, b.Cells);
        var faces = Names.Select(n => FaceComposer.Compose(FaceSpec.For(n), 20, 28, 20).Cells).ToList();
        Assert.IsGreaterThan(3, faces.Select(f => string.Concat(f.Select(c => (char)('a' + (int)c)))).Distinct().Count());
    }

    [TestMethod]
    public void Blend_EndsAreTheParts_AndTheMiddleIsBetween()
    {
        FaceVolume Of(params (string, string, float)[] c) => FaceComposer.Compose(Spec(c), 20, 28, 20);
        // 鼻は部品の絵を測った値を丸めた単純な形（NoseShape）なので、絵の混ぜは髪で確かめる（短い髪とアフロ）。
        var straight = Of(("hair", "hair.2", 1f));
        var hooked = Of(("hair", "hair.6", 1f));
        var scaled = Of(("hair", "hair.2", 7f));
        CollectionAssert.AreEqual(straight.Cells, scaled.Cells, "weights are normalised, a single part at any weight is that part");
        var mid = Of(("hair", "hair.2", 0.5f), ("hair", "hair.6", 0.5f));
        CollectionAssert.AreNotEqual(straight.Cells, hooked.Cells);
        CollectionAssert.AreNotEqual(mid.Cells, straight.Cells);
        CollectionAssert.AreNotEqual(mid.Cells, hooked.Cells);
        for (var i = 0; i < mid.Cells.Length; i++)
        {
            if (straight.Cells[i] != FaceMaterial.Empty && straight.Cells[i] == hooked.Cells[i])
            {
                Assert.AreNotEqual(FaceMaterial.Empty, mid.Cells[i], $"a cell both ends fill stays filled (x={i % 20} y={(i / 20) % 28} z={i / 560})");
            }
        }

        // 目の混ぜ: 白目の体積が両端の間にある。
        var monolid = Of(("eyes", "eyes.1", 1f)).Count(FaceMaterial.White);
        var wide = Of(("eyes", "eyes.5", 1f)).Count(FaceMaterial.White);
        var halfway = Of(("eyes", "eyes.1", 0.5f), ("eyes", "eyes.5", 0.5f)).Count(FaceMaterial.White);
        Assert.IsGreaterThan(monolid, wide, "a wide-open eye shows more white than a monolid");
        Assert.IsTrue(halfway >= Math.Min(monolid, wide) - 2 && halfway <= Math.Max(monolid, wide) + 12, $"mid {halfway} between {monolid} and {wide}");
    }

    [TestMethod]
    public void Resolution_DoesNotChangeWhichPartsAreThere()
    {
        var spec = FaceSpec.For("Gaston");
        var coarse = FaceComposer.Compose(spec, 20, 28, 20);
        var fine = FaceComposer.Compose(spec, 40, 56, 40);
        // 体積は解像度の 3 乗に比例して増える（±15% 以内で一致 = 同じ形を細かく切っただけ）。
        var ratio = fine.Cells.Count(c => c != FaceMaterial.Empty) / 8.0 / coarse.Cells.Count(c => c != FaceMaterial.Empty);
        Assert.IsTrue(ratio is > 0.85 and < 1.15, $"volume ratio {ratio}");
        foreach (var m in new[] { FaceMaterial.White, FaceMaterial.Lip, FaceMaterial.Iris })
        {
            Assert.IsGreaterThan(0, fine.Count(m), m.ToString());
            Assert.IsGreaterThan(0, coarse.Count(m), m.ToString());
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FaceComposer.Compose(spec, 0, 28, 20));
    }

    [TestMethod]
    public void Values_MoveTheFeatures()
    {
        var spec = FaceSpec.For("Gaston");
        // 目の大きさは部品の絵のまま（シンプルな顔: 白目 3〜4 x 2〜3）。鼻の幅の値は鼻を広げる。
        int Nose(FaceParts v) => FaceComposer.Compose(spec with { Values = v }, 20, 28, 20).Cells.Count(c => c != FaceMaterial.Empty);
        Assert.IsGreaterThan(Nose(FaceParts.Default with { NoseWidth = 0.7f }), Nose(FaceParts.Default with { NoseWidth = 1.5f }), "a wider nose, more cells");
        var narrow = FaceComposer.Compose(spec with { Values = FaceParts.Default with { FaceWidth = 0.9f } }, 40, 56, 40).Cells.Count(c => c != FaceMaterial.Empty);
        var wide = FaceComposer.Compose(spec with { Values = FaceParts.Default with { FaceWidth = 1.15f } }, 40, 56, 40).Cells.Count(c => c != FaceMaterial.Empty);
        Assert.IsGreaterThan(narrow, wide, "wider face, more cells");
        Assert.AreEqual(0, FaceComposer.Compose(spec, 20, 28, 20).Count(FaceMaterial.Freckle));
        Assert.IsGreaterThan(0, FaceComposer.Compose(spec with { Values = FaceParts.Default with { Freckles = 1f } }, 40, 56, 40).Count(FaceMaterial.Freckle));
    }

    [TestMethod]
    public void EveryRosterCharacter_HasAFaceThatComposes()
    {
        foreach (var character in MiniRing.Characters.CharacterRoster.All)
        {
            var volume = FaceComposer.Compose(character.Face, 20, 28, 20);
            Assert.IsGreaterThan(0, volume.Count(FaceMaterial.White), character.Id);
            Assert.IsGreaterThan(0, volume.Count(FaceMaterial.Lip), character.Id);
        }
    }

    /// <summary>調査用: FACE_DUMP_DIR に、組み立てた顔の格子を文字で書く（tools/face-parts/render.py が絵にする）。</summary>
    [TestMethod]
    [TestCategory("Probe")]
    public void Probe_DumpFaces()
    {
        var dir = Environment.GetEnvironmentVariable("FACE_DUMP_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            Assert.Inconclusive("FACE_DUMP_DIR を指定してください。");
        }

        Directory.CreateDirectory(dir);
        const string symbols = ".shbwilenft";
        void Dump(string name, FaceSpec spec)
        {
            var v = FaceComposer.Compose(spec, 20, 28, 20);
            var colors = FacePalette.Resolve(spec).ToDictionary(c => symbols[(int)c.Key].ToString(), c => $"#{(int)((c.Value.X * 255f) + 0.5f):X2}{(int)((c.Value.Y * 255f) + 0.5f):X2}{(int)((c.Value.Z * 255f) + 0.5f):X2}");
            File.WriteAllText(Path.Combine(dir, name + ".txt"), $"{v.Nx} {v.Ny} {v.Nz}\n{JsonSerializer.Serialize(colors)}\n" + new string([.. v.Cells.Select(c => symbols[(int)c])]));
        }

        foreach (var character in MiniRing.Characters.CharacterRoster.All)
        {
            Dump("roster_" + character.Id, character.Face);
        }

        foreach (var name in Names)
        {
            Dump(name, FaceSpec.For(name));
        }

        foreach (var category in new[] { "eyes", "brows", "mouth", "nose", "ears", "jaw", "hair", "beard", "browridge" })
        {
            for (var i = 1; i <= FacePartLibrary.Default.Parts(category).Count; i++)
            {
                Dump($"part_{category}_{i}", Spec((category, $"{category}.{i}", 1f)));
            }
        }
    }
}
