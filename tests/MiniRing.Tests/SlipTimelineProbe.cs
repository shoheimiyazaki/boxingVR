using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 右ストレートのドリルの時系列（打ち始め、回避の始まり、頭の動き、拳と頭の距離、当たり）を出す調査用。
/// 合図は打ち始めと同時。標準出力に出す（--logger "console;verbosity=detailed"）。
/// </summary>
[TestClass]
public sealed class SlipTimelineProbe
{
    [TestMethod]
    public void RightCross_Timeline()
    {
        var attackers = new List<BoxerBot>();
        var defenders = new List<BoxerBot>();
        var move = new DefenseMove
        {
            Outside = true, HeadX = 0.6f, HeadY = -0.7f, HeadZ = -0.3f, Step = 1f, Duration = 0.3f, Rate = 60f, HandsUp = 0.3f, GloveAnchor = 0f,
            GloveWidth = 0.1f, GloveHeight = -0.03f, GloveForward = 0.25f, VsStraight = 1f,
        };
        var attacker = DefenseDrill.PunchAttacker("2");
        var defender = (DefenseDrill.Defender(DefenseBook.Default.With(Defense.Duck, move), defend: true, counters: false) with { DefenseRate = 0f }).Validated();
        var corners = new BoxingCorners("a", s => Keep(attackers, new BoxerBot(attacker, s)), s => Keep(defenders, new BoxerBot(defender, s, Defense.Duck)));
        var cue = DefenseDrill.DrillCue.For("2", attacker, 0f);
        using var game = new BoxingGame(seed: 3, corners: corners);
        var reach = game.Player.ArmReach;
        var thrownSoFar = 0;
        var throwAt = -1f;
        var headAtThrow = Vector3.Zero;
        var frameAtThrow = game.Player.Frame;
        var step = 0;
        for (var t = 0f; t < 4f; t += 1f / 240f, step++)
        {
            game.Step(default, 1f / 240f);
            cue.Update(t, attackers, defenders[^1]);
            var frame = throwAt >= 0f ? frameAtThrow : game.Player.Frame; // 打ち始めの向きで固定して測る（回る座標で測ると動きがずれる）
            var head = frame.InverseRotate(game.Player.HeadPosition);
            var thrown = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));
            if (thrown != thrownSoFar)
            {
                thrownSoFar = thrown;
                throwAt = t;
                frameAtThrow = game.Player.Frame;
                headAtThrow = frameAtThrow.InverseRotate(game.Player.HeadPosition);
                Console.WriteLine($"{t:0.000} THROW #{thrown}");
            }

            var since = t - throwAt;
            if (throwAt >= 0f && since <= 0.3f && step % 6 == 0)
            {
                var d = (head - headAtThrow) / reach;
                var glove = Vector3.Distance(game.Cpu.GloveCenter(Hand.Right), game.Player.HeadPosition) / reach;
                var g = (frame.InverseRotate(game.Cpu.GloveCenter(Hand.Right)) - headAtThrow) / reach;
                Console.WriteLine($"  +{since:0.000}s head moved ({d.X:+0.00;-0.00},{d.Y:+0.00;-0.00},{d.Z:+0.00;-0.00})R  glove from old head ({g.X:+0.00;-0.00},{g.Y:+0.00;-0.00},{g.Z:+0.00;-0.00})R  glove-head {glove:0.00}R");
            }

            foreach (var hit in game.RecentHits)
            {
                Console.WriteLine($"{t:0.000} HIT {(hit.Attacker == game.Cpu ? "attacker" : "defender")} {hit.Hand} {hit.Zone} {hit.Damage:0.0} (+{t - throwAt:0.000}s)");
            }
        }
    }

    [TestMethod]
    [DataRow(1.15f)]
    [DataRow(0.9f)]
    [DataRow(0.72f)]
    public void HeadSize_VersusOutsideSlip(float headScale)
    {
        var attackers = new List<BoxerBot>();
        var defenders = new List<BoxerBot>();
        var move = new DefenseMove
        {
            Outside = true, HeadX = 0.6f, HeadY = -0.7f, HeadZ = -0.3f, Step = 1f, Duration = 0.3f, Rate = 60f, HandsUp = 0.3f, GloveAnchor = 0f,
            GloveWidth = 0.1f, GloveHeight = -0.03f, GloveForward = 0.25f, VsStraight = 1f,
        };
        var attacker = DefenseDrill.PunchAttacker("2");
        var defender = (DefenseDrill.Defender(DefenseBook.Default.With(Defense.Duck, move), defend: true, counters: false) with { DefenseRate = 0f }).Validated();
        var corners = new BoxingCorners("a", s => Keep(attackers, new BoxerBot(attacker, s)), s => Keep(defenders, new BoxerBot(defender, s, Defense.Duck)));
        var cue = DefenseDrill.DrillCue.For("2", attacker, 0f);
        using var game = new BoxingGame(FighterTuning.Default with { HeadScale = headScale }, seed: 3, corners: corners);
        int head = 0, any = 0;
        var zones = new Dictionary<HitZone, int>();
        for (var t = 0f; t < 20f; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            cue.Update(t + (1f / 60f), attackers, defenders[^1]);
            foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Cpu))
            {
                any++;
                zones[hit.Zone] = zones.GetValueOrDefault(hit.Zone) + 1;
                head += hit.Zone is HitZone.Chin or HitZone.Temple or HitZone.Head or HitZone.Neck ? 1 : 0;
            }
        }

        var punches = attackers.Sum(a => a.Stats.Techniques.Values.Sum(c => c.Thrown));
        Console.WriteLine($"HeadScale {headScale:0.00}: punches {punches}, head hits {head}, any hits {any} ({string.Join(", ", zones.Select(z => $"{z.Key} {z.Value}"))})");
    }

    private static BoxerBot Keep(List<BoxerBot> list, BoxerBot bot)
    {
        list.Add(bot);
        return bot;
    }
}
