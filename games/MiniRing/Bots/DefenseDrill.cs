namespace MiniRing.Bots;

/// <summary>
/// 防御ドリルの 1 本の結果。被ダメージは攻め手が打った 1 発あたり。
/// </summary>
/// <param name="Defense">受け手が使った防御技。</param>
/// <param name="Kind">攻め手が打った拳の種類。</param>
/// <param name="Punches">攻め手が打った拳の数。</param>
/// <param name="Damage">受け手が受けたダメージの合計。</param>
/// <param name="BaselineDamage">同じ種で受け手が守らなかったときのダメージの合計（比べる基準）。</param>
/// <param name="BaselinePunches">同じ種で受け手が守らなかったときに、攻め手が打った拳の数。</param>
/// <param name="CounterDamage">受け手のカウンターが与えたダメージ。</param>
/// <param name="Defenses">守りを出した回数。</param>
/// <param name="Failed">守りの最中に打たれた回数。</param>
public sealed record DrillResult(Defense Defense, PunchKind Kind, int Punches, float Damage, int BaselinePunches, float BaselineDamage,
    float CounterDamage, int Defenses, int Failed)
{
    public float DamagePerPunch => Damage / Math.Max(1, Punches);

    public float BaselinePerPunch => BaselineDamage / Math.Max(1, BaselinePunches);

    /// <summary>守らないときに比べて、1 発あたりの被ダメージがどれだけ減ったか（1 = 全く受けない、0 = 変わらない、負 = かえって悪い）。</summary>
    public float Reduction => BaselinePerPunch > 0f ? 1f - (DamagePerPunch / BaselinePerPunch) : 0f;

    /// <summary>守ったあとのカウンターが、守らないときの被ダメージに対してどれだけ取り返したか。</summary>
    public float CounterReturn => BaselineDamage > 0f ? CounterDamage / BaselineDamage : 0f;

    /// <summary>
    /// ドリルの点: 被ダメージの減り + 0.25 × カウンターの取り返し（上限 1）。守って打ち返せる技を少し良しとするが、主は被ダメージの減り。
    /// 取り返しに上限が無いと、1 発が軽いボディやアッパーでは「守らない被ダメージ」がほぼ 0 で割り算が膨らみ（最大 885%）、
    /// 被ダメージを増やしてでもカウンターを打つ守りが選ばれてしまった（防御ドリル D2 の反省）。
    /// </summary>
    public float Score => Reduction + (0.25f * Math.Min(1f, CounterReturn));
}

/// <summary>
/// 防御ドリル: 攻め手は 1 種類の拳（ストレート・フック・アッパー・ボディ）だけを打ち続け、受け手は 1 つの防御技だけで守る
/// （守れたらカウンターだけ返し、自分からは攻めない）。同じ種で「守らない受け手」とも戦わせ、被ダメージの減りを測る。
/// 試合と同じ物理・同じボットで行うので、ここで良い技は実戦でもそのまま効く（はず — 実戦で確かめる）。
/// </summary>
public static class DefenseDrill
{
    private const float StepDt = 1f / 60f;

    /// <summary>その種類の拳だけを打つ攻め手（守らない・回らない・頭を振らない）。</summary>
    public static BotStyle Attacker(PunchKind kind) => new()
    {
        Name = $"drill-{kind}",
        Aggression = 0.8f,
        AttackRange = 1.05f, // 実戦の踏み込み（champion は腕の長さ 1.0 倍）に近い間合い
        NeutralRange = 1.15f,
        EngageRange = 1.6f,
        ComboWeights = kind switch
        {
            PunchKind.Straight => new Dictionary<string, float> { ["1"] = 1f, ["2"] = 1f },
            PunchKind.Hook => new Dictionary<string, float> { ["3"] = 1f, ["4"] = 1f },
            PunchKind.Uppercut => new Dictionary<string, float> { ["5"] = 1f, ["6"] = 1f },
            _ => new Dictionary<string, float> { ["1b"] = 1f, ["2b"] = 1f, ["3b"] = 1f, ["4b"] = 1f },
        },
        ExitAfterCombo = 0f,
        ThrowRate = 0f,
        Circling = 0f,
        HeadMovement = 0f,
        DefenseRate = 0f,
        CounterRate = 0f,
        ClinchRate = 0f,
    };

    /// <summary>
    /// 受け手: 自分からは攻めず、守る率は上限、守り切ったら必ずカウンター。反応時間は人並みの既定（0.18 秒）で、
    /// 動き出しは読み切る（<see cref="BotStyle.Anticipation"/> = 1）。技そのものの良し悪しだけを比べるため。
    /// </summary>
    public static BotStyle Defender(DefenseBook book, bool defend) => new()
    {
        Name = defend ? "drill-defender" : "drill-open",
        Attacks = false,
        NeutralRange = 1.15f,
        Circling = 0f,
        HeadMovement = 0f,
        GuardHeight = 0.6f,
        ReactionTime = 0.18f,
        Anticipation = 1f,
        DefenseRate = defend ? BotStyle.MaxDefenseRate : 0f,
        CounterRate = 1f,
        ClinchRate = 0f,
        ThrowRate = 0f,
        Defenses = book,
    };

    /// <summary>1 本のドリル（守る受け手と、同じ種で守らない受け手の 2 試合）。</summary>
    public static DrillResult Run(Defense defense, PunchKind kind, DefenseBook book, int seed, float seconds, Baseline? baseline = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        var open = baseline ?? MeasureBaseline(kind, seed, seconds);
        var (punches, damage, counter, stats) = Bout(kind, Defender(book, defend: true), defense, seed, seconds);
        var defended = stats.Defenses.Values;
        return new DrillResult(defense, kind, punches, damage, open.Punches, open.Damage, counter,
            defended.Sum(c => c.Attempts), defended.Sum(c => c.Failed));
    }

    /// <summary>守らない受け手の成績（防御の動きに依らないので、同じ種・同じ種類なら使い回せる）。</summary>
    public static Baseline MeasureBaseline(PunchKind kind, int seed, float seconds)
    {
        var (punches, damage, _, _) = Bout(kind, Defender(DefenseBook.Default, defend: false), null, seed, seconds);
        return new Baseline(punches, damage);
    }

    private static (int Punches, float Damage, float Counter, BotStats Stats) Bout(PunchKind kind, BotStyle defender, Defense? forced, int seed, float seconds)
    {
        var attackers = new List<BoxerBot>();
        var defenders = new List<BoxerBot>();
        var attacker = Attacker(kind).Validated();
        var corners = new BoxingCorners(attacker.Name,
            s => Keep(attackers, new BoxerBot(attacker, s)),
            s => Keep(defenders, new BoxerBot(defender, s, forced)));
        using var game = new BoxingGame(seed: seed, corners: corners);
        var damage = 0f;
        var counter = 0f;
        for (var t = 0f; t < seconds; t += StepDt)
        {
            game.Step(default, StepDt);
            foreach (var hit in game.RecentHits)
            {
                if (hit.Attacker == game.Cpu)
                {
                    damage += hit.Damage;
                }
                else
                {
                    counter += hit.Damage;
                }
            }
        }

        var stats = new BotStats();
        foreach (var bot in defenders)
        {
            stats.Merge(bot.Stats);
        }

        var punches = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));
        return (punches, damage, counter, stats);
    }

    private static BoxerBot Keep(List<BoxerBot> list, BoxerBot bot)
    {
        list.Add(bot);
        return bot;
    }

    /// <summary>守らない受け手が受けた拳の数とダメージ。</summary>
    public readonly record struct Baseline(int Punches, float Damage);
}
