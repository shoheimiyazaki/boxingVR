using System.Globalization;
using MiniRing.Arena.Evolution;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>1 回の狩り（<see cref="HuntLab"/>）。</summary>
/// <param name="Victim">受け手（名簿の人間）の id。</param>
/// <param name="Seed">試合の種。</param>
/// <param name="Killed">打ち切りまでに受け手が死んだか（VFC の決着）。</param>
/// <param name="Seconds">死ぬまでの時間 [s]（死ななければ打ち切りの時間）。</param>
/// <param name="DownAt">受け手が初めて倒れた時刻 [s]（倒れなければ null）。</param>
/// <param name="Cause">死因（<see cref="Fighter.DeathCause"/>。死ななければ "-"）。</param>
/// <param name="Removed">受け手の体から失われたボクセルの数。</param>
/// <param name="Lethal">死への近さ 0..1（<see cref="HuntLab.Lethality"/>。死んだら 1）。</param>
internal sealed record HuntRun(string Victim, int Seed, bool Killed, float Seconds, float? DownAt, string Cause, int Removed, float Lethal);

/// <summary>
/// 狩りのドリル（2026-11-25）: 名簿のキャラ（既定は四足の <c>galva</c>）が、VFC の檻（レフェリーなし・ダウンを数えない・死だけが決着）で、
/// 素手で抵抗しない人間（<see cref="PassiveControl"/>: 立って相手を向き、腕を下ろしたまま）を倒すまでの時間を測る。
/// 攻め手の教育（スタイルの値）だけを変え、体・材料・相手は変えない。中央から始める（<see cref="FinishDrill"/> と同じ）。
/// 歩の幅は動画と同じ（30 fps・<see cref="GameSpeed.Standard"/> の速さのコマを 1/60 s 以下に割る）なので、同じ種の動画は測った 1 回と同じ試合になる。
/// </summary>
internal static class HuntLab
{
    public const string DefaultHunter = "galva";

    /// <summary>真なら 1 回ごとに、0.5 s おきの様子を書く（<c>--trace</c>）。</summary>
    public static TextWriter? Trace { get; set; }

    /// <summary>練習の相手（体格の違う 3 人: 145 cm 61 kg・164 cm 125 kg・153 cm 72 kg）。</summary>
    public static readonly string[] Practice = ["jin", "marcus", "daichi"];

    /// <summary>比べ直しの相手（練習に使わない 3 人: 132 cm 63 kg・174 cm 126 kg・153 cm 102 kg）。</summary>
    public static readonly string[] Holdout = ["ramon", "konrad", "sean"];

    private const int Fps = 30;

    public static float StepDt
    {
        get
        {
            var frameDt = GameSpeed.Standard / Fps;
            return frameDt / Math.Max(1, (int)MathF.Ceiling((frameDt / (1f / 60f)) - 1e-3f));
        }
    }

    /// <summary>打ち切りの時間を 1 ラウンドに収めた VFC の決まり（休み・再生が入らない）。</summary>
    public static MatchRules Rules(float seconds) => MatchRules.Vfc with { RoundSeconds = seconds + 2f, Rounds = 1 };

    public static Contender Victim => new("victim", _ => new PassiveControl());

    public static Contender HunterOf(BotStyle style) => new(style.Name, s => BotCatalog.Bot(style, s));

    /// <summary>檻の中央から始める（動画の <c>editCorners</c> と同じ）・受け手は素手。</summary>
    public static BoxingCorners EditCorners(BoxingCorners c) => c with { PlayerWear = HandWear.None, Ring = c.Ring! with { FromCorners = false } };

    /// <summary>攻め手の教育: 名簿の教育（受け手を相手にした解決）に、<paramref name="set"/> の値を重ねる。</summary>
    public static BotStyle Style(string hunterId, string victimId, IReadOnlyDictionary<string, float>? set)
    {
        var style = CharacterRoster.Get(hunterId).Resolve(victimId);
        return set is null || set.Count == 0 ? style : StyleSet.Apply(style, set);
    }

    public static HuntRun Run(string hunterId, string victimId, int seed, float seconds, IReadOnlyDictionary<string, float>? set = null)
    {
        var hunter = CharacterRoster.Get(hunterId);
        var victim = CharacterRoster.Get(victimId);
        var style = Style(hunterId, victimId, set);
        var red = Victim;
        var blue = HunterOf(style);
        var corners = VideoMaker.Corners(red, blue, seed, HandWear.None, hunter.Wear, null, hunter.Weapon, s => blue.Create(s), s => red.Create(s), referee: false);
        corners = EditCorners(TvShowRules.Stage(VideoMaker.WithCharacters(corners, victim, hunter), Organizations.Get("vfc")));
        using var game = new BoxingGame(seed: seed, corners: corners, rules: Rules(seconds));
        var body = game.Player;
        var cells0 = Filled(body);
        float? downAt = null;
        var dt = StepDt;
        var t = 0f;
        while (t < seconds && !body.IsDead)
        {
            game.Step(default, dt);
            t += dt;
            if (downAt is null && body.State != FighterState.Standing)
            {
                downAt = t;
            }

            if (Trace is { } trace && ((int)MathF.Round(t / dt)) % 30 == 0)
            {
                var g = (game.CpuControl as GroundStriker)?.Ground;
                trace.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  t={t:0.0} victim={body.State} hunter={game.Cpu.State} plan={g?.CurrentName ?? "-"} started={g?.Started ?? 0} dist={g?.Distance:0.00} sink={g?.Sink:0.00} lethal={Lethality(body):0.00} conscious={body.Consciousness:0.00} removed={cells0 - Filled(body)}"));
            }
        }

        return new HuntRun(victimId, seed, body.IsDead, body.IsDead ? t : seconds, downAt, body.DeathCause ?? "-", cells0 - Filled(body), Lethality(body));
    }

    /// <summary>
    /// 死への近さ 0..1（測る側の物差し。ボットの判断には使わない）: 死んだら 1、生きていれば、致命の材質（脳・心臓などの臓器）ごとの
    /// 「失った割合 / 死ぬ割合（<see cref="FighterTuning.OrganDeathFraction"/>）」の最大。首の切断・臓器の喪失と同じ物差し（<see cref="Voxels.BoxerBody.DeathCause"/>）。
    /// </summary>
    public static float Lethality(Fighter f)
    {
        if (f.IsDead)
        {
            return 1f;
        }

        if (f.Voxels is not { } body)
        {
            return 0f;
        }

        var palette = Voxels.BoxerAnatomy.Palette;
        var worst = 0f;
        for (var m = 1; m < palette.Count; m++)
        {
            if (!palette[(byte)m].Vital)
            {
                continue;
            }

            int left = 0, start = 0;
            foreach (var part in body.Parts)
            {
                left += part.Body.Grid.Remaining((byte)m);
                start += part.Body.Grid.Initial((byte)m);
            }

            worst = start > 0 ? MathF.Max(worst, (1f - ((float)left / start)) / new FighterTuning().OrganDeathFraction) : worst;
        }

        return Math.Clamp(worst, 0f, 1f);
    }

    private static int Filled(Fighter f) => f.Voxels is { } v ? v.Parts.Sum(p => p.Body.Grid.FilledCount) : 0;

    /// <summary>相手と種の全部の組を並列に回す（結果は相手・種の順）。</summary>
    public static HuntRun[] RunSet(string hunterId, IReadOnlyList<string> victims, IReadOnlyList<int> seeds, float seconds, IReadOnlyDictionary<string, float>? set, int parallel)
    {
        var cases = victims.SelectMany(v => seeds.Select(s => (v, s))).ToArray();
        var results = new HuntRun[cases.Length];
        Parallel.For(0, cases.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) },
            i => results[i] = Run(hunterId, cases[i].v, cases[i].s, seconds, set));
        return results;
    }

    /// <summary>
    /// 点（小さいほど良い）: 死んだ回は死ぬまでの時間。死ななかった回は、打ち切りの時間 × (2 − 死への近さ)（打ち切りより必ず悪く、死に近いほど良い）。
    /// 打ち切りの時間は <see cref="HuntRun.Seconds"/>（生きていればそれが打ち切り）。
    /// </summary>
    public static double Score(HuntRun r) => r.Killed ? r.Seconds : r.Seconds * (2d - r.Lethal);

    public static string Summary(IReadOnlyList<HuntRun> runs)
    {
        var times = runs.Select(r => (double)r.Seconds).OrderBy(x => x).ToArray();
        var median = times.Length == 0 ? double.NaN : times.Length % 2 == 1 ? times[times.Length / 2] : 0.5 * (times[(times.Length / 2) - 1] + times[times.Length / 2]);
        var downs = runs.Where(r => r.DownAt is not null).Select(r => (double)r.DownAt!.Value).OrderBy(x => x).ToArray();
        var downMedian = downs.Length == 0 ? double.NaN : downs[downs.Length / 2];
        return string.Create(CultureInfo.InvariantCulture,
            $"killed {runs.Count(r => r.Killed)}/{runs.Count}  time median {median:0.00} s  mean {times.DefaultIfEmpty().Average():0.00} s  (q1 {Quantile(times, 0.25):0.00}, q3 {Quantile(times, 0.75):0.00})  first down median {downMedian:0.00} s ({downs.Length}/{runs.Count})  lethal {runs.Average(r => r.Lethal):0.00}  score {runs.Average(Score):0.00}");
    }

    private static double Quantile(double[] sorted, double q)
    {
        if (sorted.Length == 0)
        {
            return double.NaN;
        }

        var x = q * (sorted.Length - 1);
        var lo = (int)Math.Floor(x);
        var hi = Math.Min(sorted.Length - 1, lo + 1);
        return sorted[lo] + ((sorted[hi] - sorted[lo]) * (x - lo));
    }

    public static void Write(TextWriter log, string label, IReadOnlyList<HuntRun> runs, bool detail)
    {
        log.WriteLine($"{label,-12} {Summary(runs)}");
        if (!detail)
        {
            return;
        }

        foreach (var g in runs.GroupBy(r => r.Victim))
        {
            log.WriteLine($"  {g.Key,-8} {Summary(g.ToList())}");
            foreach (var r in g)
            {
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"    seed {r.Seed,4}  {(r.Killed ? "killed" : "alive "),-6} {r.Seconds,6:0.00} s  down {(r.DownAt is { } d ? d.ToString("0.00", CultureInfo.InvariantCulture) : "-"),6}  removed {r.Removed,5}  lethal {r.Lethal:0.00}  {r.Cause}"));
            }
        }
    }

    /// <summary>探す値の 1 つ: スタイルの名前と範囲（正規化の [0, 1] を範囲へ写す）。</summary>
    internal readonly record struct Knob(string Name, float Min, float Max)
    {
        public float At(double u) => Min + ((Max - Min) * (float)Math.Clamp(u, 0d, 1d));

        public double Of(float value) => Math.Clamp((value - Min) / (Max - Min), 0f, 1f);

        public static Knob Parse(string text)
        {
            var p = text.Split(':');
            return p.Length == 3
                ? new Knob(p[0], float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture))
                : throw new ArgumentException($"--knobs は 名前:下限:上限 をカンマで並べます: '{text}'");
        }
    }

    /// <summary>
    /// CMA-ES で教育の値を探す（小さいほど良い = 死ぬまでの時間の平均）。世代ごとに種を変え、練習の相手 × 種で測る。
    /// 平均は名簿の今の値から始める。各世代の平均（分布の中心）も同じ種で測って記録する（世代の間の点は種が違うので比べない）。
    /// </summary>
    public static Dictionary<string, float> Tune(string hunterId, IReadOnlyList<Knob> knobs, IReadOnlyList<string> victims, int seedsPerGen, float seconds,
        int gens, int lambda, double sigma, int seed, int parallel, TextWriter log)
    {
        var start = Style(hunterId, victims[0], null);
        var json = System.Text.Json.Nodes.JsonNode.Parse(start.ToJson())!.AsObject();
        var mean0 = knobs.Select(k => k.Of(json[k.Name]!.GetValue<float>())).ToArray();
        var cma = new CmaEs(mean0, seed, sigma, maximize: false, lambda: lambda, maxGenerations: gens);
        log.WriteLine($"start {Describe(knobs, mean0)}");
        for (var g = 0; g < gens && cma.StopReason == CmaStop.None; g++)
        {
            var seeds = Enumerable.Range(0, seedsPerGen).Select(i => 1000 + (g * 97) + (i * 13)).ToArray();
            var candidates = cma.Ask();
            var all = candidates.Append(cma.Best).ToArray();
            var cases = all.SelectMany((c, ci) => victims.SelectMany(v => seeds.Select(s => (ci, v, s)))).ToArray();
            var results = new HuntRun[cases.Length];
            Parallel.For(0, cases.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) },
                i => results[i] = Run(hunterId, cases[i].v, cases[i].s, seconds, SetOf(knobs, all[cases[i].ci])));
            var scores = all.Select((_, ci) => results.Where((_, i) => cases[i].ci == ci).Average(Score)).ToArray();
            cma.Tell(scores[..candidates.Length]);
            var bestIndex = Array.IndexOf(scores, scores[..candidates.Length].Min());
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"gen {g,2}  mean-point {scores[^1]:0.00} s  best {scores[bestIndex]:0.00} s  sigma {cma.Sigma:0.000}  best: {Describe(knobs, all[bestIndex])}  center: {Describe(knobs, all[^1])}"));
            log.Flush();
        }

        return SetOf(knobs, cma.Best);
    }

    public static Dictionary<string, float> SetOf(IReadOnlyList<Knob> knobs, IReadOnlyList<double> u)
        => knobs.Select((k, i) => (k.Name, Value: k.At(u[i]))).ToDictionary(p => p.Name, p => p.Value);

    private static string Describe(IReadOnlyList<Knob> knobs, double[] u)
        => string.Join(", ", knobs.Select((k, i) => string.Create(CultureInfo.InvariantCulture, $"{k.Name}={k.At(u[i]):0.###}")));

    /// <summary>
    /// 比べ直し: 今の名簿の教育（A）と <paramref name="set"/> を重ねた教育（B）を、選ぶのに使わなかった相手と種で対にして比べる
    /// （<see cref="PairedEvaluation.Confirm"/>。差 = A − B なので、正なら B が速い）。結果は <c>docs/arena/confirm.jsonl</c> に残す。
    /// </summary>
    public static PairedResult Confirm(string hunterId, IReadOnlyDictionary<string, float> set, IReadOnlyList<string> victims, IReadOnlyList<int> seeds, float seconds, int parallel, TextWriter log)
    {
        // 1 標本 = 相手 × 種の 1 組（同じ組を A と B で対にする）。
        var baseline = RunSet(hunterId, victims, seeds, seconds, null, parallel);
        var trained = RunSet(hunterId, victims, seeds, seconds, set, parallel);
        var result = PairedEvaluation.Compute(baseline.Select(Score).ToArray(), trained.Select(Score).ToArray());
        Write(log, "baseline", baseline, detail: true);
        Write(log, "trained", trained, detail: true);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"paired n={result.N}: baseline {result.MeanA:0.00} s, trained {result.MeanB:0.00} s, diff {result.MeanDiff:0.00} s (95% CI {result.CiLow:0.00} .. {result.CiHigh:0.00}), sign test p={result.SignP:0.0000} (+{result.Plus}/-{result.Minus})"));
        var pairSeeds = victims.SelectMany((_, vi) => seeds.Select(s => (vi * 100000) + s)).ToArray();
        ConfirmLog.Append("huntlab", $"{hunterId} の教育で、素手で抵抗しない人間が死ぬまでの時間（打ち切り {seconds:0} s）が縮むか",
            "roster", string.Join(",", set.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key}={p.Value:0.###}"))), pairSeeds, result,
            new Dictionary<string, string> { ["victims"] = string.Join(",", victims), ["seeds"] = string.Join(",", seeds), ["baseline"] = Summary(baseline), ["trained"] = Summary(trained) });
        return result;
    }

    public static void Video(string hunterId, string victimId, int seed, float seconds, IReadOnlyDictionary<string, float>? set, string output, TextWriter console)
    {
        var hunter = CharacterRoster.Get(hunterId);
        var victim = CharacterRoster.Get(victimId);
        var style = Style(hunterId, victimId, set);
        new VideoMaker(1280, 720, Fps, org: Organizations.Get("vfc"), rules: Rules(seconds)).Make(Victim, HunterOf(style), seed, seconds, output, console,
            redWear: HandWear.None, blueWear: hunter.Wear, blueWeapon: hunter.Weapon, redCharacter: victim, blueCharacter: hunter, referee: false,
            editCorners: EditCorners);
    }

    /// <summary>
    /// <c>huntlab</c> の入口。既定は練習の相手 × 種で今の教育を測る。<c>--set K=V,...</c> で値を重ねて測る、<c>--tune</c> で CMA-ES、
    /// <c>--confirm</c> で比べ直し（<c>--set</c> の教育を比べ直しの相手 × 種で）、<c>--video out.mp4 --victim id --seed n</c> で動画。
    /// </summary>
    public static int Command(Options o)
    {
        var hunter = o.Get("hunter") ?? DefaultHunter;
        var seconds = o.Float("seconds", 30f, 1f);
        var parallel = o.Int("parallel", Environment.ProcessorCount, 1);
        var set = ParseSet(o.Get("set"));
        using var file = o.Get("log") is { } path ? new StreamWriter(path, append: true) : null;
        TextWriter log = file is null ? Console.Out : new Tee(Console.Out, file);
        Trace = o.Get("trace") is not null ? Console.Out : null;
        if (o.Get("video") is { } video)
        {
            Video(hunter, o.Get("victim") ?? Practice[0], o.Int("seed", 1), seconds, set, video, Console.Out);
            return 0;
        }

        if (o.Get("tune") is not null)
        {
            var knobs = o.List("knobs", []).Select(Knob.Parse).ToArray();
            var best = Tune(hunter, knobs, o.List("victims", Practice), o.Int("seeds", 3, 1), seconds, o.Int("gens", 12, 1), o.Int("lambda", 8, 2),
                o.Float("sigma", 0.2f, 0.001f), o.Int("seed", 1), parallel, log);
            log.WriteLine("best --set " + string.Join(",", best.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key}={p.Value:0.###}"))));
            return 0;
        }

        var seeds = o.List("seeds", ["1", "2", "3", "4"]).Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        if (o.Get("confirm") is not null)
        {
            Confirm(hunter, set, o.List("victims", Holdout), seeds, seconds, parallel, log);
            return 0;
        }

        Write(log, set.Count == 0 ? "roster" : "set", RunSet(hunter, o.List("victims", Practice), seeds, seconds, set, parallel), detail: true);
        return 0;
    }

    private static Dictionary<string, float> ParseSet(string? text)
        => (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('='))
            .ToDictionary(p => p[0], p => p.Length == 2 ? float.Parse(p[1], CultureInfo.InvariantCulture) : throw new ArgumentException($"--set は 名前=値: '{string.Join("=", p)}'"));

    private sealed class Tee(TextWriter a, TextWriter b) : TextWriter
    {
        public override System.Text.Encoding Encoding => a.Encoding;

        public override void Write(char value)
        {
            a.Write(value);
            b.Write(value);
        }

        public override void WriteLine(string? value)
        {
            a.WriteLine(value);
            b.WriteLine(value);
        }

        public override void Flush()
        {
            a.Flush();
            b.Flush();
        }
    }
}
