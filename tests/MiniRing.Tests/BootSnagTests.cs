using System.Numerics;

namespace MiniRing.Tests;

/// <summary>ブーツの引っかかり: 何もないキャンバスの上で足が運べること、縁の外へ出た足は落ちること（仕様）。</summary>
[TestClass]
[TestCategory("slow")]
public sealed class BootSnagTests
{
    /// <summary>
    /// 対角・でたらめに歩く道は、直す前は後ろ足が前足より前へ出られず 1 cm ずつ刻み、伸びきった前足が置き直されないまま 25 秒のうち 17〜24 秒止まった
    /// （骨盤の進みが指令の 1 割以下、回りきれない）。直した後は止まらず回りきる（止まっても 1 秒未満）。
    /// </summary>
    [TestMethod]
    public void WalkingDiagonallyOnTheCanvas_NeverStallsAndFinishesTheCourse()
    {
        foreach (var seed in new[] { 1, 2, 3 })
        {
            foreach (var (name, waypoints) in BootSnagDrill.Paths(seed))
            {
                if (name is not ("diagonals" or "square" or "circle") && !name.StartsWith("random", StringComparison.Ordinal))
                {
                    continue;
                }

                var r = BootSnagDrill.Walk(name, waypoints, 25f);
                Assert.IsTrue(r.Finished, $"{name} (seed {seed}): the walker never finished the course (stuck for {r.StallSeconds:0.0} s)");
                Assert.IsLessThan(1f, r.StallSeconds, $"{name} (seed {seed}): stalled {r.StallSeconds:0.00} s over {r.Stalls} events");
                Assert.AreEqual(0, r.FootSnags, $"{name} (seed {seed}): a swinging foot stuck on the floor {r.FootSnagSeconds:0.00} s");
                Assert.IsGreaterThan(0.5f, r.SpeedRatio, $"{name} (seed {seed}): pelvis speed / command {r.SpeedRatio:0.00}");
            }
        }
    }

    /// <summary>リングの縁の外には、キャンバスが無く足が下がる（落ちる）。仕様なので変えない。</summary>
    [TestMethod]
    public void AFootBeyondTheCanvasEdge_FallsBelowTheCanvas()
    {
        var (world, fighter, ring, _) = BootSnagDrill.Build();
        using (world)
        {
            for (var i = 0; i < 240; i++)
            {
                fighter.ApplyControl(BootSnagDrill.Command(fighter, Vector3.Zero, Vector3.UnitZ), BootSnagDrill.Dt);
                world.Step(BootSnagDrill.Dt);
            }

            var lowest = float.MaxValue;
            for (var i = 0; i < 240 * 6; i++)
            {
                fighter.ApplyControl(BootSnagDrill.Command(fighter, -Vector3.UnitZ, Vector3.UnitZ), BootSnagDrill.Dt);
                world.Step(BootSnagDrill.Dt);
                lowest = MathF.Min(lowest, MathF.Min(world.GetState(fighter.ToeId(0)).Position.Y, world.GetState(fighter.ToeId(1)).Position.Y));
            }

            // つま先の箱の半分の厚さは約 1 cm。キャンバスに乗っていれば、中心はキャンバスの上 1 cm あたり。縁の外へ出れば 3 cm 以上下がる。
            Assert.IsLessThan(ring.SurfaceHeight - 0.02f, lowest, $"toe never dropped below the canvas (lowest {lowest - ring.SurfaceHeight:0.000} m)");
        }
    }
}
