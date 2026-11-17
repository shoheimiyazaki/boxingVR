using System.Numerics;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 回し蹴りをロー・ミドル・ハイに分けた練習（docs/arena/kick_shin_lmh_*.md）: 手本の混ぜ合わせ、練習の差分（KickTune）、
/// 前の足・後ろの足で当たる深さが違うこと（左右の差の原因）、3 つの高さが狙った所へすねで当たること、前蹴りの人の手本。
/// </summary>
[TestClass]
public sealed class KickShinTests
{
    private static CapturedKick Synthetic(float azimuthA, float azimuthB, float elevation = 0f)
        => new("synthetic", 1, 1f, 0.5f, [azimuthA, azimuthB], [elevation, elevation], [0f, 0f], [0.3f, 0.3f], [0f, 0f], [0f, 0f]);

    [TestMethod]
    public void TheAzimuthInterpolation_TurnsTheShortWay_AcrossPlusMinusPi()
    {
        // 方位が +3.1 → −3.1 rad（どちらも「後ろ」寄り）の間を線形補間すると 0（前）を通ってしまう。短い方（後ろのまま）へ回る。
        var capture = Synthetic(3.1f, -3.1f);
        var body = new LegBody(new Vector3(0f, -0.4f, 0f), new Vector3(-0.1f, -0.8f, 0f), 0.4f, 0.4f);
        var command = capture.Evaluate(Hand.Right, 0.4f, new Vector3(0f, 0f, 0.5f), body, 0f);
        var thighDirection = (command.KneeLocal!.Value - body.Hip) / body.Thigh;
        Assert.IsLessThan(-0.9f, thighDirection.Z, "the thigh stays pointing backwards while the azimuth crosses ±π");
    }

    [TestMethod]
    public void TheMidModel_IsABlendOfTheLowAndHighModels_BetweenThem()
    {
        var low = CapturedKick.Load("roundhouse_low");
        var high = CapturedKick.Load("roundhouse_high");
        var mid = CapturedKick.Load("roundhouse_mid");
        Assert.IsTrue(mid.ImpactShare > low.ImpactShare && mid.ImpactShare < high.ImpactShare, "the impact moment lies between");
        Assert.IsTrue(mid.HumanSeconds > MathF.Min(low.HumanSeconds, high.HumanSeconds) && mid.HumanSeconds < MathF.Max(low.HumanSeconds, high.HumanSeconds));
        // 混ぜる割合 0 と 1 は端の手本そのもの（当たる瞬間の腿の仰角で確かめる）。
        float ElevationAtImpact(CapturedKick k) => k.Elevation[(int)MathF.Round(k.ImpactShare * (k.Elevation.Length - 1))];
        var zero = CapturedKick.Blend("zero", low, high, 0f);
        var one = CapturedKick.Blend("one", low, high, 1f);
        Assert.AreEqual(ElevationAtImpact(low), ElevationAtImpact(zero), 0.02f);
        Assert.AreEqual(ElevationAtImpact(high), ElevationAtImpact(one), 0.02f);
        // 中段の当たる瞬間の腿の仰角は、下段と上段の間。
        var lo = MathF.Min(ElevationAtImpact(low), ElevationAtImpact(high));
        var hi = MathF.Max(ElevationAtImpact(low), ElevationAtImpact(high));
        Assert.IsTrue(ElevationAtImpact(mid) >= lo - 0.02f && ElevationAtImpact(mid) <= hi + 0.02f, "the middle impact elevation lies between the two");
    }

    [TestMethod]
    public void TheTuneShiftsTheImpactOnly_AndKeepsTheStartOfTheKick()
    {
        var capture = CapturedKick.Load("roundhouse_low");
        var body = new LegBody(new Vector3(0f, -0.3f, 0f), new Vector3(-0.07f, -0.6f, 0f), 0.3f, 0.3f);
        var aim = new Vector3(0f, -0.1f, 0.4f);
        var tune = new KickTune(1f, 0.6f, 0.3f, 0.2f, 0f, -0.5f);
        var start = capture.Evaluate(Hand.Right, 0f, aim, body, -0.5f);
        var startTuned = capture.Evaluate(Hand.Right, 0f, aim, body, -0.5f, tune);
        Assert.AreEqual(0f, Vector3.Distance(start.KneeLocal!.Value, startTuned.KneeLocal!.Value), 1e-4f, "the window is closed at the start");
        var impact = capture.Evaluate(Hand.Right, capture.ImpactShare, aim, body, -0.5f);
        var impactTuned = capture.Evaluate(Hand.Right, capture.ImpactShare, aim, body, -0.5f, tune);
        Assert.IsGreaterThan(0.03f, Vector3.Distance(impact.KneeLocal!.Value, impactTuned.KneeLocal!.Value), "the knee target moves at the impact moment");
    }

    [TestMethod]
    public void TheTunesLoad_ForEveryHeight_AndTheLegRoleFollowsTheStance()
    {
        foreach (var name in new[] { "roundhouse_low", "roundhouse_mid", "roundhouse_high", "front_mid" })
        {
            var tunes = KickTunes.Load(name);
            foreach (var t in new[] { tunes.Rear, tunes.Front })
            {
                Assert.IsTrue(float.IsFinite(t.Standoff) && t.Standoff is > 0.5f and < 2f, name);
                Assert.IsTrue(float.IsFinite(t.Seconds) && t.Seconds is > 0.2f and < 1.5f, name);
            }
        }

        var probe = new KickTunes(new KickTune(1f, 1f, 0f, 0f, 0f, 0f), new KickTune(2f, 2f, 0f, 0f, 0f, 0f));
        Assert.AreEqual(2f, probe.For(Hand.Left).Standoff, "the lead (left by default) leg is the front foot");
        Assert.AreEqual(1f, probe.For(Hand.Right).Standoff);
        Assert.AreEqual(2f, probe.For(Hand.Right, Hand.Right).Standoff, "with the right hand leading, the right leg is the front foot");
        Assert.AreEqual(1f, probe.For(Hand.Left, Hand.Right).Standoff);
    }

    [TestMethod]
    public void TheFrontFootKick_MovesTheHipAwayFromTheTarget_AndTheRearFootKick_MovesItCloser()
    {
        // 左右の差の原因（docs/arena/kick_shin_lmh_cause.md）: 体は軸足を軸に回る。前の足（左）で蹴ると軸足は後ろの足で股が狙いから遠ざかり、
        // 後ろの足（右）で蹴ると軸足が前の足で股が狙いへ近づく。蹴る脚の指令は左右で鏡になっているので、差は構えの違いだけから出る。
        var spec = KickSpec.Of(KickTechnique.RoundhouseLow);
        var baseline = KickShin.Baseline(KickShin.Heights[0]);
        spec = spec with { Tunes = new KickTunes(baseline, baseline) };
        var change = new Dictionary<Hand, float>();
        foreach (var side in new[] { Hand.Left, Hand.Right })
        {
            var start = 0f;
            var atContact = 0f;
            KickMocap.Doll(BodyBuild.Standard, spec, side, baseline.Standoff, baseline.Seconds, 1, null, (game, leg, t) =>
            {
                var a = game.Player;
                var aim = spec.AimWorld(game.PlayerSense);
                var hip = a.HipPosition(side);
                var d = Vector2.Distance(new Vector2(hip.X, hip.Z), new Vector2(aim.X, aim.Z));
                if (t == 0f)
                {
                    start = d;
                }

                if (MathF.Abs(t - 0.36f) < 0.003f)
                {
                    atContact = d;
                }
            });
            change[side] = atContact - start;
        }

        Assert.IsGreaterThan(0.03f, change[Hand.Left], "the front-foot kick pushes the hip away from the target");
        Assert.IsLessThan(0f, change[Hand.Right], "the rear-foot kick brings the hip closer to the target");
    }
}
