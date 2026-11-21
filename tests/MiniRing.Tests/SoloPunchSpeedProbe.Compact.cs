using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Tests;

public sealed partial class SoloPunchSpeedProbe
{
    private static FighterTuning CompactReference => FighterTuning.Default with { SplitTorso = true, CompactTorsoWave = true };

    [TestMethod]
    public void SweepCompactPunchTuning()
    {
        var baseline = CompactReference;
        var elbowSpeeds = new[] { 39.9f, 50f, 60f, 70f, 80f };
        var servoSpeeds = new[] { 23.83f, 30f, 40f, 55f };
        var handTimes = new[] { 0.04f, 0.07f, 0.10f };
        var bodies = new[]
        {
            baseline,
            baseline with { MaxTorsoTwistRadians = 55f * MathF.PI / 180f, WavePelvisSeconds = 0.020f, WaveAbdomenSeconds = 0.028f, WaveChestSeconds = 0.018f },
        };
        var grid = from body in bodies
                   from elbow in elbowSpeeds
                   from servo in servoSpeeds
                   from hand in handTimes
                   select body with { ElbowMuscleMaxSpeed = elbow, ArmServoMaxSpeed = servo, WaveHandSeconds = hand };

        var refineElbow = new[] { 56f, 58f, 60f, 62f, 64f, 66f };
        var refineHand = new[] { 0.030f, 0.035f, 0.045f, 0.050f };
        var refinement = from elbow in refineElbow
                         from hand in refineHand
                         select baseline with { ElbowMuscleMaxSpeed = elbow, WaveHandSeconds = hand };

        var scenarios = new (float Range, float Load, int Hz)[]
        {
            (1.30f, BotStyle.MaxTelegraph, 240), (1.30f, BotStyle.MaxTelegraph, 480),
            (1.05f, BotStyle.MaxTelegraph, 240), (1.05f, BotStyle.MaxTelegraph, 480),
            (1.30f, 0.1343f, 240), (1.30f, 0.1343f, 480),
        };
        const string header = "id,changedValues,angle,elbow,servo,handSec,range,load,hz,fist,shoulderAtFist,pelvisMax,bodyMax,qualifies";
        var output = new List<string> { header };
        var references = scenarios.Select(s => RunShoulderTrial("baseline", baseline, s.Load, s.Range, s.Hz)).ToArray();
        foreach (var reference in references)
        {
            output.Add(CompactRow(-1, 0, reference, qualifies: true));
        }

        var id = 0;
        foreach (var tuning in grid.Concat(refinement))
        {
            var changes = (tuning.ElbowMuscleMaxSpeed != baseline.ElbowMuscleMaxSpeed ? 1 : 0)
                + (tuning.ArmServoMaxSpeed != baseline.ArmServoMaxSpeed ? 1 : 0)
                + (tuning.WaveHandSeconds != baseline.WaveHandSeconds ? 1 : 0)
                + (tuning.MaxTorsoTwistRadians != baseline.MaxTorsoTwistRadians ? 4 : 0);
            foreach (var reference in references)
            {
                var run = RunShoulderTrial("candidate", tuning, reference.Load, reference.Range, reference.Hz);
                var qualifies = run.AtFist.Fist >= reference.AtFist.Fist
                    && WithinCompactTravel(run, reference);
                output.Add(CompactRow(id, changes, run, qualifies));
                if (!qualifies)
                {
                    break;
                }
            }

            id++;
        }

        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_compact_tuning.csv"), output);
    }

    [TestMethod]
    public void CompactPunchPreset_ReachesSixPointFiveInStandardLongPunch()
    {
        var actual = RunShoulderTrial("compact", FighterTuning.CompactPunch);
        Assert.IsTrue(actual.AtFist.Fist >= 6.5f);
        Assert.IsTrue(actual.PelvisTravel <= 0.0251f);
    }

    [TestMethod]
    public void MeasureCompactSixPointFive()
    {
        var baseline = CompactReference;
        var earlierPreset = baseline with { ElbowMuscleMaxSpeed = 62f, WaveHandSeconds = 0.035f };
        var elbows = new[] { 90f, 120f, 160f, 200f, 300f, 500f, 1000f };
        var hands = new[] { 0.025f, 0.035f, 0.050f, 0.070f, 0.100f };
        var servoSpeeds = new[] { baseline.ArmServoMaxSpeed, 50f };
        var grid = from elbow in elbows
                   from hand in hands
                   from servo in servoSpeeds
                   select baseline with { ElbowMuscleMaxSpeed = elbow, WaveHandSeconds = hand, ArmServoMaxSpeed = servo };
        var output = new List<string> { "id,elbow,servo,handSec,range,load,hz,fist,shoulderAtFist,pelvisMax,bodyMax,qualifies" };
        var scenarios = new (float Range, float Load, int Hz)[]
        {
            (1.30f, BotStyle.MaxTelegraph, 240), (1.30f, BotStyle.MaxTelegraph, 480),
            (1.05f, BotStyle.MaxTelegraph, 240), (1.05f, BotStyle.MaxTelegraph, 480),
            (1.30f, 0.1343f, 240), (1.30f, 0.1343f, 480),
        };
        var references = scenarios.Select(s => RunShoulderTrial("reference", baseline, s.Load, s.Range, s.Hz)).ToArray();

        void Record(int id, ShoulderTrial run, ShoulderTrial reference) => output.Add(string.Create(CultureInfo.InvariantCulture,
            $"{id},{run.Tuning.ElbowMuscleMaxSpeed:G9},{run.Tuning.ArmServoMaxSpeed:G9},{run.Tuning.WaveHandSeconds:G9},{run.Range:G9},{run.Load:G9},{run.Hz},{run.AtFist.Fist:G9},{run.AtFist.Shoulder:G9},{run.PelvisTravel:G9},{run.BodyTravel:G9},{WithinCompactTravel(run, reference)}"));

        foreach (var reference in references)
        {
            Record(-1, reference, reference);
            Record(-2, RunShoulderTrial("earlier_preset", earlierPreset, reference.Load, reference.Range, reference.Hz), reference);
        }

        var screened = grid.Select((tuning, id) =>
        {
            var reference = references[0];
            var run = RunShoulderTrial("screen", tuning, reference.Load, reference.Range, reference.Hz);
            Record(id, run, reference);
            return (Id: id, Run: run);
        }).ToArray();
        var nearTarget = screened.Where(c => c.Run.AtFist.Fist >= 6.5f)
            .OrderBy(c => MathF.Max(c.Run.PelvisTravel - references[0].PelvisTravel, c.Run.BodyTravel - references[0].BodyTravel))
            .Take(10);
        var compact = screened.Where(c => WithinCompactTravel(c.Run, references[0]))
            .OrderByDescending(c => c.Run.AtFist.Fist).Take(6);
        foreach (var candidate in nearTarget.Concat(compact).DistinctBy(c => c.Id))
        {
            foreach (var reference in references.Skip(1))
            {
                var run = RunShoulderTrial("validate", candidate.Run.Tuning, reference.Load, reference.Range, reference.Hz);
                Record(candidate.Id, run, reference);
            }
        }

        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_compact_6_5.csv"), output);
    }

    private static bool WithinCompactTravel(ShoulderTrial run, ShoulderTrial reference)
        => run.PelvisTravel <= reference.PelvisTravel + 0.01f && run.BodyTravel <= reference.BodyTravel + 0.01f;

    private static string CompactRow(int id, int changes, ShoulderTrial run, bool qualifies)
        => string.Create(CultureInfo.InvariantCulture,
            $"{id},{changes},{run.Tuning.MaxTorsoTwistRadians * 180f / MathF.PI:G9},{run.Tuning.ElbowMuscleMaxSpeed:G9},{run.Tuning.ArmServoMaxSpeed:G9},{run.Tuning.WaveHandSeconds:G9},{run.Range:G9},{run.Load:G9},{run.Hz},{run.AtFist.Fist:G9},{run.AtFist.Shoulder:G9},{run.PelvisTravel:G9},{run.BodyTravel:G9},{qualifies}");
}
