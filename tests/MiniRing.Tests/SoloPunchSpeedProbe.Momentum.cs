using System.Globalization;

namespace MiniRing.Tests;

public sealed partial class SoloPunchSpeedProbe
{
    [TestMethod]
    public void MeasureMomentumShortcutAblation()
    {
        var profiles = new (string Name, FighterTuning Tuning)[]
        {
            ("compact", FighterTuning.CompactPunch),
            ("adaptive", FighterTuning.AdaptivePunch),
            ("dynamic", FighterTuning.DynamicPunch),
        };
        var output = new List<string> { "profile,case,hz,fist,forwardAtFist,shoulderAtFist,shoulderRotationAtFist,pelvisMax,bodyMax" };
        foreach (var (name, profile) in profiles)
        {
            // 無限の時定数では 1 - exp(-dt / time) が 0。目標先行と胴の減速の両方を止め、手の Spring 指令は従来のまま。
            var noLead = profile with { BodyToFistTime = float.PositiveInfinity };
            var linearReaction = noLead with { HandReactionShare = 1f, BodyDrive = 0f, WeightShiftShare = 0f };
            var cases = new (string Name, FighterTuning Tuning)[]
            {
                ("baseline", profile),
                ("no_target_lead", noLead),
                ("balanced_arm_linear", linearReaction),
                ("also_no_leg_impulse", linearReaction with { Footwork = linearReaction.Footwork with { StepSpringPerKg = 0f } }),
            };
            foreach (var (label, tuning) in cases)
            {
                foreach (var hz in new[] { 240, 480 })
                {
                    var result = RunShoulderTrial(label, tuning, hz: hz);
                    var p = result.AtFist;
                    output.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{name},{label},{hz},{p.Fist:G9},{p.FistForward:G9},{p.Shoulder:G9},{p.Shoulder - p.ChestTranslation:G9},{result.PelvisTravel:G9},{result.BodyTravel:G9}"));
                }
            }
        }

        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_momentum_ablation.csv"), output);
    }
}
