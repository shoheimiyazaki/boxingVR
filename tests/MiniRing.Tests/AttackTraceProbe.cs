using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>攻撃のドリルの 1 発を追う調査用: 右ストレートの拳と相手の頭の距離・拳の速さ・拳の目標の先走りを、溜めの長さ別に。</summary>
[TestClass]
public sealed class AttackTraceProbe
{
    [TestMethod]
    [DataRow(0f)]
    [DataRow(0.2f)]
    public void OneCross(float telegraph)
    {
        var attacker = (DefenseDrill.Attacker(PunchKind.Straight) with { Name = "a", Telegraph = telegraph, ComboWeights = new Dictionary<string, float> { ["2"] = 1f } }).Validated();
        var target = BotCatalog.Boxer with { Name = "t", GuardHeight = 0.2f, Attacks = false, DefenseRate = 0f, Prediction = 0f, CounterRate = 0f, ClinchRate = 0f, Circling = 0f, HeadMovement = 0f };
        var bots = new List<BoxerBot>();
        Spy? spy = null;
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("a", s => { var b = new BoxerBot(attacker, s); bots.Add(b); spy = new Spy(b); return spy; }, s => new BoxerBot(target, s)));
        var thrownAt = -1f;
        for (var t = 0f; t < 8f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
            var a = game.Cpu;
            if (thrownAt < 0f && bots.Sum(b => b.Stats.Techniques.Values.Sum(c => c.Thrown)) > 0)
            {
                thrownAt = t;
            }

            if (thrownAt >= 0f && t < thrownAt + telegraph + 0.35f && ((int)(t * 240f)) % 6 == 0)
            {
                var glove = a.GloveCenter(Hand.Right);
                var target3 = a.HandTargetWorld(Hand.Right);
                Console.WriteLine($"{t - thrownAt:0.000}s glove→head {Vector3.Distance(glove, game.Player.HeadPosition):0.000} m, target→head {Vector3.Distance(target3, game.Player.HeadPosition):0.000}, glove {a.GloveVelocity(Hand.Right).Length():0.00} m/s, lead {a.HandLead(Hand.Right).Length():0.000}, spring {spy!.Last.Right.Spring}, twist {spy.Last.TwistBias:0.00}, hits {game.RecentHits.Count(h => h.Attacker == a)}");
            }
        }
    }
}
