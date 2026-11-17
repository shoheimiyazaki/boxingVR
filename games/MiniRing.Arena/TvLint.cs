using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniRing.Arena;

/// <summary>台本の点検で見つかった 1 件。</summary>
/// <param name="At">台詞（または出来事）の時刻 [s]。</param>
/// <param name="Rule">決まりの名前（docs/TV_BROADCAST.md の「台本の決まり」）。</param>
/// <param name="Message">何がいけないか。</param>
internal sealed record TvLintIssue(float At, string Rule, string Message);

/// <summary>
/// 実況・解説の台本（<c>*.script.json</c>）の機械で確かめられる点検。ラジオで聞いても分かる台本の決まり（docs/TV_BROADCAST.md）のうち、
/// 文字と時刻で判定できるもの: 主語（選手の名前）の抜け・長すぎる台詞・中継で言わない言葉・ラウンドの頭の状況の言い直し・
/// 実況から解説への問いかけ・ダウンと決着のコールの遅れ・声を入れた後のずれ。聞いて分かるかの最終判断は人（聞き手役）がする。
/// </summary>
internal static partial class TvLint
{
    /// <summary>
    /// 台詞の長さの上限 [字]: 話し言葉として 1 息で言える長さ。2026-11-17 に 28 → 60（ユーザーの指摘: 28 字では体言止め・電報のような文になり、
    /// 実況・解説の喋り方が不自然だった。長い台詞は次を押し出すので、詰め込みすぎないのは書き手の判断）。
    /// </summary>
    public const int MaxChars = 60;

    /// <summary>ダウン・決着のコールが出来事から遅れてよい時間 [s]。</summary>
    public const float CallDelay = 1.0f;

    /// <summary>声を入れた後に許す台詞のずれ [s]。</summary>
    public const float MaxShift = 1.0f;

    /// <summary>動き・体の状態を言う台詞（主語が要る）の目印。</summary>
    [GeneratedRegex("入っ|ダウン|倒れ|斬|打っ|打ち|突|返|立っ|折れ|飛ん|失|効い|削|揺れ|意識|スタミナ|疲れ|蹴|振り|当て|崩れ|止ま|えぐ")]
    private static partial Regex ActionWords();

    /// <summary>呼ぶだけの呼びかけ（「サエキさん！」）。</summary>
    [GeneratedRegex("^[^、。？?]{1,8}さん[！!。]?$")]
    private static partial Regex CallOnly();

    /// <summary>「〜の…が、〜の…に」の型。</summary>
    [GeneratedRegex("^.+の.+が、.+の.+に")]
    private static partial Regex SamePattern();

    /// <summary>中継では言わない言葉（内部の数値・ゲームの裏側・ゲームの名前）。</summary>
    [GeneratedRegex("ダメージ\\s*[0-9０-９]|打撲|VGV|ボット|物理|セル|%|％")]
    private static partial Regex Forbidden();

    /// <summary>台本と出来事の記録から、決まりに合わない所を並べる。<paramref name="manifestJson"/>（声を入れた後の manifest.json）があれば、ずれも見る。</summary>
    public static IReadOnlyList<TvLintIssue> Check(string scriptJson, string eventsJson, string? manifestJson = null)
    {
        using var script = JsonDocument.Parse(scriptJson);
        using var events = JsonDocument.Parse(eventsJson);
        var red = events.RootElement.GetProperty("red").GetString() ?? "";
        var blue = events.RootElement.GetProperty("blue").GetString() ?? "";
        // 名前: リングネームと、「・」で区切った呼び名（「ザ・ハンマー」→「ハンマー」、「エル・トロ」→「トロ」）。
        var names = new[] { red, blue }.Where(n => n.Length > 0)
            .SelectMany(n => n.Split('・').Where(p => p.Length >= 2 && p != "ザ").Prepend(n))
            .Distinct()
            .ToArray();
        var lines = script.RootElement.GetProperty("lines").EnumerateArray()
            .Select(l => (At: (float)l.GetProperty("at").GetDouble(), Speaker: l.GetProperty("speaker").GetString() ?? "", Text: l.GetProperty("text").GetString() ?? ""))
            .OrderBy(l => l.At)
            .ToList();
        var happenings = events.RootElement.GetProperty("events").EnumerateArray()
            .Select(e => (T: (float)e.GetProperty("t").GetDouble(), Type: e.GetProperty("type").GetString() ?? "", Who: e.TryGetProperty("who", out var w) ? w.GetString() ?? "" : "",
                For: e.TryGetProperty("for", out var f) ? f.GetString() ?? "" : ""))
            .OrderBy(e => e.T)
            .ToList();
        var issues = new List<TvLintIssue>();

        // 主語: 動き・体の状態を言う台詞には、どちらの選手かの名前を入れる（ラジオでは見えない）。
        // 直前の台詞（3 秒以内）が同じ選手を名前で言い、この台詞がその続き（短い）なら許す。
        for (var i = 0; i < lines.Count; i++)
        {
            var (at, speaker, text) = lines[i];
            if (!ActionWords().IsMatch(text) || names.Any(n => text.Contains(n, StringComparison.Ordinal)))
            {
                continue;
            }

            var previousNamed = i > 0 && at - lines[i - 1].At < 3f && names.Any(n => lines[i - 1].Text.Contains(n, StringComparison.Ordinal));
            // 名前を出した問い（実況が解説を「さん」で呼ぶ）への答えは、その選手の話と分かるので主語を省いてよい。
            var answersNamedQuestion = i > 0 && at - lines[i - 1].At < 4.5f && names.Any(n => lines[i - 1].Text.Contains(n, StringComparison.Ordinal)) && speaker == "commentator" && TvDraft.IsQuestion(lines[i - 1].Speaker, lines[i - 1].Text);
            if (!(previousNamed && text.Length <= 12) && !answersNamedQuestion)
            {
                issues.Add(new TvLintIssue(at, "主語", $"{Speaker(speaker)}「{text}」: どちらの選手のことか名前が無い"));
            }
        }

        foreach (var (at, speaker, text) in lines)
        {
            if (text.Length > MaxChars)
            {
                issues.Add(new TvLintIssue(at, "長さ", $"{Speaker(speaker)}「{text}」: {text.Length} 字（{MaxChars} 字まで）"));
            }

            if (Forbidden().Match(text) is { Success: true } m)
            {
                issues.Add(new TvLintIssue(at, "言わない言葉", $"{Speaker(speaker)}「{text}」: 「{m.Value}」は中継で言わない"));
            }
        }

        // 呼びかけ: 「サエキさん！」だけの呼びかけは使わない（何を聞くかを添える）。
        foreach (var (at, speaker, text) in lines.Where(l => l.Speaker == "announcer"))
        {
            if (CallOnly().IsMatch(text))
            {
                issues.Add(new TvLintIssue(at, "呼びかけ", $"実況「{text}」: 呼ぶだけで問いが無い"));
            }
        }

        // 型: 実況の「〜の…が、〜の…に」が 3 回続いたら、短い声（入った！・打ち返す！）を混ぜる。
        var run = 0;
        foreach (var (at, speaker, text) in lines.Where(l => l.Speaker == "announcer"))
        {
            run = SamePattern().IsMatch(text) ? run + 1 : 0;
            if (run == 3)
            {
                issues.Add(new TvLintIssue(at, "型", $"実況「{text}」: 「〜の…が、〜の…に」が 3 回続いた"));
            }
        }

        // ラウンドの頭の言い直し: 開始から 6 秒以内に、ラウンドを言い、両者の名前（か赤・青）を出す。
        foreach (var start in happenings.Where(e => e.Type == "round_start"))
        {
            var window = lines.Where(l => l.At >= start.T - 0.5f && l.At <= start.T + 6f).ToList();
            if (!window.Any(l => l.Text.Contains("ラウンド", StringComparison.Ordinal)))
            {
                issues.Add(new TvLintIssue(start.T, "言い直し", "ラウンドの始まりに「第Nラウンド」と言っていない"));
            }
        }

        // 休みの区間ごとに、実況から解説への問いかけ（「さん」で呼ぶ）と、それに続く解説が 1 組はある（解説は動きの最中でなく、間に話す）。
        // 最後のラウンドの後（次のラウンドが無い: 判定・決着の後）は休みではないので見ない。
        var breaks = happenings.Where(e => e.Type == "round_end").Select(e => (From: e.T, To: happenings.FirstOrDefault(x => x.Type == "round_start" && x.T > e.T).T))
            .Where(b => b.To > b.From)
            .ToList();
        foreach (var (from, to) in breaks)
        {
            var span = lines.Where(l => l.At >= from && l.At < to).ToList();
            var asked = span.Select((l, i) => (l, i)).Any(x => TvDraft.IsQuestion(x.l.Speaker, x.l.Text)
                && x.i + 1 < span.Count && span[x.i + 1].Speaker == "commentator");
            if (!asked)
            {
                issues.Add(new TvLintIssue(from, "掛け合い", "この休みに、実況が解説に問いを振り、解説が答える組が無い"));
            }
        }

        // ダウン・決着のコール: 出来事から CallDelay 秒以内に、倒れた選手の名前か「ダウン」「ノックアウト」を実況が言う。
        foreach (var e in happenings.Where(e => e.For == "実況" && e.Type is "knockdown" or "ko" or "decision"))
        {
            var called = lines.Any(l => l.Speaker == "announcer" && l.At >= e.T - 0.6f && l.At <= e.T + CallDelay
                && (l.Text.Contains("ダウン", StringComparison.Ordinal) || l.Text.Contains("倒れ", StringComparison.Ordinal) || l.Text.Contains("止め", StringComparison.Ordinal)
                    || l.Text.Contains("アウト", StringComparison.Ordinal) || l.Text.Contains("KO", StringComparison.Ordinal) || l.Text.Contains("テン", StringComparison.Ordinal) || l.Text.Contains("判定", StringComparison.Ordinal)));
            if (!called)
            {
                issues.Add(new TvLintIssue(e.T, "コール", $"{(e.Type == "knockdown" ? $"{e.Who} のダウン" : "決着")}を {CallDelay:0.#} 秒以内に実況が言っていない"));
            }
        }

        // カウントの間は問いを振らない（立つか立たないかを実況が追う時間。解説に聞くのは立った後か休み）。
        // 試合中のダウン（knockdown）から、立った・決着・ラウンドの終わりのどれかまで。
        foreach (var down in happenings.Where(e => e.For == "実況" && e.Type == "knockdown"))
        {
            var until = happenings.FirstOrDefault(x => x.T > down.T && (x.Type is "ko" or "decision" or "round_end" || (x.Type == "get_up" && x.Who == down.Who))).T;
            until = until > down.T ? until : float.MaxValue;
            foreach (var (at, _, text) in lines.Where(l => l.At > down.T && l.At < until && TvDraft.IsQuestion(l.Speaker, l.Text)))
            {
                issues.Add(new TvLintIssue(at, "カウント中", $"実況「{text}」: {down.Who} のカウントの最中に解説へ問いを振った"));
            }
        }

        // リプレイの入り: 映像がリプレイに変わったら 2 秒以内に「リプレイ」「スロー」（決着のリプレイは「決着の場面」でもよい）と実況が言う。ラジオでは見えない。
        foreach (var e in happenings.Where(e => e.Type == "replay"))
        {
            var said = lines.Any(l => l.Speaker == "announcer" && l.At >= e.T - 1f && l.At <= e.T + 2f
                && (l.Text.Contains("リプレイ", StringComparison.Ordinal) || l.Text.Contains("スロー", StringComparison.Ordinal) || l.Text.Contains("決着の場面", StringComparison.Ordinal)));
            if (!said)
            {
                issues.Add(new TvLintIssue(e.T, "リプレイ", "リプレイに変わったことを 2 秒以内に実況が言っていない"));
            }
        }

        if (manifestJson is not null)
        {
            using var manifest = JsonDocument.Parse(manifestJson);
            foreach (var l in manifest.RootElement.GetProperty("lines").EnumerateArray())
            {
                var shift = l.GetProperty("shift").GetDouble();
                if (shift > MaxShift)
                {
                    issues.Add(new TvLintIssue((float)l.GetProperty("requestedAt").GetDouble(), "ずれ",
                        string.Create(CultureInfo.InvariantCulture, $"「{l.GetProperty("text").GetString()}」が {shift:0.0} 秒遅れた（前の台詞が長い）")));
                }
            }
        }

        // 型（script.json の "style"、docs/commentary/styles/）ごとに、その型に無い役の決まりは見ない。
        var style = script.RootElement.TryGetProperty("style", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() ?? "" : "";
        var skipped = SkippedRules.TryGetValue(style, out var rules) ? [.. rules, .. Retired] : Retired;
        return [.. issues.Where(i => !skipped.Contains(i.Rule)).OrderBy(i => i.At)];
    }

    /// <summary>
    /// 型ごとに見ない決まり。tv-duo（実況＋解説、既定）はすべて見る。solo（実況 1 人）は解説との掛け合いが無い。
    /// inner-voice（選手の心の声）は実況がいないので、コール・言い直し・リプレイの入り・主語（本人の声で分かる）も見ない。
    /// </summary>
    /// <summary>
    /// やめた決まり（2026-11-17、ユーザーの指摘: 実況と解説の喋り方を不自然に縛っていた）。主語: すべての台詞に選手の名前を求めると「ドミトリー、…」で始まる文が続き、
    /// 会話にならなかった（誰の話かは前後の文脈で分かればよい）。型: 文の形の繰り返しを機械的に数えるのは書き手の判断に任せる。
    /// </summary>
    private static readonly HashSet<string> Retired = ["主語", "型"];

    private static readonly Dictionary<string, HashSet<string>> SkippedRules = new(StringComparer.Ordinal)
    {
        ["solo"] = ["掛け合い", "カウント中"],
        ["inner-voice"] = ["掛け合い", "カウント中", "コール", "言い直し", "リプレイ", "主語", "呼びかけ", "型"],
        // 2026-11-17、ユーザーの指摘（台詞の中身を決まりで誘導して単調になった）: 役（実況・解説）だけ与えて自由に話す型。実況の役に
        // 欠かせない入りのコール（ラウンド・ダウン・決着・リプレイ）だけ見て、会話の運び（問いを振る・振り方）は書き手に任せる。
        ["open"] = ["掛け合い", "カウント中", "呼びかけ"],
        // 役も与えないフリートーク（2 人が試合を見ながら話す）。見るのは長さ（声が重なる）と言わない言葉（作中の世界）とずれだけ。
        ["free"] = ["掛け合い", "カウント中", "コール", "言い直し", "リプレイ", "主語", "呼びかけ", "型"],
        // 2 エージェントのプロスペクティブ制（tools/tvshow/prospective/）: マイク 1 本を時間の資源として話し、1 回に話す長さにも決まりが無い。
        // 見るのは言わない言葉とずれだけ。
        ["prospective"] = ["掛け合い", "カウント中", "コール", "言い直し", "リプレイ", "主語", "呼びかけ", "型", "長さ"],
    };

    private static string Speaker(string speaker) => speaker switch { "announcer" => "実況", "commentator" => "解説", _ => speaker };
}
