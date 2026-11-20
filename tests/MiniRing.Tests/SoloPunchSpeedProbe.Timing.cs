using System.Globalization;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

public sealed partial class SoloPunchSpeedProbe
{
    private sealed record Rotation(float Hip, float Chest, float Lead, float Foot);
    private sealed record Trial(string Stage, Rotation Rotation, float Delay, float Range, float Load,
        float Fist, float Shoulder, float HipSweep, float FootEnergy);

    [TestMethod]
    public void OptimizeRotationAndFistTiming()
    {
        var hips = new[] { 0.45f, 0.70f, 0.95f };
        var chests = new[] { 0.012f, 0.025f, 0.050f };
        var leads = new[] { 0f, 0.04f, 0.08f };
        var feet = new[] { 0f, 1f };
        var grid = from hip in hips
                   from chest in chests
                   from lead in leads
                   from foot in feet
                   select new Rotation(hip, chest, lead, foot);
        var reference = new Rotation(0.45f, 0.025f, 0f, 0f);
        var rotationTrials = grid.Select(r => Run("shoulder", r, 0f)).ToArray();
        var finalists = rotationTrials.Where(t => t.Rotation.Foot > 0f && t.FootEnergy > 0f)
            .OrderByDescending(t => t.Shoulder).Take(3).ToArray();
        Assert.HasCount(3, finalists);
        var delays = new[] { -0.06f, -0.04f, -0.02f, 0f, 0.02f, 0.04f, 0.06f, 0.08f };
        var fistTrials = (from winner in finalists
                          from delay in delays
                          select Run("fist", winner.Rotation, delay)).ToArray();
        var chosen = fistTrials.MaxBy(t => t.Fist)!;
        var comparisonRanges = new[] { 1.05f, 1.18f };
        var holdout = (from range in comparisonRanges
                       from load in new[] { 0.1343f, BotStyle.MaxTelegraph }
                       from candidate in new[] { (reference, 0f), (chosen.Rotation, chosen.Delay) }
                       select Run("holdout", candidate.Item1, candidate.Item2, range, load)).ToArray();
        const string header = "stage,hip,chestSeconds,twistLead,foot,handDelay,range,load,fistPeak,shoulderPeak,hipSweep,footEnergy";
        var results = rotationTrials.Concat(fistTrials).Concat(holdout).Select(t => string.Create(CultureInfo.InvariantCulture,
            $"{t.Stage},{t.Rotation.Hip},{t.Rotation.Chest},{t.Rotation.Lead},{t.Rotation.Foot},{t.Delay},{t.Range},{t.Load},{t.Fist},{t.Shoulder},{t.HipSweep},{t.FootEnergy}"));
        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_timing.csv"), results.Prepend(header));
    }

    private static Trial Run(string stage, Rotation r, float delay, float range = 1.3f, float load = BotStyle.MaxTelegraph)
    {
        var tuning = FighterTuning.Default with { HipTwistShare = r.Hip, ChestTwistResponseSeconds = r.Chest };
        var knobs = new PunchLab.DriveKnobs(TwistLead: r.Lead, HandDelay: delay, FootDrive: r.Foot);
        var (fist, shoulder, hip, energy) = MeasureSwing(stage, tuning, load, range, [], knobs: knobs);
        return new Trial(stage, r, delay, range, load, fist, shoulder, hip, energy);
    }
}
