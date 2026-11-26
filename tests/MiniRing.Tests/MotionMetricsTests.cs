using System.Numerics;
using MiniRing.Arena.MotionTools;

namespace MiniRing.Tests;

/// <summary>
/// 動きの物差し（docs/plans/MOTION_TOOLS_PLAN.md）の式を、答えの分かっている軌跡で確かめる: 最小躍度の動きの無次元の躍度は 720、
/// 近い所から順に速さの山が来る連鎖は 1、逆なら 0、支えの凸包の内外、速い振動だけが震えに数えられる、関節の隙間・引っかかりが見つかる。
/// </summary>
[TestClass]
public sealed class MotionMetricsTests
{
    private const float Dt = 1f / 240f;

    private static Vector3[] MinimumJerk(float length, float seconds, float dt = Dt)
    {
        var n = (int)MathF.Round(seconds / dt) + 1;
        return [.. Enumerable.Range(0, n).Select(i =>
        {
            var tau = i / (float)(n - 1);
            return new Vector3(length * ((10f * tau * tau * tau) - (15f * tau * tau * tau * tau) + (6f * tau * tau * tau * tau * tau)), 0f, 0f);
        })];
    }

    [TestMethod]
    public void MinimumJerkReachHasDimensionlessJerk720()
    {
        // 連続の答えは 720。記録の間隔（1/240 s）では 3 階の差分の端の目減りで 1 割ほど小さく出る（細かく取ると float の位置の丸めが効くので、
        // 記録と同じ間隔で確かめる）。
        var jerk = MotionMetrics.NormalizedJerk(MinimumJerk(0.4f, 0.5f), Dt);
        Assert.AreEqual(720d, jerk, 720d * 0.1, $"jerk {jerk}");
        // 長さ・時間を変えても同じ（無次元）。
        Assert.AreEqual(jerk, MotionMetrics.NormalizedJerk(MinimumJerk(1.3f, 0.5f), Dt), jerk * 0.02);
    }

    [TestMethod]
    public void TwoSubmovementsAreJerkierThanOneSmoothReach()
    {
        var smooth = MinimumJerk(0.4f, 0.5f);
        var half = MinimumJerk(0.2f, 0.25f);
        Vector3[] halting = [.. half, .. half.Skip(1).Select(p => p + new Vector3(0.2f, 0f, 0f))];
        Assert.IsTrue(MotionMetrics.NormalizedJerk(halting, Dt) > 2d * MotionMetrics.NormalizedJerk(smooth, Dt));
    }

    private static float[] Bump(float peak, float seconds) =>
        [.. Enumerable.Range(0, (int)(seconds / Dt)).Select(i => MathF.Exp(-MathF.Pow(((i * Dt) - peak) / 0.03f, 2f)))];

    [TestMethod]
    public void ChainOrderIsOneProximalFirstAndZeroReversed()
    {
        float[] peaks = [0.10f, 0.14f, 0.18f, 0.22f, 0.26f];
        var forward = MotionMetrics.ChainOrder([.. peaks.Select(p => (IReadOnlyList<float>)Bump(p, 0.4f))], Dt);
        Assert.AreEqual(1f, forward.OrderScore);
        Assert.AreEqual(0.18f, forward.PeakTimes[2], Dt);
        var reversed = MotionMetrics.ChainOrder([.. peaks.Reverse().Select(p => (IReadOnlyList<float>)Bump(p, 0.4f))], Dt);
        Assert.AreEqual(0f, reversed.OrderScore);
    }

    [TestMethod]
    public void SupportDistanceIsNegativeInsideAndTheGapOutside()
    {
        Vector2[] feet = [new(0f, 0f), new(0.3f, 0f), new(0.3f, 0.2f), new(0f, 0.2f)];
        Assert.AreEqual(-0.1f, MotionMetrics.SupportDistance(new Vector2(0.15f, 0.1f), feet), 1e-5f);
        Assert.AreEqual(0.1f, MotionMetrics.SupportDistance(new Vector2(0.4f, 0.1f), feet), 1e-5f);
        Assert.AreEqual(0.05f, MotionMetrics.SupportDistance(new Vector2(0.15f, 0.05f), [new(0f, 0f), new(0.3f, 0f)]), 1e-5f);
        Assert.AreEqual(0.5f, MotionMetrics.SupportDistance(new Vector2(0.3f, 0.4f), [Vector2.Zero]), 1e-5f);
        Assert.IsTrue(float.IsNaN(MotionMetrics.SupportDistance(Vector2.Zero, [])));
    }

    [TestMethod]
    public void OnlyFastOscillationCountsAsJitter()
    {
        const float Sample = 1f / 120f;
        Vector3[] Wave(float hz, float amplitude) => [.. Enumerable.Range(0, 120).Select(i => new Vector3(amplitude * MathF.Sin(2f * MathF.PI * hz * i * Sample), 0f, 0f))];
        var shaking = MotionMetrics.JitterFlips(Wave(40f, 0.01f), Sample, 0.1f, 30f);
        var swinging = MotionMetrics.JitterFlips(Wave(3f, 0.2f), Sample, 0.1f, 30f);
        Assert.IsTrue(shaking[60] >= MotionAudit.JitterFlips, $"40 Hz: {shaking[60]}");
        Assert.IsTrue(swinging.Max() <= 1, $"3 Hz: {swinging.Max()}");
    }

    [TestMethod]
    public void AnglesTwistAndPlaneOfKnownShapes()
    {
        Assert.AreEqual(180f, MotionMetrics.AngleAt(new Vector3(0f, 1f, 0f), new Vector3(0f, 0.5f, 0f), Vector3.Zero), 1e-3f);
        Assert.AreEqual(90f, MotionMetrics.AngleAt(Vector3.UnitX, Vector3.Zero, Vector3.UnitY), 1e-3f);
        Assert.AreEqual(30f, MotionMetrics.TrunkLean(Vector3.Zero, new Vector3(0f, MathF.Cos(MathF.PI / 6f), MathF.Sin(MathF.PI / 6f))), 1e-3f);
        // 肩の線を腰の線から 20° 回す。
        var r = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 20f * MathF.PI / 180f);
        var twist = MotionMetrics.TorsoTwist(Vector3.Transform(Vector3.UnitX, r), Vector3.Transform(-Vector3.UnitX, r), Vector3.UnitX, -Vector3.UnitX);
        Assert.AreEqual(20f, MathF.Abs(twist), 1e-3f);
        Assert.AreEqual(-170f, MotionWords.Wrap(190f), 1e-3f);
        var circle = Enumerable.Range(0, 36).Select(i => new Vector3(MathF.Cos(i * 0.17f), 0.5f, MathF.Sin(i * 0.17f))).ToArray();
        var plane = MotionMetrics.PlaneFit(circle);
        Assert.IsTrue(plane.Rms < 1e-3f);
        Assert.AreEqual(1f, MathF.Abs(plane.Normal.Y), 1e-3f);
        Vector3[] u = [new(0f, 1f, 0f), new(0.5f, 0.6f, 0f), new(1f, 1f, 0f)];
        Assert.AreEqual(0.4f, MotionWords.Dip(u), 1e-5f);
    }

    [TestMethod]
    public void NaturalnessCostIsZeroForAnOrderedMinimumJerkMotion()
    {
        Assert.AreEqual(0d, MiniRing.Arena.FormCost.OfNaturalness(1d, 720d), 1e-9);
        Assert.AreEqual(MiniRing.Arena.FormCost.PerChainDisorder + MiniRing.Arena.FormCost.PerJerkDecade, MiniRing.Arena.FormCost.OfNaturalness(0d, 7200d), 1e-9);
    }

    [TestMethod]
    public void RunsKeepOnlyLongEnoughStretches()
    {
        bool[] flags = [false, true, true, true, false, true, false];
        var runs = MotionMetrics.Runs(flags, 0.1f, 0.25f);
        Assert.AreEqual(1, runs.Count);
        Assert.AreEqual((1, 3), runs[0]);
    }

    /// <summary>立って動かない体の記録（脚の長さ 0.9 m、足は両方着いている）。<paramref name="edit"/> で時刻ごとに変える。</summary>
    private static MotionTrace Standing(int frames, Func<int, MotionSample, MotionSample> edit)
    {
        var trace = new MotionTrace("synthetic", "red", 1, legLength: 0.9f, armReach: 0.6f, every: 2);
        for (var i = 0; i < frames; i++)
        {
            var p = new Vector3[MotionTrace.PointNames.Length];
            void Set(string name, Vector3 v) => p[Array.IndexOf(MotionTrace.PointNames, name)] = v;
            Set("head", new(0f, 1.7f, 0f)); Set("chest", new(0f, 1.4f, 0f)); Set("pelvis", new(0f, 1f, 0f)); Set("com", new(0f, 1f, 0f));
            Set("shoulderL", new(0.2f, 1.45f, 0f)); Set("elbowL", new(0.25f, 1.2f, 0.1f)); Set("handL", new(0.2f, 1.4f, 0.3f));
            Set("shoulderR", new(-0.2f, 1.45f, 0f)); Set("elbowR", new(-0.25f, 1.2f, 0.1f)); Set("handR", new(-0.2f, 1.4f, 0.3f));
            Set("hipL", new(0.09f, 0.95f, 0f)); Set("kneeL", new(0.1f, 0.5f, 0.02f)); Set("ankleL", new(0.1f, 0.08f, 0f)); Set("heelL", new(0.1f, 0.03f, -0.05f)); Set("toeL", new(0.1f, 0.03f, 0.15f));
            Set("hipR", new(-0.09f, 0.95f, 0f)); Set("kneeR", new(-0.1f, 0.5f, 0.02f)); Set("ankleR", new(-0.1f, 0.08f, 0f)); Set("heelR", new(-0.1f, 0.03f, -0.05f)); Set("toeR", new(-0.1f, 0.03f, 0.15f));
            Set("tip", new(-0.2f, 1.4f, 0.3f));
            var sample = new MotionSample(i * trace.Dt, p, new(0.2f, 1.4f, 0.3f), new(-0.2f, 1.4f, 0.3f), [true, true, true, true], 0.002f, "LeftAnkle", float.NaN, 1f, false, null, 0,
                Vector3.UnitZ, new(0f, 1.7f, 1f), 0f);
            trace.Frames.Add(edit(i, sample));
        }

        return trace;
    }

    [TestMethod]
    public void QuietStanceHasNoFindings()
    {
        var findings = MotionAudit.Run(Standing(120, (_, s) => s));
        Assert.AreEqual(0, findings.Count, MotionAudit.Format(findings));
    }

    [TestMethod]
    public void AuditFindsAToeGapThatLasts()
    {
        var findings = MotionAudit.Run(Standing(120, (i, s) => i is >= 40 and < 60 ? s with { MaxGap = 0.06f, GapJoint = "RightToe" } : s));
        var gap = findings.Single(f => f.Kind == "gap");
        Assert.AreEqual("RightToe", gap.Where);
        Assert.AreEqual(40 / 120f, gap.From, 1e-3f);
        Assert.AreEqual(0.06f, gap.Value, 1e-5f);
    }

    [TestMethod]
    public void AuditFindsAFootLeftBehindWhileTheHipsMoveAway()
    {
        // 右足は床に着いたまま後ろ（−Z）に残り、腰が前へ 0.6 m/s で離れていく。0.3 s 後には脚が伸び切る。
        var findings = MotionAudit.Run(Standing(120, (i, s) =>
        {
            var p = (Vector3[])s.Points.Clone();
            var shift = new Vector3(0f, 0f, 0.6f * s.Time);
            foreach (var name in MotionTrace.PointNames.Where(n => n is not ("kneeR" or "ankleR" or "heelR" or "toeR")))
            {
                var k = Array.IndexOf(MotionTrace.PointNames, name);
                p[k] += shift;
            }

            return s with { Points = p };
        }));
        Assert.IsTrue(findings.Any(f => f.Kind == "snag" && f.Where == "右足"), MotionAudit.Format(findings));
        Assert.IsFalse(findings.Any(f => f.Kind == "snag" && f.Where == "左足"), MotionAudit.Format(findings));
    }

    [TestMethod]
    public void AuditFindsLeftAndRightWiredTheWrongWay()
    {
        var findings = MotionAudit.Run(Standing(60, (_, s) => s with { LeftTarget = s.RightTarget, RightTarget = s.LeftTarget }));
        Assert.IsTrue(findings.Any(f => f.Kind == "swap"), MotionAudit.Format(findings));
    }
}
