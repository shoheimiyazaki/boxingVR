using MiniRing.Bots;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("slow")]
public sealed class HybridMatchTests
{
    [TestMethod]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    public void GlovedBotsScoreInTheHybridMatch(int seed, bool fromCorners)
    {
        const int framesPerSecond = 60;
        var corners = new BoxingCorners("hybrid", s => BotCatalog.Create("swarmer", s),
            s => BotCatalog.Create("champion", s), VoxelGloves: true);
        if (fromCorners)
        {
            corners = corners.StartingAtCorners();
        }

        using var game = new BoxingGame(FighterTuning.HybridPunch, seed, corners);
        var hits = 0;
        var firstHit = float.NaN;
        var firstDown = float.NaN;
        var seconds = fromCorners ? 12 : 3;
        for (var frame = 1; frame <= seconds * framesPerSecond; frame++)
        {
            game.Step(default, 1f / framesPerSecond);
            hits += game.RecentHits.Count;
            if (float.IsNaN(firstHit) && hits > 0)
            {
                firstHit = frame / (float)framesPerSecond;
            }

            if (float.IsNaN(firstDown) && (game.Player.State != FighterState.Standing || game.Cpu.State != FighterState.Standing))
            {
                firstDown = frame / (float)framesPerSecond;
            }
        }

        Console.WriteLine(FormattableString.Invariant(
            $"seed={seed} corners={fromCorners} duration={seconds}s hits={hits} first hit={firstHit:F2}s first down={firstDown:F2}s"));
        Assert.IsTrue(hits > 0);
        Assert.IsTrue(float.IsNaN(firstDown) || firstDown >= firstHit,
            "The fighters should be able to exchange a scoring blow before a down.");
    }
}
