using System.Text.Json;
using System.Text.Json.Nodes;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>
/// E0（CLAUDE.md §1.8）: キャラクター 1 人 1 ファイル（体 ＋ 教科書 ＋ 教育 ＋ 育ちの記録）の読み込み・検証、
/// 教育の解決（教科書 → education.style → education.vs[相手]）、解決したスタイルが試合・意図操作・記録に通ること。
/// 教育が空なら今と同じ試合になること自体は <see cref="CharacterSamenessTests"/>。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class CharacterFileTests
{
    private static string Json(string id) => CharacterRosterTests.RosterFileJson(id);

    /// <summary>組み込みの 1 人ぶんのファイルの education を差し替えた JSON。</summary>
    private static string WithEducation(string id, string education) => CharacterRosterTests.WithEducation(Json(id), education);

    private static FighterCharacter Load(string id, string education)
        => CharacterRoster.FromFiles([(id + ".json", WithEducation(id, education))], CharacterRoster.All.Select(c => c.Id).ToList()).Single();

    [TestMethod]
    public void BuiltInRoster_IsOneFilePerCharacter_InTheDecidedOrder_WithOnlyTheGamePlanEducated()
    {
        var all = CharacterRoster.All;
        Assert.HasCount(18, all); // 2026-11-18: 柔術家 ren を足して 18 人
        Assert.HasCount(13, CharacterRoster.Countries.Keys.Where(k => all.Any(c => c.Country.Id == k)).ToList());
        foreach (var c in all)
        {
            // 2026-11-18: 教育は休みの動作の好み（BreakLikes）と戦略の層（GamePlan: 得意な間合いの表）だけ。ほかの値は教科書そのもの。
            Assert.AreEqual("BreakLikes,GamePlan", string.Join(",", c.Education.Style.Select(p => p.Key).Order()), c.Id);
            Assert.AreEqual(0, c.Education.Vs.Count, c.Id);
            Assert.IsNull(c.Education.WeaponBot, c.Id);
            Assert.IsEmpty(c.Record, c.Id);
            var textbook = BotCatalog.Style(c.StyleName);
            CharacterRosterTests.AssertTextbookPlusGamePlan(textbook, c.Resolve(), c.Id);
            CharacterRosterTests.AssertTextbookPlusGamePlan(textbook, c.Resolve("marcus"), c.Id);
            CharacterRosterTests.AssertTextbookPlusGamePlan(textbook, c.Style, c.Id);
            Assert.IsNotEmpty(c.Style.GamePlan!.Own, c.Id);
            Assert.IsEmpty(c.Style.GamePlan.Foe, "no opponent, no foe table: " + c.Id);
        }
    }

    // ---- 読み込みの検証 ----

    [TestMethod]
    public void Load_RejectsAFileNameThatIsNotTheId_ADuplicateId_AndAMissingOrder()
    {
        var jin = Json("jin");
        var wrongName = Assert.ThrowsExactly<InvalidDataException>(() => CharacterRoster.FromFiles([("someone.json", jin)]));
        Assert.Contains("someone.json", wrongName.Message);
        Assert.Contains("jin", wrongName.Message);

        var duplicate = Assert.ThrowsExactly<InvalidDataException>(() => CharacterRoster.FromFiles([("jin.json", jin), ("jin.json", jin)]));
        Assert.Contains("重複", duplicate.Message);

        var noOrder = jin.Replace("\"order\": 1,", string.Empty, StringComparison.Ordinal);
        Assert.AreNotEqual(jin, noOrder);
        Assert.Contains("order", Assert.ThrowsExactly<InvalidDataException>(() => CharacterRoster.FromFiles([("jin.json", noOrder)])).Message);
    }

    [TestMethod]
    [DataRow("{ \"style\": { \"Agression\": 0.5 }, \"vs\": {}, \"weaponBot\": null }", "education.style.Agression")]
    [DataRow("{ \"style\": { \"Aim\": { \"Chinn\": 2 } }, \"vs\": {}, \"weaponBot\": null }", "Chinn")]
    [DataRow("{ \"style\": { \"Name\": \"x\" }, \"vs\": {}, \"weaponBot\": null }", "education.style.Name")]
    [DataRow("{ \"style\": { \"Build\": { \"Height\": 2 } }, \"vs\": {}, \"weaponBot\": null }", "education.style.Build")]
    [DataRow("{ \"style\": { \"ComboWeights\": { \"no-such-combo\": 1 } }, \"vs\": {}, \"weaponBot\": null }", "no-such-combo")]
    [DataRow("{ \"style\": {}, \"vs\": { \"akira\": { \"Aggresion\": 1 } }, \"weaponBot\": null }", "education.vs.akira.Aggresion")]
    [DataRow("{ \"style\": {}, \"vs\": { \"nobody\": { \"Aggression\": 1 } }, \"weaponBot\": null }", "nobody")]
    [DataRow("{ \"style\": {}, \"vs\": {}, \"weaponBot\": { \"Anything\": 1 } }", "weaponBot")]
    public void Load_RejectsABrokenEducation_WithAMessageThatNamesTheKey(string education, string expected)
    {
        var error = Assert.ThrowsExactly<InvalidDataException>(() => Load("marcus", education));
        Assert.Contains(expected, error.Message);
        Assert.Contains("marcus", error.Message);
    }

    [TestMethod]
    public void Load_RejectsANullRecordEntry_AndAnUnknownFileKey_AndKeepsTheRecord()
    {
        var marcus = Json("marcus");
        Assert.ThrowsExactly<InvalidDataException>(() => CharacterRoster.FromFiles([("marcus.json", marcus.Replace("\"record\": []", "\"record\": [null]", StringComparison.Ordinal))]));
        Assert.ThrowsExactly<InvalidDataException>(() => CharacterRoster.FromFiles([("marcus.json", marcus.Replace("\"record\": []", "\"record\": [], \"power\": 1", StringComparison.Ordinal))]));
        var kept = marcus.Replace("\"record\": []", "\"record\": [ { \"date\": \"2026-11-17\", \"method\": \"cma-es\", \"adopted\": true } ]", StringComparison.Ordinal);
        var loaded = CharacterRoster.FromFiles([("marcus.json", kept)]).Single();
        Assert.HasCount(1, loaded.Record);
        Assert.AreEqual("cma-es", loaded.Record[0]["method"]!.GetValue<string>());
    }

    // ---- 解決: 重ね方 ----

    [TestMethod]
    public void Resolve_MergesObjectsByKey_ReplacesValues_AndKeepsTheTextbooksName()
    {
        var marcus = Load("marcus", "{ \"style\": { \"Aggression\": 0.3, \"Aim\": { \"Chin\": 5 }, \"ComboWeights\": { \"1-2\": 99 } }, \"vs\": {}, \"weaponBot\": null }");
        var textbook = BotCatalog.Champion;
        var resolved = marcus.Resolve();
        Assert.AreEqual(0.3f, resolved.Aggression, "a value is replaced");
        Assert.AreEqual(5f, resolved.Aim!.Chin, "the object is merged by key: Chin replaced");
        Assert.AreEqual(textbook.Aim!.Temple, resolved.Aim.Temple, "the other Aim keys stay from the textbook (recursion)");
        Assert.AreEqual(textbook.Aim.ExposedBrain, resolved.Aim.ExposedBrain);
        Assert.AreEqual(99f, resolved.ComboWeights["1-2"]);
        Assert.AreEqual(textbook.ComboWeights["1-2-3"], resolved.ComboWeights["1-2-3"], "the other combos stay");
        Assert.AreEqual(textbook.ComboWeights.Count, resolved.ComboWeights.Count);
        Assert.AreEqual(textbook.PunchSpeed, resolved.PunchSpeed, "keys that were not written are the textbook's");
        Assert.AreEqual("champion", resolved.Name, "the name stays the textbook's (karate etc. are decided by name)");
        Assert.AreEqual(1f, textbook.Aggression, "resolving never touches the shared textbook");
    }

    [TestMethod]
    public void Resolve_VsLayersOnTopOfStyle_ForThatOpponentOnly()
    {
        var marcus = Load("marcus", "{ \"style\": { \"Aggression\": 0.3, \"ComboWeights\": { \"1-2\": 99 } }, \"vs\": { \"akira\": { \"Aggression\": 0.9 } }, \"weaponBot\": null }");
        var general = marcus.Resolve("dmitri");
        var vsAkira = marcus.Resolve("akira");
        Assert.AreEqual(0.3f, general.Aggression);
        Assert.AreEqual(0.3f, marcus.Resolve().Aggression, "no opponent: the general education only");
        Assert.AreEqual(0.9f, vsAkira.Aggression, "vs wins over style");
        Assert.AreEqual(99f, vsAkira.ComboWeights["1-2"], "the general education still holds under vs");
        Assert.AreEqual("champion", vsAkira.Name);
    }

    // ---- 解決したスタイルが試合・意図操作・記録に通る ----

    private static (FighterCharacter Red, FighterCharacter Blue) Educated()
    {
        // GuardHeight: 構えの高さはいつも見える違い（手数 Aggression と RangePress は攻めの間にしか効かず、打ち合いの続く試合では、弱った相手を詰める層（LogicVersion 36）が間を縮めて差が出ないことがある）。
        var red = Load("marcus", "{ \"style\": { \"Aggression\": 0.2, \"RangePress\": 3, \"GuardHeight\": 1.4 }, \"vs\": { \"dmitri\": { \"Aggression\": 0.4 } }, \"weaponBot\": null }");
        var blue = Load("dmitri", "{ \"style\": { \"Aggression\": 0.6 }, \"vs\": {}, \"weaponBot\": null }");
        return (red, blue);
    }

    [TestMethod]
    public void Match_PassesTheResolvedStyle_ToBothBotsAndBothIntentControls()
    {
        var (red, blue) = Educated();
        var spectate = new MatchSelection(MatchMode.Spectate, red, blue).Corners;
        Assert.AreEqual(0.4f, spectate.RedBotStyle!.Aggression, "red: the vs dmitri layer");
        Assert.AreEqual(0.6f, spectate.CpuStyle!.Aggression, "blue: its general education");
        Assert.AreEqual(0.6f, BotStyle.FromJson(spectate.CpuStyleJson!).Aggression, "the recorded CPU style is the resolved whole");
        Assert.AreEqual("champion", spectate.PlayerName, "names stay the textbooks (IsKarate etc.)");
        Assert.AreEqual("slugger", spectate.CpuName);

        // 別の相手なら、赤は一般の教育だけ。
        var other = new MatchSelection(MatchMode.Spectate, red, CharacterRoster.Get("albert")).Corners;
        Assert.AreEqual(0.2f, other.RedBotStyle!.Aggression);

        // 人が操る側の意図の解釈（赤・2P の青）に同じ解決済みのスタイルが届く。
        var single = new MatchSelection(MatchMode.Single, red, blue).Corners;
        Assert.IsNull(single.RedBotStyle, "a human plays red");
        Assert.AreEqual(0.4f, single.PlayerStyle!.Aggression);
        Assert.AreEqual(0.4f, LocalVersusControl.RedControl(single).Style!.Aggression);
        var versus = new MatchSelection(MatchMode.Versus, red, blue).Corners;
        Assert.AreEqual(0.4f, LocalVersusControl.RedControl(versus).Style!.Aggression);
        Assert.AreEqual(0.6f, LocalVersusControl.BlueControl(versus).Style!.Aggression, "2P blue plays with its own resolved style");

        // 教育の無いキャラは教科書そのもの（今までの名前引きと同じ物）。
        var plain = new MatchSelection(MatchMode.Single, CharacterRoster.Get("marcus"), CharacterRoster.Get("dmitri")).Corners;
        CharacterRosterTests.AssertTextbookPlusGamePlan(BotCatalog.Champion, plain.PlayerStyle!, "marcus");
    }

    [TestMethod]
    public void Education_ChangesTheMatch_AndReplayKeepsTheRecordedStyleWhateverTheFilesSayLater()
    {
        var (red, blue) = Educated();
        // 途中の毎秒の状態で比べる（試合の終わりの 1 点だけでは、ダウンでラウンドが作り直されると両方が同じ初めの状態に戻り、差が見えないことがある: LogicVersion 36 で起きた）。
        var plainChecksum = RunChecksums(new MatchSelection(MatchMode.Spectate, CharacterRoster.Get("marcus"), CharacterRoster.Get("dmitri")).Corners, 8f);
        var educatedChecksum = RunChecksums(new MatchSelection(MatchMode.Spectate, red, blue).Corners, 8f);
        Assert.IsTrue(plainChecksum.Zip(educatedChecksum).Any(p => p.First != p.Second), "the education reaches the bots");

        foreach (var mode in new[] { MatchMode.Spectate, MatchMode.Single })
        {
            var corners = new MatchSelection(mode, red, blue, Referee: false).Corners;
            using var game = new BoxingGame(seed: 7, corners: corners);
            using var stream = new MemoryStream();
            using (var recorder = new ReplayRecorder(stream, game, "test"))
            {
                for (var i = 0; i < 2 * ReplayRecorder.ChecksumInterval; i++)
                {
                    recorder.Step(game, default, 1f / 60f);
                }
            }

            // 名簿のファイル（教育）が後で変わっても、記録の見出しの解決済みのスタイルで同じ試合になる（名簿は引かない）。
            var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
            var header = data.Header.Corners;
            Assert.AreEqual(0.6f, header.CpuStyle!.Aggression, mode.ToString());
            Assert.AreEqual(0.4f, header.PlayerStyle!.Aggression, mode.ToString());
            Assert.AreEqual(mode == MatchMode.Spectate ? 0.4f : (float?)null, header.RedBotStyle?.Aggression, mode.ToString());
            var result = ReplayPlayer.Verify(data);
            Assert.IsTrue(result.Reproduced, mode.ToString());
            Assert.AreEqual(2, result.ChecksumsMatched, mode.ToString());
        }
    }

    [TestMethod]
    public void OldRecordings_WithoutAPlayerStyle_StillRead_AndTheVersionWasRaised()
    {
        var version = ReplayFile.Version;
        Assert.AreEqual(13, version, "v13: steps carry the step command; v12: leg commands carry the knee control; v11: steps carry the hand Slide flags; v10: steps carry the elbow tuck; v9: matchSettings may carry PlayerStyleJson (v8) and the weapon-bot style JSONs (v9)");
        var corners = new MatchSelection(MatchMode.Single, CharacterRoster.Get("marcus"), CharacterRoster.Get("dmitri")).Corners;
        var options = new JsonSerializerOptions { IncludeFields = true };
        var node = JsonNode.Parse(JsonSerializer.Serialize(ReplayMatchSettings.From(corners, null), options))!.AsObject();
        Assert.IsNotNull(node["PlayerStyleJson"], "the new field is written");
        node.Remove("PlayerStyleJson");
        var old = JsonSerializer.Deserialize<ReplayMatchSettings>(node.ToJsonString(), options)!;
        Assert.IsNull(old.PlayerStyleJson, "a record without the field reads as null (the old way of deciding)");
        Assert.IsNull(old.Apply(corners).PlayerStyle);
    }

    private static List<ulong> RunChecksums(BoxingCorners corners, float seconds)
    {
        using var game = new BoxingGame(seed: 3, corners: corners);
        var sums = new List<ulong>();
        for (var i = 0; i < (int)(seconds * 60f); i++)
        {
            game.Step(default, 1f / 60f);
            if (i % 60 == 59)
            {
                sums.Add(game.StateChecksum());
            }
        }

        return sums;
    }

    private static ulong RunChecksum(BoxingCorners corners, float seconds)
    {
        using var game = new BoxingGame(seed: 3, corners: corners);
        for (var i = 0; i < (int)(seconds * 60f); i++)
        {
            game.Step(default, 1f / 60f);
        }

        return game.StateChecksum();
    }

    // ---- ファイルのパスから読む入口 ----

    [TestMethod]
    public void LoadFile_And_LoadDirectory_ReadAPlayersCharacter_AndRefuseBuiltInIds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "miniring_chars_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mine = CharacterRosterTests.WithEducation(Json("jin").Replace("\"id\": \"jin\"", "\"id\": \"newbie\"", StringComparison.Ordinal)
                .Replace("\"order\": 1", "\"order\": 100", StringComparison.Ordinal),
                "{ \"style\": { \"Aggression\": 0.5 }, \"vs\": { \"marcus\": { \"Aggression\": 0.8 } }, \"weaponBot\": null }");
            File.WriteAllText(Path.Combine(dir, "newbie.json"), mine);
            var one = CharacterRoster.LoadFile(Path.Combine(dir, "newbie.json"));
            Assert.AreEqual("newbie", one.Id);
            Assert.AreEqual(0.8f, one.Resolve("marcus").Aggression, "vs a built-in character is allowed");
            Assert.AreEqual(0.5f, one.Resolve("akira").Aggression);
            Assert.HasCount(1, CharacterRoster.LoadDirectory(dir));
            Assert.HasCount(18, CharacterRoster.All, "the built-in roster is untouched");

            File.WriteAllText(Path.Combine(dir, "jin.json"), Json("jin"));
            Assert.Contains("組み込み", Assert.ThrowsExactly<InvalidDataException>(() => CharacterRoster.LoadDirectory(dir)).Message);
            File.Delete(Path.Combine(dir, "jin.json"));

            File.WriteAllText(Path.Combine(dir, "wrong.json"), mine);
            Assert.ThrowsExactly<InvalidDataException>(() => CharacterRoster.LoadFile(Path.Combine(dir, "wrong.json")));
            Assert.ThrowsExactly<DirectoryNotFoundException>(() => CharacterRoster.LoadDirectory(Path.Combine(dir, "nothing")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
