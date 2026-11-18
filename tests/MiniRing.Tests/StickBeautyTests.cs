using System.Numerics;
using MiniRing.Motion.Stick;

namespace MiniRing.Tests;

/// <summary>動きのきれいさの関数（<see cref="StickBeauty"/>、docs/STICK_MOTION.md §11）が、きれいな動きとそうでない動きを正しく分けるか。</summary>
[TestClass]
public sealed class StickBeautyTests
{
    private const float Dt = 1f / 120f;

    /// <summary>躍度最小の直線: x(s) = 10 s³ − 15 s⁴ + 6 s⁵（s = t / T）。</summary>
    private static Vector3[] MinimumJerkLine(float length, float seconds, Vector3 direction, float from = 0f, float to = 1f)
    {
        var n = (int)MathF.Round(seconds / Dt) + 1;
        var path = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            var s = i / (float)(n - 1);
            var x = (10f * s * s * s) - (15f * s * s * s * s) + (6f * s * s * s * s * s);
            path[i] = direction * length * (from + ((to - from) * x));
        }

        return path;
    }

    [TestMethod]
    public void AMinimumJerkLine_IsSmootherThanTheSameLineWithNoise_ByLdljAndSparc()
    {
        var clean = MinimumJerkLine(0.3f, 0.5f, Vector3.UnitX);
        var random = new Random(7);
        var noisy = clean.Select(p => p + new Vector3(((random.NextSingle() * 2f) - 1f) * 0.005f, ((random.NextSingle() * 2f) - 1f) * 0.005f, ((random.NextSingle() * 2f) - 1f) * 0.005f)).ToArray();

        var ldljClean = StickBeauty.Ldlj(clean, Dt)!.Value;
        var ldljNoisy = StickBeauty.Ldlj(noisy, Dt)!.Value;
        var sparcClean = StickBeauty.Sparc(StickBeauty.Speeds(clean, Dt), Dt)!.Value;
        var sparcNoisy = StickBeauty.Sparc(StickBeauty.Speeds(noisy, Dt), Dt)!.Value;

        Assert.IsTrue(ldljClean > ldljNoisy + 2f, $"LDLJ: きれい {ldljClean:0.0}、雑音つき {ldljNoisy:0.0}");
        Assert.IsTrue(sparcClean > sparcNoisy + 0.2f, $"SPARC: きれい {sparcClean:0.00}、雑音つき {sparcNoisy:0.00}");
        Assert.IsTrue(sparcClean is < -0.8f and > -2.5f, $"滑らかな 1 つの山の SPARC は約 −1.4（{sparcClean:0.00}）");
    }

    [TestMethod]
    public void TwoSpeedPeaks_AreWorseThanOne()
    {
        var one = MinimumJerkLine(0.3f, 1.0f, Vector3.UnitX);
        // 前半 0 → 0.15、後半 0.15 → 0.3 を、間に止まる谷をはさんでつなぐ（山が 2 つ）。
        var first = MinimumJerkLine(0.3f, 0.5f, Vector3.UnitX, 0f, 0.5f);
        var second = MinimumJerkLine(0.3f, 0.5f, Vector3.UnitX, 0.5f, 1f);
        var two = first.Concat(second.Skip(1)).ToArray();

        Assert.AreEqual(1, StickBeauty.SpeedPeaks(StickBeauty.Speeds(one, Dt)));
        Assert.AreEqual(2, StickBeauty.SpeedPeaks(StickBeauty.Speeds(two, Dt)));
        Assert.IsTrue(StickBeauty.Ldlj(two, Dt)!.Value < StickBeauty.Ldlj(one, Dt)!.Value, "山が 2 つの動きは LDLJ が悪い");
        Assert.IsTrue(StickBeauty.Sparc(StickBeauty.Speeds(two, Dt), Dt)!.Value < StickBeauty.Sparc(StickBeauty.Speeds(one, Dt), Dt)!.Value, "山が 2 つの動きは SPARC が悪い");
    }

    [TestMethod]
    public void TheMainSubmovement_IsTheValleyToValleyRunAroundTheTallestPeak()
    {
        var first = MinimumJerkLine(0.1f, 0.4f, Vector3.UnitX);
        var second = MinimumJerkLine(0.3f, 0.5f, Vector3.UnitX, 0f, 1f).Select(p => p + (0.1f * Vector3.UnitX));
        var path = first.Concat(second.Skip(1)).ToArray();
        var speeds = StickBeauty.Speeds(path, Dt);
        var (from, to) = StickBeauty.MainSubmovement(speeds);
        var boundary = first.Length - 1;
        Assert.IsTrue(Math.Abs(from - boundary) <= 2, $"主な動きは大きい方（2 本目）の始まり {boundary} から（{from}）");
        Assert.IsTrue(to >= path.Length - 3, $"…終わりまで（{to}）");
    }

    private static Vector3[] Circle(float radius, int n, float rise = 0f, Vector3? normal = null)
    {
        var axis = Vector3.Normalize(normal ?? Vector3.UnitZ);
        var u = Vector3.Normalize(Vector3.Cross(axis, Math.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var v = Vector3.Cross(axis, u);
        return [.. Enumerable.Range(0, n).Select(i =>
        {
            var a = MathF.Tau * i / n;
            return (radius * ((MathF.Cos(a) * u) + (MathF.Sin(a) * v))) + (rise * i / n * axis);
        })];
    }

    [TestMethod]
    public void APlanarCircle_HasNoPlaneMisfit_AndATwistedHelixHasALargeOne()
    {
        var normal = Vector3.Normalize(new Vector3(1f, 1f, 0f));
        var origin = new Vector3(0.1f, 0.2f, 0.3f);
        var circle = Circle(0.3f, 100, normal: normal).Select(p => p + origin).ToArray();
        var helix = Circle(0.3f, 100, rise: 0.6f, normal: normal).Select(p => p + origin).ToArray();

        Assert.AreEqual(0f, StickBeauty.PlaneMisfit(circle, origin, normal), 1e-4f);
        Assert.IsTrue(StickBeauty.PlaneMisfit(helix, origin, normal) > 0.4f, "らせんは断面から大きく外れる");
        Assert.IsTrue(StickBeauty.PlaneMisfit(circle, origin, Vector3.UnitZ) > 0.4f, "別の平面を与えれば外れる");

        var (planar, found) = StickBeauty.BestFitPlane(circle);
        Assert.IsTrue(planar < 1e-3f, $"最もよく合う平面で円の外れは 0（{planar}）");
        Assert.AreEqual(1f, MathF.Abs(Vector3.Dot(found, normal)), 1e-3f, "法線が合う");
        Assert.IsTrue(StickBeauty.BestFitPlane(helix).Misfit > 0.2f, "らせんは最も合う平面でも外れる");
    }

    [TestMethod]
    public void TheEdgeAngle_IsZeroWhenCuttingAlongTheEdge_AndRightAngleWhenSlappingWithTheFlat()
    {
        const int n = 120;
        var tip = Circle(0.3f, n);
        var radial = tip.Select(Vector3.Normalize).ToArray();
        var tangent = radial.Select(r => Vector3.Cross(Vector3.UnitZ, r)).ToArray();
        var normalEdges = Enumerable.Repeat(Vector3.UnitZ, n).ToArray();

        // 刃の向き = 半径、刃先の向き = 進む向き → 刃の面の中を切る。
        Assert.AreEqual(0f, StickBeauty.EdgeAngle(tip, radial, tangent, Dt), 0.02f);
        // 刃先の向きが面の法線（Z）なら、刃の腹が進む向きを向いて叩く。
        Assert.AreEqual(MathF.PI / 2f, StickBeauty.EdgeAngle(tip, radial, normalEdges, Dt), 0.02f);
    }

    [TestMethod]
    public void TheCurvatureVariation_IsNearZeroForACircleAndLargeForAWobblyPath()
    {
        var circle = Circle(0.3f, 200);
        var wobbly = Enumerable.Range(0, 200).Select(i =>
        {
            var s = i / 199f;
            return new Vector3(0.3f * s, 0.03f * MathF.Sin(MathF.Tau * 4f * s), 0f);
        }).ToArray();

        var round = StickBeauty.CurvatureVariation(circle);
        var rough = StickBeauty.CurvatureVariation(wobbly);
        Assert.IsTrue(round < 0.5f, $"円の曲率の変化は 0 に近い（{round}）");
        Assert.IsTrue(rough > 20f * round + 5f, $"揺れた軌跡は大きい（{rough}）");
    }
}
