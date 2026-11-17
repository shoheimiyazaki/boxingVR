using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// テレビ中継の動画の道具のうち、描画と試合に触れない部分（中継の決まり・下見の点数・台本の資料・台本の点検）。
/// </summary>
[TestClass]
public sealed class TvShowTests
{
    private const string Events = """
        {"red":"ザ・ハンマー","blue":"アキラ","event":"WVC 大会","bout":"第1試合","redProfile":"ザ・ハンマー（ボクシング）","blueProfile":"アキラ（空手）","events":[
          {"t":0,"for":"実況","type":"opening","who":"","text":"冒頭のタイトル"},
          {"t":7,"for":"実況","type":"round_start","who":"","text":"第1ラウンド開始"},
          {"t":8,"for":"解説","type":"technique","who":"アキラ","text":"アキラの狙い: ジャブ"},
          {"t":9,"for":"解説","type":"technique","who":"アキラ","text":"アキラの狙い: ストレート"},
          {"t":20,"for":"実況","type":"hit","who":"ザ・ハンマー","text":"ザ・ハンマーの重い左が、アキラの顔面に入った"},
          {"t":30,"for":"実況","type":"knockdown","who":"アキラ","text":"アキラ、ダウン"},
          {"t":47,"for":"実況","type":"round_end","who":"","text":"第1ラウンド終了"},
          {"t":53,"for":"実況","type":"replay","who":"","text":"リプレイ"},
          {"t":60,"for":"実況","type":"ko","who":"","text":"10カウントのノックアウト。ザ・ハンマーの勝ち"},
          {"t":64,"for":"実況","type":"result","who":"","text":"勝敗の画面（9 秒）"},
          {"t":73,"for":"実況","type":"end","who":"","text":"動画の終わり"}
        ]}
        """;

    [TestMethod]
    public void Rules_LongerBreak_KeepsTheSameTotalHealing()
    {
        var rules = TvShowRules.Rules(rounds: 3, breakSeconds: 20f);

        Assert.AreEqual(3, rules.Rounds);
        Assert.AreEqual(20f, rules.BreakSeconds);
        Assert.AreEqual(MatchRules.Tv.RegenVoxelsPerSecond * MatchRules.Tv.BreakSeconds, rules.RegenVoxelsPerSecond * rules.BreakSeconds, 1e-3f,
            "休みを延ばしても、休みの間に治る量（速さ × 長さ）は標準と同じ");
    }

    [TestMethod]
    public void Drama_ComebackKnockoutInTheLastRound_BeatsAQuickFirstRoundKnockout()
    {
        var comeback = new TvScoutResult(1, "K.O.", Ko: true, Round: 3, RoundTime: 20f, RedDealt: 100f, BlueDealt: 300f, RedDowns: 2, BlueDowns: 1, Winner: 1);
        var quick = new TvScoutResult(2, "K.O.", Ko: true, Round: 1, RoundTime: 6f, RedDealt: 40f, BlueDealt: 0f, RedDowns: 0, BlueDowns: 1, Winner: 1);
        var draw = new TvScoutResult(3, "DRAW", Ko: false, Round: 3, RoundTime: 30f, RedDealt: 100f, BlueDealt: 100f, RedDowns: 0, BlueDowns: 0, Winner: 0);

        Assert.IsTrue(TvShowRules.Drama(comeback, 3, 30f) > TvShowRules.Drama(draw, 3, 30f));
        Assert.IsTrue(TvShowRules.Drama(draw, 3, 30f) > TvShowRules.Drama(quick, 3, 30f));
        Assert.AreEqual(1, TvShowRules.Best([quick, draw, comeback], 3, 30f).Seed);
    }

    [TestMethod]
    public void Digest_SplitsIntoSectionsAndDropsRepeatedIntentions()
    {
        var digest = TvDigest.Build(Events);

        StringAssert.Contains(digest, "| 7.0 | 47.0 | 第1ラウンド |");
        StringAssert.Contains(digest, "| 64.0 | 73.0 | 勝敗の画面 |");
        StringAssert.Contains(digest, "- 30.0 実況: アキラ、ダウン");
        StringAssert.Contains(digest, "アキラの狙い: ジャブ");
        Assert.IsFalse(digest.Contains("アキラの狙い: ストレート", StringComparison.Ordinal), "同じ選手の狙いが 4 秒以内に続いたら落とす");
    }

    [TestMethod]
    public void Lint_FlagsMissingSubjectsLateCallsAndForbiddenWords()
    {
        const string script = """
            {"lines":[
              {"at":7.1,"speaker":"announcer","text":"ゴング！"},
              {"at":20.0,"speaker":"announcer","text":"左が入った！"},
              {"at":31.5,"speaker":"announcer","text":"ダウン！"},
              {"at":40.0,"speaker":"commentator","text":"ダメージ35の一撃です。"}
            ]}
            """;

        var rules = TvLint.Check(script, Events).Select(i => i.Rule).ToList();

        CollectionAssert.DoesNotContain(rules, "主語", "主語の決まりはやめた（2026-11-17: 喋り方を不自然に縛っていた。誰の話かは文脈で分かればよい）");
        CollectionAssert.Contains(rules, "コール", "ダウンを 1.5 秒遅れて言った");
        CollectionAssert.Contains(rules, "言わない言葉", "ダメージの数値は言わない");
        CollectionAssert.Contains(rules, "言い直し", "ラウンドの始まりに「第1ラウンド」と言っていない");
        CollectionAssert.Contains(rules, "リプレイ", "リプレイに変わったことを言っていない");
    }

    [TestMethod]
    public void Lint_AcceptsNamedCallsNicknamesAndAnswersToNamedQuestions()
    {
        const string script = """
            {"lines":[
              {"at":7.1,"speaker":"announcer","text":"第1ラウンド、ゴング！"},
              {"at":20.0,"speaker":"announcer","text":"ハンマーの左、入った！"},
              {"at":22.0,"speaker":"announcer","text":"サエキさん、アキラ選手は？"},
              {"at":24.0,"speaker":"commentator","text":"頭が揺れて、意識が飛びかけています。"},
              {"at":30.0,"speaker":"announcer","text":"ダウン！アキラ、ダウン！"},
              {"at":48.0,"speaker":"announcer","text":"サエキさん、次のラウンドは？"},
              {"at":50.0,"speaker":"commentator","text":"ハンマー選手の一発が怖い。"},
              {"at":53.2,"speaker":"announcer","text":"スローでもう一度。"},
              {"at":60.2,"speaker":"announcer","text":"レフェリーが止めた！ハンマーのKO勝ち！"}
            ]}
            """;

        var issues = TvLint.Check(script, Events);

        Assert.AreEqual(0, issues.Count, string.Join("\n", issues.Select(i => $"{i.At} {i.Rule} {i.Message}")));
    }

    /// <summary>ダウン → カウント → 10 カウントの K.O. → 決着のリプレイ → 勝敗の画面。</summary>
    private const string KnockoutEvents = """
        {"red":"ザ・ハンマー","blue":"アキラ","redDiscipline":"ボクシング","blueDiscipline":"空手","bout":"第1試合","events":[
          {"t":0,"for":"実況","type":"opening","who":"","text":"冒頭のタイトル"},
          {"t":7,"for":"実況","type":"round_start","who":"","text":"第1ラウンド開始"},
          {"t":19.5,"for":"実況","type":"hit","who":"ザ・ハンマー","text":"ザ・ハンマーの強烈な左が、アキラの顔面に入った"},
          {"t":20,"for":"実況","type":"knockdown","who":"アキラ","text":"アキラ、ダウン"},
          {"t":21,"for":"実況","type":"count","who":"アキラ","text":"カウント 1"},
          {"t":27,"for":"実況","type":"count","who":"アキラ","text":"カウント 7"},
          {"t":28,"for":"実況","type":"count","who":"アキラ","text":"カウント 8"},
          {"t":29,"for":"実況","type":"count","who":"アキラ","text":"カウント 9"},
          {"t":30,"for":"実況","type":"ko","who":"","text":"10カウントのノックアウト。ザ・ハンマーの勝ち"},
          {"t":34,"for":"実況","type":"replay","who":"","text":"決着のリプレイ（スロー）: ザ・ハンマーの強烈な左がアキラの顔面に → ダウン → K.O."},
          {"t":42,"for":"実況","type":"result","who":"","text":"勝敗の画面（9 秒）"},
          {"t":51,"for":"実況","type":"end","who":"","text":"動画の終わり"}
        ]}
        """;

    private static List<(float At, string Speaker, string Text)> Lines(string scriptJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(scriptJson);
        return [.. doc.RootElement.GetProperty("lines").EnumerateArray()
            .Select(l => ((float)l.GetProperty("at").GetDouble(), l.GetProperty("speaker").GetString() ?? "", l.GetProperty("text").GetString() ?? ""))];
    }

    [TestMethod]
    public void Draft_CallsTheKnockdownWithBothNames_CountsTheLateNumbers_AndPassesTheLint()
    {
        var draft = TvDraft.Build(KnockoutEvents);
        var lines = Lines(draft);
        var texts = lines.Select(l => l.Text).ToList();

        var call = lines.Single(l => l.Text.Contains("ダウン", StringComparison.Ordinal) && l.At is >= 20f and < 21f);
        StringAssert.Contains(call.Text, "ザ・ハンマー", "ダウンのコールには倒した側の名前も入れる（ラジオでは誰の拳か見えない）");
        StringAssert.Contains(call.Text, "アキラ");
        CollectionAssert.Contains(texts, "エイト！");
        CollectionAssert.Contains(texts, "ナイン！");
        CollectionAssert.DoesNotContain(texts, "ワン！", "カウントの前半は数えず、誰が何で倒れたかを言う時間に使う");
        CollectionAssert.Contains(texts, "テン！");
        CollectionAssert.Contains(texts, "ザ・ハンマーのKO勝ち！");
        Assert.IsTrue(lines.Any(l => l.Text.StartsWith("決着の場面", StringComparison.Ordinal) && l.At <= 36f), "決着のリプレイの入りは 2 秒以内に言う");
        Assert.IsTrue(texts.Any(t => t.StartsWith("勝者、赤コーナー、ザ・ハンマー", StringComparison.Ordinal)));
        Assert.AreEqual(draft, TvDraft.Build(KnockoutEvents), "同じ記録からは同じ下書き（乱数を使わない）");

        var issues = TvLint.Check(draft, KnockoutEvents);
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues.Select(i => $"{i.At} {i.Rule} {i.Message}")));
    }

    [TestMethod]
    public void Merge_KeepsTheSkeletonOnTime_PutsQuestionAndAnswerTogether_AndDropsWhatDoesNotFit()
    {
        var draft = TvDraft.Build(KnockoutEvents);
        const string extras = """
            {"drop":["ザ・ハンマーのKO勝ち！"],"lines":[
              {"at":10,"speaker":"announcer","text":"サエキさん、アキラ選手の作戦は？","excited":false},
              {"at":12,"speaker":"commentator","text":"アキラ選手は蹴りで足を止めたい。","excited":false},
              {"at":20.2,"speaker":"commentator","text":"アキラ選手、あごを打ち抜かれました。とても長い説明をここに入れます。","excited":false}
            ]}
            """;
        var dropped = new List<string>();

        var merged = Lines(TvDraft.Merge(draft, extras, dropped));
        var skeleton = Lines(draft).Where(l => l.Text != "ザ・ハンマーのKO勝ち！").ToList();

        foreach (var line in skeleton)
        {
            Assert.IsTrue(merged.Any(m => m.Text == line.Text && Math.Abs(m.At - line.At) < 0.05f), $"下書きの「{line.Text}」は時刻どおりに残る");
        }

        Assert.IsFalse(merged.Any(m => m.Text == "ザ・ハンマーのKO勝ち！"), "extras の drop に書いた台詞は消える");
        var question = merged.FindIndex(m => m.Text.StartsWith("サエキさん", StringComparison.Ordinal));
        Assert.IsTrue(question >= 0 && merged[question + 1].Speaker == "commentator", "問いのすぐ後に解説の答え");
        CollectionAssert.Contains(dropped, "アキラ選手、あごを打ち抜かれました。とても長い説明をここに入れます。", "ダウンのコールと重なり、2.5 秒以内に入らない台詞は落とす");
    }

    [TestMethod]
    public void Draft_NamesTheDecisionWinner_NotTheWordsBeforeTheName()
    {
        // Broadcast の判定の文は「判定でAの勝ち」（「。」で区切られていない）。以前は「判定でアキラ」を名前として読んでいた。
        const string events = """
            {"red":"アキラ","blue":"ザ・ハンマー","events":[
              {"t":7,"for":"実況","type":"round_start","who":"","text":"第1ラウンド開始"},
              {"t":37,"for":"実況","type":"round_end","who":"","text":"第1ラウンド終了"},
              {"t":37.1,"for":"実況","type":"decision","who":"","text":"判定でアキラの勝ち"},
              {"t":40,"for":"実況","type":"result","who":"","text":"勝敗の画面（9 秒）"}
            ]}
            """;

        var texts = Lines(TvDraft.Build(events)).Select(l => l.Text).ToList();

        CollectionAssert.Contains(texts, "勝者、赤コーナー、アキラ！");
        Assert.AreEqual(0, TvLint.Check(TvDraft.Build(events), events).Count(i => i.Rule == "掛け合い"), "最後のラウンドの後は休みではない（問いと答えを求めない）");
    }

    [TestMethod]
    public void Question_IsTheAnalystsNameFollowedBySan_NotAnyWordWithSan()
    {
        Assert.IsTrue(TvDraft.IsQuestion("announcer", "サエキさん、アキラ選手は？"));
        Assert.IsFalse(TvDraft.IsQuestion("announcer", "さんざん打たれたアキラ！"));
        Assert.IsFalse(TvDraft.IsQuestion("commentator", "サエキさん、どうぞ。"), "問いを振るのは実況");
    }

    [TestMethod]
    public void Merge_InnerVoiceStyle_SkipsTheSkeletonAndCarriesStyleAndCast()
    {
        const string extras = """
            {"style":"inner-voice","useDraft":false,
             "speakers":{"red_inner":{"voice":["Ichiro"],"pitch":"-10%"}},
             "lines":[{"at":20.1,"speaker":"red_inner","text":"よし、顔に入った。","excited":false}]}
            """;

        var merged = TvDraft.Merge(TvDraft.Build(KnockoutEvents), extras, []);
        using var doc = System.Text.Json.JsonDocument.Parse(merged);

        Assert.AreEqual("inner-voice", doc.RootElement.GetProperty("style").GetString());
        Assert.AreEqual("-10%", doc.RootElement.GetProperty("speakers").GetProperty("red_inner").GetProperty("pitch").GetString(), "配役（声と話し方）を台本へ渡す");
        Assert.AreEqual(1, Lines(merged).Count, "useDraft: false なら実況の骨組みを入れない");

        // 心の声には実況がいない: ダウンのコール・ラウンドの言い直し・リプレイの入りは求めない。
        var issues = TvLint.Check(merged, KnockoutEvents);
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues.Select(i => $"{i.At} {i.Rule} {i.Message}")));
        Assert.IsTrue(TvLint.Check(merged.Replace("inner-voice", "tv-duo", StringComparison.Ordinal), KnockoutEvents).Any(i => i.Rule == "コール"),
            "同じ台本でも tv-duo ならコールが無いと言われる");
    }

    [TestMethod]
    public void Duration_GrowsWithPausesAndSlowSpeech()
    {
        // 実測（2026-11-16）: 遅い心の声（rate −12%）の「相手はジン、居合か。間合いは俺が長い。」19 字が 4.8 秒。
        var slow = TvDraft.Duration("相手はジン、居合か。間合いは俺が長い。", -0.12f);
        Assert.AreEqual(4.8f, slow, 0.8f, "区切りの間と遅い話し方を数える（字だけの 0.17 秒/字では 3.2 秒と短すぎた）");
        Assert.IsTrue(TvDraft.Duration("入った！", 0.35f) < TvDraft.Duration("入った！", 0f), "速く話すと短い");
        Assert.IsTrue(TvDraft.Duration("顔が、熱い。", 0f) > TvDraft.Duration("顔が熱い", 0f), "区切りは間になる");
    }

    [TestMethod]
    public void Lint_FlagsAQuestionToTheAnalystDuringTheCount()
    {
        const string script = """
            {"lines":[
              {"at":7.1,"speaker":"announcer","text":"第1ラウンド、ゴング！"},
              {"at":20.0,"speaker":"announcer","text":"ザ・ハンマーの左！アキラ、ダウン！"},
              {"at":23.0,"speaker":"announcer","text":"サエキさん、アキラは立てますか。"},
              {"at":30.0,"speaker":"announcer","text":"テン！ザ・ハンマーのKO勝ち！"},
              {"at":34.1,"speaker":"announcer","text":"決着の場面。"}
            ]}
            """;

        var issues = TvLint.Check(script, KnockoutEvents);

        Assert.AreEqual(1, issues.Count(i => i.Rule == "カウント中"), "カウントの最中は解説に問いを振らない");
    }

    private const string TournamentProgram = """
        {"title":"WVC ボクシングトーナメント","bouts":[
          {"bout":"準決勝第1試合","red":"marcus","blue":"ramon","winner":"red","method":"K.O.","stake":"勝者は決勝へ","next":"決勝進出"},
          {"bout":"準決勝第2試合","red":"yoandry","blue":"dmitri","winner":"blue","method":"判定","stake":"勝者は決勝へ","next":"決勝進出"},
          {"bout":"決勝","red":"marcus","blue":"dmitri","current":true,"stake":"勝てば優勝","next":"優勝"}
        ]}
        """;

    [TestMethod]
    public void Program_ResolvesRosterIds_KeepsPlaceholders_AndRoundTripsThroughTheEventsRecord()
    {
        var program = TvProgram.FromJson(TournamentProgram, TvProgram.RosterName);

        Assert.AreEqual("ザ・ハンマー", program.Bouts[0].Red, "名簿の id は画面の短い名前に");
        Assert.AreEqual("ザ・ハンマー", program.Bouts[0].WinnerName);
        Assert.AreEqual(program.Bouts[1].Blue, program.Bouts[1].WinnerName);
        Assert.AreEqual("決勝", program.Current?.Bout);
        Assert.AreEqual(2, program.Finished.Count());
        Assert.IsTrue(program.Seconds > TvProgram.FromJson(TournamentProgram.Replace("\"winner\":\"red\",", "", StringComparison.Ordinal), TvProgram.RosterName).Seconds,
            "済んだ試合が多いほど、実況が結果を言い切れるよう表を長く出す");

        var placeholder = TvProgram.FromJson("""{"title":"T","bouts":[{"bout":"決勝","red":"準決勝第1試合の勝者","blue":"marcus","current":true}]}""", TvProgram.RosterName);
        Assert.AreEqual("準決勝第1試合の勝者", placeholder.Bouts[0].Red, "名簿の id でなければ書いたまま");

        // 中継の記録（Broadcast は既定の名前の付け方で書く）から読み戻しても同じ表。
        var written = System.Text.Json.JsonSerializer.Serialize(program);
        var reread = TvProgram.FromJson(written, _ => null);
        CollectionAssert.AreEqual(program.Bouts.ToList(), reread.Bouts.ToList());

        Assert.ThrowsExactly<InvalidDataException>(() => TvProgram.FromJson(TournamentProgram.Replace("\"winner\":\"red\"", "\"winner\":\"marcus\"", StringComparison.Ordinal), _ => null));
        Assert.ThrowsExactly<InvalidDataException>(() => TvProgram.FromJson(TournamentProgram.Replace("\"winner\":\"red\",", "\"current\":true,", StringComparison.Ordinal), _ => null),
            "今の試合は 1 つだけ");
    }

    [TestMethod]
    public void Draft_WithAProgram_ReadsPastResultsAndTheStake_ThenIntroducesAfterTheTable_AndCallsTheAdvance()
    {
        var program = TvProgram.FromJson(TournamentProgram, TvProgram.RosterName);
        var opening = program.Seconds;
        var events = $$"""
            {"red":"ザ・ハンマー","blue":"ドミトリー","redDiscipline":"ボクシング","blueDiscipline":"ボクシング","bout":"決勝",
             "program":{{System.Text.Json.JsonSerializer.Serialize(program)}},"events":[
              {"t":0,"for":"実況","type":"program","who":"","text":"本日のプログラムの表"},
              {"t":{{opening.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"for":"実況","type":"opening","who":"","text":"冒頭のタイトル"},
              {"t":{{(opening + 7f).ToString(System.Globalization.CultureInfo.InvariantCulture)}},"for":"実況","type":"round_start","who":"","text":"第1ラウンド開始"},
              {"t":40,"for":"実況","type":"decision","who":"","text":"判定でザ・ハンマーの勝ち"},
              {"t":44,"for":"実況","type":"result","who":"","text":"勝敗の画面（9 秒）"},
              {"t":53,"for":"実況","type":"end","who":"","text":"動画の終わり"}
            ]}
            """;

        var draft = TvDraft.Build(events);
        var lines = Lines(draft);
        var texts = lines.Select(l => l.Text).ToList();

        CollectionAssert.Contains(texts, "本日のプログラムです。");
        CollectionAssert.Contains(texts, "準決勝第1試合はザ・ハンマーのKO勝ち。");
        Assert.IsTrue(texts.Any(t => t.StartsWith("準決勝第2試合は", StringComparison.Ordinal) && t.EndsWith("の判定勝ち。", StringComparison.Ordinal)));
        CollectionAssert.Contains(texts, "この決勝、勝てば優勝。");
        var programLines = lines.Where(l => l.Text.Contains("試合は", StringComparison.Ordinal) || l.Text.Contains("本日", StringComparison.Ordinal) || l.Text.StartsWith("この決勝", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(4, programLines.Count);
        Assert.IsTrue(programLines.All(l => l.At + TvDraft.Duration(l.Text, 0.15f) < opening + 0.5f), string.Join(" / ", programLines.Select(l => $"{l.At:0.0}+{TvDraft.Duration(l.Text, 0.15f):0.0} {l.Text}")) + $" opening {opening} — プログラムの台詞は表が出ている間に言い切る");
        Assert.IsTrue(lines.Single(l => l.Text.StartsWith("決勝。赤、", StringComparison.Ordinal)).At >= opening, "選手紹介は両コーナーの板が出てから");
        Assert.IsTrue(lines.Any(l => l.Text == "ザ・ハンマー、優勝！" && l.At > 44f), "勝者のコールの後に、勝って得たものを言う");

        var issues = TvLint.Check(draft, events);
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues.Select(i => $"{i.At} {i.Rule} {i.Message}")));
    }

    [TestMethod]
    public void ProgramBoard_ShowsEveryBout_TheResults_AndMarksThisBout()
    {
        var program = TvProgram.FromJson(TournamentProgram, TvProgram.RosterName);

        var (text, shapes) = new TvShowGraphics(1280, 720).Program(program, 4f);
        var words = text.Select(t => t.Text).ToList();

        CollectionAssert.Contains(words, "本日のプログラム");
        CollectionAssert.Contains(words, "準決勝第1試合");
        CollectionAssert.Contains(words, "ザ・ハンマー 勝ち（K.O.）");
        CollectionAssert.Contains(words, "この試合");
        Assert.IsTrue(words.Any(w => w.Contains("勝てば優勝", StringComparison.Ordinal)));
        Assert.IsTrue(shapes.Count > 0);
        Assert.IsTrue(text.All(t => t.Color.W <= 1f && t.Color.W >= 0f));
    }

    private const string TwoRoundEvents = """
        {"red":"ザ・ハンマー","blue":"ドミトリー","redDiscipline":"ボクシング","blueDiscipline":"ボクシング","bout":"決勝","events":[
          {"t":0,"for":"実況","type":"opening","who":"","text":"冒頭のタイトル"},
          {"t":7,"for":"実況","type":"round_start","who":"","text":"第1ラウンド開始"},
          {"t":15,"for":"解説","type":"tired","who":"ザ・ハンマー","text":"ザ・ハンマーはスタミナが尽きかけている。手数が落ち、パンチが軽くなる"},
          {"t":25,"for":"解説","type":"tired","who":"ザ・ハンマー","text":"ザ・ハンマーはスタミナが尽きかけている。手数が落ち、パンチが軽くなる"},
          {"t":37,"for":"実況","type":"round_end","who":"","text":"第1ラウンド終了"},
          {"t":37.5,"for":"解説","type":"score","who":"","text":"ここまではザ・ハンマーがドミトリーを大きく上回っている"},
          {"t":37.5,"for":"解説","type":"body","who":"ザ・ハンマー","text":"ザ・ハンマー: 左目が腫れて見えにくい。スタミナはほぼ尽きている"},
          {"t":37.5,"for":"解説","type":"body","who":"ドミトリー","text":"ドミトリー: 大きな傷はない。疲れが見える"},
          {"t":43,"for":"実況","type":"replay","who":"","text":"リプレイ（第1ラウンド、スロー）: ドミトリーの重い左がザ・ハンマーのこめかみに"},
          {"t":51,"for":"実況","type":"replay","who":"","text":"リプレイ（第1ラウンド、スロー）: ザ・ハンマーのいい右がドミトリーのみぞおちに"},
          {"t":70.3,"for":"実況","type":"round_start","who":"","text":"第2ラウンド開始"},
          {"t":77.8,"for":"実況","type":"hit","who":"ドミトリー","text":"ドミトリーの強烈な左が、ザ・ハンマーのこめかみに入った"},
          {"t":78.3,"for":"実況","type":"knockdown","who":"ザ・ハンマー","text":"ザ・ハンマー、ダウン"},
          {"t":78.3,"for":"解説","type":"knockdown","who":"ザ・ハンマー","text":"ザ・ハンマーは頭を揺らされて、意識が飛んでいる"},
          {"t":88.3,"for":"実況","type":"ko","who":"","text":"10カウントのノックアウト。ドミトリーの勝ち"},
          {"t":92.3,"for":"実況","type":"replay","who":"","text":"決着のリプレイ（スロー）: ドミトリーの強烈な左がザ・ハンマーのこめかみに → ダウン → K.O."},
          {"t":100.3,"for":"実況","type":"result","who":"","text":"勝敗の画面（9 秒）"},
          {"t":109.3,"for":"実況","type":"end","who":"","text":"動画の終わり"}
        ]}
        """;

    [TestMethod]
    public void BroadcastDraft_IsTwoPeople_AsksTheAnalystInTheBreak_AndPassesTheLint()
    {
        var script = TvDraft.BuildBroadcast(TwoRoundEvents);
        var lines = Lines(script);
        foreach (var l in lines)
        {
            Console.WriteLine($"{l.At,5:0.0} {l.Speaker,-11} {l.Text}");
        }

        Assert.IsTrue(lines.Any(l => l.Speaker == "commentator"), "標準は実況と解説の 2 人");
        var question = lines.Single(l => l.Text == "サエキさん、ザ・ハンマーの様子は？");
        Assert.IsTrue(question.At is > 37f and < 70.3f, "休みの間に問う（傷のある方の選手を）");
        var answer = lines.First(l => l.At > question.At);
        Assert.AreEqual("commentator", answer.Speaker, "問いのすぐ後に解説が答える");
        StringAssert.Contains(answer.Text, "左目");
        Assert.AreEqual("肺に効いています。", TvAnalyst.Answer("ヨアンドリ: 肺に効いている。スタミナはまだある")?.Split('。')[0] + "。", "記録の文を話し言葉にする");
        Assert.IsTrue(lines.Any(l => l.Speaker == "commentator" && l.Text.Contains("こめかみで", StringComparison.Ordinal) || l.Speaker == "commentator" && l.Text.Contains("みぞおちで", StringComparison.Ordinal)), "リプレイの後に、なぜ効くか");
        Assert.AreEqual(1, lines.Count(l => l.Text.StartsWith("ザ・ハンマーはスタミナが尽きかけて", StringComparison.Ordinal)), "疲れの見立てはラウンドに 1 回まで");
        Assert.IsTrue(lines.All(l => l.Text.Length <= 28), "1 台詞 28 字まで");
        Assert.AreEqual(script, TvDraft.BuildBroadcast(TwoRoundEvents), "同じ記録からは同じ台本（乱数を使わない）");

        var issues = TvLint.Check(script, TwoRoundEvents);
        Assert.AreEqual(0, issues.Count, string.Join("\n", issues.Select(i => $"{i.At} {i.Rule} {i.Message}")));
    }
}
