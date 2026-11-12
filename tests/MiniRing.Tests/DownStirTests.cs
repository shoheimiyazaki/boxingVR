using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 倒れている間の意識の細かさ: 意識が少しでもあれば起き上がろうとして体が動き（10 カウントが続く）、
/// まったく無ければ体は止まり、止まったまま <see cref="BoxingGame.StillKoSeconds"/> 続くとその場で K.O.。
/// </summary>
[TestClass]
public sealed class DownStirTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void OutCold_LiesStill_AndIsCountedOutOnTheSpot()
    {
        using var game = StillGame();
        var f = game.Cpu;
        Settle(game);
        f.Concussion = 3f; // 意識 0 が 40 秒以上続く深さ

        var endedAt = -1f;
        var downAtEnd = 0f;
        for (var t = 0f; t < 12f; t += Dt)
        {
            game.Step(default, Dt);
            if (game.RoundOver)
            {
                endedAt = t;
                downAtEnd = f.DownTime;
                break;
            }
        }

        Console.WriteLine($"round over at {endedAt:0.00}s, down for {downAtEnd:0.00}s: '{game.Message}'");
        Assert.IsGreaterThan(0f, endedAt, "the referee stopped it");
        StringAssert.Contains(game.Message, "OUT COLD");
        Assert.IsLessThan(BoxingGame.CountSeconds - 1f, downAtEnd, "well before the count of ten");
        Assert.IsGreaterThan(BoxingGame.StillKoSeconds, downAtEnd);
    }

    [TestMethod]
    public void BarelyConscious_KeepsStirring_SoTheCountGoesOn()
    {
        // 張りが 0 の浅い意識（LimpConsciousness 未満）でも、試みのたびに体が動き、止まっている時間は StillKoSeconds に届かない。
        var consciousness = 0.04f;
        Assert.IsLessThan(FighterTuning.Default.LimpConsciousness, consciousness);
        using var game = StillGame();
        var f = game.Cpu;
        Settle(game);
        var longestStill = 0f;
        var attempts = 0;
        var wasStirring = false;
        for (var t = 0f; t < 9.5f; t += Dt)
        {
            f.Concussion = 1f - consciousness;
            game.Step(default, Dt);
            if (t > 2f)
            {
                longestStill = MathF.Max(longestStill, f.StillTime);
                attempts += f.Stir > 0.05f && !wasStirring ? 1 : 0;
            }

            wasStirring = f.Stir > 0.05f;
        }

        Console.WriteLine($"consciousness {consciousness}: {attempts} attempts in 7.5 s, longest still {longestStill:0.00}s, state {f.State}, round over {game.RoundOver}");
        Assert.AreNotEqual(FighterState.Standing, f.State, "too weak to get up");
        Assert.IsFalse(game.RoundOver, "not stopped: the body moves");
        Assert.IsLessThan(BoxingGame.StillKoSeconds * 0.75f, longestStill);
        Assert.IsGreaterThanOrEqualTo(3, attempts);
    }

    [TestMethod]
    public void Attempts_GrowWithConsciousness()
    {
        // 試みの大きさ（体のどこかの部位の、上下の振れ幅のいちばん大きいもの）は意識が深いほど大きい。意識 0 では動かない。
        // 浅い意識では腰はほとんど上がらず、頭や腕がわずかに持ち上がる。
        var levels = new[] { 0f, 0.04f, 0.2f };
        var swings = levels.Select(BodySwing).ToArray();
        Console.WriteLine(string.Join(", ", levels.Zip(swings, (c, s) => $"consciousness {c:0.00} → largest part swing {s * 100f:0.0} cm")));
        // 意識 0 の体も倒れた後しばらくは沈み込み・滑りで少し動く（0.9 cm ほど）。起き上がろうとする動き（意識 0.04 で 5 cm）よりずっと小さい。
        Assert.IsLessThan(swings[1] / 3f, swings[0], "out cold: only settling, no attempts");
        Assert.IsGreaterThan(swings[0] + 0.005f, swings[1], "barely conscious: it moves");
        Assert.IsGreaterThan(swings[1], swings[2], "more consciousness, bigger attempts");
    }

    private static float BodySwing(float consciousness)
    {
        using var game = StillGame();
        var f = game.Cpu;
        Settle(game);
        f.Concussion = 1f; // まず倒す
        for (var t = 0f; t < 3f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var low = f.AllParts.ToDictionary(id => id, _ => float.MaxValue);
        var high = f.AllParts.ToDictionary(id => id, _ => float.MinValue);
        for (var t = 0f; t < 5f; t += Dt)
        {
            f.Concussion = 1f - consciousness;
            game.Step(default, Dt);
            if (t > 1f)
            {
                foreach (var id in f.AllParts)
                {
                    var y = game.World.GetState(id).Position.Y;
                    low[id] = MathF.Min(low[id], y);
                    high[id] = MathF.Max(high[id], y);
                }
            }
        }

        return f.AllParts.Max(id => high[id] - low[id]);
    }

    private static BoxingGame StillGame()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        return new BoxingGame(seed: 1, corners: new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s)));
    }

    private static void Settle(BoxingGame game)
    {
        for (var t = 0f; t < 1.5f; t += Dt)
        {
            game.Step(default, Dt);
        }
    }
}
