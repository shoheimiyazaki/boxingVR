using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

[TestClass]
public sealed class StaminaBudgetTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    [DataRow(0f, 1f, 0f, 0f)]
    [DataRow(2f, 1f, 0.1f, 0.3f)]
    [DataRow(-2f, 1f, 0.1f, 0.01f)]
    [DataRow(0f, 0f, 2f, 0.5f)]
    [DataRow(1f, 2f, 3f, 10f)]
    public void ForceLimiter_RespectsWorkBudgetIncludingAccelerationAndHolding(float linear, float quadratic, float holding, float budget)
    {
        var scale = MuscleWork.ForceScale(linear, quadratic, holding, budget);
        var work = MathF.Max(0f, (linear * scale) + (quadratic * scale * scale)) + (holding * scale);
        Assert.IsTrue(scale is >= 0f and <= 1f);
        Assert.IsLessThanOrEqualTo(budget + 1e-6f, work);
        if (scale < 0.999f)
        {
            var more = scale + 0.001f;
            Assert.IsGreaterThan(budget, MathF.Max(0f, (linear * more) + (quadratic * more * more)) + (holding * more));
        }
    }

    [TestMethod]
    public void BothArmsWalkingAndSpring_ShareOneBudget()
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var tuning = FighterTuning.Default with { StaminaCapacity = 2f, StaminaRecoveryPower = 0f };
        var fighter = Fighter.Spawn(world, tuning, "budget", Vector3.Zero, 0f);
        for (var step = 0; step < 1200; step++)
        {
            var side = step % 120 < 60 ? 1f : -1f;
            var command = new FighterCommand(Vector3.UnitZ, Vector3.UnitZ,
                new HandCommand(new Vector3(0.13f, 0.25f, 0.6f + (side * 0.3f)) * fighter.ArmReach, false, 1f),
                new HandCommand(new Vector3(-0.13f, 0.25f, 0.6f - (side * 0.3f)) * fighter.ArmReach, false, 1f));
            fighter.ApplyControl(command, Dt);
            Assert.IsLessThanOrEqualTo(fighter.StepMuscleBudget + 1e-6f, fighter.StepMuscleAuthorized);
            Assert.IsTrue(fighter.Stamina is >= 0f and <= 1f);
            world.Step(Dt);
        }
    }

    [TestMethod]
    public void EmptyReserve_RestingRecoversButRepeatedPunchesDoNot()
    {
        var rest = Run(attacking: false, full: false);
        var attack = Run(attacking: true, full: false);
        Console.WriteLine($"10 s: rest {rest.Stamina:P2}, attack {attack.Stamina:P2}");
        Assert.IsGreaterThanOrEqualTo(0.6f, rest.Stamina);
        Assert.IsLessThan(0.1f, attack.Stamina);
    }

    [TestMethod]
    public void EmptyReserve_TenthPunchLosesSpeedAndImpactEnergy_WhileFullReserveDoesNot()
    {
        var empty = Run(attacking: true, full: false);
        var full = Run(attacking: true, full: true);
        Console.WriteLine($"empty peaks: {string.Join(", ", empty.Peaks.Select(p => p.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
        Console.WriteLine($"full peaks: {string.Join(", ", full.Peaks.Select(p => p.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
        Assert.IsGreaterThan(0.1f, empty.Peaks[0], "初発が実際に出ている");
        Assert.IsLessThanOrEqualTo(empty.Peaks[0] * 0.5f, empty.Peaks[9]);
        Console.WriteLine($"impact energy [J]: empty {empty.Hits[0].Energy:F4} -> {empty.Hits[9].Energy:F4}; full {full.Hits[0].Energy:F4} -> {full.Hits[9].Energy:F4}");
        Assert.IsLessThanOrEqualTo(empty.Hits[0].Energy * 0.5f, empty.Hits[9].Energy);
        Assert.IsGreaterThan(full.Peaks[0] * 0.9f, full.Peaks[9]);
        Assert.IsGreaterThan(full.Hits[0].Energy * 0.8f, full.Hits[9].Energy);

        // 衝突後の押し合い・損傷で次のパンチを変えないよう、測った実速度・実有効質量を別の受け手へ渡す。
        // ApplyHit を通し、処理されたイベントと共有のエネルギー式を確かめる。
        using var receiver = new BoxingGame();
        foreach (var sample in new[] { empty.Hits[0], empty.Hits[9], full.Hits[0], full.Hits[9] })
        {
            var hit = sample with { Attacker = receiver.Player, Victim = receiver.Cpu, Point = receiver.Cpu.ChestPosition, Zone = HitZone.Body };
            receiver.ApplyHit(hit, receiver.Cpu.Parts.Chest, Vector3.UnitZ * hit.Speed, carve: false);
            Assert.AreEqual(hit, receiver.LastHit);
        }
    }

    private static (float Stamina, float[] Peaks, HitEvent[] Hits) Run(bool attacking, bool full)
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var fighter = Fighter.Spawn(world, FighterTuning.Default, "stamina", Vector3.Zero, 0f);
        var left = AiControl.Guard(Hand.Left, fighter.ArmReach);
        var guard = AiControl.Guard(Hand.Right, fighter.ArmReach);
        FighterCommand Command(Vector3 right, float spring) => new(Vector3.Zero, Vector3.UnitZ,
            new HandCommand(left, false), new HandCommand(right, false, spring));
        for (var step = 0; step < 480; step++)
        {
            fighter.ApplyControl(Command(guard, 0f), Dt);
            world.Step(Dt);
        }

        fighter.Stamina = full ? 1f : 0f;
        var peaks = new float[20];
        var hits = new HitEvent[20];
        for (var step = 0; step < 2400; step++)
        {
            if (full)
            {
                fighter.Stamina = 1f; // 満タンの対照実験。疲れ以外のドリフトを区別する。
            }

            var punch = step / 120;
            var phase = step % 120;
            var extending = attacking && phase < 48;
            var aim = guard + new Vector3(0f, 0.05f, 0.75f) * fighter.ArmReach;
            var target = extending ? Vector3.Lerp(guard, aim, MathF.Min(1f, phase * Dt / 0.12f)) : guard;
            fighter.ApplyControl(Command(target, extending ? 1f : 0f), Dt);
            world.Step(Dt);
            if (extending)
            {
                var relative = fighter.GloveVelocity(Hand.Right) - fighter.ChestVelocity;
                peaks[punch] = MathF.Max(peaks[punch], relative.Length());
                var velocity = fighter.GloveVelocity(Hand.Right);
                var mass = fighter.ImpactMass(Hand.Right, velocity, out var bodyShare);
                var sample = new HitEvent(fighter, Hand.Right, fighter, HitZone.Body, fighter.GloveCenter(Hand.Right), velocity.Length(), mass, bodyShare, 0f);
                if (sample.Energy > hits[punch].Energy || (sample.Energy == hits[punch].Energy && sample.Speed > hits[punch].Speed))
                {
                    hits[punch] = sample;
                }
            }
        }

        Assert.AreEqual(FighterState.Standing, fighter.State, "疲れだけで倒れない");
        return (fighter.Stamina, peaks, hits);
    }
}
