using System.Diagnostics;
using Aigis.Render.Voxels;
using MiniRing.Bots;
using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>調査用: リプレイを裏で走査するときの、1 ステップの時間と場面 1 枚の大きさ。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ReplayScanCostProbe
{
    [TestMethod]
    public void MeasureScanCost()
    {
        var corners = BotCatalog.Corners("swarmer") with { RedBotStyle = BotCatalog.Style("champion") };
        using var game = new BoxingGame(corners: corners.StartingAtCorners(), seed: 3);
        using var stream = new MemoryStream();
        var steps = 240 * 20;
        var watch = Stopwatch.StartNew();
        using (var recorder = new ReplayRecorder(stream, game, "probe"))
        {
            for (var i = 0; i < steps; i++) { recorder.Step(game, default, 1f / 240f); }
        }
        Console.WriteLine($"live: {steps} steps in {watch.Elapsed.TotalSeconds:0.00}s");
        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        using var player = new ReplayPlayer(data);
        var producer = new VoxelSnapshotProducer();
        var kept = new List<object>();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalMemory(true);
        watch.Restart();
        var captureTicks = 0L; Aigis.Render.PrimitiveInstance[]? prev = null; long same = 0, total = 0, lineCount = 0, particleCount = 0, voxelCount = 0;
        while (!player.Finished)
        {
            player.StepOnce();
            if (player.Position % 4 == 0)
            {
                var t = Stopwatch.GetTimestamp();
                var (instances, lines) = player.Game.BuildRenderables();
                var arr = instances.ToArray();
                if (prev is not null && prev.Length == arr.Length) { for (var k = 0; k < arr.Length; k++) { if (arr[k].Equals(prev[k])) { same++; } } total += arr.Length; }
                prev = arr;
                lineCount += lines.Count; particleCount += player.Game.BuildParticles().Count; voxelCount += player.Game.BuildVoxelVolumes().Count;
                kept.Add((arr, lines.ToArray(), player.Game.BuildParticles().ToArray(), producer.Capture(player.Game.BuildVoxelVolumes())));
                captureTicks += Stopwatch.GetTimestamp() - t;
            }
        }
        var seconds = watch.Elapsed.TotalSeconds;
        var after = GC.GetTotalMemory(true);
        Console.WriteLine($"replay+capture: {steps} steps in {seconds:0.00}s ({steps / seconds:0} steps/s), capture {captureTicks * 1000.0 / Stopwatch.Frequency:0} ms");
        Console.WriteLine($"{kept.Count} scenes, {(after - before) / 1024.0 / 1024.0:0.0} MiB, {(after - before) / kept.Count / 1024.0:0.0} KiB/scene");
        Console.WriteLine($"instances/scene {total / (double)Math.Max(1, kept.Count - 1):0}, unchanged {same / (double)Math.Max(1, total):P0}, lines {lineCount / kept.Count}, particles {particleCount / kept.Count}, voxel volumes {voxelCount / kept.Count}");
        GC.KeepAlive(kept);
    }
}
