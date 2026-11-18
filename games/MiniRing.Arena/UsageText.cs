using System.Reflection;
using System.Text.RegularExpressions;

namespace MiniRing.Arena;

/// <summary>
/// コマンドの使い方（組み込みの Usage.txt）。1 つのコマンドは「2 字下げ＋コマンド名」で始まる行と、それより深く字下げした続きの行。
/// </summary>
internal static partial class UsageText
{
    private static readonly Lazy<string> Text = new(Load);

    public static string All => Text.Value;

    /// <summary>書いてあるコマンドの名前（書いてある順）。</summary>
    public static IReadOnlyList<string> Commands() =>
        CommandLine().Matches(All).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>1 つのコマンドの行と続きの行。書いていなければ null。</summary>
    public static string? Of(string command)
    {
        var lines = All.Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("  " + command + " ", StringComparison.Ordinal) || l.TrimEnd() == "  " + command);
        if (start < 0)
        {
            return null;
        }

        var end = start + 1;
        while (end < lines.Length && lines[end].StartsWith("   ", StringComparison.Ordinal) && !CommandLine().IsMatch(lines[end]))
        {
            end++;
        }

        return string.Join('\n', lines[start..end]).TrimEnd();
    }

    private static string Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MiniRing.Arena.Usage.txt")
            ?? throw new InvalidOperationException("Usage.txt が組み込まれていない（MiniRing.Arena.csproj）。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?m)^  ([a-z][a-z0-9-]*)(?= |$)")]
    private static partial Regex CommandLine();
}
