using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>疲れの見た目の調べ（2026-11-22、スタミナの仕様）: 連打の後と、空の体を立たせて、構えの高さ・あご・胸を 1 秒ごとに出す。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class FatigueLookProbe
{
    private const float Dt = 1f / 60f;

    private static FighterIntent Rush(float t)
    {
        var phase = t / 0.2014f;
        var index = (int)MathF.Floor(phase);
        var down = phase - index < 0.67f;
        return new FighterIntent(Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
    }

    private static void Run(string name, Func<float, FighterIntent> plan, float seconds, float? startStamina, float work = 0f)
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        using var game = new BoxingGame(StaminaFeelProbe.Tuning() with { ConcussionSpin = float.PositiveInfinity, CoreToneFloor = 1f }, seed: 3, corners: corners);
        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl!).Input = new FighterIntent(Vector2.Zero, Evade: true);
        if (startStamina is { } s0)
        {
            game.Player.Stamina = s0;
        }

        game.Player.SetWorkDone(work);
        Console.WriteLine($"== {name}");
        Console.WriteLine("  t | stam O2 L R trunk winded | vigor L R | glove-shoulder Y [m] L R | head-chest Y | chin(head pitch deg) chest pitch deg | chest Y");
        for (var step = 1; step <= (int)(seconds / Dt); step++)
        {
            var t = step * Dt;
            control.Input = plan(t);
            game.Step(new BoxingInput(default), Dt);
            if (step % 30 == 0)
            {
                var p = game.Player;
                string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
                var up = Vector3.Transform(Vector3.UnitY, p.HeadOrientation);
                var fwd = Vector3.Transform(Vector3.UnitZ, p.HeadOrientation);
                var cfwd = Vector3.Transform(Vector3.UnitZ, p.TorsoOrientation);
                // 前を向いた体の局所での前傾（正 = 前へ倒れる）。顔の向きの水平成分で測る。
                static float Pitch(Vector3 v) => MathF.Atan2(-v.Y, MathF.Sqrt((v.X * v.X) + (v.Z * v.Z))) * 180f / MathF.PI;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{t,5:0.0} | {F(p.Stamina)} {F(p.OxygenOf(MuscleRegion.LeftArm))} {F(p.OxygenOf(MuscleRegion.RightArm))} {F(p.OxygenOf(MuscleRegion.Trunk))} {F(p.Winded)} | {F(p.VigorOf(MuscleRegion.LeftArm))} {F(p.VigorOf(MuscleRegion.RightArm))} | {F(p.GloveCenter(Hand.Left).Y - p.ShoulderPosition(Hand.Left).Y)} {F(p.GloveCenter(Hand.Right).Y - p.ShoulderPosition(Hand.Right).Y)} | {F(p.HeadPosition.Y - p.ChestPosition.Y)} | {Pitch(fwd):0.0} {Pitch(cfwd):0.0} | {F(p.ChestPosition.Y)}"));
            }
        }
    }

    [TestMethod]
    public void RushThenGuard() => Run("rush 10 s then guard 20 s", t => t < 10f ? Rush(t) : default, 30f, null);

    [TestMethod]
    public void EmptyBodyGuard() => Run("empty at start, guard 12 s", _ => default, 12f, 0f);

    [TestMethod]
    public void LateRoundEmptyGuard() => Run("late (work 60 kJ) empty, guard 20 s", _ => default, 20f, 0f, 60000f);

    [TestMethod]
    public void LateRoundRush() => Run("late (work 60 kJ) rush 8 s then guard", t => t < 8f ? Rush(t) : default, 24f, null, 60000f);

    [TestMethod]
    public void FreshGuard() => Run("fresh guard 6 s", _ => default, 6f, null);
}

[TestClass]
[TestCategory("Probe")]
public sealed class FatigueLeanProbe
{
    private const float Dt = 1f / 60f;

    private static void Run(string name, FighterTuning tuning)
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        using var game = new BoxingGame(tuning with { ConcussionSpin = float.PositiveInfinity, CoreToneFloor = 1f }, seed: 3, corners: corners);
        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl!).Input = new FighterIntent(Vector2.Zero, Evade: true);
        game.Player.Stamina = 0f;
        var line = new System.Text.StringBuilder($"{name,-32}");
        for (var step = 1; step <= 8 * 60; step++)
        {
            game.Step(new BoxingInput(default), Dt);
            if (step % 120 == 0)
            {
                var p = game.Player;
                static float Pitch(Vector3 v) => MathF.Atan2(-v.Y, MathF.Sqrt((v.X * v.X) + (v.Z * v.Z))) * 180f / MathF.PI;
                line.Append(string.Create(CultureInfo.InvariantCulture, $" | t{step / 60}: head {Pitch(Vector3.Transform(Vector3.UnitZ, p.HeadOrientation)):0} chest {Pitch(Vector3.Transform(Vector3.UnitZ, p.TorsoOrientation)):0}"));
            }
        }

        Console.WriteLine(line.ToString());
        _ = control;
    }

    [TestMethod]
    public void Variants()
    {
        var t = FighterTuning.Default;
        Run("default", t);
        Run("no breath", t with { BreathTorque = 0f });
        Run("no chin", t with { ChinReflexRadians = 0f });
        Run("no breath no chin", t with { BreathTorque = 0f, ChinReflexRadians = 0f });
        Run("no breath no chin, no fatigue", t with { BreathTorque = 0f, ChinReflexRadians = 0f, StaminaFatigue = false });
        Run("share 1", t with { BreathTorque = 0f, ChinReflexRadians = 0f, TrunkMuscleShare = 1f });
    }
}
