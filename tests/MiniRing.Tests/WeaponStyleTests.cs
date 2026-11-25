using System.Text.Json;
using System.Text.Json.Nodes;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Motion;
using MiniRing.Replay;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 武器の教科書（<see cref="WeaponStyle"/>・<c>Bots/weapons/*.json</c>）と、キャラの教育（<c>education.weaponBot</c>）。
/// 値を JSON へ移しただけの段階で試合が同じことは <see cref="CharacterSamenessTests"/>（チェックサム）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class WeaponStyleTests
{
    private static readonly WeaponKind[] Kinds = [WeaponKind.Katana, WeaponKind.Longsword, WeaponKind.WoodenClub];

    private static JsonObject TextbookNode() => JsonNode.Parse(WeaponCatalog.Textbook(WeaponKind.Katana).ToJson())!.AsObject();

    private static JsonObject Edu(string json) => JsonNode.Parse(json)!.AsObject();

    [TestMethod]
    public void Json_RoundTripsEveryValue_IncludingTheOptionalSections()
    {
        var style = new WeaponStyle
        {
            Name = "custom",
            Defense = SwordDefense.Parrying,
            HitAndAway = new HitAndAway(Margin: 0.2817f, Patience: 3.357f),
            Aim = new AimTuning(MaxShift: 0.9f),
            AutoAim = true,
            Clearance = SelfClearance.Adopted,
            Pacing = new SwingPacing(Floor: 0.3f),
            Rest = 0.6714f,
            MinReach = 1.127f,
            InnerMinReach = 0.5634f,
            KeepReach = 0.5f,
            ClubRange = 1.127f,
            ClubRest = 0.8392f,
            ClubStandOff = 1.690f,
            ClubExact = true,
        };
        Assert.AreEqual(style, WeaponStyle.FromJson(style.ToJson()));
        var textbook = WeaponCatalog.Textbook(WeaponKind.Katana);
        // 技の重みは辞書（参照の比較になる）なので、JSON を通して書き戻した文字列で比べる。
        Assert.AreEqual(textbook.ToJson(), WeaponStyle.FromJson(textbook.ToJson()).ToJson());
    }

    [TestMethod]
    public void FromJson_RefusesUnknownKeysAndOutOfRangeValues_SayingWhich()
    {
        var typo = TextbookNode();
        typo["Rst"] = 0.5;
        Assert.ThrowsExactly<JsonException>(() => WeaponStyle.FromJson(typo.ToJsonString()));
        var nested = TextbookNode();
        nested["Defense"]!["Blokshare"] = 0.5;
        Assert.ThrowsExactly<JsonException>(() => WeaponStyle.FromJson(nested.ToJsonString()));

        foreach (var (path, value) in new (string, double)[] { ("Rest", -1), ("MinReach", 9), ("ClubRange", 0), ("KeepReach", 1e30) })
        {
            var broken = TextbookNode();
            broken[path] = value;
            var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WeaponStyle.FromJson(broken.ToJsonString()), path);
            Assert.Contains(path, error.Message);
        }

        var badDefense = TextbookNode();
        badDefense["Defense"]!["BlockShare"] = 1.5;
        var defenseError = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WeaponStyle.FromJson(badDefense.ToJsonString()));
        Assert.Contains("Defense.BlockShare", defenseError.Message);
        var badMode = TextbookNode();
        badMode["Defense"]!["Mode"] = 99;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WeaponStyle.FromJson(badMode.ToJsonString()));
        var badHit = TextbookNode();
        badHit["HitAndAway"] = new JsonObject { ["Patience"] = 0 };
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WeaponStyle.FromJson(badHit.ToJsonString()));
    }

    [TestMethod]
    public void Education_OverlaysTheTextbookByKey_AndAnEmptyOneIsTheTextbookItself()
    {
        var katana = WeaponCatalog.Textbook(WeaponKind.Katana);
        Assert.AreSame(katana, WeaponEducation.Resolve(WeaponKind.Katana, null));
        Assert.AreSame(katana, WeaponEducation.Resolve(WeaponKind.Katana, new JsonObject()));

        var educated = WeaponEducation.Resolve(WeaponKind.Katana, Edu("{ \"Rest\": 0.5035, \"Defense\": { \"Mode\": 3 }, \"HitAndAway\": { \"Patience\": 3.357 } }"))!;
        Assert.AreEqual(0.5035f, educated.Rest);
        Assert.AreEqual(DefenseMode.ParryAndStep, educated.Defense.Mode);
        Assert.AreEqual(katana.Defense.Margin, educated.Defense.Margin, "keys not written stay the textbook's");
        Assert.AreEqual(3.357f, educated.HitAndAway!.Patience);
        Assert.AreEqual("katana", educated.Name);
        Assert.AreEqual(1.007f, WeaponCatalog.Textbook(WeaponKind.Katana).Rest, "the textbook itself is not changed");
    }

    [TestMethod]
    public void Education_IsRefusedWhenBrokenOrWhenThereIsNoWeaponBot()
    {
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.Katana, Edu("{ \"Rst\": 0.3 }")));
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.Katana, Edu("{ \"Name\": \"x\" }")));
        var range = Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.Katana, Edu("{ \"Rest\": 50 }")));
        Assert.Contains("Rest", range.Message);
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.Katana, Edu("{ \"Defense\": { \"Blokshare\": 0.3 } }")));
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(null, Edu("{ \"Rest\": 0.3 }")));
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.Glove, Edu("{ \"Rest\": 0.3 }")));
        Assert.IsNull(WeaponEducation.Resolve(null, null));
        Assert.IsNull(WeaponEducation.Resolve(WeaponKind.Glove, new JsonObject()));
    }

    private static FighterCharacter WithWeaponBot(string id, string weaponBot)
    {
        var replaced = CharacterRosterTests.WithEducation(CharacterRosterTests.RosterFileJson(id), "{ \"style\": {}, \"vs\": {}, \"weaponBot\": " + weaponBot + " }");
        return CharacterRoster.FromFiles([(id + ".json", replaced)], CharacterRoster.All.Select(c => c.Id).ToList()).Single();
    }

    [TestMethod]
    public void CharacterFile_WeaponBotEducation_IsValidatedWhenTheFileIsLoaded()
    {
        var jin = WithWeaponBot("jin", "{ \"Rest\": 0.6714, \"ClubRange\": 1.408 }");
        Assert.AreEqual(0.6714f, jin.WeaponStyle!.Rest);
        Assert.AreEqual(1.408f, jin.WeaponStyle.ClubRange, "the club values are carried even when unused");
        Assert.AreSame(WeaponCatalog.Textbook(WeaponKind.Katana), WithWeaponBot("jin", "{}").WeaponStyle, "an empty education plays the textbook itself");
        // 2026-11-26: 迅・コンラートの教育は 1 振りの型の経路（Forms）と、コンラートは狙いの切り替え（AutoAim）だけ。
        CharacterRosterTests.AssertWeaponTextbookPlusGround(WeaponCatalog.Textbook(WeaponKind.Katana), CharacterRoster.Get("jin").WeaponStyle, "jin");
        CharacterRosterTests.AssertWeaponTextbookPlusGround(WeaponCatalog.Textbook(WeaponKind.Longsword), CharacterRoster.Get("konrad").WeaponStyle, "konrad");
        // 2026-11-24: ショーンの教育はとどめの経路（weaponBot.Ground）だけを持つ。
        CharacterRosterTests.AssertWeaponTextbookPlusGround(WeaponCatalog.Textbook(WeaponKind.WoodenClub), CharacterRoster.Get("sean").WeaponStyle, "sean");
        Assert.IsNull(CharacterRoster.Get("marcus").WeaponStyle, "fists have no weapon bot");

        Assert.Throws<InvalidDataException>(() => WithWeaponBot("jin", "{ \"Rst\": 0.4 }"));
        Assert.Throws<InvalidDataException>(() => WithWeaponBot("jin", "{ \"Rest\": 99 }"));
        var fists = Assert.Throws<InvalidDataException>(() => WithWeaponBot("marcus", "{ \"Rest\": 0.4 }"));
        Assert.Contains("weaponBot", fists.Message);
    }

    [TestMethod]
    public void EducatedWeaponStyle_ReachesTheBots_ChangesTheMatch_AndReplayKeepsTheRecordedValues()
    {
        var jin = WithWeaponBot("jin", "{ \"Rest\": 0.3357, \"HitAndAway\": { } }");
        var konrad = CharacterRoster.Get("konrad");
        var plain = new MatchSelection(MatchMode.Spectate, CharacterRoster.Get("jin"), konrad, Referee: true).Corners;
        var educated = new MatchSelection(MatchMode.Spectate, jin, konrad, Referee: true).Corners;
        Assert.AreEqual(0.3357f, educated.PlayerWeaponStyle!.Rest);
        Assert.AreEqual(1.007f, educated.CpuWeaponStyle!.Rest, "blue plays its own textbook");

        using (var probe = new BoxingGame(seed: 5, corners: educated))
        {
            probe.Step(default, 1f / 60f);
            Assert.AreEqual(0.3357f, ((SwordsmanBot)((DisarmedFallback)probe.PlayerControl!).Armed).Rest, "the educated value is what the bot runs with");
            Assert.AreEqual(1.007f, ((SwordsmanBot)((DisarmedFallback)probe.CpuControl!).Armed).Rest);
        }

        Assert.AreNotEqual(Run(plain, 20.14f), Run(educated, 20.14f), "the education reaches the match");
        using var game = new BoxingGame(seed: 5, corners: educated);

        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test"))
        {
            for (var i = 0; i < 2 * ReplayRecorder.ChecksumInterval; i++)
            {
                recorder.Step(game, default, 1f / 60f);
            }
        }

        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        Assert.AreEqual(0.3357f, data.Header.Corners.PlayerWeaponStyle!.Rest, "the header carries the resolved style");
        var result = ReplayPlayer.Verify(data);
        Assert.IsTrue(result.Reproduced);
        Assert.AreEqual(2, result.ChecksumsMatched);
    }

    [TestMethod]
    public void ClubBot_FromStyle_UsesTheStylesValues()
    {
        var plain = ClubBot.FromStyle(WeaponCatalog.Textbook(WeaponKind.WoodenClub), 1);
        Assert.AreEqual(1.014f, plain.Range);
        Assert.AreEqual(1.007f, plain.Rest);
        Assert.AreEqual(1.352f, plain.StandOff);
        var custom = ClubBot.FromStyle(WeaponCatalog.Textbook(WeaponKind.WoodenClub) with { ClubRange = 1.127f, ClubRest = 0.5035f, ClubStandOff = 1.972f }, 1);
        Assert.AreEqual(1.127f, custom.Range);
        Assert.AreEqual(0.5035f, custom.Rest);
        Assert.AreEqual(1.972f, custom.StandOff);
    }

    [TestMethod]
    public void Weights_AreValidated_AndTechniqueNamesMustExistInTheWeaponsForms()
    {
        var ok = WeaponEducation.Resolve(WeaponKind.Katana, Edu("{ \"TechniqueWeights\": { \"kiriage\": 0.2, \"tsuki\": 0 } }"))!;
        Assert.AreEqual(0.2f, ok.TechniqueWeights!["kiriage"]);
        Assert.AreEqual(ok, WeaponStyle.FromJson(ok.ToJson()) with { TechniqueWeights = ok.TechniqueWeights }, "the rest of the style survives a JSON round trip");
        var typo = Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.Katana, Edu("{ \"TechniqueWeights\": { \"kiriagee\": 0.2 } }")));
        Assert.Contains("kiriagee", typo.Message);
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.Longsword, Edu("{ \"TechniqueWeights\": { \"kiriage\": 0.2 } }")), "katana's form is not a longsword's");
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.Katana, Edu("{ \"TechniqueWeights\": { \"kesa\": -1 } }")));
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.WoodenClub, Edu("{ \"ClubSwingWeights\": { \"Slash\": 1 } }")));
        Assert.Throws<InvalidDataException>(() => WeaponEducation.Resolve(WeaponKind.WoodenClub, Edu("{ \"ClubSwingWeights\": { \"Chop\": 0, \"Side\": 0, \"Thrust\": 0 } }")), "nothing to swing");
        Assert.AreEqual(0f, WeaponEducation.Resolve(WeaponKind.WoodenClub, Edu("{ \"ClubSwingWeights\": { \"Chop\": 0 } }"))!.ClubSwingWeights!["Chop"]);
    }

    [TestMethod]
    public void TechniqueWeights_DecideWhichFormsTheSwordsmanThrows()
    {
        var only = WeaponCatalog.Textbook(WeaponKind.Katana) with
        {
            TechniqueWeights = FormSwordsman.Forms(WeaponKind.Katana).ToDictionary(f => f.Name, f => f.Name == "kesa" ? 1f : 0f),
        };
        var corners = BoxingCorners.Default with
        {
            // 攻めてこない受け手（2026-11-18 のマージで替えた）: チャンピオンは構えた刀の内側（胸と胸 0.25〜0.3 m）まで詰め、
            // 刀を振る前に構えた刃で首を断たれて 6 秒で死んだ（種 3）。このテストは重みで出す技が決まるかを見るので、試合の流れに頼らない。
            // 1 つだけ重みを付ける技は袈裟（kesa）。突き（tsuki）だけにすると、打って離れる歩きが突きの間合いに合わず一度も突かない
            // （剣のブランチ単体でも同じ。前はチャンピオンが間合いへ入ってきたので突けていた。docs/DEBT.md「剣士のボットの蹴りと内側の技の近道」）。
            RedBotStyle = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f },
            PlayerWeapon = WeaponKind.Katana,
            PlayerTwoHanded = true,
            UseWeaponBots = true,
            PlayerWeaponStyle = only,
        };
        using var game = new BoxingGame(seed: 3, corners: corners);
        var used = new HashSet<string>();
        // 2026-11-18（PhysicsVersion 45）: 刀の最初の 1 振りまでの待ち（ボクサーの攻めを避けて間合いに入る立ち回り）が、版 43 の約 15 秒から約 50 秒に延びた
        // （測った: 40 秒の間、両者とも立ったまま・間合い 0.2〜0.4 m を保ち、振り手の意識も 0.78 以上。倒れて振れなかったのではなく、振り始める時機が遅れただけ。
        // 立ち回りの軌道が体の運びの変更（相手や自分に渡る運動量）で変わった揺らぎで、判断の部分は変えていない）。
        // 「どの型を投げるか」を見るテストなので、最初の 1 振りを待ち（最大 201 秒。人形の単位で 120 秒の窓）、その後 34 秒の間に出た型を数える。
        int? firstUsed = null;
        for (var i = 0; i < 12084 && !game.Player.IsDead && !game.Cpu.IsDead; i++)
        {
            game.Step(default, 1f / 60f);
            if (((DisarmedFallback)game.PlayerControl!).Armed is SwordsmanBot bot)
            {
                used.UnionWith(bot.Used);
            }

            if (used.Count > 0)
            {
                firstUsed ??= i;
                if (i - firstUsed > 2014)
                {
                    break;
                }
            }
        }

        Assert.IsNotEmpty(used, "it swings");
        Assert.HasCount(1, used, "only the form with weight is thrown");
        Assert.Contains("kesa", used);
    }

    [TestMethod]
    public void ClubSwingWeights_DecideWhichSwingsTheClubThrows()
    {
        var thrustOnly = WeaponCatalog.Textbook(WeaponKind.WoodenClub) with
        {
            ClubSwingWeights = new Dictionary<string, float> { ["Chop"] = 0f, ["Side"] = 0f, ["Thrust"] = 1f },
        };
        ClubBot? bot = null;
        using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("stand", _ => new StandGuard(), s => bot = ClubBot.FromStyle(thrustOnly, s), PlayerWeapon: WeaponKind.WoodenClub));
        var families = new HashSet<SwingFamily>();
        for (var i = 0; i < 2014; i++)
        {
            game.Step(default, 1f / 60f);
            if (bot?.Current is { } swing)
            {
                families.Add(swing.Family);
            }
        }

        Assert.HasCount(1, families, "only the swing with weight is thrown");
        Assert.Contains(SwingFamily.Thrust, families);
        Assert.ThrowsExactly<ArgumentException>(() => new ClubBot([WeaponSwing.Default(SwingFamily.Chop)], 1, weights: [0f]));
        Assert.ThrowsExactly<ArgumentException>(() => new ClubBot([WeaponSwing.Default(SwingFamily.Chop)], 1, weights: [1f, 1f]));
    }

    private static ulong Run(BoxingCorners corners, float seconds)
    {
        using var game = new BoxingGame(seed: 3, corners: corners);
        for (var i = 0; i < (int)(seconds * 60f); i++)
        {
            game.Step(default, 1f / 60f);
        }

        return game.StateChecksum();
    }
}
