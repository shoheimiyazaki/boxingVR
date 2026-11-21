using System.Globalization;
using System.Numerics;
using Aigis.Physics;
using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.LowPoly;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>床と自分の体だけの世界で、空中の目印へ標準の右ストレートを打つ。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed partial class SoloPunchSpeedProbe
{
    private const string SwingHeader = "setting,load,range,t,speed,forward,pelvisYawRate,chestYawRate,pelvisYaw,chestYaw,abdomenYaw,abdomenYawRate,footEnergy,shoulderForward,pelvisForward,rightWaistRelative,leftWaistRelative,leftGloveLocalZ,priorDesired,priorCap,priorForce,priorReachClamp,priorElbowBendDeg,priorElbowShortening,priorHillElbow";
    private const float Dt = 1f / 240f;
    private const float SettleSeconds = 2.518f;

    [TestMethod]
    public void Measure()
    {
        var baseline = FighterTuning.Default with { HipTwistShare = 0.45f, ChestTwistResponseSeconds = 0.025f };
        var current = FighterTuning.Default;
        var rows = new List<string> { SwingHeader };
        foreach (var load in new[] { 0.1343f, BotStyle.MaxTelegraph })
        {
            foreach (var distance in new[] { 1.05f, 1.3f })
            {
                MeasureSwing("baseline", baseline, load, distance, rows, knobs: new PunchLab.DriveKnobs());
                var videoPath = load == BotStyle.MaxTelegraph && distance == 1.3f
                    ? Environment.GetEnvironmentVariable("SOLO_PUNCH_VIDEO") : null;
                MeasureSwing("current", current, load, distance, rows, videoPath);
            }
        }

        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_voxel_v72.csv"), rows);
    }

    [TestMethod]
    public void MeasureSplitTorso()
    {
        var tuning = FighterTuning.Default with { SplitTorso = true };
        var rows = new List<string> { SwingHeader };
        MeasureSwing("splitWave", tuning, BotStyle.MaxTelegraph, 1.3f, rows,
            Environment.GetEnvironmentVariable("SOLO_PUNCH_VIDEO"), new PunchLab.DriveKnobs(HandDelay: -0.02f));
        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_split_wave.csv"), rows);
    }

    [TestMethod]
    public void MeasureCompactTorso()
    {
        var rows = new List<string> { SwingHeader };
        var tuning = FighterTuning.Default with { SplitTorso = true };
        var knobs = new PunchLab.DriveKnobs(HandDelay: -0.02f);
        MeasureSwing("wave", tuning, BotStyle.MaxTelegraph, 1.3f, rows, knobs: knobs);
        MeasureSwing("compactNoPull", tuning with { CompactTorsoWave = true, CompactLeftRetractShare = 0f }, BotStyle.MaxTelegraph, 1.3f, rows, knobs: knobs);
        var compact = MeasureSwing("compact", tuning with { CompactTorsoWave = true }, BotStyle.MaxTelegraph, 1.3f, rows,
            Environment.GetEnvironmentVariable("SOLO_PUNCH_VIDEO"), knobs);
        Assert.IsGreaterThan(0f, compact.FootEnergy, "接地した脚のばねから腰へ仕事を渡す");
        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_compact_shoulder_follow.csv"), rows);
    }

    private static (float Fist, float Shoulder, float HipSweep, float FootEnergy) MeasureSwing(string setting, FighterTuning tuning, float load, float distance, List<string>? rows, string? videoPath = null, PunchLab.DriveKnobs? knobs = null, int physicsHz = 240, float radialDelay = 0f, List<ShoulderSample>? shoulderSamples = null, float gravity = 9.81f,
        float contactFriction = 1f)
    {
        var dt = 1f / physicsHz;
        using var world = new PhysicsWorld(new Vector3(0f, -gravity, 0f), contactFriction);
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        var fighter = Fighter.Spawn(world, tuning, "solo", Vector3.Zero, 0f);
        var model = BoxerModel.Build(tuning.HalfExtents, tuning.ModelSubdivisions);
        fighter.Voxels = new BoxerBody(fighter, world, tuning, model, Outfit.RedCorner, BoxerVoxelOverrides.Shipped);
        fighter.RefreshVoxelMass();
        var gloves = new List<Weapon>();
        foreach (var hand in new[] { Hand.Left, Hand.Right })
        {
            var kind = HandWear.Glove.Kind();
            var mirrored = Weapon.MirroredOn(kind, hand);
            var glove = Weapon.CreateWorn(world, kind, BoxerAnatomy.RedGlove, fighter.GloveCenter(hand), Quaternion.Identity, mirrored);
            fighter.Wield(hand, glove.Body.BodyId, Weapon.GripOf(kind, mirrored), glove: true);
            fighter.SetStrikeReach(hand, glove.HandState is null ? null : glove.StrikeOffset);
            gloves.Add(glove);
        }

        var delaySteps = (int)MathF.Round(radialDelay / dt);
        if (delaySteps > 0)
        {
            var radialHistory = new Queue<float>(delaySteps + 1);
            fighter.ProbeRightHandTarget = target =>
            {
                var shoulder = fighter.Frame.WorldToLocal(fighter.ShoulderPosition(Hand.Right));
                var offset = target - shoulder;
                var length = offset.Length();
                radialHistory.Enqueue(length);
                var radius = radialHistory.Count > delaySteps ? radialHistory.Dequeue() : radialHistory.Peek();
                return length > 1e-5f ? shoulder + offset * (radius / length) : target;
            };
        }

        var forward = fighter.Frame.Forward;
        var aim = fighter.HeadPosition + (forward * (fighter.ArmReach * distance));
        FighterSense Sense() => new(fighter.Frame, fighter.ArmReach, aim, aim, false, SelfHead: fighter.HeadPosition);
        var punch = new PunchLab.Puncher(PunchLabPath.Standard, load, AimGoal.Chin, knobs: knobs);
        void Step()
        {
            fighter.ApplyControl(punch.Command(Sense(), forward * punch.StepIn, dt), dt);
            world.Step(dt);
        }

        for (var i = 0; i < (int)MathF.Round(SettleSeconds / dt); i++)
        {
            Step();
        }

        if (tuning.CompactTorsoWave)
        {
            var gloveMass = gloves.Sum(glove => world.GetMass(glove.Body.BodyId));
            var standingHeight = fighter.HeadPosition.Y + tuning.HalfExtents.Head.Y;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"gravity={-world.Gravity.Y:F3} m/s² body={fighter.TotalMass:F3} kg gloves={gloveMass:F3} kg guardHeight={standingHeight:F3} m armReach={fighter.ArmReach:F3} m"));
        }

        aim = fighter.HeadPosition + (forward * (fighter.ArmReach * distance));
        punch.Start(Sense(), fighter.ShoulderPosition(Hand.Right));
        using var video = videoPath is null ? null : new SwingVideo(videoPath, fighter, gloves);
        var pelvisStart = fighter.PelvisPosition;
        var releasePelvis = Vector3.Zero;
        var releaseCenter = Vector3.Zero;
        var peak = 0f;
        var shoulderPeak = 0f;
        var hipAtRelease = float.NaN;
        var hipSweep = 0f;
        var peakForward = 0f;
        var peakTime = 0f;
        var outbound = punch.Action.Duration * PunchSpec.OutboundShare;
        while (punch.Elapsed < outbound)
        {
            var velocity = fighter.GloveVelocity(Hand.Right);
            var speed = velocity.Length();
            var projection = Vector3.Dot(velocity, forward);
            var pelvisRate = world.GetState(fighter.Parts.Pelvis).AngularVelocity.Y;
            var chestRate = world.GetState(fighter.Parts.Chest).AngularVelocity.Y;
            float Yaw(int part) => BodyFrame.DeltaAngle(0f, BodyFrame.YawOf(
                Vector3.Transform(Vector3.UnitZ, world.GetState(part).Orientation), 0f)) * 180f / MathF.PI;
            var pelvisYaw = Yaw(fighter.Parts.Pelvis);
            var chestYaw = Yaw(fighter.Parts.Chest);
            var abdomenYaw = fighter.Parts.Abdomen is { } abdomen ? Yaw(abdomen) : float.NaN;
            var abdomenRate = fighter.Parts.Abdomen is { } abdomenBody ? world.GetState(abdomenBody).AngularVelocity.Y : float.NaN;
            var chest = world.GetState(fighter.Parts.Chest);
            var shoulder = fighter.ShoulderPosition(Hand.Right);
            var shoulderVelocity = chest.LinearVelocity + Vector3.Cross(chest.AngularVelocity, shoulder - chest.Position);
            var shoulderForward = Vector3.Dot(shoulderVelocity, forward);
            var pelvis = world.GetState(fighter.Parts.Pelvis);
            var rightWaist = Vector3.Transform(-Vector3.UnitX * tuning.HalfExtents.Pelvis.X, pelvis.Orientation);
            var waistAdvance = Vector3.Dot(rightWaist, forward);
            var trace = fighter.HandTrace(Hand.Right);
            if (shoulderSamples is not null && punch.Elapsed >= 0f)
            {
                var center = fighter.CenterOfMass;
                if (shoulderSamples.Count == 0)
                {
                    releasePelvis = pelvis.Position;
                    releaseCenter = center;
                }

                shoulderSamples.Add(new ShoulderSample(punch.Elapsed, speed, shoulderForward,
                    Vector3.Dot(chest.LinearVelocity, forward), Vector3.Dot(pelvis.Position - releasePelvis, forward),
                    Vector3.Dot(center - releaseCenter, forward), Vector3.Dot(pelvis.LinearVelocity, forward),
                    projection, fighter.HeadPosition.Y, fighter.TotalMass, trace.ElbowShortening, trace.Force));
            }

            var elbowHill = ArmMuscle.ForceVelocity(trace.ElbowShortening, tuning.HandMuscleCurvature, tuning.HandMuscleEccentricMax, tuning.HandMuscleEccentricSlope);
            rows?.Add(string.Create(CultureInfo.InvariantCulture,
                $"{setting},{load:G9},{distance:G9},{punch.Elapsed:G9},{speed:G9},{projection:G9},{pelvisRate:G9},{chestRate:G9},{pelvisYaw:G9},{chestYaw:G9},{abdomenYaw:G9},{abdomenRate:G9},{fighter.StepEnergyReleased:G9},{shoulderForward:G9},{Vector3.Dot(fighter.PelvisPosition - pelvisStart, forward):G9},{waistAdvance:G9},{-waistAdvance:G9},{fighter.Frame.WorldToLocal(fighter.GloveCenter(Hand.Left)).Z:G9},{trace.Desired:G9},{trace.Cap:G9},{trace.Force:G9},{Vector3.Distance(trace.RawTarget, trace.Target):G9},{trace.ElbowBend * 180f / MathF.PI:G9},{trace.ElbowShortening:G9},{elbowHill:G9}"));
            if (punch.Elapsed >= 0f)
            {
                if (speed > peak)
                {
                    peak = speed;
                    peakTime = punch.Elapsed;
                }

                peakForward = MathF.Max(peakForward, projection);
                shoulderPeak = MathF.Max(shoulderPeak, shoulderForward);
                hipAtRelease = float.IsNaN(hipAtRelease) ? pelvisYaw : hipAtRelease;
                hipSweep = MathF.Max(hipSweep, pelvisYaw - hipAtRelease);
            }

            video?.Write(world, aim, punch.Elapsed, speed, peak, pelvisYaw, chestYaw);
            Step();
        }

        Assert.IsTrue(float.IsFinite(peak) && peak > 0f);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{setting} load={load:F4}s range={distance:F2} peak={peak:F3}m/s at {peakTime:F4}s forwardPeak={peakForward:F3}m/s"));
        return (peak, shoulderPeak, hipSweep, fighter.StepEnergyReleased);
    }
}
