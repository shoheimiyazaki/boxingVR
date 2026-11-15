using MiniRing.Arena.Evolution;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>CMA-ES の本体（収束・決定性・再開・鏡映）と、技の値 ↔ 遺伝子の写し。</summary>
[TestClass]
public sealed class CmaEsTests
{
    private const int N = 10;

    private static double[] Half() => Enumerable.Repeat(0.5, N).ToArray();

    // 球: 最小は x = 0.3（[0, 1] の内側）。
    private static double Sphere(double[] x) => x.Sum(v => (v - 0.3) * (v - 0.3));

    // 回転した楕円（条件数 1e3）。回転は固定の種から作る（Gram-Schmidt）。
    private static readonly double[,] Rotation = MakeRotation(N, 12345);

    private static double Ellipsoid(double[] x)
    {
        var sum = 0.0;
        for (var i = 0; i < N; i++)
        {
            var y = 0.0;
            for (var j = 0; j < N; j++)
            {
                y += Rotation[i, j] * (x[j] - 0.3);
            }

            sum += Math.Pow(1000, i / (double)(N - 1)) * y * y;
        }

        return sum;
    }

    // Rosenbrock: [0, 1] を [-2, 2] へ写す（最小は y = 1 → x = 0.75）。
    private static double Rosenbrock(double[] x)
    {
        var y = x.Select(v => (4 * v) - 2).ToArray();
        var sum = 0.0;
        for (var i = 0; i < N - 1; i++)
        {
            sum += (100 * Math.Pow((y[i] * y[i]) - y[i + 1], 2)) + Math.Pow(y[i] - 1, 2);
        }

        return sum;
    }

    private static double[,] MakeRotation(int n, int seed)
    {
        var random = new Random(seed);
        var q = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            var v = Enumerable.Range(0, n).Select(_ => random.NextDouble() - 0.5).ToArray();
            for (var k = 0; k < i; k++)
            {
                var dot = 0.0;
                for (var j = 0; j < n; j++)
                {
                    dot += v[j] * q[k, j];
                }

                for (var j = 0; j < n; j++)
                {
                    v[j] -= dot * q[k, j];
                }
            }

            var norm = Math.Sqrt(v.Sum(a => a * a));
            for (var j = 0; j < n; j++)
            {
                q[i, j] = v[j] / norm;
            }
        }

        return q;
    }

    /// <summary>目標の値以下になるまでの評価回数（折り返した平均の値で判定。止める条件は使わず上限まで）。</summary>
    private static int EvaluationsToReach(Func<double[], double> f, double target, int seed, double sigma, int maxEvaluations, out double reached)
    {
        var cma = new CmaEs(Half(), seed, sigma);
        var evaluations = 0;
        reached = f(cma.Best);
        while (evaluations < maxEvaluations && reached > target)
        {
            var points = cma.Ask();
            cma.Tell(points.Select(f).ToArray());
            evaluations += points.Length;
            reached = f(cma.Best);
        }

        return evaluations;
    }

    [TestMethod]
    public void Sphere_ReachesOneEMinusEight_InAboutTwoThousandEvaluations()
    {
        var counts = new List<int>();
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var evals = EvaluationsToReach(Sphere, 1e-8, seed, 0.15, 6000, out var reached);
            Console.WriteLine($"sphere n={N} seed={seed}: {evals} evaluations, f={reached:E2}");
            Assert.IsTrue(reached <= 1e-8, $"seed {seed}: f = {reached}");
            counts.Add(evals);
        }

        Assert.IsTrue(counts.Max() <= 3500, string.Join(",", counts));
    }

    [TestMethod]
    public void RotatedEllipsoid_Cond1e3_Converges()
    {
        var counts = new List<int>();
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var evals = EvaluationsToReach(Ellipsoid, 1e-8, seed, 0.15, 30000, out var reached);
            Console.WriteLine($"ellipsoid cond=1e3 n={N} seed={seed}: {evals} evaluations, f={reached:E2}");
            Assert.IsTrue(reached <= 1e-8, $"seed {seed}: f = {reached}");
            counts.Add(evals);
        }
    }

    [TestMethod]
    public void Rosenbrock_Converges()
    {
        var counts = new List<int>();
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var evals = EvaluationsToReach(Rosenbrock, 1e-8, seed, 0.15, 60000, out var reached);
            Console.WriteLine($"rosenbrock n={N} seed={seed}: {evals} evaluations, f={reached:E2}");
            Assert.IsTrue(reached <= 1e-8, $"seed {seed}: f = {reached}");
            counts.Add(evals);
        }
    }

    [TestMethod]
    public void Maximize_FindsTheTop()
    {
        var cma = new CmaEs(Half(), 5, maximize: true);
        for (var g = 0; g < 400; g++)
        {
            var points = cma.Ask();
            cma.Tell(points.Select(p => -Sphere(p)).ToArray());
        }

        Assert.IsTrue(Sphere(cma.Best) < 1e-8);
    }

    [TestMethod]
    public void DefaultLambdaAndStopRules()
    {
        var cma = new CmaEs(Half(), 1);
        Assert.AreEqual(4 + (int)Math.Floor(3 * Math.Log(N)), cma.Lambda);
        Assert.AreEqual(CmaStop.None, cma.StopReason);

        // σ が初めの 1/50 未満になると止める（球は十分に絞られる）。
        for (var g = 0; g < 400 && cma.StopReason == CmaStop.None; g++)
        {
            cma.Tell(cma.Ask().Select(Sphere).ToArray());
        }

        Assert.AreEqual(CmaStop.SigmaShrunk, cma.StopReason);

        var capped = new CmaEs(Half(), 1, maxGenerations: 2);
        capped.Tell(capped.Ask().Select(Sphere).ToArray());
        capped.Tell(capped.Ask().Select(Sphere).ToArray());
        Assert.AreEqual(CmaStop.MaxGenerations, capped.StopReason);
    }

    [TestMethod]
    public void SameSeed_SameTrajectory_AndAskIsRepeatable()
    {
        var a = new CmaEs(Half(), 42);
        var b = new CmaEs(Half(), 42);
        var other = new CmaEs(Half(), 43);
        for (var g = 0; g < 30; g++)
        {
            var pa = a.Ask();
            CollectionAssert.AreEqual(pa.SelectMany(p => p).ToArray(), a.Ask().SelectMany(p => p).ToArray());
            a.Tell(pa.Select(Ellipsoid).ToArray());
            b.Tell(b.Ask().Select(Ellipsoid).ToArray());
            other.Tell(other.Ask().Select(Ellipsoid).ToArray());
        }

        Assert.AreEqual(a.SaveState(), b.SaveState());
        Assert.AreNotEqual(a.SaveState(), other.SaveState());
    }

    [TestMethod]
    public void ParallelEvaluation_GivesTheSameTrajectory()
    {
        var serial = new CmaEs(Half(), 7);
        var parallel = new CmaEs(Half(), 7);
        for (var g = 0; g < 40; g++)
        {
            serial.Tell(serial.Ask().Select(Rosenbrock).ToArray());

            var points = parallel.Ask();
            var scores = new double[points.Length];
            Parallel.For(0, points.Length, i => scores[i] = Rosenbrock(points[i]));
            parallel.Tell(scores);
        }

        Assert.AreEqual(serial.SaveState(), parallel.SaveState());
    }

    [TestMethod]
    public void SaveAndResume_MatchesRunningStraightThrough()
    {
        var straight = new CmaEs(Half(), 11);
        var resumed = new CmaEs(Half(), 11);
        for (var g = 0; g < 25; g++)
        {
            resumed.Tell(resumed.Ask().Select(Ellipsoid).ToArray());
        }

        for (var g = 0; g < 25; g++)
        {
            straight.Tell(straight.Ask().Select(Ellipsoid).ToArray());
        }

        resumed = CmaEs.LoadState(resumed.SaveState());
        Assert.AreEqual(25, resumed.Generation);
        for (var g = 0; g < 25; g++)
        {
            straight.Tell(straight.Ask().Select(Ellipsoid).ToArray());
            resumed.Tell(resumed.Ask().Select(Ellipsoid).ToArray());
        }

        Assert.AreEqual(straight.Generation, resumed.Generation);
        Assert.AreEqual(straight.Sigma, resumed.Sigma);
        CollectionAssert.AreEqual(straight.RawMean.ToArray(), resumed.RawMean.ToArray());
        CollectionAssert.AreEqual(straight.Covariance.Cast<double>().ToArray(), resumed.Covariance.Cast<double>().ToArray());
        Assert.AreEqual(straight.SaveState(), resumed.SaveState());
    }

    [TestMethod]
    public void Reflect_FoldsIntoUnitInterval()
    {
        Assert.AreEqual(0.25, CmaEs.Reflect(0.25), 1e-12);
        Assert.AreEqual(0.8, CmaEs.Reflect(1.2), 1e-12);
        Assert.AreEqual(0.2, CmaEs.Reflect(-0.2), 1e-12);
        Assert.AreEqual(0.3, CmaEs.Reflect(2.3), 1e-12); // 何度でも折れる
        Assert.AreEqual(0.3, CmaEs.Reflect(-2.3), 1e-12);
        Assert.AreEqual(1.0, CmaEs.Reflect(1.0), 1e-12);
        foreach (var x in new[] { -7.9, -0.001, 0, 0.5, 1, 1.0001, 13.37 })
        {
            Assert.IsTrue(CmaEs.Reflect(x) is >= 0 and <= 1, $"{x}");
        }
    }

    [TestMethod]
    public void Mirroring_AskFoldsButTheUpdateUsesTheUnfoldedSamples()
    {
        // 平均を端（0.98）に置き、σ を大きくして標本が範囲の外へ出るようにする。
        var start = Enumerable.Repeat(0.98, N).ToArray();
        var cma = new CmaEs(start, 3, initialSigma: 0.6);
        var raw = cma.AskRaw();
        var folded = cma.Ask();
        Assert.IsTrue(raw.SelectMany(p => p).Any(v => v is < 0 or > 1), "範囲の外の標本が無い");
        for (var i = 0; i < raw.Length; i++)
        {
            for (var k = 0; k < N; k++)
            {
                Assert.AreEqual(CmaEs.Reflect(raw[i][k]), folded[i][k], 1e-12);
                Assert.IsTrue(folded[i][k] is >= 0 and <= 1);
            }
        }

        // 点数 = 候補の番号（0 が最良）。平均は折り返す前の標本の重み付き平均へ動く。
        var scores = Enumerable.Range(0, cma.Lambda).Select(i => (double)i).ToArray();
        var mu = cma.Lambda / 2;
        var weights = Enumerable.Range(1, mu).Select(i => Math.Log((cma.Lambda + 1) / 2.0) - Math.Log(i)).ToArray();
        var sum = weights.Sum();
        var expected = new double[N];
        for (var r = 0; r < mu; r++)
        {
            for (var k = 0; k < N; k++)
            {
                expected[k] += weights[r] / sum * raw[r][k];
            }
        }

        cma.Tell(scores);
        for (var k = 0; k < N; k++)
        {
            Assert.AreEqual(expected[k], cma.RawMean[k], 1e-9);
        }

        Assert.IsTrue(cma.RawMean.Any(v => v is < 0 or > 1) || cma.Best.All(v => v is >= 0 and <= 1));
        Assert.IsTrue(cma.Best.All(v => v is >= 0 and <= 1));
    }

    [TestMethod]
    public void NonFiniteScores_AreTreatedAsWorst()
    {
        var cma = new CmaEs(Half(), 9);
        var scores = cma.Ask().Select(Sphere).ToArray();
        scores[0] = double.NaN;
        cma.Tell(scores);
        Assert.IsTrue(cma.RawMean.All(double.IsFinite));
    }

    [TestMethod]
    public void WrongScoreCount_Throws()
    {
        var cma = new CmaEs(Half(), 1);
        Assert.ThrowsExactly<ArgumentException>(() => cma.Tell([1.0, 2.0]));
    }

    // ---- 遺伝子 ----

    [TestMethod]
    public void PunchGenome_RoundTrips_ForEveryTechnique_IncludingTheEdges()
    {
        foreach (var technique in Enum.GetValues<Technique>())
        {
            var genome = new PunchGenome(technique);
            Assert.AreEqual(9, genome.Dimension);
            var original = PunchBook.Default.MoveOf(technique);
            AssertMoveEqual(original, genome.Decode(genome.Encode(original)), technique.ToString());
            AssertMoveEqual(original, genome.Decode(genome.Initial()), technique + " initial");

            foreach (var edge in new[] { 0.0, 1.0 })
            {
                var x = Enumerable.Repeat(edge, genome.Dimension).ToArray();
                var move = genome.Decode(x);
                var again = genome.Decode(genome.Encode(move));
                AssertMoveEqual(move, again, $"{technique} edge {edge}");
            }
        }
    }

    [TestMethod]
    public void PunchGenome_RangeIsCenteredOnTheDefault_AndWithinThePhysicalRange()
    {
        var ranges = PunchRanges.Tunable.ToDictionary(r => r.Name);
        foreach (var technique in Enum.GetValues<Technique>())
        {
            var genome = new PunchGenome(technique);
            foreach (var gene in genome.Genes)
            {
                var width = PunchGenome.Widths[gene.Name];
                var range = ranges[gene.Name];
                Assert.IsTrue(gene.Lo >= gene.Center - width - 1e-6f, $"{technique}.{gene.Name} Lo");
                Assert.IsTrue(gene.Hi <= gene.Center + width + 1e-6f, $"{technique}.{gene.Name} Hi");
                Assert.IsTrue(gene.Lo >= range.Min && gene.Hi <= range.Max, $"{technique}.{gene.Name} range");
                Assert.IsTrue(gene.Lo <= gene.Center && gene.Center <= gene.Hi, $"{technique}.{gene.Name} contains center");
            }
        }

        // 範囲で切られない値は、既定の値が [0, 1] の真ん中。
        var cross = new PunchGenome(Technique.Cross);
        var duration = cross.Genes.Single(g => g.Name == nameof(PunchMove.Duration));
        Assert.AreEqual(0.5, duration.Encode(0.40f), 1e-6);
        Assert.AreEqual(0.28f, duration.Lo, 1e-6f);
        Assert.AreEqual(0.52f, duration.Hi, 1e-6f);
    }

    [TestMethod]
    public void PunchGenome_KeepsTheIdentityOfTheTechnique()
    {
        foreach (var technique in Enum.GetValues<Technique>())
        {
            var genome = new PunchGenome(technique);
            var original = PunchBook.Default.MoveOf(technique);
            var move = genome.Decode(Enumerable.Repeat(1.0, genome.Dimension).ToArray());
            Assert.AreEqual(original.Lead, move.Lead);
            Assert.AreEqual(original.ToBody, move.ToBody);
            if (technique != Technique.Feint)
            {
                Assert.AreEqual(1f, move.Extend);
            }
        }
    }

    [TestMethod]
    public void DefenseGenome_RoundTrips_ForEveryDefense()
    {
        foreach (var defense in Enum.GetValues<Defense>().Where(d => d != Defense.None))
        {
            var genome = new DefenseGenome(defense);
            var original = DefenseBook.Default.Of(defense).Validated();
            AssertDefenseEqual(original, genome.Decode(genome.Encode(original)), defense.ToString());
            AssertDefenseEqual(original, genome.Decode(genome.Initial()), defense + " initial");

            foreach (var edge in new[] { 0.0, 1.0 })
            {
                var move = genome.Decode(Enumerable.Repeat(edge, genome.Dimension).ToArray());
                AssertDefenseEqual(move, genome.Decode(genome.Encode(move)), $"{defense} edge {edge}");
            }
        }
    }

    [TestMethod]
    public void DefenseGenome_Dimensions_BlockHandsUpFixed_EndXOnlyForSweeps()
    {
        var block = new DefenseGenome(Defense.Block);
        Assert.IsFalse(block.Genes.Any(g => g.Name == nameof(DefenseMove.HandsUp)));
        Assert.IsFalse(block.Genes.Any(g => g.Name == nameof(DefenseMove.EndX)));
        Assert.AreEqual(13, block.Dimension);

        foreach (var x in new[] { 0.0, 0.3, 1.0 })
        {
            Assert.AreEqual(1f, block.Decode(Enumerable.Repeat(x, block.Dimension).ToArray()).HandsUp);
        }

        var weave = new DefenseGenome(Defense.Weave);
        Assert.IsTrue(weave.Genes.Any(g => g.Name == nameof(DefenseMove.EndX)));
        Assert.AreEqual(15, weave.Dimension);

        var slip = new DefenseGenome(Defense.Slip);
        Assert.IsFalse(slip.Genes.Any(g => g.Name == nameof(DefenseMove.EndX)));
        Assert.AreEqual(14, slip.Dimension);
    }

    [TestMethod]
    public void Genome_DrivesCmaEs_ToTheOptimumInsideTheGenes()
    {
        // 拳の遺伝子を CMA-ES で回す: 目標の値（範囲の内側）へ寄る。
        var genome = new PunchGenome(Technique.Cross);
        var target = genome.Initial().Select((v, i) => Math.Clamp(v + (i % 2 == 0 ? 0.15 : -0.15), 0.05, 0.95)).ToArray();
        double Cost(double[] x) => x.Select((v, i) => (v - target[i]) * (v - target[i])).Sum();
        var cma = new CmaEs(genome.Initial(), 21, 0.15);
        for (var g = 0; g < 200; g++)
        {
            cma.Tell(cma.Ask().Select(Cost).ToArray());
        }

        Assert.IsTrue(Cost(cma.Best) < 1e-8);
        _ = genome.Decode(cma.Best);
    }

    private static void AssertMoveEqual(PunchMove expected, PunchMove actual, string label)
    {
        Assert.AreEqual(expected.Lead, actual.Lead, label);
        Assert.AreEqual(expected.ToBody, actual.ToBody, label);
        Assert.AreEqual(expected.Lateral, actual.Lateral, 1e-5f, label + " Lateral");
        Assert.AreEqual(expected.Low, actual.Low, 1e-5f, label + " Low");
        Assert.AreEqual(expected.Overshoot, actual.Overshoot, 1e-5f, label + " Overshoot");
        Assert.AreEqual(expected.Duration, actual.Duration, 1e-5f, label + " Duration");
        Assert.AreEqual(expected.HeadShiftX, actual.HeadShiftX, 1e-5f, label + " HeadShiftX");
        Assert.AreEqual(expected.HeadShiftY, actual.HeadShiftY, 1e-5f, label + " HeadShiftY");
        Assert.AreEqual(expected.HeadShiftZ, actual.HeadShiftZ, 1e-5f, label + " HeadShiftZ");
        Assert.AreEqual(expected.StepIn, actual.StepIn, 1e-5f, label + " StepIn");
        Assert.AreEqual(expected.Sweep, actual.Sweep, 1e-5f, label + " Sweep");
        Assert.AreEqual(expected.Extend, actual.Extend, 1e-5f, label + " Extend");
    }

    private static void AssertDefenseEqual(DefenseMove expected, DefenseMove actual, string label)
    {
        const float Tol = 1e-5f;
        Assert.AreEqual(expected.Sweep, actual.Sweep, label);
        Assert.AreEqual(expected.HeadX, actual.HeadX, Tol, label + " HeadX");
        Assert.AreEqual(expected.HeadY, actual.HeadY, Tol, label + " HeadY");
        Assert.AreEqual(expected.HeadZ, actual.HeadZ, Tol, label + " HeadZ");
        Assert.AreEqual(expected.EndX, actual.EndX, Tol, label + " EndX");
        Assert.AreEqual(expected.Duration, actual.Duration, Tol, label + " Duration");
        Assert.AreEqual(expected.Move, actual.Move, Tol, label + " Move");
        Assert.AreEqual(expected.Step, actual.Step, Tol, label + " Step");
        Assert.AreEqual(expected.Rate, actual.Rate, 1e-4f, label + " Rate");
        Assert.AreEqual(expected.HandsUp, actual.HandsUp, Tol, label + " HandsUp");
        Assert.AreEqual(expected.GloveWidth, actual.GloveWidth, Tol, label + " GloveWidth");
        Assert.AreEqual(expected.GloveHeight, actual.GloveHeight, Tol, label + " GloveHeight");
        Assert.AreEqual(expected.GloveForward, actual.GloveForward, Tol, label + " GloveForward");
        Assert.AreEqual(expected.BodyDrop, actual.BodyDrop, Tol, label + " BodyDrop");
        Assert.AreEqual(expected.TempleWidth, actual.TempleWidth, Tol, label + " TempleWidth");
        Assert.AreEqual(expected.GloveAnchor, actual.GloveAnchor, Tol, label + " GloveAnchor");
    }
}
