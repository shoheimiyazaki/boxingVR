using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 倒れた相手への攻めのドリル（groundlab、docs/BOT_PDCA.md「倒れた相手への攻め」）: レフェリーなしの試合で、受け手を気絶させて倒したままにし、
/// 立っている攻め手が打ち下ろす。技（鉄槌・突き下ろし・踏みつけ）ごとに、打ち始めた数・当たり・渡したエネルギーと、攻め手が倒れた回数を数える。
/// 動画は同じ場面を撮る。
/// </summary>
internal sealed class GroundLab(int bouts, float seconds, int seed, Action<string> log)
{
    private const float StepDt = 1f / 60f;

    /// <summary>受け手を倒しておく脳しんとう（気を失って倒れたまま。GroundStrikeTests と同じ値）。</summary>
    private const float Out = 0.97f;

    private static BotStyle Still => BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f, FinishTempo = 0f, FinishPress = 0f, FinishSpend = 0f };

    /// <summary>1 つの技だけを選ぶスタイル: 踏みつけは毎回（<see cref="BotStyle.GroundStomp"/> = 1 でも直前に踏んだら手の技を挟む）、手は振り下ろす系か突き下ろす系。</summary>
    internal static BotStyle Attacker(string styleName, string move, GroundTuning? ground = null) => (BotCatalog.Style(styleName) with
    {
        GroundDrive = 1f,
        GroundStomp = move is "stomp" or "heel" ? 1f : 0f,
        GroundHeel = move == "heel" ? 1f : 0f, // stomp = 足裏の踏みつけ、heel = かかとの踏み下ろしの連打
        GroundCut = move == "hammer" ? 1f : 0f,
        Ground = ground ?? BotCatalog.Style(styleName).Ground,
    }).Validated();

    internal static BoxingCorners Corners(BotStyle attacker) =>
        new("attacker", s => new BoxerBot(attacker, s), s => new BoxerBot(Still, s), CpuStyleJson: attacker.ToJson(), Referee: false);

    /// <summary>技ごとの集計。<paramref name="MaxHit"/> はいちばん大きい 1 発のエネルギー [J]（外れ値を除いた 1 撃の平均 <see cref="TrimmedPerHit"/> に使う）。</summary>
    internal readonly record struct Tally(int Started, int Hits, float Energy, int Falls, float MaxHit = 0f)
    {
        public static Tally operator +(Tally a, Tally b) => new(a.Started + b.Started, a.Hits + b.Hits, a.Energy + b.Energy, a.Falls + b.Falls, MathF.Max(a.MaxHit, b.MaxHit));

        /// <summary>いちばん大きい 1 発を除いた、1 撃あたりのエネルギー [J]（まれに出る飛び抜けた 1 発で練習の採否が決まらないように）。</summary>
        public float TrimmedPerHit => Hits > 1 ? (Energy - MaxHit) / (Hits - 1) : 0f;
    }

    public void Run(string styleName, IReadOnlyList<string> moves)
    {
        log($"## 倒れた相手への攻め（{styleName}、{bouts} 本 × {seconds:0} 秒、受け手は気を失って倒れたまま、レフェリーなし、PhysicsVersion {Fighter.PhysicsVersion}）");
        log("");
        log("| 選ばせた技 | 技 | 打ち始め | 当たり | J/当たり | J/打ち | 攻め手が倒れた |");
        log("|---|---|---|---|---|---|---|");
        foreach (var move in moves)
        {
            var totals = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
            var falls = 0;
            for (var b = 0; b < bouts; b++)
            {
                falls += Bout(Attacker(styleName, move), seed + b, totals);
            }

            foreach (var (name, t) in totals)
            {
                log($"| {move} | {name} | {t.Started} | {t.Hits} | {(t.Hits > 0 ? t.Energy / t.Hits : 0f):0.00} | {(t.Started > 0 ? t.Energy / t.Started : 0f):0.00} | {falls} |");
            }
        }

        log("");
    }

    /// <summary>1 本: 技ごとの集計（打ち始め・当たり・エネルギー・その技の間かその後 1.68 秒以内に攻め手が倒れた回数）を <paramref name="totals"/> へ足し、攻め手が倒れた回数を返す。</summary>
    private int Bout(BotStyle attacker, int boutSeed, SortedDictionary<string, Tally> totals)
    {
        using var game = new BoxingGame(seed: boutSeed, corners: Corners(attacker));
        var victim = game.Player;
        var falls = 0;
        var wasStanding = true;
        var wasStriking = false;
        var last = "-";
        var since = float.PositiveInfinity;
        for (var t = 0f; t < seconds && !game.RoundOver; t += StepDt)
        {
            if (t > 2.518f)
            {
                victim.Concussion = Out;
            }

            game.Step(default, StepDt);
            var ground = (game.CpuControl as GroundStriker)?.Ground;
            var name = ground?.CurrentName ?? "-";
            var striking = ground?.Striking == true;
            if (striking && !wasStriking)
            {
                totals[name] = totals.GetValueOrDefault(name) + new Tally(1, 0, 0f, 0);
            }

            if (striking)
            {
                last = name;
                since = 0f;
            }
            else
            {
                since += StepDt;
            }

            wasStriking = striking;
            foreach (var hit in game.RecentHits)
            {
                if (hit.Attacker == game.Cpu && hit.Victim == victim && striking)
                {
                    totals[name] = totals.GetValueOrDefault(name) + new Tally(0, 1, hit.Energy, 0, hit.Energy);
                }
            }

            var standing = game.Cpu.State == FighterState.Standing;
            if (Trace && striking && ((int)(t / StepDt)) % 6 == 0)
            {
                var c = game.Cpu;
                Console.WriteLine($"  t={t:0.00} {name} {c.State} hull={c.SupportHull.Count} feet={c.FeetHullCount} margin={c.SupportMargin:0.000} bal={c.Balance:0.00} vb={c.VerticalBalance:0.00} pelvisY={c.PelvisPosition.Y:0.000} kickL={c.IsKicking(Hand.Left)} kickR={c.IsKicking(Hand.Right)}");
            }

            if (wasStanding && !standing)
            {
                falls++;
                if (since <= 1.678f)
                {
                    totals[last] = totals.GetValueOrDefault(last) + new Tally(0, 0, 0f, 1);
                }
            }

            wasStanding = standing;
        }

        return falls;
    }

    /// <summary>技 <paramref name="move"/>（hammer・thrust・stomp）を、経路 <paramref name="ground"/> で種 <paramref name="firstSeed"/> から測る（その技の集計）。</summary>
    internal Tally Measure(string styleName, string move, GroundTuning ground, int firstSeed)
    {
        var totals = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
        for (var b = 0; b < bouts; b++)
        {
            Bout(Attacker(styleName, move, ground), firstSeed + b, totals);
        }

        return totals.GetValueOrDefault(MoveOf(ground, move).Name);
    }

    internal static GroundMove MoveOf(GroundTuning g, string move) => move switch
    {
        "hammer" => g.Hammer,
        "stomp" => g.Stomp,
        "heel" => g.HeelStomp,
        _ => g.PunchDown,
    };

    internal static GroundTuning With(GroundTuning g, string move, GroundMove m) => move switch
    {
        "hammer" => g with { Hammer = m },
        "stomp" => g with { Stomp = m },
        "heel" => g with { HeelStomp = m },
        _ => g with { PunchDown = m },
    };

    internal static bool Trace { get; set; } = Environment.GetEnvironmentVariable("GROUND_TRACE") == "1";

    /// <summary>攻め手が倒れた 1 回の罰 [J]（踏みつけ 1 回の約 1.5 倍: 倒れるくらいなら打たないほうが良い）。</summary>
    private const float FallPenalty = 1690f;

    /// <summary>点: 1 本あたりにその技で渡したエネルギー − 倒れた罰。</summary>
    private float Score(Tally t) => (t.Energy - (FallPenalty * t.Falls)) / bouts;

    /// <summary>
    /// 技の経路の練習（(1+1) 進化戦略、変える幅は 1/5 の決まり。PracticeLab と同じ）: 経路の寸法を少しずつ変え、点が良くなったら採る。
    /// 最後に使わなかった種で始めの経路と比べ直す。結果は経路の値（教科書の <see cref="BotStyle.Ground"/>）として出すだけ。
    /// </summary>
    public void Practice(string styleName, string move, int candidates, float sigma, int validationSeed)
    {
        var random = new Random(seed ^ move.Sum(c => (int)c));
        var start = BotCatalog.Style(styleName).Ground ?? GroundTuning.Default;
        var best = (G: start, T: Measure(styleName, move, start, seed));
        var step = sigma;
        var successes = 0;
        log($"## {move} の練習（{styleName}、種 {seed}〜、{bouts} 本 × {seconds:0} 秒、{candidates} 候補、倒れた罰 {FallPenalty:0} J）");
        log("");
        log("| 候補 | 幅 | 打ち始め | 当たり | J | 倒れた | 点 | 採った |");
        log("|---|---|---|---|---|---|---|---|");
        log($"| 0 | - | {best.T.Started} | {best.T.Hits} | {best.T.Energy:0} | {best.T.Falls} | {Score(best.T):0.0} | - |");
        for (var i = 1; i <= candidates; i++)
        {
            var g = With(best.G, move, Mutate(MoveOf(best.G, move), step, random));
            var t = Measure(styleName, move, g, seed);
            var taken = Score(t) > Score(best.T);
            log($"| {i} | {step:0.000} | {t.Started} | {t.Hits} | {t.Energy:0} | {t.Falls} | {Score(t):0.0} | {(taken ? "採った" : "")} |");
            if (taken)
            {
                best = (g, t);
                successes++;
            }

            if (i % 5 == 0)
            {
                step *= successes > 1 ? 1.3f : 0.75f;
                successes = 0;
            }
        }

        log("");
        log($"### 比べ直し（使わなかった種 {validationSeed}〜）");
        log("");
        log("| 経路 | 打ち始め | 当たり | J | 倒れた | 点 |");
        log("|---|---|---|---|---|---|");
        foreach (var (label, g) in new[] { ("始め", start), ("練習した", best.G) })
        {
            var t = Measure(styleName, move, g, validationSeed);
            log($"| {label} | {t.Started} | {t.Hits} | {t.Energy:0} | {t.Falls} | {Score(t):0.0} |");
        }

        log("");
        log($"練習した経路: `{MoveOf(best.G, move)}`");
        log("");
    }

    /// <summary>
    /// かかとの連打の練習（1 撃の威力を上げる、2026-11-24 ユーザーの指定）: 経路（<see cref="GroundTuning.HeelStomp"/>）と連打の形（<see cref="HeelBarrage"/>:
    /// 下ろす区間・つま先の角・腰の落とし）を少しずつ変え、点 = 1 撃あたりに渡したエネルギー [J]（いちばん大きい 1 発を除く: <see cref="Tally.TrimmedPerHit"/>）が良くなったら採る。ただし攻め手が始めより多く倒れる・
    /// 当たる数が始めの <see cref="HeelMinHitShare"/> を切る候補は採らない（1 撃を重くして数や立ち姿を失わない）。最後に使わなかった種で比べ直す。
    /// </summary>
    public void PracticeHeel(string styleName, int candidates, float sigma, int validationSeed)
    {
        var random = new Random(seed ^ 0x4ee1);
        var start = BotCatalog.Style(styleName).Ground ?? GroundTuning.Default;
        var first = Measure(styleName, "heel", start, seed);
        var best = (G: start, T: first);
        static float PerHit(Tally t) => t.TrimmedPerHit;
        bool Allowed(Tally t) => t.Falls <= first.Falls && t.Hits >= HeelMinHitShare * first.Hits;
        var step = sigma;
        var successes = 0;
        log($"## かかとの連打の練習（1 撃の威力。{styleName}、種 {seed}〜、{bouts} 本 × {seconds:0} 秒、{candidates} 候補）");
        log("");
        log("| 候補 | 幅 | 打ち始め | 当たり | J | J/当たり（最大の 1 発を除く） | 最大の 1 発 | 倒れた | 採った |");
        log("|---|---|---|---|---|---|---|---|---|");
        log($"| 0 | - | {first.Started} | {first.Hits} | {first.Energy:0} | {PerHit(first):0.0} | {first.MaxHit:0} | {first.Falls} | - |");
        for (var i = 1; i <= candidates; i++)
        {
            var g = MutateHeel(best.G, step, random);
            var t = Measure(styleName, "heel", g, seed);
            var taken = Allowed(t) && PerHit(t) > PerHit(best.T);
            log($"| {i} | {step:0.000} | {t.Started} | {t.Hits} | {t.Energy:0} | {PerHit(t):0.0} | {t.MaxHit:0} | {t.Falls} | {(taken ? "採った" : "")} |");
            if (taken)
            {
                best = (g, t);
                successes++;
            }

            if (i % 5 == 0)
            {
                step *= successes > 1 ? 1.3f : 0.75f;
                successes = 0;
            }
        }

        log("");
        log($"### 比べ直し（使わなかった種 {validationSeed}〜）");
        log("");
        log("| 経路 | 打ち始め | 当たり | J | J/当たり（最大の 1 発を除く） | 最大の 1 発 | 倒れた |");
        log("|---|---|---|---|---|---|---|");
        foreach (var (label, g) in new[] { ("始め", start), ("練習した", best.G) })
        {
            var t = Measure(styleName, "heel", g, validationSeed);
            log($"| {label} | {t.Started} | {t.Hits} | {t.Energy:0} | {PerHit(t):0.0} | {t.MaxHit:0} | {t.Falls} |");
        }

        log("");
        log($"練習した経路: `{best.G.HeelStomp}`");
        log("");
        log($"練習した連打: `{best.G.HeelBarrage}`");
        log("");
    }

    /// <summary>かかとの連打の練習で、当たる数が始めのこの割合を切る候補は採らない。</summary>
    private const float HeelMinHitShare = 0.6f;

    /// <summary>かかとの連打の経路と形を少し変える（<see cref="PracticeHeel"/>）。</summary>
    private static GroundTuning MutateHeel(GroundTuning g, float step, Random random)
    {
        float N() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));
        float J(float v, float lo, float hi) => Math.Clamp(v + (step * N()), lo, hi);
        var m = g.HeelStomp;
        var b = g.HeelBarrage;
        return g with
        {
            HeelStomp = m with
            {
                Duration = Math.Clamp(m.Duration * (1f + (0.1f * N())), 0.17f, 0.6f),
                Stand = J(m.Stand, 0f, 0.6f),
                Rise = J(m.Rise, 0.2f, 1.0f),
                Overshoot = J(m.Overshoot, 0f, 0.4f),
                Lean = J(m.Lean, -0.2f, 0.4f),
                StanceLean = J(m.StanceLean, 0f, 0.4f),
            },
            HeelBarrage = (b with
            {
                DownShare = J(b.DownShare, 0.1f, 0.7f),
                Pitch = J(b.Pitch, -0.3f, 0.7f),
                Drop = J(b.Drop, 0f, 0.3f),
            }).Validated(),
        };
    }

    /// <summary>経路を少し変える: 時間を 0.85〜1.15 倍、寸法（腕の長さ単位）に幅 <paramref name="step"/> の正規乱数。沈みの上限は浅いまま（立って打つ）。</summary>
    private static GroundMove Mutate(GroundMove m, float step, Random random)
    {
        float N() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble()));
        float J(float v, float lo, float hi) => Math.Clamp(v + (step * N()), lo, hi);
        var foot = m.Limb is GroundLimb.Foot or GroundLimb.Heel;
        // かかとの連打の Duration は 1 回の踏み下ろしの時間（短い）。ほかは 1 回の攻め全体の時間。
        var (shortest, longest) = m.Limb == GroundLimb.Heel ? (0.17f, 0.6f) : (0.587f, 2.35f);
        return m with
        {
            Duration = Math.Clamp(m.Duration * (1f + (0.1f * N())), shortest, longest),
            Stand = J(m.Stand, 0.05f, 0.9f),
            Reach = J(m.Reach, 0.7f, foot ? 1.7f : 1.1f),
            Rise = J(m.Rise, 0.1f, 1.2f),
            Pull = J(m.Pull, -0.3f, 0.5f),
            Overshoot = J(m.Overshoot, 0f, 0.6f),
            Dip = J(m.Dip, 0f, 0.9f),
            Lean = J(m.Lean, -0.2f, 0.5f),
            MaxSink = J(m.MaxSink ?? 0.5f, 0f, foot ? 0.3f : 0.8f),
            StanceLean = foot ? J(m.StanceLean, 0f, 0.4f) : 0f,
        };
    }

    /// <summary>動画: 1 つの技を選ばせた攻め手（青）が、倒れた受け手（赤）を打つ。</summary>
    public static void Video(string styleName, string move, int seed, float seconds, string output, TextWriter console)
    {
        var attacker = Attacker(styleName, move);
        var red = new Contender("victim", s => new BoxerBot(Still, s));
        var blue = new Contender(attacker.Name, s => new BoxerBot(attacker, s));
        BoxingGame? game = null;
        // 足の技は横の低い角度から撮る（俯瞰では足の上げ下げが見えない）。
        new VideoMaker(1280, 720, 30, speed: 0.5f) { SideView = move is "stomp" or "heel" }.Make(red, blue, seed, seconds, output, console,
            afterStep: time =>
            {
                if (time > 2.518f && game is not null)
                {
                    game.Player.Concussion = Out;
                }
            },
            redWear: MiniRing.Weapons.HandWear.None, blueWear: MiniRing.Weapons.HandWear.None, setup: g => game = g, referee: false);
    }
}
