using System.Numerics;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;
using MiniRing.Replay;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>2026-11-16: キャラクターの名簿（国籍・武器・肌の色・体格・名前）と、試合・記録への渡し方。</summary>
[TestClass]
public sealed class CharacterRosterTests
{
    private static readonly string[] WeaponUsers = ["jin", "konrad", "sean", "daichi", "edgar"];
    private static readonly string[] KarateUsers = ["akira"];

    /// <summary>
    /// 組み込みの 1 人ぶんのファイル（roster/&lt;id&gt;.json）の中身。名簿のファイルの教育には休みの動作の好み（<c>BreakLikes</c>）だけが入っているが、
    /// 教育の差し替えを試すテストは「教育が空の人」から始めたいので、好みを外した形（<c>"style": {}</c>）で返す。
    /// </summary>
    internal static string RosterFileJson(string id)
        => System.Text.RegularExpressions.Regex.Replace(RawRosterFileJson(id), "\"style\": \\{ \"BreakLikes\": \\{[^}]*\\} \\}", "\"style\": {}");

    /// <summary>組み込みの 1 人ぶんのファイルの生の中身（教育の好みを含む）。</summary>
    internal static string RawRosterFileJson(string id)
    {
        using var stream = typeof(CharacterRoster).Assembly.GetManifestResourceStream($"MiniRing.Characters.roster.{id}.json")!;
        return new StreamReader(stream).ReadToEnd();
    }

    /// <summary>キャラのファイルの <c>education</c> を丸ごと <paramref name="education"/>（JSON）に差し替えた中身（ほかの鍵はそのまま）。</summary>
    internal static string WithEducation(string fileJson, string education)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(fileJson)!.AsObject();
        Assert.IsTrue(node.ContainsKey("education"), "the character file has an education");
        node["education"] = System.Text.Json.Nodes.JsonNode.Parse(education);
        return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 名簿の教育は戦略の層（<c>education.style.GamePlan</c>、2026-11-18）と休みの好みだけ: 解決したスタイルは、それらを除けば教科書と同じ値。
    /// </summary>
    internal static void AssertTextbookPlusGamePlan(BotStyle textbook, BotStyle resolved, string message)
    {
        Assert.IsNotNull(resolved.GamePlan, "the roster education carries the game plan: " + message);
        Assert.IsNull(textbook.GamePlan, "textbooks carry no game plan: " + message);
        Assert.IsNotEmpty(resolved.BreakLikes, "the roster education carries the break-time likes: " + message);
        Assert.AreEqual((textbook with { BreakLikes = resolved.BreakLikes }).ToJson(), (WithTextbookGround(resolved, textbook) with { GamePlan = null }).ToJson(), "apart from the game plan, the break likes and the ground finishing, the textbook itself: " + message);
    }

    /// <summary>
    /// 倒れた相手への攻めの教育（2026-11-24、VFC のキャラの練習: <c>Ground</c>・<c>GroundDrive</c>…）を教科書の値に戻したスタイル。
    /// 名簿の教育は休みの好み・戦略の層・倒れた相手への攻めの 3 つ。
    /// </summary>
    internal static BotStyle WithTextbookGround(BotStyle resolved, BotStyle textbook) => resolved with
    {
        Ground = textbook.Ground, GroundDrive = textbook.GroundDrive, GroundRest = textbook.GroundRest, GroundLoad = textbook.GroundLoad,
        GroundCut = textbook.GroundCut, GroundStomp = textbook.GroundStomp, GroundKick = textbook.GroundKick, GroundHeel = textbook.GroundHeel,
    };

    /// <summary>倒れた相手への攻めの教育の鍵（<c>education.style</c>）。</summary>
    internal static readonly string[] GroundKeys = ["Ground", "GroundCut", "GroundDrive", "GroundHeel", "GroundKick", "GroundLoad", "GroundRest", "GroundStomp"];

    /// <summary>武器の教育（<c>education.weaponBot</c>）が無いか、とどめの経路（<c>Ground</c>）だけで、それ以外は武器の教科書そのもの。</summary>
    internal static void AssertWeaponTextbookPlusGround(WeaponStyle textbook, WeaponStyle? resolved, string message)
    {
        Assert.IsNotNull(resolved, message);
        Assert.AreEqual(textbook.ToJson(), (resolved with { Ground = textbook.Ground }).ToJson(), "apart from the ground finishing, the weapon's textbook itself: " + message);
    }

    /// <summary>決めた名簿（CLAUDE_BRIEF_ROSTER_MENU の表）: id・武器・はめる物・スタイル・身長/腕/脚/筋肉。</summary>
    private static readonly (string Id, WeaponKind? Weapon, HandWear Wear, string Style, float H, float A, float L, float M)[] Decided =
    [
        ("jin", WeaponKind.Katana, HandWear.Glove, "champion", 1.0f, 1.0f, 1.0f, 0.95f),
        ("akira", null, HandWear.None, "karate", 0.95f, 1.0f, 1.0f, 1.05f),
        ("konrad", WeaponKind.Longsword, HandWear.Glove, "boxer", 1.15f, 1.05f, 1.05f, 1.1f),
        ("sean", WeaponKind.WoodenClub, HandWear.Glove, "slugger", 1.05f, 1.0f, 1.0f, 1.15f),
        ("vikram", null, HandWear.Katar, "boxer", 1.0f, 1.0f, 1.0f, 1.0f),
        ("ramon", null, HandWear.Glove, "swarmer", 0.92f, 0.95f, 1.0f, 1.1f),
        ("yoandry", null, HandWear.Glove, "counter", 1.02f, 1.06f, 1.0f, 1.0f),
        ("marcus", null, HandWear.Glove, "champion", 1.12f, 1.1f, 1.0f, 1.15f),
        ("albert", null, HandWear.None, "jabber", 1.0f, 1.04f, 1.0f, 1.0f),
        ("dmitri", null, HandWear.Glove, "slugger", 1.15f, 1.0f, 1.0f, 1.2f),
        ("joao", null, HandWear.KnuckleDuster, "swarmer", 0.98f, 1.0f, 1.0f, 1.05f),
        ("gaston", null, HandWear.Gauntlet, "outboxer", 1.05f, 1.05f, 1.0f, 1.0f),
        ("taeyun", null, HandWear.None, "taekwondo", 1.1f, 1.0f, 1.1f, 0.92f),
        ("daichi", WeaponKind.Staff, HandWear.Glove, "champion", 1.05f, 1.05f, 1.0f, 0.95f),
        ("tsuyoshi", null, HandWear.Tonfa, "tonfa", 1.0f, 1.03f, 1.0f, 1.0f),
        ("edgar", WeaponKind.IronSword, HandWear.Shield, "boxer", 1.05f, 1.0f, 1.0f, 1.15f),
        ("narong", null, HandWear.Glove, "muaythai", 1.02f, 1.02f, 1.05f, 0.96f),
        ("ren", null, HandWear.None, "jujutsu", 1.0f, 1.02f, 1.0f, 1.05f), // 2026-11-17: 柔術家（組み技の教科書、docs/GRAPPLING.md）
    ];

    [TestMethod]
    public void Roster_HoldsTheDecidedTwelve_WithTheirEquipmentStyleAndBody()
    {
        // 手で作った名簿（新人リーグで作られ登録された選手 = record の kind: generated を除く）。2026-11-18: 柔術家 ren を足して 18 人。
        var handMade = MiniRing.Arena.Circuit.ProspectFactory.ReservedIds(CharacterRoster.All).ToHashSet();
        var all = CharacterRoster.All.Where(c => handMade.Contains(c.Id)).ToList();
        Assert.HasCount(18, all);
        // 2026-11-25: IVF の新人リーグの上位 10 人を登録（docs/arena/ivf_prospects.md）。所属は IVF、グローブ、ボクシングの教科書。
        var league = CharacterRoster.All.Where(c => !handMade.Contains(c.Id)).ToList();
        Assert.HasCount(10, league);
        Assert.IsTrue(league.All(c => c.Org == "ivf" && c.Wear == HandWear.Glove && c.Weapon is null && c.Profile is not null), "登録した新人の所属・装備・プロフィール");
        CollectionAssert.AreEqual(Decided.Select(d => d.Id).ToArray(), all.Select(c => c.Id).ToArray(), "the decided order");
        foreach (var d in Decided)
        {
            var c = CharacterRoster.Get(d.Id);
            Assert.AreEqual(d.Weapon, c.Weapon, d.Id);
            Assert.AreEqual(d.Wear, c.Wear, d.Id);
            Assert.AreEqual(d.Style, c.StyleName, d.Id);
            Assert.AreEqual(new BodyBuild(d.H, d.A, d.L, d.M), c.Build, d.Id);
            Assert.IsTrue(BotCatalog.Styles.Any(s => s.Name == c.StyleName), d.Id);
            _ = c.Look.Validate();
            _ = c.Style;
        }
    }

    [TestMethod]
    public void Roster_HasDistinctNames_AndNoOneSharesSkinHairAndBody()
    {
        var all = CharacterRoster.All;
        Assert.HasCount(all.Count, all.Select(c => c.Name).Distinct().ToList());
        Assert.HasCount(all.Count, all.Select(c => c.HudName).Distinct().ToList());
        Assert.HasCount(all.Count, all.Select(c => (c.Look.Skin, c.Look.Hair, c.Build)).Distinct().ToList());
        // 武器はキャラクターごとに固定: 握る武器のある人は 5 人だけ。
        CollectionAssert.AreEquivalent(WeaponUsers, all.Where(c => c.Weapon is not null).Select(c => c.Id).ToArray());
    }

    [TestMethod]
    public void Roster_OnlyAkiraIsKarate()
    {
        CollectionAssert.AreEqual(KarateUsers, CharacterRoster.All.Where(c => c.IsKarate).Select(c => c.Id).ToArray());
        // 空手の判定はスタイルの名前で決まる（表示名ではない）。
        var akira = new MatchSelection(MatchMode.Versus, CharacterRoster.Get("akira"), CharacterRoster.Get("jin")).Corners;
        Assert.IsTrue(LocalVersusControl.IsKarate(akira.PlayerName, akira.PlayerWear));
        Assert.IsFalse(LocalVersusControl.IsKarate(akira.CpuName, akira.CpuWear));
    }

    [TestMethod]
    [DataRow("jin", "\"textbook\": \"champion\"", "\"textbook\": \"nope\"", "nope")]
    [DataRow("jin", "\"skin\": [0.88, 0.7, 0.55]", "\"skin\": [1.88, 0.7, 0.55]", "look.skin")]
    [DataRow("jin", "\"muscle\": 0.95", "\"muscle\": 2.5", "体格")]
    [DataRow("jin", "\"weapon\": \"katana\"", "\"weapon\": \"laser\"", "laser")]
    [DataRow("konrad", "\"country\": \"germany\"", "\"country\": \"atlantis\"", "atlantis")]
    [DataRow("jin", "\"tagline\": \"居合の剣士\"", "\"tagline\": \"居合の剣士\", \"power\": 9000", "power")]
    [DataRow("akira", "\"parts\": { \"browThickness\": 1.2 }", "\"parts\": { \"browThickness\": 9 }", "look.parts")]
    public void Roster_RejectsBrokenData_WithAMessageThatNamesTheField(string id, string from, string to, string expected)
    {
        var json = RosterFileJson(id);
        Assert.Contains(from, json, "the test's source text is in the character file");
        var broken = json.Replace(from, to, StringComparison.Ordinal);
        var error = Assert.ThrowsExactly<InvalidDataException>(() => CharacterRoster.FromFiles([(id + ".json", broken)]));
        Assert.Contains(expected, error.Message);
    }

    [TestMethod]
    public void MatchSelection_PassesTheCharactersEquipmentFaceBodyAndReferee_ToTheMatch()
    {
        var sean = CharacterRoster.Get("sean");
        var jin = CharacterRoster.Get("jin");
        var single = new MatchSelection(MatchMode.Single, sean, jin, Referee: false).Corners;
        Assert.AreEqual(WeaponKind.WoodenClub, single.PlayerWeapon);
        Assert.AreEqual(WeaponKind.Katana, single.CpuWeapon);
        Assert.IsFalse(single.PlayerTwoHanded, "the club is one-handed");
        Assert.IsTrue(single.CpuTwoHanded, "the katana is two-handed");
        Assert.AreEqual(sean.Build, single.PlayerBody);
        Assert.AreEqual(jin.Build, single.CpuBody);
        Assert.AreEqual(sean.Look, single.PlayerLook);
        Assert.AreEqual(jin.Look, single.CpuLook);
        Assert.AreEqual(sean.HudName, single.PlayerLabel);
        Assert.AreEqual(jin.HudName, single.CpuLabel);
        Assert.IsFalse(single.Referee);
        Assert.IsNull(single.RedBotStyle, "a human plays red");
        Assert.IsFalse(single.ExternalBlue);

        var versus = new MatchSelection(MatchMode.Versus, sean, jin).Corners;
        Assert.IsTrue(versus.ExternalBlue);
        Assert.IsTrue(versus.Referee, "the referee is on by default");
        var spectate = new MatchSelection(MatchMode.Spectate, CharacterRoster.Get("marcus"), CharacterRoster.Get("dmitri")).Corners;
        Assert.AreEqual("champion", spectate.RedBotStyle!.Name);
        Assert.AreEqual("slugger", spectate.CpuName);
        Assert.IsNull(spectate.PlayerWeapon);
        Assert.AreEqual(HandWear.Glove, spectate.PlayerWear);
    }

    [TestMethod]
    public void Match_UsesTheCharactersFaceAndBodySize()
    {
        var ramon = CharacterRoster.Get("ramon");
        var marcus = CharacterRoster.Get("marcus");
        var corners = new MatchSelection(MatchMode.Spectate, ramon, marcus).Corners;
        using var game = new BoxingGame(seed: 1, corners: corners);
        // 肌の色は名簿の値そのもの（リングネームから決めた顔ではない）。名簿の顔（"face" の skin）があればその色（顔と首・体の肌をそろえる: FaceLayer.LookWith）。
        Assert.AreEqual(new Vector4(ramon.Face.Skin ?? ramon.Look.Skin, 1f), game.Player.Voxels!.PartOf(game.Player.Parts.Head)!.Body.Grid.Palette[BoxerAnatomy.Skin].Color);
        Assert.AreEqual(new Vector4(marcus.Face.Skin ?? marcus.Look.Skin, 1f), game.Cpu.Voxels!.PartOf(game.Cpu.Parts.Head)!.Body.Grid.Palette[BoxerAnatomy.Skin].Color);
        // 背の高い人は体の箱が大きい（見えている体 = 強さの元、§1.7）。
        Assert.IsGreaterThan(game.Player.HalfExtents.Chest.Y, game.Cpu.HalfExtents.Chest.Y);
        Assert.AreEqual(ramon.HudName, game.RedLabel);
        Assert.AreEqual(marcus.HudName, game.BlueLabel);
    }

    [TestMethod]
    public void Replay_RoundTripsTheCharactersFace_AndOldRecordingsKeepTheNameDerivedFace()
    {
        var gaston = CharacterRoster.Get("gaston");
        var joao = CharacterRoster.Get("joao");
        var corners = new MatchSelection(MatchMode.Single, gaston, joao, Referee: false).Corners;
        using var game = new BoxingGame(seed: 5, corners: corners);
        using var stream = new MemoryStream();
        using (var recorder = new ReplayRecorder(stream, game, "test")) { }
        var restored = ReplayFile.Read(new MemoryStream(stream.ToArray())).Header.Corners;
        Assert.AreEqual(gaston.Look, restored.PlayerLook, "the same face on replay (colours, hairstyle, brows, beard, parts)");
        Assert.AreEqual(joao.Look, restored.CpuLook);
        Assert.AreEqual(gaston.HudName, restored.PlayerLabel);
        Assert.AreEqual(joao.HudName, restored.CpuLabel);
        Assert.AreEqual(HandWear.Gauntlet, restored.PlayerWear);
        Assert.AreEqual(HandWear.KnuckleDuster, restored.CpuWear);
        Assert.AreEqual(gaston.Build, restored.PlayerBody);

        // 古い記録（顔の欄が無い）は今までどおり名前から。
        using var oldGame = new BoxingGame(seed: 5, corners: BotCatalog.Corners("boxer"));
        using var oldStream = new MemoryStream();
        using (var recorder = new ReplayRecorder(oldStream, oldGame, "test")) { }
        var old = ReplayFile.Read(new MemoryStream(oldStream.ToArray())).Header.Corners;
        Assert.IsNull(old.PlayerLook);
        Assert.IsNull(old.CpuLook);
        Assert.IsNull(old.PlayerLabel);
    }

    [TestMethod]
    public void Replay_RejectsABrokenFaceInTheHeader()
    {
        var broken = new ReplayMatchSettings(null, null, false, false, true, HandWear.Glove, HandWear.Glove, null, null, null, null,
            null, null, null, CpuLook: CharacterRoster.Get("jin").Look with { Skin = new Vector3(3f, 0f, 0f) });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => broken.Apply(BotCatalog.Corners("boxer")));
    }

    [TestMethod]
    public void Portraits_AreProjectedFromTheRealVoxels_AndTallerBodiesAreTaller()
    {
        var ramon = CharacterPortraits.Build(CharacterRoster.Get("ramon"));
        var marcus = CharacterPortraits.Build(CharacterRoster.Get("marcus"));
        var standard = CharacterPortraits.Build(CharacterRoster.Get("vikram"));
        // 全身の高さ・幅は体格の寸法に従う（背の高い人はピクセルが多い）。
        Assert.IsGreaterThan(standard.Body.Height, marcus.Body.Height);
        Assert.IsLessThan(standard.Body.Height, ramon.Body.Height);
        Assert.IsGreaterThan(standard.Body.Width, marcus.Body.Width, "a thicker (muscle 1.15, arms 1.1) body is wider");
        // 顔: 頭の顔の層（FaceLayer: 20 × 28 セル、約 2.5 mm）の正面。肌の色のセルが顔の大半。
        Assert.IsInRange(16, 28, marcus.Face.Width);
        Assert.IsInRange(24, 36, marcus.Face.Height);
        var skin = CharacterRoster.Get("marcus").Face.Skin ?? CharacterRoster.Get("marcus").Look.Skin; // 名簿の顔の肌の色（FaceLayer.LookWith）
        var skinCells = 0;
        for (var y = 0; y < marcus.Face.Height; y++)
        {
            for (var x = 0; x < marcus.Face.Width; x++)
            {
                skinCells += marcus.Face.MaterialAt(x, y) == BoxerAnatomy.Skin ? 1 : 0;
            }
        }

        Assert.IsGreaterThan(marcus.Face.FilledCount / 3, skinCells);
        var palette = marcus.Face.Palette[BoxerAnatomy.Skin].SurfaceColor;
        Assert.AreEqual(skin.X, palette.X, 1e-6f, "the portrait takes its colour from the match's palette");
        // 白目・黒目がある（目が写っている）。
        var materials = new HashSet<byte>(marcus.Face.Materials);
        Assert.IsTrue(materials.Contains(BoxerAnatomy.Eye) || materials.Contains(BoxerAnatomy.Pupil), "eyes are visible from the front");
        // 頭のてっぺんは顔の形（名簿の "face"）の髪: F3 で名簿の全員に髪の部品が付いた（マーカスも hair.10。docs/plans/face_refs/compare/roster_composed.png）。
        // 髪の無い顔（部品 hair.3 = 坊主）なら、てっぺんは皮膚。
        var bald = CharacterRoster.Get("marcus");
        var baldParts = bald.Face.Parts.ToDictionary(p => p.Key, p => p.Value);
        baldParts["hair"] = new Dictionary<string, float> { ["hair.3"] = 1f };
        var baldFace = CharacterPortraits.Build(bald with { Face = bald.Face with { Parts = baldParts } }).Face;
        Assert.IsFalse(Enumerable.Range(0, baldFace.Width).Any(x => baldFace.MaterialAt(x, 0) == BoxerAnatomy.Hair), "bald: no hair on top");
        var konrad = CharacterPortraits.Build(CharacterRoster.Get("konrad")).Face;
        Assert.IsTrue(Enumerable.Range(0, konrad.Width).Any(x => konrad.MaterialAt(x, 0) == BoxerAnatomy.Hair), "full hair on top");
    }

    [TestMethod]
    public void Flags_AreSimpleStripesAndShapes()
    {
        foreach (var c in CharacterRoster.All)
        {
            Assert.IsNotEmpty(c.Country.Flag.Stripes, c.Id);
        }

        Assert.AreSame(CharacterRoster.Get("jin").Country, CharacterRoster.Get("akira").Country, "Okinawa flies the Japanese flag");
        Assert.AreEqual("日本（沖縄）", CharacterRoster.Get("akira").Origin);
    }

    [TestMethod]
    public void Replay_RejectsABrokenHeader_NullFacePartsAndBadLabels()
    {
        FaceLookBroken(CharacterRoster.Get("jin").Look with { Parts = null! });
        foreach (var label in new[] { "", new string('あ', 33), "a\u0007b", "x\ny" })
        {
            var settings = new ReplayMatchSettings(null, null, false, false, true, HandWear.Glove, HandWear.Glove, null, null, null, null,
                null, null, null, PlayerLabel: label);
            Assert.ThrowsExactly<ArgumentException>(() => settings.Apply(BotCatalog.Corners("boxer")), label);
        }

        var ok = new ReplayMatchSettings(null, null, false, false, true, HandWear.Glove, HandWear.Glove, null, null, null, null,
            null, null, null, PlayerLabel: "ジン", CpuLabel: new string('a', 32));
        _ = ok.Apply(BotCatalog.Corners("boxer"));
    }

    private static void FaceLookBroken(FaceLook look)
    {
        var settings = new ReplayMatchSettings(null, null, false, false, true, HandWear.Glove, HandWear.Glove, null, null, null, null,
            null, null, null, CpuLook: look);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => settings.Apply(BotCatalog.Corners("boxer")));
    }

    [TestMethod]
    public void PortraitSet_OneFailingCharacterDoesNotStopTheOthers_AndDisposeDoesNotThrow()
    {
        var jin = CharacterRoster.Get("jin");
        var broken = jin with { Id = "broken", Look = jin.Look with { Parts = null! } };
        var roster = new[] { broken, CharacterRoster.Get("sean") };
        var set = new PortraitSet(roster).Complete();
        Assert.IsNull(set.TryGet(0), "the broken one keeps its placeholder");
        Assert.IsNotNull(set.TryGet(1), "the next one is still built");
        set.Dispose();
    }
}
