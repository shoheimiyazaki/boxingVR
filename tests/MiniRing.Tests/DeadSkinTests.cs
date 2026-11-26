using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>死んだ体の皮膚は、数秒かけてだんだん青黒くなる（見た目だけ: <see cref="Fighter.Lividity"/>）。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class DeadSkinTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void DeadBody_DarkensGraduallyOverSeconds()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        var corners = new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)) with { Ring = RingLayout.Cage };
        using var game = new BoxingGame(seed: 1, corners: corners, rules: MatchRules.Vfc);
        for (var t = 0f; t < 0.5f; t += Dt)
        {
            game.Step(default, Dt);
        }

        Assert.AreEqual(0f, game.Cpu.Lividity, "生きている間は変色しない");
        game.Cpu.Die("test");
        var samples = new List<float>();
        for (var t = 0f; t < 8f; t += Dt)
        {
            game.Step(default, Dt);
            samples.Add(game.Cpu.Lividity);
        }

        var atOne = samples[(int)(1f / Dt)];
        Assert.IsTrue(atOne is > 0f and < 0.5f, $"1 秒ではまだ途中: {atOne}");
        for (var i = 1; i < samples.Count; i++)
        {
            Assert.IsGreaterThanOrEqualTo(samples[i - 1], samples[i], "だんだん濃くなり、戻らない");
        }

        Assert.AreEqual(1f, samples[^1], 1e-4f, "数秒で満ちる");
    }
}
