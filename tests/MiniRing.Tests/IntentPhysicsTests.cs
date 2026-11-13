using System.Numerics;

namespace MiniRing.Tests;

[TestClass]
public sealed class IntentPhysicsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void LoadingAndStepping_AccelerateThePhysicalFist()
    {
        var tap = Measure(1, false);
        var loaded = Measure(18, false);
        var step = Measure(18, true);
        TestContext.WriteLine($"短押し {tap:0.000} / 溜め {loaded:0.000} / 踏み込み溜め {step:0.000} m/s");
        Assert.IsGreaterThan(tap, loaded, "溜めのバネの反発で拳が速くなる");
        Assert.IsGreaterThan(tap, step, "踏み込み強攻撃は短押しより速い");
    }

    private static float Measure(int heldSteps, bool forward)
    {
        using var game = new BoxingGame(seed: 43);
        var control = new IntentControl();
        const float dt = 1f / 60f;
        for (var i = 0; i < 60; i++)
        {
            game.Step(new BoxingInput(control.Read(game.PlayerSense, dt)), dt);
        }

        for (var i = 0; i < heldSteps; i++)
        {
            control.Input = new FighterIntent(forward ? Vector2.UnitY : Vector2.Zero, Heavy: true);
            game.Step(new BoxingInput(control.Read(game.PlayerSense, dt)), dt);
        }

        control.Input = new FighterIntent(forward ? Vector2.UnitY : Vector2.Zero);
        var peak = 0f;
        for (var i = 0; i < 18; i++)
        {
            game.Step(new BoxingInput(control.Read(game.PlayerSense, dt)), dt);
            peak = MathF.Max(peak, game.Player.GloveVelocity(Hand.Right).Length());
        }

        return peak;
    }
}
