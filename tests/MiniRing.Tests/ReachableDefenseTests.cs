using System.Numerics;
using MiniRing.Bots;
using MiniRing.Motion;

namespace MiniRing.Tests;

/// <summary>剣の守り 2 周目（間に合う受け・構えの経済・弧の読み）が、届く範囲と遮る量の連続の比べで決まることを数値で確かめる。</summary>
[TestClass]
public sealed class ReachableDefenseTests
{
    private const float Reach = 0.4789f;

    private static FighterSense Sense() =>
        new(new BodyFrame(Vector3.Zero, 0f), Reach, new Vector3(0f, 0.2817f, 0.9859f), new Vector3(0f, 0f, 0.9859f), OpponentAlive: true,
            SelfHead: new Vector3(0f, 0.2254f, 0f), SelfArmed: true, Ring: RingLayout.Weapons);

    private static WeaponThreat OverheadCut(float time)
    {
        var hilt = new Vector3(0f, 0.7042f, 0.6197f);
        var tip = new Vector3(0f, 0.845f, 0.1408f);
        var velocity = Vector3.Normalize(new Vector3(0f, -3f, -0.5f)) * (7.150f * Reach);
        var line = new WeaponLine(hilt, tip, velocity, velocity, hilt, Reach * WeaponDefense.ShoulderReach, default);
        return new WeaponThreat(tip, velocity, time, new Vector3(0f, 0.2254f, 0f), 1f, line);
    }

    private static readonly SwordDefense Open = SwordDefense.Parrying with { Reach = true, HandSpeed = 3.575f, ReactDelay = 0.05035f, InterceptGap = 10f, Counter = false };

    private static MotionFrame Rest => new(new Vector3(0.1f, -0.2f, 0.5f), new Vector3(0.1f, -0.2f, -0.9f), Vector3.UnitZ, Vector3.UnitY, Vector3.Zero, 0f, Vector3.Zero);

    private static MotionFrame? Reachable(float time, SwordDefense defense, Vector3 from)
    {
        var sense = Sense();
        var threat = OverheadCut(time);
        var ideal = WeaponDefense.Parry(sense, threat, defense, 1.7f, SwordForms.KatanaSpacing, Vector3.UnitZ);
        return WeaponDefense.ReachableParry(sense, threat, defense, ideal, from, Vector3.UnitZ, Rest, 1.7f, SwordForms.KatanaSpacing);
    }

    [TestMethod]
    public void TheHandTarget_NeverLeavesTheRangeTheHandCanCoverInTheTimeLeft()
    {
        var from = new Vector3(0.3f, -0.3f, 0.4f);
        foreach (var time in new[] { 0.0671f, 0.1343f, 0.2014f, 0.3357f })
        {
            var frame = Reachable(time, Open, from)!.Value;
            var budget = Open.HandSpeed * MathF.Max(0f, time - Open.ReactDelay);
            Assert.IsTrue(Vector3.Distance(frame.Right, from) <= budget + 1e-3f, $"残り {time} 秒で手が届く {budget:0.00} 腕の長さの内");
        }
    }

    [TestMethod]
    public void WithMoreTimeLeft_TheTargetApproachesTheIdealParry()
    {
        var from = new Vector3(0.3f, -0.3f, 0.4f);
        var sense = Sense();
        var ideal = WeaponDefense.Parry(sense, OverheadCut(0.5035f), Open, 1.7f, SwordForms.KatanaSpacing, Vector3.UnitZ);
        var shortTime = Reachable(0.1007f, Open, from)!.Value;
        var longTime = Reachable(0.5035f, Open, from)!.Value;
        Assert.IsTrue(Vector3.Distance(longTime.Right, ideal.Right) <= Vector3.Distance(shortTime.Right, ideal.Right) + 1e-3f, "時間が長いほど理想の受けの所に近い");
        Assert.IsTrue(Vector3.Distance(shortTime.Right, ideal.Right) > 0.3f, "時間が短いと理想の所へは届かない");
    }

    [TestMethod]
    public void WhenNothingReachableCanBlockTheLine_NoParryIsMade()
    {
        var strict = Open with { InterceptGap = 0.1f };
        Assert.IsNull(Reachable(0.05035f, strict, new Vector3(0.3f, -0.6f, 0.4f)), "時間が無く、手の届く範囲では線を遮れないなら受けず、足と頭に任せる");
        Assert.IsNotNull(Reachable(0.5035f, Open with { InterceptGap = 0.5f }, new Vector3(0.3f, -0.6f, 0.4f)), "時間があれば受ける");
    }

    [TestMethod]
    public void Economy_PrefersKeepingTheGuardWhenItAlreadyCoversTheLine()
    {
        var from = Rest.Right;
        var lazy = Reachable(0.5035f, Open with { Economy = 3f }, from)!.Value;
        var free = Reachable(0.5035f, Open with { Economy = 0f }, from)!.Value;
        Assert.IsTrue(Vector3.Distance(lazy.Right, Rest.Right) <= Vector3.Distance(free.Right, Rest.Right) + 1e-4f, "動きの重みが大きいほど構えに近い");
    }

    [TestMethod]
    public void ReachableParry_IsDeterministic()
    {
        var from = new Vector3(0.2f, -0.3f, 0.4f);
        Assert.AreEqual(Reachable(0.1678f, Open, from), Reachable(0.1678f, Open, from));
    }

    [TestMethod]
    public void ArcPrediction_FollowsACircularSwing_BetterThanALine()
    {
        // 肩を中心に、ω = 10 rad/s（+Y まわり）で回る点: 半径 0.5 m、速さ 5 m/s。
        var p0 = new Vector3(0f, 0f, 0.5f);
        var v = new Vector3(5f, 0f, 0f);
        var line = new WeaponLine(p0, p0, v, v, Vector3.Zero, 2f, default);
        var tau = 0.1f;
        var truth = new Vector3(0.5f * MathF.Sin(1f), 0f, 0.5f * MathF.Cos(1f));
        Assert.IsTrue(Vector3.Distance(line.Predict(1f, tau, true), truth) < 1e-4f, "弧の読みは円運動に合う");
        Assert.IsTrue(Vector3.Distance(line.Predict(1f, tau, false), truth) > 0.2f, "直線は外れる");
        Assert.AreEqual(5f, line.PredictVelocity(1f, tau, true).Length(), 1e-3f, "回っても速さは変わらない");
        Assert.IsTrue(MathF.Abs(Vector3.Distance(line.Predict(1f, 0.1f, true), Vector3.Zero) - 0.5f) < 1e-4f, "肩からの距離は保つ");
    }

    [TestMethod]
    public void ArcPrediction_StaysALine_ForAThrust()
    {
        var p0 = new Vector3(0f, 0f, 0.5f);
        var v = new Vector3(0f, 0f, -4f);
        var line = new WeaponLine(p0, p0, v, v, Vector3.Zero, 2f, default);
        Assert.IsTrue(Vector3.Distance(line.Predict(1f, 0.05f, true), line.Predict(1f, 0.05f, false)) < 1e-5f, "肩へ向かう動きに回りは無い");
    }

    [TestMethod]
    public void ArcPrediction_StopsAtTheShoulderRangeInsteadOfCirclingForever()
    {
        var p0 = new Vector3(0f, 0f, 0.5f);
        var v = new Vector3(5f, 0f, 0f);
        var line = new WeaponLine(p0, p0, v, v, Vector3.Zero, 2f, default);
        Assert.IsTrue(Vector3.Distance(line.Predict(1f, 5f, true), line.Predict(1f, 50f, true)) < 1e-4f);
    }
}
