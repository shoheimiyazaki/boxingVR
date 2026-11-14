using MiniRing.Vr;

namespace MiniRing.Tests;

[TestClass]
public sealed class VrSimulationTests
{
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
