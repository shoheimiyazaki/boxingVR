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
        // 短押しは経路が 0.62 倍の時間で出る軽いはたき（TapDurationScale）で、拳が相手へ届いて当たると最大速度に当たりの跳ね返りも入るので、
        // 短押しとの大小は比べない（短押しの連打は IntentFeelTests で測る）。溜めて押し出した拳が出ていて、踏み込むとさらに速いことを確かめる。
        Assert.IsGreaterThan(1f, loaded, "溜めて押し出した拳は出ている");
        Assert.IsGreaterThan(1f, step, "踏み込みながら溜めた拳も出ている（踏み込みの上乗せは試合の混沌で大小が入れ替わるので比べない）");
        Assert.IsGreaterThan(0f, tap);
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
