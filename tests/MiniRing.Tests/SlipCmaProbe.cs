using System.Text.Json;
using MiniRing.Arena;
using MiniRing.Arena.Evolution;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>新しい体で教科書のスリップを練習し直す（BASE_BODY_PLAN §10.7、通常の実行から外す Probe）。基本動作のドリル（右ストレート）の、打ち返し − 頭へのダメージで CMA-ES。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class SlipCmaProbe
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly int[] TrainSeeds = [21, 22, 23, 24, 25, 26, 27, 28];
    private static readonly int[] HeldSeeds = [31, 32, 33, 34, 35, 36, 37, 38, 41, 42, 43, 44];
    private static readonly int[] TestSeeds = [1, 2, 3, 4, 5, 6, 7, 8];

    private static (float Counter, float Head, int Punches) Measure(string punch, Defense defense, DefenseMove slip, int[] seeds, float seconds)
    {
        var book = BotCatalog.Champion.Defenses.With(defense, slip);
        var style = DefenseDrill.Defender(book, defend: true, DefenseDrill.DefaultRange, counters: true);
        var rs = new PunchDrillResult[seeds.Length];
        Parallel.For(0, seeds.Length, i => rs[i] = DefenseDrill.Punch(punch, defense, style, seed: seeds[i], seconds: seconds, cue: 0.06714f));
        return (rs.Sum(r => r.CounterDamage), rs.Sum(r => r.HeadDamage), rs.Sum(r => r.Punches));
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void RetrainSlip() => Retrain("2", Defense.Slip);

    [TestMethod]
    [TestCategory("Probe")]
    public void RetrainDuck() => Retrain("2", Defense.Duck);

    [TestMethod]
    [TestCategory("Probe")]
    public void RetrainWeave() => Retrain("3", Defense.Weave);

    private void Retrain(string punch, Defense defense)
    {
        TestContext.WriteLine($"=== {defense} against punch {punch}");
        var champion = BotCatalog.Champion.Defenses.Of(defense).Validated();
        var genome = new DefenseGenome(defense, champion);
        var cma = new CmaEs(genome.Initial(), seed: 7, initialSigma: 0.2, maximize: true, lambda: 16, maxGenerations: 60);
        const float Seconds = 14f;
        var start = Measure(punch, defense, champion, HeldSeeds, 23.5f);
        TestContext.WriteLine($"champion on held seeds: counter {start.Counter:0.0} head {start.Head:0.0} punches {start.Punches}");
        for (var gen = 1; gen <= 40; gen++)
        {
            var candidates = cma.Ask();
            var scores = new double[candidates.Length];
            Parallel.For(0, candidates.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, c =>
            {
                var m = Measure(punch, defense, genome.Decode(candidates[c]), TrainSeeds.Select(s => s + (gen * 100)).ToArray(), Seconds);
                scores[c] = (m.Counter - m.Head) / TrainSeeds.Length;
            });
            cma.Tell(scores);
            TestContext.WriteLine($"gen {gen}: best {scores.Max():0.0} mean {scores.Average():0.0} worst {scores.Min():0.0}");
        }

        var mean = genome.Decode(cma.Best);
        TestContext.WriteLine("RESULT " + JsonSerializer.Serialize(mean, CmaTune.Json));
        foreach (var (name, move) in new[] { ("champion", champion), ("retrained", mean) })
        {
            var held = Measure(punch, defense, move, HeldSeeds, 23.5f);
            var test = Measure(punch, defense, move, TestSeeds, 23.5f);
            TestContext.WriteLine($"{name}: held counter {held.Counter:0.0} head {held.Head:0.0} punches {held.Punches} | test seeds counter {test.Counter:0.0} head {test.Head:0.0} punches {test.Punches}");
        }
    }
}
