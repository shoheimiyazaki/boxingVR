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
/// <param name="CountersThrown">受け手が打ったカウンターの拳の数。</param>
/// <param name="CountersLanded">そのうち当たった数。</param>
public sealed record DrillResult(Defense Defense, PunchKind Kind, int Punches, float Damage, int BaselinePunches, float BaselineDamage,
    float CounterDamage, int Defenses, int Failed, int CountersThrown = 0, int CountersLanded = 0)
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
    /// <summary>ドリルの既定の間合い（受け手が保つ胸と胸の距離、腕の長さ倍）。攻め手はこれより 0.1 だけ踏み込んで打つ。</summary>
    public const float DefaultRange = 1.15f;

    /// <summary>
    /// その種類の拳を打つ攻め手（守らない・回らない・頭を振らない）。<paramref name="combos"/> なら、その種類の拳を含む連打を打つ
    /// （実戦の相手は単発でなく連打で来る。単発で磨いた回避は、前へ入る形が連打の 2 発目に当たりに行き、実戦で効かなかった。回避編 E4）。
    /// </summary>
    public static BotStyle Attacker(PunchKind kind, float range = DefaultRange, bool combos = false) => new()
    {
        Name = $"drill-{kind}",
        Aggression = 0.8f,
        AttackRange = range - 0.1f,
        NeutralRange = range,
        EngageRange = 1.6f,
        ComboWeights = combos ? CombosOf(kind) : kind switch
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
    public static BotStyle Defender(DefenseBook book, bool defend, float range = DefaultRange, bool counters = true) => new()
    {
        Name = defend ? "drill-defender" : "drill-open",
        Attacks = false,
        NeutralRange = range,
        Circling = 0f,
        HeadMovement = 0f,
        GuardHeight = 0.6f,
        ReactionTime = 0.18f,
        Anticipation = 1f,
        DefenseRate = defend ? BotStyle.MaxDefenseRate : 0f,
        CounterRate = counters ? 1f : 0f,
        RopeEscape = 1f, // 回避の後退でロープに詰まらない（回避編 E1）
        EvadeReach = 1.2f, // 届かない拳は避けない（回避編 E2）
        ClinchRate = 0f,
        ThrowRate = 0f,
        Defenses = book,
    };

    /// <summary>1 本のドリル（守る受け手と、同じ種で守らない受け手の 2 試合）。</summary>
    public static DrillResult Run(Defense defense, PunchKind kind, DefenseBook book, int seed, float seconds, Baseline? baseline = null,
        float range = DefaultRange, bool counters = true, bool combos = false)
    {
        ArgumentNullException.ThrowIfNull(book);
        var open = baseline ?? MeasureBaseline(kind, seed, seconds, range, combos);
        var (punches, damage, counter, stats) = Bout(kind, Defender(book, defend: true, range, counters), defense, seed, seconds, range, combos);
        var defended = stats.Defenses.Values;
        return new DrillResult(defense, kind, punches, damage, open.Punches, open.Damage, counter,
            defended.Sum(c => c.Attempts), defended.Sum(c => c.Failed), stats.CountersThrown, stats.CountersLanded);
    }

    /// <summary>守らない受け手の成績（防御の動きに依らないので、同じ種・同じ種類なら使い回せる）。</summary>
    public static Baseline MeasureBaseline(PunchKind kind, int seed, float seconds, float range = DefaultRange, bool combos = false)
    {
        var (punches, damage, _, _) = Bout(kind, Defender(DefenseBook.Default, defend: false, range), null, seed, seconds, range, combos);
        return new Baseline(punches, damage);
    }

    private static (int Punches, float Damage, float Counter, BotStats Stats) Bout(PunchKind kind, BotStyle defender, Defense? forced, int seed, float seconds, float range, bool combos)
    {
        var attackers = new List<BoxerBot>();
        var defenders = new List<BoxerBot>();
        var attacker = Attacker(kind, range, combos).Validated();
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

    /// <summary>
    /// 基本動作のドリル: 攻め手は 1 つの型（例: "2" = 右ストレート）だけを打ち続け、受け手は 1 つの回避だけで守る（打ち返さない）。
    /// 当たった部位を、受け手が守りの最中だったかで分けて数える（頭・ガード（腕と拳）・そのほか）。
    /// </summary>
    /// <param name="punch">攻め手の型の名前（<see cref="Combo.All"/>）。</param>
    /// <param name="defense">受け手が使う回避。</param>
    /// <param name="defender">受け手のスタイル（<see cref="Defender"/> から作り、構えの高さなどを変えてよい）。</param>
    /// <param name="seed">試合の種。</param>
    /// <param name="seconds">ドリルの長さ [s]。</param>
    /// <param name="range">受け手が保つ間合い（腕の長さ倍）。</param>
    /// <param name="cue">
    /// 合図の遅れ [s]。値があれば、受け手は自分で拳を見て守るのでなく、攻め手が打ち始めてからこの時間で必ず回避を始める
    /// （決まった拳を打ってもらう練習。反応を除いて動きだけを測る）。null なら受け手自身の目と反応で守る。
    /// </param>
    /// <param name="telegraph">攻め手の打つ前の体の溜め [s]。合図は溜めの始めから数える。</param>
    public static PunchDrillResult Punch(string punch, Defense defense, BotStyle defender, int seed, float seconds, float range = DefaultRange, float? cue = null, float telegraph = 0f)
    {
        ArgumentNullException.ThrowIfNull(defender);
        var attackers = new List<BoxerBot>();
        var defenders = new List<BoxerBot>();
        var attacker = PunchAttacker(punch, range, telegraph: telegraph);
        var style = (cue is null ? defender : defender with { DefenseRate = 0f }).Validated();
        var cues = cue is { } delay ? DrillCue.For(punch, attacker, delay) : null;
        var corners = new BoxingCorners(attacker.Name,
            s => Keep(attackers, new BoxerBot(attacker, s)),
            s => Keep(defenders, new BoxerBot(style, s, defense)));
        using var game = new BoxingGame(seed: seed, corners: corners);
        int head = 0, guard = 0, other = 0, openHead = 0;

        // 1 発は 1 回だけ数える。ガードに触れてから頭に当たったら頭に数え直す
        // （接触ごとに数えると、頭の割合が 1 を超えた。レビュー指摘）。
        var counted = new Dictionary<Hand, (int Punch, bool Head, bool Guard, bool Other)>();
        var headDamage = 0f;
        for (var t = 0f; t < seconds; t += StepDt)
        {
            game.Step(default, StepDt);
            cues?.Update(t + StepDt, attackers, defenders[^1]);
            var thrownNow = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));

            foreach (var hit in game.RecentHits)
            {
                if (hit.Attacker != game.Cpu)
                {
                    continue;
                }

                var defending = defenders.Count > 0 && defenders[^1].CurrentDefense != Defense.None;
                var isHead = hit.Zone is HitZone.Chin or HitZone.Temple or HitZone.Head or HitZone.Neck;
                var seen = counted.GetValueOrDefault(hit.Hand);
                if (seen.Punch == thrownNow && thrownNow > 0)
                {
                    if (seen.Head || !isHead)
                    {
                        headDamage += isHead ? hit.Damage : 0f;
                        continue; // この 1 発はもう数えた
                    }

                    // ガードかほかに数えた 1 発が頭にも当たった: 頭に数え直す。
                    guard -= seen.Guard ? 1 : 0;
                    other -= seen.Other ? 1 : 0;
                }

                var guardZone = hit.Zone is HitZone.Guard or HitZone.Elbow;
                counted[hit.Hand] = (thrownNow, isHead, defending && guardZone, defending && !isHead && !guardZone);
                switch (hit.Zone)
                {
                    case HitZone.Chin or HitZone.Temple or HitZone.Head or HitZone.Neck:
                        headDamage += hit.Damage;
                        if (defending)
                        {
                            head++;
                        }
                        else
                        {
                            openHead++;
                        }

                        break;
                    case HitZone.Guard or HitZone.Elbow:
                        guard += defending ? 1 : 0;
                        break;
                    default:
                        other += defending ? 1 : 0;
                        break;
                }
            }
        }

        var stats = new BotStats();
        foreach (var bot in defenders)
        {
            stats.Merge(bot.Stats);
        }

        var count = stats.Defenses.GetValueOrDefault(defense);
        var punches = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));
        return new PunchDrillResult(punches, count.Attempts, count.Failed, head, guard, other, openHead, headDamage);
    }

    /// <summary>その種類の拳を含む、実戦でよく出る連打（ストレートなら 1-2 や 1-1-2、フックなら 2-3-2 や 3-2）。</summary>
    private static Dictionary<string, float> CombosOf(PunchKind kind) => kind switch
    {
        PunchKind.Straight => new() { ["1-2"] = 2f, ["1-1-2"] = 1f, ["1-1"] = 1f, ["2-3"] = 1f },
        PunchKind.Hook => new() { ["1-2-3"] = 1f, ["2-3-2"] = 1f, ["3-2"] = 1f, ["3-4"] = 1f },
        PunchKind.Uppercut => new() { ["6-3"] = 1f, ["5-2"] = 1f, ["6-3-2"] = 1f },
        _ => new() { ["1b-2"] = 1f, ["3b-3"] = 1f, ["2b-3"] = 1f, ["4b-3"] = 1f },
    };

    /// <summary>1 つの型（例: "2"）だけを打ち続ける攻め手。</summary>
    /// <param name="punch">打たせる単発の型。</param>
    /// <param name="range">受け手が保つ間合い。</param>
    /// <param name="guardHeight">攻め手の構えの高さ（1 = あご。拳があごから出るので、ストレートが頭の高さをまっすぐ来る）。</param>
    /// <param name="telegraph">打つ前の体の溜め [s]（<see cref="BotStyle.Telegraph"/>）。</param>
    public static BotStyle PunchAttacker(string punch, float range = DefaultRange, float guardHeight = 1f, float telegraph = 0f)
        => Combo.Named(punch).Steps.Count != 1
            ? throw new ArgumentException($"基本動作のドリルは単発の型だけを打たせます（{punch}）。", nameof(punch))
            : (Attacker(PunchKind.Straight, range) with { Name = $"drill-{punch}", GuardHeight = guardHeight, Telegraph = telegraph, ComboWeights = new Dictionary<string, float> { [punch] = 1f } }).Validated();

    /// <summary>
    /// 練習の合図: 攻め手が型を打ち始めるたびに、決めた遅れの後で受け手に知らせる（<see cref="BoxerBot.Cue"/>）。
    /// 試合を進めるたびに <see cref="Update"/> を呼ぶ。
    /// </summary>
    public sealed class DrillCue
    {
        private readonly Hand _opponentHand;
        private readonly PunchKind _kind;
        private readonly float _delay;
        private readonly Queue<float> _due = new();
        private int _seen;

        private DrillCue(Hand opponentHand, PunchKind kind, float delay)
        {
            _opponentHand = opponentHand;
            _kind = kind;
            _delay = delay;
        }

        public static DrillCue For(string punch, BotStyle attacker, float delay)
        {
            ArgumentNullException.ThrowIfNull(attacker);
            var technique = Combo.Named(punch).Steps[0];
            var hand = PunchSpec.Of(technique).Lead ? attacker.Lead : Opposite(attacker.Lead);
            return new DrillCue(hand, BoxerBot.KindOf(technique), delay);
        }

        /// <param name="time">いまの時刻 [s]。</param>
        /// <param name="attackers">攻め手のボット（ラウンドごとに作り直されたものを含め、全部）。</param>
        /// <param name="defender">いまの受け手。</param>
        public void Update(float time, IEnumerable<BoxerBot> attackers, BoxerBot defender)
        {
            ArgumentNullException.ThrowIfNull(defender);
            var thrown = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));
            for (; _seen < thrown; _seen++)
            {
                _due.Enqueue(time + _delay);
            }

            // 守りの最中で始められなければ、次の歩でまた試す（合図を捨てない。レビュー指摘）。
            while (_due.Count > 0 && _due.Peek() <= time + 1e-4f && defender.Cue(_opponentHand, _kind))
            {
                _due.Dequeue();
            }
        }
    }

    private static Hand Opposite(Hand hand) => hand == Hand.Left ? Hand.Right : Hand.Left;

    private static BoxerBot Keep(List<BoxerBot> list, BoxerBot bot)
    {
        list.Add(bot);
        return bot;
    }

    /// <summary>守らない受け手が受けた拳の数とダメージ。</summary>
    public readonly record struct Baseline(int Punches, float Damage);
}

/// <summary>
/// 基本動作のドリル（<see cref="DefenseDrill.Punch"/>）の結果。
/// </summary>
/// <param name="Punches">攻め手が打った拳の数。</param>
/// <param name="Defenses">受け手が回避を出した回数。</param>
/// <param name="Failed">回避の最中に（どこかを）打たれた回数。</param>
/// <param name="HeadHits">回避の最中に頭（あご・こめかみ・頭・首）へ当たった数。</param>
/// <param name="GuardHits">回避の最中に腕・拳で受けた数。</param>
/// <param name="OtherHits">回避の最中にそのほか（胴・肩など）へ当たった数。</param>
/// <param name="OpenHeadHits">回避を出していない時に頭へ当たった数（気づけなかった・間に合わなかった拳）。</param>
/// <param name="HeadDamage">頭へのダメージの合計（回避の最中かどうかを問わない）。</param>
public sealed record PunchDrillResult(int Punches, int Defenses, int Failed, int HeadHits, int GuardHits, int OtherHits, int OpenHeadHits, float HeadDamage)
{
    /// <summary>回避が何にも触れられずに終わった割合（きれいに外した）。</summary>
    public float EvadeRate => Defenses > 0 ? 1f - ((float)Failed / Defenses) : 0f;

    /// <summary>打たれた拳のうち、頭に当たった割合（回避の最中かどうかを問わない）。</summary>
    public float HeadRate => Punches > 0 ? (float)(HeadHits + OpenHeadHits) / Punches : 0f;

    /// <summary>拳のうち、回避を出せた割合（気づいて間に合った）。</summary>
    public float Coverage => Punches > 0 ? Math.Min(1f, (float)Defenses / Punches) : 0f;

    /// <summary>拳のうち、回避の最中に腕・拳で受けた割合（ブロックの上に当たった）。</summary>
    public float GuardRate => Punches > 0 ? (float)GuardHits / Punches : 0f;

    /// <summary>
    /// 点 = きれいに外した割合（拳がどこにも触れずに通り過ぎた）− 頭に当たった割合 − ガードに当たった割合。
    /// 回避は拳を通過させる技で、ガードで受けるのはブロック。ガードを頭の半分の罰にすると、最適化は拳を額の前に上げて
    /// 全部受ける形を選んだ（基本動作編 B8: 頭 7 %・ガード 89 %）。
    /// </summary>
    public float Score => EvadeRate - HeadRate - GuardRate;

    public static PunchDrillResult operator +(PunchDrillResult a, PunchDrillResult b) => new(
        a.Punches + b.Punches, a.Defenses + b.Defenses, a.Failed + b.Failed, a.HeadHits + b.HeadHits, a.GuardHits + b.GuardHits,
        a.OtherHits + b.OtherHits, a.OpenHeadHits + b.OpenHeadHits, a.HeadDamage + b.HeadDamage);

    public static PunchDrillResult Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0f);
}
