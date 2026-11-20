
namespace MiniRing.Motion.Stick;

/// <summary>振りの探索の設定。</summary>
/// <param name="Samples">一様に引く本数（既定 2000）。</param>
/// <param name="Polish">磨く上位の本数 K。</param>
/// <param name="Iterations">1 本あたりの (1+1) 進化戦略の繰り返し。</param>
/// <param name="Seed">乱数の種（同じ種で同じ結果）。</param>
/// <param name="Weights">点の重み。</param>
public sealed record SwingSearchOptions(int Samples = 2000, int Polish = 8, int Iterations = 3000, int Seed = 1, SwingWeights? Weights = null)
{
    public SwingWeights EffectiveWeights => Weights ?? new SwingWeights();
}

/// <summary>1 つの断面の探索結果: ランダムに引いただけの分布と、磨いた最良。</summary>
public sealed class SwingSearchResult
{
    public SwingSearchResult(SwingPlane plane, IReadOnlyList<SwingEval> random, float[] bestRandomGenes, SwingEval bestRandom, float[] bestGenes, SwingEval best)
    {
        Plane = plane;
        Random = random;
        BestRandomGenes = bestRandomGenes;
        BestRandom = bestRandom;
        BestGenes = bestGenes;
        Best = best;
    }

    public SwingPlane Plane { get; }

    /// <summary>一様に引いた全部の評価。</summary>
    public IReadOnlyList<SwingEval> Random { get; }

    public float[] BestRandomGenes { get; }

    /// <summary>ランダムに引いた中の最高点。</summary>
    public SwingEval BestRandom { get; }

    public float[] BestGenes { get; }

    /// <summary>磨いた最良。</summary>
    public SwingEval Best { get; }

    /// <summary>ランダムに引いた評価のうち、値 <paramref name="select"/> の中央値（NaN は除く）。</summary>
    public float Median(Func<SwingEval, float> select)
    {
        var values = Random.Select(select).Where(float.IsFinite).Order().ToArray();
        return values.Length == 0 ? float.NaN : values[values.Length / 2];
    }

    /// <summary>失格でない振りの割合。</summary>
    public float ValidFraction => Random.Count(e => e.Valid) / (float)Math.Max(1, Random.Count);
}

/// <summary>
/// モンテカルロの探索（docs/STICK_MOTION.md §11）: (1) 範囲の中から一様に引いた N 本を評価し、(2) 上位 K 本を (1+1) 進化戦略で磨く。
/// 種付きで、本ごとに独立した乱数（引いた順・スレッドの数に依らず同じ結果）。
/// </summary>
public static class SwingSearch
{
    /// <summary>磨くときの 1 回の変え方の大きさ（遺伝子ごとの標準偏差 [rad・s]、<c>sigma</c> を掛ける）。</summary>
    private const float AngleStep = 0.3f;

    private const float TimeStep = 0.0671f;

    /// <summary>遺伝子を 1 回ずつ変える確率（少なくとも 1 つは変える）。</summary>
    private const float MutationRate = 0.3f;

    public static SwingSearchResult Run(SwingRig rig, SwingPlane plane, SwingSearchOptions options, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(options);
        var weights = options.EffectiveWeights;
        var planeIndex = Array.FindIndex([.. SwingPlane.All], p => p.Name == plane.Name);

        // (1) 一様に引く。
        var genes = new float[options.Samples][];
        var evals = new SwingEval[options.Samples];
        Parallel.For(0, options.Samples, i =>
        {
            var random = new Random(Mix(options.Seed, planeIndex, i));
            genes[i] = rig.RandomGenes(random);
            evals[i] = rig.Evaluate(genes[i], plane, weights);
        });
        var order = Enumerable.Range(0, options.Samples).OrderByDescending(i => evals[i].Score).ThenBy(i => i).ToArray();
        var bestRandomIndex = order[0];
        log?.Invoke($"  {plane.Name}: 一様に {options.Samples} 本、失格でない {evals.Count(e => e.Valid)} 本、最高点 {evals[bestRandomIndex].Score:0.00}");

        // (2) 上位 K 本を磨く。
        var starts = order.Take(Math.Max(1, options.Polish)).ToArray();
        var polished = new (float[] Genes, SwingEval Eval)[starts.Length];
        Parallel.For(0, starts.Length, k =>
        {
            polished[k] = Polish(rig, plane, weights, genes[starts[k]], evals[starts[k]], new Random(Mix(options.Seed, planeIndex, 1_000_000 + k)), options.Iterations);
        });
        var winner = polished.Select((p, k) => (p, k)).OrderByDescending(x => x.p.Eval.Score).ThenBy(x => x.k).First().p;
        log?.Invoke($"  {plane.Name}: 磨いた最良 {winner.Eval.Score:0.00}（{string.Join("・", polished.Select(p => $"{p.Eval.Score:0.00}"))}）");
        return new SwingSearchResult(plane, evals, genes[bestRandomIndex], evals[bestRandomIndex], winner.Genes, winner.Eval);
    }

    /// <summary>(1+1) 進化戦略: 遺伝子の一部を正規乱数で変え、点が上がったら採る。成功で歩幅を広げ、失敗で縮める（1/5 則に近い）。</summary>
    public static (float[] Genes, SwingEval Eval) Polish(SwingRig rig, SwingPlane plane, SwingWeights weights, float[] start, SwingEval startEval, Random random, int iterations)
    {
        var current = start;
        var currentEval = startEval;
        var sigma = 1f;
        for (var it = 0; it < iterations; it++)
        {
            var candidate = (float[])current.Clone();
            var changed = false;
            for (var i = 0; i < candidate.Length; i++)
            {
                if (random.NextSingle() < MutationRate)
                {
                    candidate[i] += Gaussian(random) * sigma * (i >= SwingRig.KeyCount * SwingRig.PerKey ? TimeStep : AngleStep);
                    changed = true;
                }
            }

            if (!changed)
            {
                var i = random.Next(candidate.Length);
                candidate[i] += Gaussian(random) * sigma * (i >= SwingRig.KeyCount * SwingRig.PerKey ? TimeStep : AngleStep);
            }

            candidate = rig.Clamp(candidate);
            var eval = rig.Evaluate(candidate, plane, weights);
            if (eval.Score > currentEval.Score)
            {
                (current, currentEval) = (candidate, eval);
                sigma = MathF.Min(1f, sigma * 1.15f);
            }
            else
            {
                sigma = MathF.Max(0.02f, sigma * 0.965f);
            }
        }

        return (current, currentEval);
    }

    private static float Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    /// <summary>種・断面・番号から本ごとの乱数の種を作る（<see cref="HashCode"/> は実行ごとに変わるので使わない）。</summary>
    private static int Mix(int seed, int plane, int index)
    {
        unchecked
        {
            var h = (uint)seed;
            h = (h * 2654435761u) ^ (uint)plane;
            h = (h * 2654435761u) ^ (uint)index;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (int)(h & 0x7FFFFFFF);
        }
    }
}
