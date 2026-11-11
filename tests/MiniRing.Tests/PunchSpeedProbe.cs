using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 拳の速さが何で決まるかの調査用: 予定の時間で目標をなぞる打ち方と、目標を打ち抜く先へ一気に置いて筋肉の力で加速させる打ち方を、
/// 腕の力の上限と、拳を引いておく量（予備動作）別に比べる。
/// </summary>
[TestClass]
public sealed class PunchSpeedProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void WhatSetsTheFistSpeed()
    {
        foreach (var force in new[] { 35f, 15f, 8f })
        {
            foreach (var cock in new[] { 0f, 0.15f })
            {
                var timed = Peak(force, cock, timed: true);
                var launched = Peak(force, cock, timed: false);
                Console.WriteLine($"MaxHandForce {force}, fist cocked back {cock:0.00} reach: timed path {timed:0.00} m/s, launched {launched:0.00} m/s");
            }
        }
    }

    private static float Peak(float maxForce, float cock, bool timed)
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var tuning = FighterTuning.Default with { MaxHandForce = maxForce };
        var f = Fighter.Spawn(world, tuning, "f", Vector3.Zero, 0f);
        var forward = f.Frame.Forward;
        var left = AiControl.Guard(Hand.Left, f.ArmReach);
        var guard = AiControl.Guard(Hand.Right, f.ArmReach) - new Vector3(0f, 0f, cock * f.ArmReach);
        var aim = AiControl.Guard(Hand.Right, f.ArmReach) + new Vector3(0f, 0.05f * f.ArmReach, 0.75f * f.ArmReach);
        FighterCommand Command(Vector3 right) => new(Vector3.Zero, forward, new HandCommand(left, false), new HandCommand(right, false));
        for (var t = 0f; t < 1.5f; t += Dt)
        {
            f.ApplyControl(Command(guard), Dt);
            world.Step(Dt);
        }

        var peak = 0f;
        for (var t = 0f; t < 0.25f; t += Dt)
        {
            var right = timed ? Vector3.Lerp(guard, aim, MathF.Min(1f, t / 0.12f)) : aim;
            f.ApplyControl(Command(right), Dt);
            world.Step(Dt);
            peak = MathF.Max(peak, f.GloveVelocity(Hand.Right).Length());
        }

        return peak;
    }
}
