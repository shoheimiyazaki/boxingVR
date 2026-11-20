using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// StaminaRushTests の連打の写し: 始めの 10 秒の拳の最高速度が下がった原因を、間合い・当たりの数・ダメージ・相手が押し下げられた距離で見る
/// （版 43 の写しと並べるための Probe。通常の実行から外す）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class RushRangeProbe
{
    private const float Dt = 1f / 60f;

    [TestMethod]
    [TestCategory("Probe")]
    public void RushRange() => Run(awayY: 0f);

    [TestMethod]
    [TestCategory("Probe")]
    public void RushRangeOpponentWalksAway() => Run(awayY: -1f);

    [TestMethod]
    [TestCategory("Probe")]
    public void RushRangeThroughTheOpponent() => Run(awayY: 0f, passThrough: true);

    private static void Run(float awayY, bool passThrough = false)
    {
        var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
        using var game = new BoxingGame(FighterTuning.Default with { ConcussionSpin = float.PositiveInfinity }, seed: 3, corners: corners);
        if (passThrough)
        {
            foreach (var p in game.Player.AllParts)
            {
                foreach (var q in game.Cpu.AllParts)
                {
                    game.World.SetPairCollisionEnabled(p, q, false);
                }
            }
        }

        var control = (IntentControl)game.PlayerControl!;
        ((IntentControl)game.CpuControl).Input = awayY == 0f ? new FighterIntent(Vector2.Zero, Evade: true) : new FighterIntent(new Vector2(0f, awayY));
        var rushing = 0f;
        var tap = 0f;
        var second = 0f;
        var peak = 0f;
        var gapSum = 0f;
        var gapCount = 0;
        var hits = 0;
        var damage = 0f;
        var hitMass = 0f;
        var hitSpeed = 0f;
        var lastWork = 0f;
        var clock = 0f;
        while (rushing < 20f && clock < 60f)
        {
            var standing = game.Player.State == FighterState.Standing && game.Cpu.State == FighterState.Standing;
            var phase = tap / 0.12f;
            var index = (int)MathF.Floor(phase);
            var down = standing && phase - index < 0.67f;
            control.Input = new FighterIntent(Vector2.Zero, Light: down && index % 2 == 0, Heavy: down && index % 2 == 1);
            game.Step(new BoxingInput(default), Dt);
            clock += Dt;
            if (!standing)
            {
                continue;
            }

            tap += Dt;
            rushing += Dt;
            second += Dt;
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                peak = MathF.Max(peak, (game.Player.GloveVelocity(hand) - game.Player.ChestVelocity).Length());
            }

            var d = game.Cpu.ChestPosition - game.Player.ChestPosition;
            gapSum += new Vector2(d.X, d.Z).Length();
            gapCount++;
            foreach (var h in game.RecentHits.Where(h => h.Attacker == game.Player))
            {
                hits++;
                damage += h.Damage;
                hitMass += h.ImpactMass;
                hitSpeed += h.Speed;
            }

            if (second >= 1f)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"RUSH t {rushing:F0} peak {peak:F2} gap {gapSum / gapCount:F3} hits {hits} dmg {damage:F0} meanMass {hitMass / Math.Max(1, hits):F2} meanSpeed {hitSpeed / Math.Max(1, hits):F2} down {game.Cpu.State} work {game.Player.WorkDone - lastWork:F1} stamina {game.Player.Stamina:F2} cpuStamina {game.Cpu.Stamina:F2} cpuConsc {game.Cpu.Consciousness:F2} playerHeadSpeed {game.Player.ChestVelocity.Length():F2}"));
                lastWork = game.Player.WorkDone;
                second = 0f;
                peak = 0f;
                gapSum = 0f;
                gapCount = 0;
                hits = 0;
                damage = 0f;
                hitMass = 0f;
                hitSpeed = 0f;
            }
        }
    }
}
