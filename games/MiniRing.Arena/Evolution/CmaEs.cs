using System.Text.Json;

namespace MiniRing.Arena.Evolution;

/// <summary>CMA-ES が止まった理由（<see cref="CmaEs.StopReason"/>）。点の改善では止めない（世代ごとに種が変わり、世代の間の点は比べられないため）。</summary>
public enum CmaStop
{
    /// <summary>まだ止まらない。</summary>
    None,

    /// <summary>σ が初めの 1/<see cref="CmaEs.SigmaShrinkStop"/> 未満になった（分布が十分に絞られた）。</summary>
    SigmaShrunk,

    /// <summary>世代の上限に達した。</summary>
    MaxGenerations,

    /// <summary>共分散の条件数が <see cref="CmaEs.ConditionStop"/> を超えた（数値として意味を失う）。</summary>
    ConditionTooLarge,
}

/// <summary>保存・再開用の状態（JSON）。世代・平均・σ・C・p_σ・p_c・種と、再開に要る設定。固有分解は C から作り直す（C が同じなら同じ結果）。</summary>
public sealed record CmaEsState
{
    public int Seed { get; init; }

    public int Generation { get; init; }

    public int Lambda { get; init; }

    public int MaxGenerations { get; init; }

    public bool Maximize { get; init; }

    public double InitialSigma { get; init; }

    public double Sigma { get; init; }

    public double[] Mean { get; init; } = [];

    /// <summary>共分散 C を行優先で n × n。</summary>
    public double[] C { get; init; } = [];

    public double[] Ps { get; init; } = [];

    public double[] Pc { get; init; } = [];
}

/// <summary>
/// CMA-ES（Hansen, "The CMA Evolution Strategy: A Tutorial", 2016 と pycma の purecma の既定値）。依存なし・<c>double</c>・決定的。
/// <para>
/// 探索の空間は正規化した [0, 1]。標本は範囲の外へ出てよく、<b>評価と出力にだけ</b>三角波で折り返す（鏡映、<see cref="Reflect"/>）。
/// 平均・共分散の更新は折り返す前の標本で行う（標準のやり方）。<see cref="Ask"/> が返すのは折り返した点（評価に使う点）。
/// 正規乱数は世代ごとに <c>new Random(Hash(seed, generation))</c> から作るので、<see cref="Ask"/> は何度呼んでも同じで、
/// 状態の保存・再開が続けて回した場合と一致する。
/// </para>
/// </summary>
public sealed class CmaEs
{
    /// <summary>止める条件: σ が初めの 1/この値 未満。</summary>
    public const double SigmaShrinkStop = 50.0;

    /// <summary>止める条件: 共分散の固有値の比（条件数）がこれを超えた。</summary>
    public const double ConditionStop = 1e14;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly int _n;
    private readonly int _lambda;
    private readonly int _mu;
    private readonly double[] _weights;
    private readonly double _muEff;
    private readonly double _cs;
    private readonly double _ds;
    private readonly double _cc;
    private readonly double _c1;
    private readonly double _cmu;
    private readonly double _chiN;
    private readonly int _seed;
    private readonly double _initialSigma;

    private double[] _mean;
    private double _sigma;
    private double[,] _c;
    private double[] _ps;
    private double[] _pc;
    private double[,] _b;
    private readonly double[] _d;
    private double _eigenRatio;
    private double[][]? _rawCache;

    /// <param name="initialMean">正規化した空間の初めの平均（[0, 1]）。</param>
    /// <param name="seed">種。世代の乱数の元。</param>
    /// <param name="initialSigma">初めの σ（正規化の空間で。既定 0.15）。</param>
    /// <param name="maximize">真なら点が大きいほど良い。</param>
    /// <param name="lambda">1 世代の候補数。省略で 4 + ⌊3 ln n⌋。</param>
    /// <param name="maxGenerations">世代の上限。</param>
    public CmaEs(IReadOnlyList<double> initialMean, int seed, double initialSigma = 0.15, bool maximize = false, int? lambda = null, int maxGenerations = 1000)
        : this(initialMean, seed, initialSigma, maximize, lambda, maxGenerations, null)
    {
    }

    private CmaEs(IReadOnlyList<double> initialMean, int seed, double initialSigma, bool maximize, int? lambda, int maxGenerations, CmaEsState? restore)
    {
        ArgumentNullException.ThrowIfNull(initialMean);
        _n = initialMean.Count;
        if (_n < 1 || _n > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(initialMean), "次元は 1..30（ヤコビ法の想定）");
        }

        if (!(initialSigma > 0) || !double.IsFinite(initialSigma))
        {
            throw new ArgumentOutOfRangeException(nameof(initialSigma));
        }

        _seed = seed;
        _initialSigma = initialSigma;
        Maximize = maximize;
        MaxGenerations = maxGenerations;
        _lambda = lambda ?? (4 + (int)Math.Floor(3 * Math.Log(_n)));
        if (_lambda < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(lambda), "候補数は 4 以上");
        }

        _mu = _lambda / 2;
        var raw = new double[_mu];
        for (var i = 0; i < _mu; i++)
        {
            raw[i] = Math.Log((_lambda + 1) / 2.0) - Math.Log(i + 1);
        }

        var sum = raw.Sum();
        _weights = raw.Select(w => w / sum).ToArray();
        _muEff = 1.0 / _weights.Sum(w => w * w);

        double n = _n;
        _cc = (4 + (_muEff / n)) / (n + 4 + (2 * _muEff / n));
        _cs = (_muEff + 2) / (n + _muEff + 5);
        _c1 = 2 / (((n + 1.3) * (n + 1.3)) + _muEff);
        _cmu = Math.Min(1 - _c1, 2 * (_muEff - 2 + (1 / _muEff)) / (((n + 2) * (n + 2)) + _muEff));
        _ds = 1 + (2 * Math.Max(0, Math.Sqrt((_muEff - 1) / (n + 1)) - 1)) + _cs;
        _chiN = Math.Sqrt(n) * (1 - (1 / (4 * n)) + (1 / (21 * n * n)));

        _b = new double[_n, _n];
        _d = new double[_n];
        _c = new double[_n, _n];
        if (restore is null)
        {
            _mean = initialMean.ToArray();
            _sigma = initialSigma;
            for (var i = 0; i < _n; i++)
            {
                _c[i, i] = 1;
            }

            _ps = new double[_n];
            _pc = new double[_n];
        }
        else
        {
            Generation = restore.Generation;
            _mean = restore.Mean.ToArray();
            _sigma = restore.Sigma;
            _ps = restore.Ps.ToArray();
            _pc = restore.Pc.ToArray();
            for (var i = 0; i < _n; i++)
            {
                for (var j = 0; j < _n; j++)
                {
                    _c[i, j] = restore.C[(i * _n) + j];
                }
            }
        }

        Decompose();
    }

    public int Dimension => _n;

    /// <summary>1 世代の候補数 λ。</summary>
    public int Lambda => _lambda;

    public int Generation { get; private set; }

    public int MaxGenerations { get; }

    public bool Maximize { get; }

    public double Sigma => _sigma;

    /// <summary>折り返す前の平均（更新に使う値）。</summary>
    public IReadOnlyList<double> RawMean => _mean;

    /// <summary>今の平均を折り返した点。最終の出力はこれ（呼び出し側が <c>Validated()</c> を通す）。</summary>
    public double[] Best => _mean.Select(Reflect).ToArray();

    /// <summary>共分散 C（コピー）。</summary>
    public double[,] Covariance => (double[,])_c.Clone();

    /// <summary>C の固有値の比（最大 / 最小）。</summary>
    public double ConditionNumber => _eigenRatio;

    /// <summary>止める理由（なければ <see cref="CmaStop.None"/>）。Ask / Tell は止まっていても動く（止めるかどうかは呼び出し側）。</summary>
    public CmaStop StopReason
    {
        get
        {
            if (_sigma < _initialSigma / SigmaShrinkStop)
            {
                return CmaStop.SigmaShrunk;
            }

            if (!(_eigenRatio <= ConditionStop))
            {
                return CmaStop.ConditionTooLarge;
            }

            return Generation >= MaxGenerations ? CmaStop.MaxGenerations : CmaStop.None;
        }
    }

    /// <summary>範囲の外の値を [0, 1] へ折り返す（周期 2 の三角波。0 と 1 で反射し、何度でも折れる）。有限でない値は 0.5。</summary>
    public static double Reflect(double x)
    {
        if (!double.IsFinite(x))
        {
            return 0.5;
        }

        var t = x - (2 * Math.Floor(x / 2)); // [0, 2)
        return t <= 1 ? t : 2 - t;
    }

    /// <summary>この世代の候補（折り返し済みの点、λ 個）。何度呼んでも同じ。評価した点数を <see cref="Tell"/> へ候補の番号順に渡す。</summary>
    public double[][] Ask() => Sample().Select(p => p.Select(Reflect).ToArray()).ToArray();

    /// <summary>この世代の折り返す前の標本（更新に使う値。テスト・調査用）。</summary>
    public double[][] AskRaw() => Sample().Select(p => p.ToArray()).ToArray();

    /// <summary>候補の番号順の点数（<see cref="Maximize"/> なら大きいほど良い）で、分布を 1 世代進める。有限でない点数は最悪として扱う。</summary>
    public void Tell(IReadOnlyList<double> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        if (scores.Count != _lambda)
        {
            throw new ArgumentException($"点数は {_lambda} 個（候補と同じ数）", nameof(scores));
        }

        var raw = Sample();
        var cost = new double[_lambda];
        for (var i = 0; i < _lambda; i++)
        {
            var s = scores[i];
            cost[i] = double.IsFinite(s) ? (Maximize ? -s : s) : double.PositiveInfinity;
        }

        // 同点は番号の小さい方を先に（安定した順位）。
        var order = Enumerable.Range(0, _lambda).OrderBy(i => cost[i]).ThenBy(i => i).ToArray();

        var oldMean = _mean;
        var yw = new double[_n];
        var ys = new double[_mu][];
        for (var r = 0; r < _mu; r++)
        {
            var x = raw[order[r]];
            var y = new double[_n];
            for (var k = 0; k < _n; k++)
            {
                y[k] = (x[k] - oldMean[k]) / _sigma;
                yw[k] += _weights[r] * y[k];
            }

            ys[r] = y;
        }

        var newMean = new double[_n];
        for (var k = 0; k < _n; k++)
        {
            newMean[k] = oldMean[k] + (_sigma * yw[k]);
        }

        // C^{-1/2} yw = B D^{-1} B^T yw
        var bty = new double[_n];
        for (var j = 0; j < _n; j++)
        {
            var s = 0.0;
            for (var k = 0; k < _n; k++)
            {
                s += _b[k, j] * yw[k];
            }

            bty[j] = s / _d[j];
        }

        var psFactor = Math.Sqrt(_cs * (2 - _cs) * _muEff);
        var psNorm2 = 0.0;
        var newPs = new double[_n];
        for (var k = 0; k < _n; k++)
        {
            var s = 0.0;
            for (var j = 0; j < _n; j++)
            {
                s += _b[k, j] * bty[j];
            }

            newPs[k] = ((1 - _cs) * _ps[k]) + (psFactor * s);
            psNorm2 += newPs[k] * newPs[k];
        }

        var psNorm = Math.Sqrt(psNorm2);
        var hsig = psNorm / Math.Sqrt(1 - Math.Pow(1 - _cs, 2 * (Generation + 1))) / _chiN < 1.4 + (2 / (_n + 1.0)) ? 1.0 : 0.0;

        var pcFactor = hsig * Math.Sqrt(_cc * (2 - _cc) * _muEff);
        var newPc = new double[_n];
        for (var k = 0; k < _n; k++)
        {
            newPc[k] = ((1 - _cc) * _pc[k]) + (pcFactor * yw[k]);
        }

        var delta = (1 - hsig) * _cc * (2 - _cc);
        var newC = new double[_n, _n];
        for (var i = 0; i < _n; i++)
        {
            for (var j = i; j < _n; j++)
            {
                var rank = 0.0;
                for (var r = 0; r < _mu; r++)
                {
                    rank += _weights[r] * ys[r][i] * ys[r][j];
                }

                var v = ((1 - _c1 - _cmu) * _c[i, j])
                    + (_c1 * ((newPc[i] * newPc[j]) + (delta * _c[i, j])))
                    + (_cmu * rank);
                newC[i, j] = v;
                newC[j, i] = v;
            }
        }

        _mean = newMean;
        _ps = newPs;
        _pc = newPc;
        _c = newC;
        _sigma *= Math.Exp(_cs / _ds * ((psNorm / _chiN) - 1));
        Generation++;
        _rawCache = null;
        Decompose();
    }

    /// <summary>状態を JSON にする（世代の間だけ。<see cref="Ask"/> は状態から作り直せるので、途中の候補は持たない）。</summary>
    public string SaveState()
    {
        var flat = new double[_n * _n];
        for (var i = 0; i < _n; i++)
        {
            for (var j = 0; j < _n; j++)
            {
                flat[(i * _n) + j] = _c[i, j];
            }
        }

        var state = new CmaEsState
        {
            Seed = _seed,
            Generation = Generation,
            Lambda = _lambda,
            MaxGenerations = MaxGenerations,
            Maximize = Maximize,
            InitialSigma = _initialSigma,
            Sigma = _sigma,
            Mean = _mean.ToArray(),
            C = flat,
            Ps = _ps.ToArray(),
            Pc = _pc.ToArray(),
        };
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    /// <summary>JSON から続きを作る。続けて回した場合と同じ軌跡になる。</summary>
    public static CmaEs LoadState(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        var state = JsonSerializer.Deserialize<CmaEsState>(json, JsonOptions) ?? throw new JsonException("状態が空");
        var n = state.Mean.Length;
        if (n == 0 || state.C.Length != n * n || state.Ps.Length != n || state.Pc.Length != n)
        {
            throw new JsonException("状態の次元が合わない");
        }

        return new CmaEs(state.Mean, state.Seed, state.InitialSigma, state.Maximize, state.Lambda, state.MaxGenerations, state);
    }

    /// <summary>種と世代から決まる 31 bit のハッシュ（SplitMix64 風。<c>GetHashCode</c> のような実行ごとに変わる物は使わない）。</summary>
    internal static int Hash(int seed, int generation)
    {
        unchecked
        {
            var z = ((ulong)(uint)seed << 32) | (uint)generation;
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z & 0x7FFFFFFF);
        }
    }

    private double[][] Sample()
    {
        if (_rawCache is not null)
        {
            return _rawCache;
        }

        var random = new Random(Hash(_seed, Generation));
        var raw = new double[_lambda][];
        for (var i = 0; i < _lambda; i++)
        {
            var z = new double[_n];
            for (var k = 0; k < _n; k++)
            {
                z[k] = NextNormal(random);
            }

            // y = B D z、x = m + σ y
            var x = new double[_n];
            for (var r = 0; r < _n; r++)
            {
                var y = 0.0;
                for (var j = 0; j < _n; j++)
                {
                    y += _b[r, j] * _d[j] * z[j];
                }

                x[r] = _mean[r] + (_sigma * y);
            }

            raw[i] = x;
        }

        _rawCache = raw;
        return raw;
    }

    private static double NextNormal(Random random)
    {
        // Box-Muller（double。1 つの標本に 2 つの一様乱数）。
        var u = 1.0 - random.NextDouble();
        var v = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
    }

    /// <summary>C = B D² Bᵀ。固有値は昇順、固有ベクトルは絶対値最大の成分を正にそろえる（決まった規則）。</summary>
    private void Decompose()
    {
        var (values, vectors) = Jacobi(_c, _n);
        var minEig = values[0];
        var maxEig = values[_n - 1];
        _eigenRatio = minEig > 0 ? maxEig / minEig : double.PositiveInfinity;
        _b = vectors;
        for (var i = 0; i < _n; i++)
        {
            _d[i] = Math.Sqrt(Math.Max(values[i], 1e-300));
        }
    }

    private static (double[] Values, double[,] Vectors) Jacobi(double[,] source, int n)
    {
        var a = (double[,])source.Clone();
        var v = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            v[i, i] = 1;
        }

        for (var sweep = 0; sweep < 100; sweep++)
        {
            var off = 0.0;
            var diag = 0.0;
            for (var i = 0; i < n; i++)
            {
                diag += a[i, i] * a[i, i];
                for (var j = i + 1; j < n; j++)
                {
                    off += a[i, j] * a[i, j];
                }
            }

            if (off == 0 || off <= 1e-30 * (diag + off))
            {
                break;
            }

            for (var p = 0; p < n - 1; p++)
            {
                for (var q = p + 1; q < n; q++)
                {
                    if (a[p, q] == 0)
                    {
                        continue;
                    }

                    var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    var t = (theta < 0 ? -1.0 : 1.0) / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1));
                    var c = 1 / Math.Sqrt((t * t) + 1);
                    var s = t * c;
                    for (var k = 0; k < n; k++)
                    {
                        var akp = a[k, p];
                        var akq = a[k, q];
                        a[k, p] = (c * akp) - (s * akq);
                        a[k, q] = (s * akp) + (c * akq);
                    }

                    for (var k = 0; k < n; k++)
                    {
                        var apk = a[p, k];
                        var aqk = a[q, k];
                        a[p, k] = (c * apk) - (s * aqk);
                        a[q, k] = (s * apk) + (c * aqk);
                    }

                    for (var k = 0; k < n; k++)
                    {
                        var vkp = v[k, p];
                        var vkq = v[k, q];
                        v[k, p] = (c * vkp) - (s * vkq);
                        v[k, q] = (s * vkp) + (c * vkq);
                    }
                }
            }
        }

        var order = Enumerable.Range(0, n).OrderBy(i => a[i, i]).ThenBy(i => i).ToArray();
        var values = new double[n];
        var vectors = new double[n, n];
        for (var col = 0; col < n; col++)
        {
            var src = order[col];
            values[col] = a[src, src];
            var big = 0;
            for (var r = 1; r < n; r++)
            {
                if (Math.Abs(v[r, src]) > Math.Abs(v[big, src]) + 1e-12)
                {
                    big = r;
                }
            }

            var sign = v[big, src] < 0 ? -1.0 : 1.0;
            for (var r = 0; r < n; r++)
            {
                vectors[r, col] = sign * v[r, src];
            }
        }

        return (values, vectors);
    }
}
