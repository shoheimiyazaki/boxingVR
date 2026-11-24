using System.Globalization;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// 中継の画面に出す言葉の言語（描画とラベルだけ。試合の計算には関わらない: CLAUDE.md §1.6）。
/// 言語そのものは <see cref="DisplayLanguage"/>（名簿・団体のファイルの <c>locales</c> も同じ言語で重なる）。ここは画面の文字の置き場所で使う入口と、
/// ゲームの文・決まり手の英訳（<see cref="Method"/>・<see cref="Cause"/>）。日本語では今までの文字がそのまま出る。
/// </summary>
internal static class TvLocale
{
    /// <summary>起動時に 1 度だけ呼ぶ（<c>--lang</c>）。名簿・団体の読み込みより前に。</summary>
    public static void Set(string? language) => DisplayLanguage.Current = DisplayLanguage.Validate(language ?? DisplayLanguage.Japanese);

    /// <summary>英語か。</summary>
    public static bool IsEnglish => DisplayLanguage.IsEnglish;

    /// <summary>いまの言語で言葉を選ぶ（日本語, 英語）。</summary>
    public static string T(string ja, string en) => DisplayLanguage.T(ja, en);

    /// <summary>戦績の短い言い方（比べる表・入場の帯）。日本語「38勝2敗（33KO）」、英語「38-2 (33 KO)」（引き分けがあれば 38-2-1）。</summary>
    public static string Record(FightRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return IsEnglish
            ? string.Create(CultureInfo.InvariantCulture, $"{r.Wins}-{r.Losses}{(r.Draws > 0 ? $"-{r.Draws}" : "")} ({r.Kos} KO)")
            : string.Create(CultureInfo.InvariantCulture, $"{r.Wins}勝{r.Losses}敗{(r.Draws > 0 ? $"{r.Draws}分" : "")}（{r.Kos}KO）");
    }

    /// <summary>英語の「1 rounds」を「1 round」に（団体の決まりの文は {rounds} を数で差し込むだけなので）。日本語はそのまま。</summary>
    public static string Singular(string line) => IsEnglish ? System.Text.RegularExpressions.Regex.Replace(line, @"1 rounds", "1 round") : line;

    /// <summary>決まり手（<see cref="TvResult.Method"/> や札の <c>method</c>。日本語の値）を画面の言葉に。知らない言葉はそのまま。</summary>
    public static string Method(string method) => !IsEnglish ? method : method switch
    {
        "判定" => "Decision",
        "引き分け" => "Draw",
        "リングアウト" => "Ring Out",
        _ => method, // "K.O." "T.K.O." は共通
    };

    /// <summary>決まり手の説明（<see cref="TvResult.Cause"/> の日本語の値）を画面の言葉に。知らない言葉はそのまま。</summary>
    public static string Cause(string cause)
    {
        if (!IsEnglish)
        {
            return cause;
        }

        switch (cause)
        {
            case "リングの外へ": return "out of the ring";
            case "10 カウント": return "10-count";
            case "失神": return "knocked out cold";
        }

        const string Suffix = "の破壊";
        if (cause.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return $"{Organ(cause[..^Suffix.Length])} destroyed";
        }

        // 「1 ラウンドに 3 度のダウン」
        var m = System.Text.RegularExpressions.Regex.Match(cause, @"^1 ラウンドに (\d+) 度のダウン$");
        return m.Success ? $"{m.Groups[1].Value} knockdowns in one round" : cause;
    }

    /// <summary>臓器・部位の名前（<see cref="TvResult.DeathOrgan"/> の日本語の値）を英語に。</summary>
    public static string Organ(string organ) => organ switch
    {
        "脳" => "brain",
        "心臓" => "heart",
        "肺" => "lung",
        "肝臓" => "liver",
        "内臓" => "organs",
        "首" => "neck",
        _ => organ,
    };

    /// <summary>死因の言葉（<see cref="TvResult.DeathPhrase"/> の画面用。「首を断たれて」「脳を壊されて」）。</summary>
    public static string DeathPhrase(TvResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!IsEnglish || result.DeathPhrase.Length == 0)
        {
            return result.DeathPhrase;
        }

        return result.DeathOrgan == "首" ? "NECK SEVERED" : $"{Organ(result.DeathOrgan).ToUpperInvariant()} DESTROYED";
    }

    /// <summary>
    /// リプレイの下の場面の説明（<see cref="TvHighlight.Text"/>。中継の記録と同じ日本語の文）を画面の言葉に。日本語はそのまま。
    /// 英語は文の形「A の[強さ]左|右|蹴り|肘|武器 が V の 部位 に → ダウン → 〇〇骨折 → K.O.（…）」から組み直す
    /// （例「Vikram's right to Albert's solar plexus → FRACTURE → DOWN」）。形に合わない文は、続きの印（→ …）だけを英語にする。
    /// 2026-11-25: 英語版の番組でリプレイの説明が日本語のまま出た。
    /// </summary>
    public static string ReplayCaption(string text) => ReplayCaption(text, IsEnglish);

    /// <summary><see cref="ReplayCaption(string)"/> の言語を引数で決める形（テストが全体の言語に触れずに確かめる）。</summary>
    public static string ReplayCaption(string text, bool english)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!english)
        {
            return text;
        }

        var parts = text.Split(" → ", StringSplitOptions.None);
        var head = parts[0];
        var hit = System.Text.RegularExpressions.Regex.Match(head, @"^(?<a>.+?)の(?:強烈な|重い|いい)?(?<tool>.+?)が(?<v>.+?)の(?<zone>.+?)に$");
        var lead = hit.Success
            ? $"{hit.Groups["a"].Value}'s {Tool(hit.Groups["tool"].Value)} to {hit.Groups["v"].Value}'s {Zone(hit.Groups["zone"].Value)}"
            : System.Text.RegularExpressions.Regex.Replace(head, @"\s*(ダウン|K\.O\..*)$", "").Trim();
        var tags = parts.Skip(1).Select(Tag).Where(t => t.Length > 0);
        if (!hit.Success && head.EndsWith("ダウン", StringComparison.Ordinal))
        {
            tags = tags.Prepend("DOWN");
        }
        else if (!hit.Success && head.Contains("K.O.", StringComparison.Ordinal))
        {
            tags = tags.Prepend("K.O.");
        }

        return string.Join(" → ", new[] { lead }.Concat(tags).Where(t => t.Length > 0));

        static string Tool(string tool) => tool switch
        {
            "左" => "left",
            "右" => "right",
            "蹴り" => "kick",
            "肘" => "elbow",
            _ => tool.Any(c => c > 0x7F) ? "weapon" : tool,
        };

        static string Zone(string zone) => zone switch
        {
            "あご" => "chin",
            "こめかみ" => "temple",
            "顔面" => "face",
            "首" => "neck",
            "心臓の上" => "heart",
            "レバー（右わき腹）" => "liver",
            "みぞおち" => "solar plexus",
            "ボディ" => "body",
            "肩" => "shoulder",
            "肘" => "elbow",
            "ガードの上" => "guard",
            "膝" => "knee",
            _ => "leg",
        };

        static string Tag(string tag) => tag.StartsWith("ダウン", StringComparison.Ordinal) ? "DOWN"
            : tag.StartsWith("K.O.", StringComparison.Ordinal) ? "K.O."
            : tag.EndsWith("骨折", StringComparison.Ordinal) ? "FRACTURE"
            : tag.EndsWith("切断", StringComparison.Ordinal) ? "SEVERED"
            : tag.Any(c => c > 0x7F) ? "" : tag;
    }
}
