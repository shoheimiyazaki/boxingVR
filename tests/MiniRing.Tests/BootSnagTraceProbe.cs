namespace MiniRing.Tests;

/// <summary>調べもの: 指定した道を歩く足・骨盤の位置の経過と、引っかかっている間の接触、原因を切り分ける実験。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class BootSnagTraceProbe
{
    public TestContext TestContext { get; set; } = null!;


    [TestMethod]
    public void Diagonal()
    {
        foreach (var (n, w) in BootSnagDrill.Paths(1).Where(p => p.Name is "diagonals"))
        {
            BootSnagDrill.Walk(n, w, 8f, TestContext.WriteLine, TestContext.WriteLine);
        }
    }

    [TestMethod]
    public void FrictionExperiment()
    {
        foreach (var mu in new[] { 1f, 0.5f, 0.2f, 0f })
        {
            foreach (var (n, w) in BootSnagDrill.Paths(1).Where(p => p.Name is "diagonals" or "random-1"))
            {
                var r = BootSnagDrill.Walk(n, w, 25f, setup: (world, f) =>
                {
                    foreach (var foot in new[] { Footwork.Lead, Footwork.Rear })
                    {
                        world.SetFriction(f.ToeId(foot), mu);
                        world.SetFriction(f.HeelId(foot), mu);
                    }
                });
                TestContext.WriteLine($"mu {mu}: {r.Name} stalls {r.Stalls} {r.StallSeconds:0.00}s ratio {r.SpeedRatio:0.00} finished {r.Finished}");
            }
        }
    }

    [TestMethod]
    public void WalkOffTheEdge()
    {
        foreach (var dir in new[] { new System.Numerics.Vector3(0, 0, -1), new System.Numerics.Vector3(1, 0, 0), new System.Numerics.Vector3(0, 0, 1) })
        {
            var (world, f, ring, _) = BootSnagDrill.Build();
            using (world)
            {
                for (var i = 0; i < 240; i++)
                {
                    f.ApplyControl(BootSnagDrill.Command(f, System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitZ), BootSnagDrill.Dt);
                    world.Step(BootSnagDrill.Dt);
                }

                var minY = 9f;
                for (var i = 0; i < 240 * 8; i++)
                {
                    f.ApplyControl(BootSnagDrill.Command(f, dir, System.Numerics.Vector3.UnitZ), BootSnagDrill.Dt);
                    world.Step(BootSnagDrill.Dt);
                    minY = Math.Min(minY, Math.Min(world.GetState(f.ToeId(0)).Position.Y, world.GetState(f.ToeId(1)).Position.Y) - ring.SurfaceHeight);
                    if (i % 240 == 239)
                    {
                        var p = f.PelvisPosition - ring.SurfaceCenter;
                        TestContext.WriteLine($"dir {dir} t={(i + 1) / 240} pelvis ({p.X:0.00},{p.Y:0.00},{p.Z:0.00}) minToeY {minY:0.000} state {f.State}");
                    }
                }
            }
        }
    }
}
