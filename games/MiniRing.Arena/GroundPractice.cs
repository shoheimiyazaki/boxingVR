using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRing.Arena.Evolution;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// 倒れた相手へのとどめの教育を、名簿のキャラごとに CMA-ES で練習する（2026-11-24、groundtime --practice）。点はとどめのドリル（<see cref="FinishDrill"/>）の
/// 「倒れてから死ぬまでの秒（死ななければ上限）＋ 攻め手が倒れた 1 回の罰」の平均（小さいほど良い）。変えるのは教育の側（技の経路・間・選び方: CLAUDE.md §1.8）だけ。
/// 狙う範囲（<see cref="GroundMove.AimFrom"/>）を固定にしたい技（刃の首狙い）は、始めの値のまま動かさない（<c>fixedAim</c>）。
/// 最後に、練習に使わなかった受け手と種で、始めの教育と比べ直す（<see cref="PairedEvaluation.Compute"/>・<see cref="ConfirmLog"/>）。
/// </summary>
internal sealed class GroundPractice(Action<string> log, int parallel = 1)
{
    /// <summary>攻め手が倒れた 1 回の罰 [s]（倒れると起き上がるまで攻められない。倒れるくらいなら遅くても立って打つほうが良い）。</summary>
    private const float FallPenalty = 3f;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private sealed record Knob(string Name, float Lo, float Hi, Func<State, float> Get, Func<State, float, State> Set);

    /// <summary>練習の状態: スタイル（好み・間）と、とどめの経路（武器を握るなら武器の教科書の側へ入れる）。</summary>
    internal sealed record State(BotStyle Style, GroundTuning Ground);

    private static GroundMove MoveOf(GroundTuning g, string move) => move switch
    {
        "hammer" => g.Hammer,
        "punch-down" => g.PunchDown,
        "stomp" => g.Stomp,
        "kick" => g.Kick,
        "cut" => g.Cut,
        _ => g.Thrust,
    };

    private static GroundTuning With(GroundTuning g, string move, GroundMove m) => move switch
    {
        "hammer" => g with { Hammer = m },
        "punch-down" => g with { PunchDown = m },
        "stomp" => g with { Stomp = m },
        "kick" => g with { Kick = m },
        "cut" => g with { Cut = m },
        _ => g with { Thrust = m },
    };

    private static IEnumerable<Knob> MoveKnobs(string move, bool foot, bool blade, bool fixedAim)
    {
        Knob K(string field, float lo, float hi, Func<GroundMove, float> get, Func<GroundMove, float, GroundMove> set)
            => new($"{move}.{field}", lo, hi, s => get(MoveOf(s.Ground, move)), (s, v) => s with { Ground = With(s.Ground, move, set(MoveOf(s.Ground, move), v)) });

        yield return K("Duration", 0.42f, 1.68f, m => m.Duration, (m, v) => m with { Duration = v });
        yield return K("Stand", 0f, 1.1f, m => m.Stand, (m, v) => m with { Stand = v });
        yield return K("Reach", foot ? 1.0f : 0.6f, foot ? 2f : 1.4f, m => m.Reach, (m, v) => m with { Reach = v });
        yield return K("Rise", -0.2f, 1.4f, m => m.Rise, (m, v) => m with { Rise = v });
        yield return K("Pull", -0.3f, 1f, m => m.Pull, (m, v) => m with { Pull = v });
        yield return K("Overshoot", 0f, 0.8f, m => m.Overshoot, (m, v) => m with { Overshoot = v });
        yield return K("Through", -0.2f, 0.8f, m => m.Through, (m, v) => m with { Through = v });
        yield return foot
            ? K("StanceLean", 0f, 0.4f, m => m.StanceLean, (m, v) => m with { StanceLean = v })
            : K("MaxSink", 0f, 1.5f, m => m.MaxSink ?? 1.5f, (m, v) => m with { MaxSink = v });

        if (blade)
        {
            yield return K("WindElevation", -1.5f, 2.8f, m => m.WindElevation, (m, v) => m with { WindElevation = v });
            yield return K("StrikeElevation", -1.6f, 0.3f, m => m.StrikeElevation, (m, v) => m with { StrikeElevation = v });
        }

        if (!fixedAim)
        {
            yield return K("AimFrom", 0f, 2f, m => m.AimFrom, (m, v) => m with { AimFrom = v });
            yield return K("AimTo", 0f, 2f, m => m.AimTo, (m, v) => m with { AimTo = v });
        }
    }

    private static Knob S(string name, float lo, float hi, Func<BotStyle, float> get, Func<BotStyle, float, BotStyle> set)
        => new(name, lo, hi, s => get(s.Style), (s, v) => s with { Style = set(s.Style, v) });

    private static Knob T(string name, float lo, float hi, Func<GroundTuning, float> get, Func<GroundTuning, float, GroundTuning> set)
        => new(name, lo, hi, s => get(s.Ground), (s, v) => s with { Ground = set(s.Ground, v) });

    /// <summary>キャラの持つ道具で使える技と、変える値。</summary>
    private static List<Knob> Knobs(FighterCharacter c, bool fixedAim)
    {
        var armed = WeaponCatalog.HasBot(c.Weapon);
        var club = c.Weapon is MiniRing.Weapons.WeaponKind.WoodenClub or MiniRing.Weapons.WeaponKind.Bat;
        var knobs = new List<Knob>
        {
            S("GroundDrive", 0.3f, 1f, s => s.GroundDrive, (s, v) => s with { GroundDrive = v }),
            S("GroundRest", 0.1678f, 1.2f, s => s.GroundRest, (s, v) => s with { GroundRest = v }),
            S("GroundLoad", 0f, 0.3f, s => s.GroundLoad, (s, v) => s with { GroundLoad = v }),
            T("ReadyGap", 0.05f, 0.6f, g => g.ReadyGap, (g, v) => g with { ReadyGap = v }),
            T("ReadyDistance", 0.2f, 0.9f, g => g.ReadyDistance, (g, v) => g with { ReadyDistance = v }),
            T("GiveUpSeconds", 0.2f, 2f, g => g.GiveUpSeconds, (g, v) => g with { GiveUpSeconds = v }),
            T("RetryDelay", 0.1f, 0.6f, g => g.RetryDelay, (g, v) => g with { RetryDelay = v }),
        };
        if (!armed)
        {
            knobs.Add(S("GroundStomp", 0f, 1f, s => s.GroundStomp, (s, v) => s with { GroundStomp = v }));
            knobs.Add(S("GroundKick", 0f, 1f, s => s.GroundKick, (s, v) => s with { GroundKick = v }));
            knobs.Add(S("GroundCut", 0f, 1f, s => s.GroundCut, (s, v) => s with { GroundCut = v }));
            // 30 次元に収めるため、経路を磨くのは足の技（ユーザーの指定「蹴りや踏みつけ」）だけ。手の技は選ぶ割合（GroundStomp・GroundCut）で使う。
            foreach (var move in new[] { "stomp", "kick" })
            {
                knobs.AddRange(MoveKnobs(move, move is "stomp" or "kick", false, false));
            }
        }
        else
        {
            if (!club)
            {
                knobs.Add(S("GroundCut", 0f, 1f, s => s.GroundCut, (s, v) => s with { GroundCut = v }));
            }

            foreach (var move in club ? new[] { "cut" } : ["cut", "thrust-down"])
            {
                knobs.AddRange(MoveKnobs(move, false, true, fixedAim));
            }
        }

        return knobs;
    }

    /// <summary>キャラの今の教育（練習の始め）。</summary>
    internal static State Start(string id, string victim)
    {
        var (style, weaponGround) = FinishDrill.Current(id, victim);
        return new State(style, (weaponGround ?? style.Ground ?? GroundTuning.Default).Validated());
    }

    private static FinishDrill.Teach TeachOf(FighterCharacter c, State s)
        => WeaponCatalog.HasBot(c.Weapon) ? (_, _) => (s.Style.Validated(), s.Ground.Validated()) : (_, _) => (s.Style.Validated() with { Ground = s.Ground.Validated() }, null);

    /// <summary>
    /// 1 本の点 [s]: 死ぬまでの秒（死ななければ上限）＋ 倒れた罰。死ななかった本は、受け手から欠けたセルが多いほど少し良い（上限 + 3 / (1 + セル / 200)）:
    /// どの候補も殺せないうちも、近いほうへ進めるため。
    /// </summary>
    private static float Score(FinishRun r) => r.Seconds + (FallPenalty * r.AttackerFalls) + (r.Killed ? 0f : 3f / (1f + (r.Cells / 200f)));

    private static double[] Scores(FighterCharacter c, State s, IReadOnlyList<string> victims, IReadOnlyList<int> seeds, float seconds)
        => [.. victims.SelectMany(v => seeds.Select(seed => (double)Score(FinishDrill.Run(c.Id, v, seed, seconds, TeachOf(c, s)))))];

    /// <summary>練習して、良かった状態を JSON（教育へ入れる値）で <paramref name="outPath"/> へ書く。</summary>
    public void Practice(string id, IReadOnlyList<string> victims, IReadOnlyList<int> seeds, IReadOnlyList<string> holdVictims, IReadOnlyList<int> holdSeeds,
        int generations, int lambda, float seconds, int seed, State? start, string outPath)
    {
        var c = CharacterRoster.Get(id);
        var begin = start ?? Start(id, victims[0]);
        var knobs = Knobs(c, fixedAim: begin.Ground.Cut.AimFrom == begin.Ground.Cut.AimTo && WeaponCatalog.HasBot(c.Weapon));
        double Norm(Knob k, float v) => Math.Clamp((v - k.Lo) / (k.Hi - k.Lo), 0f, 1f);
        State Decode(IReadOnlyList<double> x) => knobs.Select((k, i) => (k, v: k.Lo + ((float)x[i] * (k.Hi - k.Lo)))).Aggregate(begin, (s, kv) => kv.k.Set(s, kv.v));
        // 探す出発点: 今の教育に、ユーザーの指定（2026-11-24「蹴りや踏みつけを連続で、短時間で」）の向きを足した所（足の技の半分を蹴り・打つ気いっぱい・合間を短く）。
        // 比べ直しは今の教育（begin）と比べる。
        var from = begin with { Style = begin.Style with { GroundKick = 0.5f, GroundDrive = 1f, GroundRest = MathF.Min(begin.Style.GroundRest, 0.3f) } };
        var cma = new CmaEs([.. knobs.Select(k => Norm(k, k.Get(from)))], seed, initialSigma: 0.12, lambda: lambda, maxGenerations: generations);
        var startScore = Scores(c, begin, victims, seeds, seconds).Average();
        log(string.Create(CultureInfo.InvariantCulture, $"## {id} のとどめの練習（{knobs.Count} 値、λ {lambda} × {generations} 世代、受け手 {string.Join(",", victims)}・種 {string.Join(",", seeds)}、上限 {seconds:0} s、倒れた罰 {FallPenalty:0} s）"));
        log("");
        log(string.Create(CultureInfo.InvariantCulture, $"- 始め: {startScore:0.00}"));
        var best = (State: begin, Score: startScore);
        for (var g = 0; g < generations; g++)
        {
            var candidates = cma.Ask();
            // 候補は並べて測る（同じプロセスで並べると、同じ種でも順に回した試合とずれることがある: TvScout。練習の点の雑音として受け入れ、比べ直しは順に回す）。
            var scores = candidates.AsParallel().AsOrdered().WithDegreeOfParallelism(Math.Max(1, parallel)).Select(x => Scores(c, Decode(x), victims, seeds, seconds).Average()).ToArray();
            cma.Tell(scores);
            var i = Array.IndexOf(scores, scores.Min());
            if (scores[i] < best.Score)
            {
                best = (Decode(candidates[i]), scores[i]);
            }

            log(string.Create(CultureInfo.InvariantCulture, $"- 世代 {g + 1}: 最良 {scores.Min():0.00}・中央 {GroundTime.Median(scores.Select(s => (float)s)):0.00}（ここまでの最良 {best.Score:0.00}）"));
        }

        // 平均（分布の中心）も比べる: 1 回の良い標本は運のことがある。
        var meanState = Decode(cma.Best);
        var meanScore = Scores(c, meanState, victims, seeds, seconds).Average();
        var chosen = meanScore <= best.Score ? (Label: "分布の中心", meanState) : (Label: "最良の候補", best.State);
        log(string.Create(CultureInfo.InvariantCulture, $"- 分布の中心: {meanScore:0.00} → 採る候補: {chosen.Label}"));

        // 比べ直し: 練習に使わなかった受け手と種。
        var before = Scores(c, begin, holdVictims, holdSeeds, seconds);
        var after = Scores(c, chosen.Item2, holdVictims, holdSeeds, seconds);
        var result = PairedEvaluation.Compute(after, before);
        log(string.Create(CultureInfo.InvariantCulture, $"- 比べ直し（受け手 {string.Join(",", holdVictims)}・種 {string.Join(",", holdSeeds)}、A = 練習した、B = 始め、点は秒 + 倒れた罰、小さいほど良い）: 始め {before.Average():0.00} → 練習した {after.Average():0.00}、{result}"));
        ConfirmLog.Append("groundtime --practice", $"{id}: 倒れた相手を死なせるまでの秒（+ 倒れた罰）", "練習した", "始め", holdSeeds, result,
            new Dictionary<string, string> { ["victims"] = string.Join(",", holdVictims) });
        log("");
        File.WriteAllText(outPath, JsonSerializer.Serialize(new PracticeOut(id, result.CiHigh < 0d, before.Average(), after.Average(), Edu(chosen.Item2, c)), Json));
    }

    internal sealed record PracticeOut(string Id, bool Better, double Before, double After, Dictionary<string, object?> Education);

    /// <summary>教育へ入れる値（スタイルの好みと経路）。</summary>
    private static Dictionary<string, object?> Edu(State s, FighterCharacter c) => new()
    {
        ["GroundDrive"] = s.Style.GroundDrive,
        ["GroundRest"] = s.Style.GroundRest,
        ["GroundLoad"] = s.Style.GroundLoad,
        ["GroundStomp"] = s.Style.GroundStomp,
        ["GroundKick"] = s.Style.GroundKick,
        ["GroundCut"] = s.Style.GroundCut,
        ["Ground"] = s.Ground,
        ["Armed"] = WeaponCatalog.HasBot(c.Weapon),
    };
}
