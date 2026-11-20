using System.Diagnostics;
using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 評価の雑音を測る（<c>noiselab</c>）。試合は種と値で決定的なので、同じ値を同じ種で 2 回測っても差は 0 で雑音は測れない。
/// 雑音は「種を変えたときの揺れ」と「ごく小さく値を変えたときの揺れ（体の動きはカオス的で、わずかな違いで軌道が分かれる）」の 2 つで測る。
/// <list type="bullet">
/// <item>(a) 種による揺れ: 同じ値を、重ならない 2 つの種の組で測り、標本ごとの点の標準偏差。</item>
/// <item>(b) 小さな変更での揺れ: champion と、<c>Cross.Duration</c> を +0.001 s（守りのドリルは <c>Slip.HeadX</c> を +0.001）変えた champion′ を同じ種で対にし、d_i の標準偏差と ρ。</item>
/// <item>(c) 本当の差を言うのに要る数: champion と champion-v1 を対にし、効果の大きさ ÷ 標準誤差が 2.5 を超えるのに要る標本の数。</item>
/// <item>(d) 1 標本の長さ: 秒数ごとに (c) を同じ壁時計あたりで比べる（標準誤差は 1 / √n、n は壁時計に比例するので、t 値 ÷ √壁時計 が同じ壁時計あたりの検出力）。</item>
/// </list>
/// </summary>
internal sealed class NoiseLab(int matchSamples, int drillSamples, int seedBase, int? maxParallel, Action<string> log)
{
    /// <summary>試合の相手。</summary>
    public static readonly string[] Opponents = ["boxer", "outboxer", "swarmer", "counter", "slugger", "jabber"];

    /// <summary>効果の大きさ ÷ 標準誤差がこれを超えたら差があると言う目安（(c)・(d)）。</summary>
    public const double TargetT = 2.5;

    /// <summary>ドリルの合図の遅れ [s]（回避の基本動作の測り方と同じ）。</summary>
    public const float DrillCue = 0.0671f;

    /// <summary>champion′ の変え幅（Cross の Duration [s]、Slip の HeadX）。測れる最小のずれとして 1 mm / 0.001。</summary>
    public const float Nudge = 0.001f;

    private readonly int _cores = Environment.ProcessorCount;

    /// <summary>1 つの測定（値 × 秒数 × 種の組）の結果と壁時計。</summary>
    private sealed record Measured(double[] Points, double WallSeconds);

    public string Run(IReadOnlySet<string> only, IReadOnlyList<float> matchSeconds, IReadOnlyList<float> drillSeconds, float matchReference, float drillReference)
    {
        var sb = new StringBuilder();
        var total = Stopwatch.StartNew();
        if (only.Contains("drill"))
        {
            sb.AppendLine(DrillSection(drillSeconds, drillReference));
        }

        if (only.Contains("match"))
        {
            sb.AppendLine(MatchSection(matchSeconds, matchReference));
        }

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"全体の壁時計: {total.Elapsed.TotalMinutes:0.0} 分（{_cores} コア、並列の数 {(maxParallel?.ToString(CultureInfo.InvariantCulture) ?? "既定")}）。"));
        return sb.ToString();
    }

    private static List<int> Seeds(int baseSeed, int first, int count) => Enumerable.Range(first, count).Select(k => baseSeed + (1000 * k)).ToList();

    private static Measured Timed(Func<double[]> measure)
    {
        var watch = Stopwatch.StartNew();
        var points = measure();
        return new Measured(points, watch.Elapsed.TotalSeconds);
    }

    // ---------------------------------------------------------------- ドリル

    private string DrillSection(IReadOnlyList<float> secondsList, float reference)
    {
        var n = drillSamples;
        var champion = BotCatalog.Champion;
        var v1 = BotCatalog.ChampionV1;
        // champion の Slip.HeadX は 0.8 で、範囲の上限 0.6 に収められて使われる。上限の外へ +0.001 しても収め直されて同じ値になる（最初の測定で
        // 全標本の差が 0 になって気づいた）ので、収めた後の値から、範囲の内側へ 0.001 動かす。
        var slipNow = champion.Defenses.Of(Defense.Slip).Validated();
        var headNudge = slipNow.HeadX + Nudge > DefenseRanges.HeadX.Max ? -Nudge : Nudge;
        var prime = champion with { Defenses = champion.Defenses.With(Defense.Slip, slipNow with { HeadX = slipNow.HeadX + headNudge }) };
        var set1 = Seeds(seedBase, 0, n);
        var set2 = Seeds(seedBase, n, n);
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"## ドリル（相手の型 \"2\" = 右ストレート、受け手の守りは Slip に固定、合図 {DrillCue:0.00} s、打ち返しあり）"));
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"1 標本 = 1 つの種の 1 本のドリル。点 = きれいに外した割合 − 頭に当たった割合 − ガードに当たった割合（`PunchDrillResult.Score`、拳の数で割った割合どうしの差）。標本は種の組ごとに {n} 個（種は {seedBase} + 1000 k）。"));
        sb.AppendLine();

        double[] Score(BotStyle style, float seconds, IReadOnlyList<int> seeds) => DrillScore(style, seconds, seeds);

        var refSeconds = reference;
        log($"[drill] {refSeconds:0} 秒: champion 1 組目");
        var champ1 = Timed(() => Score(champion, refSeconds, set1));
        log($"[drill]   {champ1.WallSeconds:0.0} s");
        var champ2 = Timed(() => Score(champion, refSeconds, set2));
        var nudged = Timed(() => Score(prime, refSeconds, set1));
        sb.AppendLine(SeedNoiseTable("ドリル", refSeconds, champ1, champ2));
        sb.AppendLine(SmallChangeTable("ドリル（Slip.HeadX を範囲の内側へ 0.001）", refSeconds, champ1, nudged));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"### (c)(d) champion 対 champion-v1（同じ {n} 種で対にする）"));
        sb.AppendLine();
        sb.AppendLine(PowerHeader);
        foreach (var seconds in secondsList)
        {
            var a = seconds == refSeconds ? champ1 : Timed(() => Score(champion, seconds, set1));
            var b = Timed(() => Score(v1, seconds, set1));
            sb.AppendLine(PowerRow("ドリル", seconds, a, b, n));
            log($"[drill] {seconds:0} 秒: champion {a.WallSeconds:0.0} s, v1 {b.WallSeconds:0.0} s");
        }

        sb.AppendLine();

        // 逐次の道具の動作例: 使わなかった種（3 組目）で固定 n の確認も行う。
        PairedEvaluation.Scorer sa = seeds => Score(champion, refSeconds, seeds);
        PairedEvaluation.Scorer sbv = seeds => Score(v1, refSeconds, seeds);
        var race = PairedEvaluation.Race(sa, sbv, k => seedBase + (1000 * (3 * n + k)), nMin: Math.Min(8, n), k: PairedEvaluation.MinBatch(1, _cores), nMax: n, boutsPerSample: 1, cores: _cores);
        var confirm = PairedEvaluation.Confirm(sa, sbv, Seeds(seedBase, 4 * n, n));
        sb.AppendLine("### 逐次（予算の配分）と固定 n の確認の動作例（champion 対 champion-v1）");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- 逐次: {race}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- 使わなかった種での固定 n の確認: {confirm}");
        sb.AppendLine();
        return sb.ToString();
    }

    private double[] DrillScore(BotStyle style, float seconds, IReadOnlyList<int> seeds)
    {
        var book = style.Defenses;
        var defender = DefenseDrill.Defender(book, defend: true, DefenseDrill.DefaultRange, counters: true) with { GuardHeight = style.GuardHeight };
        var points = new double[seeds.Count];
        Parallel.For(0, seeds.Count, new ParallelOptions { MaxDegreeOfParallelism = maxParallel ?? _cores },
            i => points[i] = DefenseDrill.Punch("2", Defense.Slip, defender, seeds[i], seconds, DefenseDrill.DefaultRange, DrillCue).Score);
        return points;
    }

    // ---------------------------------------------------------------- 試合

    private string MatchSection(IReadOnlyList<float> secondsList, float reference)
    {
        var n = matchSamples;
        var champion = Contender.Of(BotCatalog.Champion);
        var v1 = Contender.Of(BotCatalog.ChampionV1);
        var crossed = BotCatalog.Champion.Punches.Cross;
        var prime = Contender.Of(BotCatalog.Champion with
        {
            Name = "champion-prime",
            Punches = BotCatalog.Champion.Punches with { Cross = crossed with { Duration = crossed.Duration + Nudge } },
        });
        var opponents = Opponents.Select(Contender.Named).ToList();
        var set1 = Seeds(seedBase, 0, n);
        var set2 = Seeds(seedBase, n, n);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## 試合（相手: {string.Join("・", Opponents)}）");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"1 標本 = 1 つの種 × 相手 {opponents.Count} 人 × 両コーナー（{opponents.Count * 2} 試合）の合計に `MatchScore.Of`（勝ち − 負け + 0.9 tanh((与えた − 受けた) / 400)）をかけた点。" +
            $"全体の合計にかける `MatchTrainer` の点とは尺度が違う（標本 1 つは {opponents.Count * 2} 試合ぶんで、勝ち − 負けの範囲は ±{opponents.Count * 2}）。標本は種の組ごとに {n} 個（種は {seedBase} + 1000 k、相手 i との試合は + 101 i）。"));
        sb.AppendLine();

        Measured Run(Contender mine, float seconds, IReadOnlyList<int> seeds)
        {
            var sampler = new MatchSampler(opponents, seconds, maxParallel);
            return Timed(() => sampler.Score(mine, seeds));
        }

        log($"[match] {reference:0} 秒: champion 1 組目");
        var champ1 = Run(champion, reference, set1);
        log($"[match]   {champ1.WallSeconds:0.0} s");
        var champ2 = Run(champion, reference, set2);
        log($"[match]   champion 2 組目 {champ2.WallSeconds:0.0} s");
        var nudged = Run(prime, reference, set1);
        log($"[match]   champion-prime {nudged.WallSeconds:0.0} s");
        sb.AppendLine(SeedNoiseTable("試合", reference, champ1, champ2));
        sb.AppendLine(SmallChangeTable("試合（Cross.Duration +0.001 s）", reference, champ1, nudged));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"### (c)(d) champion 対 champion-v1（同じ {n} 種で対にする）"));
        sb.AppendLine();
        sb.AppendLine(PowerHeader);
        foreach (var seconds in secondsList)
        {
            var a = seconds == reference ? champ1 : Run(champion, seconds, set1);
            var b = Run(v1, seconds, set1);
            sb.AppendLine(PowerRow("試合", seconds, a, b, n));
            log($"[match] {seconds:0} 秒: champion {a.WallSeconds:0.0} s, v1 {b.WallSeconds:0.0} s");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    // ---------------------------------------------------------------- 表

    private static string SeedNoiseTable(string what, float seconds, Measured one, Measured two)
    {
        var sdOne = PairedEvaluation.StdDev(one.Points);
        var sdTwo = PairedEvaluation.StdDev(two.Points);
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"### (a) 種による揺れ（{what}、champion、{seconds:0} 秒、重ならない 2 つの種の組）"));
        sb.AppendLine();
        sb.AppendLine("| 種の組 | n | 点の平均 | 点の標準偏差 | 平均の標準誤差 |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| 1 組目 | {one.Points.Length} | {one.Points.Average():0.000} | {sdOne:0.000} | {sdOne / Math.Sqrt(one.Points.Length):0.000} |"));
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| 2 組目 | {two.Points.Length} | {two.Points.Average():0.000} | {sdTwo:0.000} | {sdTwo / Math.Sqrt(two.Points.Length):0.000} |"));
        var pooled = Math.Sqrt(0.5d * ((sdOne * sdOne) + (sdTwo * sdTwo)));
        var diff = one.Points.Average() - two.Points.Average();
        var seDiff = Math.Sqrt(((sdOne * sdOne) / one.Points.Length) + ((sdTwo * sdTwo) / two.Points.Length));
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"同じ値の 2 つの組の平均の差 {diff:+0.000;-0.000;0.000}（その標準誤差 {seDiff:0.000}、差 ÷ 標準誤差 = {(seDiff > 0d ? diff / seDiff : 0d):+0.00;-0.00;0.00}）。合わせた標準偏差 {pooled:0.000}。"));
        sb.AppendLine();
        return sb.ToString();
    }

    private static string SmallChangeTable(string what, float seconds, Measured champion, Measured nudged)
    {
        var result = PairedEvaluation.Compute(champion.Points, nudged.Points);
        var independentSd = Math.Sqrt(2d) * Math.Sqrt(0.5d * (Math.Pow(PairedEvaluation.StdDev(champion.Points), 2d) + Math.Pow(PairedEvaluation.StdDev(nudged.Points), 2d)));
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"### (b) 小さな変更での揺れ（{what}、{seconds:0} 秒、同じ種で対）"));
        sb.AppendLine();
        sb.AppendLine("| n | 平均の差 | d_i の標準偏差 | 独立なら（√2 × 点の標準偏差） | ρ | 符号（+/−/=） | 符号検定 p |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---|---:|");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| {result.N} | {result.MeanDiff:+0.000;-0.000;0.000} | {result.SdDiff:0.000} | {independentSd:0.000} | {result.Rho:0.00} | {result.Plus}/{result.Minus}/{result.Ties} | {result.SignP:0.000} |"));
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"点(A) と 点(B) の相関 ρ = {result.Rho:0.00}。同じ種で対にすると d_i の分散は独立のときの約 (1 − ρ) = {1d - result.Rho:0.00} 倍になる（d_i の標準偏差 ÷ 独立なら = {result.SdDiff / independentSd:0.00}）。"));
        sb.AppendLine(result.Rho < 0.2d
            ? "ρ が 0 に近い: ごく小さな値の変更でも軌道が分かれ、同じ種で対にしても分散は減らない。"
            : result.Rho < 0.7d
            ? "ρ は中くらい: ごく小さな値の変更でも軌道が分かれ、種を揃える効きは一部にとどまる。"
            : "ρ が大きい: 同じ種で対にすると、種の運の分が引き算で消えて分散が減る。");
        sb.AppendLine();
        return sb.ToString();
    }

    private const string PowerHeader =
        "| 種類 | 秒数 | n | champion の平均 | v1 の平均 | 平均の差 | d_i の標準偏差 | 標準誤差 | t = 差 ÷ 標準誤差 | 95% 区間 | 符号 +/−/= | 符号検定 p | ρ | 1 標本の壁時計 [s] | t を 2.5 にする n | その壁時計 [分] | t ÷ √壁時計[s]（大きいほど良い） |\n"
        + "|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---:|---:|---:|---:|---:|---:|";

    private static string PowerRow(string what, float seconds, Measured a, Measured b, int n)
    {
        var r = PairedEvaluation.Compute(a.Points, b.Points);
        var wall = a.WallSeconds + b.WallSeconds; // champion と v1 の両方を測る費用
        var perSample = wall / n;
        var need = r.SamplesFor(TargetT);
        var needText = need is { } m ? m.ToString(CultureInfo.InvariantCulture) : "—";
        var minutes = need is { } m2 ? (m2 * perSample / 60d).ToString("0.0", CultureInfo.InvariantCulture) : "—";
        var power = wall > 0d ? r.TValue / Math.Sqrt(wall) : 0d;
        return string.Create(CultureInfo.InvariantCulture,
            $"| {what} | {seconds:0} | {r.N} | {r.MeanA:0.000} | {r.MeanB:0.000} | {r.MeanDiff:+0.000;-0.000;0.000} | {r.SdDiff:0.000} | {r.StdErr:0.000} | {r.TValue:+0.00;-0.00;0.00} | {r.CiLow:0.000}〜{r.CiHigh:0.000} | {r.Plus}/{r.Minus}/{r.Ties} | {r.SignP:0.000} | {r.Rho:0.00} | {perSample:0.0} | {needText} | {minutes} | {power:+0.000;-0.000;0.000} |");
    }
}
