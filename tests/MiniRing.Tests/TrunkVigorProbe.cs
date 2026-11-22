using System.Globalization;
using MiniRing.Arena;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("Probe")]
public sealed class TrunkVigorProbe
{
    [TestMethod]
    public void AtThePunch()
    {
        foreach (var torque in new float?[] { null, 60f })
        {
            var tuning = torque is { } t ? FighterTuning.Default with { PelvisRotatorTorque = t } : FighterTuning.Default;
            using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), 1, 0.06f, tuning);
            var steps = 0;
            while (bout.PunchTime is null)
            {
                bout.Step();
                steps++;
            }

            var f = bout.Game.Player;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"pelvis torque {(torque is { } tq ? tq.ToString(CultureInfo.InvariantCulture) : "default")}: at punch start after {steps} steps: trunk O2 {f.OxygenOf(MuscleRegion.Trunk):F2} vigor {f.VigorOf(MuscleRegion.Trunk):F2} (power {f.RegionPower(MuscleRegion.Trunk):F0} W) arms {f.OxygenOf(MuscleRegion.RightArm):F2} winded {f.Winded:F2} endurance {f.Endurance:F2}"));
        }
    }
}
