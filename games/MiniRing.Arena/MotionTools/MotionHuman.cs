using System.Globalization;
using System.Numerics;
using System.Text.Json;
using MiniRing.Motion;

namespace MiniRing.Arena.MotionTools;

/// <summary>人の値の幅（最小・中央・最大と、何本から出したか）。</summary>
internal sealed record HumanRange(double Min, double Median, double Max, int Count)
{
    /// <summary>値が幅の中か: "inside" / "below" / "above"（NaN は "n/a"）。</summary>
    public string Classify(double value) => double.IsNaN(value) ? "n/a" : value < Min ? "below" : value > Max ? "above" : "inside";

    public static HumanRange Of(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var v = values.Where(double.IsFinite).Order().ToArray();
        if (v.Length == 0)
        {
            return new HumanRange(double.NaN, double.NaN, double.NaN, 0);
        }

        var median = v.Length % 2 == 1 ? v[v.Length / 2] : 0.5 * (v[(v.Length / 2) - 1] + v[v.Length / 2]);
        return new HumanRange(v[0], median, v[^1], v.Length);
    }
}

/// <summary>
/// 人の動きの物差しの幅（docs/plans/MOTION_TOOLS_PLAN.md §5 の閾値と §C の自然さ）: data/mocap/kyokushin の蹴り（18 本、200 Hz）を
/// <see cref="MotionTrace"/> に読み替え、ゲームの記録と同じ式（<see cref="MotionWords.Natural"/>・<see cref="MotionMetrics"/>）で
/// 躍度・運動連鎖の順・床に着いた足の滑りの速さ・骨の長さのぶれ（関節の隙間に当たる計測のぶれ）を測る。
/// 1 つの式で測るので、カードの「人の幅の中・外」はゲームと人を同じ物差しで比べている（§1.5）。
/// </summary>
internal static class MotionHuman
{
    public const string DefaultDir = "data/mocap/kyokushin";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public const string DefaultOut = "data/mocap/kyokushin/human_ranges.json";

    /// <summary>計測の点が床に着いているとみなす高さ [m]: その点の区間の最低の高さからこれ以内（印は皮膚の上に 1〜2 cm 浮いて付く）。</summary>
    public const float ContactHeight = 0.02f;

    /// <summary>計測（mocap）の蹴り 1 本を記録の形へ。手の点は無いので肩の点を置く（手の物差しには使わない）。</summary>
    public static MotionTrace FromCapture(KickCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        Vector3 At(string label, int frame)
        {
            for (var d = 0; d < capture.FrameCount; d++)
            {
                // 欠けた点は近い方のフレームの値で埋める（前を先に見る）
                if (frame - d >= 0 && capture.Point(label, frame - d) is { } a)
                {
                    return a;
                }

                if (frame + d < capture.FrameCount && capture.Point(label, frame + d) is { } b)
                {
                    return b;
                }
            }

            return Vector3.Zero;
        }

        var dt = 1f / capture.Rate;
        var mittLabels = capture.LabelNames.Where(l => l.StartsWith("Tarcza", StringComparison.Ordinal)).ToArray();
        var feet = new[] { "LHEE", "LTOE", "RHEE", "RTOE" };
        var floors = feet.ToDictionary(f => f, f => Enumerable.Range(0, capture.FrameCount).Min(i => At(f, i).Y));
        var hip = At("RHJC", 0);
        var legLength = Vector3.Distance(hip, At("RKJC", 0)) + Vector3.Distance(At("RKJC", 0), At("RAJC", 0));
        var trace = new MotionTrace($"mocap {capture.Name}", "human", 0, legLength, 0.7f, frameSeconds: dt);
        for (var i = 0; i < capture.FrameCount; i++)
        {
            var pelvis = (At("LASI", i) + At("RASI", i) + At("LPSI", i) + At("RPSI", i)) / 4f;
            var mitt = mittLabels.Length == 0 ? pelvis + Vector3.UnitZ : mittLabels.Aggregate(Vector3.Zero, (s, l) => s + At(l, i)) / mittLabels.Length;
            var forward = mitt - pelvis;
            forward.Y = 0f;
            forward = forward.LengthSquared() < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(forward);
            var shoulderL = At("LSJC", i);
            var shoulderR = At("RSJC", i);
            Vector3[] front = [At("LHJC", i), At("LKJC", i), At("LAJC", i), At("LHEE", i), At("LTOE", i), At("RHJC", i), At("RKJC", i), At("RAJC", i), At("RHEE", i), At("RTOE", i)];
            Vector3[] points =
            [
                At("C7", i) + (0.2f * Vector3.UnitY), At("CLAV", i), pelvis, pelvis,
                shoulderL, shoulderL, shoulderL, shoulderR, shoulderR, shoulderR,
                .. front,
                At("RTOE", i), At("LTOE", i),
                front[1], front[2], front[3], front[4], front[6], front[7], front[8], front[9],
            ];
            var contacts = new bool[MotionTrace.ContactNames.Length];
            for (var k = 0; k < feet.Length; k++)
            {
                contacts[k] = At(feet[k], i).Y <= floors[feet[k]] + ContactHeight;
            }

            trace.Frames.Add(new MotionSample(i * dt, points, shoulderL, shoulderR, contacts, 0f, "", float.NaN, 1f, false, null, 0, forward, mitt, 0f));
        }

        return trace;
    }

    /// <summary>計測の点の床に沿った速さ [m/s]（床に着いている間だけ）。滑りの閾値の人の値。</summary>
    public static IEnumerable<float> PlantedSpeeds(MotionTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        for (var k = 0; k < 4; k++)
        {
            var v = MotionMetrics.Velocities(trace.Series(MotionTrace.ContactNames[k]), trace.Dt);
            for (var i = 0; i < trace.Frames.Count; i++)
            {
                if (trace.Frames[i].Contacts[k])
                {
                    yield return new Vector2(v[i].X, v[i].Z).Length();
                }
            }
        }
    }

    /// <summary>値の列の分位（0..1、線形補間）。</summary>
    public static double Quantile(IReadOnlyList<double> values, double q)
    {
        ArgumentNullException.ThrowIfNull(values);
        var v = values.Order().ToArray();
        if (v.Length == 0)
        {
            return double.NaN;
        }

        var x = Math.Clamp(q, 0d, 1d) * (v.Length - 1);
        var lo = (int)Math.Floor(x);
        var hi = Math.Min(v.Length - 1, lo + 1);
        return v[lo] + ((v[hi] - v[lo]) * (x - lo));
    }

    /// <summary>motionhuman: 蹴りの計測から人の幅を出して JSON に書く（--dir、--out）。--export &lt;名前&gt; なら 1 本を .motion.json にもする（並べ比べの手本）。</summary>
    public static int Run(Options o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var dir = o.Get("dir") ?? DefaultDir;
        var files = Directory.GetFiles(dir, "kick_*.csv").Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            throw new FileNotFoundException($"{dir} に kick_*.csv がありません。");
        }

        var jerk = new List<double>();
        var chain = new List<double>();
        var planted = new List<double>();
        var bone = new List<double>();
        var c = CultureInfo.InvariantCulture;
        foreach (var file in files)
        {
            var trace = FromCapture(KickCapture.Load(file));
            var n = MotionWords.Natural(trace, trace.Frames[0].Time, trace.Frames[^1].Time, "toeR");
            jerk.Add(Math.Log10(n.Jerk));
            chain.Add(n.ChainOrder);
            planted.AddRange(PlantedSpeeds(trace).Select(s => (double)s));
            var thigh = trace.Frames.Select(f => Vector3.Distance(f.Points[15], f.Points[16])).ToArray();
            var shin = trace.Frames.Select(f => Vector3.Distance(f.Points[16], f.Points[17])).ToArray();
            bone.Add(Math.Max(thigh.Max() - thigh.Min(), shin.Max() - shin.Min()));
            Console.WriteLine(string.Create(c, $"{Path.GetFileName(file),-24} jerk log10 {Math.Log10(n.Jerk):0.00} chain {n.ChainOrder:0.00} peak {n.PeakSpeed:0.0} m/s bone spread {100 * bone[^1]:0.0} cm"));
            if (o.Get("export") is { } name && Path.GetFileNameWithoutExtension(file).Contains(name, StringComparison.Ordinal))
            {
                var path = o.Get("trace") ?? Path.Combine("media_tmp", "motion", $"mocap_{Path.GetFileNameWithoutExtension(file)}.motion.json");
                trace.Save(path, [], MotionWords.Captions(trace), NaturalFields(n, trace.Frames[0].Time, trace.Frames[^1].Time, $"motionhuman --export {name}"));
                Console.WriteLine(path);
            }
        }

        var ranges = new Dictionary<string, object>
        {
            ["source"] = $"{dir} ({files.Length} kicks, Kyokushin, CC0; same formulas as motioncard)",
            ["jerkLog10"] = HumanRange.Of(jerk),
            ["chainOrder"] = HumanRange.Of(chain),
            ["plantedSpeedP95"] = Quantile(planted, 0.95),
            ["plantedSpeedP99"] = Quantile(planted, 0.99),
            ["boneSpreadMax"] = bone.Max(),
        };
        var output = o.Get("out") ?? DefaultOut;
        File.WriteAllText(output, JsonSerializer.Serialize(ranges, Indented) + "\n");
        Console.WriteLine(string.Create(c, $"jerk log10 {HumanRange.Of(jerk)}, chain {HumanRange.Of(chain)}, planted speed p95 {Quantile(planted, 0.95):0.00} p99 {Quantile(planted, 0.99):0.00} m/s, bone spread max {100 * bone.Max():0.0} cm"));
        Console.WriteLine(output);
        return 0;
    }

    /// <summary>カード・並べ比べが読む、自然さの数（文字列で JSON の見出しに置く）。</summary>
    public static Dictionary<string, string> NaturalFields(Naturalness n, float from, float to, string command)
    {
        ArgumentNullException.ThrowIfNull(n);
        var c = CultureInfo.InvariantCulture;
        return new Dictionary<string, string>
        {
            ["windowFrom"] = from.ToString("0.000", c),
            ["windowTo"] = to.ToString("0.000", c),
            ["natural"] = n.Format(),
            ["effector"] = n.Effector,
            ["peakSpeed"] = n.PeakSpeed.ToString("0.000", c),
            ["peakTime"] = n.PeakTime.ToString("0.000", c),
            ["jerkLog10"] = Math.Log10(n.Jerk).ToString("0.000", c),
            ["chainOrder"] = n.ChainOrder.ToString("0.000", c),
            ["headShake"] = n.HeadShake.ToString("0.0", c),
            ["twistRange"] = n.TwistRange.ToString("0.0", c),
            ["planeRms"] = n.PathPlaneRms.ToString("0.0000", c),
            ["dip"] = n.PathDip.ToString("0.0000", c),
            ["command"] = command,
        };
    }
}
