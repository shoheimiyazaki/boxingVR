using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>胴の筋の仕事の数え方の調べ（2026-11-22、スタミナの仕様）: 立つ・足運び・連打の 3 つで、部位ごとの仕事率と蓄えを 1 秒ごとに出す。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class TrunkWorkProbe
{
    private const float Dt = 1f / 60f;

    private static FighterIntent Rush(float t)
    {
        var phase = t / 0.2014f;
        var index = (int)MathF.Floor(phase);
        var down = phase - index < 0.67f;
        return new FighterIntent(Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
    }

    private static FighterIntent Footwork(float t)
        => new(new Vector2(MathF.Sign(MathF.Sin(t * 0.4171f)), MathF.Sin(t * 1.132f) * 0.6f));

    private static void Run(string name, Func<float, FighterIntent> plan, float seconds)
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        using var game = new BoxingGame(StaminaFeelProbe.Tuning() with { ConcussionSpin = float.PositiveInfinity, CoreToneFloor = 1f }, seed: 3, corners: corners);
        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl!).Input = new FighterIntent(Vector2.Zero, Evade: true);
        Console.WriteLine($"== {name}");
        Console.WriteLine("  t  | O2 L R legs trunk | W  L R legs trunk | fist m/s  endu  winded");
        var peak = 0f;
        var last = new float[4];
        for (var step = 1; step <= (int)(seconds / Dt); step++)
        {
            var t = step * Dt;
            control.Input = plan(t);
            game.Step(new BoxingInput(default), Dt);
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                peak = MathF.Max(peak, (game.Player.GloveVelocity(hand) - game.Player.ChestVelocity).Length());
            }

            if (step % 60 == 0)
            {
                var p = game.Player;
                string O(MuscleRegion r) => p.OxygenOf(r).ToString("0.00", CultureInfo.InvariantCulture);
                var work = new float[4];
                for (var i = 0; i < 4; i++)
                {
                    work[i] = p.RegionWork((MuscleRegion)i);
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{t,4:0}  | {O(MuscleRegion.LeftArm)} {O(MuscleRegion.RightArm)} {O(MuscleRegion.Legs)} {O(MuscleRegion.Trunk)} | {work[0] - last[0],6:0} {work[1] - last[1],6:0} {work[2] - last[2],6:0} {work[3] - last[3],6:0} | {peak,5:0.00}  {p.Endurance:0.00}  {p.Winded:0.00}  {p.State}"));
                last = work;
                peak = 0f;
            }
        }
    }

    [TestMethod]
    public void Idle() => Run("idle guard 12 s", _ => default, 12f);

    [TestMethod]
    public void Footwork12() => Run("footwork 20 s", Footwork, 20f);

    [TestMethod]
    public void RushTwenty() => Run("rush 20 s", Rush, 20f);
}
