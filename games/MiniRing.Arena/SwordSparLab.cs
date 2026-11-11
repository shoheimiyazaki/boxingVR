using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 剣同士の稽古で技を磨く: ドリルで磨いた技（<see cref="SwordLab"/>）から始め、世代ごとに、今の流儀（王者）から 1 か所だけ変えた挑戦者を
/// λ 人作る（1 つの技の形と間合いを少し動かす、または 1 つの技の選ばれやすさを変える）。挑戦者は王者と剣で <c>bouts</c> 試合
/// （コーナーを半分ずつ入れ替え）稽古する。いちばん勝ち越した挑戦者は、まだ使っていない種でもう <c>bouts</c> 試合、王者と稽古し直し、
/// そこでも勝ち越したときだけ新しい王者になる（少ない試合で選ぶと、運よく勝った挑戦者を選んでしまう: 勝者の呪い）。
/// 勝ち越し = 失わせたボクセルの差 / 100 + （殺した − 殺された）× 3（<see cref="SparResult.RedMargin"/>）の平均。
/// 相手が止まった的ではなく、同じ技で斬り返してくる剣士なので、間合い・速さ・狙いの取り合いの中で効く形が残る。
/// </summary>
internal sealed class SwordSparLab(int generations, int lambda, int bouts, float seconds, int seed)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    /// <summary>勝ち越しがこれを超えた挑戦者だけ王者と入れ替える（稽古の運のぶれで入れ替えない）。</summary>
    private const float Threshold = 0.05f;

    /// <summary>稽古を回し、Markdown の報告を返す。磨いた流儀は <paramref name="outPath"/> へ JSON で書く。</summary>
    public string Run(string startPath, string outPath, TextWriter log, int finalBouts, float finalSeconds)
    {
        var watch = Stopwatch.StartNew();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        var start = SwordStyle.Even(SwordLab.Load(startPath));
        var champion = start;
        var random = new Random(seed);
        var report = new StringBuilder();
        report.AppendLine("# 剣同士の稽古で技を磨く（sparlab）");
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"出発点はドリルで磨いた技（{startPath}、どの技も同じ選ばれやすさ）。世代ごとに 1 か所だけ変えた挑戦者 {lambda} 人が、王者と剣で {bouts} 試合 × {seconds:0} 秒ずつ稽古（コーナーを半分ずつ入れ替え）。勝ち越し = 失わせたボクセルの差 / 100 + （殺した − 殺された）× 3 の平均。いちばん勝ち越した挑戦者は、新しい種でもう {bouts} 試合王者と稽古し直し（確かめ）、そこでも {Threshold:0.00} を超えて勝ち越したときだけ新しい王者になる。{generations} 世代（種 {seed}）。"));
        report.AppendLine();
        report.AppendLine("| 世代 | 変えた所 | 勝ち越し（選んだ稽古） | 勝ち越し（確かめ） | 入れ替え | 挑戦者が失わせた（確かめ・平均） | 王者が失わせた（確かめ・平均） |");
        report.AppendLine("|---:|---|---:|---:|---|---:|---:|");
        for (var g = 0; g < generations; g++)
        {
            var scale = 1f - (0.6f * g / Math.Max(1, generations - 1));
            var challengers = Enumerable.Range(0, lambda).Select(_ => Mutate(champion, scale, random)).ToList();
            var results = new SparResult[lambda, bouts];
            Parallel.For(0, lambda * bouts, k =>
            {
                var (c, b) = (k / bouts, k % bouts);
                var boutSeed = seed + (g * 1000) + b;
                // 偶数番は挑戦者が赤、奇数番は青（コーナーと立ち位置の偏りを消す）。
                results[c, b] = b % 2 == 0
                    ? SwordSparring.Run(challengers[c].Style, champion, boutSeed, seconds)
                    : SwordSparring.Run(champion, challengers[c].Style, boutSeed, seconds);
            });
            var scored = Enumerable.Range(0, lambda).Select(c =>
            {
                float margin = 0f, dealt = 0f, taken = 0f;
                for (var b = 0; b < bouts; b++)
                {
                    var r = results[c, b];
                    var red = b % 2 == 0;
                    margin += red ? r.RedMargin : -r.RedMargin;
                    dealt += red ? r.DealtByRed : r.DealtByBlue;
                    taken += red ? r.DealtByBlue : r.DealtByRed;
                }

                return (Challenger: challengers[c], Margin: margin / bouts, Dealt: dealt / bouts, Taken: taken / bouts);
            }).ToList();
            var best = scored.MaxBy(s => s.Margin);
            // 確かめ: 選ぶのに使っていない種で、もう一度。
            var check = new SparResult[bouts];
            if (best.Margin > Threshold)
            {
                Parallel.For(0, bouts, b =>
                {
                    var boutSeed = seed + 500_000 + (g * 1000) + b;
                    check[b] = b % 2 == 0
                        ? SwordSparring.Run(best.Challenger.Style, champion, boutSeed, seconds)
                        : SwordSparring.Run(champion, best.Challenger.Style, boutSeed, seconds);
                });
            }

            float checkMargin = 0f, checkDealt = 0f, checkTaken = 0f;
            for (var b = 0; b < bouts && check[b] is not null; b++)
            {
                var red = b % 2 == 0;
                checkMargin += (red ? check[b].RedMargin : -check[b].RedMargin) / bouts;
                checkDealt += (red ? check[b].DealtByRed : check[b].DealtByBlue) / bouts;
                checkTaken += (red ? check[b].DealtByBlue : check[b].DealtByRed) / bouts;
            }

            var accepted = best.Margin > Threshold && checkMargin > Threshold;
            if (accepted)
            {
                champion = best.Challenger.Style;
            }

            report.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {g + 1} | {best.Challenger.Change} | {best.Margin:0.00} | {(check[0] is null ? "—" : checkMargin.ToString("0.00", CultureInfo.InvariantCulture))} | {(accepted ? "○" : "—")} | {checkDealt:0} | {checkTaken:0} |"));
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"gen {g + 1}: {best.Challenger.Change} margin {best.Margin:0.00} check {checkMargin:0.00} {(accepted ? "accepted" : "kept")} ({watch.Elapsed.TotalSeconds:0}s)"));
        }

        File.WriteAllText(outPath, JsonSerializer.Serialize(champion, Json));
        report.AppendLine();
        report.AppendLine("## 磨いた流儀");
        report.AppendLine();
        report.Append(Describe(start, champion));
        report.AppendLine();
        report.AppendLine("## 確かめる（まだ使っていない種）");
        report.AppendLine();
        report.Append(Confirm(start, champion, finalBouts, finalSeconds, log));
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"（稽古にかかった時間 {watch.Elapsed.TotalMinutes:0.0} 分）"));
        return report.ToString();
    }

    /// <summary>保存した流儀を読む。</summary>
    public static SwordStyle Load(string path) =>
        JsonSerializer.Deserialize<SwordStyle>(File.ReadAllText(path), Json) ?? throw new JsonException("空の流儀です。");

    /// <summary>1 か所だけ変えた挑戦者: 7 割は 1 つの技の形と間合い、3 割は 1 つの技の選ばれやすさ（× e^±0.7、0.05〜5）。</summary>
    private static (SwordStyle Style, string Change) Mutate(SwordStyle style, float scale, Random random)
    {
        var i = random.Next(style.Techniques.Count);
        var technique = style.Techniques[i];
        if (random.NextDouble() < 0.7)
        {
            var basic = SwordTechnique.Basics.FirstOrDefault(b => b.Name == technique.Name) ?? technique;
            var techniques = style.Techniques.ToArray();
            techniques[i] = SwordLab.Mutate(technique, basic, 0.5f * scale, random);
            return (style with { Techniques = techniques }, $"{technique.Title} の形と間合い");
        }

        var weights = style.Weights.ToArray();
        var before = weights[i];
        weights[i] = Math.Clamp(before * MathF.Exp(0.7f * (float)((random.NextDouble() * 2.0) - 1.0)), 0.05f, 5f);
        return (style with { Weights = weights }, string.Create(CultureInfo.InvariantCulture, $"{technique.Title} の選ばれやすさ {before:0.00} → {weights[i]:0.00}"));
    }

    /// <summary>出発点と磨いた流儀の、技ごとの違い。</summary>
    private static string Describe(SwordStyle start, SwordStyle champion)
    {
        var text = new StringBuilder();
        text.AppendLine("| 技 | 選ばれやすさ（前 → 後） | 間合い [cm]（前 → 後） | 打ちの時間 [ms]（前 → 後） | 踏み込み（前 → 後） | 刃筋（前 → 後） |");
        text.AppendLine("|---|---|---|---|---|---|");
        for (var i = 0; i < champion.Techniques.Count; i++)
        {
            var (a, b) = (start.Techniques[i], champion.Techniques[i]);
            static string D(float r) => string.Create(CultureInfo.InvariantCulture, $"{r * 180f / MathF.PI:0}°");
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {b.Title} | {start.Weights[i]:0.00} → {champion.Weights[i]:0.00} | {a.Range * 100f:0} → {b.Range * 100f:0} | {a.Swing.StrikeTime * 1000f:0} → {b.Swing.StrikeTime * 1000f:0} | {a.Swing.Step:0.00} → {b.Swing.Step:0.00} | {D(a.Swing.Roll)} → {D(b.Swing.Roll)} |"));
        }

        return text.ToString();
    }

    /// <summary>
    /// 新しい種で: 磨いた流儀と出発点を剣で戦わせ、技ごとにどれだけ斬ったかを数える。磨いた流儀・出発点・基本の技それぞれを、拳の最強ボット
    /// （champion）とも戦わせる。
    /// </summary>
    private string Confirm(SwordStyle start, SwordStyle champion, int count, float boutSeconds, TextWriter log)
    {
        var text = new StringBuilder();
        var results = new SparResult[count];
        Parallel.For(0, count, b => results[b] = b % 2 == 0
            ? SwordSparring.Run(champion, start, seed + 900_000 + b, boutSeconds)
            : SwordSparring.Run(start, champion, seed + 900_000 + b, boutSeconds));
        int win = 0, loss = 0, kills = 0, deaths = 0;
        float dealt = 0f, taken = 0f;
        var byTechnique = new Dictionary<string, float>();
        var used = new Dictionary<string, int>();
        var startBy = new Dictionary<string, float>();
        for (var b = 0; b < count; b++)
        {
            var r = results[b];
            var red = b % 2 == 0;
            var margin = red ? r.RedMargin : -r.RedMargin;
            win += margin > 0f ? 1 : 0;
            loss += margin < 0f ? 1 : 0;
            kills += (red ? r.BlueDied : r.RedDied) ? 1 : 0;
            deaths += (red ? r.RedDied : r.BlueDied) ? 1 : 0;
            dealt += red ? r.DealtByRed : r.DealtByBlue;
            taken += red ? r.DealtByBlue : r.DealtByRed;
            foreach (var (k, v) in red ? r.RedByTechnique : r.BlueByTechnique)
            {
                byTechnique[k] = byTechnique.GetValueOrDefault(k) + v;
            }

            foreach (var (k, v) in red ? r.BlueByTechnique : r.RedByTechnique)
            {
                startBy[k] = startBy.GetValueOrDefault(k) + v;
            }

            foreach (var name in red ? r.RedUsed : r.BlueUsed)
            {
                used[name] = used.GetValueOrDefault(name) + 1;
            }
        }

        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"磨いた流儀 対 出発点（剣同士、{count} 試合 × {boutSeconds:0} 秒）: {win} 勝 {loss} 敗、殺した {kills}・殺された {deaths}、失わせたボクセル 平均 {dealt / count:0} 対 {taken / count:0}。"));
        text.AppendLine();
        text.AppendLine("技ごと（磨いた流儀が出した回数と、その技で相手から失わせたボクセル。「構え」は技を出していない間に斬れた分）:");
        text.AppendLine();
        text.AppendLine("| 技 | 出した回数 | 失わせたボクセル | 1 回あたり | 出発点がその技で失わせたボクセル |");
        text.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var t in champion.Techniques)
        {
            var n = used.GetValueOrDefault(t.Name);
            var cells = byTechnique.GetValueOrDefault(t.Name);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {t.Title} | {n} | {cells:0} | {(n > 0 ? cells / n : 0f):0.0} | {startBy.GetValueOrDefault(t.Name):0} |"));
        }

        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"| 構え | — | {byTechnique.GetValueOrDefault(SwordSparring.Guard):0} | — | {startBy.GetValueOrDefault(SwordSparring.Guard):0} |"));
        text.AppendLine();
        log.WriteLine($"confirm: {win}-{loss}");

        // 拳の最強ボットとも。
        var champ = Contender.Named("champion");
        var contenders = new[]
        {
            new Contender("sword-spar", s => champion.Bot(s), WeaponKind.IronSword),
            new Contender("sword-drill", s => start.Bot(s), WeaponKind.IronSword),
        };
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"拳の最強ボット（champion）と（各 {count} 試合 × {boutSeconds:0} 秒）:"));
        text.AppendLine();
        text.AppendLine("| 組 | 勝ち | 負け | 分け | 取ったダウン | 取られたダウン | 与えたダメージ | 受けたダメージ |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var bot in contenders)
        {
            var specs = Enumerable.Range(0, count).Select(i => i % 2 == 0
                ? new BoutSpec(bot, champ, seed + 800_000 + i, boutSeconds)
                : new BoutSpec(champ, bot, seed + 800_000 + i, boutSeconds)).ToList();
            int w = 0, l = 0, d = 0, downsFor = 0, downsAgainst = 0;
            float given = 0f, got = 0f;
            foreach (var r in BotArena.RunAll(specs))
            {
                var red = r.Spec.Red.Name == bot.Name;
                var mine = red ? r.Red : r.Blue;
                var theirs = red ? r.Blue : r.Red;
                var sign = red ? r.Winner : -r.Winner;
                w += sign > 0 ? 1 : 0;
                l += sign < 0 ? 1 : 0;
                d += sign == 0 ? 1 : 0;
                downsFor += mine.Knockdowns;
                downsAgainst += theirs.Knockdowns;
                given += mine.DamageDealt;
                got += theirs.DamageDealt;
            }

            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {bot.Name} 対 champion | {w} | {l} | {d} | {downsFor} | {downsAgainst} | {given:0} | {got:0} |"));
            log.WriteLine($"vs champion {bot.Name}: {w}-{l}-{d}");
        }

        return text.ToString();
    }
}
