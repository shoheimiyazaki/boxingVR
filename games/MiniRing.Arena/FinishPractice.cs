using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRing.Arena.Evolution;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>フィニッシュのドリルの 1 本（<see cref="FinishChance"/>）。</summary>
/// <param name="Downed">受け手が倒れた（ダウン・死）か。</param>
/// <param name="Seconds">チャンスを作ってから倒れるまで [s]（倒れなければ上限）。</param>
/// <param name="Damage">その間に受け手が受けたダメージ。</param>
internal sealed record ChanceRun(bool Downed, float Seconds, float Damage);

/// <summary>
/// フィニッシュのドリル（2026-11-24、groundtime --finish）: 名簿の素手のキャラ（体・教育）が攻め手（赤）、名簿のキャラが受け手（青、自分のボットのまま守る）。
/// 立ち上がりの後、受け手にチャンスを作る: 種が偶数なら意識を薄める（脳しんとう 0.45: 構えが下がり、ふらつく。時間で戻る）、奇数ならスタミナを尽きさせる（0.05: 時間で戻る）。
/// そこから受け手が倒れるまでの秒を測る（レフェリーありの試合の決まり。倒れればダウン）。チャンスは放っておけば戻るので、短く決めるほど良い。
/// </summary>
internal static class FinishChance
{
    private const float Dt = 1f / 120f;

    /// <summary>チャンスを作る時刻 [s]（構えに着いて間合いへ入った頃）。</summary>
    private const float OpenAt = 3.356f;

    public static ChanceRun Run(string attackerId, string victimId, int seed, float seconds, Func<BotStyle, BotStyle>? teach = null)
    {
        var a = CharacterRoster.Get(attackerId);
        var v = CharacterRoster.Get(victimId);
        var style = a.Resolve(victimId);
        style = (teach?.Invoke(style) ?? style).Validated();
        var victimStyle = v.Resolve(attackerId);
        var corners = new BoxingCorners(victimStyle.Name, s => BotCatalog.Bot(victimStyle, s), s => BotCatalog.Bot(style, s),
            PlayerWear: a.Wear, CpuWear: v.Wear, PlayerBody: a.Build, CpuBody: v.Build, PlayerStyle: style, CpuStyleJson: victimStyle.ToJson());
        using var game = new BoxingGame(seed: seed, corners: corners, rules: MatchRules.Tv);
        var victim = game.Cpu;
        var damage0 = 0f;
        for (var t = 0f; t < OpenAt + seconds; t += Dt)
        {
            if (t < OpenAt && t + Dt >= OpenAt)
            {
                if (seed % 2 == 0)
                {
                    victim.Concussion = MathF.Max(victim.Concussion, 0.55f);
                }
                else
                {
                    victim.Stamina = 0.05f;
                }

                damage0 = victim.Condition.TotalDamage;
            }

            game.Step(default, Dt);
            if (!ReferenceEquals(victim, game.Cpu) || victim.State != FighterState.Standing || victim.IsDead || game.RoundOver)
            {
                var down = t >= OpenAt && (victim.State != FighterState.Standing || victim.IsDead || !ReferenceEquals(victim, game.Cpu));
                return new ChanceRun(down, down ? t - OpenAt : seconds, victim.Condition.TotalDamage - damage0);
            }
        }

        return new ChanceRun(false, seconds, victim.Condition.TotalDamage - damage0);
    }
}

/// <summary>
/// フィニッシュの教育の練習（groundtime --finish-practice id）: <see cref="FinishChance"/> の「倒すまでの秒（倒れなければ上限 + 受けたダメージで少し良い）」を CMA-ES で縮める。
/// 変えるのは教育の側だけ（どの型をどれだけ・連打の詰め方・詰める強さ・休みを惜しむ度合い・開きの読み方。CLAUDE.md §1.8）。比べ直しは使わなかった受け手と種。
/// </summary>
internal sealed class FinishPractice(Action<string> log, int parallel = 1)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>練習の受け手（素手の名簿。前の 4 人で練習し、残りで比べ直す）。</summary>
    internal static readonly string[] Victims = ["akira", "albert", "dmitri", "marcus", "ramon", "taeyun", "yoandry", "narong"];

    /// <summary>フィニッシュの表の候補（素手の型。単発・フェイントは外す）。</summary>
    internal static readonly string[] Candidates = ["1-2", "1-2-3", "1-2-3-2", "2-3-2", "6-3-2", "3-4", "1-2-3b", "4b-3", "5-2", "6-3", "1-oh", "3b-3"];

    private sealed record Knob(string Name, float Lo, float Hi, Func<BotStyle, float> Get, Func<BotStyle, float, BotStyle> Set);

    private static List<Knob> Knobs()
    {
        var knobs = new List<Knob>
        {
            new("FinishComboShare", 0f, 1f, s => s.FinishComboShare, (s, v) => s with { FinishComboShare = v }),
            new("FinishGap", 0f, 0.8f, s => s.FinishGap, (s, v) => s with { FinishGap = v }),
            new("FinishTempo", 0f, 5f, s => s.FinishTempo, (s, v) => s with { FinishTempo = v }),
            new("FinishPress", 0f, 2f, s => s.FinishPress, (s, v) => s with { FinishPress = v }),
            new("FinishSpend", 0f, 1f, s => s.FinishSpend, (s, v) => s with { FinishSpend = v }),
            new("HurtWinded", 0f, 2f, s => s.HurtWinded, (s, v) => s with { HurtWinded = v }),
            new("HurtInjury", 0f, 2f, s => s.HurtInjury, (s, v) => s with { HurtInjury = v }),
        };
        foreach (var name in Candidates)
        {
            knobs.Add(new($"combo:{name}", 0f, 3f, s => s.FinishCombos.GetValueOrDefault(name),
                (s, v) => s with { FinishCombos = new Dictionary<string, float>(s.FinishCombos) { [name] = v } }));
        }

        return knobs;
    }

    private static float Score(ChanceRun r, float seconds) => r.Downed ? r.Seconds : seconds + (3f / (1f + (r.Damage / 30f)));

    private double[] Scores(string id, Func<BotStyle, BotStyle>? teach, IReadOnlyList<string> victims, IReadOnlyList<int> seeds, float seconds)
        => [.. victims.SelectMany(v => seeds.Select(s => (v, s))).AsParallel().AsOrdered().WithDegreeOfParallelism(Math.Max(1, parallel))
            .Select(r => (double)Score(FinishChance.Run(id, r.v, r.s, seconds, teach), seconds))];

    public void Practice(string id, IReadOnlyList<string> victims, IReadOnlyList<int> seeds, IReadOnlyList<string> holdVictims, IReadOnlyList<int> holdSeeds,
        int generations, int lambda, float seconds, int seed, string outPath)
    {
        var knobs = Knobs();
        var current = CharacterRoster.Get(id).Resolve(victims[0]);
        // 探す出発点: 今の教育に、ユーザーの指定（2026-11-24「スタミナ消費度外視で短時間に決める」）の向きを足した所。
        var from = current with
        {
            FinishComboShare = 0.6f, FinishGap = 0.3f, FinishSpend = 0.9f, HurtWinded = 1f, HurtInjury = 1f,
            FinishCombos = Candidates.ToDictionary(n => n, n => 1f),
        };
        double Norm(Knob k, float v) => Math.Clamp((v - k.Lo) / (k.Hi - k.Lo), 0f, 1f);
        Func<BotStyle, BotStyle> Decode(IReadOnlyList<double> x) => s => knobs.Select((k, i) => (k, v: k.Lo + ((float)x[i] * (k.Hi - k.Lo)))).Aggregate(s, (st, kv) => kv.k.Set(st, kv.v));
        var cma = new CmaEs([.. knobs.Select(k => Norm(k, k.Get(from)))], seed, initialSigma: 0.15, lambda: lambda, maxGenerations: generations);
        log(string.Create(CultureInfo.InvariantCulture, $"## {id} のフィニッシュの練習（{knobs.Count} 値、λ {lambda} × {generations} 世代、受け手 {string.Join(",", victims)}・種 {string.Join(",", seeds)}、上限 {seconds:0} s）"));
        log("");
        log(string.Create(CultureInfo.InvariantCulture, $"- 始め（今の教育）: {Scores(id, null, victims, seeds, seconds).Average():0.00}"));
        var best = (Teach: (Func<BotStyle, BotStyle>?)null, Score: double.MaxValue, X: (double[]?)null);
        for (var g = 0; g < generations; g++)
        {
            var candidates = cma.Ask();
            var scores = candidates.Select(x => Scores(id, Decode(x), victims, seeds, seconds).Average()).ToArray();
            cma.Tell(scores);
            var i = Array.IndexOf(scores, scores.Min());
            if (scores[i] < best.Score)
            {
                best = (Decode(candidates[i]), scores[i], candidates[i]);
            }

            log(string.Create(CultureInfo.InvariantCulture, $"- 世代 {g + 1}: 最良 {scores.Min():0.00}・中央 {GroundTime.Median(scores.Select(s => (float)s)):0.00}（ここまでの最良 {best.Score:0.00}）"));
        }

        var meanX = cma.Best;
        var meanScore = Scores(id, Decode(meanX), victims, seeds, seconds).Average();
        var chosenX = meanScore <= best.Score ? meanX : best.X!;
        log(string.Create(CultureInfo.InvariantCulture, $"- 分布の中心: {meanScore:0.00} → 採る候補: {(meanScore <= best.Score ? "分布の中心" : "最良の候補")}"));
        var before = Scores(id, null, holdVictims, holdSeeds, seconds);
        var after = Scores(id, Decode(chosenX), holdVictims, holdSeeds, seconds);
        var result = PairedEvaluation.Compute(after, before);
        log(string.Create(CultureInfo.InvariantCulture, $"- 比べ直し（受け手 {string.Join(",", holdVictims)}・種 {string.Join(",", holdSeeds)}、A = 練習した、B = 今、点は倒すまでの秒、小さいほど良い）: 今 {before.Average():0.00} → 練習した {after.Average():0.00}、{result}"));
        ConfirmLog.Append("groundtime --finish-practice", $"{id}: チャンスから倒すまでの秒", "練習した", "今", holdSeeds, result,
            new Dictionary<string, string> { ["victims"] = string.Join(",", holdVictims) });
        // 2 回目の比べ直し（2026-11-24: 1 回目だけ良くて別の種で再現しないことがあった）: さらに別の種（holdSeeds + 100）で、良いときだけ採る。
        var better = result.CiHigh < 0d;
        var second = (PairedResult?)null;
        if (better)
        {
            var seeds2 = holdSeeds.Select(x => x + 100).ToList();
            var before2 = Scores(id, null, holdVictims, seeds2, seconds);
            var after2 = Scores(id, Decode(chosenX), holdVictims, seeds2, seconds);
            second = PairedEvaluation.Compute(after2, before2);
            better = second.CiHigh < 0d;
            log(string.Create(CultureInfo.InvariantCulture, $"- 比べ直し 2（種 {string.Join(",", seeds2)}）: 今 {before2.Average():0.00} → 練習した {after2.Average():0.00}、{second}"));
            ConfirmLog.Append("groundtime --finish-practice", $"{id}: チャンスから倒すまでの秒（2 回目）", "練習した", "今", seeds2, second,
                new Dictionary<string, string> { ["victims"] = string.Join(",", holdVictims) });
        }

        log("");
        var taught = Decode(chosenX)(current);
        var edu = new Dictionary<string, object?>
        {
            ["FinishComboShare"] = taught.FinishComboShare, ["FinishGap"] = taught.FinishGap, ["FinishTempo"] = taught.FinishTempo,
            ["FinishPress"] = taught.FinishPress, ["FinishSpend"] = taught.FinishSpend, ["HurtWinded"] = taught.HurtWinded, ["HurtInjury"] = taught.HurtInjury,
            ["FinishCombos"] = taught.FinishCombos.Where(p => p.Value > 0.05f).ToDictionary(p => p.Key, p => MathF.Round(p.Value, 3)),
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(new { Id = id, Better = better, Before = before.Average(), After = after.Average(), Result = result.ToString(), Second = second?.ToString(), Education = edu }, Json));
    }
}
