using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

public sealed partial class SoloPunchSpeedProbe
{
    [TestMethod]
    public void MeasureSerialExtension()
    {
        var baseline = FighterTuning.Default with { SplitTorso = true, CompactTorsoWave = true };
        var knobs = new PunchLab.DriveKnobs(HandDelay: -0.02f);
        var rows = new List<string> { SwingHeader };

        // 240 Hz で半径の遅れ（前へ伸ばす成分）と筋の最大短縮速度を独立に比較。
        var delays = new[] { 0f, 1f / 60f, 1f / 30f, 0.05f };
        var speeds = new[] { baseline.ElbowMuscleMaxSpeed, 50f, 60f };
        foreach (var speed in speeds)
        {
            foreach (var delay in delays)
            {
                var name = FormattableString.Invariant($"grid_e{speed:F1}_r{1000f * delay:F0}");
                MeasureSwing(name, baseline with { ElbowMuscleMaxSpeed = speed }, BotStyle.MaxTelegraph,
                    1.3f, rows, knobs: knobs, radialDelay: delay);
            }
        }

        // 条件の違いと刻み幅への敏感さを、同じ組み合わせで検証。
        var cases = new (string Name, float Speed, float Delay)[]
        {
            ("base", baseline.ElbowMuscleMaxSpeed, 0f),
            ("radius", baseline.ElbowMuscleMaxSpeed, 1f / 30f),
            ("elbow", 60f, 0f),
            ("both", 60f, 1f / 30f),
        };
        foreach (var (name, speed, delay) in cases)
        {
            var tuning = baseline with { ElbowMuscleMaxSpeed = speed };
            MeasureSwing("near_" + name, tuning, BotStyle.MaxTelegraph, 1.05f, rows, knobs: knobs, radialDelay: delay);
            MeasureSwing("short_" + name, tuning, 0.1343f, 1.3f, rows, knobs: knobs, radialDelay: delay);
            foreach (var hz in new[] { 120, 480 })
            {
                MeasureSwing(FormattableString.Invariant($"hz{hz}_{name}"), tuning, BotStyle.MaxTelegraph, 1.3f, rows,
                    knobs: knobs, physicsHz: hz, radialDelay: delay);
            }
        }

        File.WriteAllLines(Path.Combine(DriveDoeProbe.OutDir, "solo_punch_serial_extension.csv"), rows);
    }
}
