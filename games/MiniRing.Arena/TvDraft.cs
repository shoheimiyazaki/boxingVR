using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniRing.Arena;

/// <summary>
/// 実況の骨組みの下書き（中継の記録 → <c>script.json</c>）。ラジオで聞いても試合が追えるのに要る台詞を、決まった型で抜けなく置く:
/// 選手紹介・ラウンドの頭（ダウンの数の整理・倒れたままの選手）・誰の何でダウンしたか（何度目か）・立った・骨折と切断・残り 10 秒・
/// ラウンドの終わり（倒れたままか・ダウンの数）・リプレイの入り（どの場面か）・決着（止めた理由）・勝者。
/// 解説との掛け合いと色づけは、これを土台に書き手（エージェント）が足す（docs/TV_BROADCAST.md §4）。下書きだけでも動画にできる。
/// 台詞の長さは字数と区切りと話す速さから見積もり（<see cref="Duration"/>）、前の台詞と重なる「省いてよい台詞」はずらし、2 秒より遅れるなら落とす。
/// </summary>
internal static partial class TvDraft
{
    /// <summary>
    /// 台詞の長さの見積もり: (字 × <see cref="CharSeconds"/> ＋ 区切り（。、！？…）× <see cref="PauseSeconds"/>) ÷ (1 ＋ 話す速さ)。
    /// OneCore の声の実測（2026-11-16、実況・解説・心の声 4 声 290 台詞）に当てはめた値で、平均の誤差 0.28 秒
    /// （字だけの見積もりでは 0.42 秒。区切りの間が長く、遅い話し方の台詞が押し出されていた）。
    /// </summary>
    private const float CharSeconds = 0.159f;

    /// <summary>重ねるときに 1 台詞が占める長さの余裕（見積もりに対する実測の最大 1.3 倍）。</summary>
    private const float MergeMargin = 1.15f;

    /// <summary>区切り 1 つの間 [s]（上の当てはめ）。</summary>
    private const float PauseSeconds = 0.476f;

    /// <summary>話し手の既定の速さ（Synthesize.ps1 の既定の prosody の rate と同じ）。</summary>
    private static readonly Dictionary<string, (float Normal, float Excited)> DefaultRates = new(StringComparer.Ordinal)
    {
        ["announcer"] = (0.30f, 0.50f),
        ["commentator"] = (0.15f, 0.28f),
    };

    /// <summary>台詞の間 [s]（声入れの <c>-Gap</c> と同じ）。</summary>
    private const float Gap = 0.15f;

    /// <summary>省いてよい台詞を後ろへずらせる上限 [s]。</summary>
    private const float MaxDelay = 2f;

    /// <summary>実況用の当たりの文（「Aの重い左が、Bの顔面に入った」「Aの刀が、Bの胸（胸）を斬った」）。</summary>
    [GeneratedRegex("^(?<a>.+?)の(?<blow>.+?)が、?(?<v>.+?)の(?<zone>.+?)(に入った|を斬った|を打った)$")]
    private static partial Regex HitText();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private sealed record Draft(float At, string Text, bool Excited, bool Essential);

    /// <summary>
    /// テレビ中継の標準の下書き: 実況の骨組み（<see cref="Build"/>）に、解説の台詞と休みの問いと答え（<see cref="TvAnalyst"/>）を
    /// <see cref="Merge"/> で重ねた、実況と解説の 2 人の台本。入らなかった解説の台詞は落とす（骨組みは動かさない）。
    /// </summary>
    public static string BuildBroadcast(string eventsJson, Organization? org = null) => Merge(Build(eventsJson, org), TvAnalyst.Build(eventsJson), []);

    /// <summary>記録（events.json）の <c>org</c> の団体。無ければ WVC。台本・点検・要約の道具が、<c>--org</c> が無いときに使う。</summary>
    public static Organization OrgOf(string eventsJson)
    {
        using var doc = JsonDocument.Parse(eventsJson);
        return OrgOf(doc.RootElement);
    }

    private static Organization OrgOf(JsonElement root)
        => Organization.Of(root.TryGetProperty("org", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null);

    /// <summary>
    /// 中継の記録から、実況の骨組みの台本（script.json の中身）を作る。<paramref name="org"/> の言い回し（ラウンドの合図・決着のコール）で書き、
    /// ダウンを数えない団体（<see cref="Organization.CountsDowns"/> が false）では、ダウンの数・カウント・「ダウン」と言わない。
    /// <paramref name="org"/> が null なら記録の <c>org</c>（無ければ WVC）。
    /// </summary>
    public static string Build(string eventsJson, Organization? org = null)
    {
        using var doc = JsonDocument.Parse(eventsJson);
        var root = doc.RootElement;
        org ??= OrgOf(root);
        var phrases = org.Draft;
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var red = Text("red");
        var blue = Text("blue");
        var events = root.GetProperty("events").EnumerateArray()
            .Select(e => (T: (float)e.GetProperty("t").GetDouble(), For: e.TryGetProperty("for", out var f) ? f.GetString() ?? "" : "",
                Type: e.GetProperty("type").GetString() ?? "", Who: e.TryGetProperty("who", out var w) ? w.GetString() ?? "" : "", Text: e.GetProperty("text").GetString() ?? ""))
            .OrderBy(e => e.T)
            .ToList();
        var drafts = new List<Draft>();
        var downs = new Dictionary<string, int> { [red] = 0, [blue] = 0 };
        var lying = new HashSet<string>();
        var round = 0;
        int knockdowns = 0, getUps = 0, saved = 0, replays = 0;
        var severed = new HashSet<string>();
        var bout = Text("bout");
        var program = root.TryGetProperty("program", out var programJson) && programJson.ValueKind == JsonValueKind.Object
            ? TvProgram.FromJson(programJson.GetRawText(), _ => null)
            : null;
        drafts.AddRange(ProgramLines(program));
        // 選手紹介は冒頭のタイトル（両コーナーの板）が出てから。プログラムの表があればその後ろ。
        var opening = events.FirstOrDefault(e => e.Type == "opening").T;
        drafts.Add(new Draft(opening + 0.3f, $"{bout}{(bout.Length > 0 ? "。" : "")}赤、{Discipline(Text("redDiscipline"))}{red}。", false, true));
        drafts.Add(new Draft(opening + 3.6f, $"青、{Discipline(Text("blueDiscipline"))}{blue}。", false, true));

        var roundStarts = events.Where(e => e.Type == "round_start").Select(e => e.T).ToList();
        var roundEnds = events.Where(e => e.Type is "round_end" or "ko" or "decision").Select(e => e.T).ToList();
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            switch (e.Type)
            {
                case "round_start":
                    round++;
                    drafts.Add(new Draft(e.T + 0.1f, $"第{round}ラウンド、{phrases.BellWord}！", true, true));
                    if (round > 1 && org.CountsDowns)
                    {
                        drafts.Add(new Draft(e.T + 2f, Tally(red, blue, downs), false, false));
                    }

                    foreach (var name in lying)
                    {
                        drafts.Add(new Draft(e.T + 2f, $"{name}は倒れたまま、カウントは数え直し。", false, true));
                    }

                    // 残り 10 秒（ラウンドが 30 秒続いたときの 3 分の 2 の所。早く決着したラウンドでは言わない）。
                    var end = roundEnds.FirstOrDefault(t => t > e.T);
                    var next = roundStarts.FirstOrDefault(t => t > e.T);
                    if (end > e.T && (next == 0f || end < next) && events.Any(x => x.Type == "round_end" && Math.Abs(x.T - end) < 0.01f))
                    {
                        drafts.Add(new Draft(e.T + ((end - e.T) * 2f / 3f), $"第{round}ラウンド、残り10秒！", true, false));
                    }

                    break;

                case "knockdown" when e.For == "実況" && !org.CountsDowns:
                    // ダウンを取らない団体: 倒れたと言うだけ（数えない・カウントしない・倒れたままを覚えない）。
                    drafts.Add(new Draft(e.T, phrases.FallenCall.Replace("{victim}", e.Who, StringComparison.Ordinal), true, true));
                    break;

                case "knockdown" when e.For == "実況":
                    var victim = e.Who;
                    downs[victim] = downs.GetValueOrDefault(victim) + 1;
                    lying.Add(victim);
                    var cause = events.Take(i).LastOrDefault(x => x.For == "実況" && x.Type is "hit" or "weapon_hit" && x.T >= e.T - 4.5f && HitText().Match(x.Text) is { Success: true } m && m.Groups["v"].Value == victim);
                    // 言い回しは決まった順に変える（同じ型の連続を避ける。乱数は使わない: 同じ記録なら同じ台本）。
                    var call = cause.Text is { } c && HitText().Match(c) is { Success: true } hm
                        ? (knockdowns++ % 3) switch
                        {
                            0 => $"{hm.Groups["a"].Value}の{hm.Groups["blow"].Value}！{victim}、ダウン！",
                            1 => $"{hm.Groups["a"].Value}の{Plain(hm.Groups["blow"].Value)}！{victim}が倒れる！",
                            _ => $"{victim}、ダウン！{hm.Groups["a"].Value}の{Plain(hm.Groups["blow"].Value)}が{Short(hm.Groups["zone"].Value)}に！",
                        }
                        : $"{victim}、ダウン！";
                    drafts.Add(new Draft(e.T, call, true, true));
                    if (downs[victim] > 1)
                    {
                        drafts.Add(new Draft(e.T + 2.4f, $"{victim}、この試合{downs[victim]}度目のダウン。", false, false));
                    }

                    break;

                case "count" when e.For == "実況" && org.CountsDowns && CountWord(e.Text) is { } word:
                    // レフェリーのカウントを後半だけ声に出す（ラジオでは数えないと立てるかどうかの緊張が伝わらない）。
                    // 8・9 は欠かせない台詞、7 は空いていれば。前半は数えず、実況は誰が何で倒れたか・解説はなぜ倒れたかを言う時間に使う。
                    drafts.Add(new Draft(e.T, $"{word}！", true, CountNumber(e.Text) >= 8));
                    break;

                case "get_up" when e.Text.Contains("休みの間", StringComparison.Ordinal):
                    lying.Remove(e.Who); // 休みの間に立った: 次のラウンドを「倒れたまま」で始めない（コールはしない）
                    break;

                case "get_up":
                    lying.Remove(e.Who);
                    var count = org.CountsDowns && Regex.Match(e.Text, "カウント (\\d+)") is { Success: true } cm ? cm.Groups[1].Value : "";
                    var counted = count is "" or "0" ? "" : $"カウント{count}";
                    drafts.Add(new Draft(e.T, (getUps++ % 3) switch
                    {
                        0 => $"{e.Who}、立った！{(counted.Length > 0 ? counted + "。" : "")}",
                        1 => $"{e.Who}が立ち上がる！{(counted.Length > 0 ? counted + "。" : "")}",
                        _ => counted.Length > 0 ? $"{counted}、{e.Who}が立った！" : $"{e.Who}、立った！",
                    }, true, true));
                    break;

                case "round_end":
                    drafts.Add(new Draft(e.T + 0.2f, $"{phrases.BellWord}！第{round}ラウンド終了。", false, true));
                    foreach (var name in lying)
                    {
                        drafts.Add(new Draft(e.T + 2.3f, (saved++ % 3) switch
                        {
                            0 => $"{name}は倒れたまま、ゴングに救われた。",
                            1 => $"{name}、倒れたままのゴング。助かった。",
                            _ => $"{name}は立てないまま、ラウンドが終わった。",
                        }, false, true));
                    }

                    if (org.CountsDowns)
                    {
                        drafts.Add(new Draft(e.T + 4.5f, Tally(red, blue, downs), false, false));
                    }

                    break;

                case "fracture" or "severed" when e.For == "実況":
                    // 骨が折れた・手足が飛んだ（画面で見える大きな出来事）。
                    // 同じ部位がまた飛んだ（残りを斬られた）ときは「残りも」と言う（同じ文を 2 度叫ばない）。
                    var injury = e.Text;
                    if (e.Type == "severed" && !severed.Add(injury))
                    {
                        injury = injury.Replace("が飛んだ", "、残りも飛んだ", StringComparison.Ordinal);
                    }

                    drafts.Add(new Draft(e.T, injury + "！", true, true));
                    break;

                case "replay":
                    drafts.Add(new Draft(e.T + 0.1f, ReplayLine(e.Text, replays++), false, true));
                    break;

                case "ko":
                    // 勝者が読めないとき（記録の文が想定と違う）は、名前を出す台詞を作らない（取り違えるよりは言わない）。
                    var winner = Winner(e.Text, red, blue);
                    var loser = winner is null ? null : winner == red ? blue : red;
                    // コール → 勝者を欠かせない台詞に（すぐ決着のリプレイが入るので短く）。理由（誰がどうなったか）は空いていれば。
                    // 「レフェリーが止めた」とは言わない（レフェリーなしの試合もある。動けない・立てないは見て分かる）。
                    var finishCall = phrases.FinishCall;
                    var (callLine, why) = loser is null ? (finishCall, "")
                        : e.Text.StartsWith("10カウント", StringComparison.Ordinal) ? ("テン！", $"{loser}、立てなかった。")
                        : e.Text.StartsWith("心臓", StringComparison.Ordinal) || e.Text.StartsWith('脳') ? ($"{finishCall}{loser}、動かない！", "")
                        : e.Text.StartsWith("リング", StringComparison.Ordinal) ? ($"リングアウト！{loser}、場外へ！", "")
                        : ($"{finishCall}{loser}、動けない！", $"{loser}、意識がない。");
                    drafts.Add(new Draft(e.T, callLine, true, true));
                    if (winner is not null)
                    {
                        drafts.Add(new Draft(e.T + 0.1f, phrases.WinLine.Replace("{winner}", winner, StringComparison.Ordinal), true, true));
                    }

                    if (why.Length > 0)
                    {
                        drafts.Add(new Draft(e.T + 0.2f, why, false, false));
                    }

                    break;

                case "decision":
                    drafts.Add(new Draft(e.T + 0.2f, phrases.DrawLine.Length > 0 && Winner(e.Text, red, blue) is null ? phrases.DrawLine : $"試合終了の{phrases.BellWord}。{e.Text}。", true, true));
                    break;

                case "result":
                    var who = Winner(events.LastOrDefault(x => x.Type is "ko" or "decision").Text ?? "", red, blue);
                    if (who is not null)
                    {
                        drafts.Add(new Draft(e.T + 2.5f, $"勝者、{(who == red ? "赤" : "青")}コーナー、{who}！", true, true));
                        if (program?.Current is { Next.Length: > 0 } stake)
                        {
                            drafts.Add(new Draft(e.T + 2.6f, $"{who}、{stake.Next}！", true, true));
                        }
                    }

                    break;
            }
        }

        return Serialize(Place(drafts));
    }

    /// <summary>
    /// 本日のプログラムの表の間の台詞: 大会の名前 → 済んだ試合の結果（試合の順に）→ この試合に懸かるもの。
    /// 欠かせない台詞として順に並べる（表の長さ <see cref="TvProgram.Seconds"/> はこの行数から決めてある）。
    /// </summary>
    private static IEnumerable<Draft> ProgramLines(TvProgram? program)
    {
        if (program is null)
        {
            yield break;
        }

        // 大会名は表の見出しに出ているので言わない（「WVC」を 1 字ずつ読むと 4.5 秒かかり、選手紹介が押し出された）。
        yield return new Draft(0.3f, "本日のプログラムです。", false, true);
        foreach (var done in program.Finished)
        {
            var method = done.Method switch
            {
                "K.O." => "KO勝ち",
                "リングアウト" => "リングアウト勝ち",
                "判定" => "判定勝ち",
                "優勢" => "優勢勝ち", // 引き分けで、与えたダメージの多い側が勝ち上がった
                _ => "勝ち",
            };
            yield return new Draft(0.4f, $"{done.Bout}は{done.WinnerName}の{method}。", false, true);
        }

        if (program.Current is { Stake.Length: > 0 } current)
        {
            yield return new Draft(0.5f, $"この{current.Bout}、{current.Stake}。", false, true);
        }
    }

    /// <summary>リプレイの入り:「第1ラウンド、スローでもう一度。」と「ザ・ハンマーの重い左で、アキラが倒れた所。」（説明の当たりの文から、誰の何で誰がどうなった所かを言う）。</summary>
    private static string ReplayLine(string note, int index)
    {
        var finale = note.StartsWith("決着", StringComparison.Ordinal);
        var round = Regex.Match(note, "第(\\d+)ラウンド") is { Success: true } r ? $"第{r.Groups[1].Value}ラウンド、" : "";
        var body = note.Contains(": ", StringComparison.Ordinal) ? note[(note.IndexOf(": ", StringComparison.Ordinal) + 2)..] : note;
        var first = body.Split(" →")[0].Trim().TrimEnd('に');
        // 「→ 左上腕切断」「→ 右前腕骨折」が付いていれば、その場面（どこが折れた・飛んだ）として言う。
        var injury = Regex.Match(body, "→ (?<part>[^ →]+?)(?<what>骨折|切断)");
        var down = body.Contains("ダウン", StringComparison.Ordinal);
        // 当たりの説明（「Aの重い左がBの顔面に」）・骨折や切断だけの説明（「B 右前腕骨折」）・倒れただけの説明（「B ダウン」）。
        var scene = HitText().Match(first + "に入った") is { Success: true } m
                ? injury.Success ? $"{m.Groups["v"].Value}の{injury.Groups["part"].Value}が{(injury.Groups["what"].Value == "骨折" ? "折れる" : "飛ぶ")}"
                : (down ? $"{m.Groups["a"].Value}の{Plain(m.Groups["blow"].Value)}で{m.Groups["v"].Value}がダウン" : $"{m.Groups["a"].Value}の{Plain(m.Groups["blow"].Value)}が{m.Groups["v"].Value}の{Short(m.Groups["zone"].Value)}へ")
            : Regex.Match(first, @"^(?<v>\S+) (?<part>\S+?)(?<what>骨折|切断)$") is { Success: true } p
                ? $"{p.Groups["v"].Value}の{p.Groups["part"].Value}が{(p.Groups["what"].Value == "骨折" ? "折れる" : "飛ぶ")}"
            : Regex.Match(first, @"^(?<v>\S+) ダウン") is { Success: true } d ? $"{d.Groups["v"].Value}が倒れる"
            : first;
        // 1 行で短く（解説が「なぜ効いたか」を足す隙間を残す）。休みのリプレイは終わったばかりのラウンドの場面。
        _ = round;
        return finale ? $"決着の場面。{scene}。" : (index % 3) switch
        {
            0 => $"リプレイ。{scene}。",
            1 => $"スローで。{scene}。",
            _ => $"スローでもう一度。{scene}。",
        };
    }

    /// <summary>当たりの強さの言葉を外す（「重い左」→「左」）。</summary>
    private static string Plain(string blow) => blow.Replace("強烈な", "", StringComparison.Ordinal).Replace("重い", "", StringComparison.Ordinal).Replace("いい", "", StringComparison.Ordinal);

    /// <summary>当たった所の短い呼び方（「レバー（右わき腹）」→「レバー」、「顔面（頭）」→「顔面」）。</summary>
    private static string Short(string zone) => Regex.Replace(zone, "（.*?）", "");

    private static string Tally(string red, string blue, Dictionary<string, int> downs)
        => downs[red] == 0 && downs[blue] == 0 ? $"{red}と{blue}、ダウンはまだありません。"
            : $"ダウンは{red}{Count(downs[red])}、{blue}{Count(downs[blue])}。";

    private static string Count(int n) => n == 0 ? "なし" : $"{n}度";

    /// <summary>決着の文（「…。Aの勝ち」「判定でAの勝ち」）の勝者。赤・青のどちらでもなければ null（引き分けも）。</summary>
    private static string? Winner(string text, string red, string blue) =>
        red.Length > 0 && text.Contains(red + "の勝ち", StringComparison.Ordinal) ? red
        : blue.Length > 0 && text.Contains(blue + "の勝ち", StringComparison.Ordinal) ? blue
        : null;

    /// <summary>実況が解説へ振る問い（「サエキさん、…」）。「さんざん」のような言葉は問いにしない。</summary>
    internal static bool IsQuestion(string speaker, string text) => speaker == "announcer" && QuestionHead().IsMatch(text);

    [GeneratedRegex("^[^、。！？]{1,8}さん[、！？]")]
    private static partial Regex QuestionHead();

    private static readonly string[] CountWords = ["", "ワン", "ツー", "スリー", "フォー", "ファイブ", "シックス", "セブン", "エイト", "ナイン"];

    private static int CountNumber(string text) => Regex.Match(text, @"(\d+)") is { Success: true } m && int.TryParse(m.Value, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>声に出すカウント（7〜9）。それ以外は null。</summary>
    private static string? CountWord(string text) => CountNumber(text) is >= 7 and <= 9 and var n ? CountWords[n] : null;

    private static string Discipline(string discipline) => discipline.Length > 0 ? $"{discipline}の" : "";

    /// <summary>
    /// 重なりを解く。欠かせない台詞（コール・ラウンドの始まりと終わり・リプレイの入り・勝者）を先に時刻どおり置き（前の欠かせない台詞が長ければその後）、
    /// 省いてよい台詞（ダウンの数の整理・残り 10 秒など）は、欠かせない台詞の間の空きに MaxDelay 秒までの遅れで入るときだけ置く。
    /// </summary>
    private static List<Draft> Place(List<Draft> drafts)
    {
        var essentials = new List<Draft>();
        var busyUntil = 0f;
        foreach (var d in drafts.Where(d => d.Essential).OrderBy(d => d.At))
        {
            var start = MathF.Max(d.At, busyUntil + Gap);
            essentials.Add(d with { At = start });
            busyUntil = start + AnnouncerDuration(d);
        }

        var placed = new List<Draft>(essentials);
        foreach (var d in drafts.Where(d => !d.Essential).OrderBy(d => d.At))
        {
            var start = d.At;
            foreach (var other in placed.Where(o => o.At <= start + AnnouncerDuration(d) + Gap && o.At + AnnouncerDuration(o) + Gap > start).OrderBy(o => o.At))
            {
                start = MathF.Max(start, other.At + AnnouncerDuration(other) + Gap);
            }

            var end = start + AnnouncerDuration(d);
            var collides = placed.Any(o => o.At < end + Gap && o.At + AnnouncerDuration(o) + Gap > start);
            if (start - d.At <= MaxDelay && !collides)
            {
                placed.Add(d with { At = start });
            }
        }

        return [.. placed.OrderBy(d => d.At).Select(d => d with { At = MathF.Round(d.At, 1) })];
    }

    /// <summary>台詞を話す長さの見積もり [s]。<paramref name="rate"/> は話す速さ（+15% なら 0.15）。</summary>
    internal static float Duration(string text, float rate)
    {
        var pauses = Regex.Count(text, "[。、！？!?]") + Regex.Count(text, "…+");
        var chars = Regex.Replace(text, "[。、！？!?…]", "").Length;
        // −50% より遅い・+200% より速い指定は見積もりの外（0 や負の割り算を避ける）。
        return ((chars * CharSeconds) + (pauses * PauseSeconds)) / (1f + Math.Clamp(rate, -0.5f, 2f));
    }

    private static float AnnouncerDuration(Draft d) => Duration(d.Text, d.Excited ? DefaultRates["announcer"].Excited : DefaultRates["announcer"].Normal);

    /// <summary>台本の配役（speakers）の rate を読む。書いていなければ既定（実況・解説）か 0。</summary>
    private static Func<string, bool, float> Rates(JsonElement root)
    {
        static float Parse(JsonElement e, string name, float fallback) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && float.TryParse(v.GetString()!.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) ? pct / 100f : fallback;

        var table = new Dictionary<string, (float Normal, float Excited)>(DefaultRates, StringComparer.Ordinal);
        if (root.TryGetProperty("speakers", out var speakers) && speakers.ValueKind == JsonValueKind.Object)
        {
            foreach (var sp in speakers.EnumerateObject())
            {
                var old = table.GetValueOrDefault(sp.Name);
                var normal = Parse(sp.Value, "rate", old.Normal);
                var excited = sp.Value.ValueKind == JsonValueKind.Object && sp.Value.TryGetProperty("excited", out var ex) ? Parse(ex, "rate", table.ContainsKey(sp.Name) ? old.Excited : normal) : table.ContainsKey(sp.Name) ? old.Excited : normal;
                table[sp.Name] = (normal, excited);
            }
        }

        return (speaker, excited) => table.TryGetValue(speaker, out var r) ? (excited ? r.Excited : r.Normal) : 0f;
    }

    /// <summary>
    /// 下書き（骨組み）に、書き手が足す台詞（<c>extras.json</c>）を重ねて台本にする。骨組みは時刻どおりに残し、足す台詞は空きに入れる:
    /// 打ち合いの実況（<c>excited</c>）を 1 秒まで遅らせて先に、次に問い（実況の「さん」）と続く解説の組を前 2 秒・後ろ 6 秒の範囲で、
    /// 最後にほかの台詞を前 1.5 秒・後ろ 2.5 秒の範囲で置く。入らない台詞は落とし、<paramref name="dropped"/> に返す。
    /// extras の <c>drop</c> に書いた台詞は下書きから取り除く（下書きの思い違いを書き手が直す）。
    /// </summary>
    public static string Merge(string draftJson, string extrasJson, List<string> dropped)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        using var draft = JsonDocument.Parse(draftJson);
        using var extras = JsonDocument.Parse(extrasJson);
        var drop = extras.RootElement.TryGetProperty("drop", out var d) && d.ValueKind == JsonValueKind.Array ? d.EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet() : [];
        if (!extras.RootElement.TryGetProperty("lines", out var extraLines) || extraLines.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("extras.json に \"lines\"（台詞の配列）がありません。");
        }

        // "useDraft": false なら骨組みを使わない（実況のいない型: 選手の心の声など。docs/commentary/）。
        var useDraft = !extras.RootElement.TryGetProperty("useDraft", out var u) || u.ValueKind != JsonValueKind.False;
        // 話す長さは話し手の速さ込みで見積もる（遅い声の心の声は 1 字 0.25 秒ほどかかる）。
        var rate = Rates(extras.RootElement);
        // 見積もりは平均では合うが、1 台詞ごとには 2〜3 割長いことがある（実測の最大 1.3 倍）。詰めて並べると遅れが次へ積み重なるので、
        // 置き場所は長い側（MergeMargin 倍）で取る。
        Spoken Timed(Spoken l) => l with { Seconds = Duration(l.Text, rate(l.Speaker, l.Excited)) * MergeMargin };
        var placed = useDraft
            ? draft.RootElement.GetProperty("lines").EnumerateArray().Select(Line).Where(l => !drop.Contains(l.Text)).Select(Timed).ToList()
            : [];
        var adds = extraLines.EnumerateArray().Select(Line).Select(Timed).OrderBy(l => l.At).ToList();
        // "placed": true なら書き手が時刻を決め切っている（2 エージェントのプロスペクティブ制: マイク 1 本で順に話した記録。
        // tools/tvshow/prospective/）。空きを探して動かさず、そのまま台本にする（重なりは声入れが後ろへ押す）。
        if (!useDraft && extras.RootElement.TryGetProperty("placed", out var p) && p.ValueKind == JsonValueKind.True)
        {
            placed.AddRange(adds);
            adds = [];
        }

        var groups = new List<List<Spoken>>();
        for (var i = 0; i < adds.Count; i++)
        {
            if (IsQuestion(adds[i].Speaker, adds[i].Text) && i + 1 < adds.Count && adds[i + 1].Speaker == "commentator")
            {
                groups.Add([adds[i], adds[i + 1]]);
                i++;
            }
            else
            {
                groups.Add([adds[i]]);
            }
        }

        foreach (var g in groups.OrderBy(g => g[0].Excited ? 0 : g.Count == 2 ? 1 : 2).ThenBy(g => g[0].At))
        {
            var (early, late) = g[0].Excited ? (0f, 1f) : g.Count == 2 ? (2f, 6f) : (1.5f, 2.5f);
            if (Slot(placed, g, g[0].At - early, g[0].At + late) is { } starts)
            {
                placed.AddRange(g.Select((l, k) => l with { At = starts[k] }));
            }
            else
            {
                dropped.AddRange(g.Select(l => l.Text));
            }
        }

        // 型（style）と配役（speakers: 話し手ごとの声と話し方）は書き手の extras からそのまま台本へ渡す（点検と声入れが読む）。
        var payload = new Dictionary<string, object>();
        foreach (var key in new[] { "style", "speakers" })
        {
            if (extras.RootElement.TryGetProperty(key, out var v))
            {
                payload[key] = v.Clone();
            }
        }

        payload["lines"] = placed.OrderBy(l => l.At).Select(l =>
        {
            var line = new Dictionary<string, object>
            {
                ["at"] = Math.Round(l.At, 1),
                ["speaker"] = l.Speaker,
                ["text"] = l.Text,
                ["excited"] = l.Excited,
            };
            if (l.Overlap)
            {
                line["overlap"] = true; // 興奮の感嘆: 声入れが順番の外で重ねて鳴らす
            }

            return line;
        }).ToList();
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>台本の 1 台詞。<see cref="Seconds"/> は話す長さの見積もり（話し手の速さ込み）。</summary>
    private sealed record Spoken(float At, string Speaker, string Text, bool Excited, float Seconds = 0f, bool Overlap = false);

    private static Spoken Line(JsonElement l) => new(
        (float)l.GetProperty("at").GetDouble(),
        l.TryGetProperty("speaker", out var s) ? s.GetString() ?? "announcer" : "announcer",
        l.GetProperty("text").GetString() ?? "",
        l.TryGetProperty("excited", out var e) && e.ValueKind == JsonValueKind.True,
        Overlap: l.TryGetProperty("overlap", out var o) && o.ValueKind == JsonValueKind.True);

    /// <summary>台詞の組を続けて置ける最初の時刻の並び（<paramref name="from"/>〜<paramref name="to"/> を 0.1 秒刻みで探す）。</summary>
    private static float[]? Slot(List<Spoken> placed, List<Spoken> group, float from, float to)
    {
        for (var t = MathF.Max(0f, from); t <= to + 1e-4f; t += 0.1f)
        {
            var starts = new float[group.Count];
            var s = t;
            var ok = true;
            for (var k = 0; k < group.Count && ok; k++)
            {
                var e = s + group[k].Seconds;
                ok = !placed.Any(o => o.At < e + Gap && o.At + o.Seconds + Gap > s);
                starts[k] = s;
                s = e + Gap;
            }

            if (ok)
            {
                return starts;
            }
        }

        return null;
    }

    private static string Serialize(List<Draft> lines)
    {
        var payload = new
        {
            lines = lines.Select(l => new Dictionary<string, object>
            {
                ["at"] = Math.Round(l.At, 1),
                ["speaker"] = "announcer",
                ["text"] = l.Text,
                ["excited"] = l.Excited,
            }),
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>数値を見せる用に丸める（テストの比較用）。</summary>
    internal static string Seconds(float t) => t.ToString("0.0", CultureInfo.InvariantCulture);
}
