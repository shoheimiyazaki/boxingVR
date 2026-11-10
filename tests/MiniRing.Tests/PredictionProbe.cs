using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 予測の回避の調査用: 右ストレートだけを打つ相手に、合図なしで（自分の読みだけで）インサイドスリップする。
/// 溜めの長さ × 予測の度合いごとに、頭に当たった割合・きれいに外した割合を出す（--logger "console;verbosity=detailed"）。
/// </summary>
[TestClass]
public sealed class PredictionProbe
{
    [TestMethod]
    [DataRow(0f)]
    [DataRow(0.05f)]
    [DataRow(0.08f)]
    [DataRow(0.12f)]
    [DataRow(0.16f)]
    public void RightCross_InsideSlip_ByPrediction(float telegraph)
    {
        foreach (var prediction in new[] { 0f, 1f })
        {
            var defender = PredictionTests.SlipDefender(prediction);
            var total = PunchDrillResult.Empty;
            for (var seed = 1; seed <= 3; seed++)
            {
                total += DefenseDrill.Punch("2", Defense.Slip, defender, seed, 40f, telegraph: telegraph);
            }

            Console.WriteLine($"telegraph {telegraph:0.00}s prediction {prediction:0.0}: {total}");
        }
    }

    /// <summary>外側へ横下に外すスリップ（相手の右ストレートの外、拳の軌跡の横下へ頭を落とす）を、予測で始める。</summary>
    [TestMethod]
    [DataRow(0.08f)]
    public void RightCross_OutsideSlip_ByPrediction(float telegraph)
    {
        foreach (var (name, move) in new[]
        {
            ("outside-down", new DefenseMove { Outside = true, HeadX = 0.5f, HeadY = -0.6f, HeadZ = -0.2f, EndX = 0.5f, Duration = 0.3f, Step = 1f, Rate = 60f, HandsUp = 0.3f, GloveWidth = 0.1f, GloveHeight = -0.03f, GloveForward = 0.25f, VsStraight = 1f }),
            ("outside-wide", new DefenseMove { Outside = true, HeadX = 0.7f, HeadY = -0.5f, HeadZ = -0.1f, EndX = 0.7f, Duration = 0.3f, Step = 1f, Rate = 60f, HandsUp = 0.3f, GloveWidth = 0.1f, GloveHeight = -0.03f, GloveForward = 0.25f, VsStraight = 1f }),
            ("inside", PredictionTests.SlipDefender(0f).Defenses.Of(Defense.Slip)),
        })
        {
            var defender = PredictionTests.SlipDefender(1f) with { Defenses = PredictionTests.SlipDefender(1f).Defenses.With(Defense.Slip, move) };
            var total = PunchDrillResult.Empty;
            for (var seed = 1; seed <= 3; seed++)
            {
                total += DefenseDrill.Punch("2", Defense.Slip, defender, seed, 40f, telegraph: telegraph);
            }

            Console.WriteLine($"{name} telegraph {telegraph:0.00}: head {total.HeadRate:P0} clean {total.EvadeRate:P0} (in defense head {total.HeadHits} guard {total.GuardHits} other {total.OtherHits}, open head {total.OpenHeadHits}, of {total.Punches})");
        }
    }

    /// <summary>予測の度合い × 体の手がかりの要求ごとに、溜めた右ストレートへのスリップの結果を出す。</summary>
    [TestMethod]
    public void BodyCueSweep()
    {
        foreach (var prediction in new[] { 0.6f, 1f })
        {
            foreach (var cue in new[] { 0f, 0.5f, 1f, 1.5f, 2f, 2.5f })
            {
                var defender = PredictionTests.SlipDefender(prediction) with { BodyCue = cue };
                var total = PunchDrillResult.Empty;
                for (var seed = 1; seed <= 3; seed++)
                {
                    total += DefenseDrill.Punch("2", Defense.Slip, defender, seed, 30f, telegraph: 0.08f);
                }

                Console.WriteLine($"prediction {prediction:0.0} body cue {cue:0.0}: head {total.HeadRate:P0} clean {total.EvadeRate:P0} covered {total.Coverage:P0} ({total.Punches} punches)");
            }
        }
    }

    /// <summary>比べるための合図つきドリル（溜めの始めから決まった遅れで必ず回避を始める）。</summary>
    [TestMethod]
    public void CuedSlip()
    {
        var defender = PredictionTests.SlipDefender(0f);
        foreach (var (telegraph, cue) in new[] { (0.08f, 0f), (0.08f, 0.04f), (0.08f, 0.08f), (0.16f, 0f), (0.16f, 0.07f), (0.16f, 0.12f) })
        {
            var total = PunchDrillResult.Empty;
            for (var seed = 1; seed <= 3; seed++)
            {
                total += DefenseDrill.Punch("2", Defense.Slip, defender, seed, 40f, cue: cue, telegraph: telegraph);
            }

            Console.WriteLine($"cued telegraph {telegraph:0.00}s cue +{cue:0.00}s: head {total.HeadRate:P0} clean {total.EvadeRate:P0} ({total.HeadHits}+{total.OpenHeadHits} head, {total.OtherHits} other of {total.Punches})");
        }
    }

    /// <summary>予測の守りを始めた時と、相手が打つと決めた時（溜めの始め）の差、その 1 発の結果を並べる。</summary>
    [TestMethod]
    [DataRow(0.08f, 1f)]
    [DataRow(0.12f, 1f)]
    public void Timing(float telegraph, float prediction)
    {
        var attackers = new List<BoxerBot>();
        var defenders = new List<BoxerBot>();
        var attacker = DefenseDrill.PunchAttacker("2", telegraph: telegraph);
        var defender = (PredictionTests.SlipDefender(prediction)).Validated();
        var corners = new BoxingCorners("a", s => Keep(attackers, new BoxerBot(attacker, s)), s => Keep(defenders, new BoxerBot(defender, s, Defense.Slip)));
        using var game = new BoxingGame(seed: 1, corners: corners);
        var starts = new List<(float T, float Side, bool Predicted)>();
        var throws = new List<float>();
        var hits = new List<(float T, string Zone)>();
        var was = Defense.None;
        var thrownSoFar = 0;
        for (var t = 0f; t < 40f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
            var now = defenders[^1].CurrentDefense;
            if (now != Defense.None && was == Defense.None)
            {
                starts.Add((t, defenders[^1].DefenseSide.Side, defenders[^1].DefenseSide.Predicted));
            }

            was = now;
            var thrown = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));
            if (thrown != thrownSoFar)
            {
                thrownSoFar = thrown;
                throws.Add(t);
            }

            hits.AddRange(game.RecentHits.Where(h => h.Attacker == game.Cpu).Select(h => (t, h.Zone.ToString() + (defenders[^1].CurrentDefense == Defense.None ? "(open)" : "(in " + defenders[^1].CurrentDefense + ")"))));
        }

        foreach (var throwAt in throws.Skip(5).Take(40))
        {
            var start = starts.Where(s => s.T > throwAt - 0.6f && s.T < throwAt + 0.4f).Select(s => ((float T, float Side, bool Predicted)?)s).LastOrDefault();
            var hit = hits.Where(h => h.T > throwAt && h.T < throwAt + 0.4f).Select(h => h.Zone).FirstOrDefault() ?? "-";
            Console.WriteLine($"throw {throwAt:0.000}: defense {(start is { } s ? $"{s.T - throwAt:+0.000;-0.000}s side {s.Side:+0;-0} {(s.Predicted ? "predicted" : "reacted")}" : "none")}  hit {hit}");
        }

        var stats = defenders.Select(d => d.Stats).Aggregate(new BotStats(), (a, b) => { a.Merge(b); return a; });
        Console.WriteLine($"predictions {stats.Predictions}, hit {stats.PredictionsHit}, idle {stats.PredictionsIdle}; onsets learned {defenders[^1].Opponent.Onsets}, attack distance {defenders[^1].Opponent.AttackDistance:0.00}");
    }

    /// <summary>溜めの前後で、受け手が読む手がかり（踏み込み・ひねり・前傾の速さ）がどう動くかを出す（受け手は守らない）。</summary>
    [TestMethod]
    [DataRow(0f)]
    [DataRow(0.08f)]
    public void CueSignals(float telegraph)
    {
        var attackers = new List<BoxerBot>();
        var defenders = new List<BoxerBot>();
        var attacker = DefenseDrill.PunchAttacker("2", telegraph: telegraph);
        var defender = DefenseDrill.Defender(DefenseBook.Default, defend: false, counters: false).Validated();
        var corners = new BoxingCorners("a", s => Keep(attackers, new BoxerBot(attacker, s)), s => Keep(defenders, new BoxerBot(defender, s)));
        using var game = new BoxingGame(seed: 1, corners: corners);
        var log = new List<(float T, (float Closing, float Twist, float Lean) Cue)>();
        var throws = new List<float>();
        var thrownSoFar = 0;
        for (var t = 0f; t < 20f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
            log.Add((t, defenders[^1].Opponent.Cues));
            var thrown = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));
            if (thrown != thrownSoFar)
            {
                thrownSoFar = thrown;
                throws.Add(t);
            }
        }

        // 溜めの始めからの時刻ごとの平均（10 発以上）。
        for (var offset = -0.3f; offset <= 0.2f; offset += 0.025f)
        {
            var at = throws.Skip(3).Select(th => log.MinBy(l => MathF.Abs(l.T - (th + offset))).Cue).ToList();
            Console.WriteLine($"telegraph {telegraph:0.00} {offset:+0.000;-0.000}s closing {at.Average(c => c.Closing):+0.00;-0.00} twist {at.Average(c => c.Twist):+0.00;-0.00} lean {at.Average(c => c.Lean):+0.00;-0.00}");
        }

        var idle = log.Where(l => throws.All(th => MathF.Abs(l.T - th) > 0.5f)).ToList();
        Console.WriteLine($"telegraph {telegraph:0.00} idle sd: closing {Sd(idle.Select(l => l.Cue.Closing)):0.00} twist {Sd(idle.Select(l => l.Cue.Twist)):0.00} lean {Sd(idle.Select(l => l.Cue.Lean)):0.00}");
    }

    private static float Sd(IEnumerable<float> values)
    {
        var list = values.ToList();
        var mean = list.Average();
        return MathF.Sqrt(list.Average(v => (v - mean) * (v - mean)));
    }

    private static BoxerBot Keep(List<BoxerBot> list, BoxerBot bot)
    {
        list.Add(bot);
        return bot;
    }
}
