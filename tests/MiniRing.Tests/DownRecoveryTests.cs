using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// ダウンは段階でなく連続: 体はいつも立とうとし、腰を上げられる高さは意識で決まる。倒れた後は意識が戻るのに合わせてゆっくり起き上がる。
/// </summary>
[TestClass]
public sealed class DownRecoveryTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void PelvisHeight_FallsSmoothlyWithConsciousness()
    {
        // 意識を一定に保つと、腰はその意識で上げられる高さに落ち着く: 意識が低いほど低く、途中の意識では途中の高さ（膝立ち・中腰）。
        var levels = new[] { 0.6f, 0.4f, 0.25f, 0.1f };
        var heights = levels.Select(SettledPelvis).ToArray();
        Console.WriteLine(string.Join(", ", levels.Zip(heights, (c, h) => $"consciousness {c:0.00} → pelvis {h:P0}")));

        for (var i = 1; i < heights.Length; i++)
        {
            Assert.IsLessThan(heights[i - 1] + 0.03f, heights[i], $"lower consciousness, lower hips ({levels[i]})");
        }

        Assert.IsGreaterThan(0.4f, heights[2], "half-conscious: neither standing nor flat");
        Assert.IsLessThan(0.85f, heights[2], "half-conscious: neither standing nor flat");
        Assert.IsLessThan(0.3f, heights[3], "barely conscious: on the floor");
    }

    [TestMethod]
    public void AfterADeepKnockdown_RisesSlowlyAsConsciousnessReturns()
    {
        using var game = StillGame();
        var f = game.Cpu;
        var (ground, stand) = Settle(game);
        f.Concussion = 0.95f;

        float? kneelAt = null, uprightAt = null, standingAt = null;
        var consciousnessWhenStanding = 0f;
        var lowestWhileLimp = 1f;
        for (var t = 0f; t < 12f && standingAt is null; t += Dt)
        {
            game.Step(default, Dt);
            var ratio = (f.PelvisPosition.Y - ground) / stand;
            if (t > 0.5f && f.Consciousness < FighterTuning.Default.LimpConsciousness)
            {
                lowestWhileLimp = MathF.Min(lowestWhileLimp, ratio);
            }

            kneelAt ??= t > 0.5f && ratio > 0.4f ? t : null;
            uprightAt ??= kneelAt is not null && ratio > 0.8f ? t : null;
            if (t > 0.5f && f.State == FighterState.Standing)
            {
                standingAt = t;
                consciousnessWhenStanding = f.Consciousness;
            }
        }

        Console.WriteLine($"limp low {lowestWhileLimp:P0}, hips past 40% at {kneelAt:0.00}s, past 80% at {uprightAt:0.00}s, standing at {standingAt:0.00}s (consciousness {consciousnessWhenStanding:0.00})");
        Assert.IsLessThan(0.3f, lowestWhileLimp, "out cold: flat on the floor");
        Assert.IsNotNull(standingAt, "always tries to stand, and does once conscious enough");
        Assert.IsNotNull(kneelAt);
        Assert.IsNotNull(uprightAt);
        Assert.IsGreaterThan(0.6f, uprightAt.Value - kneelAt.Value, "rises gradually, not in one pop");
        Assert.IsGreaterThan(0.35f, consciousnessWhenStanding, "stands only once consciousness has come back");
    }

    private static float SettledPelvis(float consciousness)
    {
        using var game = StillGame();
        var f = game.Cpu;
        var (ground, stand) = Settle(game);
        var sum = 0f;
        var n = 0;
        for (var t = 0f; t < 4f; t += Dt)
        {
            f.Concussion = 1f - consciousness;
            game.Step(default, Dt);
            if (t > 2.5f)
            {
                sum += f.PelvisPosition.Y - ground;
                n++;
            }
        }

        return sum / n / stand;
    }

    private static BoxingGame StillGame()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        return new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
    }

    private static (float Ground, float Stand) Settle(BoxingGame game)
    {
        for (var t = 0f; t < 1.5f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var f = game.Cpu;
        var ground = f.AllParts.Min(id => game.World.GetState(id).Position.Y) - 0.01f;
        return (ground, f.PelvisPosition.Y - ground);
    }
}
