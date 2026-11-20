using System.Globalization;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// スタミナの試合での効き方の調べ（2026-11-17）: 名簿のボット同士の 3 ラウンド（50.35 秒 × 3、休み 16.78 秒。人形の単位の 30 秒・10 秒）を、ラウンドごとに
/// 平均と最低のスタミナ・休みたい度合い（RestNeed &gt; 0.5）の時間・棒立ち（打っていない・守っていない・ほぼ動いていない）の時間・
/// 打った数・仕事率・持久力で見る。ユーザーの目標: 1 ラウンド目は全力で続けられ、3 ラウンド目は疲れる。棒立ちが少ない。
/// 調整値は STAMINA_TUNE（<see cref="StaminaFeelProbe.Tuning"/>）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class StaminaMatchProbe
{
    private const float Dt = 1f / 60f;

    private static readonly (string Red, string Blue, int Seed)[] Pairs =
        [("marcus", "ramon", 1), ("yoandry", "dmitri", 5), ("marcus", "dmitri", 5), ("ramon", "joao", 2),
         ("dmitri", "gaston", 3), ("yoandry", "marcus", 4), ("joao", "albert", 6), ("akira", "ramon", 7)];

    /// <summary>環境変数 STYLE_SET="ElbowTuck=1,RestTempo=0.5" で両方のボットのスタイルを上書きする。</summary>
    private static BotStyle Styled(BotStyle style)
    {
        var spec = Environment.GetEnvironmentVariable("STYLE_SET");
        if (string.IsNullOrWhiteSpace(spec))
        {
            return style;
        }

        var set = spec.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('='))
            .ToDictionary(p => p[0].Trim(), p => float.Parse(p[1], CultureInfo.InvariantCulture));
        return MiniRing.Arena.StyleSet.Apply(style, set);
    }

    private sealed class Round
    {
        public double N, Stamina, Rest, Idle, Power;
        public float Min = 1f, Endurance = 1f;
        public int Thrown, BodyAt, BodyIn;
        public double Wind, Work, WindBase = -1, WorkBase;
    }

    private static bool IsBody(HitZone zone) => zone is HitZone.Body or HitZone.Liver or HitZone.SolarPlexus or HitZone.Heart;

    [TestMethod]
    public void ThreeRounds()
    {
        var tuning = StaminaFeelProbe.Tuning();
        var totals = new Dictionary<int, Round>();
        var windByZone = new Dictionary<HitZone, (double Joules, int Hits)>();
        foreach (var (redId, blueId, seed) in Pairs)
        {
            var red = CharacterRoster.Get(redId);
            var blue = CharacterRoster.Get(blueId);
            BoxerBot? redBot = null;
            BoxerBot? blueBot = null;
            var redStyle = Styled(red.Resolve(blue.Id));
            var blueStyle = Styled(blue.Resolve(red.Id));
            var corners = new BoxingCorners(blueStyle.Name, s => blueBot = new BoxerBot(blueStyle, s), s => redBot = new BoxerBot(redStyle, s),
                PlayerWear: red.Wear, CpuWear: blue.Wear, PlayerBody: red.Build, CpuBody: blue.Build);
            using var game = new BoxingGame(tuning, seed: seed, corners: corners, rules: new MatchRules(50.35f, 3, 16.78f));
            var rounds = new Dictionary<(int Corner, int Round), Round>();
            var thrownBefore = new int[2];
            var lastRound = 1;
            // 胴打ちの追跡: 角ごと・手ごとに、いま出している拳が胴打ちか、もう当たりを数えたか。
            var open = new (bool Active, bool Body, bool Landed)[2, 2];
            for (var step = 0; step < (int)(235.0f / Dt) && !game.MatchOver; step++)
            {
                var windBefore = (game.Player.WindLost, game.Cpu.WindLost);
                game.Step(default, Dt);
                foreach (var hit in game.RecentHits)
                {
                    var before = hit.Victim == game.Player ? windBefore.Item1 : windBefore.Item2;
                    var lost = hit.Victim.WindLost - before;
                    var w = windByZone.GetValueOrDefault(hit.Zone);
                    windByZone[hit.Zone] = (w.Joules + (lost / Math.Max(1, game.RecentHits.Count(x => x.Victim == hit.Victim))), w.Hits + 1);
                }
                if (game.MatchRound != lastRound)
                {
                    lastRound = game.MatchRound;
                }

                if (game.InBreak)
                {
                    continue;
                }

                foreach (var hit in game.RecentHits)
                {
                    var c = hit.Attacker == game.Player ? 0 : 1;
                    var h = (int)hit.Hand;
                    if (!hit.Kick && open[c, h] is { Active: true, Body: true, Landed: false } && IsBody(hit.Zone)
                        && rounds.TryGetValue((1 - c, game.MatchRound), out var victimRound))
                    {
                        victimRound.BodyIn++;
                        open[c, h].Landed = true;
                    }
                }

                for (var corner = 0; corner < 2; corner++)
                {
                    var fighter = corner == 0 ? game.Player : game.Cpu;
                    var bot = corner == 0 ? redBot : blueBot;
                    if (bot is null || game.Player.State != FighterState.Standing || game.Cpu.State != FighterState.Standing)
                    {
                        continue;
                    }

                    if (!rounds.TryGetValue((corner, game.MatchRound), out var r))
                    {
                        r = rounds[(corner, game.MatchRound)] = new Round();
                        thrownBefore[corner] = bot.Stats.Techniques.Values.Sum(t => t.Thrown);
                    }

                    var probe = bot.ProbeState;
                    var speed = (fighter.ChestVelocity with { Y = 0f }).Length() / fighter.ArmReach;
                    r.N++;
                    r.Stamina += fighter.Stamina;
                    r.Min = MathF.Min(r.Min, fighter.Stamina);
                    r.Rest += probe.RestNeed > 0.5f ? 1 : 0;
                    r.Idle += !probe.Attacking && !probe.Defending && speed < 0.1787f ? 1 : 0;
                    r.Power += fighter.RecentMusclePower / fighter.Build.MassRatio;
                    r.Endurance = fighter.Endurance;
                    r.Thrown = bot.Stats.Techniques.Values.Sum(t => t.Thrown) - thrownBefore[corner];
                    if (r.WindBase < 0)
                    {
                        r.WindBase = fighter.WindLost;
                        r.WorkBase = fighter.WorkDone;
                    }

                    r.Wind = fighter.WindLost - r.WindBase;
                    r.Work = fighter.WorkDone - r.WorkBase;
                    foreach (var hand in new[] { Hand.Left, Hand.Right })
                    {
                        var (technique, toBody, _) = bot.PunchOf(hand);
                        var h = (int)hand;
                        if (technique is not null && !open[corner, h].Active)
                        {
                            open[corner, h] = (true, toBody, false);
                            if (toBody && rounds.TryGetValue((1 - corner, game.MatchRound), out var target))
                            {
                                target.BodyAt++;
                            }
                        }
                        else if (technique is null)
                        {
                            open[corner, h] = default;
                        }
                    }
                }
            }

            Console.WriteLine($"== {redId} vs {blueId} (seed {seed}) wins {game.PlayerWins}-{game.CpuWins} round {game.MatchRound}");
            Console.WriteLine("  corner R  stamina(avg/min)  rest  idle  P/kg-ratio[W]  thrown  endurance  wind[J]/work[J]  body in/at");
            foreach (var ((corner, round), r) in rounds.OrderBy(k => k.Key.Corner).ThenBy(k => k.Key.Round))
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {(corner == 0 ? redId : blueId),-8} {round}  {r.Stamina / r.N,5:P0} / {r.Min,4:P0}  {r.Rest / r.N,4:P0}  {r.Idle / r.N,4:P0}  {r.Power / r.N,5:0.0}  {r.Thrown,4}  {r.Endurance,5:0.00}  {r.Wind,5:0}/{r.Work,5:0}  {r.BodyIn}/{r.BodyAt}"));
                if (!totals.TryGetValue(round, out var t))
                {
                    t = totals[round] = new Round { Min = 0f, Endurance = 0f };
                }

                t.N++;
                t.Stamina += r.Stamina / r.N;
                t.Min += r.Min;
                t.Rest += r.Rest / r.N;
                t.Idle += r.Idle / r.N;
                t.Power += r.Power / r.N;
                t.Thrown += r.Thrown;
                t.Endurance += r.Endurance;
                t.Wind += r.Wind;
                t.Work += r.Work;
                t.BodyIn += r.BodyIn;
                t.BodyAt += r.BodyAt;
            }
        }

        Console.WriteLine("== stamina lost to bruising by the zone that was hit (J total / hits)");
        foreach (var (zone, w) in windByZone.OrderByDescending(kv => kv.Value.Joules))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {zone,-12} {w.Joules,6:0} J / {w.Hits,4} hits"));
        }

        Console.WriteLine("== average over fighters");
        foreach (var (round, t) in totals.OrderBy(k => k.Key))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  R{round}  stamina {t.Stamina / t.N:P0} min {t.Min / t.N:P0}  rest {t.Rest / t.N:P0}  idle {t.Idle / t.N:P0}  P {t.Power / t.N:0.0} W  thrown {t.Thrown / t.N:0.0}  endurance {t.Endurance / t.N:0.00}  wind {t.Wind / t.N:0} J vs work {t.Work / t.N:0} J  body landed {t.BodyIn}/{t.BodyAt}"));
        }
    }
}
