namespace MiniRing.Tests;

[TestClass]
[TestCategory("Probe")]
public sealed class JointPunchCadenceProbe
{
    [TestMethod]
    [DataRow(240)]
    [DataRow(480)]
    public void MeasureHybridTorsoYawByCadence(int hz)
    {
        var dt = 1f / hz;
        var throwSteps = (int)MathF.Round(JointPunchSeriesTests.ThrowSeconds / dt);
        foreach (var guard in new[] { 0f, 0.025f, 0.060f, 0.120f, 0.200f, 0.350f, 0.500f })
        {
            var guardSteps = (int)MathF.Round(guard / dt);
            var cadence = (float)hz / (throwSteps + guardSteps);
            foreach (var alternate in new[] { false, true })
            {
                var series = JointPunchSeriesTests.RunSeries(alternate, grounded: true, dt, FighterTuning.HybridPunch, guard);
                Write(1, series.First);
                Write(2, series.Second);
                Write(3, series.Third);

                void Write(int number, JointPunchSeriesTests.Strike strike)
                {
                    var pattern = alternate ? "RLR" : "RRR";
                    Console.WriteLine(FormattableString.Invariant(
                        $"CADENCE,{hz},{guard * 1000f:F0},{guardSteps * dt * 1000f:F3},{cadence:F4},{pattern},{number},{strike.Toward.X:F3},{strike.Toward.Y:F3},{strike.Toward.Z:F3},{strike.Span.X:F3},{strike.Span.Y:F3},{strike.Span.Z:F3},{strike.InitialYaw:F3},{strike.Speed:F3},{strike.PeakTime * 1000f:F3},{strike.EndState}"));
                }
            }
        }
    }
}
