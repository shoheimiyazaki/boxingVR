using System.Numerics;
using Aigis.Render;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 息が上がった見た目（<see cref="TiredLook"/>）: 元気なうちは何も出ず、スタミナが減るほど息が速く・濃く・メーターが点滅する。
/// 見た目だけで、試合の計算は変わらない。
/// </summary>
[TestClass]
public sealed class TiredLookTests
{
    private const float Dt = 1f / 240f;
    private static readonly float[] ExpectedTicks = [30f, 60f];

    [TestMethod]
    public void Fresh_ShowsNoBreath_AndNoFlash()
    {
        using var game = Game();
        Step(game, 1f);
        Assert.AreEqual(0, Puffs(game), "a fresh fighter breathes unseen");
        Assert.AreEqual(0f, TiredLook.MeterFlash(game.Player));
    }

    [TestMethod]
    public void Winded_BreathesFaster_PuffsFromTheMouth_AndTheMeterFlashes()
    {
        using var game = Game();
        Step(game, 0.5f);
        var restPhase = game.Cpu.BreathPhase;
        var tiredStart = game.Player.BreathPhase;
        var puffSeen = 0;
        var flashPeak = 0f;
        for (var t = 0f; t < 2f; t += Dt)
        {
            game.Player.Stamina = 0f; // 尽きたまま（回復の分を毎歩消す）
            game.Step(default, Dt);
            puffSeen = Math.Max(puffSeen, Puffs(game));
            flashPeak = MathF.Max(flashPeak, TiredLook.MeterFlash(game.Player));
        }

        var tiredBreaths = game.Player.BreathPhase - tiredStart;
        var restBreaths = game.Cpu.BreathPhase - restPhase;
        Console.WriteLine($"2 s: winded {tiredBreaths:0.00} breaths vs fresh {restBreaths:0.00}; puffs {puffSeen}; flash peak {flashPeak:0.00}; cyanosis {game.Player.Cyanosis:0.00}");
        Assert.IsGreaterThan(restBreaths * 4f, tiredBreaths, "panting is several times faster than resting");
        Assert.IsGreaterThan(0, puffSeen, "breath clouds come out of the mouth");
        Assert.IsGreaterThan(0.9f, flashPeak, "the empty meter flashes");
        Assert.AreEqual(1f, game.Player.Cyanosis, 1e-3f);
    }

    [TestMethod]
    public void BreathRate_RisesSteadilyWithWindedness()
    {
        var rates = Enumerable.Range(0, 11).Select(i => TiredLook.BreathsPerSecond(i / 10f)).ToArray();
        for (var i = 1; i < rates.Length; i++)
        {
            Assert.IsGreaterThan(rates[i - 1], rates[i]);
        }
    }

    [TestMethod]
    public void StaminaMeter_ColoursTheBarByZone_FineSluggishDanger()
    {
        // 帯の色は今いる区域の色（問題ない・鈍る・ヤバい）。同じ区域なら同じ色、区域が違えば違う色。区切りには目盛りが 2 本。
        Vector4 Fill(float stamina) => TvHud.StaminaBar(Vector2.Zero, 100f, 8f, stamina, 0.6f, 0.3f).Single(s => s.FillColor.W >= 1f).FillColor;
        var fine = Fill(0.9f);
        var sluggish = Fill(0.45f);
        var danger = Fill(0.15f);
        Assert.AreEqual(fine, Fill(0.7f));
        Assert.AreEqual(sluggish, Fill(0.35f));
        Assert.AreEqual(danger, Fill(0.05f));
        Assert.AreNotEqual(fine, sluggish);
        Assert.AreNotEqual(sluggish, danger);
        Assert.AreNotEqual(fine, danger);
        var ticks = TvHud.StaminaBar(Vector2.Zero, 100f, 8f, 1f, 0.6f, 0.3f).Where(s => s.SizePixels.X <= 2f).Select(s => s.CenterPixels.X).ToArray();
        Assert.HasCount(ExpectedTicks.Length, ticks);
        Assert.IsTrue(ExpectedTicks.All(x => ticks.Any(t => MathF.Abs(t - x) < 1e-3f)), string.Join(", ", ticks));
    }

    private static int Puffs(BoxingGame game)
    {
        var instances = new List<PrimitiveInstance>();
        TiredLook.WriteInstances(game, instances);
        return instances.Count;
    }

    private static BoxingGame Game()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        return new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
    }

    private static void Step(BoxingGame game, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
        {
            game.Step(default, Dt);
        }
    }
}
