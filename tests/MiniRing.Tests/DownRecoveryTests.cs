using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// ダウンは段階でなく連続: 体はいつも立とうとし、腰を上げられる高さは意識で決まる。倒れた後は意識が戻るのに合わせてゆっくり起き上がる。
/// </summary>
[TestClass]
[TestCategory("slow")]
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

        // 意識が KO の線（0.3）を下回った体は倒れて起き上がろうとし、試みの分（StirReach × Stir、最大 0.3）だけ腰を上げる（意志のモデル、CLAUDE.md §8）。
        // 立ったまま張りが抜けて沈む体（0.6 → 0.4）と、倒れて試みる体（0.25 → 0.1）は別の働きなので、それぞれの中で「意識が低いほど腰が低い」を見る
        // （2026-11-18、床から支える体: 0.4 は 67 %・0.25 は 73 %。腰を上げられる高さ StandReach は 0.78 → 0.61 と単調に下がっている）。
        for (var i = 1; i < heights.Length; i++)
        {
            if (levels[i - 1] >= 0.3f && levels[i] < 0.3f)
            {
                continue;
            }

            Assert.IsLessThan(heights[i - 1] + 0.03f, heights[i], $"lower consciousness, lower hips ({levels[i]})");
        }

        // 起き上がりを手足の支えから出す体（docs/GRAPPLING.md §8）では、半ば意識のある体は手と膝で支えた低い膝立ちに落ち着く（39%）。
        Assert.IsGreaterThan(0.35f, heights[2], "half-conscious: neither standing nor flat");
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
        for (var t = 0f; t < 20.14f && standingAt is null; t += Dt)
        {
            game.Step(default, Dt);
            var ratio = (f.PelvisPosition.Y - ground) / stand;
            if (t > 0.8392f && f.Consciousness < FighterTuning.Default.LimpConsciousness)
            {
                lowestWhileLimp = MathF.Min(lowestWhileLimp, ratio);
            }

            kneelAt ??= t > 0.8392f && ratio > 0.4f ? t : null;
            uprightAt ??= kneelAt is not null && ratio > 0.8f ? t : null;
            if (t > 0.8392f && f.State == FighterState.Standing)
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
        Assert.IsGreaterThan(1.007f, uprightAt.Value - kneelAt.Value, "rises gradually, not in one pop");
        // 立ち直りの条件は意識が KnockoutConsciousness 以上（Fighter.TickDown）。起き上がろうとする試み（Stir）が腰を上げる分、線のすぐ上（0.33）で立つ。
        Assert.IsGreaterThanOrEqualTo(FighterTuning.Default.KnockoutConsciousness, consciousnessWhenStanding, "stands only once consciousness has come back above the knockout line");
    }

    private static float SettledPelvis(float consciousness)
    {
        using var game = StillGame();
        var f = game.Cpu;
        var (ground, stand) = Settle(game);
        var sum = 0f;
        var n = 0;
        for (var t = 0f; t < 6.714f; t += Dt)
        {
            f.Concussion = 1f - consciousness;
            game.Step(default, Dt);
            if (t > 4.196f)
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
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var f = game.Cpu;
        var ground = f.AllParts.Min(id => game.World.GetState(id).Position.Y) - 0.02817f;
        return (ground, f.PelvisPosition.Y - ground);
    }
}
