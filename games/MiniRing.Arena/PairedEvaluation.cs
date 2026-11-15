using System.Globalization;

namespace MiniRing.Arena;

/// <summary>対にした評価の結果（<see cref="PairedEvaluation"/>）。</summary>
/// <param name="N">標本（種）の数。</param>
/// <param name="MeanA">点(A) の平均。</param>
/// <param name="MeanB">点(B) の平均。</param>
/// <param name="MeanDiff">種ごとの差 d_i = 点(A) − 点(B) の平均。</param>
/// <param name="SdDiff">d_i の標準偏差（n − 1）。</param>
/// <param name="StdErr">平均の差の標準誤差。</param>
/// <param name="CiLow">信頼区間の下端（<see cref="Alpha"/> の両側、t 分布）。</param>
/// <param name="CiHigh">信頼区間の上端。</param>
/// <param name="Alpha">この結果の有意水準（両側）。</param>
/// <param name="SignP">符号検定の両側 p 値（d_i = 0 の種は除く、二項分布で正確に）。</param>
/// <param name="Plus">A が上回った種の数。</param>
/// <param name="Minus">B が上回った種の数。</param>
/// <param name="Ties">差が 0 の種の数。</param>
/// <param name="Rho">点(A) と 点(B) の相関。0 に近ければ、同じ種で対にしても分散は減らない。</param>
internal sealed record PairedResult(int N, double MeanA, double MeanB, double MeanDiff, double SdDiff, double StdErr, double CiLow, double CiHigh,
    double Alpha, double SignP, int Plus, int Minus, int Ties, double Rho)
{
    /// <summary>信頼区間が 0 を含まない（差があると言える）。差が全部 0 のときは区間が [0, 0] で、含むので「差なし」。</summary>
    public bool Significant => CiLow > 0d || CiHigh < 0d;

    /// <summary>効果の大きさ ÷ 標準誤差（t 値）。標準誤差が 0 のときは 0（差も 0 のとき）か ±∞。</summary>
    public double TValue => StdErr > 0d ? MeanDiff / StdErr : MeanDiff == 0d ? 0d : double.CopySign(double.PositiveInfinity, MeanDiff);

    /// <summary>t 値が <paramref name="target"/> を超えるのに要る標本の数（今の効果と d_i のばらつきのまま増やしたとして）。差が 0 なら null。</summary>
    public int? SamplesFor(double target)
        => MeanDiff == 0d ? null : (int)Math.Ceiling(Math.Pow(target * SdDiff / Math.Abs(MeanDiff), 2d));

    public string Verdict => Significant ? (MeanDiff > 0d ? "A が上" : "B が上") : "差なし";

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"n={N} 平均の差 {MeanDiff:+0.000;-0.000;0.000} ± {StdErr:0.000}（{(1d - Alpha) * 100d:0.#}% 区間 {CiLow:0.000}〜{CiHigh:0.000}）符号検定 p={SignP:0.0000}（+{Plus}/-{Minus}/={Ties}）ρ={Rho:0.00} → {Verdict}");
}

/// <summary>逐次に測った結果。止めた時点の値は目安で、採否の主張には使わない（<see cref="PairedEvaluation.Confirm"/> を使う）。</summary>
/// <param name="Result">止めた時点の結果（有意水準は α / 覗く回数）。</param>
/// <param name="Looks">覗いた回数。</param>
/// <param name="PlannedLooks">覗く回数の上限（Bonferroni の分母）。</param>
/// <param name="StoppedEarly">n_max に届く前に止めたか。</param>
internal sealed record RaceResult(PairedResult Result, int Looks, int PlannedLooks, bool StoppedEarly)
{
    public override string ToString() => $"（目安）{Result} 覗いた回数 {Looks}/{PlannedLooks}{(StoppedEarly ? "、途中で止めた" : "")}";
}

/// <summary>
/// 対にした評価: 2 つの値 A・B を同じ種の組で測り、種ごとの差 d_i = 点(A) − 点(B) から差を言う（共通の乱数）。
/// <list type="bullet">
/// <item>点の関数は「種の並び → 点の並び（同じ位置）」。同じ値を同じ種で測れば同じ点になる（試合は種と値で決定的）。</item>
/// <item>逐次（<see cref="Race"/>）は予算の配分にだけ使う: n_min を測ったあと k 標本ずつ足して n_max まで。覗く L 回の各回の有意水準は α / L（Bonferroni）。
/// 採否の主張は、使わなかった種の固定の n（<see cref="Confirm"/>）で行う。</item>
/// </list>
/// </summary>
internal static class PairedEvaluation
{
    /// <summary>種ごとの点を測る関数。結果は種の並びと同じ位置に入れる。</summary>
    public delegate double[] Scorer(IReadOnlyList<int> seeds);

    /// <summary>対の差を集計する（<paramref name="a"/>[i] と <paramref name="b"/>[i] は同じ種の点）。</summary>
    public static PairedResult Compute(IReadOnlyList<double> a, IReadOnlyList<double> b, double alpha = 0.05)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Count != b.Count || a.Count < 2)
        {
            throw new ArgumentException("A と B は同じ数（2 以上）の標本が要ります。");
        }

        if (!(alpha > 0d && alpha < 1d))
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), "有意水準は 0 と 1 の間です。");
        }

        var n = a.Count;
        var d = new double[n];
        int plus = 0, minus = 0;
        for (var i = 0; i < n; i++)
        {
            d[i] = a[i] - b[i];
            plus += d[i] > 0d ? 1 : 0;
            minus += d[i] < 0d ? 1 : 0;
        }

        var meanDiff = d.Average();
        var sd = StdDev(d);
        var se = sd / Math.Sqrt(n);
        var half = StudentT.Critical(n - 1, alpha) * se;
        return new PairedResult(n, a.Average(), b.Average(), meanDiff, sd, se, meanDiff - half, meanDiff + half, alpha,
            SignTest.TwoSidedP(plus, minus), plus, minus, n - plus - minus, Correlation(a, b));
    }

    /// <summary>
    /// 固定の n での評価（採否の主張に使う）。<paramref name="seeds"/> は、値を選ぶのに使わなかった種にする。
    /// </summary>
    public static PairedResult Confirm(Scorer a, Scorer b, IReadOnlyList<int> seeds, double alpha = 0.05)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(seeds);
        return Compute(a(seeds), b(seeds), alpha);
    }

    /// <summary>1 回の追加でコア（<paramref name="cores"/>）が遊ばない最小の標本の数: 1 標本の試合の数 × k ≥ コアの数。</summary>
    public static int MinBatch(int boutsPerSample, int cores)
    {
        var bouts = Math.Max(1, boutsPerSample);
        return Math.Max(1, (Math.Max(1, cores) + bouts - 1) / bouts);
    }

    /// <summary>
    /// 逐次に足す。<paramref name="seedAt"/>(i) は i 番目の標本の種。n_min を測ったあと <paramref name="k"/> 標本ずつ足し、
    /// 覗くたびに（α / L の）区間が 0 を含まなくなったら止める。k は <see cref="MinBatch"/> 未満なら引き上げる。
    /// </summary>
    public static RaceResult Race(Scorer a, Scorer b, Func<int, int> seedAt, int nMin, int k, int nMax, int boutsPerSample, int cores, double alpha = 0.05)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(seedAt);
        if (nMin < 2 || nMax < nMin || k < 1)
        {
            throw new ArgumentException("n_min は 2 以上、n_max は n_min 以上、k は 1 以上にしてください。");
        }

        k = Math.Max(k, MinBatch(boutsPerSample, cores));
        var plannedLooks = 1 + (int)Math.Ceiling((nMax - nMin) / (double)k);
        var perLook = alpha / plannedLooks;
        var pointsA = new List<double>();
        var pointsB = new List<double>();
        var looks = 0;
        for (var n = nMin; ; n = Math.Min(n + k, nMax))
        {
            var seeds = Enumerable.Range(pointsA.Count, n - pointsA.Count).Select(seedAt).ToList();
            pointsA.AddRange(a(seeds));
            pointsB.AddRange(b(seeds));
            looks++;
            var result = Compute(pointsA, pointsB, perLook);
            if (result.Significant || n >= nMax)
            {
                return new RaceResult(result, looks, plannedLooks, n < nMax);
            }
        }
    }

    public static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0d;
        }

        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    /// <summary>ピアソンの相関。どちらかが一定なら定義できないので、全く同じ並びなら 1、そうでなければ 0 とする。</summary>
    public static double Correlation(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        var ma = a.Average();
        var mb = b.Average();
        double sab = 0d, saa = 0d, sbb = 0d;
        for (var i = 0; i < a.Count; i++)
        {
            sab += (a[i] - ma) * (b[i] - mb);
            saa += (a[i] - ma) * (a[i] - ma);
            sbb += (b[i] - mb) * (b[i] - mb);
        }

        if (saa <= 0d || sbb <= 0d)
        {
            return a.SequenceEqual(b) ? 1d : 0d;
        }

        return sab / Math.Sqrt(saa * sbb);
    }
}

/// <summary>符号検定（両側）。</summary>
internal static class SignTest
{
    /// <summary>
    /// 両側 p 値 = min(1, 2 × P(X ≤ min(plus, minus)))、X ~ Binomial(plus + minus, 1/2)。差が 0 の種は数えない（呼び出し側が除く）。
    /// 10 勝 2 敗なら 2 × 79 / 4096 ≈ 0.0386。
    /// </summary>
    public static double TwoSidedP(int plus, int minus)
    {
        var n = plus + minus;
        if (n == 0)
        {
            return 1d;
        }

        var low = Math.Min(plus, minus);
        double tail = 0d;
        for (var x = 0; x <= low; x++)
        {
            tail += Math.Exp(LogChoose(n, x) - (n * Math.Log(2d)));
        }

        return Math.Min(1d, 2d * tail);
    }

    private static double LogChoose(int n, int k) => StudentT.LogGamma(n + 1d) - StudentT.LogGamma(k + 1d) - StudentT.LogGamma(n - k + 1d);
}

/// <summary>t 分布の臨界値（外部の統計ライブラリに頼らない: 不完全ベータ関数と二分法）。</summary>
internal static class StudentT
{
    /// <summary>両側 <paramref name="alpha"/> の臨界値 t（P(|T| &gt; t) = α）。自由度 7・α = 0.05 で 2.3646、29 で 2.0452。</summary>
    public static double Critical(int df, double alpha)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(df, 1);

        double lo = 0d, hi = 1e4;
        for (var i = 0; i < 200; i++)
        {
            var mid = 0.5d * (lo + hi);
            if (TwoSidedTail(df, mid) > alpha)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return 0.5d * (lo + hi);
    }

    /// <summary>P(|T| &gt; t)。</summary>
    public static double TwoSidedTail(int df, double t) => RegularizedBeta(df / (df + (t * t)), df / 2d, 0.5d);

    /// <summary>ln Γ(x)（Lanczos 近似、g = 7、9 項）。</summary>
    public static double LogGamma(double x)
    {
        double[] c = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
            12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7];
        if (x < 0.5d)
        {
            return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1d - x);
        }

        x -= 1d;
        var a = c[0];
        var t = x + 7.5d;
        for (var i = 1; i < 9; i++)
        {
            a += c[i] / (x + i);
        }

        return (0.5d * Math.Log(2d * Math.PI)) + ((x + 0.5d) * Math.Log(t)) - t + Math.Log(a);
    }

    /// <summary>正則化不完全ベータ関数 I_x(a, b)（連分数、Numerical Recipes の betacf）。</summary>
    private static double RegularizedBeta(double x, double a, double b)
    {
        if (x <= 0d)
        {
            return 0d;
        }

        if (x >= 1d)
        {
            return 1d;
        }

        var front = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + (a * Math.Log(x)) + (b * Math.Log(1d - x)));
        return x < (a + 1d) / (a + b + 2d)
            ? front * ContinuedFraction(x, a, b) / a
            : 1d - (front * ContinuedFraction(1d - x, b, a) / b);
    }

    private static double ContinuedFraction(double x, double a, double b)
    {
        const double Tiny = 1e-300;
        var qab = a + b;
        var qap = a + 1d;
        var qam = a - 1d;
        var c = 1d;
        var d = 1d - (qab * x / qap);
        d = Math.Abs(d) < Tiny ? Tiny : d;
        d = 1d / d;
        var h = d;
        for (var m = 1; m <= 500; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1d + (aa * d);
            d = Math.Abs(d) < Tiny ? Tiny : d;
            c = 1d + (aa / c);
            c = Math.Abs(c) < Tiny ? Tiny : c;
            d = 1d / d;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1d + (aa * d);
            d = Math.Abs(d) < Tiny ? Tiny : d;
            c = 1d + (aa / c);
            c = Math.Abs(c) < Tiny ? Tiny : c;
            d = 1d / d;
            var delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1d) < 1e-14)
            {
                break;
            }
        }

        return h;
    }
}
