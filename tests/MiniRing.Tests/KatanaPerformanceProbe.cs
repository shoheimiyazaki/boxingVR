using System.Diagnostics;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
public sealed class KatanaPerformanceProbe
{
    [TestMethod]
    public void MeasureContactSteps()
    {
        var corners = BoxingCorners.Default with
        {
            Player = SwordBot.Basic, Cpu = SwordBot.Basic,
            PlayerWeapon = WeaponKind.Katana, CpuWeapon = WeaponKind.Katana,
            PlayerTwoHanded = true, CpuTwoHanded = true,
        };
        var quiet = new List<double>();
        var contact = new List<double>();
        for (var seed = 0; seed < 5; seed++)
        {
            using var game = new BoxingGame(seed: seed, corners: corners);
            for (var frame = 0; frame < 300; frame++)
            {
                var start = Stopwatch.GetTimestamp();
                game.Step(default, 1f / 60f);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (seed == 0) { continue; }
                (game.RecentCuts.Count > 0 ? contact : quiet).Add(elapsed);
            }
            Console.WriteLine($"seed {seed} checksum {game.StateChecksum():x}");
        }

        Assert.IsGreaterThan(0, contact.Count);
        foreach (var (name, samples) in new[] { ("quiet", quiet), ("contact", contact) })
        {
            samples.Sort();
            Console.WriteLine($"{name} n={samples.Count} p50={samples[samples.Count / 2]:F3} p95={samples[(int)(samples.Count * 0.95)]:F3} max={samples[^1]:F3} ms");
        }
    }
}
