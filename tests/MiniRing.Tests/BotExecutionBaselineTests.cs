using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Tests;

[TestClass]
public sealed class BotExecutionBaselineTests
{
    [TestMethod]
    public void Execution_IsDeterministic_AndPreservesCapturedBaselineWhenRequested()
    {
        var actual = Checkpoints();
        // Bepu の決定性の保証は同じマシン内。通常 CI に CPU 固有の固定値を強制しない。
        var expected = Environment.GetEnvironmentVariable("MINIRING_VERIFY_EXTRACTION") == "1"
            ? File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "bot-execution-baseline.txt"))
            : Checkpoints();
        CollectionAssert.AreEqual(expected, actual, "同じ環境の実行（または明示した切り出し前の証跡）と一致すること");
    }

    private static string[] Checkpoints()
    {
        var actual = new List<string>();
        foreach (var seed in new[] { 3, 17 })
        {
            using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("champion",
                s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s)));
            for (var step = 0; step < 1200; step++)
            {
                game.Step(default, 1f / 60f);
                actual.Add(string.Create(CultureInfo.InvariantCulture, $"{seed}:{step + 1}:{game.StateChecksum():X16}"));
            }
        }

        return actual.ToArray();
    }
}
