using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 実戦で、拳の前に読める手がかりがどれだけあるかを測る調査用（間合い・リズム・踏み込み・体の動き）。
/// 標準出力に出す（--logger "console;verbosity=detailed"）。
/// </summary>
[TestClass]
public sealed class AnticipationProbe
{
    [TestMethod]
    [DataRow("champion")]
    [DataRow("slugger")]
    [DataRow("boxer")]
    [DataRow("jabber")]
    public void CuesBeforeAttacks(string attackerName)
    {
        var attackers = new List<IFighterControl>();
        var corners = new BoxingCorners(attackerName, s => Keep(attackers, BotCatalog.Create(attackerName, s)), s => BotCatalog.Create("swarmer", s));
        using var game = new BoxingGame(seed: 5, corners: corners);
        var reach = game.Cpu.ArmReach;
        const float dt = 1f / 240f;
        var history = new Queue<(float T, float Distance, Vector3 ChestVelocity, Vector3 GloveL, Vector3 GloveR)>();
        var onsets = new List<(float T, float Distance, float Closing, float ChestSpeed, float Since)>();
        var combosSoFar = 0;
        var lastOnset = -1f;
        for (var t = 0f; t < 120f; t += dt)
        {
            game.Step(default, dt);
            var distance = Vector3.Distance(game.Cpu.Frame.Origin with { Y = 0f }, game.Player.Frame.Origin with { Y = 0f }) / reach;
            history.Enqueue((t, distance, game.Cpu.ChestVelocity, game.Cpu.GloveVelocity(Hand.Left), game.Cpu.GloveVelocity(Hand.Right)));
            while (history.Count > 0 && history.Peek().T < t - 0.25f)
            {
                history.Dequeue();
            }

            var combos = attackers.OfType<BoxerBot>().Sum(b => b.Stats.Combos.Values.Sum());
            if (combos > combosSoFar)
            {
                combosSoFar = combos;
                var past = history.Peek();
                var closing = (past.Distance - distance) / MathF.Max(1e-3f, t - past.T); // R/s、正で詰めている
                onsets.Add((t, distance, closing, game.Cpu.ChestVelocity.Length() / reach, lastOnset < 0f ? -1f : t - lastOnset));
                lastOnset = t;
            }
        }

        var intervals = onsets.Where(o => o.Since > 0f && o.Since < 4f).Select(o => o.Since).ToList();
        var mean = intervals.Average();
        var sd = MathF.Sqrt(intervals.Average(i => (i - mean) * (i - mean)));
        var d = onsets.Select(o => o.Distance).ToList();
        var dMean = d.Average();
        var dSd = MathF.Sqrt(d.Average(x => (x - dMean) * (x - dMean)));
        var closingShare = onsets.Count(o => o.Closing > 0.5f) / (float)onsets.Count;
        Console.WriteLine($"{attackerName}: {onsets.Count} attacks in 120 s. interval {mean:0.00}±{sd:0.00}s, distance at onset {dMean:0.00}±{dSd:0.00}R, " +
            $"stepping in (closing > 0.5R/s over the last 0.25 s) {closingShare:P0}, closing mean {onsets.Average(o => o.Closing):0.00}R/s");
    }

    private static IFighterControl Keep(List<IFighterControl> list, IFighterControl bot)
    {
        list.Add(bot);
        return bot;
    }
}
