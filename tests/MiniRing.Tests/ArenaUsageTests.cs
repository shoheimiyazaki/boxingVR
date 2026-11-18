using System.Text.RegularExpressions;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>Arena のコマンドは全部 Usage.txt に書いてあり（<c>miniring-arena help</c> で引ける）、書いてある物は全部 switch にある。</summary>
[TestClass]
public sealed class ArenaUsageTests
{
    [TestMethod]
    public void EveryDispatchedCommand_IsInUsage_AndEveryUsageCommandIsDispatched()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "games", "MiniRing.Arena", "Program.cs"));
        var body = program[program.IndexOf("return command switch", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("_ => Usage(", StringComparison.Ordinal)];
        var dispatched = Regex.Matches(body, @"(?m)^\s*""([a-z][a-z0-9-]*)""(?:\s+or\s+""[^""]+"")*\s*=>")
            .Select(m => m.Groups[1].Value).Where(n => n != "help").ToHashSet(StringComparer.Ordinal);
        var documented = UsageText.Commands().ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(dispatched.Order(StringComparer.Ordinal).ToList(), documented.Order(StringComparer.Ordinal).ToList(),
            $"switch にだけある: {string.Join(", ", dispatched.Except(documented))} / Usage.txt にだけある: {string.Join(", ", documented.Except(dispatched))}");
    }

    [TestMethod]
    public void HelpOfOneCommand_GivesItsLineAndContinuation()
    {
        var tune = UsageText.Of("tune");
        Assert.IsNotNull(tune);
        StringAssert.StartsWith(tune, "  tune ");
        StringAssert.Contains(tune, "スタイルの自動調整");
        Assert.IsNull(UsageText.Of("no-such-command"));
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

        throw new InvalidOperationException("リポジトリの根が見つからない。");
    }
}
