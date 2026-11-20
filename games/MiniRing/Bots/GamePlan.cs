namespace MiniRing.Bots;

/// <summary>
/// 間合いの表の 1 行: 胸と胸の水平距離 <see cref="Cm"/> [cm] の帯にいた間の、1 秒あたりに与えた（<see cref="Dealt"/>）・受けた（<see cref="Taken"/>）ダメージ
/// （試合の得点の物差し）。出どころは <c>miniring-arena maailab --ranges</c> の <c>ranges_bins.csv</c>（30 秒以上いた帯だけ）。
/// </summary>
public sealed record RangeBin
{
    /// <summary>帯の真ん中の距離 [cm]（胸と胸の水平距離）。</summary>
    public float Cm { get; init; }

    /// <summary>その帯にいた間に与えたダメージ [/s]。</summary>
    public float Dealt { get; init; }

    /// <summary>その帯にいた間に受けたダメージ [/s]。</summary>
    public float Taken { get; init; }
}

/// <summary>
/// 戦略の層の値（<see cref="BotStyle.GamePlan"/>。キャラの教育）: 得意な間合いの表（自分の <see cref="Own"/> と相手の <see cref="Foe"/>）と性格。
/// 判断の式は <see cref="Bots.GamePlan"/>。どれも入力の側（足運びの目標の距離・手数）にしか効かず、体の強さ・威力には触れない（CLAUDE.md §1.8）。
/// </summary>
public sealed record GamePlanStyle
{
    /// <summary>0..1: 足運びの普段の目標の距離と手数を、この層の値へどれだけ寄せるか（0 で層なし: 判断は今までとビット単位で同じ）。</summary>
    public float Weight { get; init; } = 1f;

    /// <summary>0..3: 得（自分が攻めやすい距離の値打ち）を重く見る性格。</summary>
    public float Boldness { get; init; } = 1f;

    /// <summary>0..3: 脅威（相手が攻めやすい距離の怖さ）を重く見る性格。</summary>
    public float Caution { get; init; } = 1f;

    /// <summary>[s] 打ち合いの無い時間がこれくらい続くと急ぎ始める（1 − exp(−時間 / これ)）。小さいほど膠着を嫌う。</summary>
    public float Patience { get; init; } = 13.43f;

    /// <summary>0..3: 膠着・負け・ラウンドの終わりで急ぐ度合い。</summary>
    public float Pressure { get; init; } = 1f;

    /// <summary>自分の距離ごとの与えた・受けたダメージの表（測定から。距離の順）。</summary>
    public IReadOnlyList<RangeBin> Own { get; init; } = [];

    /// <summary>相手の表（試合の相手のキャラの <see cref="Own"/>。<see cref="Characters.FighterCharacter.Resolve(string)"/> が入れる。分からなければ空）。</summary>
    public IReadOnlyList<RangeBin> Foe { get; init; } = [];

    /// <summary>値域に収め、表を距離の順に並べたもの（有限でない行・距離が正でない行は捨てる。ダメージは 0 以上）。</summary>
    public GamePlanStyle Validated()
    {
        static float Range(float v, float min, float max, float fallback) => Math.Clamp(float.IsFinite(v) ? v : fallback, min, max);
        static IReadOnlyList<RangeBin> Table(IReadOnlyList<RangeBin>? bins) => (bins ?? [])
            .Where(b => b is not null && float.IsFinite(b.Cm) && b.Cm > 0f && float.IsFinite(b.Dealt) && float.IsFinite(b.Taken))
            .Select(b => b with { Dealt = MathF.Max(0f, b.Dealt), Taken = MathF.Max(0f, b.Taken) })
            .OrderBy(b => b.Cm)
            .ToArray();
        return this with
        {
            Weight = Range(Weight, 0f, 1f, 0f),
            Boldness = Range(Boldness, 0f, 3f, 1f),
            Caution = Range(Caution, 0f, 3f, 1f),
            Patience = Range(Patience, 0.8392f, 201.4f, 13.43f),
            Pressure = Range(Pressure, 0f, 3f, 1f),
            Own = Table(Own),
            Foe = Table(Foe),
        };
    }
}

/// <summary>戦略の層の 1 歩の入力（見えている・感じている量だけ。CLAUDE.md §1.7）。</summary>
/// <param name="Opening">相手の開き（<see cref="HurtRead.Opening"/>、構えの下がりと手応え）。</param>
/// <param name="OpponentWinded">相手の息の上がり（<see cref="FighterSense.OpponentWinded"/>）。</param>
/// <param name="OpponentInjury">相手の見えている傷み（<see cref="FighterSense.OpponentInjury"/>）。</param>
/// <param name="Stamina">自分のスタミナ（0..1、自分が感じる量）。</param>
/// <param name="Faintness">自分の意識の薄れ（0..1、自分が感じる量）。</param>
/// <param name="SelfInjury">自分の体の傷み（<see cref="FighterSense.SelfInjury"/>）。</param>
/// <param name="Match">試合の時刻と点（<see cref="MatchSense"/>）。</param>
public readonly record struct PlanInputs(float Opening, float OpponentWinded, float OpponentInjury, float Stamina, float Faintness, float SelfInjury, MatchSense Match);

/// <summary>戦略の層の答え: 好む距離 <see cref="Distance"/> [m]（胸と胸）と手数の倍率 <see cref="Tempo"/>、途中の得 M'・脅威 T'・急ぐ度合い U（調べもの用）。</summary>
public readonly record struct PlanChoice(float Distance, float Tempo, float Merit, float Threat, float Urgency);

/// <summary>
/// 戦略の層（docs/BOTS.md「戦略の層」、2026-11-18）: 自分と相手の得意な間合いの表、自分と相手の傷み・疲れ、意識、試合の時刻と点から、
/// 毎歩「どの距離に立つか」と「手数」を連続に決める。技・状態の名前で分けない（CLAUDE.md §1.3）: すべて重みと温度つきの平均（なめらかな最大）。
/// 乱数・時計を使わない（§1.6）。相手の状態は見える手がかりだけ（§1.7）。
/// <list type="number">
/// <item>得の重み M = Boldness × (1 + 開き + 相手の息の上がり + 相手の傷み) × (0.4 + 0.6 × 自分のスタミナ) × (1 − 0.5 × 意識の薄れ)</item>
/// <item>脅威の重み T = Caution × (1 + 自分の傷み + 意識の薄れ + (1 − 自分のスタミナ)) × (0.5 + 0.5 × (1 − 相手の息の上がり))</item>
/// <item>急ぐ度合い U = Pressure × (負けている量 × ラウンドの進み + (1 − exp(−打ち合いの無い時間 / Patience)))。M' = M (1 + U)、T' = T / (1 + U)</item>
/// <item>距離ごとの見込み S(d) = M' × 攻め(d) − T' × 守り(d)。攻め = 自分の Dealt と相手の Taken の平均、守り = 相手の Dealt と自分の Taken の平均（片方しか無ければそれ。表の間は線形に補い、外は端の値）</item>
/// <item>好む距離 d* = S を温度（S の幅 × <see cref="TemperatureShare"/>）つきで重みにした距離の平均（softmax）</item>
/// <item>手数の倍率 = (1 + U) × 2M' / (M' + T')（<see cref="MinTempo"/>〜<see cref="MaxTempo"/>）</item>
/// </list>
/// </summary>
public static class GamePlan
{
    /// <summary>
    /// 点の差でダウン 1 つを数える重さ [ダメージ]: 測定の物差し（docs/BOTS.md「戦略の層」§5: K.O. 40）と同じ。ダウンは K.O. の一歩手前で、
    /// 勝ち点率との相関はダウンが 0.92、与えたダメージが 0.23（docs/ROSTER_REPORT.md 初回の考察）。
    /// </summary>
    public const float DownValue = 40f;

    /// <summary>負けている量の物差し [ダメージ]: 点の差がこれだけ負けていると負けている量 = 1 − 1/e ≈ 0.63（ダウン 1 つぶん）。</summary>
    public const float LeadScale = 40f;

    /// <summary>距離の候補の間隔 [cm]（表は人形の単位で 2 cm の帯、実物大で 5.6 cm の帯なので、その半分）。</summary>
    public const float GridCm = 2.817f;

    /// <summary>softmax の温度 = S の幅（最大 − 最小）× これ。小さいほど山の頂へ寄る（0.25: 幅の 1/4 下がった距離の重みは頂の e⁻¹）。</summary>
    public const float TemperatureShare = 0.25f;

    /// <summary>手数の倍率の下限・上限（間をこれ以上は空けない・縮めない）。</summary>
    public const float MinTempo = 0.5f;
    public const float MaxTempo = 2f;

    /// <summary>負けている量（0..1）: 点の差（ダメージの差 + <see cref="DownValue"/> × ダウンの差）が負けている分を、<see cref="LeadScale"/> で 1 − exp へ。勝っていれば 0。</summary>
    public static float Losing(in MatchSense match)
    {
        var lead = match.DamageLead + (DownValue * match.DownLead);
        return float.IsFinite(lead) && lead < 0f ? 1f - MathF.Exp(lead / LeadScale) : 0f;
    }

    /// <summary>急ぐ度合い U（0 以上）: 負けている量 × ラウンドの進み + 膠着（打ち合いの無い時間）の、Pressure 倍。</summary>
    public static float Urgency(GamePlanStyle plan, in MatchSense match)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var quiet = float.IsFinite(match.Quiet) ? MathF.Max(0f, match.Quiet) : 0f;
        var stall = 1f - MathF.Exp(-quiet / plan.Patience);
        return plan.Pressure * ((Losing(match) * match.RoundProgress) + stall);
    }

    /// <summary>この歩の答え。表が 1 つも無ければ null（層は何もしない）。</summary>
    public static PlanChoice? Decide(GamePlanStyle plan, in PlanInputs input)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var own = plan.Own;
        var foe = plan.Foe;
        if (own.Count == 0 && foe.Count == 0)
        {
            return null;
        }

        static float Unit(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 0f;
        var opening = Unit(input.Opening);
        var winded = Unit(input.OpponentWinded);
        var hurt = Unit(input.OpponentInjury);
        var stamina = float.IsFinite(input.Stamina) ? Math.Clamp(input.Stamina, 0f, 1f) : 1f;
        var faint = Unit(input.Faintness);
        var injury = Unit(input.SelfInjury);

        var merit = plan.Boldness * (1f + opening + winded + hurt) * (0.4f + (0.6f * stamina)) * (1f - (0.5f * faint));
        var threat = plan.Caution * (1f + injury + faint + (1f - stamina)) * (0.5f + (0.5f * (1f - winded)));
        var urgency = Urgency(plan, input.Match);
        merit *= 1f + urgency;
        threat /= 1f + urgency;

        var lo = MathF.Min(own.Count > 0 ? own[0].Cm : float.MaxValue, foe.Count > 0 ? foe[0].Cm : float.MaxValue);
        var hi = MathF.Max(own.Count > 0 ? own[^1].Cm : float.MinValue, foe.Count > 0 ? foe[^1].Cm : float.MinValue);
        var count = Math.Max(1, (int)MathF.Ceiling((hi - lo) / GridCm) + 1);
        Span<float> scores = count <= 512 ? stackalloc float[count] : new float[count];
        var best = float.MinValue;
        var worst = float.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var cm = MathF.Min(hi, lo + (i * GridCm));
            var s = (merit * Attack(own, foe, cm)) - (threat * Defense(own, foe, cm));
            scores[i] = s;
            best = MathF.Max(best, s);
            worst = MathF.Min(worst, s);
        }

        var temperature = MathF.Max(1e-4f, TemperatureShare * (best - worst));
        var weightSum = 0f;
        var cmSum = 0f;
        for (var i = 0; i < count; i++)
        {
            var w = MathF.Exp((scores[i] - best) / temperature);
            weightSum += w;
            cmSum += w * MathF.Min(hi, lo + (i * GridCm));
        }

        var distance = cmSum / weightSum / 100f;
        var balance = merit + threat > 1e-6f ? 2f * merit / (merit + threat) : 1f;
        var tempo = Math.Clamp((1f + urgency) * balance, MinTempo, MaxTempo);
        return new PlanChoice(distance, tempo, merit, threat, urgency);
    }

    /// <summary>攻め(d): 自分の与えた量と相手の受けた量の平均（片方しか無ければそれ）。</summary>
    internal static float Attack(IReadOnlyList<RangeBin> own, IReadOnlyList<RangeBin> foe, float cm)
        => own.Count > 0 && foe.Count > 0 ? 0.5f * (At(own, cm, dealt: true) + At(foe, cm, dealt: false))
            : own.Count > 0 ? At(own, cm, dealt: true) : At(foe, cm, dealt: false);

    /// <summary>守り(d): 相手の与えた量と自分の受けた量の平均（片方しか無ければそれ）。</summary>
    internal static float Defense(IReadOnlyList<RangeBin> own, IReadOnlyList<RangeBin> foe, float cm)
        => own.Count > 0 && foe.Count > 0 ? 0.5f * (At(foe, cm, dealt: true) + At(own, cm, dealt: false))
            : own.Count > 0 ? At(own, cm, dealt: false) : At(foe, cm, dealt: true);

    /// <summary>距離の順に並んだ表の、距離 <paramref name="cm"/> での値（間は線形に補い、範囲の外は端の値）。</summary>
    internal static float At(IReadOnlyList<RangeBin> bins, float cm, bool dealt)
    {
        static float Of(RangeBin b, bool dealt) => dealt ? b.Dealt : b.Taken;
        if (cm <= bins[0].Cm)
        {
            return Of(bins[0], dealt);
        }

        for (var i = 1; i < bins.Count; i++)
        {
            if (cm <= bins[i].Cm)
            {
                var a = bins[i - 1];
                var b = bins[i];
                var t = b.Cm > a.Cm ? (cm - a.Cm) / (b.Cm - a.Cm) : 1f;
                return Of(a, dealt) + ((Of(b, dealt) - Of(a, dealt)) * t);
            }
        }

        return Of(bins[^1], dealt);
    }
}
