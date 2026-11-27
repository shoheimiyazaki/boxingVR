using System.Globalization;
using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 調査（2026-11-27、届く間合いへの移し替えの確かめ）: ボット同士の試合（champion 対 swarmer、種 1〜4、各 60 秒）で、右の拳（両者とも左が前）の
/// 当たりの速さ・エネルギーと、当たった瞬間の胸と胸の距離・届く間合い（両肩から相手のあご）を平均する。土台の体と同じ API だけを使う。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class BoutCrossProbe
{
    [TestMethod]
    public void RightHandsInBouts()
    {
        var rows = new List<(float Speed, float Energy, float Chest, float Reach, bool Head)>();
        for (var seed = 1; seed <= 12; seed++)
        {
            using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s)));
            for (var i = 0; i < 14400 && !game.RoundOver; i++)
            {
                game.Step(default, 1f / 240f);
                foreach (var h in game.RecentHits.Where(h => h.Hand == Hand.Right && !h.Kick && !h.Elbow))
                {
                    var a = h.Attacker;
                    var v = h.Victim;
                    var center = 0.5f * (a.ShoulderPosition(Hand.Left) + a.ShoulderPosition(Hand.Right));
                    var chin = v.AsTarget.Point(AimGoal.Chin, center) ?? v.HeadPosition;
                    var reach = 0.5f * (Vector3.Distance(a.ShoulderPosition(Hand.Left), chin) + Vector3.Distance(a.ShoulderPosition(Hand.Right), chin)) / a.ArmReach;
                    var chest = Vector3.Distance(a.ChestPosition with { Y = 0f }, v.ChestPosition with { Y = 0f }) / a.ArmReach;
                    rows.Add((h.Speed, h.Energy, chest, reach, h.Zone == HitZone.Head));
                }
            }
        }

        foreach (var (name, set) in new[] { ("all", rows), ("head", rows.Where(r => r.Head).ToList()), ("body", rows.Where(r => !r.Head).ToList()) })
        {
            if (set.Count == 0)
            {
                continue;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"CROSS {name}: n {set.Count} speed {set.Average(r => r.Speed):0.00} m/s energy {set.Average(r => r.Energy):0.0} J (median {set.Select(r => r.Energy).Order().ElementAt(set.Count / 2):0.0}) chest {set.Average(r => r.Chest):0.000} reach {set.Average(r => r.Reach):0.000}"));
        }
    }
}
