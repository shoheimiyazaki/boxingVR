using System.Globalization;
using System.Numerics;
using MiniRing.Arena;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("Probe")]
public sealed class FistScaleProbe
{
    [TestMethod]
    public void AttackLab_RecordsTheFistsOwnSpeedAlongsideClosingSpeed()
    {
        var result = new AttackLab(1, 12f, 1, _ => { }).Measure("3", 0.08f, 1.05f);
        Assert.IsGreaterThan(0, result.Hits, "速さを集計する実際の接触が必要");
        Assert.IsGreaterThan(0f, result.FistSpeed, "相対接近速度だけでなく接触前の拳の速さも記録する");
    }

    [TestMethod]
    public void DecomposeCross()
    {
        var baseline = FighterTuning.Default;
        foreach (var (name, tuning) in new (string, FighterTuning)[]
        {
            ("baseline", baseline),
            ("target-speed-cap", baseline with { MaxTargetSpeed = PunchLab.NoCapSpeed }),
            ("unsmoothed-target-speed", baseline with { TargetVelocitySmoothing = 1f / (1f / 240f) }),
            ("no-lead-energy-discarded", baseline with { BodyToFistEfficiency = 0f }),
            ("soft-servo", baseline with { ArmServoHz = baseline.ArmServoHz / 2f }),
            ("legacy-wrist-link-speed", baseline with { ArmServoMaxSpeed = baseline.ElbowMuscleMaxSpeed }),
            ("force", baseline with { MaxHandForce = 500f }),
            ("no-transfer", baseline with { BodyToFistTime = 1e6f }),
            ("fast-muscle", baseline with { ElbowMuscleMaxSpeed = baseline.ElbowMuscleMaxSpeed * 2f, ShoulderMuscleMaxSpeed = baseline.ShoulderMuscleMaxSpeed * 2f }),
        })
        {
            using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), 1, Bots.BotCatalog.Boxer.Telegraph, tuning);
            while (bout.PunchTime is null) { bout.Step(); }
            var f = bout.Game.Player;
            var world = bout.Game.World;
            const float dt = 1f / 240f;
            Console.WriteLine($"CASE {name} armMass={f.ArmMass(Hand.Right):F4}");
            Console.WriteLine("t,fist,shoulder,relative,chest,spin,armMass,bodyMassUpperBound,transferJ,forceN");
            while (bout.PunchTime is { } t && t < 0.35f)
            {
                var dir = f.Frame.Forward;
                var chest = world.GetState(f.Parts.Chest);
                var shoulder = f.ShoulderPosition(Hand.Right);
                var shoulderV = chest.LinearVelocity + Vector3.Cross(chest.AngularVelocity, shoulder - chest.Position);
                var fist = f.GloveVelocity(Hand.Right);
                var mass = f.ImpactMass(Hand.Right, dir, out _);
                if (name == "baseline")
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{t:F5},{Vector3.Dot(fist, dir):F4},{Vector3.Dot(shoulderV, dir):F4},{Vector3.Dot(fist - shoulderV, dir):F4},{Vector3.Dot(chest.LinearVelocity, dir):F4},{chest.AngularVelocity.Y:F4},{f.ArmMass(Hand.Right):F4},{mass - f.ArmMass(Hand.Right):F4},{f.BodyToFistEnergy:F4},{f.HandTrace(Hand.Right).Force:F4}"));
                }
                var speed = fist.Length();
                bout.Step();
                foreach (var hit in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"HIT {name} t={t + dt:F5} fist={speed:F4} shoulder={Vector3.Dot(shoulderV, dir):F4} relative={Vector3.Dot(fist - shoulderV, dir):F4} closing={hit.Speed:F4} mass={hit.ImpactMass:F4} body={hit.ImpactMass * hit.BodyShare:F4} energy={hit.Energy:F4} momentum={hit.Momentum:F4}"));
                }
            }
        }
    }
}
