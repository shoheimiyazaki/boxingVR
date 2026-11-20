using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

[TestClass]
public sealed class StaminaRestDecisionTests
{
    [TestMethod]
    public void ExhaustedBot_LeavesDangerAndThrowsLess_ThenResumesWithReserve()
    {
        static (float Move, int Thrown) Observe(float stamina)
        {
            const float reach = 0.563f;
            var style = BotCatalog.Boxer with { Circling = 0f, DefenseRate = 0f, Prediction = 0f };
            var bot = new BoxerBot(style, seed: 7);
            var at = RingLayout.Boxing.Center + new Vector3(0f, 2.817f, 0f);
            var chest = at + new Vector3(0f, 0f, 0.9f * reach);
            var sense = new FighterSense(new BodyFrame(at, 0f), reach, chest + new Vector3(0f, 0.3f * reach, 0f), chest, true,
                at + new Vector3(0f, 0.3f * reach, 0f), SelfStamina: stamina);
            var move = 0f;
            for (var step = 0; step < 1007; step++)
            {
                move += bot.Read(sense, 1f / 60f).MoveWorld.Z / 1007f;
            }

            return (move, bot.Stats.Techniques.Values.Sum(t => t.Thrown));
        }

        var full = Observe(1f);
        var empty = Observe(0f);
        Console.WriteLine($"full: move {full.Move:F3}, thrown {full.Thrown}; empty: move {empty.Move:F3}, thrown {empty.Thrown}");
        Assert.IsLessThan(-0.1f, empty.Move, "相手が届く所から下がる目標を出す");
        Assert.IsLessThan(full.Move - 0.1f, empty.Move);
        Assert.IsLessThan(full.Thrown, empty.Thrown, "回復した側は再び攻める");
    }
}
