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

    /// <summary>練習する型（基準の表で、頭・首へ届く打ち下ろし・払い・斬り上げと、点の高かった型）。</summary>
    private static readonly Dictionary<string, string[]> Practice = new()
    {
        ["jin"] = ["kesa", "shomen", "harai-men", "kiriage", "hiki-men"],
        ["konrad"] = ["oberhau", "zornhau", "scheitelhau", "unterhau"],
        ["edgar"] = ["kiri-oroshi", "yoko-barai", "tsuki"],
    };

    [TestMethod]
    [DataRow("jin")]
    [DataRow("konrad")]
    [DataRow("edgar")]
    public void Train(string id)
    {
        var who = CharacterRoster.Get(id);
        var adopted = new Dictionary<string, MiniRing.Motion.FormTune>();
        foreach (var form in Practice[id])
        {
            var (tune, report) = SwordPractice.Train(who, form, generations: 30, lambda: 12, sigma: 0.4f, seed: 1, seeds: [1, 2, 3, 4], victims: ["standard"],
                heldSeeds: [11, 12, 13, 14, 15, 16], heldVictims: ["standard", "heavy"], parallel: Environment.ProcessorCount, log: TextWriter.Null);
            Console.WriteLine(report);
            if (tune is not null)
            {
                adopted[form] = tune;
            }
        }

        Console.WriteLine($"ADOPTED {id} " + System.Text.Json.JsonSerializer.Serialize(adopted));
    }
}
