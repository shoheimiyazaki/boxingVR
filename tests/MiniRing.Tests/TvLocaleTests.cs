using System.Text.Json.Nodes;
using MiniRing.Arena;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 画面の言葉の言語（<see cref="DisplayLanguage"/>・<see cref="LocaleOverlay"/>）: データの <c>locales.en</c> が本体へ深く重なること、
/// 名簿の全員と全団体に英語があること、日本語が既定で今までと変わらないこと。全体の言語は触らず、言語を引数で渡して読む（並列のテストと干渉しない）。
/// </summary>
[TestClass]
public sealed class TvLocaleTests
{
    private const string Sample = """
        {
          // コメントと末尾のカンマも読める
          "name": "ジャパン",
          "hudName": "ジ",
          "profile": { "age": 30, "stance": "オーソドックス", "tags": ["a", "b"], },
          "locales": {
            "en": { "name": "Japan", "profile": { "stance": "Orthodox", "tags": ["x"] } }
          }
        }
        """;

    [TestMethod]
    public void EnglishOverlayOverridesAndKeepsTheRest()
    {
        var en = JsonNode.Parse(LocaleOverlay.Apply(Sample, "en"))!;
        Assert.AreEqual("Japan", (string?)en["name"]);
        Assert.AreEqual("ジ", (string?)en["hudName"], "書いていない欄は本体のまま");
        Assert.AreEqual(30, (int?)en["profile"]!["age"], "入れ子のオブジェクトは再帰で重なる（書いていない欄が残る）");
        Assert.AreEqual("Orthodox", (string?)en["profile"]!["stance"]);
        Assert.AreEqual(1, en["profile"]!["tags"]!.AsArray().Count, "配列は置き換え");
        Assert.IsNull(en["locales"], "locales は結果に残さない（本体の型は知らない欄を断る）");
    }

    [TestMethod]
    public void JapaneseIsTheBodyUnchanged()
    {
        var ja = JsonNode.Parse(LocaleOverlay.Apply(Sample, "ja"))!;
        Assert.AreEqual("ジャパン", (string?)ja["name"]);
        Assert.AreEqual("オーソドックス", (string?)ja["profile"]!["stance"]);
        Assert.AreEqual(2, ja["profile"]!["tags"]!.AsArray().Count);
        Assert.IsNull(ja["locales"]);
    }

    [TestMethod]
    public void UnknownLanguageIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => LocaleOverlay.Apply(Sample, "fr"));
        Assert.ThrowsExactly<ArgumentException>(() => DisplayLanguage.Validate("xx"));
    }

    [TestMethod]
    public void JapaneseIsTheDefaultLanguageAndTheRosterIsUnchanged()
    {
        Assert.AreEqual(DisplayLanguage.Japanese, DisplayLanguage.Current, "言語の既定は日本語（--lang を渡さない限り）");
        var marcus = CharacterRoster.ForLanguage("ja").Single(c => c.Id == "marcus");
        Assert.AreEqual("ザ・ハンマー", marcus.HudName);
        Assert.AreEqual("オーソドックス", marcus.Profile!.Stance);
        Assert.AreEqual("立花", Organizations.ForLanguage("ja")["wvc"].Cast.Announcer.Name);
        Assert.AreEqual("無差別級", Organizations.ForLanguage("ja")["wvc"].Divisions[0].Name);
    }

    [TestMethod]
    public void EveryRosterCharacterHasAnEnglishLocale()
    {
        var ja = CharacterRoster.ForLanguage("ja");
        var en = CharacterRoster.ForLanguage("en");
        Assert.AreEqual(ja.Count, en.Count);
        foreach (var c in en)
        {
            var j = ja.Single(x => x.Id == c.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(c.Name), $"{c.Id}: name");
            Assert.IsFalse(string.IsNullOrWhiteSpace(c.HudName), $"{c.Id}: hudName");
            Assert.AreNotEqual(j.Name, c.Name, $"{c.Id}: name が英語になっていません");
            Assert.AreNotEqual(j.HudName, c.HudName, $"{c.Id}: hudName が英語になっていません");
            Assert.IsTrue(c.Name.All(ch => ch < 0x3000), $"{c.Id}: name に日本語が残っています（{c.Name}）");
            Assert.IsTrue(c.HudName.All(ch => ch < 0x3000), $"{c.Id}: hudName に日本語が残っています");
            Assert.IsTrue(c.Origin.All(ch => ch < 0x3000), $"{c.Id}: origin");
            Assert.IsTrue(c.Tagline.All(ch => ch < 0x3000), $"{c.Id}: tagline");
            var p = c.Profile!;
            foreach (var (field, value) in new[] { ("birthplace", p.Birthplace), ("hometown", p.Hometown), ("stance", p.Stance), ("quote", p.Quote) })
            {
                Assert.IsTrue(value.Length > 0 && value.All(ch => ch < 0x3000), $"{c.Id}: profile.{field}（{value}）");
            }

            // 体・数字は言語で変わらない（強さは体から: CLAUDE.md §1.7）。
            Assert.AreEqual(j.Profile!.HeightCm, p.HeightCm);
            Assert.AreEqual(j.Profile.FightRecord, p.FightRecord);
            Assert.AreEqual(j.StyleName, c.StyleName);
            Assert.AreEqual(j.Org, c.Org);
        }
    }

    [TestMethod]
    public void EveryOrganizationHasAnEnglishLocale()
    {
        var ja = Organizations.ForLanguage("ja");
        var en = Organizations.ForLanguage("en");
        CollectionAssert.AreEquivalent(ja.Keys.ToArray(), en.Keys.ToArray());
        foreach (var (id, org) in en)
        {
            var j = ja[id];
            foreach (var seat in new[] { org.Cast.Announcer, org.Cast.Commentator })
            {
                Assert.IsTrue(seat.Name.Length > 0 && seat.Name.All(ch => ch < 0x3000), $"{id}: cast の名前（{seat.Name}）");
                Assert.AreEqual("edge", seat.Voice.Engine, $"{id}: 英語の声は Edge");
                StringAssert.EndsWith(seat.Voice.Voice[0], "Neural");
            }

            Assert.AreNotEqual(j.Cast.Announcer.Name, org.Cast.Announcer.Name);
            Assert.AreEqual(j.Divisions.Count, org.Divisions.Count, $"{id}: 階級の数は言語で変わらない");
            foreach (var (d, jd) in org.Divisions.Zip(j.Divisions))
            {
                Assert.IsTrue(d.Name.Length > 0 && d.Name.All(ch => ch < 0x3000), $"{id}: 階級 {d.Id}（{d.Name}）");
                Assert.AreEqual(jd.Id, d.Id);
                Assert.AreEqual(jd.LimitKg, d.LimitKg, "体重の上限は言語で変わらない");
            }

            foreach (var (name, value) in new[] { ("openingRule", org.Text.OpeningRule), ("decisionLine", org.Text.DecisionLine), ("belt", org.Titles.Belt), ("titleBout", org.Titles.TitleBout), ("winLine", org.Draft.WinLine) })
            {
                Assert.IsTrue(value.All(ch => ch < 0x3000), $"{id}: {name} に日本語が残っています（{value}）");
            }

            Assert.AreEqual(j.Match, org.Match, $"{id}: 試合の決まりは言語で変わらない");
            CollectionAssert.AreEqual(j.RulesSheet.ToArray(), org.RulesSheet.ToArray(), "台本の資料は日本語のまま");
        }
    }

    [TestMethod]
    public void EnglishResultWordsAreMappedAndJapaneseIsLeftAlone()
    {
        // 日本語の値を英語の言語へ引く表は、知らない言葉をそのまま返す（言語を切り替えずに確かめられる範囲: 戦績の言い方）。
        var record = new FightRecord { Wins = 38, Losses = 2, Draws = 0, Kos = 33 };
        Assert.AreEqual("38勝2敗（33KO）", TvLocale.Record(record), "日本語の既定の戦績の言い方は今までと同じ");
    }

    [TestMethod]
    public void ReplayCaption_TurnsTheJapaneseSceneIntoEnglish_AndLeavesJapaneseAlone()
    {
        const string scene = "Vikramの重い右がAlbertのみぞおちに → 右上腕骨折 → ダウン";

        Assert.AreEqual("Vikram's right to Albert's solar plexus → FRACTURE → DOWN", MiniRing.Arena.TvLocale.ReplayCaption(scene, english: true));
        Assert.AreEqual(scene, MiniRing.Arena.TvLocale.ReplayCaption(scene, english: false));
        Assert.AreEqual("Konrad's weapon to Sean's elbow → SEVERED → K.O.", MiniRing.Arena.TvLocale.ReplayCaption("Konradのロングソードが Seanの肘に → 左上腕切断 → K.O.（心臓の破壊）".Replace("が Sean", "がSean"), english: true));
        Assert.AreEqual("Dmitri → DOWN", MiniRing.Arena.TvLocale.ReplayCaption("Dmitri ダウン", english: true), "打撃の無いダウンは名前と印だけ");
    }
}
