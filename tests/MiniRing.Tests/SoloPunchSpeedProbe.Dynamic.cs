using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Tests;

public sealed partial class SoloPunchSpeedProbe
{
    private static FighterTuning DynamicReference => FighterTuning.Default with { SplitTorso = true };

    [TestMethod]
    public void SweepDynamicPunchTuning()
    {
        var baseline = DynamicReference;
        var output = new List<string> { "id,stage,footSec,footTravel,stepPushSec,angle,torque,elbow,servo,handSec,load,range,hz,fist,shoulderAtFist,pelvisMax,bodyMax" };
        var id = 0;
        (int Id, ShoulderTrial Trial) Run(string stage, FighterTuning tuning)
        {
            var trial = RunShoulderTrial(stage, tuning);
            output.Add(DynamicRow(id, trial));
            return (id++, trial);
        }

        var reference = Run("reference", baseline);
        var footTimes = new[] { 0.012f, 0.025f, 0.050f };
        var footTravels = new[] { 0.06f, 0.12f, 0.24f };
        var releaseTimes = new[] { 0.030f, 0.0587f, 0.100f };
        var feet = (from foot in footTimes
                    from travel in footTravels
                    from release in releaseTimes
                    select Run("foot", baseline with
                    {
                        WaveFootSeconds = foot,
                        WaveFootTravelShare = travel,
                        Footwork = baseline.Footwork with { StepPushTime = release },
                    })).OrderByDescending(c => c.Trial.AtFist.Fist).Take(2).ToArray();
        var cores = new (float Angle, float Torque)[]
        {
            (35f, 120f), (55f, 120f), (65f, 240f),
        };
        var elbows = new[] { 62f, 120f, 200f };
        var servoSpeeds = new[] { baseline.ArmServoMaxSpeed, 50f };
        var handTimes = new[] { 0.025f, 0.050f, 0.100f };
        var candidates = (from foot in feet
                          from core in cores
                          from elbow in elbows
                          from servo in servoSpeeds
                          from hand in handTimes
                          select Run("combine", foot.Trial.Tuning with
                          {
                              MaxTorsoTwistRadians = core.Angle * MathF.PI / 180f,
                              TrunkRotatorTorque = core.Torque,
                              ElbowMuscleMaxSpeed = elbow,
                              ArmServoMaxSpeed = servo,
                              WaveHandSeconds = hand,
                          })).OrderByDescending(c => c.Trial.AtFist.Fist).Take(6).ToArray();

        // 腕サーボの上限を保った候補の周辺で、胸の回旋と拳の追従を詰める。
        var seed = candidates.First(c => c.Trial.Tuning.ArmServoMaxSpeed == baseline.ArmServoMaxSpeed).Trial.Tuning;
        var angles = new[] { 60f, 65f, 70f, 75f };
        var torques = new[] { 200f, 240f, 300f };
        var handRefinement = new[] { 0.015f, 0.025f, 0.035f };
        var refined = (from angle in angles
                       from torque in torques
                       from hand in handRefinement
                       select Run("refine", seed with
                       {
                           MaxTorsoTwistRadians = angle * MathF.PI / 180f,
                           TrunkRotatorTorque = torque,
                           WaveHandSeconds = hand,
                       })).OrderByDescending(c => c.Trial.AtFist.Fist).Take(6).ToArray();

        var scenarios = new (float Load, float Range, int Hz)[]
        {
            (BotStyle.MaxTelegraph, 1.30f, 480), (BotStyle.MaxTelegraph, 1.05f, 240),
            (BotStyle.MaxTelegraph, 1.05f, 480), (0.1343f, 1.30f, 240), (0.1343f, 1.30f, 480),
        };
        var stress = Run("stress", candidates[0].Trial.Tuning with { ElbowMuscleMaxSpeed = 1000f });
        foreach (var candidate in candidates.Concat(refined).Append(reference).Append(stress))
        {
            foreach (var (load, range, hz) in scenarios)
            {
                output.Add(DynamicRow(candidate.Id, RunShoulderTrial(candidate.Trial.Name, candidate.Trial.Tuning, load, range, hz)));
            }
        }

        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_dynamic_tuning.csv"), output);
    }

    [TestMethod]
    public void DynamicPunchPreset_ReachesNineAcrossLoadsAndStepRates()
    {
        var scenarios = new (float Load, float Range)[]
        {
            (BotStyle.MaxTelegraph, 1.30f), (BotStyle.MaxTelegraph, 1.05f), (0.1343f, 1.30f),
        };
        foreach (var (load, range) in scenarios)
        {
            foreach (var hz in new[] { 240, 480 })
            {
                var run = RunShoulderTrial("dynamic", FighterTuning.DynamicPunch, load, range, hz);
                var context = FormattableString.Invariant($"load {load}, range {range}, {hz} Hz");
                Assert.IsTrue(run.AtFist.Fist >= 9f, context);
                Assert.IsTrue(run.AtFist.FistForward >= 0.9f * run.AtFist.Fist, context);
            }
        }
    }

    private static string DynamicRow(int id, ShoulderTrial run) => string.Create(CultureInfo.InvariantCulture,
        $"{id},{run.Name},{run.Tuning.WaveFootSeconds:G9},{run.Tuning.WaveFootTravelShare:G9},{run.Tuning.Footwork.StepPushTime:G9},{run.Tuning.MaxTorsoTwistRadians * 180f / MathF.PI:G9},{run.Tuning.TrunkRotatorTorque:G9},{run.Tuning.ElbowMuscleMaxSpeed:G9},{run.Tuning.ArmServoMaxSpeed:G9},{run.Tuning.WaveHandSeconds:G9},{run.Load:G9},{run.Range:G9},{run.Hz},{run.AtFist.Fist:G9},{run.AtFist.Shoulder:G9},{run.PelvisTravel:G9},{run.BodyTravel:G9}");
}
