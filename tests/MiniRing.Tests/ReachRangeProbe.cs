using System.Globalization;
using System.Numerics;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// 調査（2026-11-27、ボットの間合いを届く点で測る形への移し替え）: 守らない相手と胸と胸の間合い R [腕] に寄せて構えたとき、
/// 自分の両肩の真ん中から相手のあごまでの距離 [腕]（届く間合い）を出す。土台の体（145 cm）で測った R → 届く間合い の対応で、
/// 前の体で磨いたボットの間合いの値を移す。使うのは両方の版にある API だけ。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ReachRangeProbe
{
    [TestMethod]
    public void ChestToReach()
    {
        foreach (var range in new[] { 0.6f, 0.8f, 0.95f, 1.1f, 1.3f, 1.6f, 1.9f, 2.2f })
        {
            using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", range, false), 1, 0.06f);
            while (bout.PunchTime is null)
            {
                bout.Step();
            }

            var f = bout.Game.Player;
            var center = (f.ShoulderPosition(Hand.Left) + f.ShoulderPosition(Hand.Right)) * 0.5f;
            var chin = bout.Game.PlayerSense.OpponentTargets?.Point(AimGoal.Chin, center) ?? bout.Game.Cpu.HeadPosition;
            var reach = Vector3.Distance(center, chin) / f.ArmReach;
            float From(Hand hand) => Vector3.Distance(f.ShoulderPosition(hand), bout.Game.PlayerSense.OpponentTargets?.Point(AimGoal.Chin, f.ShoulderPosition(hand)) ?? bout.Game.Cpu.HeadPosition) / f.ArmReach;
            var mean = 0.5f * (From(Hand.Left) + From(Hand.Right));
            var cpu = bout.Game.Cpu;
            var oldThreat = Vector3.Distance(cpu.ChestPosition + new Vector3(0f, 0.25f * cpu.ArmReach, 0f), f.HeadPosition) / cpu.ArmReach;
            var myChin = f.AsTarget.Point(AimGoal.Chin, 0.5f * (cpu.ShoulderPosition(Hand.Left) + cpu.ShoulderPosition(Hand.Right))) ?? f.HeadPosition;
            var fromOpp = 0.5f * (Vector3.Distance(cpu.ShoulderPosition(Hand.Left), myChin) + Vector3.Distance(cpu.ShoulderPosition(Hand.Right), myChin)) / cpu.ArmReach;
            float Flat(Vector3 a, Vector3 b) => Vector3.Distance(a with { Y = 0f }, b with { Y = 0f });
            float FlatFrom(Hand hand) => Flat(f.ShoulderPosition(hand), bout.Game.PlayerSense.OpponentTargets?.Point(AimGoal.Chin, f.ShoulderPosition(hand)) ?? bout.Game.Cpu.HeadPosition);
            var flatMine = 0.5f * (FlatFrom(Hand.Left) + FlatFrom(Hand.Right)) / f.ArmReach;
            var flatTheirs = 0.5f * (Flat(cpu.ShoulderPosition(Hand.Left), myChin) + Flat(cpu.ShoulderPosition(Hand.Right), myChin)) / cpu.ArmReach;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"FLAT chest {bout.Distance:0.000} -> mine {flatMine:0.000} theirs {flatTheirs:0.000} oldThreat {oldThreat:0.000}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"THREAT chest {bout.Distance:0.000} -> oldThreat {oldThreat:0.000} fromOpp {fromOpp:0.000}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"SHOULDERS chest {bout.Distance:0.000} -> mean {mean:0.000} left {From(Hand.Left):0.000} right {From(Hand.Right):0.000} glove/arm {f.GloveRadius / f.ArmReach:0.000}"));
            var offset = (center - f.ChestPosition) / f.ArmReach;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"REACH chest {bout.Distance:0.000} -> reach {reach:0.000} (arm {f.ArmReach:0.000} m, shoulder-center above chest {offset.Y:0.000} arm, settled {bout.Settled})"));
        }
    }
}
