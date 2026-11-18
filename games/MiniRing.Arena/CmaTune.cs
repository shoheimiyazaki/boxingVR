using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MiniRing.Arena.Evolution;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>cmatune の設定（コマンドの引数）。</summary>
/// <param name="Targets">対象（<c>punch:2</c>・<c>defense:Slip</c>）。複数なら順に回す（探索的）。</param>
/// <param name="Defenses">拳の対象で、受け手に固定させる守り（省略なら型ごとの表）。</param>
/// <param name="Generations">世代の数。</param>
/// <param name="Lambda">1 世代の候補数（0 = <see cref="CmaTune.AutoLambda"/>）。</param>
/// <param name="Sigma">初めの σ（[0, 1] に正規化した空間で）。</param>
/// <param name="Samples">1 候補の標本（種）の数。</param>
/// <param name="Seconds">1 本のドリルの長さ [s]。</param>
/// <param name="Seed">乱数の種（CMA-ES と評価の種の元）。</param>
/// <param name="ConfirmN">最後の比べ直しの標本の数（使わなかった種。0 なら行わない）。</param>
/// <param name="MatchN">試合での確認の標本の数（0 なら行わない）。</param>
/// <param name="MatchSeconds">試合の長さ [s]。</param>
/// <param name="MaxParallel">並列の数の上限（省略で全コア）。</param>
internal sealed record CmaTuneConfig(IReadOnlyList<string> Targets, IReadOnlyList<Defense>? Defenses, int Generations = 40, int Lambda = 0, double Sigma = 0.15,
    int Samples = 48, float Seconds = 40f, int Seed = 1, int ConfirmN = 96, int MatchN = 16, float MatchSeconds = 45f, int? MaxParallel = null);

/// <summary>世代ごとの記録（点は「その世代の種での比べ」で、世代の間では比べられない）。</summary>
/// <param name="Generation">世代（1 から）。</param>
/// <param name="Sigma">その世代の標本を引いたときの σ。</param>
/// <param name="Best">その世代の候補の最良の点。</param>
/// <param name="Mean">候補の点の平均。</param>
/// <param name="Worst">最悪の点。</param>
/// <param name="M">更新後の分布の平均 m（折り返した [0, 1] の遺伝子）。</param>
internal sealed record CmaTuneRow(int Generation, double Sigma, double Best, double Mean, double Worst, double[] M);

/// <summary>cmatune の再開用の状態（世代ごとに書く）。設定が違う状態では再開しない。</summary>
internal sealed record CmaTuneState
{
    public string Target { get; init; } = "";

    public string[] Defenses { get; init; } = [];

    public int Samples { get; init; }

    public float Seconds { get; init; }

    public int Seed { get; init; }

    /// <summary>集団の大きさ・初めの σ・始めの値（champion のその技を遺伝子にしたもの）。再開の照合に使う（champion が途中で変わると、比べ直しの基準がずれるため。レビュー指摘）。</summary>
    public int Lambda { get; init; }

    public double Sigma { get; init; }

    public double[] Initial { get; init; } = [];

    public string Cma { get; init; } = "";

    public List<CmaTuneRow> Rows { get; init; } = [];

    /// <summary>これまでのドリルの予算（<see cref="DrillMeter"/> の数え方。再開の前の分を含む。キャッシュの効き方に依るので、再開で厳密には一致しない）。</summary>
    public long BudgetCalls { get; init; }

    public double BudgetSeconds { get; init; }
}

/// <summary>1 つの対象（拳の 1 技・守りの 1 技）の、遺伝子 → 点・値・スタイルの写し。</summary>
internal abstract class CmaTarget
{
    public abstract string Name { get; }

    public abstract IReadOnlyList<Gene> Genes { get; }

    /// <summary>中心（champion の値）の遺伝子。</summary>
    public abstract double[] Initial();

    /// <summary>標本 1 つ（種 1 つ）の点。大きいほど良い。</summary>
    public abstract double Point(IReadOnlyList<double> genes, int seed);

    /// <summary>champion に候補を重ねたスタイル。</summary>
    public abstract BotStyle Overlay(IReadOnlyList<double> genes);

    /// <summary>候補の本（<see cref="PunchBook"/> か <see cref="DefenseBook"/>）の JSON。</summary>
    public abstract string BookJson(IReadOnlyList<double> genes);

    /// <summary>点の説明（表の下に書く）。</summary>
    public abstract string ScoreNote { get; }

    /// <summary>遺伝子から実際の値を「名前=値」にして並べる。</summary>
    public string Values(IReadOnlyList<double> x)
        => string.Join(" ", Genes.Select((g, i) => string.Create(CultureInfo.InvariantCulture, $"{g.Name}={g.Decode(x[i]):0.###}")));
}

/// <summary>拳 1 技。攻め手 = <see cref="DefenseDrill.PunchAttacker"/> に候補の <see cref="PunchBook"/> を持たせたもの、受け手 = champion の体の構えと守りの表（守りを固定）。</summary>
internal sealed class PunchTarget : CmaTarget
{
    public const float Range = DefenseDrill.DefaultRange;

    /// <summary>ドリルの合図の遅れ [s]（<c>video --drill</c> の既定、<see cref="NoiseLab.DrillCue"/> と同じ）。</summary>
    public const float Cue = 0.04f;

    private readonly string _punch;
    private readonly Technique _technique;
    private readonly IReadOnlyList<Defense> _defenses;
    private readonly float _seconds;
    private readonly BotStyle _champion;
    private readonly PunchGenome _genome;
    private readonly BotStyle _defender;

    public PunchTarget(string punch, IReadOnlyList<Defense> defenses, float seconds, BotStyle champion)
    {
        var combo = Combo.Named(punch);
        if (combo.Steps.Count != 1)
        {
            throw new ArgumentException($"拳の対象は単発の型だけです（{punch}）。", nameof(punch));
        }

        _punch = punch;
        _technique = combo.Steps[0];
        _defenses = defenses;
        _seconds = seconds;
        _champion = champion;
        _genome = new PunchGenome(_technique, champion.Punches.MoveOf(_technique));
        _defender = CmaTune.DrillDefender(champion);
    }

    public override string Name => $"punch:{_punch}";

    public override IReadOnlyList<Gene> Genes => _genome.Genes;

    public override string ScoreNote
        => string.Create(CultureInfo.InvariantCulture,
            $"1 標本 = 1 つの種で、受け手の守り（{string.Join("・", _defenses)}）を全部回したもの。点 = Σ（頭と胴に与えたダメージ − 打ち返されたダメージ）/ 秒。受け手は champion の構えと守りの表、守りは固定、合図 {Cue:0.00} s、打ち返しあり、間合い {Range:0.00}。");

    public override double[] Initial() => _genome.Initial();

    private PunchBook Book(IReadOnlyList<double> genes) => _champion.Punches.With(_technique, _genome.Decode(genes));

    public override double Point(IReadOnlyList<double> genes, int seed)
    {
        var attacker = (DefenseDrill.PunchAttacker(_punch, Range) with { Punches = Book(genes) }).Validated();
        var sum = 0d;
        foreach (var defense in _defenses)
        {
            sum += DefenseDrill.Punch(_punch, defense, _defender, seed, _seconds, Range, Cue, 0f, null, null, attacker).AttackPoint(_seconds);
        }

        return sum;
    }

    public override BotStyle Overlay(IReadOnlyList<double> genes) => _champion with { Name = $"{_champion.Name}-cma-punch-{_punch}", Punches = Book(genes) };

    public override string BookJson(IReadOnlyList<double> genes) => JsonSerializer.Serialize(Book(genes), CmaTune.Json);
}

/// <summary>守り 1 技。点 = <see cref="DefenseTuner.SeedPoints"/>（守らない受け手との被ダメージの差 + 0.25 × 打ち返し）。</summary>
internal sealed class DefenseTarget : CmaTarget
{
    private readonly Defense _defense;
    private readonly BotStyle _champion;
    private readonly DefenseGenome _genome;
    private readonly DefenseTuner _tuner;
    private readonly IReadOnlyList<PunchKind> _kinds;

    public DefenseTarget(Defense defense, float seconds, BotStyle champion)
    {
        _defense = defense;
        _champion = champion;
        _genome = new DefenseGenome(defense, champion.Defenses.Of(defense).Validated());
        _tuner = new DefenseTuner(1, seconds, 0, _ => { });
        _kinds = DefenseTuner.Targets(defense);
    }

    public override string Name => $"defense:{_defense}";

    public override IReadOnlyList<Gene> Genes => _genome.Genes;

    public override string ScoreNote
        => $"1 標本 = 1 つの種で、守り {_defense} が受け持つ拳（{string.Join("・", _kinds)}）を打たせたドリルの差の点（守らない受け手の被ダメージ − 被ダメージ + 0.25 × 打ち返し）の和。受け手は champion の守りの表。";

    public override double[] Initial() => _genome.Initial();

    private DefenseBook Book(IReadOnlyList<double> genes) => _champion.Defenses.With(_defense, _genome.Decode(genes));

    public override double Point(IReadOnlyList<double> genes, int seed)
        => DefenseTuner.SeedPoints(_tuner.EvaluateBySeed(Book(genes), _defense, _kinds, seed))[0];

    public override BotStyle Overlay(IReadOnlyList<double> genes) => _champion with { Name = $"{_champion.Name}-cma-defense-{_defense}", Defenses = Book(genes) };

    public override string BookJson(IReadOnlyList<double> genes) => JsonSerializer.Serialize(Book(genes), CmaTune.Json);
}

/// <summary>
/// <c>cmatune</c>: CMA-ES（<see cref="CmaEs"/>）を、拳・守りのドリルの点につなぐ。
/// 同じ世代の候補はみな同じ種の組で測り（共通の乱数）、世代ごとに種を変える。結果は<b>データとして出すだけ</b>で、champion・判断・体の物理は変えない。
/// 最後に、使わなかった種で「始めの値」と「分布の平均 m（最良の個体ではない: 勝者の呪いを避ける）」を対にして比べ（<see cref="PairedEvaluation.Confirm"/>）、
/// 信頼区間の下限が 0 より上のときだけ「改善」と言う。さらに champion の技表のその技だけを替えたスタイルと元の champion を、試合の点（<see cref="MatchSampler"/>）で比べる。
/// 結果は並列の数に依らず決定的（添字の位置に書く）で、<c>--resume</c> で続けても続けて回した場合と同じ。
/// </summary>
internal sealed class CmaTune(CmaTuneConfig config, Action<string> log)
{
    /// <summary>候補数の既定（0 = 自動）: <c>evo_noise.md</c> の推奨（λ = 12）。</summary>
    public const int AutoLambda = 12;

    /// <summary>採否の有意水準（両側、1 つの対象あたり。対象が複数なら 0.05 / 対象の数）。</summary>
    public const double Alpha = 0.05;

    /// <summary>世代ごとの種と、使わなかった種（比べ直し・試合）の置き場。1000 以上離す（<see cref="MatchSampler"/> の都合）。</summary>
    private const int ConfirmBlock = 500_000;
    private const int MatchBlock = 600_000;

    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>拳のドリルの受け手に固定させる守りの組（型ごと）。<c>Defense.None</c>（固定しない）は使わない。</summary>
    public static IReadOnlyList<Defense> DefenseSet(string punch, IReadOnlyList<Defense>? explicitSet)
    {
        if (explicitSet is { Count: > 0 })
        {
            return explicitSet.Contains(Defense.None) ? throw new ArgumentException("--defenses に None は使えません。") : explicitSet;
        }

        return punch switch
        {
            "2" => [Defense.Block, Defense.Slip, Defense.Duck],
            "3" => [Defense.Block, Defense.Weave, Defense.Duck],
            "1b" => [Defense.Block, Defense.StepBack],
            _ => throw new ArgumentException($"拳の型 {punch} の守りの組が決まっていません。--defenses で明示してください（例: --defenses Block,Slip）。"),
        };
    }

    /// <summary>ドリルの受け手: champion の守りの表と構え。<c>video --drill --style champion --counters true</c> と同じ条件。</summary>
    public static BotStyle DrillDefender(BotStyle champion)
        => DefenseDrill.Defender(champion.Defenses, defend: true, DefenseDrill.DefaultRange, counters: true) with
        {
            Name = champion.Name,
            GuardHeight = champion.GuardHeight,
            GuardWidth = champion.GuardWidth,
            GuardReach = champion.GuardReach,
            Prediction = 0f,
        };

    public static CmaTarget MakeTarget(string text, CmaTuneConfig config, BotStyle champion)
    {
        var parts = text.Split(':', 2);
        if (parts.Length != 2 || parts[1].Length == 0)
        {
            throw new ArgumentException($"--target は punch:<型> か defense:<技> です（{text}）。");
        }

        switch (parts[0])
        {
            case "punch":
                return new PunchTarget(parts[1], DefenseSet(parts[1], config.Defenses), config.Seconds, champion);
            case "defense":
                if (!Enum.TryParse<Defense>(parts[1], ignoreCase: true, out var defense) || defense == Defense.None || !Enum.IsDefined(defense))
                {
                    throw new ArgumentException($"守りの技 '{parts[1]}' はありません（{string.Join(", ", DefenseBook.Techniques)}）。");
                }

                return new DefenseTarget(defense, config.Seconds, champion);
            default:
                throw new ArgumentException($"--target は punch:<型> か defense:<技> です（{text}）。");
        }
    }

    /// <summary>世代 <paramref name="generation"/>（0 から）の k 番目の標本の種。重ならないように 1000 ずつ離す。</summary>
    internal static int GenerationSeed(int seed, int generation, int samples, int k) => seed + (1000 * ((generation * samples) + k));

    private int Lambda => config.Lambda == 0 ? AutoLambda : config.Lambda;

    private ParallelOptions Options => new() { MaxDegreeOfParallelism = config.MaxParallel ?? Environment.ProcessorCount };

    /// <summary>全部の対象を順に回して、報告（Markdown）を返す。</summary>
    /// <param name="resumeBase">再開の状態のファイル（無ければ新規、あれば続ける）。対象が複数なら対象名を足す。</param>
    /// <param name="jsonBase">候補の本を書く先（<c>*.json</c>、同じ場所に <c>*.style.json</c>）。対象が複数なら対象名を足す。</param>
    public string Run(string? resumeBase, string? jsonBase)
    {
        var champion = BotCatalog.Champion;
        var sb = new StringBuilder();
        var total = Stopwatch.StartNew();
        var multiple = config.Targets.Count > 1;
        sb.AppendLine("# cmatune（CMA-ES でドリルの点を上げる）");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"設定: 世代 {config.Generations}、λ {Lambda}{(config.Lambda == 0 ? "（自動 = evo_noise.md の推奨）" : "")}、初めの σ {config.Sigma}、1 候補の標本 {config.Samples}、{config.Seconds:0.#} 秒、種 {config.Seed}、並列 {(config.MaxParallel?.ToString(CultureInfo.InvariantCulture) ?? "既定")}。受け手と基準は champion（champion-v1 ではない）。"));
        sb.AppendLine();
        if (multiple)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"**探索的**: 対象が {config.Targets.Count} つある。「改善」の判定は対象ごとの有意水準を {Alpha} / {config.Targets.Count} に下げて行うが、どれか 1 つの当たりを選ぶ使い方は探索的で、別の種での追試が要る。"));
            sb.AppendLine();
        }

        foreach (var text in config.Targets)
        {
            var target = MakeTarget(text, config, champion);
            var safe = target.Name.Replace(':', '_');
            sb.AppendLine(RunTarget(target, champion, multiple ? Suffix(resumeBase, safe) : resumeBase, multiple ? Suffix(jsonBase, safe) : jsonBase,
                multiple ? Alpha / config.Targets.Count : Alpha, multiple));
        }

        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"全体の壁時計: {total.Elapsed.TotalMinutes:0.0} 分（このプロセスの分。{Environment.ProcessorCount} コア）。"));
        return sb.ToString();
    }

    private static string? Suffix(string? path, string tag)
        => path is null ? null : Path.Combine(Path.GetDirectoryName(path) ?? "", $"{Path.GetFileNameWithoutExtension(path)}_{tag}{Path.GetExtension(path)}");

    private string RunTarget(CmaTarget target, BotStyle champion, string? resumePath, string? jsonPath, double alpha, bool exploratory)
    {
        // 学習の種（seed + 1000 × (世代 × 標本 + k)）が比べ直しの種（seed + 1000 × 500000〜）・試合の種（600000〜）と重ならず、int があふれない範囲か（レビュー指摘）。
        if ((long)config.Generations * config.Samples >= 500_000 || config.Seed is < 0 or > 1_000_000_000)
        {
            throw new ArgumentException($"世代 × 標本（{config.Generations} × {config.Samples}）は 500000 未満、種（{config.Seed}）は 0〜1e9 にしてください（種が比べ直しの種と重なるため）。");
        }

        var watch = Stopwatch.StartNew();
        var meterStart = DrillMeter.Total;
        var defensesKey = config.Defenses?.Select(d => d.ToString()).ToArray() ?? [];
        var state = LoadState(resumePath, target, defensesKey);
        var cma = state is null
            ? new CmaEs(target.Initial(), config.Seed, config.Sigma, maximize: true, lambda: Lambda)
            : CmaEs.LoadState(state.Cma);
        var rows = state?.Rows.ToList() ?? [];
        var carryCalls = state?.BudgetCalls ?? 0;
        var carrySeconds = state?.BudgetSeconds ?? 0d;
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## {target.Name}{(exploratory ? "（探索的）" : "")}");
        sb.AppendLine();
        sb.AppendLine(target.ScoreNote);
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"遺伝子（{target.Genes.Count} 次元）: {string.Join("・", target.Genes.Select(g => $"{g.Name} [{g.Lo:0.###}, {g.Hi:0.###}]"))}。"));
        sb.AppendLine();
        if (state is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"（{resumePath} の状態から世代 {cma.Generation} の続き）");
            sb.AppendLine();
        }

        log($"[{target.Name}] 世代 {cma.Generation + 1} 〜 {config.Generations}、λ {cma.Lambda}、標本 {config.Samples}");
        while (cma.Generation < config.Generations && cma.StopReason is CmaStop.None)
        {
            var generation = cma.Generation;
            var sigma = cma.Sigma;
            var candidates = cma.Ask();
            var seeds = Enumerable.Range(0, config.Samples).Select(k => GenerationSeed(config.Seed, generation, config.Samples, k)).ToArray();
            var scores = Score(target, candidates, seeds);
            cma.Tell(scores);
            var row = new CmaTuneRow(cma.Generation, sigma, scores.Max(), scores.Average(), scores.Min(), cma.Best);
            rows.Add(row);
            var now = DrillMeter.Total;
            log(string.Create(CultureInfo.InvariantCulture,
                $"[{target.Name}] 世代 {row.Generation}: σ {row.Sigma:0.0000} 最良 {row.Best:0.000} 平均 {row.Mean:0.000} 最悪 {row.Worst:0.000}（{watch.Elapsed.TotalMinutes:0.0} 分）"));
            if (resumePath is not null)
            {
                SaveState(resumePath, new CmaTuneState
                {
                    Target = target.Name,
                    Defenses = defensesKey,
                    Samples = config.Samples,
                    Seconds = config.Seconds,
                    Seed = config.Seed,
                    Lambda = cma.Lambda,
                    Sigma = config.Sigma,
                    Initial = target.Initial(),
                    Cma = cma.SaveState(),
                    Rows = rows,
                    BudgetCalls = carryCalls + (now.Calls - meterStart.Calls),
                    BudgetSeconds = carrySeconds + (now.Seconds - meterStart.Seconds),
                });
            }
        }

        sb.AppendLine("| 世代 | σ | 最良 | 平均 | 最悪 | 平均 m の値 |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---|");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {r.Generation} | {r.Sigma:0.0000} | {r.Best:0.000} | {r.Mean:0.000} | {r.Worst:0.000} | {target.Values(r.M)} |"));
        }

        sb.AppendLine();
        sb.AppendLine("**世代の点は「その世代の種での比べ」で、世代の間では比べられない**（世代ごとに種を変えるので、点の水準が世代で動く）。止めた理由: "
            + (cma.StopReason is CmaStop.None ? "世代の数に届いた" : cma.StopReason.ToString()) + "。");
        sb.AppendLine();

        var m = cma.Best;
        var start = target.Initial();
        sb.AppendLine(Confirm(target, champion, start, m, alpha));
        var budgetNow = DrillMeter.Total;
        var calls = carryCalls + (budgetNow.Calls - meterStart.Calls);
        var simSeconds = carrySeconds + (budgetNow.Seconds - meterStart.Seconds);
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"### 予算（`DefenseDrill.Run`・`DefenseDrill.Punch`・`MeasureBaseline` を呼んだ回数 × 秒数。キャッシュで呼ばなかった分は数えない）\n\n{calls} 回、合計 {simSeconds:0} 秒（シミュレーション上。最後の比べ直しのドリルの分を含み、試合の確認は含まない）。このプロセスの壁時計 {watch.Elapsed.TotalMinutes:0.0} 分。\n"));

        if (jsonPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
            File.WriteAllText(jsonPath, target.BookJson(m));
            var stylePath = Path.ChangeExtension(jsonPath, ".style.json");
            File.WriteAllText(stylePath, target.Overlay(m).ToJson());
            sb.AppendLine(CultureInfo.InvariantCulture, $"候補（分布の平均 m）の本: `{jsonPath}`、champion に重ねたスタイル: `{stylePath}`。");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>候補ごとの点 = 標本の平均（共通の種）。結果は添字の位置に書くので並列の数に依らず決定的。</summary>
    private double[] Score(CmaTarget target, double[][] candidates, int[] seeds)
    {
        var points = new double[candidates.Length * seeds.Length];
        Parallel.For(0, points.Length, Options, i => points[i] = target.Point(candidates[i / seeds.Length], seeds[i % seeds.Length]));
        var scores = new double[candidates.Length];
        for (var c = 0; c < scores.Length; c++)
        {
            scores[c] = points.Skip(c * seeds.Length).Take(seeds.Length).Average();
        }

        return scores;
    }

    private double[] Points(CmaTarget target, double[] genes, IReadOnlyList<int> seeds)
    {
        var points = new double[seeds.Count];
        Parallel.For(0, points.Length, Options, i => points[i] = target.Point(genes, seeds[i]));
        return points;
    }

    private string Confirm(CmaTarget target, BotStyle champion, double[] start, double[] m, double alpha)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### 最後の比べ直し（使わなかった種・固定の n）");
        sb.AppendLine();
        if (config.ConfirmN >= 2)
        {
            var seeds = Enumerable.Range(0, config.ConfirmN).Select(k => config.Seed + (1000 * (ConfirmBlock + k))).ToArray();
            var result = PairedEvaluation.Confirm(s => Points(target, m, s), s => Points(target, start, s), seeds, alpha);
            ConfirmLog.Append("cmatune", $"drill points of {target}", "m: " + target.Values(m), "start: " + target.Values(start), seeds, result);
            var improved = result.CiLow > 0d;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"始めの値（champion）と、分布の平均 m（最良の個体ではない）を同じ種で対にした差の点: {result}（A = m、B = 始めの値、{(1d - alpha) * 100d:0.#}% 区間）。"));
            sb.AppendLine();
            sb.AppendLine(improved
                ? "→ **改善**（信頼区間の下限が 0 より上）。"
                : result.CiHigh < 0d ? "→ **悪化**（信頼区間の上限が 0 より下）。" : "→ 改善とは言えない（信頼区間が 0 を含む）。");
            sb.AppendLine();
            sb.AppendLine("始めの値: " + target.Values(start));
            sb.AppendLine();
            sb.AppendLine("m の値: " + target.Values(m));
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("（行わない: --confirm-n が 2 未満）");
            sb.AppendLine();
        }

        sb.AppendLine("### 試合でも悪くならないか（champion の技表のその技だけを替えたスタイル 対 元の champion）");
        sb.AppendLine();
        if (config.MatchN >= 2)
        {
            var opponents = NoiseLab.Opponents.Select(Contender.Named).ToList();
            var sampler = new MatchSampler(opponents, config.MatchSeconds, config.MaxParallel);
            var seeds = Enumerable.Range(0, config.MatchN).Select(k => config.Seed + (1000 * (MatchBlock + k))).ToArray();
            var changed = Contender.Of(target.Overlay(m));
            var original = Contender.Of(champion);
            var result = PairedEvaluation.Confirm(s => sampler.Score(changed, s), s => sampler.Score(original, s), seeds, alpha);
            ConfirmLog.Append("cmatune", $"match score with {target} replaced (vs {string.Join(",", NoiseLab.Opponents)}, {config.MatchSeconds:0} s)",
                "champion with m: " + target.Values(m), "champion", seeds, result);
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"相手 {string.Join("・", NoiseLab.Opponents)} × 両コーナー、{config.MatchSeconds:0} 秒、n = {config.MatchN}（点は `MatchScore.Of` の 1 標本ぶん。A = 替えたスタイル、B = champion）: {result}"));
            sb.AppendLine();
            sb.AppendLine(result.CiHigh < 0d
                ? "→ **ドリルで良くても試合では悪い**（区間が 0 より下）。採るなら別の理由が要る。"
                : result.CiLow > 0d ? "→ 試合でも上がった（区間が 0 より上）。" : "→ 試合では差が言えない（区間が 0 を含む）。悪くなったとも言えない。");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("（行わない: --match-n が 2 未満）");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private CmaTuneState? LoadState(string? path, CmaTarget target, string[] defensesKey)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        var state = JsonSerializer.Deserialize<CmaTuneState>(File.ReadAllText(path), Json) ?? throw new JsonException($"{path} が空です。");
        var same = state.Target == target.Name && state.Samples == config.Samples && state.Seconds == config.Seconds && state.Seed == config.Seed
            && state.Defenses.SequenceEqual(defensesKey)
            && state.Lambda == Lambda && state.Sigma == config.Sigma && state.Initial.SequenceEqual(target.Initial());
        return same
            ? state
            : throw new InvalidOperationException($"{path} は別の設定の状態です（対象 {state.Target}、標本 {state.Samples}、{state.Seconds} 秒、種 {state.Seed}、λ {state.Lambda}、σ {state.Sigma}。champion の技の値が変わっても別の設定とみなす）。同じ設定で再開してください。");
    }

    private static void SaveState(string path, CmaTuneState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, path, overwrite: true);
    }
}
