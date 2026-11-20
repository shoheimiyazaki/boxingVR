using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 相手の弱り（意識の薄れ Faintness）が、見える手がかりにどう出るか: 構えの下がり（頭と拳の高さの差）・ふらつき（腰に対する頭の横のずれと
/// その速さ）・頭の高さ。ボット同士の試合で 0.1 秒ごとに両者を測り、本当の Faintness の帯ごとに平均を出す（立ち回りの層の読みの物差し）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class HurtCueProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void CuesByFaintness()
    {
        var bins = new[] { 0f, 0.05f, 0.2f, 0.4f, 0.7f, 1.01f };
        var sums = new double[bins.Length - 1, 6];
        var tired = new double[3, 4];
        foreach (var (a, b) in new[] { ("champion", "slugger"), ("swarmer", "champion"), ("slugger", "counter"), ("outboxer", "swarmer") })
        {
            for (var seed = 1; seed <= 3; seed++)
            {
                using var game = new BoxingGame(seed: seed, corners: new BoxingCorners(b, s => BotCatalog.Create(b, s), s => BotCatalog.Create(a, s)));
                var prev = new Dictionary<Fighter, Vector3>();
                var standY = new Dictionary<Fighter, float>();
                for (var i = 0; i < 24169 && !game.MatchOver; i++)
                {
                    game.Step(default, 1f / 240f);
                    if (i % 24 != 0)
                    {
                        continue;
                    }

                    foreach (var f in new[] { game.Player, game.Cpu })
                    {
                        if (f.State != FighterState.Standing)
                        {
                            prev.Remove(f);
                            continue;
                        }

                        var reach = f.ArmReach;
                        var head = f.HeadPosition;
                        var gloves = 0.5f * (f.GloveCenter(Hand.Left).Y + f.GloveCenter(Hand.Right).Y);
                        var drop = (head.Y - gloves) / reach;
                        var off = head - f.PelvisPosition;
                        off.Y = 0f;
                        var sway = off.Length() / reach;
                        var speed = prev.TryGetValue(f, out var p) ? Vector3.Distance(off, p) / 0.1f / reach : 0f;
                        prev[f] = off;
                        if (i < 403 || !standY.TryGetValue(f, out var stand))
                        {
                            stand = head.Y;
                            standY[f] = stand;
                        }

                        var height = head.Y / stand;
                        var faint = f.Faintness;
                        var k = Array.FindIndex(bins, x => x > faint) - 1;
                        sums[k, 0] += 1;
                        sums[k, 1] += drop;
                        sums[k, 2] += sway;
                        sums[k, 3] += speed;
                        sums[k, 4] += height;
                        sums[k, 5] += f.Stamina;
                        if (faint < 0.05f)
                        {
                            var t = f.Stamina < 0.2f ? 0 : f.Stamina < 0.5f ? 1 : 2;
                            tired[t, 0] += 1;
                            tired[t, 1] += drop;
                            tired[t, 2] += sway;
                            tired[t, 3] += speed;
                        }
                    }
                }
            }
        }

        Console.WriteLine("faint bin | n | guard drop/reach | sway/reach | sway speed/reach/s | head height ratio | stamina");
        for (var k = 0; k < bins.Length - 1; k++)
        {
            var n = sums[k, 0];
            Console.WriteLine($"{bins[k]:0.00}-{bins[k + 1]:0.00} | {n} | {sums[k, 1] / n:0.000} | {sums[k, 2] / n:0.000} | {sums[k, 3] / n:0.000} | {sums[k, 4] / n:0.000} | {sums[k, 5] / n:0.00}");
        }

        Console.WriteLine("not faint, by stamina (<0.2, <0.5, >=0.5) | n | drop | sway | speed");
        for (var t = 0; t < 3; t++)
        {
            var n = tired[t, 0];
            Console.WriteLine($"{t} | {n} | {tired[t, 1] / n:0.000} | {tired[t, 2] / n:0.000} | {tired[t, 3] / n:0.000}");
        }
    }
}
