using System.Globalization;
using System.Text;
using System.Text.Json;
using MiniRing.Bots;
using MiniRing.Highlights;

namespace MiniRing.Arena;

/// <summary>
/// 試合の面白さ（派手さ・ドラマ・緩急）を測る（docs/EXCITEMENT.md）。
/// <list type="bullet">
/// <item><c>excitement --red champion --blue swarmer --seconds 67 --seeds 4 [--curve 接頭辞]</c>: 1 組を何試合か回し、出来事の一覧とまとめ。
/// <c>--curve</c> は各試合の曲線（秒, 興奮, p_red）を <c>接頭辞_種.csv</c> に書く（tools/excitement_curve.py で図にする）。</item>
/// <item><c>excitementleague --pairs champion:swarmer,... --seeds 2 --seconds 67 --out docs/arena/excitement_日付.md --label 名前</c>: 何組かの表。</item>
/// <item><c>excitementfit --seeds 8 --seconds 67 --out docs/arena/winmodel_日付.json</c>: 観客の見込みのモデル（<see cref="WinModel"/>）を当てはめる。</item>
/// </list>
/// </summary>
internal static class ExcitementLab
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>既定の組（拳のボットの、打ち合い・逃げ足・カウンターがそろう組）。</summary>
    public static readonly string[] DefaultPairs = ["champion:swarmer", "slugger:outboxer", "counter:jabber"];

    /// <summary>見込みのモデルを当てはめる組（既定の組より広く）。</summary>
    public static readonly string[] FitPairs = ["champion:swarmer", "slugger:outboxer", "counter:jabber", "boxer:slugger", "swarmer:counter", "jabber:champion"];

    public static int SingleCommand(Options o) =>
        Single(o.Get("red") ?? "champion", o.Get("blue") ?? "swarmer", o.Float("seconds", 67.14f, min: 1f), o.Int("seeds", 4, min: 1), o.Int("seed", 1), o.Get("out"), o.Get("curve"));

    public static int LeagueCommand(Options o) =>
        League(o.List("pairs", DefaultPairs), o.Float("seconds", 67.14f, min: 1f), o.Int("seeds", 2, min: 1), o.Int("seed", 1), o.Get("out"), o.Get("label") ?? "");

    public static int FitCommand(Options o) =>
        Fit(o.List("pairs", FitPairs), o.Float("seconds", 67.14f, min: 1f), o.Int("seeds", 8, min: 2), o.Int("seed", 1), o.Get("out"));

    public static int Single(string red, string blue, float seconds, int seeds, int seed, string? outPath, string? curvePrefix = null)
    {
        var sb = new StringBuilder();
        var all = new List<ExcitementSummary>();
        for (var i = 0; i < seeds; i++)
        {
            var h = ExcitementRun.Bout(Contender.Named(red), Contender.Named(blue), seed + i, seconds);
            var s = h.Summarize();
            all.Add(s);
            if (curvePrefix is not null)
            {
                WriteCurve($"{curvePrefix}_{seed + i}.csv", h, s);
            }

            sb.Append(CultureInfo.InvariantCulture, $"### {red}（赤） vs {blue}（青） 種 {seed + i}、{seconds:0} 秒（勝ち {(h.Winner > 0 ? red : h.Winner < 0 ? blue : "引き分け")}）\n\n```\n{ExcitementRun.EventList(h, red, blue)}```\n\n{s.Line()}\n\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"**まとめ（{seeds} 試合）**: {ExcitementSummary.Mean(all).Line()}\n");
        Console.WriteLine(sb.ToString());
        if (outPath is not null)
        {
            File.WriteAllText(outPath, sb.ToString());
        }

        return 0;
    }

    /// <summary>曲線: 秒, 興奮, p_red と、出来事の行（#秒,種類,強さ）。</summary>
    public static void WriteCurve(string path, MatchHighlights h, ExcitementSummary s)
    {
        ArgumentNullException.ThrowIfNull(h);
        ArgumentNullException.ThrowIfNull(s);
        var lines = new List<string> { "t,arousal,p" };
        var arousal = s.ArousalCurve!;
        var belief = s.DramaCurve!;
        var origin = h.Beliefs.Count > 0 ? h.Beliefs[0].Time : 0f;
        for (var k = 0; k < arousal.Curve.Length; k++)
        {
            var t = origin + (k * arousal.SampleSeconds);
            var j = Array.FindLastIndex(belief.Times, x => x <= t + 1e-4f);
            lines.Add(FormattableString.Invariant($"{t:0.00},{arousal.Curve[k]:0.000},{(j >= 0 ? belief.P[j] : 0.5f):0.000}"));
        }

        lines.AddRange(h.Events.Select(e => FormattableString.Invariant($"#{e.Time:0.00},{e.Kind},{e.Intensity:0.00}")));
        File.WriteAllLines(path, lines);
    }

    public static int League(IReadOnlyList<string> pairs, float seconds, int seeds, int seed, string? outPath, string label)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var rows = new List<(string Pair, ExcitementSummary Mean)>();
        var all = new List<ExcitementSummary>();
        foreach (var pair in pairs)
        {
            var (red, blue) = Split(pair);
            var bouts = new List<ExcitementSummary>();
            for (var i = 0; i < seeds; i++)
            {
                var s = ExcitementRun.Bout(Contender.Named(red), Contender.Named(blue), seed + i, seconds).Summarize();
                Console.Error.WriteLine($"{pair} 種 {seed + i}: {s.Line()}");
                bouts.Add(s);
            }

            rows.Add((pair, ExcitementSummary.Mean(bouts)));
            all.AddRange(bouts);
        }

        var text = Table(label, rows, ExcitementSummary.Mean(all), seconds, seeds, seed);
        Console.WriteLine(text);
        Console.Error.WriteLine($"{all.Count} bouts in {watch.Elapsed.TotalSeconds:0}s");
        if (outPath is not null)
        {
            File.WriteAllText(outPath, text);
        }

        return 0;
    }

    private static (string Red, string Blue) Split(string pair)
    {
        var parts = pair.Split(':');
        return parts.Length == 2 ? (parts[0], parts[1]) : throw new ArgumentException($"--pairs は 赤:青 をカンマで区切ってください（{pair}）。");
    }

    private static string Table(string label, List<(string Pair, ExcitementSummary Mean)> rows, ExcitementSummary total, float seconds, int seeds, int seed)
    {
        var kinds = Enum.GetValues<HighlightKind>();
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"### 面白さ {label}（各組 {seeds} 試合 × {seconds:0} 秒、種 {seed}〜、PhysicsVersion {Fighter.PhysicsVersion}・LogicVersion {BoxerBot.LogicVersion}）\n\n");
        sb.Append("| 組 | 総合 | 派手さ/分 | ドラマ | 驚き | はらはら | 最大の揺れ | 入れ替わり | 緩急 | 退屈 % | 最長の退屈 s | 山/分 | 空白 % | 生ダメージ | ")
            .Append(string.Join(" | ", kinds)).Append(" |\n");
        sb.Append("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|").Append(string.Concat(kinds.Select(_ => "---|"))).Append('\n');
        foreach (var (pair, s) in rows.Append(("**全体**", total)))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"| {pair} | {s.Score:0.0} | {s.Spectacle:0.0} | {s.Drama:0.0} | {s.Surprise:0.00} | {s.MeanSuspense:0.000} | {s.LargestSwing:0.00} | {s.LeadChanges} | {s.Pacing:0.0} | {s.BoredShare * 100f:0} | {s.LongestBored:0.0} | {s.PeaksPerMinute:0.0} | {s.DeadShare * 100f:0} | {s.RawDamage:0} | ")
                .Append(string.Join(" | ", kinds.Select(k => s.Count(k).ToString(CultureInfo.InvariantCulture)))).Append(" |\n");
        }

        sb.Append("\n出来事の数・入れ替わりは全試合の合計、最長の退屈は最大、ほかは 1 試合あたりの平均。\n");
        return sb.ToString();
    }

    /// <summary>
    /// 見込みのモデルの当てはめ: 偶数番の種で当てはめて奇数番の種で確かめ（使っていない種での当てはまり）、最後に全部で当てはめ直す。
    /// 引き分けの試合は使わない。標本は 1 秒ごとに間引き、赤と青を入れ替えた鏡の標本を足す。
    /// </summary>
    public static int Fit(IReadOnlyList<string> pairs, float seconds, int seeds, int seed, string? outPath)
    {
        var train = new List<(float[] X, bool RedWon)>();
        var test = new List<(float[] X, bool RedWon)>();
        var late = new List<(float[] X, bool RedWon)>();
        var draws = 0;
        var bouts = 0;
        var stride = Math.Max(1, (int)MathF.Round(1f / ExcitementTuning.Default.BeliefSample));
        foreach (var pair in pairs)
        {
            var (red, blue) = Split(pair);
            for (var i = 0; i < seeds; i++)
            {
                var h = ExcitementRun.Bout(Contender.Named(red), Contender.Named(blue), seed + i, seconds);
                bouts++;
                Console.Error.WriteLine($"{pair} 種 {seed + i}: 勝ち {h.Winner}");
                if (h.Winner == 0)
                {
                    draws++;
                    continue;
                }

                // 赤と青を入れ替えた標本も足す（観客にはコーナーの有利は見えない: 互角なら 0.5、切片は 0 に近くなる）。
                var samples = h.Beliefs.Where((_, k) => k % stride == 0)
                    .SelectMany(b => new[] { (b.Features, h.Winner > 0), (b.Features.Select(x => -x).ToArray(), h.Winner < 0) }).ToList();
                (i % 2 == 0 ? train : test).AddRange(samples);
                if (i % 2 == 1)
                {
                    late.AddRange(samples.Skip(samples.Count * 3 / 4));
                }
            }
        }

        var held = WinModel.Fit(train);
        var (loss, brier, acc) = held.Quality(test);
        var (_, lateBrier, lateAcc) = held.Quality(late);
        var baseRate = test.Count > 0 ? test.Count(s => s.RedWon) / (float)test.Count : 0.5f;
        var final = WinModel.Fit([.. train, .. test]);
        var report = new
        {
            date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            physicsVersion = Fighter.PhysicsVersion,
            logicVersion = BoxerBot.LogicVersion,
            pairs,
            seeds,
            seconds,
            bouts,
            draws,
            samples = train.Count + test.Count,
            heldOut = new { logLoss = loss, brier, accuracy = acc, brierOfBaseRate = baseRate * (1f - baseRate), lastQuarterBrier = lateBrier, lastQuarterAccuracy = lateAcc },
            features = WinModel.Names.Append("切片"),
            weights = final.Weights,
        };
        var json = JsonSerializer.Serialize(report, JsonOptions);
        Console.WriteLine(json);
        Console.WriteLine($"C#: {final.CSharp()}");
        if (outPath is not null)
        {
            File.WriteAllText(outPath, json);
        }

        return 0;
    }
}
