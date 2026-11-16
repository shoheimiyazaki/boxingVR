using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

[TestClass]
public sealed class StaminaBudgetTests
{
    private const float Dt = 1f / 240f;

    // 連打の型: 0.3 秒に 1 発（出す 0.12 秒・拳の目標は腕の長さの 1.6 倍先・0.06 秒で届かせる強い一発。拳の速さは胸に対して約 3.3 m/s、
    // 1 発 3 J 前後）。1 秒に 3 発強で、10 W 前後を使い続ける全力の攻めの仕事率（P、試合の全力の攻めの平均 8〜10 W と同じ桁）。
    private const int CycleSteps = 72;
    private const int ExtendSteps = 29;
    private const float PunchReach = 1.6f;
    private const float PunchRamp = 0.06f;
    private const int PunchesPerRun = 33;

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
        Assert.IsGreaterThanOrEqualTo(0.95f, rest.Stamina, "構えて休めば 10 秒で満タンへ戻る");
        Assert.IsLessThan(0.1f, attack.Stamina, "全力の連打を続けるあいだは、戻った分もすぐ使い切る");
    }

    [TestMethod]
    public void EmptyReserve_TenthPunchLosesSpeedAndImpactEnergy_WhileFullReserveDoesNot()
    {
        var empty = Run(attacking: true, full: false);
        var full = Run(attacking: true, full: true);
        Console.WriteLine($"empty peaks: {string.Join(", ", empty.Peaks.Select(p => p.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
        Console.WriteLine($"full peaks: {string.Join(", ", full.Peaks.Select(p => p.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
        Assert.IsGreaterThan(0.1f, empty.Peaks[0], "初発が実際に出ている");
        Console.WriteLine($"impact energy [J]: empty {empty.Hits[0].Energy:F4} -> {empty.Hits[9].Energy:F4}; full {full.Hits[0].Energy:F4} -> {full.Hits[9].Energy:F4}");
        // 空から連打し続けると、心肺の供給の分（連打の P で減る回復、約 5 W）しか仕事が出ない。満タンの 1 発目に対して、拳の速さは 0.65 倍以下、
        // 打撃のエネルギーは半分以下（2026-11-15 の測定: 速さ 0.585 倍・エネルギー 0.445 倍）。以前は「空の 1 発目の半分以下」（10 発目は 0.17 m/s・0 J）で、
        // 出せなかった仕事まで回復を止めていたため空で押すと何もできなくなっていた。今は動きながらも戻る分だけ打てる。
        Assert.IsLessThanOrEqualTo(full.Peaks[0] * 0.65f, empty.Peaks[9]);
        Assert.IsLessThanOrEqualTo(full.Hits[0].Energy * 0.5f, empty.Hits[9].Energy);
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
        var peaks = new float[PunchesPerRun];
        var hits = new HitEvent[PunchesPerRun];
        for (var step = 0; step < PunchesPerRun * CycleSteps; step++)
        {
            if (full)
            {
                fighter.Stamina = 1f; // 満タンの対照実験。疲れ以外のドリフトを区別する。
            }

            var punch = step / CycleSteps;
            var phase = step % CycleSteps;
            var extending = attacking && phase < ExtendSteps;
            var aim = guard + new Vector3(0f, 0.05f, PunchReach) * fighter.ArmReach;
            var target = extending ? Vector3.Lerp(guard, aim, MathF.Min(1f, phase * Dt / PunchRamp)) : guard;
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

    /// <summary>
    /// スタミナの蓄えは筋肉の量（質量の比 H³M²）に比例する: 構えて歩いて行き来する 6 秒（回復なし）で使う割合は、大きい体（ドミトリー:
    /// 身長 1.15・筋肉 1.2、質量 2.2 倍）でも基準の体の 1.6 倍未満。残る差は、長い脚・腕を同じ拍子で動かすぶん 1 kg あたりの仕事が増える
    /// 物理の差（測って 1.31 倍）。直す前は蓄えが体格によらず 60 J で、質量に比例して増えた仕事がそのまま効き 2.33 倍だった
    /// （中継で質量 1.9・2.2 倍の 2 人が試合のほとんどを空のまま動けなかった。2026-11-16）。
    /// </summary>
    [TestMethod]
    public void BigBody_SameWalk_UsesAboutTheSameShareOfItsReserve()
    {
        var standard = 1f - WalkWithoutRecovery(BodyBuild.Standard);
        var big = 1f - WalkWithoutRecovery(new BodyBuild(Height: 1.15f, Muscle: 1.2f));
        Console.WriteLine($"6 s walk, no recovery: standard used {standard:P1}, big used {big:P1}, ratio {big / standard:0.00}");
        Assert.IsGreaterThan(0.05f, standard, "歩いて蓄えを使っている（比べる意味がある）");
        Assert.IsTrue(big / standard is > 0.9f and < 1.6f, $"大きい体も蓄えの同じくらいの割合しか使わない（比 {big / standard:0.00}）");
    }

    private static float WalkWithoutRecovery(BodyBuild build)
    {
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var fighter = Fighter.Spawn(world, FighterTuning.Default with { Build = build, StaminaRecoveryPower = 0f }, "walk", Vector3.Zero, 0f);
        var left = new HandCommand(AiControl.Guard(Hand.Left, fighter.ArmReach), false);
        var right = new HandCommand(AiControl.Guard(Hand.Right, fighter.ArmReach), false);
        for (var step = 0; step < 6 * 240; step++)
        {
            var move = (step / 240) % 2 == 0 ? Vector3.UnitZ : -Vector3.UnitZ;
            fighter.ApplyControl(new FighterCommand(move, Vector3.UnitZ, left, right), Dt);
            world.Step(Dt);
        }

        return fighter.Stamina;
    }
}
