using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 学ぶボットのドリル（docs/LEARNING_PLAN.md の B1）: 攻め手は前半に 1 つの種類の拳だけ、後半に別の種類の拳だけを打つ。
/// 受け手（防御ドリルの受け手。自分からは攻めない）が守りの効き目を経験で覚えると、1 発あたりの被ダメージが前半の中で下がり、
/// 後半で拳が変わったら選ぶ守りが変わる（適応し直す）かを、覚えない受け手と同じ種で比べる。
/// </summary>
internal static class LearnLab
{
    private const float StepDt = 1f / 60f;

    /// <summary>試合を分ける区間の数（前半・後半それぞれを 3 つに）。</summary>
    private const int Segments = 6;

    internal sealed record Result(bool Learn, int Seed, int[] Punches, float[] Damage, Dictionary<Defense, int>[] Choices, int[] Failed);

    public static IReadOnlyList<Result> Run(BotStyle defender, PunchKind first, PunchKind second, int seeds, float seconds, int baseSeed, float prior, float damageScale, bool combos)
    {
        var specs = Enumerable.Range(0, seeds).SelectMany(i => new[] { (Learn: false, Seed: baseSeed + i), (Learn: true, Seed: baseSeed + i) }).ToList();
        var results = new Result[specs.Count];
        Parallel.For(0, specs.Count, i => results[i] = Bout(defender, first, second, specs[i].Learn, specs[i].Seed, seconds, prior, damageScale, combos));
        return results;
    }

    private static Result Bout(BotStyle defender, PunchKind first, PunchKind second, bool learn, int seed, float seconds, float prior, float damageScale, bool combos)
    {
        var defenderStyle = defender with { LearnDefense = learn, LearnPrior = prior, LearnDamageScale = damageScale };
        var memory = learn ? new DefenseMemory(defenderStyle.Validated()) : null;
        var defenders = new List<BoxerBot>();
        var clock = new Clock();
        var corners = new BoxingCorners("drill-attacker",
            s => new Switching(new BoxerBot(DefenseDrill.Attacker(first, combos: combos), s), new BoxerBot(DefenseDrill.Attacker(second, combos: combos), s + 7919), clock, seconds / 2f),
            s =>
            {
                var bot = new BoxerBot(defenderStyle, s, memory: memory);
                defenders.Add(bot);
                return bot;
            });
        using var game = new BoxingGame(seed: seed, corners: corners);
        var punches = new int[Segments];
        var damage = new float[Segments];
        var choices = Enumerable.Range(0, Segments).Select(_ => new Dictionary<Defense, int>()).ToArray();
        var before = new Dictionary<Defense, int>();
        var failed = new int[Segments];
        var failedBefore = 0;
        var steps = (int)MathF.Ceiling(seconds / StepDt);
        var segmentSteps = (int)MathF.Ceiling(steps / (float)Segments);
        var attackerThrownBefore = 0;
        for (var i = 0; i < steps; i++)
        {
            var segment = Math.Min(Segments - 1, i / segmentSteps);
            clock.Time = i * StepDt;
            game.Step(default, StepDt);
            foreach (var hit in game.RecentHits)
            {
                if (hit.Attacker == game.Cpu)
                {
                    damage[segment] += hit.Damage;
                }
            }

            if (i == steps - 1 || (i + 1) / segmentSteps != i / segmentSteps)
            {
                var now = Totals(defenders);
                foreach (var (defense, count) in now)
                {
                    choices[segment][defense] = count - before.GetValueOrDefault(defense);
                }

                before = now;
                var failedNow = defenders.Sum(b => b.Stats.Defenses.Values.Sum(c => c.Failed));
                failed[segment] = failedNow - failedBefore;
                failedBefore = failedNow;
                var thrown = clock.Thrown;
                punches[segment] = thrown - attackerThrownBefore;
                attackerThrownBefore = thrown;
            }
        }

        return new Result(learn, seed, punches, damage, choices, failed);
    }

    private static Dictionary<Defense, int> Totals(List<BoxerBot> bots)
    {
        var totals = new Dictionary<Defense, int>();
        foreach (var bot in bots)
        {
            foreach (var (defense, count) in bot.Stats.Defenses)
            {
                totals[defense] = totals.GetValueOrDefault(defense) + count.Attempts;
            }
        }

        return totals;
    }

    /// <summary>試合の時計と、攻め手が打った拳の数（ラウンドが変わって攻め手が作り直されても続けて数える）。</summary>
    private sealed class Clock
    {
        public float Time { get; set; }
        public int Thrown { get; set; }
    }

    /// <summary>前半は <paramref name="early"/>、<paramref name="switchAt"/> 秒からは <paramref name="late"/> が打つ攻め手。</summary>
    private sealed class Switching(BoxerBot early, BoxerBot late, Clock clock, float switchAt) : IFighterControl
    {
        private int _counted;

        private BoxerBot Active => clock.Time < switchAt ? early : late;

        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var command = Active.Read(sense, dt);
            var thrown = early.Stats.Techniques.Values.Sum(c => c.Thrown) + late.Stats.Techniques.Values.Sum(c => c.Thrown);
            clock.Thrown += thrown - _counted;
            _counted = thrown;
            return command;
        }

        public void OnPunchLanded(Hand hand, float damage) => Active.OnPunchLanded(hand, damage);

        public void OnPunchTaken(in HitEvent hit) => Active.OnPunchTaken(hit);
    }

    public static string Report(IReadOnlyList<Result> results, PunchKind first, PunchKind second, int seeds, float seconds, int seed, float prior, float damageScale, bool combos)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"### 学ぶボットのドリル（前半 {first} → 後半 {second}{(combos ? "、連打" : "")}、{seeds} 種 × {seconds:0} 秒、種 {seed}、事前の強さ {prior}、物差し {damageScale}）");
        text.AppendLine();
        text.AppendLine("1 発あたりの被ダメージ（攻め手が打った拳 1 本あたり、全種の合計から）。区間は前半 3 つ・後半 3 つ。");
        text.AppendLine();
        text.AppendLine("| 受け手 | " + string.Join(" | ", Enumerable.Range(1, Segments).Select(s => $"区間 {s}")) + " | 前半の最初→最後 | 後半の最初→最後 |");
        text.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", Segments)) + "---|---|");
        foreach (var learn in new[] { false, true })
        {
            var set = results.Where(r => r.Learn == learn).ToList();
            var per = Enumerable.Range(0, Segments).Select(s => set.Sum(r => r.Damage[s]) / Math.Max(1, set.Sum(r => r.Punches[s]))).ToArray();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {(learn ? "学ぶ" : "学ばない")} | {string.Join(" | ", per.Select(p => p.ToString("0.00", CultureInfo.InvariantCulture)))} | {per[0]:0.00}→{per[2]:0.00} | {per[3]:0.00}→{per[5]:0.00} |");
        }

        text.AppendLine();
        text.AppendLine("守りの最中に打たれた割合（守りを出した数に対して。選び方の良し悪しはこちらに出る）と、（）内は守りを出した数（全種の合計）。");
        text.AppendLine();
        text.AppendLine("| 受け手 | " + string.Join(" | ", Enumerable.Range(1, Segments).Select(s => $"区間 {s}")) + " |");
        text.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", Segments)));
        foreach (var learn in new[] { false, true })
        {
            var set = results.Where(r => r.Learn == learn).ToList();
            var cells = Enumerable.Range(0, Segments).Select(s =>
            {
                var tried = set.Sum(r => r.Choices[s].Values.Sum());
                var share = set.Sum(r => r.Failed[s]) / (float)Math.Max(1, tried);
                return $"{share.ToString("0%", CultureInfo.InvariantCulture)}（{tried}）";
            });
            text.AppendLine(CultureInfo.InvariantCulture, $"| {(learn ? "学ぶ" : "学ばない")} | {string.Join(" | ", cells)} |");
        }

        text.AppendLine();
        text.AppendLine("選んだ守りの割合（全種の合計）。");
        text.AppendLine();
        var defenses = DefenseBook.Techniques;
        text.AppendLine("| 受け手 | 区間 | " + string.Join(" | ", defenses) + " |");
        text.AppendLine("|---|---|" + string.Concat(Enumerable.Repeat("---|", defenses.Count)));
        foreach (var learn in new[] { false, true })
        {
            var set = results.Where(r => r.Learn == learn).ToList();
            for (var s = 0; s < Segments; s++)
            {
                var total = Math.Max(1, set.Sum(r => r.Choices[s].Values.Sum()));
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| {(learn ? "学ぶ" : "学ばない")} | {s + 1} | {string.Join(" | ", defenses.Select(d => (set.Sum(r => r.Choices[s].GetValueOrDefault(d)) / (float)total).ToString("0%", CultureInfo.InvariantCulture)))} |");
            }
        }

        return text.ToString();
    }
}
