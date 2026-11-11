using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 剣の技のドリル: 基本の技（<see cref="SwordTechnique.Basics"/>）を 1 つずつ、構えて立つ相手に出して（<see cref="SwordDrill"/>）、
/// 振りの全部の値（拳の位置・剣の向き・刃筋・時間・手首のタメ・胴のひねり・踏み込み・加速）と、その技の間合いの中での間合いを
/// 進化戦略（(1+λ)、λ 本を並列に試す）で磨き、狙った部位への効きを上げる。磨いた技から要素を 1 つずつ外して何が効いているかを調べ、
/// 最後に磨いた技を間合いに応じて使い分けるボット（<see cref="SwordBot"/>）を拳のボットと戦わせる。
/// </summary>
internal sealed class SwordLab(int generations, int lambda, int seed)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    /// <summary>ドリルを回し、Markdown の報告を返す。磨いた技は <paramref name="bestPath"/> へ JSON で書く。</summary>
    public string Run(string bestPath, TextWriter log, int bouts, float boutSeconds, IReadOnlyCollection<string>? only = null)
    {
        var watch = Stopwatch.StartNew();
        var report = new StringBuilder();
        report.AppendLine("# 剣の技のドリル（swordlab）");
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"技ごとに基本の形から (1+{lambda}) 進化戦略 × {generations} 世代（種 {seed}）。的は構えて立つ相手（StandGuard、両手のグローブで顔を守る）。効き = 狙った部位から斬り取ったボクセル / 60 + 切り落とし × 2 + 死 × 3 + 脳しんとう。どの数値も、間合い ± 2 cm の 3 点で出した平均（切り落とし・死はその割合）。間合いは基本の間合い ± {SwordTechnique.RangeLeeway * 100f:0} cm の中で動かす。"));
        report.AppendLine();
        var techniques = SwordTechnique.Basics.Where(t => only is null || only.Contains(t.Name)).ToList();
        // 前に磨いた技（一部だけ磨くときに残りに使う）は、長いドリルの前に読んでおく（壊れていたらここで止まる）。
        var saved = File.Exists(bestPath) ? Load(bestPath) : [];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(bestPath))!);
        var best = new List<SwordTechnique>();
        var summary = new StringBuilder();
        summary.AppendLine("| 技 | 間合い | 狙い | 基本の効き | 磨いた効き | 斬ったセル（狙い） | 切り落とし | 死 | 切っ先の速さ [m/s] |");
        summary.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|");
        var sections = new StringBuilder();
        foreach (var basic in techniques)
        {
            var start = SwordDrill.RunAround(basic);
            var champion = start;
            var progress = new StringBuilder();
            progress.AppendLine(Header("世代"));
            progress.AppendLine(Row("基本", champion));
            var random = new Random(seed + (1000 * SwordTechnique.Basics.ToList().IndexOf(basic))); // 種が同じなら同じドリル
            for (var g = 0; g < generations; g++)
            {
                var scale = 1f - (0.7f * g / Math.Max(1, generations - 1)); // だんだん細かく
                var candidates = Enumerable.Range(0, lambda).Select(_ => Mutate(champion.Technique, basic, scale, random)).ToList();
                var results = new SwordResult[candidates.Count];
                Parallel.For(0, candidates.Count, i => results[i] = SwordDrill.RunAround(candidates[i]));
                var top = results.MaxBy(r => r.Fitness)!;
                if (top.Fitness > champion.Fitness)
                {
                    champion = top;
                }

                progress.AppendLine(Row(string.Create(CultureInfo.InvariantCulture, $"{g + 1}"), champion));
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{basic.Name} gen {g + 1}: effect {champion.Effect:0.00} fitness {champion.Fitness:0.000} target {champion.TargetCells:0} severed {champion.Severed:0.00} killed {champion.Killed:0.00} ({watch.Elapsed.TotalSeconds:0}s)"));
            }

            best.Add(champion.Technique);
            summary.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {basic.Title} | {SwordTechnique.MaaiName(basic.Maai)} {champion.Technique.Range * 100f:0} cm | {basic.Target} | {start.Effect:0.00} | **{champion.Effect:0.00}** | {champion.TargetCells:0} | {champion.Severed:0.00} | {champion.Killed:0.00} | {champion.TipSpeed:0.0} |"));
            sections.AppendLine("## " + basic.Title + "（" + SwordTechnique.MaaiName(basic.Maai) + "）");
            sections.AppendLine();
            sections.AppendLine(basic.Origin + "。");
            sections.AppendLine();
            sections.Append(progress);
            sections.AppendLine();
            sections.AppendLine("基本の形:");
            sections.AppendLine();
            sections.AppendLine(Describe(basic));
            sections.AppendLine();
            sections.AppendLine("磨いた形:");
            sections.AppendLine();
            sections.AppendLine(Describe(champion.Technique));
            sections.AppendLine();
            sections.AppendLine("要素を 1 つずつ外すと:");
            sections.AppendLine();
            sections.Append(Ablation(champion.Technique));
            sections.AppendLine();
        }

        // 一部の技だけ磨いたときは、残りは前に磨いたもの（無ければ基本）をそのまま使う。
        var all = SwordTechnique.Basics.Select(b => best.FirstOrDefault(t => t.Name == b.Name) ?? saved.FirstOrDefault(t => t.Name == b.Name) ?? b).ToList();
        File.WriteAllText(bestPath, JsonSerializer.Serialize(all, Json));
        report.AppendLine("## まとめ");
        report.AppendLine();
        report.Append(summary);
        report.AppendLine();
        report.Append(sections);
        report.AppendLine("## 磨いた技を間合いに応じて使い分けて戦う");
        report.AppendLine();
        report.Append(Bouts(all, bouts, boutSeconds, log));
        report.AppendLine();
        report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"（ドリルにかかった時間 {watch.Elapsed.TotalMinutes:0.0} 分）"));
        return report.ToString();
    }

    /// <summary>保存した技を読む。</summary>
    public static IReadOnlyList<SwordTechnique> Load(string path) =>
        JsonSerializer.Deserialize<List<SwordTechnique>>(File.ReadAllText(path), Json) ?? throw new JsonException("空の技の一覧です。");

    /// <summary>保存した技で、試合だけを回す。</summary>
    public string FightOnly(string bestPath, TextWriter log, int bouts, float seconds) => Bouts(Load(bestPath), bouts, seconds, log);

    private static string Header(string first) =>
        $"| {first} | 効き | 斬ったセル（狙い） | 切り落とし | 死 | 脳しんとう | 斬ったセル（ほか） | 刃と狙いの最短 [cm] | 切っ先の速さ [m/s] | 最初に斬った | 自分を斬ったセル |\n|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|";

    private static string Row(string label, SwordResult r) => string.Create(CultureInfo.InvariantCulture,
        $"| {label} | {r.Effect:0.00} | {r.TargetCells:0} | {r.Severed:0.00} | {r.Killed:0.00} | {r.Concussion:0.000} | {r.OtherCells:0} | {r.Miss * 100f:0.0} | {r.TipSpeed:0.0} | {r.FirstPart?.ToString() ?? "—"} | {r.SelfCells:0} |");

    private static string Describe(SwordTechnique t)
    {
        var s = t.Swing;
        static string V(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})");
        static string D(float rad) => string.Create(CultureInfo.InvariantCulture, $"{rad * 180f / MathF.PI:0}°");
        return string.Create(CultureInfo.InvariantCulture,
            $"- 間合い {t.Range * 100f:0} cm、振りかぶり {s.WindupTime * 1000f:0} ms・打ち {s.StrikeTime * 1000f:0} ms\n" +
            $"- 振りかぶり: 拳 {V(s.WindupFist)}（腕の長さ = 1）、剣の向き 方位 {D(s.WindupAzimuth)}・仰角 {D(s.WindupElevation)}\n" +
            $"- 打ち: 拳 {V(s.StrikeFist)}、剣の向き 方位 {D(s.StrikeAzimuth)}・仰角 {D(s.StrikeElevation)}\n" +
            $"- 刃筋 {D(s.Roll)}、手首のタメ {s.WristDelay:0.00}、胴のひねり {D(s.Twist)}、踏み込み {s.Step:0.00}、拳の加速の指数 {s.Ease:0.00}");
    }

    /// <summary>要素を 1 つずつ外した技の効き。</summary>
    private static string Ablation(SwordTechnique t)
    {
        var s = t.Swing;
        var variants = new (string Name, SwordTechnique Technique)[]
        {
            ("磨いた技", t),
            ("刃筋を 90° 寝かせる（腹で当たる）", t with { Swing = s with { Roll = s.Roll + (MathF.PI / 2f) } }),
            ("手首のタメなし", t with { Swing = s with { WristDelay = 0f } }),
            ("胴のひねりなし", t with { Swing = s with { Twist = 0f } }),
            ("踏み込みなし", t with { Swing = s with { Step = 0f } }),
            ("拳を等速で（加速しない）", t with { Swing = s with { Ease = 1f } }),
            ("打ちを 1.5 倍ゆっくり", t with { Swing = s with { StrikeTime = s.StrikeTime * 1.5f } }),
        };
        var results = new SwordResult[variants.Length];
        Parallel.For(0, variants.Length, i => results[i] = SwordDrill.RunAround(variants[i].Technique));
        var text = new StringBuilder();
        text.AppendLine(Header("技"));
        for (var i = 0; i < variants.Length; i++)
        {
            text.AppendLine(Row(variants[i].Name, results[i]));
        }

        return text.ToString();
    }

    /// <summary>磨いた技で戦う剣のボットと、基本の技で戦う剣のボットを、拳のボット（champion）と戦わせる（半分ずつコーナーを入れ替える）。</summary>
    private string Bouts(IReadOnlyList<SwordTechnique> techniques, int bouts, float seconds, TextWriter log)
    {
        var sword = new Contender("sword", s => new SwordBot(techniques, s), WeaponKind.IronSword);
        var basic = new Contender("sword-basic", SwordBot.Basic, WeaponKind.IronSword);
        var champion = Contender.Named("champion");
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"各 {bouts} 試合 × {seconds:0} 秒。"));
        text.AppendLine();
        text.AppendLine("| 組 | 勝ち | 負け | 分け | 取ったダウン | 取られたダウン | 当てた数 | ガードの外 | 与えたダメージ | 受けたダメージ |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var bot in new[] { sword, basic })
        {
            var specs = Enumerable.Range(0, bouts).Select(i => i % 2 == 0
                ? new BoutSpec(bot, champion, seed + i, seconds)
                : new BoutSpec(champion, bot, seed + i, seconds)).ToList();
            var results = BotArena.RunAll(specs);
            int win = 0, loss = 0, draw = 0, downsFor = 0, downsAgainst = 0, landed = 0, clean = 0;
            float dealt = 0f, taken = 0f;
            foreach (var r in results)
            {
                var red = r.Spec.Red.Name == bot.Name;
                var mine = red ? r.Red : r.Blue;
                var theirs = red ? r.Blue : r.Red;
                var sign = red ? r.Winner : -r.Winner;
                win += sign > 0 ? 1 : 0;
                loss += sign < 0 ? 1 : 0;
                draw += sign == 0 ? 1 : 0;
                downsFor += mine.Knockdowns;
                downsAgainst += theirs.Knockdowns;
                landed += mine.Landed;
                clean += mine.CleanHits;
                dealt += mine.DamageDealt;
                taken += theirs.DamageDealt;
            }

            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {bot.Name} 対 champion | {win} | {loss} | {draw} | {downsFor} | {downsAgainst} | {landed} | {clean} | {dealt:0} | {taken:0} |"));
            log.WriteLine($"bouts {bot.Name}: {win}-{loss}-{draw}");
        }

        return text.ToString();
    }

    /// <summary>刃筋を (−π/2, π/2] に収める（両刃なので π 回すと同じ刃筋。構えから余計に回さない）。</summary>
    private static float WrapRoll(float roll)
    {
        var r = MathF.IEEERemainder(roll, MathF.PI);
        return r <= -MathF.PI / 2f ? r + MathF.PI : r;
    }

    /// <summary>技の振りと間合いを少し動かす（範囲の外へは出さない。間合いは基本の間合い ± <see cref="SwordTechnique.RangeLeeway"/>）。</summary>
    internal static SwordTechnique Mutate(SwordTechnique t, SwordTechnique basic, float scale, Random random)
    {
        float N() => (float)((random.NextDouble() * 2.0) - 1.0) * scale;
        Vector3 V(Vector3 v, float size) => Vector3.Clamp(v + (new Vector3(N(), N(), N()) * size), new Vector3(-0.8f, -0.7f, -0.2f), new Vector3(0.6f, 0.9f, 1.2f));
        var s = t.Swing;
        var swing = s with
        {
            WindupFist = V(s.WindupFist, 0.12f),
            StrikeFist = V(s.StrikeFist, 0.12f),
            WindupAzimuth = s.WindupAzimuth + (0.35f * N()),
            WindupElevation = Math.Clamp(s.WindupElevation + (0.35f * N()), -1.4f, 2.8f),
            StrikeAzimuth = s.StrikeAzimuth + (0.35f * N()),
            StrikeElevation = Math.Clamp(s.StrikeElevation + (0.35f * N()), -1.4f, 1.4f),
            WindupTime = Math.Clamp(s.WindupTime * (1f + (0.25f * N())), 0.12f, 0.7f),
            StrikeTime = Math.Clamp(s.StrikeTime * (1f + (0.25f * N())), 0.06f, 0.4f),
            WristDelay = Math.Clamp(s.WristDelay + (0.2f * N()), 0f, 0.85f),
            Twist = Math.Clamp(s.Twist + (0.25f * N()), -0.8f, 0.8f),
            Step = Math.Clamp(s.Step + (0.3f * N()), -1f, 1f), // 負は下がりながら（引き技）
            Ease = Math.Clamp(s.Ease + (0.5f * N()), 1f, 3.5f),
            Roll = WrapRoll(s.Roll + (0.4f * N())),
        };
        var range = Math.Clamp(t.Range + (0.04f * N()), basic.Range - SwordTechnique.RangeLeeway, basic.Range + SwordTechnique.RangeLeeway);
        return t with { Swing = swing, Range = range };
    }
}
