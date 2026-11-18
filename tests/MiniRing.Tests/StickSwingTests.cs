using System.Numerics;
using MiniRing.Motion.Stick;

namespace MiniRing.Tests;

/// <summary>片手剣の振りの探索（<see cref="SwingRig"/>・<see cref="SwingSearch"/>、docs/STICK_MOTION.md §11）の補間・探索・評価。</summary>
[TestClass]
public sealed class StickSwingTests
{
    private static readonly Lazy<SwingRig> Rig = new(() => SwingRig.Create("jin"));

    [TestMethod]
    public void ViaPointInterpolation_PassesTheKeys_AndIsSmoothThroughThem()
    {
        double[] x = [0.0, 1.0, -0.5, 2.0];
        double[] durations = [0.3, 0.2, 0.4];
        var segments = SwingInterpolation.ViaPoints(x, durations);

        for (var s = 0; s < 3; s++)
        {
            Assert.AreEqual(x[s], SwingInterpolation.Evaluate(segments[s], 0), 1e-9, $"区間 {s} の始まり");
            Assert.AreEqual(x[s + 1], SwingInterpolation.Evaluate(segments[s], durations[s]), 1e-9, $"区間 {s} の終わり");
        }

        // 両端は止まっている（速さ・加速度 0）。
        Assert.AreEqual(0.0, SwingInterpolation.Derivative(segments[0], 0, 1), 1e-9);
        Assert.AreEqual(0.0, SwingInterpolation.Derivative(segments[0], 0, 2), 1e-9);
        Assert.AreEqual(0.0, SwingInterpolation.Derivative(segments[2], durations[2], 1), 1e-9);
        Assert.AreEqual(0.0, SwingInterpolation.Derivative(segments[2], durations[2], 2), 1e-9);

        // 経由点で、速さ・加速度・躍度が連続（躍度最小の条件）。
        for (var s = 0; s < 2; s++)
        {
            for (var order = 1; order <= 3; order++)
            {
                Assert.AreEqual(
                    SwingInterpolation.Derivative(segments[s], durations[s], order),
                    SwingInterpolation.Derivative(segments[s + 1], 0, order),
                    1e-6,
                    $"経由点 {s + 1} で {order} 階微分が連続");
            }
        }
    }

    [TestMethod]
    public void ViaPointInterpolation_OfTwoKeys_IsTheMinimumJerkQuintic()
    {
        var segments = SwingInterpolation.ViaPoints([0.0, 1.0], [0.5]);
        // 10 s³ − 15 s⁴ + 6 s⁵ の真ん中 = 0.5。
        Assert.AreEqual(0.5, SwingInterpolation.Evaluate(segments[0], 0.25), 1e-9);
    }

    [TestMethod]
    public void ThePlanes_HaveUnitNormals_AndCutDirectionsInsideThem()
    {
        Assert.AreEqual(6, SwingPlane.All.Count);
        foreach (var p in SwingPlane.All)
        {
            Assert.AreEqual(1f, p.Normal.Length(), 1e-5f, p.Name);
            Assert.AreEqual(1f, p.Direction.Length(), 1e-5f, p.Name);
            Assert.AreEqual(0f, Vector3.Dot(p.Normal, p.Direction), 1e-5f, $"{p.Name} の斬る向きは断面の中");
        }
    }

    [TestMethod]
    public void TheRig_HoldsASwordInTheRightHand_AndEveryRandomGeneIsInsideTheJointRanges()
    {
        var rig = Rig.Value;
        Assert.IsTrue(rig.Body.Armed);
        Assert.IsTrue(rig.BladeLength > 0.1f);
        var random = new Random(3);
        for (var i = 0; i < 20; i++)
        {
            var genes = rig.RandomGenes(random);
            CollectionAssert.AreEqual(genes, rig.Clamp(genes), "引いた遺伝子は範囲の中（もう一度収めても変わらない）");
            for (var k = 0; k < SwingRig.KeyCount; k++)
            {
                var at = k * SwingRig.PerKey;
                Assert.IsTrue(genes[at + 3] >= rig.ElbowLow && genes[at + 3] <= rig.ElbowHigh, "肘の角");
                for (var w = 0; w < 3; w++)
                {
                    var (low, high) = rig.WristRange(w);
                    Assert.IsTrue(genes[at + 4 + w] >= low && genes[at + 4 + w] <= high, "手首の角");
                }
            }
        }
    }

    [TestMethod]
    public void ASlowGentleSwing_IsWithinTheJointStrength_AndTheTraceStartsAtTheGuard()
    {
        var rig = Rig.Value;
        var genes = new float[SwingRig.GeneCount];
        // 3 つのキーを全部、構えの関節角（肩・肘。手首は 0 = 構え）にして時間を長く: 構えのまま動かない振りは、重さを支えるだけで負担が 1 を大きく下回る。
        for (var k = 0; k < SwingRig.KeyCount; k++)
        {
            for (var i = 0; i < 3; i++)
            {
                genes[(k * SwingRig.PerKey) + i] = rig.Body.Guard.Values[rig.Body.Offsets[StickBody.Ids.UpperArmR] + i];
            }

            genes[(k * SwingRig.PerKey) + 3] = rig.Body.Guard.Values[rig.Body.Offsets[StickBody.Ids.ForearmR]];
            genes[(SwingRig.KeyCount * SwingRig.PerKey) + k] = 0.5f;
        }

        var trace = rig.Simulate(genes);
        var first = trace.Frames[0].Tip(StickBody.Ids.Weapon);
        var guardTip = rig.GuardFrame.Tip(StickBody.Ids.Weapon);
        Assert.IsTrue(Vector3.Distance(first, guardTip) < 0.1f * rig.Reach, "始まりは構えの近く");
        var eval = rig.Evaluate(trace, SwingPlane.Get("vertical"), new SwingWeights());
        Assert.IsTrue(eval.MaxLoad < 1f, $"負担 {eval.MaxLoad:0.00}");
        Assert.AreEqual(0f, eval.RangeExcess, "構えのままの振りは関節の範囲をはみ出さない");
    }

    [TestMethod]
    public void TheGripLimits_AreInsideTheLooseLimits()
    {
        var grip = Rig.Value;
        var loose = SwingRig.Create("jin", SwingLimits.Loose);
        for (var w = 0; w < 3; w++)
        {
            Assert.IsTrue(grip.WristRange(w).Low >= loose.WristRange(w).Low && grip.WristRange(w).High <= loose.WristRange(w).High, $"手首 {w}");
            Assert.IsTrue(grip.WristRange(w).High - grip.WristRange(w).Low < loose.WristRange(w).High - loose.WristRange(w).Low, $"握ると手首 {w} は狭まる");
        }

        Assert.IsTrue(grip.ElbowLow >= loose.ElbowLow && grip.ElbowHigh <= loose.ElbowHigh, "肘");
        Assert.IsTrue(grip.ShoulderTwistHigh - grip.ShoulderTwistLow < loose.ShoulderTwistHigh - loose.ShoulderTwistLow, "肩のねじれは狭まる");
    }

    [TestMethod]
    public void TheSearch_IsDeterministic_NeverLosesPointsWhenPolishing_AndTheBestIsInsideTheRangesAndTheStrength()
    {
        var rig = Rig.Value;
        var plane = SwingPlane.Get("kesa");
        var options = new SwingSearchOptions(Samples: 120, Polish: 3, Iterations: 60, Seed: 5);

        var a = SwingSearch.Run(rig, plane, options);
        var b = SwingSearch.Run(rig, plane, options);

        CollectionAssert.AreEqual(a.BestGenes, b.BestGenes, "同じ種なら同じ振り");
        Assert.AreEqual(a.Best.Score, b.Best.Score);
        Assert.IsTrue(a.Best.Score >= a.BestRandom.Score, $"磨いて点が下がらない（{a.BestRandom.Score:0.00} → {a.Best.Score:0.00}）");
        CollectionAssert.AreEqual(a.BestGenes, rig.Clamp(a.BestGenes), "最良は関節の範囲の中");
        Assert.IsTrue(a.Best.Valid, $"最良は失格でない（負担 {a.Best.MaxLoad:0.00}・{a.Best.MaxLoadJoint}・自傷 {a.Best.SelfCut:0.000}・腕の入り込み {a.Best.ArmOverlap:0.000}・範囲のはみ出し {a.Best.RangeExcess:0.000}）");
        Assert.IsTrue(a.Best.MaxLoad <= 1f);
        Assert.IsTrue(a.Best.RangeExcess <= MathF.PI / 180f, "補間も関節の範囲の中（1° まで）");
    }
}
