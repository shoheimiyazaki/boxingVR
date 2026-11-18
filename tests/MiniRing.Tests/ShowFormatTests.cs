using System.Numerics;
using System.Text.Json;
using Aigis.Foundation;
using Aigis.Render;
using MiniRing.Arena;
using MiniRing.Audio;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 団体ごとの番組の型（<see cref="ITvShow"/>、<c>games/MiniRing.Arena/Shows</c>）: 団体のファイルの <c>show</c> で型が選ばれること、
/// VFC の試合の前の場面の並びと長さ、WVC の番組が今までの描画（<see cref="TvShowGraphics"/>）そのままであること、
/// VFC の画面が WVC の言葉・部品を使わないこと、体の図が見えている傷だけから描かれること、記録の試合の前の場面の印、放送席の声。
/// PNG は <c>.visualcheck/show_*.png</c>（目で見る確認用）。
/// </summary>
[TestClass]
public sealed class ShowFormatTests
{
    private const int Width = 1280;
    private const int Height = 720;

    private static string OutDir()
    {
        var dir = Path.Combine(OrganizationTests.RepoRoot(), ".visualcheck");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static TvCard Card(string red = "jin", string blue = "konrad")
        => new("VFC ファイトナイト", "第1試合", TvCorner.Of(CharacterRoster.Get(red)), TvCorner.Of(CharacterRoster.Get(blue)), TvShowRules.Rules(5, 25f, Organization.Of("vfc")));

    private static TvProgram Program()
        => TvProgram.FromJson("""{"title":"VFC トーナメント","bouts":[{"bout":"準決勝第1試合","red":"jin","blue":"konrad","winner":"red","method":"K.O.","cause":"首を断たれて"},{"bout":"決勝","red":"jin","blue":"marcus","current":true,"stake":"勝者が王者"}]}""", TvProgram.RosterName);

    [TestMethod]
    public void Format_IsChosenByTheOrganizationFile()
    {
        Assert.AreEqual("wvc", ShowFormats.Create(Width, Height, Organization.Of("wvc")).Id);
        Assert.AreEqual("vfc", ShowFormats.Create(Width, Height, Organization.Of("vfc")).Id);
        Assert.AreEqual("wvc", Organization.Of("wvc").Show);
        Assert.AreEqual("vfc", Organization.Of("vfc").Show);

        // 知らない型の id は、ファイルと欄を名指しして止める。
        var good = File.ReadAllText(Path.Combine(OrganizationTests.OrgsDirectory(), "wvc.json"));
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => Organizations.FromFiles([("wvc.json", good.Replace("\"show\": \"wvc\"", "\"show\": \"cable\"", StringComparison.Ordinal))]));
        Assert.Contains("show", ex.Message);
    }

    [TestMethod]
    public void Wvc_PreFight_IsTheLegacyOpening()
    {
        var show = ShowFormats.Create(Width, Height, Organization.Of("wvc"));
        var card = new TvCard("ファイトナイト", "第1試合", TvCorner.Of(CharacterRoster.Get("marcus")), TvCorner.Of(CharacterRoster.Get("yoandry")), TvShowRules.Rules(5, 25f));
        var plain = show.PreFight(card, null, hasProfiles: false);
        const float Calls = 2 * MiniRing.Arena.Shows.Wvc.WvcShowParts.CallSeconds;
        Assert.AreEqual("opening,call,call", string.Join(",", plain.Select(s => s.EventType)));
        Assert.AreEqual(TvShowGraphics.OpeningSeconds + Calls, plain.Sum(s => s.Seconds), 1e-4f);

        var full = show.PreFight(card, Program(), hasProfiles: true);
        Assert.AreEqual("program,opening,tape,call,call", string.Join(",", full.Select(s => s.EventType)));
        Assert.AreEqual(Program().Seconds + TvShowGraphics.OpeningSeconds + TvShowGraphics.TapeSeconds + Calls, full.Sum(s => s.Seconds), 1e-4f);
        Assert.IsTrue(full.Take(3).All(s => s.Backdrop == ShowBackdrop.Wide && s.StingAt is null), "WVC の冒頭は引きの絵のまま・音は足さない");
        Assert.AreEqual(ShowBackdrop.RedLow, full[3].Backdrop, "赤のコールは赤コーナーの選手を映す");
        Assert.AreEqual(ShowBackdrop.BlueLow, full[4].Backdrop);
        Assert.IsTrue(full.Skip(3).All(s => s.Stage == StagePose.Corners && s.Description.Contains("リングアナウンサー", StringComparison.Ordinal)), "コールの間、選手は自分のコーナーで構える。記録の画面の説明");

        // 描画は今までの TvShowGraphics そのまま。
        var graphics = new TvShowGraphics(Width, Height, Organization.Of("wvc"));
        var (text, shapes) = show.Scene(full[1], card, null, 4f);
        var (legacyText, legacyShapes) = graphics.Opening(card, 4f);
        CollectionAssert.AreEqual(legacyText.ToList(), text.ToList());
        CollectionAssert.AreEqual(legacyShapes.ToList(), shapes.ToList());
        var result = new TvResult(1, "K.O. — BRAIN DESTROYED  BLUE WINS", 4, 18f, [100f, 120f], [90f, 100f]);
        using var game = new BoxingGame(seed: 3);
        var wvcResult = show.Result(card, result, 3.5f, game, ShowBodies.None);
        var (legacyResultText, legacyResultShapes) = graphics.Result(card, result, 3.5f);
        CollectionAssert.AreEqual(legacyResultText.ToList(), wvcResult.Text.ToList());
        CollectionAssert.AreEqual(legacyResultShapes.ToList(), wvcResult.Shapes.ToList());
        Assert.AreEqual(MiniRing.Arena.Shows.Wvc.WvcShowParts.RoundCardSeconds, show.IntervalBoardSeconds, "WVC の休みの最初に採点の札");
        Assert.AreEqual(0, show.Interval(game, "A", "B", ShowBodies.None, 2f, []).Shapes.Count, "終わったラウンドが無ければ札は出ない");
    }

    [TestMethod]
    public void Vfc_PreFight_IsColdOpenPosterWalkoutsFaceOff_AndFightCardWhenAProgramExists()
    {
        var show = ShowFormats.Create(Width, Height, Organization.Of("vfc"));
        var card = Card();
        var scenes = show.PreFight(card, null, hasProfiles: true);
        Assert.AreEqual("cold_open,poster,walkout_red,walkout_blue,faceoff", string.Join(",", scenes.Select(s => s.Id)));
        Assert.AreEqual("cold_open,poster,walkout,walkout,faceoff", string.Join(",", scenes.Select(s => s.EventType)));
        var total = scenes.Sum(s => s.Seconds);
        Assert.IsTrue(total is >= 22f and <= 26f, $"試合の前は 22〜26 秒: {total}");
        Assert.AreEqual(ShowBackdrop.CageOrbit, scenes[0].Backdrop);
        Assert.AreEqual(VfcShow.EmblemStrikeAt, scenes[0].StingAt, "紋章はホーンと同時に入る");
        Assert.AreEqual(ShowBackdrop.RedLow, scenes[2].Backdrop);
        Assert.AreEqual(ShowBackdrop.BlueLow, scenes[3].Backdrop);
        Assert.AreEqual(ShowBackdrop.FaceOff, scenes[4].Backdrop);
        Assert.AreEqual(card.Red.Name, scenes[2].Who);
        Assert.AreEqual(card.Blue.Name, scenes[3].Who);
        Assert.IsTrue(scenes.All(s => s.Description.Contains($"{s.Seconds:0} 秒", StringComparison.Ordinal)), "記録の説明に秒数（台本を書く側が場面の長さを知る）");
        Assert.IsTrue(scenes[2].Description.Contains("身長", StringComparison.Ordinal) && scenes[2].Description.Contains("戦績", StringComparison.Ordinal), "入場の帯に出る事実が記録に書かれる");

        var withProgram = show.PreFight(card, Program(), hasProfiles: true);
        Assert.AreEqual("fight_card", withProgram[0].EventType);
        Assert.IsTrue(withProgram[0].Description.Contains("首を断たれて", StringComparison.Ordinal), "済んだ試合の決着の言葉");
        Assert.AreEqual(Program().Seconds + total, withProgram.Sum(s => s.Seconds), 1e-4f);
    }

    [TestMethod]
    public void Vfc_Screens_DoNotUseWvcWordsOrParts()
    {
        var show = ShowFormats.Create(Width, Height, Organization.Of("vfc"));
        var card = Card();
        var program = Program();
        using var game = new BoxingGame(seed: 3, rules: TvShowRules.Rules(5, 25f, Organization.Of("vfc")));
        var forbidden = new[] { "本日のプログラム", "選手の比較", "与えたダメージ", "試合終了", "LIVE", "REPLAY", "BREAK" };
        var draws = new List<ShowDraw>();
        foreach (var scene in show.PreFight(card, program, hasProfiles: true))
        {
            foreach (var t in new[] { 0.3f, scene.Seconds * 0.5f, scene.Seconds - 0.1f })
            {
                var draw = show.Scene(scene, card, program, t);
                Assert.IsTrue(draw.Shapes.Count > 0, $"{scene.Id} {t}: 図形が出る");
                draws.Add(draw);
            }
        }

        var panel = ShowFormats.Create(Width, Height, Organization.Of("vfc")).Arrange(FaceCam.Default, Width, Height).Panel(new Vector2(Width, Height), FaceSide.Left);
        draws.Add(show.MatchHud(game, panel, panel, "ジン", "コンラート", ShowBodies.None));
        draws.Add(show.ReplayOverlay(2, "ジンの刀がコンラートの右腕へ", 1f));
        draws.Add(show.Interval(game, "ジン", "コンラート", ShowBodies.None, 2f, []));
        var death = new TvResult(1, "K.O. — NECK DESTROYED  RED WINS", 2, 16f, [], []);
        draws.Add(show.Result(card, death, 4f, game, ShowBodies.None));
        foreach (var text in draws.SelectMany(d => d.Text.Select(t => t.Text)))
        {
            foreach (var word in forbidden)
            {
                Assert.IsFalse(text.Contains(word, StringComparison.Ordinal), $"VFC の画面に WVC の言葉 '{word}': {text}");
            }
        }

        Assert.IsTrue(draws.Last().Text.Any(t => t.Text == "首を断たれて"), "決着の画面に死因の言葉");
        Assert.IsTrue(draws[^4].Text.Any(t => t.Text == Organization.Of("vfc").Text.LiveTag) || draws[^4].Shapes.Count > 50, "試合中の札（PPV）");
    }

    [TestMethod]
    public void TvResult_DeathPhrase_PutsTheDeathInWords()
    {
        Assert.AreEqual("首を断たれて", new TvResult(1, "K.O. — NECK DESTROYED  RED WINS", 1, 1f, [], []).DeathPhrase);
        Assert.AreEqual("脳を壊されて", new TvResult(1, "K.O. — BRAIN DESTROYED  RED WINS", 1, 1f, [], []).DeathPhrase);
        Assert.AreEqual("心臓を壊されて", new TvResult(-1, "K.O. — HEART DESTROYED  BLUE WINS", 1, 1f, [], []).DeathPhrase);
        Assert.AreEqual("", new TvResult(1, "10 COUNT K.O.", 1, 1f, [], []).DeathPhrase, "死でない決着は言わない");
        Assert.AreEqual("", new TvResult(0, "DRAW", 5, 30f, [], []).DeathPhrase);
    }

    [TestMethod]
    public void BodyFigure_ShowsOnlyWhatIsVisible()
    {
        using var game = new BoxingGame(seed: 3);
        var fighter = game.Player;
        var p = fighter.Parts;
        // 折れた・切れた部位は記録（Broadcast）が覚えている物を読む。剛体の Left は本人の右。
        var bodies = new ShowBodies((f, body) => f != fighter ? null : body == p.LeftForearm ? "骨折" : body == p.RightUpperArm ? "切断" : body == p.LeftThigh ? "骨折" : null);
        var figure = bodies.Of(fighter);
        Assert.IsTrue(figure[FigurePart.RightForearm].Broken, "本人の右前腕が折れている");
        Assert.IsTrue(figure[FigurePart.LeftUpperArm].Gone && figure[FigurePart.LeftForearm].Gone, "切れた腕は先も無い");
        Assert.IsFalse(figure[FigurePart.LeftThigh].Broken);
        Assert.IsFalse(bodies.Of(game.Cpu).Unharmed is false, "相手は無傷");

        var wounds = figure.Wounds();
        CollectionAssert.Contains(wounds.ToList(), "左上腕から先を失った");
        CollectionAssert.Contains(wounds.ToList(), "右前腕 骨折");
        CollectionAssert.Contains(wounds.ToList(), "右太もも 骨折");
        Assert.IsFalse(wounds.Any(w => w.Contains("左前腕", StringComparison.Ordinal)), "切れた腕の先を重ねて言わない");
        Assert.IsTrue(wounds.Count == 3, string.Join("/", wounds));
        Assert.AreEqual(0, ShowBodies.None.Of(game.Cpu).Wounds().Count);

        var eyes = new BodyFigure(new Dictionary<FigurePart, FigurePartState>(), 1f, 0f, dead: false);
        Assert.AreEqual("右目が潰れた", string.Join(",", eyes.Wounds()));
        var carved = new BodyFigure(new Dictionary<FigurePart, FigurePartState> { [FigurePart.Chest] = new(0.5f, false, false), [FigurePart.Head] = new(0.1f, false, false) }, 1f, 1f, dead: false);
        Assert.AreEqual("胸 肉が半分削れた", string.Join(",", carved.Wounds()), "削れが小さい部位は言わない");
    }

    [TestMethod]
    public void BroadcastNoteScene_MarksPrefightEvents()
    {
        var broadcast = new Broadcast("ジン", "コンラート");
        broadcast.NoteScene(0f, "cold_open", "", "コールドオープン（5 秒）");
        broadcast.Note(3f, "camera", "", "カメラ");
        var path = Path.Combine(Path.GetTempPath(), $"scene-{Guid.NewGuid():N}.events.json");
        try
        {
            broadcast.WriteEvents(path);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var events = doc.RootElement.GetProperty("events").EnumerateArray().ToList();
            Assert.IsTrue(events[0].TryGetProperty("prefight", out var mark) && mark.GetBoolean());
            Assert.IsFalse(events[1].TryGetProperty("prefight", out _), "試合の前の場面だけに印");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Digest_And_Draft_HandleFormatSpecificScenes()
    {
        const string Json = """
            {"red":"ジン","blue":"コンラート","redDiscipline":"日本刀","blueDiscipline":"ロングソード","bout":"第1試合","org":"vfc","events":[
              {"t":0,"for":"実況","type":"cold_open","who":"","text":"コールドオープン（5 秒）: 檻","prefight":true},
              {"t":5,"for":"実況","type":"poster","who":"","text":"ファイトポスター（6 秒）: 2 人","prefight":true},
              {"t":11,"for":"実況","type":"walkout","who":"ジン","text":"入場（赤コーナー、5 秒）: ジン","prefight":true},
              {"t":16,"for":"実況","type":"walkout","who":"コンラート","text":"入場（青コーナー、5 秒）: コンラート","prefight":true},
              {"t":21,"for":"実況","type":"faceoff","who":"","text":"フェイスオフ（4 秒）: 顔","prefight":true},
              {"t":25,"for":"実況","type":"round_start","who":"","text":"第1ラウンド開始"},
              {"t":60,"for":"実況","type":"result","who":"","text":"勝敗の画面（9 秒）"},
              {"t":69,"for":"実況","type":"end","who":"","text":"動画の終わり"}
            ]}
            """;
        var digest = TvDigest.Build(Json);
        foreach (var title in new[] { "コールドオープン（5 秒）", "ファイトポスター（6 秒）", "入場（赤コーナー、5 秒）", "入場（青コーナー、5 秒）", "フェイスオフ（4 秒）" })
        {
            Assert.IsTrue(digest.Contains(title, StringComparison.Ordinal), $"台本の資料に場面 {title}");
        }

        var draft = TvDraft.Build(Json);
        using var doc = JsonDocument.Parse(draft);
        var lines = doc.RootElement.GetProperty("lines").EnumerateArray().Select(l => (At: l.GetProperty("at").GetDouble(), Text: l.GetProperty("text").GetString() ?? "")).ToList();
        var red = lines.First(l => l.Text.Contains("赤、", StringComparison.Ordinal));
        var blue = lines.First(l => l.Text.Contains("青、", StringComparison.Ordinal));
        Assert.AreEqual(11.3, red.At, 0.01, "赤の紹介は赤の入場の頭");
        Assert.AreEqual(16.3, blue.At, 0.01, "青の紹介は青の入場の頭");
    }

    [TestMethod]
    public void Cast_WvcKeepsTheLegacyVoices_VfcSoundsDifferent()
    {
        var wvc = Organization.Of("wvc").Cast;
        // WVC の声は今までの立花・佐伯（Synthesize.ps1 の既定）と同じ値。
        Assert.AreEqual("立花", wvc.Announcer.Name);
        Assert.AreEqual("佐伯", wvc.Commentator.Name);
        Assert.AreEqual("+30%", wvc.Announcer.Voice.Rate);
        Assert.AreEqual("+5%", wvc.Announcer.Voice.Pitch);
        Assert.AreEqual("+50%", wvc.Announcer.Voice.Excited.Rate);
        Assert.AreEqual("+15%", wvc.Commentator.Voice.Rate);
        Assert.AreEqual("-2%", wvc.Commentator.Voice.Pitch);
        var script = File.ReadAllText(Path.Combine(OrganizationTests.RepoRoot(), "tools", "commentary", "Synthesize.ps1"));
        Assert.IsTrue(script.Contains("announcer          = @{ rate = '+30%'; pitch = '+5%';  volume = '+0%' }", StringComparison.Ordinal), "WVC の値の元（Synthesize.ps1 の既定）が変わっていない");

        var vfc = Organization.Of("vfc").Cast;
        Assert.AreNotEqual(wvc.Announcer.Name, vfc.Announcer.Name);
        Assert.AreNotEqual(wvc.Commentator.Name, vfc.Commentator.Name);
        Assert.AreNotEqual(wvc.Announcer.Voice.Pitch, vfc.Announcer.Voice.Pitch, "実況の声の高さが違う");
        CollectionAssert.AreNotEqual(wvc.Commentator.Voice.Voice.ToList(), vfc.Commentator.Voice.Voice.ToList(), "解説は別の声");

        // 人物の説明のファイルがある。LLM に渡す物の軸（口ぐせ・言い回しの例を書かない）。
        foreach (var seat in new[] { wvc.Announcer, wvc.Commentator, vfc.Announcer, vfc.Commentator })
        {
            Assert.IsTrue(File.Exists(Path.Combine(OrganizationTests.RepoRoot(), seat.Persona.Replace('/', Path.DirectorySeparatorChar))), seat.Persona);
        }

        foreach (var seat in new[] { vfc.Announcer, vfc.Commentator })
        {
            var persona = File.ReadAllText(Path.Combine(OrganizationTests.RepoRoot(), seat.Persona.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var banned in new[] { "口ぐせ", "口癖", "## 例", "言え" })
            {
                Assert.IsFalse(persona.Contains(banned, StringComparison.Ordinal), $"{seat.Persona} に '{banned}'（誘導）");
            }
        }
    }

    [TestMethod]
    public void Scenes_RenderToPng_ForBothOrganizations()
    {
        var outDir = OutDir();
        using var renderer = new HeadlessRenderer(Width, Height);
        var back = new Vector4(0.2f, 0.19f, 0.2f, 1f);
        void Save(string name, ShowDraw draw) => PngWriter.WriteRgba(Path.Combine(outDir, $"show_{name}.png"), Width, Height, renderer.RenderPrimitiveScene(back, Matrix4x4.Identity, [], [], draw.Text, shapes: draw.Shapes));

        var vfc = Organization.Of("vfc");
        var show = ShowFormats.Create(Width, Height, vfc);
        var card = Card();
        var program = Program();
        var scenes = show.PreFight(card, program, hasProfiles: true);
        Save("vfc_fightcard", show.Scene(scenes[0], card, program, 4.5f));
        Save("vfc_cold_open_1", show.Scene(scenes[1], card, program, 1.8f));
        Save("vfc_cold_open_2", show.Scene(scenes[1], card, program, 3.0f));
        Save("vfc_cold_open_3", show.Scene(scenes[1], card, program, 3.5f));
        Save("vfc_poster", show.Scene(scenes[2], card, program, 3.5f));
        Save("vfc_walkout_red", show.Scene(scenes[3], card, program, 3.5f));
        Save("vfc_walkout_blue", show.Scene(scenes[4], card, program, 3.5f));
        Save("vfc_faceoff", show.Scene(scenes[5], card, program, 2.5f));

        using var game = new BoxingGame(seed: 3, rules: TvShowRules.Rules(5, 25f, vfc));
        var p = game.Player.Parts;
        var bodies = new ShowBodies((f, body) => f == game.Player && body == p.LeftForearm ? "骨折" : f == game.Player && body == p.RightUpperArm ? "切断" : f == game.Cpu && body == p.LeftShin ? "骨折" : null);
        var cam = show.Arrange(FaceCam.Default, Width, Height);
        var left = cam.Panel(new Vector2(Width, Height), FaceSide.Left);
        var right = cam.Panel(new Vector2(Width, Height), FaceSide.Right);
        Save("vfc_match", show.MatchHud(game, left, right, "ジン", "コンラート", bodies));
        Save("vfc_interval", show.Interval(game, "ジン", "コンラート", bodies, 3f, []));
        var replay = show.ReplayOverlay(2, "ジンの刀がコンラートの右腕へ → 骨が折れた", 0.4f);
        Save("vfc_replay", new ShowDraw(replay.Text, [.. replay.Shapes, .. show.Wipe(0.5f)]));
        var death = new TvResult(1, "K.O. — NECK DESTROYED  RED WINS", 2, 16f, [], []);
        Save("vfc_result_death", show.Result(card, death, 4.5f, game, bodies));
        Save("vfc_result_draw", show.Result(card, new TvResult(0, "DRAW", 5, 30f, [], []), 4.5f, game, bodies));

        var wvc = ShowFormats.Create(Width, Height, Organization.Of("wvc"));
        var wvcCard = new TvCard("ファイトナイト", "第1試合", TvCorner.Of(CharacterRoster.Get("marcus")), TvCorner.Of(CharacterRoster.Get("yoandry")), TvShowRules.Rules(5, 25f));
        var wvcScenes = wvc.PreFight(wvcCard, program, hasProfiles: true);
        Save("wvc_program", wvc.Scene(wvcScenes[0], wvcCard, program, 4f));
        Save("wvc_opening", wvc.Scene(wvcScenes[1], wvcCard, program, 4f));
        Save("wvc_tape", wvc.Scene(wvcScenes[2], wvcCard, program, 6f));
        Save("wvc_call_red", wvc.Scene(wvcScenes[3], wvcCard, program, 4f));
        Save("wvc_call_blue", wvc.Scene(wvcScenes[4], wvcCard, program, 4f));
        using var wvcGame = new BoxingGame(seed: 3, rules: TvShowRules.Rules(5, 25f));
        var wvcCam = wvc.Arrange(FaceCam.Default, Width, Height);
        Save("wvc_match", wvc.MatchHud(wvcGame, wvcCam.Panel(new Vector2(Width, Height), FaceSide.Left), wvcCam.Panel(new Vector2(Width, Height), FaceSide.Right), "マーカス", "ヨアンドリー", ShowBodies.None));
        var wvcReplay = wvc.ReplayOverlay(2, "マーカスの右がヨアンドリーのこめかみに", 0.4f);
        Save("wvc_replay", new ShowDraw(wvcReplay.Text, [.. wvcReplay.Shapes, .. wvc.Wipe(0.5f)]));
        Save("wvc_result", wvc.Result(wvcCard, new TvResult(1, "K.O. — BRAIN DESTROYED  RED WINS", 2, 16f, [100f, 120f], [90f, 100f]), 4f, wvcGame, ShowBodies.None));
        Save("wvc_result_belt", wvc.Result(wvcCard with { Belt = true }, new TvResult(1, "K.O. — BRAIN DESTROYED  RED WINS", 2, 16f, [100f, 120f], [90f, 100f]), 4f, wvcGame, ShowBodies.None));
        Save("wvc_round_card", wvc.Interval(wvcGame, "マーカス", "ヨアンドリー", ShowBodies.None, 4f,
            [new MiniRing.Arena.Shows.Wvc.WvcRoundTally(112f, 80f, 14, 9), new MiniRing.Arena.Shows.Wvc.WvcRoundTally(60f, 131f, 7, 16)]));
    }
}
