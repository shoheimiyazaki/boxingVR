using System.Globalization;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

public sealed partial class SoloPunchSpeedProbe
{
    private sealed record LimitCase(string Name, FighterTuning Tuning, float Gravity = 9.81f, float ContactFriction = 1f);

    [TestMethod]
    public void MeasureExtremePhysicsLimits()
    {
        var b = FighterTuning.Default with { SplitTorso = true, CompactTorsoWave = true };
        var noSpeedLoss = b with
        {
            ElbowMuscleMaxSpeed = 1000f, ShoulderMuscleMaxSpeed = 1000f,
            TiredHandSlowness = 0f,
        };
        var fastHands = b with { WaveHandSeconds = 0.001f, MaxTargetSpeed = 100f, TargetVelocitySmoothing = 240f };
        var fastCore = b with
        {
            MaxTorsoTwistRadians = 65f * MathF.PI / 180f, TrunkRotatorTorque = 240f,
            WavePelvisSeconds = 0.020f, WaveAbdomenSeconds = 0.034f, WaveChestSeconds = 0.022f,
            WaveFootSeconds = 0.050f, Footwork = b.Footwork with { StepPushTime = 0.030f },
        };
        var fastElbow = b with { ElbowMuscleMaxSpeed = 1000f };
        var cases = new LimitCase[]
        {
            new("baseline", b),
            new("elbow_1000", fastElbow),
            new("shoulder_1000", b with { ShoulderMuscleMaxSpeed = 1000f }),
            new("both_muscles_1000", b with { ElbowMuscleMaxSpeed = 1000f, ShoulderMuscleMaxSpeed = 1000f }),
            new("both_muscles_1000_no_fatigue_slow", noSpeedLoss),
            new("fatigue_slow_off", b with { TiredHandSlowness = 0f }),
            new("curvature_100", b with { HandMuscleCurvature = 100f }),
            new("elbow_angle_penalty_off", b with { ElbowEndTorque = 1f }),
            new("activation_instant", b with { MuscleActivationTime = 0f, MuscleDeactivationTime = 0f }),
            new("hand_force_10x", b with { MaxHandForce = b.MaxHandForce * 10f }),
            new("hand_kp_4x", b with { HandKp = b.HandKp * 4f }),
            new("hand_kd_zero", b with { HandKd = 0f }),
            new("target_speed_100", b with { MaxTargetSpeed = 100f }),
            new("hand_wave_1ms", b with { WaveHandSeconds = 0.001f }),
            new("hand_filters_near_off", fastHands),
            new("arm_servo_speed_1000", b with { ArmServoMaxSpeed = 1000f }),
            new("arm_servo_soft", b with { ArmServoHz = 0.1f }),
            new("arm_servo_stiff", b with { ArmServoHz = 80f }),
            new("arm_reaction_full", b with { HandReactionShare = 1f }),
            new("core_torque_10x", b with { TrunkRotatorTorque = 1200f, PelvisRotatorTorque = 1500f }),
            new("core_rate_limit_1000", b with { TrunkTwistMaxRate = 1000f }),
            new("core_targets_near_instant", b with { WavePelvisSeconds = 0.001f, WaveAbdomenSeconds = 0.001f, WaveChestSeconds = 0.001f }),
            new("core_twist_zero", b with { MaxTorsoTwistRadians = 0f }),
            new("leg_spring_off", b with { Footwork = b.Footwork with { StepSpringPerKg = 0f } }),
            new("leg_yaw_grip_wide", b with { Footwork = b.Footwork with { StepFriction = 20f, StepPushTime = 0.003f } }),
            new("low_grip_everywhere", b with { SoleFriction = 0.01f, Footwork = b.Footwork with { StepFriction = 0.01f } }, ContactFriction: 0.01f),
            new("low_gravity", b, Gravity: 1f),
            new("double_gravity", b, Gravity: 19.62f),
            new("mass_quarter", b with { MassScale = 0.25f }),
            new("mass_4x", b with { MassScale = 4f }),
            new("fast_core", fastCore),
            new("fast_core_and_muscles", fastCore with { ElbowMuscleMaxSpeed = 1000f, ShoulderMuscleMaxSpeed = 1000f }),
            new("fast_muscles_and_hand_filters", noSpeedLoss with { WaveHandSeconds = fastHands.WaveHandSeconds, MaxTargetSpeed = 100f, TargetVelocitySmoothing = 240f }),
            new("fast_muscles_and_kp", noSpeedLoss with { HandKp = b.HandKp * 4f }),
            new("fast_muscles_kp_and_force", noSpeedLoss with { HandKp = b.HandKp * 4f, MaxHandForce = b.MaxHandForce * 10f }),
            new("fast_muscles_kp_force_and_core", fastCore with { ElbowMuscleMaxSpeed = 1000f, ShoulderMuscleMaxSpeed = 1000f, TiredHandSlowness = 0f, HandKp = b.HandKp * 4f, MaxHandForce = b.MaxHandForce * 10f }),
            new("elbow_80", b with { ElbowMuscleMaxSpeed = 80f }),
            new("elbow_120", b with { ElbowMuscleMaxSpeed = 120f }),
            new("elbow_250", b with { ElbowMuscleMaxSpeed = 250f }),
            new("elbow_1000_and_servo_speed", fastElbow with { ArmServoMaxSpeed = 1000f }),
            new("elbow_1000_and_fast_core", fastCore with { ElbowMuscleMaxSpeed = 1000f }),
            new("elbow_1000_and_hand_filters", fastElbow with { WaveHandSeconds = fastHands.WaveHandSeconds, MaxTargetSpeed = 100f, TargetVelocitySmoothing = 240f }),
            new("elbow_1000_and_kp", fastElbow with { HandKp = b.HandKp * 4f }),
            new("kp_and_force", b with { HandKp = b.HandKp * 4f, MaxHandForce = b.MaxHandForce * 10f }),
            new("kp_force_core_with_default_muscles", fastCore with { HandKp = b.HandKp * 4f, MaxHandForce = b.MaxHandForce * 10f }),
            new("elbow_120_and_fast_core", fastCore with { ElbowMuscleMaxSpeed = 120f }),
            new("elbow_120_and_servo_speed", b with { ElbowMuscleMaxSpeed = 120f, ArmServoMaxSpeed = 1000f }),
        };

        var output = new List<string>
        {
            "case,hz,range,load,gravity,contactFriction,bodyMass,fist,fistForward,fistTime,shoulderAtFist,chestTranslationAtFist,shoulderPeak,peakPelvisTravel,peakBodyTravel,minHeadY,headAtRelease,elbowShorteningAtFist,forceAtFist",
        };
        foreach (var trial in cases)
        {
            output.Add(RunLimit(trial, 240, 1.30f));
            output.Add(RunLimit(trial, 480, 1.30f));
        }

        foreach (var trial in cases.Where(trial => trial.Name is "baseline" or "elbow_1000" or "elbow_1000_and_servo_speed"
            or "elbow_1000_and_fast_core" or "fast_muscles_kp_force_and_core"))
        {
            output.Add(RunLimit(trial, 240, 1.05f));
        }

        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_extreme_limits.csv"), output);
    }

    private static string RunLimit(LimitCase trial, int hz, float range)
    {
        var samples = new List<ShoulderSample>();
        var knobs = new PunchLab.DriveKnobs(HandDelay: -0.02f);
        MeasureSwing(trial.Name, trial.Tuning, BotStyle.MaxTelegraph, range, null, knobs: knobs, physicsHz: hz,
            shoulderSamples: samples, gravity: trial.Gravity, contactFriction: trial.ContactFriction);
        var p = samples.MaxBy(s => s.Fist);
        return string.Create(CultureInfo.InvariantCulture,
            $"{trial.Name},{hz},{range:G9},{BotStyle.MaxTelegraph:G9},{trial.Gravity:G9},{trial.ContactFriction:G9},{p.BodyMass:G9},{p.Fist:G9},{p.FistForward:G9},{p.Time:G9},{p.Shoulder:G9},{p.ChestTranslation:G9},{samples.Max(s => s.Shoulder):G9},{samples.Max(s => s.PelvisTravel):G9},{samples.Max(s => s.BodyTravel):G9},{samples.Min(s => s.HeadY):G9},{samples[0].HeadY:G9},{p.ElbowShortening:G9},{p.ArmForce:G9}");
    }
}
