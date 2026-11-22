using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

public sealed partial class SoloPunchSpeedProbe
{
    [TestMethod]
    public void MeasureCompactForceBottlenecks()
    {
        var baseline = FighterTuning.Default with { SplitTorso = true, CompactTorsoWave = true };
        var fasterHand = baseline with { WaveHandSeconds = 0.04f };
        var fasterElbow = baseline with { ElbowMuscleMaxSpeed = 60f };
        var cases = new (string Name, FighterTuning Tuning)[]
        {
            ("baseline", baseline),
            ("hand70ms", baseline with { WaveHandSeconds = 0.07f }),
            ("hand40ms", fasterHand),
            ("hand10ms", baseline with { WaveHandSeconds = 0.01f }),
            ("elbow60", fasterElbow),
            ("curvature035", baseline with { HandMuscleCurvature = 0.35f }),
            ("force700", baseline with { MaxHandForce = 700f }),
            ("activation5ms", baseline with { MuscleActivationTime = 0.005f }),
            ("targetCap30", baseline with { MaxTargetSpeed = 30f }),
            ("targetSmoothing45", baseline with { TargetVelocitySmoothing = 45f }),
            ("armServo14", baseline with { ArmServoHz = 14f }),
            ("handKp600", baseline with { HandKp = 600f, HandKd = baseline.HandKd * MathF.Sqrt(600f / baseline.HandKp) }),
            ("hand40msElbow60", fasterHand with { ElbowMuscleMaxSpeed = fasterElbow.ElbowMuscleMaxSpeed }),
        };
        var rows = new List<string> { SwingHeader };
        var knobs = new PunchLab.DriveKnobs(HandDelay: -0.02f);
        foreach (var distance in new[] { 1.05f, 1.30f })
        {
            foreach (var (name, tuning) in cases)
            {
                MeasureSwing(name, tuning, BotStyle.MaxTelegraph, distance, rows, knobs: knobs);
            }
        }

        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_compact_force.csv"), rows);
    }
}
