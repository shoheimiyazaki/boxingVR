using MiniRing.Bots;

namespace MiniRing.Arena.Evolution;

/// <summary>
/// 1 つの値の遺伝子: 正規化した [0, 1] と実際の値 [<see cref="Lo"/>, <see cref="Hi"/>] を線形に写す。
/// 範囲は「中心の値 ± 幅」を、物理として意味のある範囲の表（<see cref="ValueRange"/>）で切ったもの。
/// </summary>
public sealed record Gene(string Name, float Lo, float Hi, float Center)
{
    /// <summary>[0, 1] の位置から実際の値へ（範囲の外は端へ収める）。</summary>
    public float Decode(double x) => (float)(Lo + (Math.Clamp(double.IsFinite(x) ? x : 0.5, 0.0, 1.0) * (Hi - Lo)));

    /// <summary>実際の値から [0, 1] の位置へ（範囲の外は端へ収める。幅が 0 の遺伝子は 0.5）。</summary>
    public double Encode(float value)
    {
        var span = (double)Hi - Lo;
        return span <= 0 ? 0.5 : Math.Clamp((value - (double)Lo) / span, 0.0, 1.0);
    }
}

/// <summary>
/// 拳 1 技の遺伝子（<see cref="PunchMove"/> の 9 値）。探索の範囲は<b>既定の値を中心にした技ごとの幅</b>（<see cref="Widths"/>）を
/// <see cref="PunchRanges"/> で切ったもの。広い範囲で探すと、ストレートが実質フックに育つことがある。技の種類は既定の値で決まる
/// （ボットは技の名前で選ぶ）ので、そうなると「ストレートとして選ばれるフック」になる。幅で、技の正体を保つ。
/// </summary>
public sealed class PunchGenome
{
    /// <summary>
    /// 値ごとの探索の幅（既定の値 ± 幅。1 か所の表）。溜めの横・低さ・打ち抜き・踏み込み・振り抜きは ±0.3（腕長単位。
    /// 既定の値から隣の技の値までの 1/3 ほど: ストレートの溜めの横 0.1 とフックの 0.9 の間は 0.8）、時間は ±0.20 s
    /// （既定 0.5〜0.84 s の 1/3 ほど）、頭のずれは ±0.12（範囲 ±0.4 の 1/3）。
    /// </summary>
    public static IReadOnlyDictionary<string, float> Widths { get; } = new Dictionary<string, float>
    {
        [nameof(PunchMove.Lateral)] = 0.3f,
        [nameof(PunchMove.Low)] = 0.3f,
        [nameof(PunchMove.Overshoot)] = 0.3f,
        [nameof(PunchMove.Duration)] = 0.201f,
        [nameof(PunchMove.HeadShiftX)] = 0.12f,
        [nameof(PunchMove.HeadShiftY)] = 0.12f,
        [nameof(PunchMove.HeadShiftZ)] = 0.12f,
        [nameof(PunchMove.StepIn)] = 0.3f,
        [nameof(PunchMove.Sweep)] = 0.3f,
    };

    private readonly PunchMove _baseline;

    /// <param name="technique">技（フェイントだけ Extend を範囲で収める）。</param>
    /// <param name="baseline">中心にする値。省略で <see cref="PunchBook.Default"/> の既定の値。</param>
    public PunchGenome(Technique technique, PunchMove? baseline = null)
    {
        Technique = technique;
        _baseline = baseline ?? PunchBook.Default.MoveOf(technique);
        Genes =
        [
            Make(PunchRanges.Lateral, _baseline.Lateral),
            Make(PunchRanges.Low, _baseline.Low),
            Make(PunchRanges.Overshoot, _baseline.Overshoot),
            Make(PunchRanges.Duration, _baseline.Duration),
            Make(PunchRanges.HeadShiftX, _baseline.HeadShiftX),
            Make(PunchRanges.HeadShiftY, _baseline.HeadShiftY),
            Make(PunchRanges.HeadShiftZ, _baseline.HeadShiftZ),
            Make(PunchRanges.StepIn, _baseline.StepIn),
            Make(PunchRanges.Sweep, _baseline.Sweep),
        ];
    }

    public Technique Technique { get; }

    /// <summary>遺伝子（Lateral, Low, Overshoot, Duration, HeadShift.X/Y/Z, StepIn, Sweep の順）。</summary>
    public IReadOnlyList<Gene> Genes { get; }

    public int Dimension => Genes.Count;

    /// <summary>中心の値の位置（CMA-ES の初めの平均）。</summary>
    public double[] Initial() => Encode(_baseline);

    /// <summary>値 → 遺伝子（[0, 1]）。</summary>
    public double[] Encode(PunchMove move)
    {
        ArgumentNullException.ThrowIfNull(move);
        return
        [
            Genes[0].Encode(move.Lateral),
            Genes[1].Encode(move.Low),
            Genes[2].Encode(move.Overshoot),
            Genes[3].Encode(move.Duration),
            Genes[4].Encode(move.HeadShiftX),
            Genes[5].Encode(move.HeadShiftY),
            Genes[6].Encode(move.HeadShiftZ),
            Genes[7].Encode(move.StepIn),
            Genes[8].Encode(move.Sweep),
        ];
    }

    /// <summary>遺伝子（[0, 1]。範囲の外は端へ収める）→ 値。技の正体（Lead・ToBody）と Extend は中心の値のまま、<c>Validated()</c> を通す。</summary>
    public PunchMove Decode(IReadOnlyList<double> x)
    {
        ArgumentNullException.ThrowIfNull(x);
        if (x.Count != Dimension)
        {
            throw new ArgumentException($"遺伝子は {Dimension} 個", nameof(x));
        }

        var move = _baseline with
        {
            Lateral = Genes[0].Decode(x[0]),
            Low = Genes[1].Decode(x[1]),
            Overshoot = Genes[2].Decode(x[2]),
            Duration = Genes[3].Decode(x[3]),
            HeadShiftX = Genes[4].Decode(x[4]),
            HeadShiftY = Genes[5].Decode(x[5]),
            HeadShiftZ = Genes[6].Decode(x[6]),
            StepIn = Genes[7].Decode(x[7]),
            Sweep = Genes[8].Decode(x[8]),
        };
        return move.Validated(PunchBook.Default.MoveOf(Technique), Technique == Technique.Feint);
    }

    private static Gene Make(ValueRange range, float center)
    {
        var width = Widths[range.Name];
        // 外から渡した中心（チャンピオンの JSON など）が範囲の外でも、先に範囲へ収めてから幅を取る（Lo > Hi で遺伝子が動かなくなるのを防ぐ。レビュー指摘）。
        var inside = range.Clamp(center, range.Min);
        return new Gene(range.Name, Math.Max(range.Min, inside - width), Math.Min(range.Max, inside + width), inside);
    }
}

/// <summary>
/// 守り 1 技の遺伝子（<c>Mutation.Move</c> が揺らす 15 値のうち、動かさない物を除いた値）。範囲は <see cref="DefenseRanges"/> の表そのもの
/// （<c>Validated()</c>・<c>Mutation</c> と共有）。ブロックの <c>HandsUp</c> は 1 に固定（常に拳を上げる）、
/// 横へ抜けない技（<c>Sweep</c> でない）の <c>EndX</c> は使われないので外す。したがって次元は
/// 抜ける技 15（ブロックでない）、抜けない技 14、ブロック 13。
/// </summary>
public sealed class DefenseGenome
{
    private readonly DefenseMove _baseline;
    private readonly List<(Gene Gene, Func<DefenseMove, float> Get, Func<DefenseMove, float, DefenseMove> Set)> _slots = [];

    /// <param name="defense">守りの技（ブロックだけ <c>HandsUp</c> を固定する）。</param>
    /// <param name="baseline">中心にする値。省略で <see cref="DefenseBook.Default"/> の既定の値。</param>
    public DefenseGenome(Defense defense, DefenseMove? baseline = null)
    {
        Defense = defense;
        _baseline = baseline ?? DefenseBook.Default.Of(defense);
        Add(DefenseRanges.HeadX, m => m.HeadX, (m, v) => m with { HeadX = v });
        Add(DefenseRanges.HeadY, m => m.HeadY, (m, v) => m with { HeadY = v });
        Add(DefenseRanges.HeadZ, m => m.HeadZ, (m, v) => m with { HeadZ = v });
        if (_baseline.Sweep)
        {
            Add(new ValueRange(nameof(DefenseMove.EndX), DefenseRanges.HeadX.Min, DefenseRanges.HeadX.Max), m => m.EndX, (m, v) => m with { EndX = v });
        }

        Add(DefenseRanges.Duration, m => m.Duration, (m, v) => m with { Duration = v });
        Add(DefenseRanges.Move, m => m.Move, (m, v) => m with { Move = v });
        Add(DefenseRanges.Step, m => m.Step, (m, v) => m with { Step = v });
        Add(DefenseRanges.Rate, m => m.Rate, (m, v) => m with { Rate = v });
        if (defense != Defense.Block)
        {
            Add(DefenseRanges.HandsUp, m => m.HandsUp, (m, v) => m with { HandsUp = v });
        }

        Add(DefenseRanges.GloveWidth, m => m.GloveWidth, (m, v) => m with { GloveWidth = v });
        Add(DefenseRanges.GloveHeight, m => m.GloveHeight, (m, v) => m with { GloveHeight = v });
        Add(DefenseRanges.GloveForward, m => m.GloveForward, (m, v) => m with { GloveForward = v });
        Add(DefenseRanges.BodyDrop, m => m.BodyDrop, (m, v) => m with { BodyDrop = v });
        Add(DefenseRanges.TempleWidth, m => m.TempleWidth, (m, v) => m with { TempleWidth = v });
        Add(DefenseRanges.GloveAnchor, m => m.GloveAnchor, (m, v) => m with { GloveAnchor = v });
    }

    public Defense Defense { get; }

    public IReadOnlyList<Gene> Genes => _slots.Select(s => s.Gene).ToArray();

    public int Dimension => _slots.Count;

    /// <summary>中心の値の位置（CMA-ES の初めの平均）。</summary>
    public double[] Initial() => Encode(_baseline);

    /// <summary>値 → 遺伝子（[0, 1]）。</summary>
    public double[] Encode(DefenseMove move)
    {
        ArgumentNullException.ThrowIfNull(move);
        return _slots.Select(s => s.Gene.Encode(s.Get(move))).ToArray();
    }

    /// <summary>遺伝子（[0, 1]。範囲の外は端へ収める）→ 値。遺伝子に無い値（<c>Vs*</c>・カウンター・技の正体）は中心の値のまま、<c>Validated()</c> を通す。</summary>
    public DefenseMove Decode(IReadOnlyList<double> x)
    {
        ArgumentNullException.ThrowIfNull(x);
        if (x.Count != Dimension)
        {
            throw new ArgumentException($"遺伝子は {Dimension} 個", nameof(x));
        }

        var move = _baseline;
        for (var i = 0; i < _slots.Count; i++)
        {
            move = _slots[i].Set(move, _slots[i].Gene.Decode(x[i]));
        }

        return move.Validated();
    }

    private void Add(ValueRange range, Func<DefenseMove, float> get, Func<DefenseMove, float, DefenseMove> set)
        => _slots.Add((new Gene(range.Name, range.Min, range.Max, get(_baseline)), get, set));
}
