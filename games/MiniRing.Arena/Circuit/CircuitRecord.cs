using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRing.Arena.Circuit;

/// <summary>
/// 1 試合の記録（<c>league/&lt;org&gt;/bouts.jsonl</c> の 1 行）。再現に要る物（コミット・版・種・両者のファイルとハッシュ・団体・決まり・歩の幅）と結果。
/// 戦績・プロフィールの数字はすべてここから出す（手で写さない）。
/// </summary>
internal sealed record BoutRecord
{
    public required string Id { get; init; }
    public int Season { get; init; }
    public int Event { get; init; }

    /// <summary>"prospect"（新人どうし）・"challenge"（ランキングへの挑戦）・"title"（王座戦・王座決定戦）。</summary>
    public required string Kind { get; init; }
    public required string Org { get; init; }
    public required string Division { get; init; }
    public required string Commit { get; init; }
    public int Physics { get; init; }
    public int Logic { get; init; }
    public int Seed { get; init; }
    public int Rounds { get; init; }
    public float RoundSeconds { get; init; }
    public float BreakSeconds { get; init; }
    public float Speed { get; init; }
    public int Fps { get; init; }
    public required string Red { get; init; }
    public required string Blue { get; init; }
    public required string RedFile { get; init; }
    public required string BlueFile { get; init; }
    public required string RedHash { get; init; }
    public required string BlueHash { get; init; }

    /// <summary>"red"・"blue"、引き分けは null。</summary>
    public string? Winner { get; init; }

    /// <summary>"KO"（倒して・3 ノックダウンを含む決着）・"DEC"（判定）・"DRAW"。</summary>
    public required string Method { get; init; }
    public string Finish { get; init; } = "";
    public int Round { get; init; }
    public float Time { get; init; }
    public float RedDealt { get; init; }
    public float BlueDealt { get; init; }
    public int RedDowns { get; init; }
    public int BlueDowns { get; init; }
    public float Drama { get; init; }
    public required string Checksum { get; init; }

    public string? WinnerId => Winner switch { "red" => Red, "blue" => Blue, _ => null };

    public string? LoserId => Winner switch { "red" => Blue, "blue" => Red, _ => null };

    public bool Involves(string id) => Red == id || Blue == id;

    public string OpponentOf(string id) => Red == id ? Blue : Red;
}

/// <summary>全試合の記録（1 行 1 試合の JSON）。書き足すだけで、書き換えない。</summary>
internal static class BoutLedger
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ToLine(BoutRecord bout) => JsonSerializer.Serialize(bout, Options);

    public static BoutRecord FromLine(string line)
        => JsonSerializer.Deserialize<BoutRecord>(line, Options) ?? throw new InvalidDataException("空の試合の記録です。");

    /// <summary>記録を読む（無ければ空。壊れた行は何行目かを書いた例外）。id の重複も断る。</summary>
    public static IReadOnlyList<BoutRecord> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var list = new List<BoutRecord>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var n = 0;
        foreach (var line in File.ReadLines(path))
        {
            n++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            BoutRecord bout;
            try
            {
                bout = FromLine(line);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{path} の {n} 行目が読めません: {ex.Message}", ex);
            }

            if (!ids.Add(bout.Id))
            {
                throw new InvalidDataException($"{path} の {n} 行目: 試合 {bout.Id} が 2 度あります。");
            }

            list.Add(bout);
        }

        return list;
    }

    /// <summary>書き足す（今ある id と重なれば断る）。</summary>
    public static void Append(string path, IEnumerable<BoutRecord> bouts)
    {
        var existing = Read(path).Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var b in bouts)
        {
            if (!existing.Add(b.Id))
            {
                throw new InvalidOperationException($"試合 {b.Id} はもう記録にあります。");
            }

            sb.Append(ToLine(b)).Append('\n');
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.AppendAllText(path, sb.ToString());
    }
}

/// <summary>1 人の戦績（記録から数える）。</summary>
internal sealed record FighterTally(string Id, int Wins, int Losses, int Draws, int Kos, int KoLosses, int Streak, float Dealt, float Taken, float Points)
{
    public int Bouts => Wins + Losses + Draws;

    /// <summary>今の連敗の数（最後の試合から数えて続く負け）。</summary>
    public int LosingStreak => Streak < 0 ? -Streak : 0;

    /// <summary>記録の順（並びの順）で、この選手の試合を数える。点は団体の順位の点（勝ち・引き分け・倒して勝った分）。</summary>
    public static FighterTally Of(string id, IEnumerable<BoutRecord> bouts, OrgRanking points)
    {
        int w = 0, l = 0, d = 0, ko = 0, kol = 0, streak = 0;
        float dealt = 0f, taken = 0f, pts = 0f;
        foreach (var b in bouts.Where(b => b.Involves(id)))
        {
            var red = b.Red == id;
            dealt += red ? b.RedDealt : b.BlueDealt;
            taken += red ? b.BlueDealt : b.RedDealt;
            var finish = b.Method == "KO";
            if (b.WinnerId is null)
            {
                d++;
                pts += points.Draw;
                streak = 0;
            }
            else if (b.WinnerId == id)
            {
                w++;
                ko += finish ? 1 : 0;
                pts += points.Win + (finish ? points.FinishBonus : 0f);
                streak = streak > 0 ? streak + 1 : 1;
            }
            else
            {
                l++;
                kol += finish ? 1 : 0;
                streak = streak < 0 ? streak - 1 : -1;
            }
        }

        return new FighterTally(id, w, l, d, ko, kol, streak, dealt, taken, pts);
    }

    /// <summary>「12勝3敗1分（8KO）」。</summary>
    public string Text => string.Create(CultureInfo.InvariantCulture, $"{Wins}勝{Losses}敗{(Draws > 0 ? $"{Draws}分" : "")}（{Kos}KO）");
}

/// <summary>引退・組み合わせ・順位の規則（戦績だけから。データは <see cref="CircuitConfig"/>）。</summary>
internal static class CircuitRules
{
    /// <summary>引退の理由（現役なら null）。どの規則も負けを足すと解けない（単調）。</summary>
    public static string? RetireReason(FighterTally t, RetireRules rules)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(rules);
        if (t.Losses - t.Wins >= rules.NetLosses)
        {
            return $"通算の負け越し {t.Losses - t.Wins}";
        }

        if (t.LosingStreak >= rules.LosingStreak)
        {
            return $"{t.LosingStreak} 連敗";
        }

        if (t.KoLosses >= rules.KoLosses)
        {
            return $"倒されての負け {t.KoLosses} 回";
        }

        return null;
    }

    /// <summary>順位の並び: 点 → 勝ち → 倒した勝ち → 与えた − 受けた → id。</summary>
    public static IReadOnlyList<FighterTally> Order(IEnumerable<FighterTally> tallies)
        => [.. tallies.OrderByDescending(t => t.Points).ThenByDescending(t => t.Wins).ThenByDescending(t => t.Kos)
            .ThenByDescending(t => t.Dealt - t.Taken).ThenBy(t => t.Id, StringComparer.Ordinal)];

    /// <summary>
    /// 同じくらいの戦績どうしの組み合わせ（スイス式）: 順位の並びで、上から順に、まだ当たっていない一番近い相手と組む（全員と当たっていれば隣）。
    /// 奇数なら、休んだ回数が一番少ない人のうち一番下が休む。赤 = 上位の側。
    /// </summary>
    public static (IReadOnlyList<(string Red, string Blue)> Pairs, string? Bye) Pair(IReadOnlyList<FighterTally> ordered, IReadOnlyList<BoutRecord> history,
        IReadOnlyDictionary<string, int> byes)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(byes);
        var pool = ordered.Select(t => t.Id).ToList();
        string? bye = null;
        if (pool.Count % 2 == 1)
        {
            bye = pool.Select((id, i) => (id, i)).OrderBy(p => byes.GetValueOrDefault(p.id)).ThenByDescending(p => p.i).First().id;
            pool.Remove(bye);
        }

        var met = new HashSet<(string, string)>();
        foreach (var b in history)
        {
            met.Add((b.Red, b.Blue));
            met.Add((b.Blue, b.Red));
        }

        var pairs = new List<(string, string)>();
        while (pool.Count > 0)
        {
            var a = pool[0];
            pool.RemoveAt(0);
            var j = pool.FindIndex(b => !met.Contains((a, b)));
            j = j < 0 ? 0 : j;
            pairs.Add((a, pool[j]));
            pool.RemoveAt(j);
        }

        return (pairs, bye);
    }
}
