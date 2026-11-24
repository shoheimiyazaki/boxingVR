using System.Numerics;
using System.Text.Json;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Arena;
using MiniRing.Audio;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 中継の団体（WVC・VFC。<c>games/MiniRing.Arena/Orgs/*.json</c>）: ファイルの読み込みと検査、WVC の値が元のコードの定数と同じこと、
/// VFC の下見の点数・台本の下書き・合図の音・会場の雰囲気・選手の所属。描画と試合に触れない部分だけ（PNG の確認は <see cref="OrganizationVisualTests"/>）。
/// </summary>
[TestClass]
public sealed class OrganizationTests
{
    private static readonly string[] Ids = ["ivf", "vfc", "wvc"];
    private static readonly string[] VfcFighters = ["daichi", "edgar", "galva", "gaston", "jin", "joao", "konrad", "sean", "tsuyoshi", "vikram"];

    private const string KnockoutEvents = """
        {"red":"ジン","blue":"コンラート","redDiscipline":"日本刀","blueDiscipline":"ロングソード","bout":"第1試合","org":"vfc","events":[
          {"t":0,"for":"実況","type":"opening","who":"","text":"冒頭のタイトル"},
          {"t":7,"for":"実況","type":"round_start","who":"","text":"第1ラウンド開始"},
          {"t":20,"for":"実況","type":"hit","who":"ジン","text":"ジンの重い刀が、コンラートの胸（胸）を斬った"},
          {"t":30,"for":"実況","type":"knockdown","who":"コンラート","text":"コンラート、ダウン"},
          {"t":31,"for":"実況","type":"count","who":"コンラート","text":"カウント 8"},
          {"t":47,"for":"実況","type":"round_end","who":"","text":"第1ラウンド終了"},
          {"t":60,"for":"実況","type":"ko","who":"","text":"脳が壊れた。ジンの勝ち"},
          {"t":64,"for":"実況","type":"result","who":"","text":"勝敗の画面（9 秒）"},
          {"t":73,"for":"実況","type":"end","who":"","text":"動画の終わり"}
        ]}
        """;

    private static string Draft(string json, Organization org)
        => string.Join("\n", JsonDocument.Parse(TvDraft.Build(json, org)).RootElement.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("text").GetString()));

    [TestMethod]
    public void Organizations_BothFilesLoad_AndWvcIsTheDefault()
    {
        CollectionAssert.AreEquivalent(Ids, Organizations.All.Keys.ToArray());
        Assert.AreEqual("wvc", Organization.Default.Id);
        Assert.AreEqual("Wonderful Voxing Council", Organization.Of(null).Name);
        Assert.AreEqual("Voxel Fighting Championship", Organization.Of("vfc").Name);
        Assert.AreEqual("VFC", Organization.Of("vfc").LogoText);
        Assert.AreEqual("Voxel Fighting", Organization.Of("vfc").Sport);
        Assert.IsFalse(Organization.Of("vfc").CountsDowns);
        Assert.IsTrue(Organization.Of("wvc").CountsDowns);
        Assert.ThrowsExactly<ArgumentException>(() => Organization.Of("ufc"));
    }

    [TestMethod]
    public void Wvc_KeepsTheValuesOfTheOldHardCodedBroadcast()
    {
        var wvc = Organization.Of("wvc");

        // 画面の色: 元の TvShowGraphics の定数（Gold・White・Grey・Ink、勝敗の画面の引き分けの帯）。
        Assert.AreEqual(new Vector4(1f, 0.82f, 0.28f, 1f), wvc.Colors.Accent);
        Assert.AreEqual(new Vector4(1f, 1f, 1f, 1f), wvc.Colors.Text);
        Assert.AreEqual(new Vector4(0.78f, 0.8f, 0.86f, 1f), wvc.Colors.Muted);
        Assert.AreEqual(new Vector4(0.04f, 0.05f, 0.08f, 0.9f), wvc.Colors.Ink);
        Assert.AreEqual(new Vector4(0.45f, 0.33f, 0.05f, 0.9f), wvc.Colors.AccentDeep);
        Assert.AreEqual("gong", wvc.Bell);
        Assert.AreEqual(1f, wvc.Crowd.ReactionGain);
        Assert.AreEqual(1f, wvc.Crowd.AmbienceGain);
        // 試合前の会場: 今の明るい昼の部屋（雰囲気の既定）。
        Assert.AreEqual(ArenaMood.Default.Ambient, wvc.ArenaMood.Ambient);
        Assert.AreEqual(0f, wvc.ArenaMood.CageSpotIntensity);
        Assert.AreEqual(ArenaLook.ScreenFinish, ArenaLook.ScreenFinishFor(wvc.ArenaMood));
        // 台本の資料の決まりは Broadcast の既定（元の文）と同じ。
        var rules = new Broadcast("a", "b").RulesSheet;
        CollectionAssert.AreEqual(rules.ToArray(), wvc.RulesSheet.ToArray());
    }

    [TestMethod]
    public void Drama_Wvc_IsTheOldFormula()
    {
        // 元の式（テレビ中継の下見）をここに写して、WVC の重みで同じ点になることを確かめる。
        static float Old(TvScoutResult r, int rounds, float roundSeconds)
        {
            var score = 3f * ((r.Round - 1) + Math.Clamp(r.RoundTime / roundSeconds, 0f, 1f));
            score += 1.5f * Math.Min(r.RedDowns + r.BlueDowns, 5);
            score += r.RedDowns > 0 && r.BlueDowns > 0 ? 3f : 0f;
            score += r.Ko ? 2f + (r.Round >= rounds ? 1f : 0f) : 0f;
            var winnerDealt = r.Winner > 0 ? r.RedDealt : r.BlueDealt;
            var loserDealt = r.Winner > 0 ? r.BlueDealt : r.RedDealt;
            score += r.Winner != 0 && winnerDealt < loserDealt ? 4f : 0f;
            score -= r.Round == 1 && r.RoundTime < 25.18f ? 6f : 0f;
            score -= r.Winner == 0 ? 1f : 0f;
            return score;
        }

        foreach (var r in new[]
        {
            new TvScoutResult(1, "K.O.", true, 3, 33.57f, 100f, 300f, 2, 1, 1),
            new TvScoutResult(2, "K.O.", true, 1, 10.07f, 40f, 0f, 0, 1, 1),
            new TvScoutResult(3, "DRAW", false, 5, 50.35f, 100f, 100f, 0, 0, 0),
            new TvScoutResult(4, "K.O. — BRAIN DESTROYED", true, 5, 20.14f, 250f, 80f, 3, 0, -1, Death: true),
        })
        {
            Assert.AreEqual(Old(r, 5, 50.35f), TvShowRules.Drama(r, 5, 50.35f, Organization.Of("wvc").Drama), 1e-5f);
            Assert.AreEqual(Old(r, 5, 50.35f), TvShowRules.Drama(r, 5, 50.35f), 1e-5f, "重みを渡さなければ WVC");
        }
    }

    [TestMethod]
    public void Drama_Vfc_LateDeathBeatsEarlyDeath_ComebackBeatsOneSided_AndDownsDoNotCount()
    {
        var w = Organization.Of("vfc").Drama;
        TvScoutResult Death(int round, float roundTime, float redDealt, float blueDealt, int winner, int downs = 0)
            => new(1, "K.O. — BRAIN DESTROYED", true, round, roundTime, redDealt, blueDealt, downs, 0, winner, Death: true);

        var late = Death(5, 33.57f, 180f, 160f, 1);
        var early = Death(2, 33.57f, 180f, 160f, 1);
        var quick = Death(1, 10.07f, 40f, 0f, 1);
        var comeback = Death(4, 25.18f, 120f, 300f, 1);
        var oneSided = Death(4, 25.18f, 300f, 120f, 1);
        var bothSurvive = new TvScoutResult(2, "DRAW", false, 5, 50.35f, 180f, 160f, 0, 0, 0);

        Assert.IsTrue(TvShowRules.Drama(late, 5, 50.35f, w) > TvShowRules.Drama(early, 5, 50.35f, w), "後半の死 > 序盤の死");
        Assert.IsTrue(TvShowRules.Drama(comeback, 5, 50.35f, w) > TvShowRules.Drama(oneSided, 5, 50.35f, w), "逆転 > 一方的");
        Assert.IsTrue(TvShowRules.Drama(early, 5, 50.35f, w) > TvShowRules.Drama(quick, 5, 50.35f, w), "第 1 ラウンド 25 秒より前の決着は大きく下がる");
        Assert.IsTrue(TvShowRules.Drama(late, 5, 50.35f, w) > TvShowRules.Drama(bothSurvive, 5, 50.35f, w), "死で決着 > 両者生還");
        Assert.AreEqual(TvShowRules.Drama(late, 5, 50.35f, w), TvShowRules.Drama(late with { RedDowns = 4, BlueDowns = 3 }, 5, 50.35f, w), 1e-5f, "ダウンの数は点にならない");
        var deep = Death(4, 25.18f, 250f, 250f, 1);
        var shallow = Death(4, 25.18f, 250f, 20f, 1);
        Assert.IsTrue(TvShowRules.Drama(deep, 5, 50.35f, w) > TvShowRules.Drama(shallow, 5, 50.35f, w), "両者が深く傷んでいる方が高い");
    }

    [TestMethod]
    public void ScoutResult_RecognisesDeathFromTheFinishText()
    {
        Assert.IsTrue(TvScoutResult.IsDeath("K.O. — BRAIN DESTROYED  BLUE WINS"));
        Assert.IsTrue(TvScoutResult.IsDeath("K.O. — NECK DESTROYED"));
        Assert.IsFalse(TvScoutResult.IsDeath("K.O. — OUT COLD"));
        Assert.IsFalse(TvScoutResult.IsDeath("10 COUNT K.O."));
        Assert.IsFalse(TvScoutResult.IsDeath("DRAW"));
    }

    [TestMethod]
    public void Draft_Vfc_NeverCountsDowns_AndCallsTheHorn()
    {
        var vfc = Draft(KnockoutEvents, Organization.Of("vfc"));
        Assert.IsFalse(vfc.Contains("ダウン", StringComparison.Ordinal), vfc);
        Assert.IsFalse(vfc.Contains("ゴング", StringComparison.Ordinal), vfc);
        Assert.IsFalse(vfc.Contains("カウント", StringComparison.Ordinal), vfc);
        Assert.IsFalse(vfc.Contains("エイト", StringComparison.Ordinal), vfc);
        StringAssert.Contains(vfc, "第1ラウンド、ホーン！");
        StringAssert.Contains(vfc, "コンラート、倒れた！それでも試合は止まらない！");
        StringAssert.Contains(vfc, "決着！コンラート、動かない！");
        StringAssert.Contains(vfc, "ジンの勝ち！");

        var wvc = Draft(KnockoutEvents, Organization.Of("wvc"));
        StringAssert.Contains(wvc, "第1ラウンド、ゴング！");
        StringAssert.Contains(wvc, "コンラート、ダウン！");
        StringAssert.Contains(wvc, "ジンのKO勝ち！");
    }

    [TestMethod]
    public void Draft_ReadsTheOrganizationFromTheEventsFile()
    {
        Assert.AreEqual("vfc", TvDraft.OrgOf(KnockoutEvents).Id);
        Assert.AreEqual("wvc", TvDraft.OrgOf("""{"red":"a","blue":"b","events":[]}""").Id, "org が無い記録は WVC");
        Assert.AreEqual(TvDraft.Build(KnockoutEvents, Organization.Of("vfc")), TvDraft.Build(KnockoutEvents), "--org が無ければ記録の org");
    }

    [TestMethod]
    public void Draft_Vfc_BothSurviveIsADraw_NotAJudgement()
    {
        const string DrawEvents = """
            {"red":"ジン","blue":"コンラート","org":"vfc","events":[
              {"t":0,"for":"実況","type":"opening","who":"","text":"冒頭"},
              {"t":7,"for":"実況","type":"round_start","who":"","text":"第1ラウンド開始"},
              {"t":40,"for":"実況","type":"decision","who":"","text":"DRAW"},
              {"t":44,"for":"実況","type":"result","who":"","text":"勝敗の画面"},
              {"t":53,"for":"実況","type":"end","who":"","text":"終わり"}
            ]}
            """;
        StringAssert.Contains(Draft(DrawEvents, Organization.Of("vfc")), "両者生還、引き分け");
    }

    [TestMethod]
    public void Lint_UsesTheOrganizationsCallWords()
    {
        // 決着のコールが「決着」だけの台本: WVC の言葉（ノックアウト・KO・判定など）には無いが、VFC の言葉には入る。
        const string Script = """{"lines":[{"at":60.2,"speaker":"announcer","text":"決着！コンラート、そこまで。"}]}""";
        const string Events = """
            {"red":"ジン","blue":"コンラート","org":"vfc","events":[
              {"t":0,"for":"実況","type":"opening","who":"","text":"冒頭"},
              {"t":60,"for":"実況","type":"ko","who":"","text":"脳が壊れた。ジンの勝ち"}
            ]}
            """;
        Assert.IsFalse(TvLint.Check(Script, Events).Any(i => i.Rule == "コール"), "記録の org（VFC）の言葉");
        Assert.IsTrue(TvLint.Check(Script, Events, org: Organization.Of("wvc")).Any(i => i.Rule == "コール"), "WVC の言葉には「決着」が無い");
    }

    [TestMethod]
    public void Digest_ShowsTheOrganizationsRulesAndTone()
    {
        var digest = TvDigest.Build(KnockoutEvents);
        StringAssert.Contains(digest, "Voxel Fighting Championship");
        StringAssert.Contains(digest, "ダウンは取らず");
        StringAssert.Contains(digest, "どこが壊れたか・あと何が残っているか");
    }

    [TestMethod]
    public void Horn_IsLowDeterministic_AndStartIsShorterThanEnd()
    {
        Assert.IsNull(HornSound.Synthesize(RoundBell.None));
        var start = HornSound.Synthesize(RoundBell.Start)!;
        var end = HornSound.Synthesize(RoundBell.End)!;
        CollectionAssert.AreEqual(start, HornSound.Synthesize(RoundBell.Start)!, "同じ音");
        Assert.IsTrue(end.Length > start.Length);
        Assert.IsLessThan(2.5f, end.Length / 2f / HornSound.SampleRate);

        // 低い音: 基音（約 98 Hz）の周りの強さが、高い帯（1.5 kHz 以上）よりずっと強い。
        static float Energy(byte[] pcm, float hz)
        {
            double re = 0, im = 0;
            for (var i = 0; i < pcm.Length / 2; i++)
            {
                var s = BitConverter.ToInt16(pcm, i * 2) / 32768.0;
                var w = 2 * Math.PI * hz * i / HornSound.SampleRate;
                re += s * Math.Cos(w);
                im += s * Math.Sin(w);
            }

            return (float)Math.Sqrt((re * re) + (im * im));
        }

        Assert.IsTrue(Energy(start, 98f) > 4f * Energy(start, 3000f), "基音が強い低い音");
    }

    [TestMethod]
    public void BellSound_Gong_IsTheSameBytesAsBefore_AndUnknownIdsAreRejected()
    {
        CollectionAssert.AreEqual(GongSound.Synthesize(RoundBell.Start)!, BellSound.Synthesize("gong", RoundBell.Start)!);
        CollectionAssert.AreEqual(GongSound.Synthesize(RoundBell.End)!, BellSound.Synthesize("gong", RoundBell.End)!);
        Assert.IsNull(BellSound.Synthesize("horn", RoundBell.None));
        Assert.ThrowsExactly<ArgumentException>(() => BellSound.Synthesize("siren", RoundBell.Start));
    }

    [TestMethod]
    public void ArenaMood_Default_IsTodaysBrightRoom_AndVfcIsDarkWithACageSpot()
    {
        var center = new Vector3(0f, 0.7f, -0.8f);
        var plain = ArenaLook.Lighting(center, 1.2f, 0.7f);
        var same = ArenaLook.Lighting(center, 1.2f, 0.7f, ArenaMood.Default);
        Assert.AreEqual(plain.Sun, same.Sun);
        Assert.AreEqual(plain.Ambient, same.Ambient);
        Assert.AreEqual(plain.PointLights[0], same.PointLights[0]);
        CollectionAssert.AreEqual(plain.SpotLights.ToArray(), same.SpotLights.ToArray());
        Assert.AreEqual(2, plain.SpotLights.Length, "今の部屋は天井と窓の 2 つのスポット");

        var mood = Organization.Of("vfc").ArenaMood;
        var dark = ArenaLook.Lighting(center, 1.2f, 0.7f, mood);
        Assert.IsTrue(dark.Ambient.Intensity < 0.4f * plain.Ambient.Intensity, "環境光を絞る");
        Assert.IsTrue(dark.Sun.Intensity < 0.5f * plain.Sun.Intensity, "主光を絞る");
        Assert.AreEqual(3, dark.SpotLights.Length, "檻の真上のスポットが 1 つ増える");
        var cage = dark.SpotLights[2];
        Assert.AreEqual(-Vector3.UnitY, cage.Direction, "真下へ");
        Assert.AreEqual(center.X, cage.Position.X, 1e-5f, "リングの真上");
        Assert.IsTrue(cage.Intensity > plain.SpotLights[0].Intensity, "檻のスポットは天井の照明より強い");
        Assert.IsTrue(ArenaLook.ScreenFinishFor(mood).Vignette > ArenaLook.ScreenFinish.Vignette, "隅が暗い");
        Assert.AreEqual(0.97f, ArenaLook.ScreenFinishFor(ArenaMood.Default).Exposure);
    }

    [TestMethod]
    public void Organizations_SchemaErrorsStopWithTheFileAndFieldNamed()
    {
        var good = File.ReadAllText(Path.Combine(OrgsDirectory(), "wvc.json"));

        // 欄が足りない・型が違う・範囲外・知らない値・ファイル名の食い違い・知らない欄は、どれも例外で止める。
        static void Rejects(string json, string expected, string fileName = "wvc.json")
        {
            var ex = Assert.ThrowsExactly<InvalidDataException>(() => Organizations.FromFiles([(fileName, json)]));
            Assert.IsTrue(ex.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), ex.Message);
        }

        Rejects(good.Replace("\"sport\": \"Voxing\",", "", StringComparison.Ordinal), "sport");
        Rejects(good.Replace("\"countsDowns\": true", "\"countsDowns\": \"yes\"", StringComparison.Ordinal), "countsDowns");
        Rejects(good.Replace("\"emblem\": \"wvc\"", "\"emblem\": \"ufc\"", StringComparison.Ordinal), "emblem");
        Rejects(good.Replace("\"bell\": \"gong\"", "\"bell\": \"siren\"", StringComparison.Ordinal), "bell");
        Rejects(good.Replace("\"accent\": [1, 0.82, 0.28]", "\"accent\": [1, 2, 0.28]", StringComparison.Ordinal), "colors.accent");
        Rejects(good.Replace("\"accent\": [1, 0.82, 0.28]", "\"accent\": [1, 0.82]", StringComparison.Ordinal), "colors.accent");
        Rejects(good.Replace("\"vignette\": 0.5", "\"vignette\": 3", StringComparison.Ordinal), "arenaMood.vignette");
        Rejects(good.Replace("\"perRound\": 3", "\"perRound\": -1", StringComparison.Ordinal), "drama.perRound");
        Rejects(good.Replace("\"id\": \"wvc\"", "\"id\": \"VFC\"", StringComparison.Ordinal), "id");
        Rejects(good, "ファイル名", "other.json");
        Rejects(good.Replace("\"sport\"", "\"sprot\": 1, \"sport\"", StringComparison.Ordinal), "sprot");
        Rejects("{ nope", "読めません");
    }

    [TestMethod]
    public void Roster_EveryFighterBelongsToAKnownOrganization_WeaponUsersToVfc()
    {
        foreach (var c in CharacterRoster.All)
        {
            Assert.IsTrue(Organizations.All.ContainsKey(c.Org), $"{c.Id} の所属 '{c.Org}' が団体のファイルにありません");
        }

        foreach (var id in VfcFighters)
        {
            Assert.AreEqual("vfc", CharacterRoster.Get(id).Org, id);
        }

        // 手で作った名簿の VFC 以外は WVC。新人リーグで作られ登録された選手（2026-11-25）は IVF。
        var handMade = MiniRing.Arena.Circuit.ProspectFactory.ReservedIds(CharacterRoster.All).ToHashSet();
        foreach (var c in CharacterRoster.All.Where(c => !VfcFighters.Contains(c.Id)))
        {
            Assert.AreEqual(handMade.Contains(c.Id) ? "wvc" : "ivf", c.Org, c.Id);
        }

        // 武器・グローブ以外のはめる物を使う選手は VFC、素手・グローブの拳は WVC（空手・テコンドー・ムエタイも）。
        foreach (var c in CharacterRoster.All.Where(c => handMade.Contains(c.Id)))
        {
            var anything = c.Weapon is not null || c.Wear is not (MiniRing.Weapons.HandWear.Glove or MiniRing.Weapons.HandWear.None);
            Assert.AreEqual(anything ? "vfc" : "wvc", c.Org, $"{c.Id}: {c.EquipmentName}");
        }
    }

    [TestMethod]
    public void Roster_VfcFightersDoNotClaimToHaveJoinedTheWvc()
    {
        foreach (var id in VfcFighters)
        {
            var c = CharacterRoster.Get(id);
            var career = string.Join("\n", c.Profile?.Career ?? []);
            Assert.IsFalse(career.Contains("WVC", StringComparison.Ordinal), $"{id}: 経歴に WVC が残っている\n{career}");
            Assert.IsFalse(career.Contains("Voxing", StringComparison.Ordinal), $"{id}: 経歴に Voxing が残っている\n{career}");
        }
    }

    [TestMethod]
    public void Roster_OrgIsValidated()
    {
        var jin = File.ReadAllText(Path.Combine(RosterDirectory(), "jin.json"));
        Assert.AreEqual("vfc", CharacterRoster.FromFiles([("jin.json", jin)]).Single().Org);
        var withoutOrg = System.Text.RegularExpressions.Regex.Replace(jin, "\"org\"\\s*:\\s*\"vfc\",?", "");
        Assert.AreEqual("wvc", CharacterRoster.FromFiles([("jin.json", withoutOrg)]).Single().Org, "所属が無ければ WVC");
        var bad = jin.Replace("\"org\": \"vfc\"", "\"org\": \"Not An Id\"", StringComparison.Ordinal);
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => CharacterRoster.FromFiles([("jin.json", bad)]));
        StringAssert.Contains(ex.Message, "org");
    }

    internal static string OrgsDirectory() => Path.Combine(RepoRoot(), "games", "MiniRing.Arena", "Orgs");

    private static string RosterDirectory() => Path.Combine(RepoRoot(), "games", "MiniRing", "Characters", "roster");

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MiniRing.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}

/// <summary>
/// 団体の紋章・タイトル・ロゴの札・勝敗の画面・会場の照明を PNG に描く（<c>.visualcheck/org_*.png</c>）。PNG があること ≠ 正しいので、人が中身を見て手本（docs/images/vfc_logo_reference.png）と比べる。
/// </summary>
[TestClass]
[TestCategory("visual")]
public sealed class OrganizationVisualTests
{
    private const int Width = 1280;
    private const int Height = 720;

    private static string OutDir()
    {
        var dir = Path.Combine(OrganizationTests.RepoRoot(), ".visualcheck");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static TvCard Card(Organization org, string red, string blue)
        => new("ファイトナイト", "第1試合", TvCorner.Of(CharacterRoster.Get(red)), TvCorner.Of(CharacterRoster.Get(blue)), TvShowRules.Rules(5, 41.96f));

    [TestMethod]
    public void Emblems_RenderOnBlack_AtLargeAndLogoSizes()
    {
        var outDir = OutDir();
        using var renderer = new HeadlessRenderer(Width, Height);
        foreach (var org in Organizations.All.Values)
        {
            var text = new List<ScreenTextDraw>();
            var shapes = new List<UiShapeDraw>();
            Emblems.Draw(org, text, shapes, new Vector2(Width * 0.5f, Height * 0.5f), 26f);
            PngWriter.WriteRgba(Path.Combine(outDir, $"org_emblem_{org.Id}.png"), Width, Height, renderer.RenderPrimitiveScene(new Vector4(0f, 0f, 0f, 1f), Matrix4x4.Identity, [], [], text, shapes: shapes));
            Assert.IsTrue(shapes.Count > 100);
        }

        // 角の小さなロゴの札（実寸）。
        using var small = new HeadlessRenderer(360, 110);
        foreach (var org in Organizations.All.Values)
        {
            var text = new List<ScreenTextDraw>();
            var shapes = new List<UiShapeDraw>();
            new TvShowGraphics(Width, Height, org).Logo(text, shapes, new Vector2(180f, 55f), 1.3f);
            PngWriter.WriteRgba(Path.Combine(outDir, $"org_logo_{org.Id}.png"), 360, 110, small.RenderPrimitiveScene(new Vector4(0.12f, 0.12f, 0.15f, 1f), Matrix4x4.Identity, [], [], text, shapes: shapes));
        }
    }

    [TestMethod]
    public void BroadcastScreens_RenderPerOrganization()
    {
        var outDir = OutDir();
        using var renderer = new HeadlessRenderer(Width, Height);
        var back = new Vector4(0.2f, 0.19f, 0.2f, 1f);
        foreach (var (org, red, blue) in new[] { (Organization.Of("wvc"), "marcus", "yoandry"), (Organization.Of("vfc"), "jin", "konrad") })
        {
            var g = new TvShowGraphics(Width, Height, org);
            var card = Card(org, red, blue);
            var (t1, s1) = g.Opening(card, 4f);
            PngWriter.WriteRgba(Path.Combine(outDir, $"org_opening_{org.Id}.png"), Width, Height, renderer.RenderPrimitiveScene(back, Matrix4x4.Identity, [], [], t1, shapes: s1));
            var draw = new TvResult(0, "DRAW", 5, 50.35f, [100f, 120f, 90f, 80f, 60f], [90f, 100f, 110f, 70f, 80f]);
            var (t2, s2) = g.Result(card, draw, 3.5f);
            PngWriter.WriteRgba(Path.Combine(outDir, $"org_result_draw_{org.Id}.png"), Width, Height, renderer.RenderPrimitiveScene(back, Matrix4x4.Identity, [], [], t2, shapes: s2));
            var ko = new TvResult(1, "K.O. — BRAIN DESTROYED  BLUE WINS", 4, 30.21f, [100f, 120f, 90f, 80f], [90f, 100f, 110f, 70f]);
            var (t3, s3) = g.Result(card, ko, 3.5f);
            PngWriter.WriteRgba(Path.Combine(outDir, $"org_result_ko_{org.Id}.png"), Width, Height, renderer.RenderPrimitiveScene(back, Matrix4x4.Identity, [], [], t3, shapes: s3));
            Assert.IsTrue(t1.Any(t => t.Text.Contains(org.Name, StringComparison.OrdinalIgnoreCase) || t.Text.Replace(" ", "").Contains(org.Name.ToUpperInvariant(), StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public void BroadcastScreens_Ivf_ShowTheDivisionTitleBoutAndTko()
    {
        var outDir = OutDir();
        using var renderer = new HeadlessRenderer(Width, Height);
        var back = new Vector4(0.2f, 0.19f, 0.2f, 1f);
        var ivf = Organization.Of("ivf");
        var g = new TvShowGraphics(Width, Height, ivf);
        var card = new TvCard("IVF ファイトナイト", "メインイベント　・　ヘビー級タイトルマッチ",
            TvCorner.Of(CharacterRoster.Get("marcus")) with { Seat = "ヘビー級 王者" }, TvCorner.Of(CharacterRoster.Get("dmitri")) with { Seat = "ヘビー級 1位" },
            TvShowRules.Rules(5, 10f, ivf));
        var (t1, s1) = g.Opening(card, 4f);
        PngWriter.WriteRgba(Path.Combine(outDir, "org_opening_ivf.png"), Width, Height, renderer.RenderPrimitiveScene(back, Matrix4x4.Identity, [], [], t1, shapes: s1));
        var (t2, s2) = g.Tape(card, 6f);
        PngWriter.WriteRgba(Path.Combine(outDir, "org_tape_ivf.png"), Width, Height, renderer.RenderPrimitiveScene(back, Matrix4x4.Identity, [], [], t2, shapes: s2));
        var tko = new TvResult(1, "TKO — 3 KNOCKDOWNS  RED WIN", 2, 21f, [100f, 120f], [40f, 30f]);
        var (t3, s3) = g.Result(card, tko, 3.5f);
        PngWriter.WriteRgba(Path.Combine(outDir, "org_result_tko_ivf.png"), Width, Height, renderer.RenderPrimitiveScene(back, Matrix4x4.Identity, [], [], t3, shapes: s3));
        Assert.IsTrue(t1.Any(t => t.Text.Contains("タイトルマッチ", StringComparison.Ordinal)));
        Assert.IsTrue(t2.Any(t => t.Text.Contains("ヘビー級 王者", StringComparison.Ordinal)), "比べる表に階級と席");
        Assert.IsTrue(t3.Any(t => t.Text.Contains("T.K.O.", StringComparison.Ordinal) || t.Text.Contains("3 度のダウン", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ArenaMoods_LightTheRingScene()
    {
        var outDir = OutDir();
        using var game = new BoxingGame(seed: 3);
        using var renderer = new HeadlessRenderer(Width, Height);
        var (eye, target) = CameraFollow.MatchOpeningView(game.Player.ChestPosition, game.Cpu.ChestPosition, 1f);
        var camera = new Aigis.Player.OrbitCamera();
        camera.SetView(eye, target);
        var view = camera.ViewProjection((float)Width / Height);
        var (instances, lines) = game.BuildRenderables();
        foreach (var org in Organizations.All.Values)
        {
            var layout = game.Layout;
            var rgba = renderer.RenderPrimitiveScene(ArenaLook.Background, view, instances, lines, [], particles: game.BuildParticles(),
                lighting: ArenaLook.Lighting(layout.Center, layout.Size, BoxingGame.RingSurfaceHeight, org.ArenaMood), shadow: game.BuildShadow(), cameraPosition: camera.Eye,
                postProcess: ArenaLook.ScreenFinishFor(org.ArenaMood), voxels: game.BuildVoxelVolumes());
            PngWriter.WriteRgba(Path.Combine(outDir, $"org_arena_{org.Id}.png"), Width, Height, rgba);
        }
    }
}
