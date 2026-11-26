using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>ガルヴァの全身で振る練習（2026-11-25）の部品: 振りの体の拍子、形の代価の項、教育が空なら今までの振り。</summary>
[TestClass]
public sealed class ClawFormTests
{
    [TestMethod]
    public void Swing_LoadsBackThenDrivesThroughThenReturns_Continuously()
    {
        var stroke = ClawStroke.Create(Hand.Right, 1f, new System.Numerics.Vector3(0f, 0.5f, 1.2f), true, drive: 0.3f, windup: 1f);
        Assert.AreEqual(0f, stroke.Swing(0f), 1e-6f, "構えから始まる");
        Assert.AreEqual(0f, stroke.Swing(stroke.Duration), 1e-6f, "構えへ戻る");
        var previous = stroke.Swing(0f);
        var low = 0f;
        var high = 0f;
        for (var t = 0f; t <= stroke.Duration; t += 0.002f)
        {
            var s = stroke.Swing(t);
            Assert.IsLessThan(0.05f, MathF.Abs(s - previous), $"拍子は跳ばない（t = {t}）");
            low = MathF.Min(low, s);
            high = MathF.Max(high, s);
            previous = s;
        }

        Assert.AreEqual(-1f, low, 1e-3f, "溜めで打つ側の肩をいちばん引く");
        Assert.AreEqual(1f, high, 1e-3f, "振り抜きでいちばん出す");
    }

    [TestMethod]
    public void FormCost_NewTermsGrowWithImbalanceJerkAndSlowness_AndVanishWhenNotMeasured()
    {
        var baseCost = FormCost.Of(0d, 0d, 10d, 0.02d);
        Assert.AreEqual(baseCost, FormCost.Of(0d, 0d, 10d, 0.02d, 0d, 0d, 0d), 1e-12, "新しい項を渡さない練習は今までと同じ代価");
        Assert.IsGreaterThan(FormCost.Of(0d, 0d, 10d, 0.02d, imbalance: 1d), FormCost.Of(0d, 0d, 10d, 0.02d, imbalance: 2d), "偏るほど高い");
        Assert.IsGreaterThan(FormCost.Of(0d, 0d, 10d, 0.02d, jerk: 1000d), FormCost.Of(0d, 0d, 10d, 0.02d, jerk: 2000d), "ぎくしゃくするほど高い");
        Assert.IsGreaterThan(FormCost.Of(0d, 0d, 10d, 0.02d, speed: 20d), FormCost.Of(0d, 0d, 10d, 0.02d, speed: 10d), "遅いほど高い");
        Assert.AreEqual(FormCost.Of(0d, 0d, 10d, 0.02d, speed: FormCost.CuttingSpeed), FormCost.Of(0d, 0d, 10d, 0.02d, speed: FormCost.CuttingSpeed + 5d), 1e-12,
            "断てる速さより速くても得点にしない（代価が 0 より下がらない）");
    }

    [TestMethod]
    public void ClawForm_PointsAreCutPerStrokeMinusTheFormCost()
    {
        var form = new ClawForm(4, ClawForm.HumanTankJoules * 0.08, 1d, 1500d, 20d, 12d, 480, [0.2, 0.2, 0.2, 0.2, 0.2]);
        Assert.AreEqual(0.02d, form.TanksPerStroke, 1e-9, "1 振りの仕事は人の満タンの割合で数える");
        Assert.AreEqual((480d / 60d / 4d) - FormCost.Of(0d, 0d, 12d, 0.02d, 1d, 1500d, 20d), form.Points, 1e-9);
    }

    [TestMethod]
    public void EmptyEducation_KeepsTheOldStroke_NoStepNoSpringNoPull()
    {
        var style = new BotStyle();
        Assert.AreEqual(0f, style.ClawPull);
        Assert.AreEqual(0f, style.ClawStep);
        Assert.AreEqual(0f, style.ClawSpring);
        var clamped = new BotStyle { ClawPull = 5f, ClawStep = -1f, ClawSpring = float.NaN }.Validated();
        Assert.AreEqual(0.6f, clamped.ClawPull, "引き手は腕の長さの 0.6 倍まで");
        Assert.AreEqual(0f, clamped.ClawStep);
        Assert.AreEqual(0f, clamped.ClawSpring);
    }
}
