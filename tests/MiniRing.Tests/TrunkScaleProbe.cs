using System.Globalization;
using System.Numerics;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>ひねりの目標・駆動・実速度を同じ接触前の時点で測る。通常テストからは除く。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class TrunkScaleProbe
{
    [TestMethod]
    public void DecomposeTrunk()
    {
        var baseline = FighterTuning.Default;
        var cases = new (string Name, FighterTuning Tuning)[]
        {
            ("baseline", baseline),
            ("stiffness", baseline with { UprightKp = 600f }),
            ("damping", baseline with { UprightKd = 10f }),
            ("accel-cap", baseline with { MaxUprightAccel = 1600f }),
            ("range", baseline with { MaxTorsoTwistRadians = 75f * MathF.PI / 180f }),
            ("hip-share", baseline with { HipTwistShare = 0.6f }),
        };
        foreach (var (name, tuning) in cases)
        {
            foreach (var seed in name == "baseline" ? new[] { 1, 2, 3, 4, 5, 6 } : new[] { 1 })
            {
                Measure(name, tuning, seed);
            }
        }
    }

    private static void Measure(string name, FighterTuning tuning, int seed)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), seed, Bots.BotCatalog.Boxer.Telegraph, tuning);
        while (bout.PunchTime is null) { bout.Step(); }
        var f = bout.Game.Player;
        var world = bout.Game.World;
        var bestEnergy = -1f;
        var peakChest = 0f;
        var peakPelvis = 0f;
        var chestPeakAt = 0f;
        var pelvisPeakAt = 0f;
        Console.WriteLine($"CASE,{name},{seed},settled={bout.Settled}");
        Console.WriteLine("ROW,case,seed,t,target,chestYaw,pelvisYaw,chestSpin,pelvisSpin,fist,shoulder,relative,chestForward,pdAccel,transferJ,inertia");
        while (bout.PunchTime is { } t && t < 0.7f)
        {
            var dir = f.Frame.Forward;
            var chest = world.GetState(f.Parts.Chest);
            var pelvis = world.GetState(f.Parts.Pelvis);
            var shoulder = f.ShoulderPosition(Hand.Right);
            var shoulderV = chest.LinearVelocity + Vector3.Cross(chest.AngularVelocity, shoulder - chest.Position);
            var fist = f.GloveVelocity(Hand.Right);
            var command = f.LastCommand;
            var target = Math.Clamp(PunchMechanics.TorsoTwist(command.Left.TargetLocal, command.Right.TargetLocal, f.ArmReach, tuning.MaxTorsoTwistRadians) + command.TwistBias, -tuning.MaxTorsoTwistRadians, tuning.MaxTorsoTwistRadians);
            var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, f.Frame.YawRadians);
            var chestYaw = -FighterMath.RotationError(chest.Orientation, facing).Y;
            var pelvisYaw = -FighterMath.RotationError(pelvis.Orientation, facing).Y;
            var pdAccel = ((target - chestYaw) * tuning.UprightKp) - (chest.AngularVelocity.Y * tuning.UprightKd);
            if (t >= 0f && MathF.Abs(chest.AngularVelocity.Y) > peakChest) { peakChest = MathF.Abs(chest.AngularVelocity.Y); chestPeakAt = t; }
            if (t >= 0f && MathF.Abs(pelvis.AngularVelocity.Y) > peakPelvis) { peakPelvis = MathF.Abs(pelvis.AngularVelocity.Y); pelvisPeakAt = t; }
            var row = string.Create(CultureInfo.InvariantCulture,
                $"{name},{seed},{t:F5},{target:F4},{chestYaw:F4},{pelvisYaw:F4},{chest.AngularVelocity.Y:F4},{pelvis.AngularVelocity.Y:F4},{fist.Length():F4},{Vector3.Dot(shoulderV, dir):F4},{Vector3.Dot(fist - shoulderV, dir):F4},{Vector3.Dot(chest.LinearVelocity, dir):F4},{pdAccel:F4},{f.BodyToFistEnergy:F4},{world.GetLocalInertiaDiagonal(f.Parts.Chest).Y:F4}");
            if (seed == 1) { Console.WriteLine("ROW," + row); }
            bout.Step();
            foreach (var hit in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
            {
                if (hit.Energy > bestEnergy)
                {
                    bestEnergy = hit.Energy;
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"HIT,{row},{hit.Speed:F4},{hit.ImpactMass:F4},{hit.Energy:F4},{hit.Momentum:F4}"));
                }
            }
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"PEAK,{name},{seed},{peakPelvis:F4},{pelvisPeakAt:F5},{peakChest:F4},{chestPeakAt:F5}"));
    }
}
