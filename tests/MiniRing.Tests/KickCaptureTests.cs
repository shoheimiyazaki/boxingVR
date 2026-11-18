using System.Globalization;
using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;

namespace MiniRing.Tests;

/// <summary>
/// 人の回し蹴りのモーションキャプチャ（極真空手、CC0。data/mocap/kyokushin）の読み込みと、膝の制御の言葉への読み替え、
/// 人の動きから作った回し蹴りの手本（Bots/kicks/*.json）が人形で相手に当たること。
/// </summary>
[TestClass]
public sealed class KickCaptureTests
{
    private static string DataDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "data", "mocap", "kyokushin")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            return dir is null ? throw new DirectoryNotFoundException("data/mocap/kyokushin") : Path.Combine(dir, "data", "mocap", "kyokushin");
        }
    }

    [TestMethod]
    public void TheEighteenKicks_AreRead_At200Hz_WithTheirLabels()
    {
        var files = Directory.GetFiles(DataDir, "kick_*.csv");
        Assert.HasCount(18, files); // 廻し蹴り下段 6・上段 6、前蹴り（中段）6
        foreach (var file in files)
        {
            var capture = KickCapture.Load(file);
            Assert.AreEqual(200f, capture.Rate);
            Assert.IsGreaterThan(100, capture.FrameCount, "a kick segment is more than half a second");
            Assert.IsGreaterThan(0, capture.PeakFrame);
            foreach (var label in new[] { "RHJC", "RKJC", "RAJC", "LHJC", "RTOE", "Tarcza1" })
            {
                Assert.Contains(label, capture.LabelNames);
                Assert.IsNotNull(capture.Point(label, capture.PeakFrame), $"{Path.GetFileName(file)} {label} at the peak");
            }

            var track = capture.Analyze();
            Assert.IsLessThan(0.3f, track.AnkleToMitt, "the ankle comes within 30 cm of the mitt");
            Assert.IsTrue(track.Samples.All(s => float.IsFinite(s.Twist) && float.IsFinite(s.Bend) && float.IsFinite(s.Azimuth)));
        }
    }

    /// <summary>1 フレームだけの合成 CSV（データの座標: mm、Z 上、前は −y）。ミットは前 1 m。</summary>
    private static KickCapture Synthetic(Vector3 knee, Vector3 ankle)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kick_synth_{Guid.NewGuid():N}.csv");
        var points = new Dictionary<string, Vector3>
        {
            ["RHJC"] = new(-100, 0, 900), ["LHJC"] = new(100, 0, 900), ["RKJC"] = knee, ["RAJC"] = ankle, ["LKJC"] = new(100, 0, 500), ["LAJC"] = new(100, 0, 100),
            ["RTOE"] = ankle, ["RHEE"] = ankle, ["LTOE"] = new(100, 0, 100), ["LHEE"] = new(100, 0, 100),
            ["LASI"] = new(100, 0, 900), ["RASI"] = new(-100, 0, 900), ["LPSI"] = new(100, 100, 900), ["RPSI"] = new(-100, 100, 900),
            ["LSJC"] = new(150, 0, 1400), ["RSJC"] = new(-150, 0, 1400), ["CLAV"] = new(0, 0, 1400), ["C7"] = new(0, 100, 1450),
        };
        for (var i = 1; i <= 6; i++)
        {
            points[$"Tarcza{i}"] = new Vector3(0, -1000, 1000);
        }

        var labels = new[] { "RHJC", "RKJC", "RAJC", "LHJC", "LKJC", "LAJC", "RTOE", "RHEE", "LTOE", "LHEE", "LASI", "RASI", "LPSI", "RPSI", "LSJC", "RSJC", "CLAV", "C7", "Tarcza1", "Tarcza2", "Tarcza3", "Tarcza4", "Tarcza5", "Tarcza6" };
        var header = "frame," + string.Join(',', labels.SelectMany(l => new[] { l + "_x", l + "_y", l + "_z" }));
        string Row(int f) => f + "," + string.Join(',', labels.SelectMany(l => new[] { points[l].X, points[l].Y, points[l].Z }.Select(v => v.ToString("0", CultureInfo.InvariantCulture))));
        File.WriteAllLines(path, ["# source=synthetic rate=200 peak_frame=1 peak_speed=0.0 first_frame=0", header, Row(0), Row(1), Row(2)]);
        try
        {
            return KickCapture.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void KneeBendAndTwist_AreReadCorrectly_ForKnownShapes()
    {
        // 立った形: 腿もすねも真下。曲げ ≈ 0、仰角 −90°。
        var standing = Synthetic(new Vector3(-100, 0, 500), new Vector3(-100, 0, 100)).Analyze().Samples[1];
        Assert.IsLessThan(0.02f, standing.Bend);
        Assert.AreEqual(-MathF.PI / 2f, standing.Elevation, 0.02f);
        Assert.AreEqual(0.4f, standing.ThighLength, 1e-3f);

        // 膝を直角に曲げ、足が体の後ろ（ゲームの −Z = データの +y）へ: 曲げ 90°、ねじり 0（定義: 曲げると足が後ろへ行く向きが 0）。
        var behind = Synthetic(new Vector3(-100, 0, 500), new Vector3(-100, 400, 500)).Analyze().Samples[1];
        Assert.AreEqual(MathF.PI / 2f, behind.Bend, 0.02f);
        Assert.AreEqual(0f, behind.Twist, 0.12f); // ミットは腰の真正面でなく 100 mm ずれているので、前の向きが 5.7° 傾く

        // 足が体の前（ミットの方）へ曲がる: 曲げ 90°、ねじり ±180°。
        var ahead = Synthetic(new Vector3(-100, 0, 500), new Vector3(-100, -400, 500)).Analyze().Samples[1];
        Assert.AreEqual(MathF.PI / 2f, ahead.Bend, 0.02f);
        Assert.AreEqual(MathF.PI, MathF.Abs(ahead.Twist), 0.12f);

        // 足が脚の外（右脚の右 = データの −x）へ曲がる: ねじり ±90°。
        var outside = Synthetic(new Vector3(-100, 0, 500), new Vector3(-500, 0, 500)).Analyze().Samples[1];
        Assert.AreEqual(MathF.PI / 2f, MathF.Abs(outside.Twist), 0.12f);
    }

    [TestMethod]
    public void TheHumanShinDirection_IsRebuiltFromAxisTwistAndBend_ThroughTheGameDefinition()
    {
        // ゲームの膝の制御の定義（腿の軸・ねじり・曲げ）から、人のすねの向きがそのまま作り直せる（どちらの定義でも同じ幾何）。
        foreach (var file in Directory.GetFiles(DataDir, "kick_S0*_01.csv"))
        {
            var track = KickCapture.Load(file).Analyze();
            foreach (var s in track.Samples.Where((_, i) => i % 10 == 0))
            {
                var forward = new Vector3(track.Mitt.X - s.Hip.X, 0f, track.Mitt.Z - s.Hip.Z);
                forward = Vector3.Normalize(forward);
                var axis = Vector3.Normalize(s.Knee - s.Hip);
                var normal = Vector3.Transform(LegKnee.BendNormalRef(axis, forward), Quaternion.CreateFromAxisAngle(axis, s.Twist));
                var rebuilt = (MathF.Cos(s.Bend) * axis) + (MathF.Sin(s.Bend) * normal);
                var actual = Vector3.Normalize(s.Ankle - s.Knee);
                Assert.IsGreaterThan(0.999f, Vector3.Dot(rebuilt, actual), $"{Path.GetFileName(file)} t {s.Time:F2}");
            }
        }
    }

    [TestMethod]
    public void TheModels_AreAveragedAtTheImpact_AndTheKneeOpensThere()
    {
        foreach (var name in new[] { "roundhouse_low", "roundhouse_high" })
        {
            var model = CapturedKick.Load(name);
            Assert.AreEqual(6, model.Count);
            Assert.IsInRange(0.4f, 0.85f, model.ImpactShare);
            // 当たる瞬間の曲げは小さく（膝がほぼ伸びている）、その前の抱えでは大きく曲がっている。
            Assert.IsLessThan(0.5f, model.BendAt(model.ImpactShare), name);
            Assert.IsGreaterThan(1.2f, Enumerable.Range(0, 100).Max(i => model.BendAt(model.ImpactShare * i / 100f)), name);
        }
    }
}
