using MiniRing.Arena;
using MiniRing.Characters;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("slow")]
public sealed class WvcSlipTests
{
    [TestMethod]
    public void AlbertStaysOnHisFeetThroughMostExchangesWithoutSuppressingKnockdowns()
    {
        const int seed = 1;
        const float dt = 1f / 60f; // TV at speed 1 and 30 fps, two game steps per frame.
        var albert = CharacterRoster.Get("albert");
        var ramon = CharacterRoster.Get("ramon");
        var red = RosterLeague.ContenderOf(albert, ramon.Id, null);
        var blue = RosterLeague.ContenderOf(ramon, albert.Id, null);
        var wvc = Organizations.Get("wvc");
        var corners = TvShowRules.Stage(VideoMaker.WithCharacters(VideoMaker.Corners(
            red, blue, seed, albert.Wear, ramon.Wear, albert.Weapon, ramon.Weapon,
            blue.Create.Invoke, red.Create.Invoke), albert, ramon), wvc);
        // Reproduce the support-only change independently of the later WVC impact calibration.
        var rules = TvShowRules.Rules(3, 20f);
        using var game = new BoxingGame(seed: seed, corners: corners, rules: rules, mood: wvc.ArenaMood);

        var wasStanding = true;
        var highConsciousnessFalls = 0;
        var hits = 0;
        var knockdownWithLossOfConsciousness = false;
        for (var step = 0; step < (rules.TotalSeconds + 9f) / dt && !game.MatchOver; step++)
        {
            game.Step(default, dt);
            if (game.Resting)
            {
                wasStanding = game.Player.State == FighterState.Standing;
                continue;
            }

            var standing = game.Player.State == FighterState.Standing;
            if (wasStanding && !standing && game.Player.Consciousness >= 0.98f)
            {
                highConsciousnessFalls++;
            }

            wasStanding = standing;
            hits += game.RecentHits.Count;
            knockdownWithLossOfConsciousness |= game.Cpu.IsKnockedDown && game.Cpu.Consciousness < 0.30f;
        }

        Assert.IsLessThanOrEqualTo(1, highConsciousnessFalls);
        // 2026-11-22（物理 87）: 持ち時間が 5 秒のスタミナ（胴・腰の酸素も数える）で、疲れた選手の手数が減った（15 → 1〜11 発。同じ種でも物理の小さな違いで揺れる）。打ち合いが起きていることだけを見る。
        Assert.IsGreaterThanOrEqualTo(3, hits);
        Assert.IsTrue(knockdownWithLossOfConsciousness);
    }
}
