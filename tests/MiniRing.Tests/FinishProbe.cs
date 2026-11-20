using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>弱った相手を詰める層の調べもの: 相手が本当に弱った間、詰める側のボットが何を目指し、何をしていたか。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class FinishProbe
{
    private static readonly (bool Fatigue, string Opponent)[] Cases = [(true, "dmitri"), (true, "marcus"), (true, "joao"), (false, "dmitri"), (false, "marcus"), (false, "joao")];
    [TestMethod]
    [TestCategory("Probe")]
    public void WhatTheSmallBoxerDoesInTheChance()
    {

        foreach (var (fatigue, opp) in Cases)
        {
            var red = CharacterRoster.Get("ramon");
            var blue = CharacterRoster.Get(opp);
            var redStyle = red.Resolve(blue.Id) with { FinishTempo = 1.5f, FinishPress = 1.5f, FinishSpend = 0.6f };
            var blueStyle = blue.Resolve(red.Id);
            BoxerBot? bot = null;
            var corners = new BoxingCorners(blueStyle.Name, s => new BoxerBot(blueStyle, s), s => bot = new BoxerBot(redStyle, s),
                PlayerWear: red.Wear, CpuWear: blue.Wear, PlayerBody: red.Build, CpuBody: blue.Build);
            using var game = new BoxingGame(tuning: FighterTuning.Default with { StaminaFatigue = fatigue }, seed: 1, corners: corners);
            double n = 0, d = 0, desired = 0, rest = 0, defend = 0, attack = 0, stamina = 0, away = 0, open = 0, cmd = 0, toward = 0, close = 0;
            double stuck = 0, gg = 0, gBody = 0, gHead = 0, myGlove = 0;
            double fullN = 0, fullSpd = 0, fullHitN = 0, fullHitSpd = 0, backN = 0, midN = 0; var lastHit = -10f;
            for (var i = 0; i < 60 * 60; i++)
            {
                game.Step(default, 1f / 60f);
                if (game.RecentHits.Count > 0) { lastHit = i / 60f; }
                if (bot is null || game.Player.State != FighterState.Standing || game.Cpu.State != FighterState.Standing || game.Cpu.Faintness <= 0.2f)
                {
                    continue;
                }

                var dv = game.Cpu.ChestPosition - game.Player.ChestPosition;
                dv.Y = 0f;
                var dist = dv.Length();
                var dbg = bot.ProbeState;
                n++;
                d += dist / game.Player.ArmReach;
                desired += dbg.Desired;
                rest += dbg.RestNeed;
                defend += dbg.Defending ? 1 : 0;
                attack += dbg.Attacking ? 1 : 0;
                stamina += game.Player.Stamina;
                open += bot.Opening;
                var v = game.Cpu.ChestVelocity;
                away += System.Numerics.Vector3.Dot(v with { Y = 0f }, dv / MathF.Max(1e-3f, dist));
                cmd += System.Numerics.Vector3.Dot(game.Player.LastCommand.ClampedMove, dv / MathF.Max(1e-3f, dist));
                toward += System.Numerics.Vector3.Dot(game.Player.ChestVelocity with { Y = 0f }, dv / MathF.Max(1e-3f, dist));
                close += dist / game.Player.ArmReach < 1.2f ? 1 : 0;
                var cmdNow = System.Numerics.Vector3.Dot(game.Player.LastCommand.ClampedMove, dv / MathF.Max(1e-3f, dist));
                var spdNow = System.Numerics.Vector3.Dot(game.Player.ChestVelocity with { Y = 0f }, dv / MathF.Max(1e-3f, dist)) - System.Numerics.Vector3.Dot(game.Cpu.ChestVelocity with { Y = 0f }, dv / MathF.Max(1e-3f, dist));
                if (cmdNow > 0.8f) { if (i / 60f - lastHit < 0.4f) { fullHitN++; fullHitSpd += spdNow; } else { fullN++; fullSpd += spdNow; } } else if (cmdNow < -0.1f) { backN++; } else { midN++; }
                if (cmdNow > 0.3f && spdNow < 0.03f && !bot.ProbeState.Defending)
                {
                    stuck++;
                    var r = game.Player.ArmReach;
                    var mine = new[] { game.Player.GloveCenter(Hand.Left), game.Player.GloveCenter(Hand.Right) };
                    var theirs = new[] { game.Cpu.GloveCenter(Hand.Left), game.Cpu.GloveCenter(Hand.Right) };
                    gg += mine.SelectMany(a => theirs.Select(b => System.Numerics.Vector3.Distance(a, b))).Min() / r;
                    gBody += mine.Select(a => System.Numerics.Vector3.Distance(a, game.Cpu.ChestPosition)).Min() / r;
                    gHead += theirs.Select(b => System.Numerics.Vector3.Distance(b, game.Player.HeadPosition)).Min() / r;
                    myGlove += mine.Select(a => System.Numerics.Vector3.Distance(a, game.Player.ChestPosition)).Average() / r;
                }
            }

            Console.WriteLine($"fatigue {fatigue} ramon vs {opp}: chance {n / 60:0.0}s d/reach {d / n:0.00} desired {desired / n:0.00} restNeed {rest / n:0.00} defending {defend / n:P0} attacking {attack / n:P0} stamina {stamina / n:0.00} opening {open / n:0.00} opp moving away {away / n:0.00} m/s, my move command toward {cmd / n:0.00}, my speed toward {toward / n:0.00} m/s, time within 1.2 reach {close / n:P0}, reach {game.Player.ArmReach:0.00} m");
            Console.WriteLine($"   approach give-ups in the whole bout: {bot?.ApproachGiveUps}, thrown {bot?.Stats.Techniques.Values.Sum(t => t.Thrown)}");
            Console.WriteLine($"   command: full forward {(fullN + fullHitN) / n:P0} (closing {fullSpd / Math.Max(1, fullN):0.000} m/s without blows, {fullHitSpd / Math.Max(1, fullHitN):0.000} m/s within 0.4 s of a blow), weak/sideways {midN / n:P0}, backward {backN / n:P0}");
            Console.WriteLine($"   stuck (pushing forward, not closing, not defending) {stuck / n:P0}: closest glove-glove {gg / stuck:0.00}, my glove-their chest {gBody / stuck:0.00}, their glove-my head {gHead / stuck:0.00}, my glove from my chest {myGlove / stuck:0.00} (×my reach)");
        }
    }
}
