using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 練習試合で値を育てる（(1+λ) の進化）。値 <typeparamref name="T"/> を少しずつ変えた候補を、決まった相手たちと戦わせ、勝ち越しの多い方を残す。
/// <list type="bullet">
/// <item>世代ごとに試合の種を変え、親もその世代の種で戦い直す（同じ種で比べ続けると、その種の運に合わせた値が残る: 打って離れる剣士の 1 回目の
/// 育て方がそうだった）。同じ世代の候補はみな同じ種で比べる（運の違いでなく値の違いを比べる）。</item>
/// <item>最後に、育てるのに使わなかった種（と、渡されれば別の戦い手: 育てた値が他の武器の剣士にも効くか）で、始めの値と比べ直し、
/// 始めの値以上のときだけ採る。</item>
/// </list>
/// </summary>
/// <typeparam name="T">育てる値。</typeparam>
internal sealed class MatchTrainer<T>(
    Func<T, Contender> trainee,
    Func<T, Random, T> mutate,
    Func<T, string> describe)
    where T : class
{
    /// <summary>候補 1 つの成績。<paramref name="Value"/> = 勝ち − 負け + 0.9 tanh((与えた − 受けた) / 400)。</summary>
    internal sealed record Score(float Value, int Wins, int Losses, int Draws, float Dealt, float Taken, string Detail);

    /// <summary>
    /// 育てる。<paramref name="opponents"/> と各 <paramref name="bouts"/> 試合 × <paramref name="seconds"/> 秒、<paramref name="gens"/> 世代 ×
    /// <paramref name="lambda"/> 候補。<paramref name="checkTrainees"/> は、最後の比べ直しを他の戦い手でも行うときの、値から戦い手を作る関数（名前つき）。
    /// </summary>
    public (T Best, string Report) Run(string title, IReadOnlyList<Contender> opponents, T start, int gens, int lambda, int bouts, float seconds, int seed, TextWriter log,
        IReadOnlyList<(string Name, Func<T, Contender> Make)>? checkTrainees = null)
    {
        var random = new Random(seed);
        var report = new StringBuilder();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"# {title}（相手: {string.Join("・", opponents.Select(o => o.Name))}、各相手 {bouts} 試合 × {seconds:0} 秒、{gens} 世代 × {lambda} 候補、世代ごとに種を変える）"));
        report.AppendLine();
        report.AppendLine("| 世代 | 値 | 点（その世代の種） | 勝ち | 負け | 分け | 与えた | 受けた | 相手ごと（勝ち-負け） |");
        report.AppendLine("|---:|---|---:|---:|---:|---:|---:|---:|---|");
        var parent = start;
        for (var g = 1; g <= gens; g++)
        {
            var genSeed = seed + (1000 * g);
            var parentScore = Evaluate(trainee, parent, opponents, bouts, seconds, genSeed);
            var children = Enumerable.Range(0, lambda).Select(_ => mutate(parent, random)).ToList();
            var scored = children.Select(c => (Child: c, Score: Evaluate(trainee, c, opponents, bouts, seconds, genSeed))).ToList();
            log.WriteLine($"gen {g}: parent {parentScore.Value:0.00} {describe(parent)}");
            foreach (var (child, score) in scored)
            {
                log.WriteLine($"  {score.Value:0.00} {describe(child)}");
            }

            var best = scored.MaxBy(x => x.Score.Value);
            // 親より良いときだけ替える（同じなら親のまま: 値の違いが成績に出ていない）。
            if (best.Score.Value > parentScore.Value)
            {
                parent = best.Child;
                report.AppendLine(Row(g, "替えた", parent, best.Score));
            }
            else
            {
                report.AppendLine(Row(g, "親のまま", parent, parentScore));
            }
        }

        var heldOut = seed + 7777;
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"育てるのに使わなかった種（{heldOut}、各相手 {bouts * 2} 試合）で、始めの値と比べ直す:"));
        report.AppendLine();
        report.AppendLine("| 戦い手 | 値 | 点 | 勝ち | 負け | 分け | 与えた | 受けた | 相手ごと |");
        report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---|");
        var checks = new List<(string Name, Func<T, Contender> Make)> { ("育てた戦い手", trainee) };
        checks.AddRange(checkTrainees ?? []);
        float gain = 0f;
        foreach (var (name, make) in checks)
        {
            var before = Evaluate(make, start, opponents, bouts * 2, seconds, heldOut);
            var after = Evaluate(make, parent, opponents, bouts * 2, seconds, heldOut);
            report.AppendLine(Line(name, "始め", start, before));
            report.AppendLine(Line(name, "育てた", parent, after));
            gain += after.Value - before.Value;
            log.WriteLine($"check {name}: start {before.Value:0.00}, trained {after.Value:0.00}");
        }

        // 比べ直しの合計で始めの値以上のときだけ採る（種や戦い手に合わせただけの値を採らない）。
        var adopted = gain >= 0f ? parent : start;
        report.AppendLine();
        report.AppendLine(ReferenceEquals(parent, start)
            ? "どの世代も親を超える候補が無く、始めの値のまま。"
            : ReferenceEquals(adopted, start)
            ? string.Create(CultureInfo.InvariantCulture, $"比べ直しの合計で始めの値より悪い（{gain:0.00}）ので、育てた値は採らず、始めの値のままにする。")
            : string.Create(CultureInfo.InvariantCulture, $"比べ直しの合計で始めの値以上（{gain:+0.00;-0.00;0}）なので、育てた値を採る。"));
        return (adopted, report.ToString());
    }

    /// <summary>値 <paramref name="value"/> の戦い手を、相手たちと各 <paramref name="bouts"/> 試合（コーナーを半分ずつ入れ替え）戦わせた成績。</summary>
    public static Score Evaluate(Func<T, Contender> make, T value, IReadOnlyList<Contender> opponents, int bouts, float seconds, int seed)
    {
        var baseMine = make(value);
        var mine = baseMine with { Name = baseMine.Name + "（育てる）" };
        var specs = opponents.SelectMany((o, i) => BotArena.Pair(mine, o, bouts, seconds, seed + (101 * i))).ToList();
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

    private string Row(int gen, string what, T value, Score s) => string.Create(CultureInfo.InvariantCulture,
        $"| {gen}（{what}） | {describe(value)} | {s.Value:0.00} | {s.Wins} | {s.Losses} | {s.Draws} | {s.Dealt:0} | {s.Taken:0} | {s.Detail} |");

    private string Line(string name, string label, T value, Score s) => string.Create(CultureInfo.InvariantCulture,
        $"| {name} | {label}: {describe(value)} | {s.Value:0.00} | {s.Wins} | {s.Losses} | {s.Draws} | {s.Dealt:0} | {s.Taken:0} | {s.Detail} |");
}
