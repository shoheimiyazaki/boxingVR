using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// ダメージモデルの調査用: 実戦の 1 発ごとに、急所・拳に乗った質量・速さ・運動エネルギーと、その後 2 ステップの頭の急なねじれ（HeadJolt の最大）を出す。
/// 自分の動きだけでの HeadJolt の大きさも出す（--logger "console;verbosity=detailed"）。
/// </summary>
[TestClass]
public sealed class ImpactProbe
{
    private int _downs;

    [TestMethod]
    public void HitsAndHeadJolts()
    {
        var rows = new List<(string Zone, float Mass, float Speed, float Energy, float Damage, float Jolt)>();
        var idle = new List<float>();
        foreach (var (red, blue, seed) in new[] { ("champion", "swarmer", 1), ("champion", "jabber", 2), ("slugger", "swarmer", 3) })
        {
            var corners = new BoxingCorners(red, s => BotCatalog.Create(red, s), s => BotCatalog.Create(blue, s));
            using var game = new BoxingGame(seed: seed, corners: corners);
            var pending = new List<(HitEvent Hit, int Steps, float Jolt)>();
            var wasDown = (false, false);
            for (var i = 0; i < 240 * 30 && !game.RoundOver; i++)
            {
                game.Step(default, 1f / 240f);
                var down = (game.Player.State != FighterState.Standing, game.Cpu.State != FighterState.Standing);
                _downs += (down.Item1 && !wasDown.Item1 ? 1 : 0) + (down.Item2 && !wasDown.Item2 ? 1 : 0);
                wasDown = down;
                for (var k = pending.Count - 1; k >= 0; k--)
                {
                    var p = pending[k];
                    p = (p.Hit, p.Steps + 1, p.Jolt);
                    if (p.Steps >= 3)
                    {
                        var h = p.Hit;
                        rows.Add((h.Zone.ToString(), h.ImpactMass, h.Speed, 0.5f * h.ImpactMass * h.Speed * h.Speed, h.Damage, p.Jolt));
                        pending.RemoveAt(k);
                    }
                    else
                    {
                        pending[k] = p;
                    }
                }

                pending.AddRange(game.RecentHits.Select(h => (h, 0, h.Zone is HitZone.Chin or HitZone.Temple or HitZone.Head or HitZone.Neck ? h.Victim.LastHeadTwist : 0f)));
                if (game.RecentHits.Count == 0 && pending.Count == 0)
                {
                    idle.Add(MathF.Max(game.Player.HeadJolt, game.Cpu.HeadJolt));
                }
            }
        }

        idle.Sort();
        Console.WriteLine($"idle jolt p50 {idle[idle.Count / 2]:0.00} p99 {idle[(int)(idle.Count * 0.99)]:0.00} max {idle[^1]:0.00} rad/s");
        foreach (var zone in rows.GroupBy(r => r.Zone).OrderByDescending(g => g.Count()))
        {
            var j = zone.Select(r => r.Jolt).OrderBy(v => v).ToList();
            var e = zone.Select(r => r.Energy).OrderBy(v => v).ToList();
            Console.WriteLine($"{zone.Key,-12} n {zone.Count(),4}  energy p50 {e[e.Count / 2]:0.000} p90 {e[(int)(e.Count * 0.9)]:0.000} max {e[^1]:0.000} J  jolt p50 {j[j.Count / 2]:0.0} p90 {j[(int)(j.Count * 0.9)]:0.0} max {j[^1]:0.0} rad/s  damage p50 {zone.Select(r => r.Damage).OrderBy(v => v).ElementAt(zone.Count() / 2):0.0}");
        }

        Console.WriteLine($"knockdowns: {_downs}");
        foreach (var r in rows.OrderByDescending(r => r.Jolt).Take(12))
        {
            Console.WriteLine($"top jolt: {r.Zone} mass {r.Mass:0.00} speed {r.Speed:0.00} energy {r.Energy:0.000} damage {r.Damage:0.0} jolt {r.Jolt:0.0}");
        }
    }
}

/// <summary>ダウンの原因の調査用: ダウンした瞬間の意識・脳しんとう・脚の力・直前 1 秒の被弾を出す。</summary>
[TestClass]
public sealed class KnockdownProbe
{
    [TestMethod]
    public void WhyTheyGoDown()
    {
        foreach (var (red, blue, seed) in new[] { ("champion-v1", "jabber", 1), ("champion", "swarmer", 2), ("champion-v1", "boxer", 3) })
        {
            var corners = new BoxingCorners(red, s => BotCatalog.Create(red, s), s => BotCatalog.Create(blue, s));
            using var game = new BoxingGame(seed: seed, corners: corners);
            var recent = new List<(float T, HitEvent Hit)>();
            var was = (FighterState.Standing, FighterState.Standing);
            for (var i = 0; i < 240 * 60 && !game.RoundOver; i++)
            {
                var t = i / 240f;
                game.Step(default, 1f / 240f);
                recent.AddRange(game.RecentHits.Select(h => (t, h)));
                foreach (var (f, before) in new[] { (game.Player, was.Item1), (game.Cpu, was.Item2) })
                {
                    if (f.State == FighterState.Down && before == FighterState.Standing)
                    {
                        var hits = recent.Where(r => r.Hit.Victim == f && r.T > t - 1f).ToList();
                        Console.WriteLine($"{red} vs {blue} t {t:0.0}: {(f == game.Player ? red : blue)} down, conscious {f.Consciousness:0.00} concussion {f.Concussion:0.00} legs {MathF.Min(f.LegStrength(true), f.LegStrength(false)):0.00} upright {f.Uprightness:0.00} dead {f.IsDead}; hits in last 1s: {string.Join(", ", hits.Select(h => $"{h.Hit.Zone} {h.Hit.ImpactMass:0.0}kg {h.Hit.Speed:0.0}m/s"))}");
                    }
                }

                was = (game.Player.State, game.Cpu.State);
            }
        }
    }
}
