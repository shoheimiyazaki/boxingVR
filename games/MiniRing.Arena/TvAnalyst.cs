using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniRing.Arena;

/// <summary>
/// 解説（サエキ）の台詞の下書き（中継の記録 → extras.json の形）。テレビ中継の標準は実況と解説の 2 人（docs/TV_BROADCAST.md §4）で、
/// 書き手（エージェント）が台詞を足さなくても 2 人で話すように、記録の「解説」向けの出来事から決まった型で作る:
/// <list type="bullet">
/// <item>休みごとに実況の問い（「サエキさん、〇〇選手の様子は？」）→ 解説の答え（体の状態の記録 <c>body</c>: 傷と疲れ）。</item>
/// <item>リプレイの後に、当たった所がなぜ効くか（当たった所の言葉から）。</item>
/// <item>ダウンの後に、倒れた理由（<c>knockdown</c>・<c>effect</c> の記録）。</item>
/// <item>ラウンドの中で 1 回まで、疲れの見立て（<c>tired</c> の記録で「尽き」と言われた選手）。</item>
/// </list>
/// 置き場所は <see cref="TvDraft.Merge"/> が骨組みの空きに入れる（入らない台詞は落ちる）。乱数は使わない（同じ記録なら同じ台詞）。
/// </summary>
internal static partial class TvAnalyst
{
    /// <summary>1 台詞の上限（<see cref="TvLint"/> の「長さ」と同じ 28 字）。超える台詞は作らない。</summary>
    private const int MaxChars = 28;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private sealed record Line(float At, string Speaker, string Text);

    /// <summary>中継の記録から、解説の台詞と問いの組（extras.json の中身）を作る。</summary>
    public static string Build(string eventsJson)
    {
        using var doc = JsonDocument.Parse(eventsJson);
        var root = doc.RootElement;
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var red = Text("red");
        var blue = Text("blue");
        var events = root.GetProperty("events").EnumerateArray()
            .Where(e => e.TryGetProperty("t", out _) && e.TryGetProperty("type", out _) && e.TryGetProperty("text", out _)) // 欠けた出来事は飛ばす
            .Select(e => (T: (float)e.GetProperty("t").GetDouble(), For: e.TryGetProperty("for", out var f) ? f.GetString() ?? "" : "",
                Type: e.GetProperty("type").GetString() ?? "", Who: e.TryGetProperty("who", out var w) ? w.GetString() ?? "" : "", Text: e.GetProperty("text").GetString() ?? ""))
            .OrderBy(e => e.T)
            .ToList();
        var lines = new List<Line>();
        var round = 0;
        var tiredSaid = new HashSet<int>();
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            switch (e.Type)
            {
                case "round_start":
                    round++;
                    break;

                case "round_end":
                    // 休み: 次のラウンドがあるときだけ（最後のラウンドの後は勝敗の画面）。
                    var next = events.Skip(i + 1).FirstOrDefault(x => x.Type == "round_start");
                    if (next.Type is null)
                    {
                        break;
                    }

                    var replays = events.Where(x => x.Type == "replay" && x.T > e.T && x.T < next.T).ToList();
                    // 最後のリプレイの後: 実況の場面の説明（約 5 秒）→ 解説のなぜ効くか（約 4.5 秒）→ 問いと答え（約 8 秒）。25 秒の休み（映像 33 秒）で入る。次のゴングの 7 秒前より後ろにはしない（早めに 2 秒まで動く）。
                    var askAt = MathF.Max(e.T + 1f, MathF.Min(replays.Count > 0 ? replays[^1].T + 12.3f : e.T + 4f, next.T - 7f)); // 短い休みでもラウンドの中へははみ出さない
                    var bodies = events.Where(x => x.Type == "body" && x.For == "解説" && x.T >= e.T - 1f && x.T < next.T).ToList();
                    if (askAt + 7f <= next.T && Subject(bodies, red, blue, round) is { } subject && Answer(subject.Text) is { } answer && Fit($"サエキさん、{subject.Who}の様子は？") is { } question)
                    {
                        lines.Add(new Line(askAt, "announcer", question));
                        lines.Add(new Line(askAt + 0.01f, "commentator", answer)); // 問いのすぐ後ろに並べる（間に他の解説の台詞が入ると、それが答えと組まれる）。置く時刻は TvDraft.Merge が問いの後ろに詰める
                    }

                    if (askAt + 8.5f + 4f <= next.T && events.LastOrDefault(x => x.Type == "score" && x.For == "解説" && x.T <= next.T).Text is { Length: > 0 } score && Lead(score) is { } lead)
                    {
                        lines.Add(new Line(askAt + 8.5f, "commentator", lead)); // 問いと答えの後、空いていれば
                    }

                    break;

                case "replay":
                    // なぜ効くか: 実況が場面を言い終えてから（入らなければ落ちる。休みの問いと答えが先に場所を取る）。
                    if (ReplayReason(e.Text) is { } reason)
                    {
                        lines.Add(new Line(e.T + 5.5f, "commentator", reason));
                    }

                    break;

                case "knockdown" when e.For == "解説" && e.Who.Length > 0:
                    if (Fit($"{e.Who}、{Polite(Regex.Replace(e.Text, $"^{Regex.Escape(e.Who)}は", ""))}") is { } why)
                    {
                        lines.Add(new Line(e.T + 3f, "commentator", why));
                    }

                    break;

                case "tired" when e.For == "解説" && round > 0 && !tiredSaid.Contains(round) && e.Text.Contains("尽き", StringComparison.Ordinal):
                    // 「Xはスタミナが尽きかけている。手数が落ち、パンチが軽くなる」の最初の文。
                    if (Fit(Polite(e.Text.Split('。')[0])) is { } tired && ((red.Length > 0 && tired.StartsWith(red, StringComparison.Ordinal)) || (blue.Length > 0 && tired.StartsWith(blue, StringComparison.Ordinal))))
                    {
                        lines.Add(new Line(e.T, "commentator", tired));
                        tiredSaid.Add(round);
                    }

                    break;
            }
        }

        var payload = new
        {
            lines = lines.OrderBy(l => l.At).Select(l => new Dictionary<string, object>
            {
                ["at"] = Math.Round(l.At, 1),
                ["speaker"] = l.Speaker,
                ["text"] = l.Text,
                ["excited"] = false,
            }),
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>
    /// 休みに様子を聞く選手: 傷のある方（「大きな傷はない」でない方）→ 疲れのひどい方 → ラウンドで赤・青を交互に。
    /// </summary>
    private static (string Who, string Text)? Subject(List<(float T, string For, string Type, string Who, string Text)> bodies, string red, string blue, int round)
    {
        var latest = bodies.Where(b => b.Who.Length > 0).GroupBy(b => b.Who).Select(g => g.Last()).ToList();
        if (latest.Count == 0)
        {
            return null;
        }

        static int Weight(string text) =>
            (text.Contains("大きな傷はない", StringComparison.Ordinal) ? 0 : 4)
            + (text.Contains("ほぼ尽き", StringComparison.Ordinal) ? 2 : text.Contains("疲れ", StringComparison.Ordinal) ? 1 : 0);
        var preferred = round % 2 == 1 ? red : blue;
        var pick = latest.OrderByDescending(b => Weight(b.Text)).ThenByDescending(b => b.Who == preferred).First();
        return (pick.Who, pick.Text);
    }

    /// <summary>
    /// 体の状態の記録（「X: 左目が腫れて見えにくい。スタミナはほぼ尽きている」）から解説の答え。傷があれば傷を、なければ疲れを、
    /// 理由を添えて（「〜です」で終わらせない: 実況と解説の役割、docs/TV_BROADCAST.md §4.1）。
    /// </summary>
    internal static string? Answer(string bodyText)
    {
        var body = bodyText.Contains(": ", StringComparison.Ordinal) ? bodyText[(bodyText.IndexOf(": ", StringComparison.Ordinal) + 2)..] : bodyText;
        var parts = body.Split('。', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var injury = parts.FirstOrDefault() ?? "";
        var stamina = parts.Skip(1).FirstOrDefault() ?? "";
        var breath = stamina switch
        {
            _ when stamina.Contains("ほぼ尽き", StringComparison.Ordinal) => "息が上がって、手が出なくなっています。",
            _ when stamina.Contains("疲れ", StringComparison.Ordinal) => "疲れで、手数が落ちてきました。",
            _ when stamina.Contains("まだある", StringComparison.Ordinal) => "体力は残っていて、まだ足が動きます。",
            _ => "",
        };
        if (injury.Contains("意識がない", StringComparison.Ordinal))
        {
            return "意識が戻っていません、立つのは難しいです。";
        }

        if (injury.Length > 0 && !injury.Contains("大きな傷はない", StringComparison.Ordinal))
        {
            var said = Polite(injury).TrimEnd('。'); // 記録の文（「肺に効いている」）を話し言葉に（「肺に効いています」）
            return Fit($"{said}。{breath}") ?? Fit($"{said}。");
        }

        return breath.Length > 0 ? Fit($"傷はありません。{breath}") ?? breath : null;
    }

    /// <summary>判定の見立て（「ここまではXがやや優勢」）を解説の言葉に。</summary>
    private static string? Lead(string score)
    {
        var m = Regex.Match(score, "^ここまでは(?<x>[^が]+)が");
        if (!m.Success)
        {
            return Fit(Polite(score));
        }

        var degree = score.Contains("大きく", StringComparison.Ordinal) ? "大きく" : score.Contains("やや", StringComparison.Ordinal) ? "少し" : "";
        return Fit($"ここまでは{m.Groups["x"].Value}が{degree}リードです。");
    }

    /// <summary>
    /// リプレイの場面（「…: Aの重い左がBのこめかみに」）の当たった所から、なぜ効くか。当たった所の言葉で引く解説の言い回しの表
    /// （台詞の選び方であって、試合の物理ではない）。
    /// </summary>
    internal static string? ReplayReason(string replay)
    {
        var scene = replay.Contains(": ", StringComparison.Ordinal) ? replay[(replay.IndexOf(": ", StringComparison.Ordinal) + 2)..] : replay;
        var hit = HitScene().Match(scene.Split(" →")[0]);
        if (!hit.Success)
        {
            return null;
        }

        var victim = hit.Groups["v"].Value;
        var zone = hit.Groups["zone"].Value;
        var reason = zone switch
        {
            _ when zone.Contains("こめかみ", StringComparison.Ordinal) => "こめかみで頭が振られました。",
            _ when zone.Contains("あご", StringComparison.Ordinal) => "あごで頭が揺れて、足にきます。",
            _ when zone.Contains("みぞおち", StringComparison.Ordinal) => "みぞおちで息が詰まります。",
            _ when zone.Contains("レバー", StringComparison.Ordinal) || zone.Contains("わき腹", StringComparison.Ordinal) => "レバーは遅れて足に効きます。",
            _ when zone.Contains("ボディ", StringComparison.Ordinal) || zone.Contains('腹') => "ボディは後から息に響きます。",
            _ when zone.Contains('顔') || zone.Contains('鼻') => "顔の真ん中にもらいました。",
            _ when zone.Contains('目') => "目に入って拳が見えにくい。",
            _ => null,
        };
        return reason is null ? null : Fit($"{victim}、{reason}") ?? reason;
    }

    [GeneratedRegex("^(?<a>.+?)の(?<blow>.+?)が(?<v>.+?)の(?<zone>.+?)に?$")]
    private static partial Regex HitScene();

    /// <summary>記録の文（「〜している」「〜ない」）を話し言葉の丁寧な形に（「〜しています。」）。</summary>
    internal static string Polite(string text)
    {
        var t = text.TrimEnd('。');
        t = Regex.Replace(t, "ている$", "ています");
        t = Regex.Replace(t, "でいる$", "でいます");
        t = Regex.Replace(t, "ない$", "ないです");
        t = Regex.Replace(t, "(上回って|優勢)$", "$1です");
        return t + "。";
    }

    private static string? Fit(string text) => text.Length <= MaxChars ? text : null;
}
