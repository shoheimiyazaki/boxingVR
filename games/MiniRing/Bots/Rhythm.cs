namespace MiniRing.Bots;

/// <summary>
/// 間の表の 1 行: 相手の拳の出始め（<see cref="OpponentRead"/> が見た onset）から <see cref="From"/> 秒の帯で自分が打ち出したときの、
/// 1 打あたりの損得（与えたダメージ − 打ち出しから 0.5 秒に返されたダメージ）。相手のその拳が、自分が打ち出すまでに
/// 自分に触れていなかった（<see cref="None"/>）・守りの上に当たった（<see cref="Guard"/>）・守りの外に入った（<see cref="Clean"/>）で分ける。
/// 出どころは <c>miniring-arena rhythmlab</c> の <c>after_opponent_contact</c>（<c>tools/roster-report/fill-rhythm.mjs</c> が書く）。
/// </summary>
public sealed record RhythmBin
{
    /// <summary>帯の始め [s]（相手の拳の出始めから）。</summary>
    public float From { get; init; }

    /// <summary>相手の拳がまだ触れていないときの 1 打の損得。</summary>
    public float None { get; init; }

    /// <summary>相手の拳が守りの上に当たった後の 1 打の損得。</summary>
    public float Guard { get; init; }

    /// <summary>相手の拳が守りの外に入った後の 1 打の損得。</summary>
    public float Clean { get; init; }
}

/// <summary>
/// 間の層の値（<see cref="BotStyle.Rhythm"/>。キャラの教育）: 相手の拳の後のどの時に打てば得をし、どの時に打つと返されるか（攻めの番・守りの番）。
/// 判断の式は <see cref="Rhythm"/>。入力の側（次の拳をいつ出すか）にしか効かず、体の強さ・威力には触れない（CLAUDE.md §1.8）。
/// </summary>
public sealed record RhythmStyle
{
    /// <summary>0..1: 次の拳までの間の縮み方を、この層の値へどれだけ寄せるか（0 で層なし: 判断は今までとビット単位で同じ）。</summary>
    public float Weight { get; init; } = 1f;

    /// <summary>0..3: 表の損得の差をどれだけ強く間に映すか（性格: 機を見て飛び込む度合い）。</summary>
    public float Seize { get; init; } = 1f;

    /// <summary>相手が 1.2 秒以上打っていないとき（静かな時）の 1 打の損得。表の値はこれとの差で効く。</summary>
    public float Quiet { get; init; }

    /// <summary>表（帯の始めの順）。</summary>
    public IReadOnlyList<RhythmBin> Bins { get; init; } = [];

    /// <summary>値域に収め、表を時刻の順に並べたもの（有限でない行は捨てる）。</summary>
    public RhythmStyle Validated()
    {
        static float Range(float v, float min, float max, float fallback) => Math.Clamp(float.IsFinite(v) ? v : fallback, min, max);
        return this with
        {
            Weight = Range(Weight, 0f, 1f, 0f),
            Seize = Range(Seize, 0f, 3f, 1f),
            Quiet = float.IsFinite(Quiet) ? Quiet : 0f,
            Bins = (Bins ?? [])
                .Where(b => b is not null && float.IsFinite(b.From) && b.From >= 0f && float.IsFinite(b.None) && float.IsFinite(b.Guard) && float.IsFinite(b.Clean))
                .OrderBy(b => b.From)
                .ToArray(),
        };
    }
}

/// <summary>
/// 間の層（2026-11-18）: 「攻めの番・守りの番」を決めて書くのでなく、相手の拳の出始めからの時間と、その拳が自分に触れたか（守りの上か、外か）から、
/// 測った 1 打の損得の表を引き、次の拳までの間（<c>BoxerBot._cooldown</c>）の縮む速さを連続に変える。相手の拳が入った直後は返されるので間が縮まず
/// （守りの番）、相手の連打が終わる頃・守りの上で止めた直後・空振りの直後は間が早く縮む（攻めの番）。番は表と物理から自然に出る。
/// <para>
/// 入力は見えている量（相手の拳の出始め: 拳の速さと向き）と自分が感じる量（打たれた・守りに当たった）だけ（§1.7）。乱数・時計を使わない（§1.6）。
/// 触れ方は場合分けせず、感じた量から作る重み（<see cref="ContactWeights"/>）で表の 3 つの列を混ぜる（§1.3）。
/// </para>
/// </summary>
public static class Rhythm
{
    /// <summary>表の損得の差がこれだけ [ダメージ / 打] あると、間の縮む速さが 2 倍（負なら止まる）。測定で静かな時の 1 打の損得が 3 前後、相手の拳が入った直後が −8 前後。</summary>
    public const float Scale = 4f;

    /// <summary>間の縮む速さの倍率の範囲。負は間が伸びる（打つのを待つ）。</summary>
    public const float MinTempo = -1f;

    public const float MaxTempo = 3f;

    /// <summary>守りの外に入った打撃のダメージがこれだけあると、入った重みが 1 − 1/e（測定の 1 打の平均は 6 前後）。</summary>
    public const float CleanFeel = 3f;

    /// <summary>表の終わりの帯の終わり [s]。rhythmlab の窓（1.2 秒）と同じ。この先は静かな時。</summary>
    public const float Horizon = 1.2f;

    /// <summary>最後の帯の真ん中からここまでで、静かな時の値へ寄せ終える [s]（静かな時の帯は 1.2 秒から先の平均なので、その始めの少し先）。</summary>
    public const float QuietAt = 1.3f;

    /// <summary>
    /// 相手の拳の後に感じたことからの、表の 3 つの列の重み（足して 1）。
    /// </summary>
    /// <param name="cleanDamage">相手のその拳の出始めから、守りの外に受けたダメージの合計。</param>
    /// <param name="guardContacts">守りの上に当たった数。</param>
    public static (float None, float Guard, float Clean) ContactWeights(float cleanDamage, float guardContacts)
    {
        var clean = 1f - MathF.Exp(-MathF.Max(0f, cleanDamage) / CleanFeel);
        var guard = (1f - clean) * (1f - MathF.Exp(-MathF.Max(0f, guardContacts)));
        return (1f - clean - guard, guard, clean);
    }

    /// <summary>
    /// 今打ち出したときの 1 打の損得の見込み。帯の値はその帯（始めから次の帯の始め、最後は <see cref="Horizon"/> まで）の平均なので、
    /// 帯の真ん中の点とみなして間を線形に補う（始めの点とみなすと、半帯ぶん早く動いた。レビュー指摘）。最初の帯の真ん中より前はその値、
    /// 最後の帯の真ん中から <see cref="QuietAt"/> までで静かな時の値へ寄せる。
    /// </summary>
    public static float Expected(RhythmStyle style, float sinceOnset, float cleanDamage, float guardContacts)
    {
        var bins = style.Bins;
        if (bins.Count == 0 || !float.IsFinite(sinceOnset) || sinceOnset < 0f)
        {
            return style.Quiet;
        }

        var (wNone, wGuard, wClean) = ContactWeights(cleanDamage, guardContacts);
        float Value(RhythmBin b) => (wNone * b.None) + (wGuard * b.Guard) + (wClean * b.Clean);
        float Middle(int i) => 0.5f * (bins[i].From + (i + 1 < bins.Count ? bins[i + 1].From : MathF.Max(bins[i].From, Horizon)));
        if (sinceOnset <= Middle(0))
        {
            return Value(bins[0]);
        }

        for (var i = 1; i < bins.Count; i++)
        {
            var to = Middle(i);
            if (sinceOnset < to)
            {
                var from = Middle(i - 1);
                var t = (sinceOnset - from) / MathF.Max(1e-4f, to - from);
                return Value(bins[i - 1]) + ((Value(bins[i]) - Value(bins[i - 1])) * t);
            }
        }

        var lastMiddle = Middle(bins.Count - 1);
        var last = Value(bins[^1]);
        var fade = Math.Clamp((sinceOnset - lastMiddle) / MathF.Max(1e-4f, QuietAt - lastMiddle), 0f, 1f);
        return last + ((style.Quiet - last) * fade);
    }

    /// <summary>
    /// 次の拳までの間の縮む速さの倍率: 1 + Weight × (Seize × (見込み − 静かな時) / <see cref="Scale"/>)、
    /// 倍率そのものは <see cref="MinTempo"/>〜<see cref="MaxTempo"/>。層が無ければちょうど 1。
    /// </summary>
    public static float Tempo(RhythmStyle? style, float sinceOnset, float cleanDamage, float guardContacts)
    {
        if (style is not { Weight: > 0f })
        {
            return 1f;
        }

        var raw = 1f + (style.Seize * (Expected(style, sinceOnset, cleanDamage, guardContacts) - style.Quiet) / Scale);
        var tempo = Math.Clamp(raw, MinTempo, MaxTempo);
        return 1f + ((tempo - 1f) * style.Weight);
    }
}
