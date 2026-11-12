using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 間合いの層の測定（docs/LEARNING_PLAN.md の R）: 間合いの層の使い方（構えを上げる・頭を動かす・当てずっぽうの回避・外へ出る）の変種ごとに、
/// (1) 全員がその変種で総当たりしたときのスタイルごとの被ダメージ・危ない所にいた時間、(2) 変種を付けたボットと付けないボットを
/// 同じスタイル同士・他のスタイルと戦わせた勝ち点（付けた側から見て）を比べる。
/// </summary>
internal static class RangeLab
{
    /// <summary>変種の名前 → スタイルに付ける間合いの層の値。</summary>
    public static Func<BotStyle, BotStyle> Variant(string name) => name switch
    {
        "off" => s => s with { RangeGuard = 0f, RangeHeadMove = 0f, RangeGuess = 0f, RangeExit = 0f, RangeKeepOut = 0f, RangePress = 0f },
        "guard" => s => s with { RangeGuard = 0.6f },
        "head" => s => s with { RangeHeadMove = 0.6f },
        "guess" => s => s with { RangeGuess = 1.5f },
        "exit" => s => s with { RangeExit = 0.5f },
        "all" => s => s with { RangeGuard = 0.6f, RangeHeadMove = 0.6f, RangeGuess = 1.5f, RangeExit = 0.5f },
        "keep" => s => s with { RangeKeepOut = 1f, RangeExit = 1f },
        "keep-guard" => s => s with { RangeKeepOut = 1f, RangeExit = 1f, RangeGuard = 0.6f },
        "keep-all" => s => s with { RangeKeepOut = 1f, RangeExit = 1f, RangeGuard = 0.6f, RangeHeadMove = 0.6f, RangeGuess = 1.5f },
        "press" => s => s with { RangePress = 1f },
        "press2" => s => s with { RangePress = 2f },
        "press-guard" => s => s with { RangePress = 1f, RangeGuard = 0.6f },
        "catalog" => s => s, // スタイルごとに決めた値のまま（R4）
        _ => throw new ArgumentException($"変種 '{name}' はありません（off, guard, head, guess, exit, all, keep, keep-guard, keep-all, press, press2, press-guard, catalog）。", nameof(name)),
    };

    public static string Run(IReadOnlyList<string> variants, IReadOnlyList<string> bots, int bouts, float seconds, int seed)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"### 間合いの層（各組 {bouts} 試合 × {seconds:0} 秒、種 {seed}）");
        text.AppendLine();
        text.AppendLine("(1) 全員が同じ変種で総当たり（スタイルごと、試合あたり）。");
        text.AppendLine();
        text.AppendLine("| 変種 | スタイル | 勝ち点 | 与えた | 受けた | 危ない所にいた時間 [s] | 当てずっぽうの回避 | 守り（打たれた） |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        var summary = new List<string>();
        foreach (var variant in variants)
        {
            var make = Variant(variant);
            var contenders = bots.Select(b => Contender.Of(make(BotCatalog.Style(b)))).ToList();
            var results = BotArena.RunAll(BotArena.RoundRobin(contenders, bouts, seconds, seed));
            // 試合の動き（面白さの物差し、試合あたり・両者の合計）: 打った数・当たった数・ダメージ・ダウン。
            var count = Math.Max(1, results.Count);
            var thrown = results.Sum(r => r.Red.Stats.Techniques.Values.Sum(c => c.Thrown) + r.Blue.Stats.Techniques.Values.Sum(c => c.Thrown)) / (float)count;
            var landed = results.Sum(r => r.Red.Landed + r.Blue.Landed) / (float)count;
            var damage = results.Sum(r => r.Red.DamageDealt + r.Blue.DamageDealt) / count;
            var downs = results.Sum(r => r.Red.Knockdowns + r.Blue.Knockdowns) / (float)count;
            var idle = results.Sum(r => r.Red.Stats.IdleDangerTime + r.Blue.Stats.IdleDangerTime) / count;
            summary.Add(string.Create(CultureInfo.InvariantCulture, $"| {variant} | {thrown:0.0} | {landed:0.0} | {damage:0} | {downs:0.00} | {idle:0.0} |"));
            foreach (var bot in bots)
            {
                var mine = results.Where(r => r.Spec.Red.Name == bot || r.Spec.Blue.Name == bot).ToList();
                var n = Math.Max(1, mine.Count);
                var points = mine.Sum(r => r.Winner == 0 ? 0.5f : (r.Winner == 1) == (r.Spec.Red.Name == bot) ? 1f : 0f) / n;
                var dealt = mine.Sum(r => r.Of(bot).DamageDealt) / n;
                var taken = mine.Sum(r => (r.Spec.Red.Name == bot ? r.Blue : r.Red).DamageDealt) / n;
                var stats = new BotStats();
                foreach (var r in mine)
                {
                    stats.Merge(r.Of(bot).Stats);
                }

                var defended = stats.Defenses.Values;
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| {variant} | {bot} | {points:0.00} | {dealt:0} | {taken:0} | {stats.DangerTime / n:0.0} | {stats.Guesses / (float)n:0.0} | {defended.Sum(c => c.Attempts) / (float)n:0.0}（{defended.Sum(c => c.Failed) / (float)n:0.0}） |");
            }
        }

        text.AppendLine();
        text.AppendLine("(1b) 試合の動き（試合あたり・両者の合計）。打っていない間に危ない所にいた時間 [s] も。");
        text.AppendLine();
        text.AppendLine("| 変種 | 打った数 | 当たった数 | ダメージ | ダウン | 打たずに危ない所にいた時間 |");
        text.AppendLine("|---|---|---|---|---|---|");
        foreach (var line in summary)
        {
            text.AppendLine(line);
        }

        text.AppendLine();
        text.AppendLine("(2) 変種を付けたボット（+）と付けないボット（−）の対戦（付けた側の勝ち点・与えた・受けた、試合あたり）。");
        text.AppendLine();
        text.AppendLine("| 変種 | スタイル（+） | 勝ち点 | 与えた | 受けた |");
        text.AppendLine("|---|---|---|---|---|");
        foreach (var variant in variants.Where(v => v != "off"))
        {
            var make = Variant(variant);
            var specs = new List<BoutSpec>();
            for (var a = 0; a < bots.Count; a++)
            {
                for (var b = 0; b < bots.Count; b++)
                {
                    var plus = Contender.Of(make(BotCatalog.Style(bots[a])) with { Name = bots[a] + "+" });
                    var minus = Contender.Of(Variant("off")(BotCatalog.Style(bots[b])) with { Name = bots[b] + "-" });
                    specs.AddRange(BotArena.Pair(plus, minus, bouts, seconds, seed + (1000 * a) + (37 * b)));
                }
            }

            var results = BotArena.RunAll(specs);
            foreach (var bot in bots)
            {
                var name = bot + "+";
                var mine = results.Where(r => r.Spec.Red.Name == name || r.Spec.Blue.Name == name).ToList();
                var n = Math.Max(1, mine.Count);
                var points = mine.Sum(r => r.Winner == 0 ? 0.5f : (r.Winner == 1) == (r.Spec.Red.Name == name) ? 1f : 0f) / n;
                var dealt = mine.Sum(r => r.Of(name).DamageDealt) / n;
                var taken = mine.Sum(r => (r.Spec.Red.Name == name ? r.Blue : r.Red).DamageDealt) / n;
                text.AppendLine(CultureInfo.InvariantCulture, $"| {variant} | {bot} | {points:0.00} | {dealt:0} | {taken:0} |");
            }

            var all = results.Count;
            var total = results.Sum(r => r.Winner == 0 ? 0.5f : (r.Winner == 1) == r.Spec.Red.Name.EndsWith('+') ? 1f : 0f) / Math.Max(1, all);
            text.AppendLine(CultureInfo.InvariantCulture, $"| {variant} | **全体** | **{total:0.00}** | | |");
        }

        return text.ToString();
    }
}
