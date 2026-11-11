using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 意識の段階ごとの体の見え方の調査用: 意識を一定に保ったまま 3 秒立たせ、腰がどれだけ落ちるか・倒れるか・拳（ガード）がどれだけ下がるか。
/// </summary>
[TestClass]
public sealed class ConsciousnessProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void BodyAtEachLevelOfConsciousness()
    {
        foreach (var level in new[] { 1f, 0.75f, 0.65f, 0.55f, 0.45f, 0.35f, 0.29f, 0.25f, 0.2f, 0.15f, 0.1f })
        {
            var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
            using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
            var f = game.Cpu;
            for (var t = 0f; t < 1.5f; t += Dt)
            {
                game.Step(default, Dt);
            }

            var ground = f.AllParts.Min(id => game.World.GetState(id).Position.Y) - 0.01f;
            float Height() => f.PelvisPosition.Y - ground;
            var stand = Height();
            var guard = f.GloveCenter(Hand.Left).Y - f.ChestPosition.Y;
            var minPelvis = stand;
            var downAt = -1f;
            var sum = 0f;
            var n = 0;
            for (var t = 0f; t < 3f; t += Dt)
            {
                f.Concussion = MathF.Max(0f, 1f - level);
                game.Step(default, Dt);
                minPelvis = MathF.Min(minPelvis, Height());
                if (t > 1f)
                {
                    sum += Height();
                    n++;
                }

                if (downAt < 0f && f.State != FighterState.Standing)
                {
                    downAt = t;
                }
            }

            var guardNow = f.GloveCenter(Hand.Left).Y - f.ChestPosition.Y;
            Console.WriteLine($"consciousness {level:0.0} (tone {f.Tone:0.00}): pelvis settles {(sum / Math.Max(1, n)) / stand:P0} of standing, lowest {minPelvis / stand:P0}, " +
                $"glove {guard:0.000} → {guardNow:0.000} m above chest, {(downAt >= 0f ? $"down at {downAt:0.00}s ({f.State})" : "stays up")}, tilt {f.Uprightness:0.00}");
        }
    }

    [TestMethod]
    public void RecoveryFromADeepConcussion()
    {
        // 一度だけ深く意識を飛ばし、その後の意識・腰を上げようとする高さ・実際の腰の高さ・状態を 0.5 秒ごとに見る。
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
        var f = game.Cpu;
        for (var t = 0f; t < 1.5f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var ground = f.AllParts.Min(id => game.World.GetState(id).Position.Y) - 0.01f;
        var stand = f.PelvisPosition.Y - ground;
        f.Concussion = 0.95f;
        var next = 0f;
        for (var t = 0f; t < 16f; t += Dt)
        {
            game.Step(default, Dt);
            if (t >= next)
            {
                next += 0.5f;
                Console.WriteLine($"t {t,5:0.0}s consciousness {f.Consciousness:0.00} tone {f.Tone:0.00} reach {f.StandReach:0.00} rise {f.Rise:0.00} pelvis {(f.PelvisPosition.Y - ground) / stand:P0} upright {f.Uprightness:0.00} {f.State}");
            }
        }
    }
}

[TestClass]
public sealed class HeadProportionProbe
{
    [TestMethod]
    [DataRow(1.15f)]
    [DataRow(1.0f)]
    [DataRow(0.9f)]
    public void BodyHeightInHeads(float headScale)
    {
        var tuning = FighterTuning.Default with { HeadScale = headScale };
        using var world = new Aigis.Physics.PhysicsWorld();
        var f = Fighter.Spawn(world, tuning, "f", System.Numerics.Vector3.Zero, 0f);
        var ground = f.AllParts.Min(id => world.GetState(id).Position.Y - f.HalfExtentsOf(id).Y);
        var top = world.GetState(f.Parts.Head).Position.Y + f.HalfExtents.Head.Y;
        var head = 2f * f.HalfExtents.Head.Y;
        Console.WriteLine($"HeadScale {headScale}: spawn height {top - ground:0.000} m, head {head:0.000} m (w {2f * f.HalfExtents.Head.X:0.000}, d {2f * f.HalfExtents.Head.Z:0.000}), {(top - ground) / head:0.00} heads tall");
    }
}

[TestClass]
public sealed class HeadCarveProbe
{
    [TestMethod]
    [DataRow(1.15f, 450f, 45f)]
    [DataRow(0.8f, 160f, 16f)]
    [DataRow(0.8f, 120f, 12f)]
    public void BrainLeftAfterAHeavyBlow(float headScale, float maxCarve, float perJoule)
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        foreach (var (name, low) in new[] { ("chin", 0.8f), ("centre", 0f) })
        {
            using var game = new BoxingGame(FighterTuning.Default with { HeadScale = headScale, MaxCarve = maxCarve, CarvePerJoule = perJoule }, seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
            for (var t = 0f; t < 1.5f; t += 1f / 240f)
            {
                game.Step(default, 1f / 240f);
            }

            var cpu = game.Cpu;
            var point = cpu.HeadPosition + (cpu.Frame.Forward * cpu.HalfExtents.Head.Z) - new System.Numerics.Vector3(0f, low * cpu.HalfExtents.Head.Y, 0f);
            const float mass = 4.5f, speed = 2.2f;
            var damage = PunchMechanics.Damage(mass * speed) * PunchMechanics.Sharpness(speed);
            game.ApplyHit(new HitEvent(game.Player, Hand.Right, cpu, HitZone.Chin, point, speed, mass, 0.7f, damage), cpu.Parts.Head, -cpu.Frame.Forward * speed);
            Console.WriteLine($"head {headScale}, MaxCarve {maxCarve}, {name}: brain function {cpu.Voxels!.BrainFunction(FighterTuning.Default.OrganDeathFraction):0.00}, twist {cpu.LastHeadTwist:0} rad/s");
        }
    }
}
