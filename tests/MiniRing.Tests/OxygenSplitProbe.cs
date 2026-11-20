using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 調べもの: 筋肉の仕事が部位（左腕・右腕・脚・胴）にどう分かれるか。呼吸・循環のモデルの部位ごとの蓄えと血管の太さを決める元。
/// 弱強を交互に連打するラッシュ（StaminaRushTests と同じ）、ボット同士の 100 秒、構えて立つだけの 34 秒を測る（人形の単位の 60 秒・20 秒）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class OxygenSplitProbe
{
    private const float Dt = 1f / 60f;

    private static string Split(Fighter f, float seconds)
    {
        var regions = Enum.GetValues<MuscleRegion>();
        var total = regions.Sum(f.RegionWork);
        return string.Join(", ", regions.Select(r => string.Create(CultureInfo.InvariantCulture,
            $"{r} {f.RegionWork(r):F0} J ({f.RegionWork(r) / seconds:F1} W, {f.RegionWork(r) / MathF.Max(1e-3f, total):P0})")))
            + string.Create(CultureInfo.InvariantCulture, $"; total {total / seconds:F1} W; stamina {f.Stamina:P0}; masses arm {f.ArmMass(Hand.Left):F2} kg, total {f.TotalMass:F1} kg");
    }

    [TestMethod]
    public void Rush()
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        using var game = new BoxingGame(FighterTuning.Default with { ConcussionSpin = float.PositiveInfinity }, seed: 3, corners: corners);
        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl).Input = new FighterIntent(Vector2.Zero, Evade: true);
        var tap = 0f;
        for (var window = 1; window <= 3; window++)
        {
            for (var clock = 0f; clock < 16.78f; clock += Dt)
            {
                var standing = game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing;
                var phase = tap / 0.2014f;
                var index = (int)MathF.Floor(phase);
                var down = standing && phase - index < 0.67f;
                control.Input = new FighterIntent(Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
                game.Step(new BoxingInput(default), Dt);
                tap += Dt;
            }

            Console.WriteLine($"rush {window * 16.78f:F1} s: {Split(game.Player, window * 16.78f)}");
        }
    }

    [TestMethod]
    public void BotBout()
    {
        foreach (var (red, blue) in new[] { ("champion", "swarmer"), ("swarmer", "outboxer") })
        {
            var corners = BoxingCorners.Default with { Player = s => Bots.BotCatalog.Create(red, s), Cpu = s => Bots.BotCatalog.Create(blue, s) };
            using var game = new BoxingGame(FighterTuning.Default, seed: 1, corners: corners);
            for (var clock = 0f; clock < 100.7f; clock += Dt)
            {
                game.Step(new BoxingInput(default), Dt);
            }

            Console.WriteLine($"{red}: {Split(game.Player, 100.7f)}");
            Console.WriteLine($"{blue}: {Split(game.Cpu, 100.7f)}");
        }
    }

    [TestMethod]
    public void Idle()
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        using var game = new BoxingGame(FighterTuning.Default, seed: 0, corners: corners);
        for (var clock = 0f; clock < 33.57f; clock += Dt)
        {
            game.Step(new BoxingInput(default), Dt);
        }

        Console.WriteLine($"idle: {Split(game.Player, 33.57f)}");
    }
}
