using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>ドリル動画と同じ設定（swarmer の守り、打ち返す、合図なし・予測あり、左フック、種 4）で、ウィービング中の上体の前傾と返しの拳の数を見る調査用。</summary>
[TestClass]
public sealed class WeaveVideoProbe
{
    [TestMethod]
    public void WeaveDrill_AsInTheVideo()
    {
        var swarmer = BotCatalog.Swarmer;
        var defender = DefenseDrill.Defender(swarmer.Defenses, defend: true, counters: true) with
        {
            Name = "swarmer", GuardHeight = swarmer.GuardHeight, GuardWidth = swarmer.GuardWidth, GuardReach = swarmer.GuardReach, Prediction = 1f,
        };
        var attacker = DefenseDrill.PunchAttacker("3", telegraph: 0.08f);
        BoxerBot? bot = null;
        Spy? spy = null;
        using var game = new BoxingGame(seed: 4, corners: new BoxingCorners(attacker.Name, s => new BoxerBot(attacker, s), s =>
        {
            bot = new BoxerBot(defender.Validated(), s, Defense.Weave);
            spy = new Spy(bot);
            return spy;
        }));
        float maxChest = -9f, maxPelvis = -9f, minHeadY = 9f;
        for (var t = 0f; t < 14f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
            var f = game.Player;
            var head = spy!.Last.HeadOffset / f.ArmReach;
            minHeadY = MathF.Min(minHeadY, head.Y);
            if (head.Y < -0.3f)
            {
                maxChest = MathF.Max(maxChest, Pitch(game, f, f.Parts.Chest));
                maxPelvis = MathF.Max(maxPelvis, Pitch(game, f, f.Parts.Pelvis));
            }
        }

        Console.WriteLine($"deepest head command {minHeadY:0.00} reach; while weaving, chest leans forward up to {maxChest * 180f / MathF.PI:0.0}°, pelvis {maxPelvis * 180f / MathF.PI:0.0}°; " +
            $"counters thrown {bot!.Stats.CountersThrown}, landed {bot.Stats.CountersLanded}; techniques {string.Join(", ", bot.Stats.Techniques.Select(k => $"{k.Key} {k.Value.Thrown}"))}");
    }

    /// <summary>前への傾き [rad]（正 = 前）。</summary>
    private static float Pitch(BoxingGame game, Fighter f, int part)
    {
        var up = Vector3.Transform(Vector3.UnitY, game.World.GetState(part).Orientation);
        return MathF.Atan2(Vector3.Dot(up, f.Frame.Forward), up.Y);
    }
}
