using System.Globalization;
using MiniRing.Bots;
using MiniRing.Highlights;

namespace MiniRing.Tests;

/// <summary>
/// 調べもの: 標準ボットの試合で、立っていた体が自分でバランスを失って倒れた回数と原因（<see cref="Fighter.LastFallCause"/>）、倒れる直前の傾き・足の浮いていた時間を数える。
/// <c>MINIRING_FALL_PRESET</c> に <c>MatchPunch</c> を渡すと試合用の体で数える（既定は <see cref="FighterTuning.Default"/>）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
[DoNotParallelize]
public sealed class FallCauseProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Measure()
    {
        var preset = Environment.GetEnvironmentVariable("MINIRING_FALL_PRESET");
        var tuning = preset == "MatchPunch" ? FighterTuning.MatchPunch : FighterTuning.Default;
        var causes = new Dictionary<string, int>();
        var falls = 0;
        foreach (var pair in new[] { ("champion", "swarmer"), ("slugger", "outboxer"), ("counter", "jabber") })
        {
            for (var seed = 1; seed <= 3; seed++)
            {
                var lastPlayer = FighterState.Standing;
                var lastCpu = FighterState.Standing;
                var history = new Dictionary<string, Queue<(float V, float B)>> { ["red"] = new(), ["blue"] = new() };
                ExcitementRun.Bout(Contender.Named(pair.Item1), Contender.Named(pair.Item2), seed, 67.14f, fighter: tuning, afterStep: (game, t) =>
                {
                    foreach (var (fighter, last, who) in new[] { (game.Player, lastPlayer, "red"), (game.Cpu, lastCpu, "blue") })
                    {
                        var queue = history[who];
                        if (fighter.State == FighterState.Standing)
                        {
                            queue.Enqueue((fighter.VerticalBalance, fighter.Balance));
                            if (queue.Count > 60)
                            {
                                queue.Dequeue();
                            }
                        }

                        if (last == FighterState.Standing && fighter.State != FighterState.Standing && fighter.Consciousness > 0.9f)
                        {
                            var cause = fighter.LastFallCause ?? "?";
                            causes[cause] = causes.GetValueOrDefault(cause) + 1;
                            falls++;
                            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture,
                                $"{pair.Item1}:{pair.Item2}/{seed} t={t:F1}s {who} {cause} upright={fighter.Uprightness:F2} consciousness={fighter.Consciousness:F2} minVerticalBalance(1s)={queue.Min(q => q.V):F2} minBalance={queue.Min(q => q.B):F2}"));
                        }
                    }

                    lastPlayer = game.Player.State;
                    lastCpu = game.Cpu.State;
                });
            }
        }

        TestContext.WriteLine($"{(preset ?? "Default")}: 意識が保たれたままの転倒 {falls} 回 / 9 試合  " + string.Join(" ", causes.Select(c => $"{c.Key}={c.Value}")));
    }
}
