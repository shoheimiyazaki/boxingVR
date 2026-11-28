using System.Collections.Concurrent;
using Aigis.Player;
using MiniRing.Replay;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("L0")]
public sealed class FixedSimulationTests
{
    private sealed record State(long Step);

    [TestMethod]
    public void SlowStep_DoesNotBlockReader_AndContinuesWithoutRenderRequests()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var simulation = new FixedSimulation<int, State>(new(-1), 0, (_, next) => next, x => x,
            (step, _, dt) =>
            {
                Assert.AreEqual(1f / 240f, dt);
                if (step == 0) { entered.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10))); }
                return new(step);
            });
        simulation.Submit(1);
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.AreEqual(-1L, simulation.Latest.Step);
        }
        finally { release.Set(); }
        Assert.IsTrue(SpinWait.SpinUntil(() => simulation.Latest.Step >= 3, TimeSpan.FromSeconds(10)));
    }

    [TestMethod]
    public void PendingEdges_AreMerged_AndConsumedOnce()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var inputs = new ConcurrentQueue<(long Step, bool Edge)>();
        using var simulation = new FixedSimulation<bool, State>(new(-1), false, (old, next) => old || next, _ => false,
            (step, edge, _) =>
            {
                if (step == 0) { entered.Set(); Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(10))); }
                inputs.Enqueue((step, edge));
                return new(step);
            });
        simulation.Submit(false);
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
            simulation.Submit(true);
            simulation.Submit(false);
        }
        finally { release.Set(); }
        Assert.IsTrue(SpinWait.SpinUntil(() => simulation.CompletedSteps >= 4, TimeSpan.FromSeconds(10)));
        var applied = inputs.ToArray();
        Assert.IsTrue(applied.Single(x => x.Step == 1).Edge);
        Assert.IsFalse(applied.Single(x => x.Step == 2).Edge);
    }

    [TestMethod]
    public void WorkerFailure_IsReportedToReaderAndDisposer()
    {
        var simulation = new FixedSimulation<int, State>(new(-1), 0, (_, next) => next, x => x,
            (_, _, _) => throw new InvalidOperationException("simulation failed"));
        simulation.Submit(1);
        Assert.IsTrue(SpinWait.SpinUntil(() =>
        {
            try { _ = simulation.Latest; return false; }
            catch (InvalidOperationException) { return true; }
        }, TimeSpan.FromSeconds(10)));
        Assert.ThrowsExactly<InvalidOperationException>(() => simulation.Dispose());
    }

    [TestMethod]
    public void AppliedStepInputs_RecordAndReplayBitExactly()
    {
        using var game = new BoxingGame(seed: 73);
        using var stream = new MemoryStream();
        var applied = new ConcurrentQueue<(long Step, BoxingInput Input)>();
        using (var recorder = new ReplayRecorder(stream, game, "fixed-worker"))
        using (var simulation = new FixedSimulation<BoxingInput, State>(new(-1), default,
            (old, next) => next with { ResetRequested = old.ResetRequested || next.ResetRequested },
            input => input with { ResetRequested = false }, (step, input, dt) =>
            {
                recorder.Step(game, input, dt);
                applied.Enqueue((step, input));
                return new(step);
            }))
        {
            simulation.Submit(default);
            Assert.IsTrue(SpinWait.SpinUntil(() => simulation.CompletedSteps >= 3, TimeSpan.FromSeconds(30)));
            simulation.Submit(new BoxingInput(default, ResetRequested: true));
            Assert.IsTrue(SpinWait.SpinUntil(() => simulation.CompletedSteps >= ReplayRecorder.ChecksumInterval + 3,
                TimeSpan.FromSeconds(60)));
        }
        var data = ReplayFile.Read(new MemoryStream(stream.ToArray()));
        var inputs = applied.ToArray();
        for (var i = 0; i < inputs.Length; i++)
        {
            Assert.AreEqual((long)i, inputs[i].Step);
            Assert.AreEqual(inputs[i].Input, data.Steps[i].Input);
            Assert.AreEqual(1f / 240f, data.Steps[i].Dt);
        }
        Assert.AreEqual(1, inputs.Count(x => x.Input.ResetRequested));
        var replay = ReplayPlayer.Verify(data);
        Assert.IsTrue(replay.Reproduced);
        Assert.IsGreaterThan(0, replay.ChecksumsMatched);
    }
}
