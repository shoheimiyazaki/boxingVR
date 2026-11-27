using System.Globalization;
using System.Reflection;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調べ物（2026-11-27）: 試合の始めの深いしゃがみの内訳。骨盤の高さ（立った高さに対する比）・指令の沈み（_crouch・_headCrouch・_rise）・足運びの沈み・体の傾きを時間で出す。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class CrouchTraceProbe
{
    private static float Field(Fighter f, string name) => (float)typeof(Fighter).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f)!;

    [TestMethod]
    public void Trace()
    {
        var corners = new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s)) with { Ring = RingLayout.Boxing.AtCorners() };
        using var game = new BoxingGame(seed: 4, corners: corners);
        var inv = CultureInfo.InvariantCulture;
        float Floor(Fighter f) => MathF.Min(f.AnklePosition(Hand.Left).Y, f.AnklePosition(Hand.Right).Y);
        var stand = game.Player.PelvisPosition.Y - Floor(game.Player);
        for (var i = 0; i <= 420; i++)
        {
            if (i >= 150 && i % 4 == 0)
            {
                var p = game.Player;
                Console.WriteLine(string.Create(inv,
                    $"t {i / 60f:F2} pelvis {(p.PelvisPosition.Y - Floor(p)) / stand:F2} crouch {Field(p, "_crouch"):F2} headCrouch {Field(p, "_headCrouch"):F2} rise {Field(p, "_rise"):F2} hover {p.HoverHeight:F2} headY {p.LastCommand.HeadOffset.Y / p.ArmReach:F2} move {p.LastCommand.ClampedMove.Length():F2} step {p.LastCommand.ClampedStep.Length():F2} tone {p.Tone:F2} ankleL {p.AnklePosition(Hand.Left).Y:F3} ankleR {p.AnklePosition(Hand.Right).Y:F3} supp {p.SupportedMass:F0}"));
            }

            game.Step(default, 1f / 60f);
        }
    }

    [TestMethod]
    public void Variants()
    {
        var d = FighterTuning.Default;
        var variants = new (string Name, FighterTuning Tuning)[]
        {
            ("default", d),
            ("no body->fist", d with { BodyToFistEfficiency = 0f }),
            ("kp x1.5", d with { HoverKp = d.HoverKp * 1.5f }),
        };
        var inv = CultureInfo.InvariantCulture;
        foreach (var (name, tuning) in variants)
        {
            foreach (var seed in new[] { 4, 5 })
            {
                var corners = new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s)) with { Ring = RingLayout.Boxing.AtCorners() };
                using var game = new BoxingGame(tuning, seed: seed, corners: corners);
                float Floor(Fighter f) => MathF.Min(f.AnklePosition(Hand.Left).Y, f.AnklePosition(Hand.Right).Y);
                var stand = game.Player.PelvisPosition.Y - Floor(game.Player);
                var low = 9f;
                var lowBlue = 9f;
                var standBlue = game.Cpu.PelvisPosition.Y - Floor(game.Cpu);
                for (var i = 0; i < 480; i++)
                {
                    game.Step(default, 1f / 60f);
                    if (i > 120 && game.Player.State == FighterState.Standing)
                    {
                        low = MathF.Min(low, (game.Player.PelvisPosition.Y - Floor(game.Player)) / stand);
                    }
                    if (i > 120 && game.Cpu.State == FighterState.Standing)
                    {
                        lowBlue = MathF.Min(lowBlue, (game.Cpu.PelvisPosition.Y - Floor(game.Cpu)) / standBlue);
                    }
                }

                Console.WriteLine(string.Create(inv, $"VAR {name} seed {seed}: red low {low:F2} blue low {lowBlue:F2}"));
            }
        }
    }
}
