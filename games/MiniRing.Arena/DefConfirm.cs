using System.Globalization;
using System.Text;
using System.Text.Json;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// <c>defconfirm</c>（作業 18）: <c>drilltune</c> が出した守りの本を、<c>cmatune</c> の最後の比べ直しと同じ手続き
/// （使わなかった種・固定の n・<see cref="DefenseTuner.SeedPoints"/>・<see cref="PairedEvaluation.Confirm"/>）で champion の値と比べる。
/// <c>--export-champion f.json</c> は champion の守りの本を JSON に書く（<c>drilltune --book</c> の出発点を cmatune と同じ champion にするため）。
/// </summary>
internal static class DefConfirm
{
    public static int Run(Options o)
    {
        var champion = BotCatalog.Champion;
        if (o.Get("export-champion") is { } export)
        {
            File.WriteAllText(export, JsonSerializer.Serialize(champion.Defenses, CmaTune.Json));
            return 0;
        }

        var defense = Enum.Parse<Defense>(o.Get("defense") ?? throw new ArgumentException("--defense が要ります。"), ignoreCase: true);
        var bookPath = o.Get("book") ?? throw new ArgumentException("--book が要ります。");
        var tuned = (JsonSerializer.Deserialize<DefenseBook>(File.ReadAllText(bookPath)) ?? throw new JsonException("空の本です。")).Validated();
        var start = champion.Defenses.With(defense, champion.Defenses.Of(defense).Validated());
        var seed = o.Int("seed", 1);
        var seconds = o.Float("seconds", 40f, min: 1f);
        var n = o.Int("confirm-n", 96, min: 2);
        var kinds = DefenseTuner.Targets(defense);
        var tuner = new DefenseTuner(1, seconds, 0, _ => { });
        double[] Points(DefenseBook book, IReadOnlyList<int> seeds)
        {
            var points = new double[seeds.Count];
            Parallel.For(0, points.Length, i => points[i] = DefenseTuner.SeedPoints(tuner.EvaluateBySeed(book, defense, kinds, seeds[i]))[0]);
            return points;
        }

        // cmatune と同じ種の置き場（ConfirmBlock = 500000）。
        var seeds = Enumerable.Range(0, n).Select(k => seed + (1000 * (500_000 + k))).ToArray();
        var result = PairedEvaluation.Confirm(s => Points(tuned, s), s => Points(start, s), seeds);
        var verdict = result.CiLow > 0d ? "改善" : result.CiHigh < 0d ? "悪化" : "改善とは言えない";
        var sb = new StringBuilder();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"defconfirm {defense} 種 {seed} 本 `{bookPath}` {seconds:0} 秒 n={n}: {result} → {verdict}"));
        sb.AppendLine("始めの値: " + DefenseTuner.Describe(start.Of(defense)));
        sb.AppendLine("磨いた値: " + DefenseTuner.Describe(tuned.Of(defense)));
        Console.WriteLine(sb);
        return 0;
    }
}
