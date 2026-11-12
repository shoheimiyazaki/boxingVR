using System.Globalization;
using System.Text;
using System.Text.Json;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 打って離れる剣士（<see cref="HitAndAway"/>）を、練習試合で育てる: 間合いの取り合いの値（余裕・相手の踏み込みの読み・隙と見る時間・待つ時間・
/// 回る強さ・出端を打つか）を少しずつ変えた候補を、決まった相手たち（今までの剣士・バット・棍棒・拳）と戦わせ、勝ち越しの多い方を残す（(1+λ) の進化）。
/// 候補はどれも同じ種の試合で比べる（運の違いでなく値の違いを比べる）。最後に、育てた値と始めの値を、育てるのに使わなかった種で比べ直す。
/// </summary>
internal static class AwayLab
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>候補 1 つの成績。</summary>
    internal sealed record Score(float Value, int Wins, int Losses, int Draws, float Dealt, float Taken, string Detail);

    public static (HitAndAway Best, string Report) Run(Func<HitAndAway?, IReadOnlyList<Contender>> contenders, string trainee, IReadOnlyList<string> opponents,
        HitAndAway start, int gens, int lambda, int bouts, float seconds, int seed, TextWriter log)
    {
        var random = new Random(seed);
        var report = new StringBuilder();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"# 打って離れる剣士を育てる（{trainee}、相手: {string.Join("・", opponents)}、各相手 {bouts} 試合 × {seconds:0} 秒、{gens} 世代 × {lambda} 候補）"));
        report.AppendLine();
        report.AppendLine("| 世代 | 値 | 点 | 勝ち | 負け | 分け | 与えた | 受けた | 相手ごと（勝ち-負け） |");
        report.AppendLine("|---:|---|---:|---:|---:|---:|---:|---:|---|");
        var parent = start;
        var parentScore = Evaluate(contenders, trainee, opponents, parent, bouts, seconds, seed);
        report.AppendLine(Row(0, parent, parentScore));
        log.WriteLine($"gen 0: {parentScore.Value:0.00} {Describe(parent)}");
        for (var g = 1; g <= gens; g++)
        {
            var children = Enumerable.Range(0, lambda).Select(_ => Mutate(parent, random)).ToList();
            var scored = children.Select(c => (Child: c, Score: Evaluate(contenders, trainee, opponents, c, bouts, seconds, seed))).ToList();
            var best = scored.MaxBy(x => x.Score.Value);
            foreach (var (child, score) in scored)
            {
                log.WriteLine($"  gen {g}: {score.Value:0.00} {Describe(child)}");
            }

            if (best.Score.Value >= parentScore.Value)
            {
                parent = best.Child;
                parentScore = best.Score;
            }

            report.AppendLine(Row(g, parent, parentScore));
            log.WriteLine($"gen {g}: best {parentScore.Value:0.00} {Describe(parent)}");
        }

        // 育てるのに使わなかった種で、始めの値と比べ直す（種に合わせただけの値を採らない）。
        var heldOut = seed + 7777;
        var before = Evaluate(contenders, trainee, opponents, start, bouts * 2, seconds, heldOut);
        var after = Evaluate(contenders, trainee, opponents, parent, bouts * 2, seconds, heldOut);
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"別の種（{heldOut}、各相手 {bouts * 2} 試合）で比べ直す:"));
        report.AppendLine();
        report.AppendLine("| 値 | 点 | 勝ち | 負け | 分け | 与えた | 受けた | 相手ごと |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
        report.AppendLine(Line("始め", start, before));
        report.AppendLine(Line("育てた", parent, after));
        // 育てるのに使わなかった種で始めの値より悪ければ、種に合わせただけの値なので採らない。
        var adopted = after.Value >= before.Value ? parent : start;
        report.AppendLine();
        report.AppendLine(adopted == parent ? "別の種でも始めの値以上なので、育てた値を採る。" : "別の種では始めの値より悪いので、育てた値は採らず、始めの値のままにする。");
        return (adopted, report.ToString());
    }

    public static HitAndAway Load(string path) => JsonSerializer.Deserialize<HitAndAway>(File.ReadAllText(path))?.Validated()
        ?? throw new InvalidDataException($"{path} を読めません。");

    public static void Save(string path, HitAndAway value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));

    private static Score Evaluate(Func<HitAndAway?, IReadOnlyList<Contender>> contenders, string trainee, IReadOnlyList<string> opponents, HitAndAway value,
        int bouts, float seconds, int seed)
    {
        var mine = contenders(value).Single(c => c.Name == trainee) with { Name = trainee + "（育てる）" };
        var others = contenders(null);
        var specs = opponents.SelectMany((name, i) => BotArena.Pair(mine, others.Single(c => c.Name == name), bouts, seconds, seed + (101 * i))).ToList();
        var results = BotArena.RunAll(specs, VersusLab.Tuning);
        int wins = 0, losses = 0, draws = 0;
        float dealt = 0f, taken = 0f;
        var detail = new StringBuilder();
        foreach (var group in results.GroupBy(r => r.Spec.Red.Name == mine.Name ? r.Spec.Blue.Name : r.Spec.Red.Name))
        {
            int w = 0, l = 0;
            foreach (var r in group)
            {
                var red = r.Spec.Red.Name == mine.Name;
                var sign = red ? r.Winner : -r.Winner;
                w += sign > 0 ? 1 : 0;
                l += sign < 0 ? 1 : 0;
                draws += sign == 0 ? 1 : 0;
                dealt += red ? r.Red.DamageDealt : r.Blue.DamageDealt;
                taken += red ? r.Blue.DamageDealt : r.Red.DamageDealt;
            }

            wins += w;
            losses += l;
            detail.Append(CultureInfo.InvariantCulture, $"{group.Key} {w}-{l} ");
        }

        // 勝ち越しが主、ダメージの差は同じ勝ち越しの間の順を決めるだけ（1 試合ぶんに届かない重さ）。
        var value01 = wins - losses + (0.9f * MathF.Tanh((dealt - taken) / 400f));
        return new Score(value01, wins, losses, draws, dealt, taken, detail.ToString().Trim());
    }

    private static HitAndAway Mutate(HitAndAway p, Random random)
    {
        float Gauss() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));
        return new HitAndAway(
            Math.Clamp(p.Margin + (0.04f * Gauss()), -0.15f, 0.4f),
            Math.Clamp(p.Lunge + (0.15f * Gauss()), 0f, 1.5f),
            Math.Clamp(p.OpeningTime + (0.12f * Gauss()), 0f, 1.5f),
            Math.Clamp(p.Patience * MathF.Exp(0.35f * Gauss()), 0.2f, 6f),
            Math.Clamp(p.Circle + (0.12f * Gauss()), 0f, 1f),
            random.NextDouble() < 0.15 ? !p.Debana : p.Debana).Validated();
    }

    private static string Describe(HitAndAway h) => string.Create(CultureInfo.InvariantCulture,
        $"余裕 {h.Margin:0.00} m・踏み込みの読み {h.Lunge:0.00}・隙 {h.OpeningTime:0.00} s・待ち {h.Patience:0.00} s・回る {h.Circle:0.00}・出端 {(h.Debana ? "打つ" : "打たない")}");

    private static string Row(int gen, HitAndAway h, Score s) => string.Create(CultureInfo.InvariantCulture,
        $"| {gen} | {Describe(h)} | {s.Value:0.00} | {s.Wins} | {s.Losses} | {s.Draws} | {s.Dealt:0} | {s.Taken:0} | {s.Detail} |");

    private static string Line(string label, HitAndAway h, Score s) => string.Create(CultureInfo.InvariantCulture,
        $"| {label}: {Describe(h)} | {s.Value:0.00} | {s.Wins} | {s.Losses} | {s.Draws} | {s.Dealt:0} | {s.Taken:0} | {s.Detail} |");
}
