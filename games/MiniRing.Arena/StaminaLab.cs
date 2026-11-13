using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// スタミナの効き方の総当たり（<c>docs/LEARNING_PLAN.md</c> の A0）: 同じ組み合わせ・同じ種で、調整値の組（変種）だけを変えて試合を回し、
/// 試合を <see cref="Segments"/> つに分けた区間ごとの打った数・当たった数・ダメージ・スタミナと、ダウン・勝ち点を比べる。
/// スタミナが試合の後半を変えるか、スタイルによって変え方が違うか（打ち続けるスタイルほど落ちるか）を見る。
/// </summary>
internal static class StaminaLab
{
    /// <summary>試合を分ける区間の数（序盤・中盤・終盤）。</summary>
    public const int Segments = 3;

    private const float StepDt = 1f / 60f;

    /// <summary>
    /// 変種の名前 → 調整値と、ボットがペース配分するか（A1）。<c>off</c> は腕の力が落ちない（スタミナが無いのと同じ動き）。
    /// <c>off</c>・<c>on</c>・<c>hard</c> は配分しない（A0 の比較）。<c>pace</c>・<c>hard-pace</c> は <see cref="PaceDrop"/> で配分する（A2）。
    /// </summary>
    public static (FighterTuning Tuning, bool Pacing) Variant(string name) => name switch
    {
        "off" => (FighterTuning.Default with { TiredStrength = 1f, TiredHandSlowness = 0f }, false),
        "on" => (FighterTuning.Default, false),
        "hard" => (Hard, false),
        "pace" => (FighterTuning.Default, true),
        "hard-pace" => (Hard, true),
        _ => throw new ArgumentException($"変種 '{name}' はありません（off, on, hard, pace, hard-pace）。", nameof(name)),
    };

    private static FighterTuning Hard => FighterTuning.Default with { StaminaCapacity = 60f, StaminaRecoveryPower = 1.8f };

    /// <summary>配分する変種の手数の落とし方（A2 で測った値。スタイルの既定は 0 = 配分しない）。</summary>
    private const float PaceDrop = 0.6f;

    private static BoxerBot Create(string name, int seed, bool pacing)
        => new(BotCatalog.Style(name) with { PaceDrop = pacing ? PaceDrop : 0f }, seed);

    /// <summary>片方のコーナーの、区間ごとの数。</summary>
    internal sealed class CornerTally
    {
        public int[] Thrown { get; } = new int[Segments];
        public int[] Landed { get; } = new int[Segments];
        public float[] Damage { get; } = new float[Segments];
        public float[] StaminaSum { get; } = new float[Segments];
        public int[] StaminaSamples { get; } = new int[Segments];
        public float MinStamina { get; set; } = 1f;
        public int Knockdowns { get; set; }
        public float? FirstKnockdown { get; set; }
        public int RoundsWon { get; set; }
        public float DamageTotal => Damage.Sum();

        public float MeanStamina(int segment) => StaminaSamples[segment] > 0 ? StaminaSum[segment] / StaminaSamples[segment] : 1f;
    }

    internal sealed record Bout(string Variant, string Red, string Blue, int Seed, CornerTally RedTally, CornerTally BlueTally)
    {
        /// <summary>+1 = 赤、−1 = 青、0 = 引き分け（<see cref="BoutResult.Winner"/> と同じ決め方）。</summary>
        public int Winner
        {
            get
            {
                if (RedTally.RoundsWon != BlueTally.RoundsWon)
                {
                    return RedTally.RoundsWon > BlueTally.RoundsWon ? 1 : -1;
                }

                var margin = 0.05f * (RedTally.DamageTotal + BlueTally.DamageTotal) / 2f;
                var diff = RedTally.DamageTotal - BlueTally.DamageTotal;
                return MathF.Abs(diff) <= MathF.Max(1f, margin) ? 0 : MathF.Sign(diff);
            }
        }
    }

    public static IReadOnlyList<Bout> Run(IReadOnlyList<string> variants, IReadOnlyList<string> bots, int boutsPerPair, float seconds, int baseSeed, MatchRules? rules = null)
    {
        var specs = new List<(string Variant, string Red, string Blue, int Seed)>();
        foreach (var variant in variants)
        {
            _ = Variant(variant); // 打ち間違いを先に知らせる
            for (var a = 0; a < bots.Count; a++)
            {
                for (var b = a + 1; b < bots.Count; b++)
                {
                    for (var i = 0; i < boutsPerPair; i++)
                    {
                        var seed = baseSeed + (1000 * a) + (37 * b) + (i / 2);
                        specs.Add(i % 2 == 0 ? (variant, bots[a], bots[b], seed) : (variant, bots[b], bots[a], seed));
                    }
                }
            }
        }

        var results = new Bout[specs.Count];
        Parallel.For(0, specs.Count, i =>
        {
            var s = specs[i];
            results[i] = RunBout(s.Variant, s.Red, s.Blue, s.Seed, seconds, rules);
        });
        return results;
    }

    private static Bout RunBout(string variant, string redName, string blueName, int seed, float seconds, MatchRules? rules)
    {
        var redBots = new List<IFighterControl>();
        var blueBots = new List<IFighterControl>();
        var (tuning, pacing) = Variant(variant);
        var corners = new BoxingCorners(blueName,
            s => Track(blueBots, Create(blueName, s, pacing)),
            s => Track(redBots, Create(redName, s, pacing)));
        using var game = new BoxingGame(tuning, seed, corners, rules);
        var red = new CornerTally();
        var blue = new CornerTally();
        var redState = FighterState.Standing;
        var blueState = FighterState.Standing;
        var round = game.Round;
        var steps = (int)MathF.Ceiling(seconds / StepDt);
        var segmentSteps = (int)MathF.Ceiling(steps / (float)Segments);
        var thrownBefore = (Red: 0, Blue: 0);
        for (var i = 0; i < steps; i++)
        {
            var segment = Math.Min(Segments - 1, i / segmentSteps);
            game.Step(default, StepDt);
            foreach (var hit in game.RecentHits) // 拳のボットだけを比べる（武器の打撃は数えない）
            {
                var tally = hit.Attacker == game.Player ? red : blue;
                tally.Landed[segment]++;
                tally.Damage[segment] += hit.Damage;
            }

            Sample(red, game.Player, segment);
            Sample(blue, game.Cpu, segment);
            if (game.Round != round)
            {
                round = game.Round;
                redState = blueState = FighterState.Standing;
            }

            var time = (i + 1) * StepDt;
            if (redState == FighterState.Standing && game.Player.State != FighterState.Standing)
            {
                blue.Knockdowns++;
                blue.FirstKnockdown ??= time;
            }

            if (blueState == FighterState.Standing && game.Cpu.State != FighterState.Standing)
            {
                red.Knockdowns++;
                red.FirstKnockdown ??= time;
            }

            redState = game.Player.State;
            blueState = game.Cpu.State;
            var lastOfSegment = i == steps - 1 || (i + 1) / segmentSteps != i / segmentSteps;
            if (lastOfSegment)
            {
                var thrown = (Red: Thrown(redBots), Blue: Thrown(blueBots));
                red.Thrown[segment] += thrown.Red - thrownBefore.Red;
                blue.Thrown[segment] += thrown.Blue - thrownBefore.Blue;
                thrownBefore = thrown;
            }
        }

        red.RoundsWon = game.PlayerWins;
        blue.RoundsWon = game.CpuWins;
        return new Bout(variant, redName, blueName, seed, red, blue);
    }

    private static void Sample(CornerTally tally, Fighter fighter, int segment)
    {
        tally.StaminaSum[segment] += fighter.Stamina;
        tally.StaminaSamples[segment]++;
        tally.MinStamina = MathF.Min(tally.MinStamina, fighter.Stamina);
    }

    private static int Thrown(List<IFighterControl> bots)
        => bots.OfType<BoxerBot>().Sum(b => b.Stats.Techniques.Values.Sum(c => c.Thrown));

    private static IFighterControl Track(List<IFighterControl> bots, IFighterControl bot)
    {
        bots.Add(bot);
        return bot;
    }

    /// <summary>変種ごと・スタイルごとの表（Markdown）。</summary>
    public static string Report(IReadOnlyList<Bout> bouts, IReadOnlyList<string> variants, IReadOnlyList<string> bots, int boutsPerPair, float seconds, int seed)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"### スタミナの総当たり（各組 {boutsPerPair} 試合 × {seconds:0} 秒、種 {seed}）");
        text.AppendLine();
        text.AppendLine("全体（両コーナーの合計、試合あたり）。区間は 序盤 / 中盤 / 終盤。");
        text.AppendLine();
        text.AppendLine("| 変種 | 打った数 | 当たった数 | ダメージ | 平均スタミナ | 最低スタミナ（平均） | ダウン | 最初のダウン [s]（平均） |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var variant in variants)
        {
            var set = bouts.Where(b => b.Variant == variant).ToList();
            var corners = set.SelectMany(b => new[] { b.RedTally, b.BlueTally }).ToList();
            var n = Math.Max(1, set.Count);
            string Seg(Func<CornerTally, int, float> f, string format) => string.Join(" / ", Enumerable.Range(0, Segments).Select(s => (corners.Sum(c => f(c, s)) / n).ToString(format, CultureInfo.InvariantCulture)));
            string SegMean(Func<CornerTally, int, float> f) => string.Join(" / ", Enumerable.Range(0, Segments).Select(s => corners.Average(c => f(c, s)).ToString("0.00", CultureInfo.InvariantCulture)));
            var firsts = set.Select(b => Min(b.RedTally.FirstKnockdown, b.BlueTally.FirstKnockdown)).Where(t => t is not null).Select(t => t!.Value).ToList();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {variant} | {Seg((c, s) => c.Thrown[s], "0.0")} | {Seg((c, s) => c.Landed[s], "0.0")} | {Seg((c, s) => c.Damage[s], "0")} | {SegMean((c, s) => c.MeanStamina(s))} | {corners.Average(c => c.MinStamina):0.00} | {corners.Sum(c => c.Knockdowns) / (float)n:0.00} | {(firsts.Count > 0 ? firsts.Average().ToString("0.0", CultureInfo.InvariantCulture) : "—")}（{firsts.Count}/{set.Count} 試合） |");
        }

        text.AppendLine();
        text.AppendLine("スタイルごと（そのスタイルのコーナー、試合あたり）。勝ち点 = 勝ち 1・引き分け 0.5。");
        text.AppendLine();
        text.AppendLine("| スタイル | 変種 | 勝ち点 | 打った数 | 当たった数 | 与えたダメージ | 平均スタミナ | 終盤/序盤の当たり |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var bot in bots)
        {
            foreach (var variant in variants)
            {
                var mine = bouts.Where(b => b.Variant == variant && (b.Red == bot || b.Blue == bot))
                    .Select(b => (Tally: b.Red == bot ? b.RedTally : b.BlueTally, Points: b.Winner == 0 ? 0.5f : (b.Winner == 1) == (b.Red == bot) ? 1f : 0f)).ToList();
                var n = Math.Max(1, mine.Count);
                string Seg(Func<CornerTally, int, float> f, string format) => string.Join(" / ", Enumerable.Range(0, Segments).Select(s => (mine.Sum(m => f(m.Tally, s)) / n).ToString(format, CultureInfo.InvariantCulture)));
                var early = mine.Sum(m => m.Tally.Landed[0]);
                var late = mine.Sum(m => m.Tally.Landed[Segments - 1]);
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| {bot} | {variant} | {mine.Sum(m => m.Points) / n:0.00} | {Seg((c, s) => c.Thrown[s], "0.0")} | {Seg((c, s) => c.Landed[s], "0.0")} | {mine.Sum(m => m.Tally.DamageTotal) / n:0} | {Seg((c, s) => c.MeanStamina(s), "0.00")} | {(early > 0 ? late / (float)early : 0f):0.00} |");
            }
        }

        return text.ToString();
    }

    private static float? Min(float? a, float? b) => a is null ? b : b is null ? a : MathF.Min(a.Value, b.Value);
}
