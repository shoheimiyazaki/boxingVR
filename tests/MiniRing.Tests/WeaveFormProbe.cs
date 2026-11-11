using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>前へ折る（腰を折って頭を U 字に）ウィービングの形を比べる調査用（合図なし・予測あり、左フック）。</summary>
[TestClass]
public sealed class WeaveFormProbe
{
    [TestMethod]
    public void ForwardWeaveForms()
    {
        var swarmer = BotCatalog.Swarmer;
        var basis = swarmer.Defenses.Weave;
        var forms = new (string Name, DefenseMove Move)[]
        {
            ("now (leans back)", basis),
            ("z0 y-0.45", basis with { HeadZ = 0f, HeadY = -0.45f }),
            ("z0 y-0.6", basis with { HeadZ = 0f, HeadY = -0.6f }),
            ("z0.05 y-0.45", basis with { HeadZ = 0.05f, HeadY = -0.45f }),
            ("z0.05 y-0.6", basis with { HeadZ = 0.05f, HeadY = -0.6f }),
            ("z0.1 y-0.45", basis with { HeadZ = 0.1f, HeadY = -0.45f }),
            ("z-0.1 y-0.5", basis with { HeadZ = -0.1f, HeadY = -0.5f }),
        };
        foreach (var (name, move) in forms)
        {
            var defender = DefenseDrill.Defender(swarmer.Defenses.With(Defense.Weave, move), defend: true, counters: false) with
            {
                GuardHeight = swarmer.GuardHeight, GuardWidth = swarmer.GuardWidth, GuardReach = swarmer.GuardReach, Prediction = 1f,
            };
            var total = PunchDrillResult.Empty;
            foreach (var seed in new[] { 11, 12, 13 })
            {
                total += DefenseDrill.Punch("3", Defense.Weave, defender, seed, seconds: 20f, telegraph: 0.08f);
            }

            Console.WriteLine($"{name}: clean {total.EvadeRate:P0}, head {total.HeadRate:P0}, guard {total.GuardRate:P0} of {total.Punches}");
        }
    }
}
