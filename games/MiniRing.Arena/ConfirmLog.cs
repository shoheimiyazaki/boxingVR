using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRing.Arena;

/// <summary>
/// 採否の比べ直し（使わなかった種・固定の n、<see cref="PairedEvaluation.Confirm"/>）の結果を、どの道具でも同じ形の 1 行の JSON で
/// <c>docs/arena/confirm.jsonl</c> に足す台帳。エージェントは道具ごとの md の表を読み解かずに、ここから「何を何と比べ、どう決まったか」を引ける。
/// 1 行 = { date, tool, question, a, b, versions, seeds, result（PairedResult の全部と Verdict・TValue）, detail }。
/// </summary>
internal static class ConfirmLog
{
    /// <summary>台帳の場所（リポジトリの根から。Arena はリポジトリの根で動かす決まり: CLAUDE.md §5）。</summary>
    public const string DefaultPath = "docs/arena/confirm.jsonl";

    // 差が全部の種で同じだと t 値は ±∞（PairedResult.TValue）。標準の JSON は ±∞・NaN を書けないので文字列で書く。
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static readonly Lock Gate = new();

    /// <summary>1 件を足す。書けなくても測定そのものは止めない（理由を標準エラーに出す）。</summary>
    public static void Append(string tool, string question, string a, string b, IReadOnlyList<int> seeds, PairedResult result,
        IReadOnlyDictionary<string, string>? detail = null, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(result);
        if (path == DefaultPath && !File.Exists("CLAUDE.md"))
        {
            // リポジトリの根の外（テストの出力の場所など）では台帳を作らない
            Console.Error.WriteLine($"confirm.jsonl には書かない（リポジトリの根で動かしていない: {Environment.CurrentDirectory}）。");
            return;
        }

        var entry = new Dictionary<string, object?>
        {
            ["date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["tool"] = tool,
            ["question"] = question,
            ["a"] = a,
            ["b"] = b,
            ["versions"] = $"physics {Fighter.PhysicsVersion} logic {Bots.BoxerBot.LogicVersion}",
            ["seeds"] = seeds.Count <= 8 ? string.Join(',', seeds) : $"{seeds[0]}..{seeds[^1]} ({seeds.Count})",
            ["result"] = result,
            ["detail"] = detail,
        };
        try
        {
            var line = JsonSerializer.Serialize(entry, Json);
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(path, line + "\n");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or JsonException)
        {
            Console.Error.WriteLine($"confirm.jsonl に書けなかった（{path}）: {e.Message}");
        }
    }
}
