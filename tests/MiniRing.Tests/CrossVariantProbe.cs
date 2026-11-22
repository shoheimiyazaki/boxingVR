using System.Globalization;
using MiniRing.Arena;

namespace MiniRing.Tests;

[TestClass]
[TestCategory("Probe")]
public sealed class CrossVariantProbe
{
    private static float Cross(FighterTuning tuning, float telegraph)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", 1.05f, false), 1, telegraph, tuning);
        while (bout.PunchTime is null)
        {
            bout.Step();
        }

        var f = bout.Game.Player;
        while (bout.PunchTime is { } t && t < 0.45f)
        {
            bout.Step();
            foreach (var hit in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
            {
                return hit.Energy;
            }
        }

        return 0f;
    }

    [TestMethod]
    public void Variants()
    {
        var d = FighterTuning.Default;
        var variants = new (string, FighterTuning)[]
        {
            ("default", d),
            ("no fatigue", d with { StaminaFatigue = false }),
            ("trunk share 1", d with { TrunkMuscleShare = 1f }),
            ("vessel 75 store 1500", d with { VesselPowerPerKg = 75.53f, OxygenStorePerKg = 1500f }),
            ("no breath/chin", d with { BreathTorque = 1e-6f, ChinReflexRadians = 1e-6f }),
            ("window 0.001", d with { MuscleEffortWindow = 0.001f }),
        };
        foreach (var (name, tuning) in variants)
        {
            var weak = tuning with { PelvisRotatorTorque = 60f };
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"CROSS {name,-24} quick {Cross(tuning, 0.06f):F1}  loaded {Cross(tuning, 0.25f):F1}  weak-hips quick {Cross(weak, 0.06f):F1}  loaded {Cross(weak, 0.25f):F1}"));
        }
    }
}
