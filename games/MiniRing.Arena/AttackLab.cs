using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 攻撃のドリル: 単発とコンビネーションを、溜め（予備動作）の長さごとに、守らず打ち返さない相手へ打ち込む。
/// 拳ごとに、当たった速さ・拳に乗った質量（体重の乗り）・1 発の運動エネルギー・ダメージと、1 本（溜めを含む）にかかる時間を測る。
/// 溜めの威力は場合分けせず物理に任せる（溜めで縮めた体のバネと、体の動きが拳に移る: <see cref="Fighter"/> の体の動きとタメを拳へ移す仕組み）。
/// </summary>
internal sealed class AttackLab(int bouts, float seconds, int seed, Action<string> log, float targetGuard = 0.2f)
{
    private const float Dt = 1f / 240f;

    /// <summary>1 発の拳とみなす、最初の接触からの時間 [s]。</summary>
    private const float PunchWindow = 0.25f;


    /// <summary>受け手の構えの高さ（低いと拳がガードに触れずに頭・胴へ届く: ミット打ちのように拳そのものを測る）。</summary>
    private float TargetGuard { get; } = targetGuard;

    /// <summary>1 つの型 × 溜めの測定。</summary>
    internal readonly record struct Result(string Combo, float Range, float Telegraph, int Thrown, int Hits, int HeadHits, float Speed, float Mass, float Energy, float Damage, float Seconds, float Gap = 0f)
    {
        /// <summary>1 本あたりのダメージ。</summary>
        public float DamagePerCombo => Thrown > 0 ? Damage / Thrown : 0f;

        /// <summary>1 秒あたりのダメージ（溜めが長いほど 1 本に時間がかかる）。</summary>
        public float DamagePerSecond => Seconds > 0f ? Damage / Seconds : 0f;
    }

    public IReadOnlyList<Result> Run(IReadOnlyList<string> combos, IReadOnlyList<float> telegraphs, IReadOnlyList<float> ranges)
    {
        foreach (var name in combos)
        {
            _ = Combo.Named(name); // 打ち間違いを、試合を回す前に知らせる
        }

        if (telegraphs.Any(t => t is < 0f or > BotStyle.MaxTelegraph))
        {
            throw new ArgumentException($"溜めは 0〜{BotStyle.MaxTelegraph} 秒です。", nameof(telegraphs));
        }

        var jobs = combos.SelectMany(c => ranges.SelectMany(r => telegraphs.Select(t => (Combo: c, Range: r, Telegraph: t)))).ToArray();
        var results = new Result[jobs.Length];
        Parallel.For(0, jobs.Length, i => results[i] = Measure(jobs[i].Combo, jobs[i].Telegraph, jobs[i].Range));
        foreach (var combo in combos)
        {
            log($"#### {combo}\n");
            log("| 間合い | 溜め [s] | 当たった拳 / 打った本数 | 頭 | 当たった速さ [m/s] | 拳に乗った質量 [kg] | 1 発の運動エネルギー [J] | 1 本のダメージ | 1 秒のダメージ | 胸の間 [腕] |");
            log("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in results.Where(r => r.Combo == combo))
            {
                log(string.Create(CultureInfo.InvariantCulture,
                    $"| {r.Range:0.00} | {r.Telegraph:0.00} | {r.Hits} / {r.Thrown} | {r.HeadHits} | {r.Speed:0.00} | {r.Mass:0.00} | {r.Energy:0.00} | {r.DamagePerCombo:0.0} | {r.DamagePerSecond:0.00} | {r.Gap:0.00} |"));
            }

            log("");
        }

        return results;
    }

    /// <summary>
    /// 攻め手に <paramref name="combo"/> だけを、溜め <paramref name="telegraph"/> 秒で打たせる。受け手は守らず打ち返さず、構えて立つ。
    /// 当たりは 1 発の拳で 1 回だけ数える（同じ拳がガードに触れてから頭に当たったら、強い方）。
    /// </summary>
    public Result Measure(string combo, float telegraph, float range)
    {
        var attacker = (DefenseDrill.Attacker(PunchKind.Straight, range) with
        {
            Name = $"attack-{combo}", Telegraph = telegraph, ComboWeights = new Dictionary<string, float> { [combo] = 1f },
        }).Validated();
        var target = BotCatalog.Boxer with { Name = "target", GuardHeight = TargetGuard, AttackRange = 0.7f, NeutralRange = 0.7f, EngageRange = 0.7f, EdgeRange = 0f, Attacks = false, DefenseRate = 0f, Prediction = 0f, CounterRate = 0f, ClinchRate = 0f, Circling = 0f, HeadMovement = 0f };
        int thrown = 0, hits = 0, head = 0;
        float speed = 0f, mass = 0f, energy = 0f, damage = 0f, time = 0f;
        float gap = 0f;
        var open = new (float Start, float Energy, HitEvent Hit)?[2];
        void Flush(int h)
        {
            if (open[h] is { } g)
            {
                hits++;
                head += g.Hit.Zone is HitZone.Chin or HitZone.Temple or HitZone.Head ? 1 : 0;
                speed += g.Hit.Speed;
                mass += g.Hit.ImpactMass;
                energy += g.Energy;
                open[h] = null;
            }
        }

        var gapSteps = 0;
        for (var b = 0; b < bouts; b++)
        {
            var bots = new List<BoxerBot>();
            var corners = new BoxingCorners(attacker.Name, s =>
            {
                var bot = new BoxerBot(attacker, s);
                bots.Add(bot);
                return bot;
            }, s => new BoxerBot(target, s));
            using var game = new BoxingGame(seed: seed + b, corners: corners);
            for (var t = 0f; t < seconds; t += Dt)
            {
                game.Step(default, Dt);
                gap += System.Numerics.Vector3.Distance(game.Cpu.ChestPosition, game.Player.ChestPosition) / game.Cpu.ArmReach;
                gapSteps++;
                foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Cpu))
                {
                    damage += hit.Damage;
                    var h = (int)hit.Hand;
                    var e = 0.5f * hit.ImpactMass * hit.Speed * hit.Speed;
                    // 1 発の拳は、最初に触れてから PunchWindow 秒の接触のうち、いちばん強いものだけを数える（押し付けたままの接触で薄めない）。
                    if (open[h] is { } g && t - g.Start < PunchWindow)
                    {
                        open[h] = e > g.Energy ? (g.Start, e, hit) : g;
                    }
                    else
                    {
                        Flush(h);
                        open[h] = (t, e, hit);
                    }
                }
            }

            Flush(0);
            Flush(1);
            time += seconds;
            thrown += bots.Sum(x => x.Stats.Techniques.Values.Sum(c => c.Thrown));
        }

        var steps = Math.Max(1, Combo.Named(combo).Steps.Count);
        return new Result(combo, range, telegraph, thrown / steps, hits, head, hits > 0 ? speed / hits : 0f, hits > 0 ? mass / hits : 0f, hits > 0 ? energy / hits : 0f, damage, time, gap / Math.Max(1, gapSteps));
    }
}
