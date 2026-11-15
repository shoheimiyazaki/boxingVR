using MiniRing.Vr;

namespace MiniRing.Tests;

[TestClass]
public sealed class VrSimulationTests
{
    [TestMethod]
    public void AudioConsumerPreservesEveryFrameAndDrainsBeforeShutdown()
    {
        var consumed = new System.Collections.Concurrent.ConcurrentQueue<float>();
        using (var audio = new VrAudioPlayback(frame => consumed.Enqueue(frame.Dt)))
        {
            for (var i = 1; i <= 20; i++) { audio.Submit(new(i, [], [], [], [], RoundBell.None, [])); }
        }
        CollectionAssert.AreEqual(Enumerable.Range(1, 20).Select(i => (float)i).ToArray(), consumed.ToArray());
    }

    [TestMethod]
    public void AudioConsumerFailureIsReportedAndClosedQueueRejectsEvents()
    {
        var audio = new VrAudioPlayback(_ => throw new InvalidOperationException("audio device failure"));
        audio.Submit(new(0f, [], [], [], [], RoundBell.None, []));
        Assert.ThrowsExactly<InvalidOperationException>(() => audio.Dispose());
        Assert.ThrowsExactly<InvalidOperationException>(() => audio.ThrowIfFaulted());
        Assert.ThrowsExactly<ObjectDisposedException>(() => audio.Submit(new(0f, [], [], [], [], RoundBell.None, [])));
    }

    [TestMethod]
    public void PhysicsKeepsFixedRateWithoutFurtherDisplayFrames()
    {
        var deltas = new System.Collections.Concurrent.ConcurrentQueue<float>();
        var initial = new VrScene([], [], [], [], default, null, "initial", []);
        using var simulation = new VrSimulation(initial, (_, dt) =>
        {
            deltas.Enqueue(dt);
            return initial;
        });
        simulation.Submit(default, 1f / 90f);
        Assert.IsTrue(SpinWait.SpinUntil(() => deltas.Count >= 3, TimeSpan.FromSeconds(5)), "Physics must continue without display input.");
        Assert.IsTrue(deltas.All(dt => dt == 1f / 240f));
    }

    [TestMethod]
    public void HapticsSurviveSkippedSnapshotsAndAreConsumedOnce()
    {
        var initial = new VrScene([], [], [], [], default, null, "initial", []);
        var hit = new VrHaptic(Hand.Left, 0.6f, 0.1f);
        var steps = 0;
        using var simulation = new VrSimulation(initial, (_, _) => ++steps <= 3 ? initial with { Haptics = [hit] } : initial);
        simulation.Submit(default, 1f / 90f);
        Assert.IsTrue(SpinWait.SpinUntil(() => simulation.CompletedSteps >= 4, TimeSpan.FromSeconds(5)));
        var events = simulation.DrainHaptics();
        Assert.AreEqual(3, events.Length);
        Assert.IsTrue(events.All(value => value == hit));
        Assert.IsEmpty(simulation.DrainHaptics());
    }

    [TestMethod]
    public void CalibrationRequestKeepsItsOriginalPoseWhenNewTrackingArrives()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var received = new System.Collections.Concurrent.ConcurrentQueue<XrInputFrame>();
        var initial = new VrScene([], [], [], [], default, null, "initial", []);
        using var simulation = new VrSimulation(initial, (frame, _) =>
        {
            received.Enqueue(frame);
            if (received.Count == 1) { started.Set(); release.Wait(); }
            return initial;
        });
        try
        {
            simulation.Submit(default, 1f / 90f);
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            simulation.Submit(default(XrInputFrame) with { CalibratePressed = true, HeadPosition = new(0f, 1.8f, 0f) }, 1f / 90f);
            simulation.Submit(default(XrInputFrame) with { HeadPosition = new(0f, 1.2f, 0f) }, 1f / 90f);
        }
        finally { release.Set(); }
        Assert.IsTrue(SpinWait.SpinUntil(() => received.Any(frame => frame.CalibratePressed), TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1.8f, received.First(frame => frame.CalibratePressed).HeadPosition.Y);
    }

    [TestMethod]
    public void RenderingCanReadLastSceneWhilePhysicsIsBlocked()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var initial = new VrScene([], [], [], [], default, null, "initial", []);
        var next = initial with { Status = "advanced" };
        using var simulation = new VrSimulation(initial, (_, _) =>
        {
            started.Set();
            release.Wait();
            return next;
        });
        try
        {
            simulation.Submit(default, 1f / 90f);
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.AreSame(initial, simulation.Latest);
            simulation.Submit(default, 1f / 90f);
            Assert.AreSame(initial, simulation.Latest);
        }
        finally
        {
            release.Set();
        }
        Assert.IsTrue(SpinWait.SpinUntil(() => ReferenceEquals(next, simulation.Latest), TimeSpan.FromSeconds(5)));
    }
}
