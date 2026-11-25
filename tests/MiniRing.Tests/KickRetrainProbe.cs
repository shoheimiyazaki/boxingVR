using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（docs/plans/FORM_DECAY_STUDY.md）: 回し蹴り・前蹴りの練習を、形の代価（<see cref="FormCost"/>）と比べ直しの門を入れた点で、tower32 でやり直す
/// （`bash tools/remote-test.sh probe:KickRetrainProbe`）。採った差分の JSON を出力に出すので、`games/MiniRing/Bots/kicks/*.tune.json` へ写す。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class KickRetrainProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void RetrainWithFormCost()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kickretrain-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        KickShin.Run(KickShin.Heights, practice: true, gens: 8, lambda: 12, parallel: Math.Max(1, Environment.ProcessorCount - 2), dir, Console.WriteLine);
        foreach (var file in Directory.GetFiles(dir, "*.tune.json").Order())
        {
            Console.WriteLine($"=== {Path.GetFileName(file)}");
            Console.WriteLine(File.ReadAllText(file));
        }
    }
}
