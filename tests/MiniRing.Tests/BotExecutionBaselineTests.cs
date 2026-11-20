using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// ボットの判断と実行（技の経路 → 指令 → 体）を、同じ種で 2 回回すと毎歩のチェックサムが一致する。
/// 2026-11-20: 2 種 × 1200 歩と外部ファイルの証跡（切り出し前の値）を、1 種 600 歩の同じビルド内の比較に縮めた。
/// 決定性の本体は <see cref="ReplayTests"/>（記録 → 再生のビット一致）と FixedSimulationTests。ここはボットの実行の経路を短く見るだけ。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class BotExecutionBaselineTests
{
    [TestMethod]
    public void Execution_IsDeterministic_StepByStep()
    {
        CollectionAssert.AreEqual(Checkpoints(), Checkpoints(), "同じ種・同じ入力なら毎歩のチェックサムが一致すること");
    }

    private static string[] Checkpoints()
    {
        const int seed = 3;
        var actual = new List<string>();
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("champion",
            s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s)));
        for (var step = 0; step < 600; step++)
        {
            game.Step(default, 1f / 60f);
            actual.Add(string.Create(CultureInfo.InvariantCulture, $"{seed}:{step + 1}:{game.StateChecksum():X16}"));
        }

        return actual.ToArray();
    }
}
