using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>倒れた体が起き上がるまで: 当てる運動量を変えて、ダウン・起き上がり・意識（DamageModelTests の写し。通常の実行から外す）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class GetUpScanProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    [TestCategory("Probe")]
    public void ScanMomentum()
    {
        foreach (var (mass, speed) in new[] { (0.55f, 2.1f), (0.6f, 2.1f), (0.65f, 2.1f), (0.7f, 2.1f), (0.65f, 2.2f), (0.7f, 2.2f) })
        {
            var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
            var corners = new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s));
            using var game = new BoxingGame(seed: 1, corners: corners);
            for (var t = 0f; t < 1.5f; t += Dt)
            {
                game.Step(default, Dt);
            }

            var cpu = game.Cpu;
            var point = cpu.HeadPosition + (cpu.Frame.Forward * cpu.HalfExtents.Head.Z) - new Vector3(0f, 0.8f * cpu.HalfExtents.Head.Y, 0f);
            var velocity = -cpu.Frame.Forward * speed;
            var damage = PunchMechanics.Damage(mass * speed) * PunchMechanics.Sharpness(speed);
            game.ApplyHit(new HitEvent(game.Player, Hand.Right, cpu, HitZone.Chin, point, speed, mass, 0.7f, damage), cpu.Parts.Head, velocity);
            var concussion = cpu.Concussion;
            var minCons = 1f;
            float downAt = -1f, upAt = -1f;
            for (var t = 0f; t < 16f; t += Dt)
            {
                game.Step(default, Dt);
                minCons = Math.Min(minCons, cpu.Consciousness);
                if (downAt < 0f && cpu.State == FighterState.Down)
                {
                    downAt = t;
                }

                if (downAt >= 0f && upAt < 0f && cpu.State == FighterState.Standing)
                {
                    upAt = t;
                }
            }

            Console.WriteLine($"GETUP mass {mass} speed {speed}: concussion {concussion:0.00} minCons {minCons:0.00} down {downAt:0.00} up {upAt:0.00} endCons {cpu.Consciousness:0.00}");
        }
    }
}
