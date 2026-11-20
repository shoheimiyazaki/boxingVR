using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>
/// 崩しの調べ（docs/GRAPPLING.md 段 1、通常の実行から外す）: 静かに立つ体の胸を水平に一定の力で引き続けたとき・前後左右へ歩くとき、
/// 支えを床から出す体（<see cref="FighterTuning.SupportFromGround"/>）と前の体を比べる。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class BalanceProbe
{
    private const float Dt = 1f / 240f;

    private static (PhysicsWorld World, Fighter Fighter, FighterCommand Idle) Stand(bool support)
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var f = Fighter.Spawn(world, FighterTuning.Default with { SupportFromGround = support }, "f", Vector3.Zero, 0f);
        var idle = new FighterCommand(Vector3.Zero, Vector3.UnitZ,
            new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));
        for (var i = 0; i < 240; i++)
        {
            f.ApplyControl(idle, Dt);
            world.Step(Dt);
        }

        return (world, f, idle);
    }

    [TestMethod]
    public void PullChest_ForceSweep()
    {
        foreach (var support in new[] { true, false })
        {
            foreach (var direction in new[] { ("forward", Vector3.UnitZ), ("side", Vector3.UnitX), ("back", -Vector3.UnitZ) })
            {
                foreach (var weights in new[] { 0.2f, 0.4f, 0.6f, 0.8f, 1.2f })
                {
                    var (world, f, idle) = Stand(support);
                    using var _ = world;
                    var start = f.PelvisPosition;
                    var force = direction.Item2 * (weights * f.TotalMass * 9.81f);
                    var minUp = 1f;
                    var downAt = -1f;
                    for (var i = 0; i < 240 * 2; i++)
                    {
                        f.ApplyControl(idle, Dt);
                        if (i < 240)
                        {
                            world.ApplyLinearImpulse(f.Parts.Chest, force * Dt);
                        }

                        world.Step(Dt);
                        minUp = MathF.Min(minUp, f.Uprightness);
                        if (downAt < 0f && f.State != FighterState.Standing)
                        {
                            downAt = i * Dt;
                        }
                    }

                    var moved = f.PelvisPosition - start;
                    Console.WriteLine($"{(support ? "new" : "old")} {direction.Item1,-8} pull {weights,4:0.0} × weight: moved {new Vector2(moved.X, moved.Z).Length() * 100f,6:0.0} cm  minUpright {minUp:0.00}  down {(downAt < 0f ? "-" : $"{downAt:0.00} s")}");
                }
            }
        }
    }

    [TestMethod]
    public void Walk_AllDirections()
    {
        foreach (var support in new[] { true, false })
        {
            foreach (var (name, dir) in new[] { ("fwd", Vector3.UnitZ), ("back", -Vector3.UnitZ), ("left", Vector3.UnitX), ("right", -Vector3.UnitX), ("diag-back", Vector3.Normalize(new Vector3(1f, 0f, -1f))) })
            {
                foreach (var speed in new[] { 0.6f, 1f })
                {
                    var (world, f, idle) = Stand(support);
                    using var _ = world;
                    var start = f.PelvisPosition;
                    var minUp = 1f;
                    var falls = 0;
                    var wasUp = true;
                    for (var i = 0; i < 240 * 3; i++)
                    {
                        f.ApplyControl(idle with { MoveWorld = dir * speed }, Dt);
                        world.Step(Dt);
                        minUp = MathF.Min(minUp, f.Uprightness);
                        var up = f.State == FighterState.Standing;
                        falls += wasUp && !up ? 1 : 0;
                        wasUp = up;
                    }

                    var moved = Vector3.Dot(f.PelvisPosition - start, dir);
                    Console.WriteLine($"{(support ? "new" : "old")} {name,-9} {speed:0.0}: moved {moved * 100,6:0.0} cm  minUp {minUp:0.00} falls {falls}");
                }
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class BlackoutBalanceProbe
{
    [TestMethod]
    public void Blackout_Trace()
    {
        const float dt = 1f / 60f;
        var still = MiniRing.Bots.BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new MiniRing.Bots.BoxerBot(still, s), s => new MiniRing.Bots.BoxerBot(still, s)));
        for (var t = 0f; t < 1.5f; t += dt)
        {
            game.Step(default, dt);
        }

        var f = game.Cpu;
        f.Concussion = 0.75f;
        for (var t = 0f; t < 3f; t += dt)
        {
            game.Step(default, dt);
            Console.WriteLine($"t {t:0.00} {f.State} bal {f.Balance:0.00} margin {f.SupportMargin * 100:0.0} up {f.Uprightness:0.00} pel {(f.PelvisPosition.Y - BoxingGame.RingSurfaceHeight) * 100:0.0} tone {f.Tone:0.00} hull {f.SupportHull.Count}");
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class RiseProbe
{
    [TestMethod]
    public void Rise_Trace()
    {
        const float dt = 1f / 240f;
        using var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
        var f = Fighter.Spawn(world, FighterTuning.Default, "f", Vector3.Zero, 0f);
        var idle = new FighterCommand(Vector3.Zero, Vector3.UnitZ,
            new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));
        for (var i = 0; i < 240; i++)
        {
            f.ApplyControl(idle, dt);
            world.Step(dt);
        }

        var dir = Environment.GetEnvironmentVariable("FALL") == "back" ? -Vector3.UnitZ : Vector3.UnitZ;
        f.Knockdown(dir, permanent: false);
        world.ApplyLinearImpulse(f.Parts.Chest, dir * (2.5f * world.GetMass(f.Parts.Chest)));
        world.ApplyLinearImpulse(f.Parts.Head, dir * (2.5f * world.GetMass(f.Parts.Head)));
        var standAt = -1f;
        for (var i = 0; i < 240 * 8; i++)
        {
            f.ApplyControl(idle, dt);
            world.Step(dt);
            if (standAt < 0f && i > 240 && f.State == FighterState.Standing)
            {
                standAt = i * dt;
            }

            if (i % 48 == 0)
            {
                Console.WriteLine($"t {i * dt:0.0} {f.State} pel {f.PelvisPosition.Y * 100:0.0} up {f.Uprightness:0.00} rise {f.Rise:0.00} cap {f.RiseCap:0.00} limbs {f.LimbSupport:0.00} hull {f.SupportHull.Count} feet {f.FeetHullCount} fd {f.FeetDistance * 100:0.0} ankles {f.AnklePosition(Hand.Left).Y * 100:0.0}/{f.AnklePosition(Hand.Right).Y * 100:0.0}");
            }
        }

        Console.WriteLine($"stood at {standAt:0.00}");
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ResonanceProbe
{
    [TestMethod]
    public void SwaySweep()
    {
        const float dt = 1f / 240f;
        var amp = float.Parse(Environment.GetEnvironmentVariable("AMP") ?? "0.3", System.Globalization.CultureInfo.InvariantCulture);
        var ki = float.Parse(Environment.GetEnvironmentVariable("KI") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var hz in new[] { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f, 4f })
        {
            using var world = new PhysicsWorld();
            world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(5f, 0.5f, 5f));
            var f = Fighter.Spawn(world, FighterTuning.Default with { BalanceKi = ki }, "f", Vector3.Zero, 0f);
            var idle = new FighterCommand(Vector3.Zero, Vector3.UnitZ,
                new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
                new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false));
            for (var i = 0; i < 240; i++)
            {
                f.ApplyControl(idle, dt);
                world.Step(dt);
            }

            var start = f.CenterOfMass;
            var maxSway = 0f;
            var minBalance = 1f;
            var fellAt = -1f;
            for (var i = 0; i < 240 * 4; i++)
            {
                var t = i * dt;
                f.ApplyControl(idle, dt);
                var force = Vector3.UnitZ * (amp * f.TotalMass * 9.81f * MathF.Sin(2f * MathF.PI * hz * t));
                world.ApplyLinearImpulse(f.Parts.Chest, force * dt);
                world.Step(dt);
                var c = f.CenterOfMass - start;
                maxSway = MathF.Max(maxSway, float.IsFinite(f.SupportMargin) ? f.SupportMargin : 0f);
                minBalance = MathF.Min(minBalance, f.Balance);
                if (fellAt < 0f && f.State != FighterState.Standing)
                {
                    fellAt = t;
                }
            }

            Console.WriteLine($"ki {ki} amp {amp} {hz,4:0.00} Hz: sway {maxSway * 100,5:0.0} cm  minBalance {minBalance:0.00}  fell {(fellAt < 0f ? "-" : $"{fellAt:0.0}s")}");
        }
    }
}
