using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 回避の基本動作の最適化: 相手は 1 つの型（例: 右ストレート "2"）だけを打ち続け、受け手は 1 つの回避だけで外す。
/// 値を動かせるのは「形」の中だけ（<see cref="Form"/>）: 拳の外側へ頭を滑り込ませ、腰から沈み、頭は残した拳（自分のブロック）の下へ。
/// <list type="number">
/// <item>Plan: いまの動きから、値を少しずつ変えた候補を作り、形の中へ収める。</item>
/// <item>Do: 全候補に同じ種で同じドリルをさせる。</item>
/// <item>Check: 点（きれいに外した割合 − 頭に当たった割合）の最良を、別の種で今の動きと比べ直す。</item>
/// <item>Act: 別の種でも上回ったら採用。目標（<see cref="TargetEvade"/>・<see cref="TargetHead"/>）に届いたら止める。</item>
/// </list>
/// </summary>
internal sealed class BasicsLab(string punch, Defense slot, int bouts, float seconds, int seed, IReadOnlyList<float> ranges, Action<string> log, IReadOnlyList<float>? cues = null, float telegraph = 0f)
{
    /// <summary>目標: 回避の 9 割はどこにも触れられずに外す。</summary>
    public const float TargetEvade = 0.9f;

    /// <summary>目標: 打たれた拳のうち頭に当たるのは 1 割以下（気づけなかった拳も含む）。</summary>
    public const float TargetHead = 0.1f;

    private readonly Mutation _mutation = new(seed);

    /// <summary>
    /// 右ストレートを外へ外す形（人の手で決めた出発点）: 拳の外側（打ってくる腕の肩の側）の横下へ、腰から頭をしっかり落とし、
    /// 拳の通り道を空けて通過させる。拳は頭と一緒に動かし、通り道に残さない（残すと拳をガードで受ける「ブロック」になる）。
    /// </summary>
    public static DefenseMove SlipOutside { get; } = new()
    {
        Outside = true, HeadX = 0.4f, HeadY = -0.5f, HeadZ = 0.05f, Duration = 0.35f, Rate = 30f,
        HandsUp = 0.3f, GloveAnchor = 0f, GloveWidth = 0.1f, GloveHeight = -0.03f, GloveForward = 0.25f,
        VsStraight = 1f,
    };

    /// <summary>
    /// 技ごとの形（予測編 P7。頭を元の大きさに戻し、ずっとしゃがまないように磨き直す）。
    /// <list type="bullet">
    /// <item>スリップ: ストレートの外側へ、頭を横へ大きく、沈みは浅く（膝を少し曲げる程度）。</item>
    /// <item>ウィービング: 真ん中から U 字に沈み、来るフックの反対側へ抜けて起き上がる。</item>
    /// <item>ローリング: フックの進む向きへ頭を流しながら浅く沈む。</item>
    /// <item>ダッキング: 短く深く沈んで、すぐ起き上がる。</item>
    /// </list>
    /// </summary>
    public static DefenseMove Form(DefenseMove m, Defense slot) => slot switch
    {
        Defense.Slip => (m with
        {
            Outside = true, Sweep = false,
            HeadX = Math.Clamp(m.HeadX, 0.3f, 0.6f), HeadY = Math.Clamp(m.HeadY, -0.4f, -0.1f),
            GloveAnchor = Math.Clamp(m.GloveAnchor, 0f, 0.3f), HandsUp = Math.Clamp(m.HandsUp, 0f, 0.5f),
        }).Validated(),
        Defense.Weave => (m with
        {
            Sweep = true, Outside = false,
            HeadX = Math.Clamp(m.HeadX, -0.1f, 0.2f), EndX = Math.Clamp(m.EndX, 0.25f, 0.6f), HeadY = Math.Clamp(m.HeadY, -0.65f, -0.3f),
            Duration = Math.Clamp(m.Duration, 0.25f, 0.5f),
        }).Validated(),
        Defense.Roll => (m with
        {
            Sweep = true, Outside = false,
            HeadX = Math.Clamp(m.HeadX, -0.1f, 0.2f), EndX = Math.Clamp(m.EndX, 0.25f, 0.6f), HeadY = Math.Clamp(m.HeadY, -0.4f, -0.1f),
        }).Validated(),
        Defense.Duck => (m with
        {
            Sweep = false, HeadY = Math.Clamp(m.HeadY, -0.7f, -0.35f), Duration = Math.Clamp(m.Duration, 0.15f, 0.35f),
        }).Validated(),
        _ => m.Validated(),
    };

    /// <summary>
    /// しゃがみの減点: 頭を <c>0.25</c>（腕の長さ単位）より深く沈める分 × 沈んでいる時間。U 字に沈む技は底にいるのが半分の時間。
    /// 減点なしで磨くと、どの技も深く長く沈む形になり、試合の 3 割をしゃがんで過ごした（予測編 P7）。
    /// </summary>
    public static float CrouchPenalty(DefenseMove m)
        => 0.8f * MathF.Max(0f, -m.HeadY - 0.25f) * (m.Duration / 0.35f) * (m.Sweep ? 0.5f : 1f);

    /// <summary>構えだけを比べる（守らない受け手が、頭に何割もらうか）。高さ × 左右の開き × 前への出し。</summary>
    public void GuardSurvey(IReadOnlyList<float> heights, IReadOnlyList<float> widths, IReadOnlyList<float> reaches)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| 構えの高さ | 開き | 出し | 頭に当たった割合 | 拳の数 |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var h in heights)
        {
            foreach (var w in widths)
            {
                foreach (var z in reaches)
                {
                    var open = DefenseDrill.Defender(DefenseBook.Default, defend: false) with { GuardHeight = h, GuardWidth = w, GuardReach = z };
                    var r = Evaluate(open, null, seed, null);
                    sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {h:0.0} | {w:0.00} | {z:0.00} | {r.HeadRate:P0} | {r.Punches} |"));
                }
            }
        }

        log(sb.ToString());
    }

    /// <summary>合図の遅れ（拳が出てから回避を始めるまで）ごとに、同じ動きを測る。動きが間に合う時間の窓を知るため。</summary>
    public void CueSurvey(string label, DefenseMove move, float guardHeight, IReadOnlyList<float> delays)
    {
        log("| 合図の遅れ | 点 | きれいに外した | 頭に当たった（全体） | 回避中の頭 / ガード / ほか | 気づけず頭 | 回避を出せた | 拳の数 |");
        log("|---|---|---|---|---|---|---|---|");
        foreach (var d in delays)
        {
            var r = Evaluate(Defender(move, guardHeight), slot, seed, d < 0f ? null : d);
            var name = d < 0f ? $"{label}（自分の目と反応）" : string.Create(CultureInfo.InvariantCulture, $"{label} {d:0.00}s");
            log(Row(name, r));
        }

        log("");
    }

    /// <summary>1 つの動きを測って 1 行にする（比べる表のため）。</summary>
    public PunchDrillResult Measure(string label, DefenseMove move, float guardHeight, int evalSeed)
    {
        var r = Evaluate(Defender(move, guardHeight), slot, evalSeed);
        log(Row(label, r));
        return r;
    }

    public static string Header =>
        "| 動き | 点 | きれいに外した | 頭に当たった（全体） | 回避中の頭 / ガード / ほか | 気づけず頭 | 回避を出せた | 拳の数 |\n|---|---|---|---|---|---|---|---|";

    public DefenseMove Optimize(DefenseMove start, float guardHeight, int generations, int population)
    {
        var current = Form(start, slot);
        log(string.Create(CultureInfo.InvariantCulture, $"### 最適化（{slot}、相手の型 {punch}、構えの高さ {guardHeight:0.0}）\n"));
        for (var generation = 1; generation <= generations; generation++)
        {
            var genSeed = seed + (generation * 211);
            var incumbent = Evaluate(Defender(current, guardHeight), slot, genSeed);
            var incumbentScore = incumbent.Score - CrouchPenalty(current);
            var candidates = Enumerable.Range(0, population).Select(_ => Mutate(current)).ToList();
            var scored = candidates.Select(c => (c.Move, c.Changes, Result: Evaluate(Defender(c.Move, guardHeight), slot, genSeed)))
                .Select(c => (c.Move, c.Changes, c.Result, Score: c.Result.Score - CrouchPenalty(c.Move)))
                .OrderByDescending(c => c.Score).ToList();
            var best = scored[0];
            var line = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
                $"- 世代 {generation}: 今 {incumbentScore:0.000}（外した {incumbent.EvadeRate:P0}・頭 {incumbent.HeadRate:P0}・しゃがみ減点 {CrouchPenalty(current):0.00}） / 最良の候補 {best.Score:0.000}（{best.Changes}）"));
            if (best.Score > incumbentScore)
            {
                var checkSeed = seed + 90000 + (generation * 17);
                var recheck = Evaluate(Defender(best.Move, guardHeight), slot, checkSeed).Score - CrouchPenalty(best.Move);
                var recheckIncumbent = Evaluate(Defender(current, guardHeight), slot, checkSeed).Score - CrouchPenalty(current);
                line.Append(string.Create(CultureInfo.InvariantCulture, $" → 別の種で {recheck:0.000} 対 今 {recheckIncumbent:0.000}"));
                if (recheck > recheckIncumbent)
                {
                    current = best.Move;
                    line.Append(" → 採用");
                }
                else
                {
                    line.Append(" → 不採用");
                }
            }
            else
            {
                line.Append(" → 据え置き");
            }

            log(line.ToString());
            var now = Evaluate(Defender(current, guardHeight), slot, seed + 555_000 + generation);
            if (now.EvadeRate >= TargetEvade && now.HeadRate <= TargetHead)
            {
                log(string.Create(CultureInfo.InvariantCulture, $"\n目標に届いた（別の種で 外した {now.EvadeRate:P0}・頭 {now.HeadRate:P0}）。世代 {generation} で止める。"));
                break;
            }
        }

        log("\n最終の動き: " + Describe(current) + "\n");
        return current;
    }

    public static string Describe(DefenseMove m) => DefenseTuner.Describe(m)
        + string.Create(CultureInfo.InvariantCulture, $" 拳を残す {m.GloveAnchor:0.00} 拳の高さ {m.GloveHeight:0.00} 前 {m.GloveForward:0.00} 幅 {m.GloveWidth:0.00}{(m.Outside ? " 外側へ" : "")}");

    public static string Row(string label, PunchDrillResult r) => string.Create(CultureInfo.InvariantCulture,
        $"| {label} | {r.Score:0.00} | {r.EvadeRate:P0} | {r.HeadRate:P0} | {r.HeadHits} / {r.GuardHits} / {r.OtherHits} | {r.OpenHeadHits} | {r.Coverage:P0} | {r.Punches} |");

    private BotStyle Defender(DefenseMove move, float guardHeight)
        => DefenseDrill.Defender(DefenseBook.Default.With(slot, move), defend: true, counters: false) with { GuardHeight = guardHeight };

    private (DefenseMove Move, string Changes) Mutate(DefenseMove parent)
    {
        _mutation.Begin();
        var moved = Form(_mutation.Move("", parent, scale: 1f, chance: 0.4f), slot);
        return (moved, _mutation.Changes);
    }

    /// <summary>
    /// 合図の遅れが複数なら、全部で測って足し合わせる（1 つの遅れだけで磨くと、その時だけ当たらない形になった:
    /// 0.04 秒で頭 0 % の形が、0 秒で 41 %・0.08 秒で 55 %。回避の基本動作 B4）。
    /// </summary>
    private PunchDrillResult Evaluate(BotStyle defender, Defense? defense, int evalSeed)
        => cues is null || cues.Count == 0
            ? Evaluate(defender, defense, evalSeed, null)
            : cues.Select(c => Evaluate(defender, defense, evalSeed, c)).Aggregate(PunchDrillResult.Empty, (a, b) => a + b);

    /// <summary>間合いごと・種を変えて <c>bouts</c> 本のドリルを並列に行い、足し合わせる。</summary>
    private PunchDrillResult Evaluate(BotStyle defender, Defense? defense, int evalSeed, float? cueDelay)
    {
        var runs = new PunchDrillResult[bouts * ranges.Count];
        Parallel.For(0, runs.Length, j =>
        {
            var range = ranges[j / bouts];
            var s = evalSeed + ((j % bouts) * 7919);
            runs[j] = DefenseDrill.Punch(punch, defense ?? slot, defender with { NeutralRange = range }, s, seconds, range, cueDelay, telegraph);
        });
        return runs.Aggregate(PunchDrillResult.Empty, (a, b) => a + b);
    }
}
