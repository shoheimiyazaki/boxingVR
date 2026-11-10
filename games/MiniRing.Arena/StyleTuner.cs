using System.Globalization;
using System.Text;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// スタイルの自動調整（PDCA を 1 世代ずつ回す）。
/// <list type="number">
/// <item>Plan: いまの王者のスタイルから、値を少しずつ変えた候補を作る（変えた値を記録）。</item>
/// <item>Do: 全候補を同じ相手・同じ種で戦わせる（同じ条件で比べるので、差が運に埋もれにくい）。</item>
/// <item>Check: いちばん良かった候補を、<b>別の種</b>で王者と直接対戦させて確かめる（その種にだけ強い偶然を除く）。</item>
/// <item>Act: 確かめても勝ち越したときだけ王者を入れ替える。</item>
/// </list>
/// 人並みの制約（反応時間・守りの成功率・手の速さ）は <see cref="BotStyle.Validated"/> が守るので、超人化では勝てない。
/// <para>
/// 採用した王者は「殿堂」として相手陣に加える（直近 <see cref="HallOfFameSize"/> 人）。固定の相手陣だけだと、全勝した時点で
/// 点が頭打ちになり、それ以上の改善を見分けられない（PDCA サイクル 4 の反省）。
/// </para>
/// </summary>
internal sealed class StyleTuner(IReadOnlyList<Contender> gauntlet, int bouts, float seconds, int seed, Action<string> log,
    Action<BotStyle>? adopted = null, TuneFocus focus = TuneFocus.All)
{
    private const int HallOfFameSize = 3;

    private readonly Mutation _mutation = new(seed);
    private readonly List<Contender> _hallOfFame = [];

    private IEnumerable<Contender> Opponents => gauntlet.Concat(_hallOfFame);

    public BotStyle Run(BotStyle start, int generations, int population)
    {
        var champion = start.Validated();
        var championScore = Evaluate(champion, seed);
        log(string.Create(CultureInfo.InvariantCulture, $"開始: {champion.Name} の点 {championScore:0.000}（相手 {string.Join(", ", gauntlet.Select(g => g.Name))} + 殿堂、各 {bouts} 試合 × {seconds:0} 秒）"));
        for (var generation = 1; generation <= generations; generation++)
        {
            var genSeed = seed + (generation * 101);
            var candidates = Enumerable.Range(0, population).Select(i => Mutate(champion, $"{start.Name}-g{generation}c{i}")).ToList();
            var scored = candidates.Select(c => (c.Style, c.Changes, Score: Evaluate(c.Style, genSeed))).OrderByDescending(c => c.Score).ToList();
            var incumbent = Evaluate(champion, genSeed);
            var best = scored[0];
            var sb = new StringBuilder();
            var scores = string.Join(" ", scored.Select(s => s.Score.ToString("0.000", CultureInfo.InvariantCulture)));
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"世代 {generation}: 王者 {incumbent:0.000} / 候補 {scores}"));
            sb.AppendLine("  最良の候補の変更: " + best.Changes);
            if (best.Score <= incumbent)
            {
                sb.Append("  → 王者より良い候補なし。据え置き。");
                log(sb.ToString());
                continue;
            }

            // Check: 別の種で、王者と直接 + 相手陣との再戦。
            var checkSeed = seed + 50000 + (generation * 7);
            var head = BotArena.RunAll(BotArena.Pair(Contender.Of(best.Style), Contender.Of(champion with { Name = "incumbent" }), bouts * 2, seconds, checkSeed).ToList());
            var headScore = Standing.From(head).Single(s => s.Name == best.Style.Name).Score;
            var recheck = Evaluate(best.Style, checkSeed);
            var recheckIncumbent = Evaluate(champion, checkSeed);
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"  確認（別の種）: 王者と直接 勝率 {headScore:P0}、相手陣に {recheck:0.000}（王者 {recheckIncumbent:0.000}）"));
            if (headScore >= 0.5f && recheck >= recheckIncumbent)
            {
                champion = best.Style with { Name = start.Name };
                championScore = recheck;
                _hallOfFame.Add(Contender.Of(champion with { Name = $"hof-g{generation}" }));
                if (_hallOfFame.Count > HallOfFameSize)
                {
                    _hallOfFame.RemoveAt(0);
                }

                adopted?.Invoke(champion);
                sb.Append(string.Create(CultureInfo.InvariantCulture, $" → 採用（殿堂 hof-g{generation} として相手陣へ）。"));
            }
            else
            {
                sb.Append(" → 偶然と判断して不採用。");
            }

            log(sb.ToString());
        }

        log(string.Create(CultureInfo.InvariantCulture, $"終了: 最終の点 {championScore:0.000}"));
        return champion;
    }

    /// <summary>
    /// 0..1 の点: 相手陣との勝率（勝ち 1・引き分け 0.5）とダメージ比を半々。ダメージ比は tanh(log 比) で滑らかに効かせ、
    /// 全勝してからも「より大差で勝つ」改善を見分けられるようにする。
    /// </summary>
    private float Evaluate(BotStyle style, int evalSeed)
    {
        var me = Contender.Of(style with { Name = "candidate" });
        var specs = Opponents.SelectMany((g, i) => BotArena.Pair(me, g, bouts, seconds, evalSeed + (i * 13))).ToList();
        var standing = Standing.From(BotArena.RunAll(specs)).Single(s => s.Name == "candidate");
        var damage = 0.5f + (0.5f * MathF.Tanh(MathF.Log(MathF.Max(0.01f, standing.DamageRatio))));
        return (0.5f * standing.Score) + (0.5f * damage);
    }

    private (BotStyle Style, string Changes) Mutate(BotStyle parent, string name)
    {
        var m = _mutation;
        m.Begin();
        var defense = focus == TuneFocus.Defense;
        var weights = defense ? parent.ComboWeights : MutateCombos(parent.ComboWeights);

        // 守りと間合いの値（どちらの調整でも動かす）。
        var style = parent with
        {
            Name = name,
            AttackRange = m.Tweak("踏込距離", parent.AttackRange, 0.06f, 0.7f, 1.3f),
            NeutralRange = m.Tweak("普段の距離", parent.NeutralRange, 0.08f, 0.7f, 1.9f),
            EngageRange = m.Tweak("仕掛け距離", parent.EngageRange, 0.1f, 0.7f, 2.2f),
            ExitAfterCombo = m.Tweak("打って離れる", parent.ExitAfterCombo, 0.15f, 0f, 1f),
            PivotAfterCombo = m.Tweak("打って回り込む", parent.PivotAfterCombo, 0.15f, 0f, 1f),
            StopJab = m.Tweak("ストップジャブ", parent.StopJab, 0.15f, 0f, 1f),
            Circling = m.Tweak("回る", parent.Circling, 0.15f, 0f, 1f),
            RopeEscape = m.Tweak("ロープを背負わない", parent.RopeEscape, 0.2f, 0f, 1f),
            // 0（見ない）と 0.8〜2 の間は飛ぶので、見ている間だけ帯の中で揺らす（帯の外の値を記録しない）。
            EvadeReach = parent.EvadeReach > 0f ? m.Tweak("避ける届く範囲", parent.EvadeReach, 0.1f, 0.8f, 2f) : 0f,
            HeadMovement = m.Tweak("頭を振る", parent.HeadMovement, 0.15f, 0f, 1f),
            GuardHeight = m.Tweak("構えの高さ", parent.GuardHeight, 0.15f, 0f, BotStyle.MaxGuardHeight),
            GuardWidth = m.Tweak("構えの開き", parent.GuardWidth, 0.15f, 0.2f, 1.5f),
            GuardReach = m.Tweak("構えの出し", parent.GuardReach, 0.15f, 0.3f, 1.5f),
            Anticipation = m.Tweak("動き出しを読む", parent.Anticipation, 0.15f, 0f, 1f),
            Prediction = m.Tweak("予測で避ける", parent.Prediction, 0.15f, 0f, 1f),
            EdgeRange = m.Tweak("間合いの端に立つ", parent.EdgeRange, 0.15f, 0f, 1f),
            // 溜めは人の体で 0 にはできない（腰と肩が先に動く）ので、調整では 0.03 秒より短くしない。
            Telegraph = m.Tweak("溜め", MathF.Max(0.03f, parent.Telegraph), 0.02f, 0.03f, 0.2f),
            DefenseRate = m.Tweak("守る率", parent.DefenseRate, 0.1f, 0f, BotStyle.MaxDefenseRate),
            BlockWeight = m.Tweak("ブロック", parent.BlockWeight, 0.3f, 0f, 3f),
            DuckWeight = m.Tweak("ダッキング", parent.DuckWeight, 0.3f, 0f, 3f),
            WeaveWeight = m.Tweak("ウィービング", parent.WeaveWeight, 0.3f, 0f, 3f),
            SlipWeight = m.Tweak("スリップ", parent.SlipWeight, 0.3f, 0f, 3f),
            SwayWeight = m.Tweak("スウェー", parent.SwayWeight, 0.3f, 0f, 3f),
            StepBackWeight = m.Tweak("バックステップ", parent.StepBackWeight, 0.3f, 0f, 3f),
            SideStepWeight = m.Tweak("サイドステップ", parent.SideStepWeight, 0.3f, 0f, 3f),
            SlipInWeight = m.Tweak("インサイドスリップ", parent.SlipInWeight, 0.3f, 0f, 3f),
            RollWeight = m.Tweak("ローリング", parent.RollWeight, 0.3f, 0f, 3f),
            CounterRate = m.Tweak("カウンター", parent.CounterRate, 0.15f, 0f, 1f),
            ClinchRate = m.Tweak("クリンチ", parent.ClinchRate, 0.1f, 0f, 1f),
            ComboWeights = weights,
        };

        if (defense)
        {
            // 実戦での仕上げ: 防御技の動きも、ドリルより小さな幅で 1 つずつ揺らす。
            var book = style.Defenses;
            var target = DefenseBook.Techniques[(int)(m.NextSingle() * DefenseBook.Techniques.Count) % DefenseBook.Techniques.Count];
            book = book.With(target, m.Move($"{target}:", book.Of(target), scale: 0.5f, chance: 0.25f, handsFixed: target == Defense.Block));
            style = style with { Defenses = book };
        }
        else
        {
            // 攻めの値は攻めも含めた調整のときだけ。
            style = style with
            {
                Aggression = m.Tweak("手数", parent.Aggression, 0.12f, 0f, 1f),
                PunchSpeed = m.Tweak("手の速さ", parent.PunchSpeed, 0.05f, 0.8f, BotStyle.MaxPunchSpeed),
                ReadGuard = m.Tweak("構えを読む", parent.ReadGuard, 0.15f, 0f, 1f),
                ThrowRate = m.Tweak("投げ", parent.ThrowRate, 0.05f, 0f, 1f),
                ReactionTime = m.Tweak("反応時間", parent.ReactionTime, 0.03f, BotStyle.MinReactionTime, 0.5f),
            };
        }

        return (style.Validated(), m.Changes);
    }

    private Dictionary<string, float> MutateCombos(IReadOnlyDictionary<string, float> parent)
    {
        var m = _mutation;
        var weights = new Dictionary<string, float>(parent);
        foreach (var combo in Combo.All)
        {
            var roll = m.NextSingle();
            var has = weights.TryGetValue(combo.Name, out var w) && w > 0f;
            if (has && roll < 0.2f)
            {
                var next = w * MathF.Exp(m.Normal() * 0.5f);
                weights[combo.Name] = next;
                m.Note(string.Create(CultureInfo.InvariantCulture, $"[{combo.Name}] {w:0.0}→{next:0.0}"));
            }
            else if (!has && roll < 0.05f)
            {
                weights[combo.Name] = 1f;
                m.Note($"[{combo.Name}] 追加");
            }
            else if (has && roll > 0.97f && weights.Count(p => p.Value > 0f) > 2)
            {
                weights.Remove(combo.Name);
                m.Note($"[{combo.Name}] 削除");
            }
        }

        return weights;
    }
}

/// <summary>自動調整で動かす値の範囲。</summary>
internal enum TuneFocus
{
    /// <summary>攻め・守り・間合いの全部。</summary>
    All,

    /// <summary>守りと間合いだけ（攻めの値は固定。防御技の動きも小さく揺らす）。</summary>
    Defense,
}
