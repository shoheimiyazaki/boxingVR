using System.Numerics;
using System.Text.Json.Nodes;
using Aigis.Physics;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// キャラメイク（<see cref="CharacterMaker"/>・<see cref="CharacterFile"/>・<see cref="BodySizing"/>）: 書いたファイルを名簿の読み込みで読み直すと同じキャラ、
/// 身長・体重から解いた体格は名簿の式で同じ身長・体重に戻る、物理の体の質量は試合の体の質量と同じ、ルーツは顔だけを変える。
/// </summary>
[TestClass]
public sealed class CharacterMakerTests
{
    private static IReadOnlyCollection<string> BuiltIn => [.. CharacterRoster.All.Select(c => c.Id)];

    private static FighterCharacter Reload(string id, string json)
        => CharacterRoster.FromFiles([($"{id}.json", json)], BuiltIn).Single();

    [TestMethod]
    public void WritingAnyRosterCharacter_AndReadingItBack_GivesTheSameCharacter()
    {
        foreach (var c in CharacterRoster.All)
        {
            var json = CharacterFile.Write(c);
            var back = Reload(c.Id, json);
            Assert.AreEqual(json, CharacterFile.Write(back), $"{c.Id}: 書いて読んで書いた物が同じ");
            Assert.AreEqual(c.Build, back.Build, c.Id);
            Assert.AreEqual(c.Order, back.Order, c.Id);
            Assert.AreEqual(c.Org, back.Org, c.Id);
            Assert.AreEqual(c.Country.Id, back.Country.Id, c.Id);
            Assert.AreEqual(c.Wear, back.Wear, c.Id);
            Assert.AreEqual(c.Weapon, back.Weapon, c.Id);
            Assert.AreEqual(c.Look with { Parts = c.Look.Parts }, back.Look with { Parts = c.Look.Parts }, c.Id);
            Assert.AreEqual(c.Look.Parts, back.Look.Parts, c.Id);
            Assert.AreEqual(c.Face.Values, back.Face.Values, c.Id);
            Assert.AreEqual(c.Face.Skin, back.Face.Skin, c.Id);
            Assert.IsTrue(JsonNode.DeepEquals(CharacterFile.Face(c.Face), CharacterFile.Face(back.Face)), c.Id);
            Assert.AreEqual(c.Style.ToJson(), back.Style.ToJson(), $"{c.Id}: 教育を重ねたスタイルが同じ");
            Assert.AreEqual(c.Profile?.Briefing(c.Name, c.HudName, c.Tagline), back.Profile?.Briefing(c.Name, c.HudName, c.Tagline), c.Id);
        }
    }

    [TestMethod]
    public void ADraft_BecomesAFile_ThatReadsBackToTheSameDraft()
    {
        var draft = CharacterMaker.NewDraft("athletic", "africa", heightCm: 183) with
        {
            Id = "maker-test", Name = "テスト・ボクサー", Country = "usa", Textbook = "swarmer", Wear = "none", Org = "vfc",
        };
        var (character, json) = CharacterMaker.ToFile(draft);
        Assert.AreEqual(183, CharacterProfile.HeightOf(character.Build));
        Assert.AreEqual(draft.WeightKg, CharacterProfile.WeightOf(character.Build));
        Assert.AreEqual("swarmer", character.StyleName);
        Assert.AreEqual(Weapons.HandWear.None, character.Wear);
        Assert.AreEqual(CharacterRoster.All.Max(c => c.Order) + 1, character.Order, "順番を書かなければ名簿の最後の次");
        Assert.IsTrue(character.Education.IsEmptyFor(null), "教育は空（教科書そのもの）");

        var again = CharacterMaker.FromCharacter(character);
        Assert.AreEqual(json, CharacterMaker.ToFile(again).Json, "ファイル → 下書き → ファイルが同じ");
        Assert.AreEqual(draft.Face.Skin[0], again.Face.Skin[0], 1e-6f);
    }

    [TestMethod]
    public void ARosterCharacter_ThroughTheDraft_KeepsItsEducationRecordBodyAndFace()
    {
        foreach (var c in CharacterRoster.All)
        {
            var (back, json) = CharacterMaker.ToFile(CharacterMaker.FromCharacter(c));
            Assert.AreEqual(c.Style.ToJson(), back.Style.ToJson(), $"{c.Id}: 育てた教育が残る");
            Assert.AreEqual(c.WeaponStyle?.ToJson(), back.WeaponStyle?.ToJson(), c.Id);
            Assert.AreEqual(c.Record.Count, back.Record.Count, c.Id);
            Assert.AreEqual(c.Build, back.Build, c.Id);
            Assert.AreEqual(c.Order, back.Order, c.Id);
            Assert.IsTrue(JsonNode.DeepEquals(CharacterFile.Face(c.Face), CharacterFile.Face(back.Face)), c.Id);
            var again = CharacterMaker.ToFile(CharacterMaker.FromCharacter(back)).Json;
            var diff = json.Split('\n').Zip(again.Split('\n')).FirstOrDefault(p => p.First != p.Second);
            Assert.AreEqual(json, again, $"{c.Id}: {diff.First} → {diff.Second}");
        }
    }

    [TestMethod]
    public void TextThatLooksLikeANumberArray_IsWrittenAsItIs()
    {
        var c = CharacterRoster.Get("akira") with { Tagline = "[12,3] の男" };
        Assert.AreEqual("[12,3] の男", Reload(c.Id, CharacterFile.Write(c)).Tagline);
    }

    [TestMethod]
    public void TheSolvedBuild_GivesBackTheAskedHeightAndWeight_AndWeightGrowsOnlyTheMuscle()
    {
        foreach (var height in new[] { 150, 163, 175, 188, 201 })
        {
            var (lo, hi) = BodySizing.WeightRange(height);
            var previous = 0f;
            for (var weight = lo; weight <= hi; weight += 3)
            {
                var build = BodySizing.Solve(height, weight, arms: 1.05f, legs: 0.97f);
                Assert.AreEqual(height, CharacterProfile.HeightOf(build), $"{height} cm {weight} kg");
                Assert.AreEqual(weight, CharacterProfile.WeightOf(build), $"{height} cm {weight} kg");
                Assert.AreEqual(1.05f, build.Arms, 1e-6f);
                Assert.AreEqual(0.97f, build.Legs, 1e-6f);
                Assert.IsGreaterThan(previous, build.Muscle, "重いほど太い");
                previous = build.Muscle;
            }

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BodySizing.Solve(height, hi + 2));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BodySizing.Solve(height, lo - 2));
        }

        Assert.AreEqual(BodyBuild.Standard, BodySizing.Solve(175, 72), "基準の身長・体重は基準の体");
    }

    [TestMethod]
    public void ThePhysicalMass_IsTheMatchBodysMass_AndGrowsWithTheVolume()
    {
        foreach (var build in new[] { BodyBuild.Standard, BodySizing.Solve(190, 100, 1.08f, 1.02f), BodySizing.Solve(160, 55) })
        {
            using var world = new PhysicsWorld();
            var fighter = Fighter.Spawn(world, FighterTuning.Default with { Build = build }, "f", Vector3.Zero, 0f);
            Assert.AreEqual(fighter.TotalMass, BodySizing.PhysicalMassKg(build), fighter.TotalMass * 1e-4f, $"{build}");
        }

        var standard = BodySizing.PhysicalMassKg(BodyBuild.Standard);
        Assert.IsGreaterThan(standard, BodySizing.PhysicalMassKg(BodyBuild.Standard with { Muscle = 1.1f }), "太いほど重い");
        Assert.IsGreaterThan(standard, BodySizing.PhysicalMassKg(BodyBuild.Standard with { Height = 1.1f }), "高いほど重い");
        Assert.IsGreaterThan(standard, BodySizing.PhysicalMassKg(BodyBuild.Standard with { Arms = 1.1f }), "腕が長いほど重い（体積に比例）");
    }

    [TestMethod]
    public void TheMakerTable_Loads_AndRootsChangeOnlyTheFace()
    {
        Assert.IsNotEmpty(CharacterMaker.BodyTypes);
        Assert.IsNotEmpty(CharacterMaker.Roots);
        var a = CharacterMaker.NewDraft("standard", CharacterMaker.Roots[0].Id);
        foreach (var root in CharacterMaker.Roots)
        {
            for (var s = 0; s < root.Skins.Count; s++)
            {
                _ = root.Face(skin: s); // 検証を通る
            }

            var b = CharacterMaker.NewDraft("standard", root.Id);
            Assert.AreEqual((a.HeightCm, a.WeightKg, a.Arms, a.Legs), (b.HeightCm, b.WeightKg, b.Arms, b.Legs), $"{root.Id}: ルーツは体格を変えない");
            Assert.IsTrue(root.Face().Parts["eyes"].Count == 1 && root.Face().Parts["brows"].Count <= 1, $"{root.Id}: 目と眉は 1 部品（名簿の決まり）");
        }

        foreach (var type in CharacterMaker.BodyTypes)
        {
            var d = CharacterMaker.NewDraft(type.Id);
            Assert.AreEqual(type.Muscle, BodySizing.Solve(d.HeightCm, d.WeightKg).Muscle, 0.01f, $"{type.Id}: 体重は体型の太さから");
        }
    }

    [TestMethod]
    public void ABrokenDraft_IsRefused_WithTheFieldNamed()
    {
        var ok = CharacterMaker.NewDraft() with { Id = "maker-test", Name = "テスト", Country = "japan" };
        Assert.Contains("name", Assert.ThrowsExactly<InvalidDataException>(() => CharacterMaker.ToFile(ok with { Name = " " })).Message);
        Assert.Contains("国", Assert.ThrowsExactly<InvalidDataException>(() => CharacterMaker.ToFile(ok with { Country = "atlantis" })).Message);
        Assert.Contains("kg", Assert.ThrowsExactly<InvalidDataException>(() => CharacterMaker.ToFile(ok with { WeightKg = 300 })).Message);
        Assert.Contains("id", Assert.ThrowsExactly<InvalidDataException>(() => CharacterMaker.ToFile(ok with { Id = "Bad Id" })).Message);
        Assert.Contains("武器", Assert.ThrowsExactly<InvalidDataException>(() => CharacterMaker.ToFile(ok with { Weapon = "laser" })).Message);
        Assert.Contains("教科書", Assert.ThrowsExactly<InvalidDataException>(() => CharacterMaker.ToFile(ok with { Textbook = "kungfu" })).Message);
        _ = CharacterMaker.ToFile(ok with { Weapon = "katana" });
    }
}
