using System.Collections.Concurrent;
using System.Text;

namespace MiniRing.Tests;

/// <summary>調査用: 足と重心の状態 × 移動の向きの総当たりで、移動の出だしの引っかかりを表にする（<see cref="FootSnagDrill"/>、docs/arena/foot_snag.md）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class FootSnagProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Table() => Table(FighterTuning.Default, "今の体");

    /// <summary>比べもの: どちらの足から出るかに体重を使わない（<see cref="FootworkTuning.UnloadedFootShare"/> 0 = 直す前の選び方）。</summary>
    [TestMethod]
    public void TableBefore() => Table(FighterTuning.Default with { Footwork = FighterTuning.Default.Footwork with { UnloadedFootShare = 0f, ReplanSwing = false } }, "直す前（体重を見ない・宙の足を運び終える）");

    private void Table(FighterTuning tuning, string label)
    {
        TestContext.WriteLine($"== {label}");
        var cases = FootSnagDrill.Cases().ToArray();
        var only = Environment.GetEnvironmentVariable("FOOTSNAG_ONLY");
        if (!string.IsNullOrEmpty(only))
        {
            cases = [.. cases.Where(c => only.Split(',').Contains(c.Setup.ToString()))];
        }

        var results = new ConcurrentDictionary<int, FootSnagDrill.Result>();
        Parallel.For(0, cases.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
            i => results[i] = FootSnagDrill.Run(cases[i].Setup, cases[i].Name, cases[i].Direction, tuning, variant: cases[i].Variant));
        var text = new StringBuilder();
        text.AppendLine(FootSnagDrill.Header);
        var all = Enumerable.Range(0, cases.Length).GroupBy(i => (cases[i].Setup, cases[i].Name)).Select(g => FootSnagDrill.Worst(g.Select(i => results[i]))).ToArray();
        foreach (var r in all)
        {
            text.AppendLine(FootSnagDrill.Row(r));
        }

        var runs = results.Values.ToArray();
        var snags = all.Count(r => FootSnagDrill.Snagged(r));
        text.AppendLine();
        text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"引っかかった組 {snags} / {all.Length}（各組 {FootSnagDrill.Variants} 種の最悪）、引っかかった試行 {runs.Count(r => FootSnagDrill.Snagged(r))} / {runs.Length}");
        text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"全試行: 出だし 平均 {runs.Average(r => r.Latency):0.000} s・上位 5 % {Quantile(runs.Select(r => r.Latency), 0.95f):0.00} s・最悪 {runs.Max(r => r.Latency):0.00} s、" +
            $"止まり 上位 5 % {Quantile(runs.Select(r => r.LongestStall), 0.95f):0.00} s・最長 {runs.Max(r => r.LongestStall):0.00} s、進み 平均 {runs.Average(r => r.Progress):0.00}・下位 5 % {Quantile(runs.Select(r => r.Progress), 0.05f):0.00}・最低 {runs.Min(r => r.Progress):0.00}");
        TestContext.WriteLine(text.ToString());
    }

    private static float Quantile(IEnumerable<float> values, float q)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted[Math.Clamp((int)MathF.Round(q * (sorted.Length - 1)), 0, sorted.Length - 1)];
    }

    [TestMethod]
    public void Trace()
    {
        // FOOTSNAG_TRACE="Step:F,KickRear:BL"
        foreach (var item in (Environment.GetEnvironmentVariable("FOOTSNAG_TRACE") ?? "Rest:F").Split(','))
        {
            var parts = item.Split(':');
            var setup = Enum.Parse<FootSnagDrill.Setup>(parts[0]);
            var direction = FootSnagDrill.Directions.First(d => d.Name == parts[1]).Direction;
            TestContext.WriteLine($"== {item}");
            var tuning = Environment.GetEnvironmentVariable("FOOTSNAG_OLD") == "1"
                ? FighterTuning.Default with { Footwork = FighterTuning.Default.Footwork with { UnloadedFootShare = 0f, ReplanSwing = false } }
                : FighterTuning.Default;
            var r = FootSnagDrill.Run(setup, parts[1], direction, tuning, trace: TestContext.WriteLine, variant: parts.Length > 2 ? int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 0);
            TestContext.WriteLine(FootSnagDrill.Row(r));
        }
    }
}
