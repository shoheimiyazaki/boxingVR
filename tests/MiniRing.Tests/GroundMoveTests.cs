using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>倒れた相手への攻めの経路を教科書（<see cref="BotStyle.Ground"/>）に持たせる（2026-11-17、LogicVersion 42）。</summary>
[TestClass]
public sealed class GroundMoveTests
{
    [TestMethod]
    public void GroundPaths_RoundTripThroughTheStyleJson()
    {
        var ground = GroundTuning.Default with { Stomp = GroundTuning.Default.Stomp with { Rise = 0.9f, StanceLean = 0.25f } };
        var style = BotCatalog.Karate with { Ground = ground };
        var back = BotStyle.FromJson(style.ToJson());
        Assert.IsNotNull(back.Ground);
        Assert.AreEqual(ground.Stomp, back.Ground.Stomp);
        Assert.AreEqual(ground.PunchDown, back.Ground.PunchDown);
    }

    [TestMethod]
    public void BadGroundPaths_AreClampedWhenTheStyleIsValidated()
    {
        // 練習の出力を JSON へ写し間違えても、試合で止まらない（長さ 0 の技・負の沈みの上限・数でない値）。
        var bad = GroundTuning.Default with { PunchDown = GroundTuning.Default.PunchDown with { Duration = 0f, MaxSink = -1f, Rise = float.NaN } };
        var style = (BotCatalog.Karate with { Ground = bad }).Validated();
        var move = style.Ground!.PunchDown;
        Assert.IsGreaterThan(0f, move.Duration);
        Assert.IsGreaterThanOrEqualTo(0f, move.MaxSink!.Value);
        Assert.AreEqual(GroundTuning.Default.PunchDown.Rise, move.Rise, "a non-number falls back to the default path");
    }

    [TestMethod]
    public void KickWeightShift_IsInTheKarateAndTaekwondoTextbooks()
    {
        // kickbalance（docs/TAEKWONDO.md「軸足に乗って蹴る」）で採った教育の値。ほかのスタイルは今までどおり（0）。
        Assert.AreEqual(0.35f, BotCatalog.Karate.KickShift, 1e-6f);
        Assert.AreEqual(0.3f, BotCatalog.Taekwondo.KickLeanForward, 1e-6f);
        Assert.AreEqual(0f, BotCatalog.Boxer.KickShift);
    }
}
