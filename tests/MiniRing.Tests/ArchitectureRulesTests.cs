using System.Text.RegularExpressions;

namespace MiniRing.Tests;

/// <summary>
/// CLAUDE.md の軸のうち、ソースの書き方で破られうるものを機械で確かめる（どのエージェントが書いても同じ所で止まるように）。
/// <list type="bullet">
/// <item>§1.6 決定的であること: 試合を計算するコード（games/MiniRing・src/Aigis.Physics・src/Aigis.Foundation）に、種なしの乱数・時計を書かない。
/// 試合の外で使う所（記録のファイル名・見出しの日時など）は、その行かすぐ上の行に <c>// determinism-ok: 理由</c> を書く。</item>
/// <item>§3 Bepu の型は Aigis.Physics の外へ出さない: ほかのプロジェクトのソースが BepuPhysics・BepuUtilities を参照しない。</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ArchitectureRulesTests
{
    private const string DeterminismMarker = "determinism-ok:";

    private static readonly (Regex Pattern, string Why)[] Nondeterministic =
    [
        (new Regex(@"new\s+(System\.)?Random\s*\(\s*\)"), "種なしの Random（種を渡す: new Random(seed)）"),
        (new Regex(@"\bRandom\.Shared\b"), "共有の Random（種が無い）"),
        (new Regex(@"\bDateTime(Offset)?\.(Now|UtcNow|Today)\b"), "時計"),
        (new Regex(@"\bEnvironment\.TickCount(64)?\b"), "時計"),
        (new Regex(@"\bGuid\.NewGuid\s*\("), "毎回違う値"),
    ];

    private static readonly Regex BepuReference = new(@"^\s*(global\s+)?using\s+(static\s+)?Bepu|\bglobal::Bepu|\bBepu(Physics|Utilities)\.[A-Z]");

    [TestMethod]
    public void MatchCode_HasNoClockOrUnseededRandom_ExceptMarkedLines()
    {
        var root = RepoRoot();
        var violations = new List<string>();
        foreach (var dir in new[] { "games/MiniRing", "src/Aigis.Physics", "src/Aigis.Foundation" })
        {
            foreach (var (path, lines) in Sources(Path.Combine(root, dir)))
            {
                for (var i = 0; i < lines.Length; i++)
                {
                    var code = StripComment(lines[i]);
                    foreach (var (pattern, why) in Nondeterministic)
                    {
                        if (pattern.IsMatch(code) && !lines[i].Contains(DeterminismMarker) && (i == 0 || !lines[i - 1].Contains(DeterminismMarker)))
                        {
                            violations.Add($"{Path.GetRelativePath(root, path)}:{i + 1}: {why}: {lines[i].Trim()}");
                        }
                    }
                }
            }
        }

        Assert.AreEqual(0, violations.Count,
            "試合の計算に時計・種なしの乱数を入れない（CLAUDE.md §1.6）。試合の外の用途なら // " + DeterminismMarker + " 理由 を書く:\n" + string.Join("\n", violations));
    }

    [TestMethod]
    public void OnlyAigisPhysics_ReferencesBepu()
    {
        var root = RepoRoot();
        var violations = new List<string>();
        foreach (var dir in new[] { "games", "src", "tests" })
        {
            foreach (var (path, lines) in Sources(Path.Combine(root, dir)))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.StartsWith("src/Aigis.Physics/", StringComparison.Ordinal))
                {
                    continue;
                }

                for (var i = 0; i < lines.Length; i++)
                {
                    if (BepuReference.IsMatch(StripComment(lines[i])))
                    {
                        violations.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.AreEqual(0, violations.Count, "Bepu の型は Aigis.Physics の外へ出さない（CLAUDE.md §3）:\n" + string.Join("\n", violations));
    }

    private static string StripComment(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
        {
            return string.Empty;
        }

        var at = line.IndexOf("//", StringComparison.Ordinal);
        return at >= 0 ? line[..at] : line;
    }

    private static IEnumerable<(string Path, string[] Lines)> Sources(string dir)
    {
        foreach (var path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var sep = Path.DirectorySeparatorChar;
            if (path.Contains($"{sep}bin{sep}", StringComparison.Ordinal) || path.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (path, File.ReadAllLines(path));
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) && Directory.Exists(Path.Combine(dir.FullName, "games")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("リポジトリの根（CLAUDE.md と games/ がある所）が見つからない。");
    }
}
