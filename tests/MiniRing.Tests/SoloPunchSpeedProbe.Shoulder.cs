using System.Globalization;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

public sealed partial class SoloPunchSpeedProbe
{
    private readonly record struct ShoulderSample(float Time, float Fist, float Shoulder, float ChestTranslation,
        float PelvisTravel, float BodyTravel, float PelvisSpeed, float FistForward, float HeadY,
        float BodyMass, float ElbowShortening, float ArmForce);

    private sealed record ShoulderTrial(string Name, FighterTuning Tuning, float Load, float Range, int Hz,
        ShoulderSample Peak, ShoulderSample AtFist, float PelvisTravel, float BodyTravel);

    [TestMethod]
    public void MeasureCompactShoulderSweep()
    {
        var baseline = FighterTuning.Default with { SplitTorso = true, CompactTorsoWave = true };
        var reference = RunShoulderTrial("baseline", baseline);
        var controls = new[]
        {
            reference,
            RunShoulderTrial("forward_wave", baseline with { CompactTorsoWave = false }),
            RunShoulderTrial("no_foot_couple", baseline with { Footwork = baseline.Footwork with { StepSpringPerKg = 0f } }),
            RunShoulderTrial("foot_fast", baseline with { WaveFootSeconds = 0.012f }),
            RunShoulderTrial("foot_slow", baseline with { WaveFootSeconds = 0.050f }),
            RunShoulderTrial("foot_release_fast", baseline with { Footwork = baseline.Footwork with { StepPushTime = 0.030f } }),
            RunShoulderTrial("foot_release_slow", baseline with { Footwork = baseline.Footwork with { StepPushTime = 0.100f } }),
            RunShoulderTrial("hip_reduced", baseline with { HipTwistShare = 0.70f }),
        };

        var angles = new[] { 35f, 45f, 55f };
        var torques = new[] { 120f, 180f, 240f };
        var timing = new (float Pelvis, float Abdomen, float Chest)[]
        {
            (0.012f, 0.020f, 0.022f), (0.008f, 0.012f, 0.014f),
            (0.008f, 0.020f, 0.035f), (0.020f, 0.028f, 0.018f),
        };
        var grid = (from angle in angles
                    from torque in torques
                    from phase in timing
                    let tuning = baseline with
                    {
                        MaxTorsoTwistRadians = angle * MathF.PI / 180f,
                        TrunkRotatorTorque = torque,
                        WavePelvisSeconds = phase.Pelvis,
                        WaveAbdomenSeconds = phase.Abdomen,
                        WaveChestSeconds = phase.Chest,
                    }
                    select RunShoulderTrial("grid", tuning)).ToArray();

        // 腰と全身重心の前進は、基準の最大値に 1 cm の余裕を足した範囲で比較。
        bool Compact(ShoulderTrial run) => run.PelvisTravel <= reference.PelvisTravel + 0.01f
            && run.BodyTravel <= reference.BodyTravel + 0.01f;
        var candidates = controls.Concat(grid).Where(Compact).ToArray();
        var byShoulder = candidates.MaxBy(t => t.Peak.Shoulder)!;
        var byFistPhase = candidates.MaxBy(t => t.AtFist.Shoulder)!;
        var footTiming = new[] { 0.012f, 0.050f };
        var footRelease = new[] { 0.030f, 0.100f };
        var coupled = (from candidate in new[] { byShoulder, byFistPhase }.Distinct()
                       from seconds in footTiming
                       from release in footRelease
                       select RunShoulderTrial("foot_refine", candidate.Tuning with
                       {
                           WaveFootSeconds = seconds,
                           Footwork = candidate.Tuning.Footwork with { StepPushTime = release },
                       })).ToArray();

        var seed = candidates.Concat(coupled.Where(Compact)).MaxBy(t => t.Peak.Shoulder)!.Tuning;
        var refineAngles = new[] { 50f, 55f, 60f, 65f };
        var refineTorques = new[] { 120f, 180f, 240f };
        var refineTiming = new (float Abdomen, float Chest)[] { (0.028f, 0.018f), (0.034f, 0.022f) };
        var refined = (from angle in refineAngles
                       from torque in refineTorques
                       from phase in refineTiming
                       select RunShoulderTrial("angle_refine", seed with
                       {
                           MaxTorsoTwistRadians = angle * MathF.PI / 180f,
                           TrunkRotatorTorque = torque,
                           WaveAbdomenSeconds = phase.Abdomen,
                           WaveChestSeconds = phase.Chest,
                       })).ToArray();
        var qualified = candidates.Concat(coupled.Where(Compact)).Concat(refined.Where(Compact)).ToArray();
        var best = qualified.MaxBy(t => t.Peak.Shoulder)!;
        var bestAtFist = qualified.MaxBy(t => t.AtFist.Shoulder)!;
        var closest = qualified.Where(t => t.Tuning.TrunkRotatorTorque == baseline.TrunkRotatorTorque)
            .MaxBy(t => t.Peak.Shoulder)!;
        var holdout = from candidate in new[] { reference, best, bestAtFist, closest }.Distinct()
                      from scenario in new (float Load, float Range, int Hz)[]
                      {
                          (BotStyle.MaxTelegraph, 1.05f, 240), (0.1343f, 1.3f, 240),
                          (BotStyle.MaxTelegraph, 1.3f, 480),
                      }
                      select RunShoulderTrial("hold_" + candidate.Name, candidate.Tuning, scenario.Load, scenario.Range, scenario.Hz);

        const string header = "stage,load,range,hz,angleDeg,torque,pelvisSec,abdomenSec,chestSec,footSec,footReleaseSec,footSpring,hipShare,pelvisMax,bodyMax,shoulderPeak,shoulderPeakTime,chestAtShoulderPeak,pelvisAtShoulderPeak,fistPeak,fistPeakTime,shoulderAtFist,chestAtFist,pelvisAtFist";
        var output = controls.Concat(grid).Concat(coupled).Concat(refined).Concat(holdout).Select(t => string.Create(CultureInfo.InvariantCulture,
            $"{t.Name},{t.Load:G9},{t.Range:G9},{t.Hz},{t.Tuning.MaxTorsoTwistRadians * 180f / MathF.PI:G9},{t.Tuning.TrunkRotatorTorque:G9},{t.Tuning.WavePelvisSeconds:G9},{t.Tuning.WaveAbdomenSeconds:G9},{t.Tuning.WaveChestSeconds:G9},{t.Tuning.WaveFootSeconds:G9},{t.Tuning.Footwork.StepPushTime:G9},{t.Tuning.Footwork.StepSpringPerKg:G9},{t.Tuning.HipTwistShare:G9},{t.PelvisTravel:G9},{t.BodyTravel:G9},{t.Peak.Shoulder:G9},{t.Peak.Time:G9},{t.Peak.ChestTranslation:G9},{t.Peak.PelvisSpeed:G9},{t.AtFist.Fist:G9},{t.AtFist.Time:G9},{t.AtFist.Shoulder:G9},{t.AtFist.ChestTranslation:G9},{t.AtFist.PelvisSpeed:G9}"));
        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_compact_shoulder_sweep.csv"), output.Prepend(header));
    }

    private static ShoulderTrial RunShoulderTrial(string name, FighterTuning tuning,
        float load = BotStyle.MaxTelegraph, float range = 1.3f, int hz = 240)
    {
        var samples = new List<ShoulderSample>();
        var knobs = new PunchLab.DriveKnobs(HandDelay: -0.02f);
        MeasureSwing(name, tuning, load, range, null, knobs: knobs, physicsHz: hz, shoulderSamples: samples);
        Assert.IsTrue(samples.Count > 0);
        return new ShoulderTrial(name, tuning, load, range, hz, samples.MaxBy(s => s.Shoulder),
            samples.MaxBy(s => s.Fist), samples.Max(s => s.PelvisTravel), samples.Max(s => s.BodyTravel));
    }
}
