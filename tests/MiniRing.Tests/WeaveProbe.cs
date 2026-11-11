using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// ウィービングの調査用: 合図なし（自分の目と読みで反応する）で、左右のフックとコンビネーションのフックをどれだけきれいに通過させるか。
/// </summary>
[TestClass]
public sealed class WeaveProbe
{
    [TestMethod]
    public void WeaveAgainstHooks_WithoutACue()
    {
        foreach (var (name, style) in new[] { ("swarmer", BotCatalog.Swarmer), ("champion", BotCatalog.Champion) })
        {
            foreach (var punch in new[] { "3", "4" })
            {
                foreach (var prediction in new[] { 0f, 1f })
                {
                    var defender = DefenseDrill.Defender(style.Defenses, defend: true, counters: false) with
                    {
                        GuardHeight = style.GuardHeight, GuardWidth = style.GuardWidth, GuardReach = style.GuardReach, Prediction = prediction,
                    };
                    var total = PunchDrillResult.Empty;
                    foreach (var seed in new[] { 1, 2 })
                    {
                        total += DefenseDrill.Punch(punch, Defense.Weave, defender, seed, seconds: 20f, telegraph: 0.08f);
                    }

                    Console.WriteLine($"{name} weave vs {punch}, prediction {prediction}: clean {total.EvadeRate:P0}, head {total.HeadRate:P0}, guard {total.GuardRate:P0}, defended {total.Coverage:P0} of {total.Punches}");
                }
            }
        }
    }
}
