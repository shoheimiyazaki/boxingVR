using System.Text.Json;
using MiniRing.Bots;
using MiniRing.Highlights;

namespace MiniRing.Arena;

/// <summary>ドラマを同じ対戦・種・コーナーの組で比較する。物理や勝率モデルの変更ごとに別々に採取する。</summary>
internal static class DramaPanel
{
    private const int BootstrapSamples = 10_000;
    private const double ScoreTieTolerance = 1e-4;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal sealed record Bout(string Card, int Seed, string Red, string Blue, float Drama);
    internal sealed record Samples(int PhysicsVersion, int LogicVersion, float Seconds, Bout[] Bouts);
    internal sealed record Distribution(double Mean, double Median, double P10, double P25, double P75, double P90, double StandardDeviation);
    internal sealed record Comparison(string Card, int Count, Distribution Before, Distribution After, Distribution PairedChange, int Lower, int Equal, int Higher);

    internal static int Capture(Options options)
    {
        var path = Required(options, "out");
        var seconds = options.Float("seconds", 67.14f, min: 1f);
        var firstSeed = options.Int("seed", 1, min: 1);
        var seeds = options.Int("seeds", 8, min: 2);
        if (seeds % 2 != 0)
        {
            throw new ArgumentException("正逆のコーナーを同数にするには --seeds を偶数にしてください。");
        }

        var cards = options.List("pairs", ExcitementLab.FitPairs);
        if (cards.Count == 0 || cards.Distinct(StringComparer.Ordinal).Count() != cards.Count)
        {
            throw new ArgumentException("--pairs には重複のない対戦カードを指定してください。");
        }

        var jobs = cards.SelectMany(card =>
        {
            var (red, blue) = ExcitementLab.Split(card);
            return Enumerable.Range(firstSeed, seeds).Select((seed, i) => i < seeds / 2
                ? (Card: card, Seed: seed, Red: red, Blue: blue)
                : (Card: card, Seed: seed, Red: blue, Blue: red));
        }).ToArray();

        var bouts = new Bout[jobs.Length];
        Parallel.For(0, jobs.Length, new ParallelOptions { MaxDegreeOfParallelism = options.Int("parallel", 4, min: 1) }, i =>
        {
            var job = jobs[i];
            var s = ExcitementRun.Bout(Contender.Named(job.Red), Contender.Named(job.Blue), job.Seed, seconds).Summarize();
            bouts[i] = new Bout(job.Card, job.Seed, job.Red, job.Blue, s.Drama);
        });

        var samples = new Samples(Fighter.PhysicsVersion, BoxerBot.LogicVersion, seconds, bouts);
        File.WriteAllText(path, JsonSerializer.Serialize(samples, JsonOptions));
        Console.WriteLine($"{bouts.Length} 試合を {path} に保存");
        return 0;
    }

    internal static int CompareCommand(Options options)
    {
        var before = Read(Required(options, "before"));
        var after = Read(Required(options, "after"));
        var (total, cards, meanChangeInterval) = Compare(before, after);
        var result = new
        {
            Before = new { before.PhysicsVersion, before.LogicVersion },
            After = new { after.PhysicsVersion, after.LogicVersion },
            before.Seconds,
            Total = total,
            Cards = cards,
            MeanChangeInterval95 = meanChangeInterval,
        };
        var json = JsonSerializer.Serialize(result, JsonOptions);
        if (options.Get("out") is { } path)
        {
            File.WriteAllText(path, json);
        }

        Console.WriteLine(json);
        return 0;
    }

    internal static (Comparison Total, Comparison[] Cards, double[] MeanChangeInterval) Compare(Samples before, Samples after)
    {
        if (!float.IsFinite(before.Seconds) || before.Seconds <= 0f || before.Seconds != after.Seconds)
        {
            throw new ArgumentException("試合時間が一致する標本を指定してください。");
        }

        var previous = Index(before);
        var current = Index(after);
        if (previous.Count != current.Count || previous.Keys.Any(key => !current.ContainsKey(key)))
        {
            throw new ArgumentException("対戦カード・シード・赤青の割り当てが一致していません。");
        }

        var pairs = previous.OrderBy(x => x.Key.Card, StringComparer.Ordinal).ThenBy(x => x.Key.Seed)
            .ThenBy(x => x.Key.Red, StringComparer.Ordinal).ThenBy(x => x.Key.Blue, StringComparer.Ordinal)
            .Select(x => (Before: x.Value, After: current[x.Key])).ToArray();
        var cards = pairs.GroupBy(x => x.Before.Card, StringComparer.Ordinal)
            .Select(g => Summarize(g.Key, g.ToArray())).ToArray();
        return (Summarize("all", pairs), cards, Bootstrap(pairs));
    }

    private static Dictionary<(string Card, int Seed, string Red, string Blue), Bout> Index(Samples samples)
    {
        if (samples.Bouts is not { Length: > 0 })
        {
            throw new ArgumentException("試合結果がありません。");
        }

        var entries = new Dictionary<(string Card, int Seed, string Red, string Blue), Bout>();
        foreach (var bout in samples.Bouts)
        {
            if (bout is null || string.IsNullOrWhiteSpace(bout.Card) || string.IsNullOrWhiteSpace(bout.Red) || string.IsNullOrWhiteSpace(bout.Blue)
                || bout.Seed < 1 || !float.IsFinite(bout.Drama) || !entries.TryAdd((bout.Card, bout.Seed, bout.Red, bout.Blue), bout))
            {
                throw new ArgumentException("試合結果に欠損値または重複があります。");
            }
        }

        return entries;
    }

    private static Comparison Summarize(string card, (Bout Before, Bout After)[] pairs)
    {
        var changes = pairs.Select(x => (double)x.After.Drama - x.Before.Drama).ToArray();
        var lower = changes.Count(x => x < -ScoreTieTolerance);
        var higher = changes.Count(x => x > ScoreTieTolerance);
        return new Comparison(card, pairs.Length, Describe(pairs.Select(x => (double)x.Before.Drama)), Describe(pairs.Select(x => (double)x.After.Drama)),
            Describe(changes), lower, changes.Length - lower - higher, higher);
    }

    private static Distribution Describe(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var mean = sorted.Average();
        var variance = sorted.Sum(x => (x - mean) * (x - mean)) / Math.Max(1, sorted.Length - 1);
        return new Distribution(mean, Quantile(sorted, 0.5), Quantile(sorted, 0.1), Quantile(sorted, 0.25), Quantile(sorted, 0.75), Quantile(sorted, 0.9), Math.Sqrt(variance));
    }

    private static double[] Bootstrap((Bout Before, Bout After)[] pairs)
    {
        // 対戦カードと赤青の構成は固定し、同じ条件の前後の差を各層内で復元抽出する。
        var strata = pairs.GroupBy(x => (x.Before.Card, x.Before.Red, x.Before.Blue))
            .Select(g => g.Select(x => (double)x.After.Drama - x.Before.Drama).ToArray()).ToArray();
        var random = new Random(29_153);
        var means = new double[BootstrapSamples];
        for (var sample = 0; sample < means.Length; sample++)
        {
            var total = 0d;
            foreach (var stratum in strata)
            {
                for (var i = 0; i < stratum.Length; i++)
                {
                    total += stratum[random.Next(stratum.Length)];
                }
            }

            means[sample] = total / pairs.Length;
        }

        Array.Sort(means);
        return [Quantile(means, 0.025), Quantile(means, 0.975)];
    }

    private static double Quantile(double[] sorted, double fraction)
    {
        var index = fraction * (sorted.Length - 1);
        var lo = (int)index;
        var hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + ((sorted[hi] - sorted[lo]) * (index - lo));
    }

    private static string Required(Options options, string name) =>
        options.Get(name) is { Length: > 0 } path ? path : throw new ArgumentException($"--{name} が必要です。");

    private static Samples Read(string path) =>
        JsonSerializer.Deserialize<Samples>(File.ReadAllText(path), JsonOptions) ?? throw new ArgumentException($"試合結果を読み込めませんでした（{path}）。");
}
