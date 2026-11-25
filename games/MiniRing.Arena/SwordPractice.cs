using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Motion;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 剣士の型の練習（swordpractice、2026-11-26、ユーザーの指定「剣 3 種類のトレーニング: 無抵抗な人間を 1 撃で無力化できるように・振りを速く」）。
/// キャラ（刀の迅・ロングソードのコンラート・片手剣と盾のエドガー…）の体で、型を 1 回だけ、腕を下げて立つだけの相手（<see cref="GuardPosture.Defenseless"/>）へ出し、
/// 型の経路の差分（<see cref="FormTune"/>: 時計の速さ・ひねり・両手の高さ・頭の出し・間合い）を (1+λ) 進化戦略で磨く。
/// <para>
/// 点 = 10 × (1 − 振り終えて <see cref="After"/> 秒後の相手の意識) + 3 × (1 − 途中の意識の最低) + 0.5 × 刃先の最速 [m/s] − 形の代価（<see cref="FormCost"/>:
/// 手本からずらした距離・体が追えなかった距離・刃筋・スタミナ） − <see cref="PerSelfCell"/> × 自分の体から削ったセル。
/// 刃先の最速は、構えから型の始めへ移る最初の <see cref="SkipSeconds"/> 秒を数えない（どの型でも同じ移りの速さが出て、振りの速さを表さない）。意識は脳の残り（斬られた脳）と脳しんとうから出て、死ねば 0（<see cref="Fighter.Consciousness"/>）。
/// どの項も物理の量の連続な関数で、型の種類で分けない（CLAUDE.md §1.3）。変えるのは経路だけで、体の強さは変えない（§1.7・§1.8）。
/// </para>
/// <para>
/// 最後に、練習に使わなかった種と、使わなかった相手の体で、教科書の型と比べ直し（§1.8）、どちらも教科書以上で合計が正のときだけ採る。
/// </para>
/// </summary>
internal static class SwordPractice
{
    /// <summary>振り終えてから相手の様子を見る時間 [s]（倒れて意識が戻るかどうかが分かるまで）。</summary>
    public const float After = 3f;

    /// <summary>
    /// 自分の体から削った 1 セルあたりの代価。教科書のロングソードの打ち下ろしは、相手が先に倒れると振り抜いた刃が自分の脚を 300〜500 セル削った
    /// （swordpractice の基準の表・動画）。相手を倒す 1 発（10 点）より自分の脚を断つ方が重いように、400 セルで 8 点。
    /// </summary>
    public const double PerSelfCell = 0.02;

    /// <summary>刃先の最速を数え始める、技の始めからの時間 [s]。</summary>
    public const float SkipSeconds = 0.15f;

    /// <summary>無力化とみなす意識（体の張りが抜けて崩れる値。<see cref="FighterTuning.LimpConsciousness"/>）。</summary>
    public static float OutCold => FighterTuning.Default.LimpConsciousness;

    /// <summary>1 回の振りの結果。</summary>
    internal sealed record Outcome(bool Swung, float ConsciousnessEnd, float ConsciousnessMin, bool Dead, float TipPeak, float HandRmsCm, float EdgeDeg, float Stamina, int Cells, int SelfCells, bool SelfDown)
    {
        public bool Incapacitated => Swung && (Dead || ConsciousnessEnd < OutCold);
    }

    /// <summary>練習の相手の体（名前 → 体格）。standard は基準の体、heavy は大きく筋肉の太い体（コンラートの体格）。</summary>
    public static BodyBuild Victim(string name) => name switch
    {
        "standard" => BodyBuild.Standard,
        "heavy" => CharacterRoster.Get("konrad").Build,
        _ => CharacterRoster.Get(name).Build,
    };

    private static readonly ConcurrentDictionary<string, float> ArmReaches = new();

    /// <summary>キャラ <paramref name="who"/> の体の腕の長さ [m]（技の間合いを出すのに使う。体を一度作って読む）。</summary>
    public static float ArmReachOf(FighterCharacter who) => ArmReaches.GetOrAdd(who.Id, _ =>
    {
        using var game = new BoxingGame(corners: SwordTraining.Corners(who.Weapon!.Value, 1.5f, who.Wear, SwordTraining.Defenseless) with { PlayerBody = who.Build });
        return game.Player.ArmReach;
    });

    /// <summary>型 <paramref name="form"/> に <paramref name="tune"/> を掛けた技（間合いは差分を掛けた後の動きから）。</summary>
    public static MotionTechnique Technique(FighterCharacter who, string form, FormTune tune) =>
        FormSwordsman.Techniques(who.Weapon!.Value, ArmReachOf(who), float.NegativeInfinity, new Dictionary<string, FormTune> { [form] = tune })
            .First(t => t.Name == form);

    /// <summary>手本の経路からずらした距離 [cm]（刃先の通り道がどれだけ動くかの見積り。時計の速さは道を変えないので数えない）。</summary>
    public static float TextbookCentimetres(FighterCharacter who, string form, FormTune tune)
    {
        var weapon = who.Weapon!.Value;
        var arm = ArmReachOf(who);
        var motion = FormSwordsman.Forms(weapon).First(f => f.Name == form);
        var maxTwist = motion.Frames.Max(f => MathF.Abs(f.Twist));
        var blade = Weapon.TipOf(weapon).X - Weapon.GripOf(weapon).X + arm;
        return 100f * ((arm * (MathF.Abs(tune.Lift) + MathF.Abs(tune.Lean))) + MathF.Abs(tune.Reach) + (MathF.Abs(tune.Twist - 1f) * maxTwist * blade));
    }

    /// <summary>キャラ <paramref name="who"/> が型を 1 回、無防備に立つ <paramref name="victim"/> の体へ出す（種 <paramref name="seed"/>）。</summary>
    public static Outcome Swing(FighterCharacter who, WeaponStyle style, MotionTechnique technique, int seed, BodyBuild victim, Action<BoxingGame, float, bool>? watch = null)
    {
        var weapon = who.Weapon!.Value;
        var arm = ArmReachOf(who);
        var tuning = FighterTuning.Default;
        var offhand = Weapon.CoverRadiusOf(HandWears.Kind(who.Wear)) / arm;
        var bot = new SwordsmanBot([technique], FormSwordsman.Guard(weapon), seed, SwordStance.Rate, rest: 1000f, exact: true, tuning: tuning,
            defense: style.Defense, bladeLength: FormSwordsman.BladeLength(weapon, arm), spacing: FormSwordsman.Spacing(weapon), aim: style.Aim)
        {
            Clearance = style.Clearance,
            Pacing = style.Pacing,
            RearLength = FormSwordsman.RearLength(weapon, arm),
            OffhandRadius = offhand,
        };
        var corners = SwordTraining.Corners(weapon, Math.Clamp(technique.Range, SwordTraining.MinGap, SwordTraining.Ring.Size - 1.127f), who.Wear, SwordTraining.Defenseless) with
        {
            Player = _ => bot,
            PlayerBody = who.Build,
            CpuBody = victim,
        };
        using var game = new BoxingGame(tuning, seed: seed, corners: corners);
        var me = game.Player;
        var foe = game.Cpu;
        var sword = game.WeaponOf(me)!;
        var tip = Weapon.TipOf(sword.Kind);
        var foeStart = foe.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var ownStart = me.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
        var dt = 1f / 240f;
        var started = false;
        var endAt = float.PositiveInfinity;
        var staminaStart = me.Stamina;
        var cMin = 1f;
        var tipPeak = 0f;
        double handSq = 0;
        var handSamples = 0;
        var swing = new List<(float Speed, float Off)>();
        var selfDown = false;
        var activeSince = 0f;
        for (var t = 0f; t < 14f && t < endAt; t += dt)
        {
            game.Step(default, dt);
            var active = bot.Current is not null;
            watch?.Invoke(game, t, active);
            if (active && !started)
            {
                started = true;
                staminaStart = me.Stamina;
                activeSince = t;
            }
            else if (!active && started && float.IsPositiveInfinity(endAt))
            {
                endAt = t + After;
            }

            cMin = MathF.Min(cMin, foe.Consciousness);
            if (!started && t > 6f)
            {
                break;
            }

            if (!active)
            {
                continue;
            }

            selfDown |= me.State != FighterState.Standing;
            var state = game.World.GetState(sword.Body.BodyId);
            var velocity = state.LinearVelocity + Vector3.Cross(state.AngularVelocity, Vector3.Transform(tip, state.Orientation));
            if (t - activeSince >= SkipSeconds)
            {
                tipPeak = MathF.Max(tipPeak, velocity.Length());
            }

            var local = Vector3.Transform(velocity, Quaternion.Conjugate(state.Orientation));
            var off = local.Length() < 1e-4f ? float.NaN : MathF.Asin(Math.Clamp(MathF.Abs(local.Z) / local.Length(), 0f, 1f)) * 180f / MathF.PI;
            swing.Add((velocity.Length(), off));
            var error = Vector3.Distance(me.HandTargetWorld(Hand.Right), me.GloveCenter(Hand.Right));
            handSq += error * error;
            handSamples++;
        }

        var strike = swing.Where(s => s.Speed >= 0.5f * tipPeak && !float.IsNaN(s.Off)).ToList();
        var weight = strike.Sum(s => s.Speed);
        return new Outcome(started, foe.Consciousness, cMin, foe.IsDead, tipPeak,
            handSamples == 0 ? 0f : 100f * MathF.Sqrt((float)(handSq / handSamples)), weight > 0f ? strike.Sum(s => s.Off * s.Speed) / weight : 0f,
            started ? MathF.Max(0f, staminaStart - me.Stamina) : 0f,
            foeStart - foe.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount), ownStart - me.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount), selfDown);
    }

    /// <summary>動画（<see cref="SwingVideo"/>）の 1 振り: キャラ <paramref name="who"/> が型 <paramref name="form"/> を差分 <paramref name="tune"/> で、無防備の相手へ 1 回。</summary>
    internal static SwingVideo.Shot Shot(FighterCharacter who, string form, FormTune tune, string label, int seed, string victim)
    {
        var style = who.WeaponStyle ?? throw new ArgumentException($"{who.Id} は武器のボットを持ちません。");
        return new SwingVideo.Shot($"{who.Name}・{form}", label, Describe(tune), afterStep =>
        {
            var o = Swing(who, style, Technique(who, form, tune), seed, Victim(victim), afterStep);
            return string.Create(CultureInfo.InvariantCulture,
                $"{who.Id} {form} {label}: consciousness {o.ConsciousnessEnd:0.00} (min {o.ConsciousnessMin:0.00}) dead {o.Dead} tip {o.TipPeak:0.0} cells {o.Cells} self {o.SelfCells}");
        });
    }

    /// <summary>1 回の振りの点（クラスの説明の式）。振れなかった振りは −10。</summary>
    public static double Score(Outcome o, float textbookCm) => !o.Swung ? -10d
        : (10d * (1d - o.ConsciousnessEnd)) + (3d * (1d - o.ConsciousnessMin)) + (0.5d * o.TipPeak) - FormCost.Of(textbookCm, o.HandRmsCm, o.EdgeDeg, o.Stamina) - (PerSelfCell * o.SelfCells);

    /// <summary>型 1 つの、条件の全部（種 × 相手の体）の結果。</summary>
    internal sealed record Trial(FormTune Tune, double Score, IReadOnlyList<Outcome> Outcomes)
    {
        public int Incapacitated => Outcomes.Count(o => o.Incapacitated);
        public float TipPeak => Outcomes.Count == 0 ? 0f : Outcomes.Average(o => o.TipPeak);
    }

    /// <summary>型 <paramref name="form"/> を <paramref name="tune"/> で、種 × 体の全部で振った結果（並列）。</summary>
    public static Trial Evaluate(FighterCharacter who, WeaponStyle style, string form, FormTune tune, IReadOnlyList<int> seeds, IReadOnlyList<string> victims, int parallel)
        => EvaluateMany(who, style, form, [tune], seeds, victims, parallel)[0];

    /// <summary>差分 <paramref name="tunes"/> の全部を、種 × 体の全部で振った結果（全部の振りを 1 つの並列にまとめる）。</summary>
    public static IReadOnlyList<Trial> EvaluateMany(FighterCharacter who, WeaponStyle style, string form, IReadOnlyList<FormTune> tunes, IReadOnlyList<int> seeds,
        IReadOnlyList<string> victims, int parallel)
    {
        var techniques = tunes.Select(t => Technique(who, form, t)).ToList();
        var jobs = tunes.SelectMany((_, k) => seeds.SelectMany(s => victims.Select(v => (Tune: k, Seed: s, Victim: v)))).ToList();
        var results = new Outcome[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel },
            i => results[i] = Swing(who, style, techniques[jobs[i].Tune], jobs[i].Seed, Victim(jobs[i].Victim)));
        return
        [
            .. tunes.Select((t, k) =>
            {
                var cm = TextbookCentimetres(who, form, t);
                var mine = jobs.Select((j, i) => (j, i)).Where(x => x.j.Tune == k).Select(x => results[x.i]).ToList();
                return new Trial(t, mine.Average(o => Score(o, cm)), mine);
            }),
        ];
    }

    /// <summary>差分を少し変えた候補（幅 <paramref name="step"/> は、各値のおよその可動幅に対する割合）。</summary>
    public static FormTune Mutate(FormTune t, float step, Random random)
    {
        float N() => (float)(Math.Sqrt(-2d * Math.Log(1d - random.NextDouble())) * Math.Cos(2d * Math.PI * random.NextDouble()));
        return new FormTune(
            Math.Clamp(t.Tempo + (N() * step * 0.6f), 0.5f, 2.5f),
            Math.Clamp(t.Twist + (N() * step * 0.8f), 0f, 2.5f),
            Math.Clamp(t.Lift + (N() * step * 0.15f), -0.5f, 0.5f),
            Math.Clamp(t.Lean + (N() * step * 0.1f), -0.3f, 0.3f),
            Math.Clamp(t.Reach + (N() * step * 0.12f), -0.4f, 0.4f));
    }

    public static string Describe(FormTune t) => string.Create(CultureInfo.InvariantCulture,
        $"速さ ×{t.Tempo:0.00}・ひねり ×{t.Twist:0.00}・高さ {t.Lift:+0.00;-0.00}・前 {t.Lean:+0.00;-0.00}・間合い {t.Reach * 100f:+0;-0} cm");

    private static string Row(string label, Trial t) => string.Create(CultureInfo.InvariantCulture,
        $"| {label} | {Describe(t.Tune)} | {t.Score:0.0} | {t.Incapacitated}/{t.Outcomes.Count} | {t.Outcomes.Average(o => o.ConsciousnessEnd):0.00} | {t.Outcomes.Count(o => o.Dead)} | {t.TipPeak:0.0} | {t.Outcomes.Average(o => o.HandRmsCm):0.0} | {t.Outcomes.Average(o => o.EdgeDeg):0} | {t.Outcomes.Average(o => o.Cells):0} | {t.Outcomes.Average(o => o.SelfCells):0} |");

    private const string Header = "| | 差分 | 点 | 無力化 | 意識（後） | 死 | 刃先の最速 [m/s] | 手のずれ [cm] | 刃筋 [°] | 斬ったセル | 自分のセル |\n|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|";

    /// <summary>教科書の型の全部を、練習の条件で 1 回ずつ測る（どの型を練習するかを選ぶ表）。</summary>
    public static string Baseline(FighterCharacter who, IReadOnlyList<int> seeds, IReadOnlyList<string> victims, int parallel, TextWriter log)
    {
        var style = who.WeaponStyle ?? throw new ArgumentException($"{who.Id} は武器のボットを持ちません。");
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"### {who.Name}（{who.Id}、{who.Weapon}）の教科書の型（種 {string.Join(",", seeds)} × 相手 {string.Join(",", victims)}）");
        report.AppendLine();
        report.AppendLine(Header);
        foreach (var form in FormSwordsman.Forms(who.Weapon!.Value).Select(f => f.Name))
        {
            var current = style.Forms?.GetValueOrDefault(form) ?? FormTune.Identity;
            var trial = Evaluate(who, style, form, current, seeds, victims, parallel);
            var line = Row(form, trial);
            log.WriteLine(line);
            report.AppendLine(line);
        }

        return report.ToString();
    }

    /// <summary>
    /// 型 <paramref name="form"/> を練習する: (1+λ) 進化戦略（変える幅は 1/5 の決まりで広げ・狭める）を <paramref name="generations"/> 世代、
    /// 練習の種 × 相手の体で。最後に比べ直しの種 × 相手の体で、始めの差分と比べる。採ったら (差分, 報告)、採らなければ (null, 報告)。
    /// </summary>
    public static (FormTune? Adopted, string Report) Train(FighterCharacter who, string form, int generations, int lambda, float sigma, int seed,
        IReadOnlyList<int> seeds, IReadOnlyList<string> victims, IReadOnlyList<int> heldSeeds, IReadOnlyList<string> heldVictims, int parallel, TextWriter log)
    {
        var style = who.WeaponStyle ?? throw new ArgumentException($"{who.Id} は武器のボットを持ちません。");
        var watch = Stopwatch.StartNew();
        var random = new Random(seed ^ (who.Id + form).Sum(c => (int)c)); // 名前から決まる種（string.GetHashCode は実行ごとに変わる）
        var start = style.Forms?.GetValueOrDefault(form) ?? FormTune.Identity;
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"### {who.Name}（{who.Id}）の {form}: (1+{lambda}) × {generations} 世代、練習の種 {string.Join(",", seeds)} × 相手 {string.Join(",", victims)}");
        report.AppendLine();
        report.AppendLine(Header.Replace("| |", "| 世代 |", StringComparison.Ordinal));
        var best = Evaluate(who, style, form, start, seeds, victims, parallel);
        report.AppendLine(Row("0", best));
        log.WriteLine(Row("0", best));
        var step = sigma;
        for (var g = 1; g <= generations; g++)
        {
            var candidates = Enumerable.Range(0, lambda).Select(_ => Mutate(best.Tune, step, random)).ToList();
            var trials = EvaluateMany(who, style, form, candidates, seeds, victims, parallel);
            var top = trials.MaxBy(t => t.Score)!;
            var improved = top.Score > best.Score;
            step = Math.Clamp(step * (improved ? 1.5f : 0.82f), 0.05f, 1f);
            if (improved)
            {
                best = top;
            }

            var line = Row(string.Create(CultureInfo.InvariantCulture, $"{g}{(improved ? " ✓" : "")}"), best);
            report.AppendLine(line);
            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{line} 幅 {step:0.00} ({watch.Elapsed.TotalMinutes:0.0} 分)"));
        }

        report.AppendLine();
        var compared = EvaluateMany(who, style, form, [start, best.Tune], heldSeeds, heldVictims, parallel);
        var (before, after) = (compared[0], compared[1]);
        var taken = best.Tune != start && after.Score > before.Score && after.Incapacitated >= before.Incapacitated;
        report.AppendLine(CultureInfo.InvariantCulture, $"比べ直し（使わなかった種 {string.Join(",", heldSeeds)} × 相手 {string.Join(",", heldVictims)}）:");
        report.AppendLine();
        report.AppendLine(Header);
        report.AppendLine(Row("始め", before));
        report.AppendLine(Row("練習後", after));
        report.AppendLine();
        report.AppendLine(taken ? $"→ 採る（点 {before.Score:0.0} → {after.Score:0.0}、無力化 {before.Incapacitated} → {after.Incapacitated}）。" : "→ 採らない（比べ直しで始めを上回らなかった）。");
        report.AppendLine(CultureInfo.InvariantCulture, $"（{watch.Elapsed.TotalMinutes:0.0} 分）");
        report.AppendLine();
        var cmBefore = TextbookCentimetres(who, form, start);
        var cmAfter = TextbookCentimetres(who, form, best.Tune);
        var paired = PairedEvaluation.Compute([.. before.Outcomes.Select(o => Score(o, cmBefore))], [.. after.Outcomes.Select(o => Score(o, cmAfter))]);
        ConfirmLog.Append("swordpractice", $"{who.Id} の {form}: 練習した型の経路は、無防備の相手を 1 振りでより無力化するか", Describe(start), Describe(best.Tune), heldSeeds, paired,
            new Dictionary<string, string> { ["victims"] = string.Join(",", heldVictims), ["incapacitated"] = $"{before.Incapacitated} -> {after.Incapacitated} / {after.Outcomes.Count}", ["adopted"] = taken ? "yes" : "no" });
        return (taken ? best.Tune : null, report.ToString());
    }
}
