using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 棍棒の振りの研究: 型（縦・横・突き）ごとに、拳の位置・棍棒の向き・時間・手首のタメ・胴のひねり・踏み込み・間合いを
/// 進化戦略（(1+λ)、λ 本を並列に試す）で動かし、構えて立つ相手から意識を奪う量（威力）を上げる。見つけた振りから要素を 1 つずつ
/// 外して（タメなし・ひねりなし・踏み込みなし・等速）何が威力を生んでいるかを調べ、最後にその振りで戦うボットを拳のボットと戦わせる。
/// </summary>
internal sealed class SwingLab(int generations, int lambda, int seed)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    /// <summary>研究を回し、Markdown の報告を返す。見つけた振りは <paramref name="bestPath"/> へ JSON で書く。</summary>
    public string Run(string bestPath, TextWriter log, int bouts, float boutSeconds, IReadOnlyList<SwingFamily> families)
    {
        var watch = Stopwatch.StartNew();
        var report = new StringBuilder();
        report.AppendLine("# 棍棒の振りの研究（swinglab）");
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"型ごとに (1+{lambda}) 進化戦略 × {generations} 世代（種 {seed}）。的は構えて立つ相手（StandGuard、両手のグローブで顔を守る）。威力 = 1 振りで相手に溜まった脳しんとう（0.25 でふらつき、0.7 で意識が途切れる）。どの数値も、間合い ± 2 cm の 3 点で振った平均（1 点の間合いでしか効かない振りを選ばないため）。"));
        report.AppendLine();
        var best = new List<SwingResult>();
        foreach (var family in families)
        {
            var start = SwingTest.RunAround(WeaponSwing.Default(family), 0.38f);
            var champion = start;
            var log2 = new StringBuilder();
            log2.AppendLine("| 世代 | 威力 | 頭に | 頭の速さ [m/s] | 当たった時の頭の速さ | 運動量 [N·s] | 頭のねじれ [rad/s] | 頭への最短 [cm] | 壊したボクセル | 自分の脳しんとう |");
            log2.AppendLine("|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|");
            log2.AppendLine(Row("出発点", champion));
            var random = new Random(seed + (int)family);
            for (var g = 0; g < generations; g++)
            {
                var scale = 1f - (0.7f * g / Math.Max(1, generations - 1)); // だんだん細かく
                var candidates = Enumerable.Range(0, lambda).Select(_ => Mutate(champion.Swing, champion.Range, scale, random)).ToList();
                var results = new SwingResult[candidates.Count];
                Parallel.For(0, candidates.Count, i => results[i] = SwingTest.RunAround(candidates[i].Swing, candidates[i].Range));
                var top = results.MaxBy(r => r.Fitness)!;
                if (top.Fitness > champion.Fitness)
                {
                    champion = top;
                }

                log2.AppendLine(Row(string.Create(CultureInfo.InvariantCulture, $"{g + 1}"), champion));
                log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{family} gen {g + 1}: power {champion.Power:0.000} fitness {champion.Fitness:0.0000} tip {champion.TipSpeed:0.00} ({watch.Elapsed.TotalSeconds:0}s)"));
            }

            best.Add(champion);
            report.AppendLine("## " + Name(family));
            report.AppendLine();
            report.Append(log2);
            report.AppendLine();
            report.AppendLine("見つけた振り:");
            report.AppendLine();
            report.AppendLine(Describe(champion));
            report.AppendLine();
            report.AppendLine("要素を 1 つずつ外すと:");
            report.AppendLine();
            report.Append(Ablation(champion));
            report.AppendLine();
        }

        File.WriteAllText(bestPath, JsonSerializer.Serialize(best.Select(b => new SavedSwing(b.Swing, b.Range)).ToList(), Json));
        report.AppendLine("## 見つけた振りで戦う");
        report.AppendLine();
        report.Append(Bouts(best, bouts, boutSeconds, log));
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"（研究にかかった時間 {watch.Elapsed.TotalMinutes:0.0} 分）"));
        return report.ToString();
    }

    /// <summary>保存する振り（間合いと一緒に）。</summary>
    internal sealed record SavedSwing(WeaponSwing Swing, float Range);

    /// <summary>保存した振りを読む。</summary>
    public static IReadOnlyList<(WeaponSwing Swing, float Range)> Load(string path) =>
        JsonSerializer.Deserialize<List<SavedSwing>>(File.ReadAllText(path), Json)!.Select(s => (s.Swing, s.Range)).ToList();

    internal static string Name(SwingFamily family) => family switch
    {
        SwingFamily.Chop => "縦振り",
        SwingFamily.Side => "横振り",
        SwingFamily.Thrust => "突き",
        SwingFamily.Bat => "バットの振り（両手）",
        _ => family.ToString(),
    };

    private static string Row(string label, SwingResult r) => string.Create(CultureInfo.InvariantCulture,
        $"| {label} | {r.Power:0.000} | {(r.Part == BodyPartKind.Head ? "○" : r.Part?.ToString() ?? "—")} | {r.TipSpeed:0.00} | {r.HitTipSpeed:0.00} | {r.Impulse:0.000} | {r.HeadTwist:0} | {r.HeadMiss * 100f:0.0} | {r.Cells:0} | {r.SelfConcussion:0.000} |");

    private static string Describe(SwingResult r)
    {
        var s = r.Swing;
        static string V(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})");
        static string D(float rad) => string.Create(CultureInfo.InvariantCulture, $"{rad * 180f / MathF.PI:0}°");
        return string.Create(CultureInfo.InvariantCulture,
            $"- 間合い {r.Range * 100f:0} cm、振りかぶり {s.WindupTime * 1000f:0} ms・打ち {s.StrikeTime * 1000f:0} ms\n" +
            $"- 振りかぶり: 拳 {V(s.WindupFist)}（腕の長さ = 1）、棍棒の向き 方位 {D(s.WindupAzimuth)}・仰角 {D(s.WindupElevation)}\n" +
            $"- 打ち: 拳 {V(s.StrikeFist)}、棍棒の向き 方位 {D(s.StrikeAzimuth)}・仰角 {D(s.StrikeElevation)}\n" +
            $"- 手首のタメ {s.WristDelay:0.00}、胴のひねり {D(s.Twist)}、踏み込み {s.Step:0.00}、拳の加速の指数 {s.Ease:0.00}");
    }

    /// <summary>要素を 1 つずつ外した振りの威力。</summary>
    private static string Ablation(SwingResult champion)
    {
        var s = champion.Swing;
        var variants = new (string Name, WeaponSwing Swing)[]
        {
            ("見つけた振り", s),
            ("手首のタメなし（拳と一緒に回す）", s with { WristDelay = 0f }),
            ("胴のひねりなし", s with { Twist = 0f }),
            ("踏み込みなし", s with { Step = 0f }),
            ("拳を等速で（加速しない）", s with { Ease = 1f }),
            ("打ちを 1.5 倍ゆっくり", s with { StrikeTime = s.StrikeTime * 1.5f }),
        };
        var results = new SwingResult[variants.Length];
        Parallel.For(0, variants.Length, i => results[i] = SwingTest.RunAround(variants[i].Swing, champion.Range));
        var text = new StringBuilder();
        text.AppendLine("| 振り | 威力 | 頭に | 頭の速さ [m/s] | 当たった時の頭の速さ | 運動量 [N·s] | 頭のねじれ [rad/s] | 頭への最短 [cm] | 壊したボクセル | 自分の脳しんとう |");
        text.AppendLine("|---|---:|---|---:|---:|---:|---:|---:|---:|---:|");
        for (var i = 0; i < variants.Length; i++)
        {
            text.AppendLine(Row(variants[i].Name, results[i]));
        }

        return text.ToString();
    }

    /// <summary>保存した振りで、試合だけを回す。</summary>
    public string FightOnly(string bestPath, TextWriter log, int bouts, float boutSeconds) =>
        Bouts(Load(bestPath).Select(s => (s.Swing, s.Range)).ToList(), bouts, boutSeconds, log);

    private string Bouts(IReadOnlyList<SwingResult> best, int bouts, float seconds, TextWriter log) =>
        Bouts(best.Select(b => (b.Swing, b.Range)).ToList(), bouts, seconds, log);

    /// <summary>見つけた振りで戦う棍棒のボットを、拳のボット（champion）と戦わせる（半分ずつコーナーを入れ替える）。</summary>
    private string Bouts(IReadOnlyList<(WeaponSwing Swing, float Range)> best, int bouts, float seconds, TextWriter log)
    {
        var range = best.Average(b => b.Range);
        var swings = best.Select(b => b.Swing).ToList();
        var twoHanded = swings[0].TwoHanded;
        var name = twoHanded ? "bat" : "club";
        var club = new Contender(name, s => new ClubBot(swings, s, range, rest: 0.6f), WeaponKind.WoodenClub, twoHanded);
        var basic = new Contender(name + "-basic", s => new ClubBot([.. swings.Select(w => w.Family).Distinct().Select(WeaponSwing.Default)], s, rest: 0.6f), WeaponKind.WoodenClub, twoHanded);
        var champion = Contender.Named("champion");
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"各 {bouts} 試合 × {seconds:0} 秒。"));
        text.AppendLine();
        text.AppendLine("| 組 | 勝ち | 負け | 分け | 取ったダウン | 取られたダウン | 当てた数 | ガードの外 | 与えたダメージ | 受けたダメージ |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var bot in new[] { club, basic })
        {
            var specs = Enumerable.Range(0, bouts).Select(i => i % 2 == 0
                ? new BoutSpec(bot, champion, seed + i, seconds)
                : new BoutSpec(champion, bot, seed + i, seconds)).ToList();
            var results = BotArena.RunAll(specs);
            int win = 0, loss = 0, draw = 0, downsFor = 0, downsAgainst = 0, landed = 0, head = 0;
            float dealt = 0f, taken = 0f;
            foreach (var r in results)
            {
                var mine = r.Spec.Red.Name == bot.Name ? r.Red : r.Blue;
                var theirs = r.Spec.Red.Name == bot.Name ? r.Blue : r.Red;
                var sign = r.Spec.Red.Name == bot.Name ? r.Winner : -r.Winner;
                win += sign > 0 ? 1 : 0;
                loss += sign < 0 ? 1 : 0;
                draw += sign == 0 ? 1 : 0;
                downsFor += mine.Knockdowns;
                downsAgainst += theirs.Knockdowns;
                landed += mine.Landed;
                head += mine.CleanHits;
                dealt += mine.DamageDealt;
                taken += theirs.DamageDealt;
            }

            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {bot.Name} 対 champion | {win} | {loss} | {draw} | {downsFor} | {downsAgainst} | {landed} | {head} | {dealt:0} | {taken:0} |"));
            log.WriteLine($"bouts {bot.Name}: {win}-{loss}-{draw}");
        }

        return text.ToString();
    }

    /// <summary>振りと間合いを少し動かす（範囲の外へは出さない）。</summary>
    private static (WeaponSwing Swing, float Range) Mutate(WeaponSwing s, float range, float scale, Random random)
    {
        float N() => (float)((random.NextDouble() * 2.0) - 1.0) * scale;
        Vector3 V(Vector3 v, float size) => Vector3.Clamp(v + (new Vector3(N(), N(), N()) * size), new Vector3(-0.8f, -0.4f, -0.2f), new Vector3(0.6f, 0.8f, 1.1f));
        var next = s with
        {
            WindupFist = V(s.WindupFist, 0.12f),
            StrikeFist = V(s.StrikeFist, 0.12f),
            WindupAzimuth = s.WindupAzimuth + (0.35f * N()),
            WindupElevation = Math.Clamp(s.WindupElevation + (0.35f * N()), -1.4f, 2.8f),
            StrikeAzimuth = s.StrikeAzimuth + (0.35f * N()),
            StrikeElevation = Math.Clamp(s.StrikeElevation + (0.35f * N()), -1.4f, 1.4f),
            WindupTime = Math.Clamp(s.WindupTime * (1f + (0.25f * N())), 0.15f, 0.7f),
            StrikeTime = Math.Clamp(s.StrikeTime * (1f + (0.25f * N())), 0.08f, 0.4f),
            WristDelay = Math.Clamp(s.WristDelay + (0.2f * N()), 0f, 0.85f),
            Twist = Math.Clamp(s.Twist + (0.25f * N()), -0.8f, 0.8f),
            Step = Math.Clamp(s.Step + (0.3f * N()), 0f, 1f),
            Ease = Math.Clamp(s.Ease + (0.5f * N()), 1f, 3.5f),
        };
        return (next, Math.Clamp(range + (0.04f * N()), 0.28f, 0.5f));
    }
}
