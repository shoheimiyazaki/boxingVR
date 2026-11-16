using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MiniRing.Arena;

/// <summary>
/// テレビ中継の台本を書くための要約: 中継の記録（<c>*.events.json</c>、<see cref="MiniRing.Audio.Broadcast"/> が書く）を、
/// 区間（冒頭・ラウンド・休み・決着・勝敗の画面）ごとの時刻つきの箇条書きにする。実況用（画面で見えること）と解説用（体の中・狙い）を
/// 分けたまま並べ、同じ内容のくり返し（同じ選手の狙い・疲れ・効いている、の連続）は落とす。台本を書く人（エージェント）はこれだけを読めばよい。
/// </summary>
internal static class TvDigest
{
    /// <summary>同じ選手の同じ種類の出来事を落とす間隔 [s]（台本の役に立たない連続を減らす）。</summary>
    private static readonly Dictionary<string, float> RepeatWindow = new()
    {
        ["technique"] = 4f,
        ["defense"] = 3f,
        ["effect"] = 8f,
        ["body_hurt"] = 15f,
        ["tired"] = 20f,
    };

    /// <summary>events.json の中身から要約の Markdown を作る。</summary>
    public static string Build(string eventsJson)
    {
        using var doc = JsonDocument.Parse(eventsJson);
        var root = doc.RootElement;
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var events = root.GetProperty("events").EnumerateArray()
            .Select(e => new Event(
                (float)e.GetProperty("t").GetDouble(),
                e.TryGetProperty("for", out var f) ? f.GetString() ?? "" : "",
                e.GetProperty("type").GetString() ?? "",
                e.TryGetProperty("who", out var w) ? w.GetString() ?? "" : "",
                e.GetProperty("text").GetString() ?? ""))
            .OrderBy(e => e.T)
            .ToList();

        var sb = new StringBuilder();
        var end = events.LastOrDefault(e => e.Type == "end")?.T;
        var result = events.FirstOrDefault(e => e.Type == "result")?.T;
        sb.AppendLine(CultureInfo.InvariantCulture, $"# 台本の資料: {Text("event")} {Text("bout")}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- 赤コーナー: {Fallback(Text("redProfile"), Text("red"))}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- 青コーナー: {Fallback(Text("blueProfile"), Text("blue"))}");
        if (end is { } e0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- 動画の長さ: {e0:0.0} 秒（勝敗の画面は {result ?? e0:0.0} 秒から）");
        }

        // 両者のプロフィール（経歴・人柄・戦績・エピソード）。実況・解説が選手を語る資料（キャラクターの軸）。
        foreach (var (key, corner) in new[] { ("redDossier", "赤コーナー"), ("blueDossier", "青コーナー") })
        {
            if (Text(key) is { Length: > 0 } dossier)
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"## {corner}のプロフィール");
                sb.AppendLine();
                sb.Append(dossier);
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 台本の決まり（docs/TV_BROADCAST.md の要約）");
        sb.AppendLine();
        sb.AppendLine("- 実況（announcer）は「実況」の出来事（画面で見えること）を短く伝え、要所（ダウン・骨折・切断・リプレイ・休み）で解説に問いを振る。");
        sb.AppendLine("- 解説（commentator、元王者のサエキさん）は「解説」の出来事（体の中・狙い）を、ゲームを知らない人に一言で分かる言葉で答える。");
        sb.AppendLine("- 数字はラウンド・時間・「心臓の3割が失われた」のような体の割合だけ。ダメージの数値や「打撲が8割」は言わない。ゲームの名前（VGV）やボット・物理の話はしない（作中のテレビ中継）。");
        sb.AppendLine("- 長さの目安: 実況 1 字 0.15〜0.19 秒、解説 0.18〜0.24 秒、台詞の間 0.15 秒。前の台詞が長いと次は後ろへずれる。ダウン・決着のコールは出来事の 0.5 秒以内に。");
        sb.AppendLine();

        var sections = Sections(events, end);
        sb.AppendLine("## 区間");
        sb.AppendLine();
        sb.AppendLine("| 開始 [s] | 終了 [s] | 区間 |");
        sb.AppendLine("|---|---|---|");
        foreach (var (from, to, title) in sections)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {from:0.0} | {to:0.0} | {title} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 出来事");
        var last = new Dictionary<(string Type, string Who), (float T, string Text)>();
        string? lastCondition = null;
        foreach (var (from, to, title) in sections)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"### {title}（{from:0.0}〜{to:0.0} s）");
            sb.AppendLine();
            foreach (var ev in events.Where(e => e.T >= from && (e.T < to || (to == sections[^1].To && e.T <= to))))
            {
                if (RepeatWindow.TryGetValue(ev.Type, out var window) && last.TryGetValue((ev.Type, ev.Who), out var prev) && ev.T - prev.T < window
                    && (ev.Type is "technique" or "defense" || prev.Text == ev.Text))
                {
                    continue;
                }

                if (ev.Type == "condition")
                {
                    if (ev.Text == lastCondition)
                    {
                        continue;
                    }

                    lastCondition = ev.Text;
                }

                last[(ev.Type, ev.Who)] = (ev.T, ev.Text);
                var role = ev.For.Length > 0 ? ev.For : "資料";
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {ev.T:0.0} {role}: {ev.Text}");
            }
        }

        return sb.ToString();
    }

    /// <summary>区間の切れ目: 冒頭 → ラウンド → 休み → … → 決着の後（決着のリプレイ）→ 勝敗の画面。</summary>
    private static List<(float From, float To, string Title)> Sections(List<Event> events, float? end)
    {
        var marks = new List<(float T, string Title)>();
        foreach (var e in events)
        {
            switch (e.Type)
            {
                case "program":
                    marks.Add((e.T, "本日のプログラム"));
                    break;
                case "opening":
                    marks.Add((e.T, "冒頭のタイトル"));
                    break;
                case "round_start":
                    marks.Add((e.T, e.Text.Replace("開始", "", StringComparison.Ordinal)));
                    break;
                case "round_end":
                    marks.Add((e.T, "休み（コーナー・リプレイ）"));
                    break;
                case "ko" or "decision":
                    marks.Add((e.T, "決着の後（倒れた様子・決着のリプレイ）"));
                    break;
                case "result":
                    marks.Add((e.T, "勝敗の画面"));
                    break;
            }
        }

        if (marks.Count == 0 || marks[0].T > 0f)
        {
            marks.Insert(0, (0f, "冒頭"));
        }

        var stop = end ?? (events.Count > 0 ? events[^1].T + 1f : 1f);
        var sections = new List<(float, float, string)>();
        for (var i = 0; i < marks.Count; i++)
        {
            var to = i + 1 < marks.Count ? marks[i + 1].T : stop;
            if (to > marks[i].T || i == marks.Count - 1)
            {
                sections.Add((marks[i].T, MathF.Max(to, marks[i].T), marks[i].Title));
            }
        }

        return sections;
    }

    private static string Fallback(string value, string fallback) => value.Length > 0 ? value : fallback;

    private sealed record Event(float T, string For, string Type, string Who, string Text);
}
