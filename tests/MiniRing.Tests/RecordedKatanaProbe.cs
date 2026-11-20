using System.Diagnostics;
using MiniRing.Replay;
using MiniRing.Weapons;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("Probe")]
public sealed class RecordedKatanaProbe
{
    [TestMethod]
    public void MeasureRecordedInput()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "MiniRing.slnx")))
        {
            directory = directory.Parent ?? throw new DirectoryNotFoundException("MiniRing repository");
        }

        var path = Environment.GetEnvironmentVariable("MINIRING_PROFILE_REPLAY")
            ?? Path.Combine(directory.FullName, "replays", "miniring_desktop_20261114_204945.mrrep");
        using var file = File.OpenRead(path);
        var data = ReplayFile.Read(file, comparisonPhysicsVersion: 27);
        using var player = new ReplayPlayer(data);
        var contact = new List<double>();
        var other = new List<double>();
        var cuts = 0;
        while (!player.Finished)
        {
            var start = Stopwatch.GetTimestamp();
            player.StepOnce();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var landed = player.Game.WeaponHits.Any(hit => hit.Attacker == player.Game.Player);
            cuts += player.Game.WeaponHits.Where(hit => hit.Attacker == player.Game.Player).Sum(hit => hit.Cells);
            (landed ? contact : other).Add(elapsed);
        }

        Assert.AreEqual(WeaponKind.Katana, data.Header.Corners.PlayerWeapon);
        Assert.IsGreaterThan(0, contact.Count);
        Console.WriteLine($"Recorded physics {data.Header.Physics}; current {Fighter.PhysicsVersion}; checksum matches {player.ChecksumsMatched}/{data.Checksums.Count}; first divergence {player.FirstDivergence}; opponent cells {cuts}.");
        foreach (var (label, samples) in new[] { ("katana hit", contact), ("other", other) })
        {
            samples.Sort();
            Console.WriteLine($"{label} n={samples.Count}, p50={samples[samples.Count / 2]:F2} ms, p95={samples[(int)(samples.Count * .95)]:F2} ms, max={samples[^1]:F2} ms");
        }
    }
}
