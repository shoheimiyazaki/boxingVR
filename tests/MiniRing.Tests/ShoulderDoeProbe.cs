using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>肩の速度を胸の並進、ヨー、残りの回転に分ける。肩甲帯のモデルは CSV に対して別に回す。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ShoulderDoeProbe
{
    private sealed record Trial(string Group, int Seed, double[] Values);

    private static readonly double[] Torques = [45d, 70d, 120d];
    private static readonly double[] FeedForwardLevels = [0d, 1d];
    private static readonly bool[] RotationLocks = [false, true];

    [TestMethod]
    public void Measure()
    {
        var baseline = DriveDoeProbe.Factors.Select(f => f.Base).ToArray();
        var grid =
            from torque in Torques
            from rom in new[] { 25d, baseline[12], 50d }
            from hip in new[] { 0.25d, baseline[13], 0.65d }
            from feedForward in FeedForwardLevels
            from seed in Enumerable.Range(1, 3)
            select new Trial("grid", seed, WithFactors(baseline, torque, rom, hip, feedForward));
        var lhs = DriveDoeProbe.LatinHypercube(60, 20261122).Select((point, index) =>
        {
            var values = WithFactors(baseline, point[23], point[12], point[13], point[24]);
            values[18] = point[18];
            values[19] = point[19];
            values[16] = point[16];
            return new Trial("lhs", 1 + index % 3, values);
        });
        var holdout =
            from seed in Enumerable.Range(4, 3)
            from distance in new[] { baseline[18], 1.3d }
            select new Trial("holdout", seed, baseline.Select((value, index) => index == 18 ? distance : value).ToArray());

        var jobs = grid.Concat(lhs).Concat(holdout)
            .SelectMany((trial, index) => RotationLocks.Select(locked => (trial, index, locked))).ToArray();
        var rows = new string[jobs.Length];
        Parallel.For(0, jobs.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            var (trial, index, locked) = jobs[i];
            rows[i] = MeasureTrial(trial, index, locked);
        });

        const string header = "id,group,seed,yawLocked,torque,rom,hip,ff,range,load,step,t,contact,hitTime,closing,mass,energy,armLength,gripProjection,reachDemand,fistSpeed,fistForward,shoulder,chest,yaw,tilt,lever,depth,chestRate,pelvisRate,chestAngle,pelvisAngle,trunkModelPeak,trunkModelShoulder";
        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "shoulder_series.csv"), rows.Prepend(header));
    }

    private static double[] WithFactors(double[] baseline, double torque, double rom, double hip, double ff)
        => baseline.Select((value, i) => i switch { 12 => rom, 13 => hip, 23 => torque, 24 => ff, _ => value }).ToArray();

    private static string MeasureTrial(Trial trial, int id, bool locked)
    {
        var r = DriveDoeProbe.RunOne(trial.Values, false, trial.Seed, locked);
        var end = r.Hit ? r.Series.FindLastIndex(s => s.T < r.THit) : r.Series.Count - 1;
        var parameters = DriveDoeProbe.TrunkBase(r.Snap.Tuning, r.ChestYawInertia) with { InertiaRatio = 4f };
        var model = DriveDoeProbe.TrunkModel(r, end, parameters);
        return string.Join('\n', r.Series.Select((s, i) =>
        {
            float Forward(Vector3 value) => Vector3.Dot(value, s.Dir);
            var yawArm = Vector3.Cross(Vector3.UnitY, s.ChestToShoulder);
            var tilt = Vector3.Cross(s.ChestOmega with { Y = 0f }, s.ChestToShoulder);
            var length = r.Upper + r.Lower;
            var demand = Vector3.Dot(s.Target - s.Shoulder, s.FrontOnChest) / length;
            var values = new double[]
            {
                trial.Seed, locked ? 1 : 0, trial.Values[23], trial.Values[12], trial.Values[13], trial.Values[24],
                trial.Values[18], trial.Values[19], trial.Values[16], s.T, r.Hit && i == end ? 1 : 0, r.THit,
                r.F.GetValueOrDefault("closing", double.NaN), r.F.GetValueOrDefault("mass", double.NaN),
                r.F.GetValueOrDefault("energy", double.NaN), length, Forward(s.FrontOnChest), demand, s.Fist,
                Forward(s.GloveV), Forward(s.ShoulderV), Forward(s.ChestV), Forward(yawArm) * s.ChestRate,
                Forward(tilt), Forward(yawArm), Vector3.Dot(s.ChestToShoulder, s.FrontOnChest), s.ChestRate,
                s.PelvisRate, s.ChestYaw, s.PelvisYaw, model.PeakRate, model.PeakShoulderFwd,
            };
            return $"{id},{trial.Group}," + string.Join(',', values.Select(v => v.ToString("G9", CultureInfo.InvariantCulture)));
        }));
    }
}
