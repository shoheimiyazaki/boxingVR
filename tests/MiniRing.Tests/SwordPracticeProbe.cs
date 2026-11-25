using MiniRing.Arena;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 剣士の型の練習（<see cref="SwordPractice"/>）を tower32 で回す入口（調査用。<c>bash tools/remote-test.sh probe:SwordPracticeProbe.&lt;名前&gt;</c>）。
/// 結果は標準出力（trx）に出す。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class SwordPracticeProbe
{
    private static readonly string[] Swordsmen = ["jin", "konrad", "edgar"];

    [TestMethod]
    public void Baseline()
    {
        foreach (var id in Swordsmen)
        {
            Console.WriteLine(SwordPractice.Baseline(CharacterRoster.Get(id), [1, 2, 3, 4], ["standard"], Environment.ProcessorCount, TextWriter.Null));
        }
    }
}
