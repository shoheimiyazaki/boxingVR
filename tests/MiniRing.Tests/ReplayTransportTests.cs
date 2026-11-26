using MiniRing.Replay;

namespace MiniRing.Tests;

/// <summary>リプレイの再生ヘッド（<see cref="ReplayTransport"/>）: スロー・巻き戻し・コマ送り・シーク・A-B 区間。</summary>
[TestClass]
public sealed class ReplayTransportTests
{
    [TestMethod]
    public void Playing_AdvancesAtTheChosenSpeed_AndWaitsForTheScan()
    {
        var t = new ReplayTransport(10f);
        t.Advance(1f, 10f);
        Assert.AreEqual(1f, t.Time, 1e-5f);
        t.SetSpeed(0.25f);
        t.Advance(1f, 10f);
        Assert.AreEqual(1.25f, t.Time, 1e-5f, "スロー");
        t.SetSpeed(4f);
        t.Advance(1f, 3f);
        Assert.AreEqual(3f, t.Time, 1e-5f, "走査が済んだ所で待つ");
        Assert.IsTrue(t.Playing, "待っている間も再生は続く");
        t.Advance(10f, 10f);
        Assert.AreEqual(10f, t.Time, 1e-5f);
        Assert.IsFalse(t.Playing, "終わりで止まる");
        t.TogglePlay();
        Assert.AreEqual(0f, t.Time, "終わりで再生すると頭から");
    }

    [TestMethod]
    public void Rewinding_RunsBackwards_AndShuttleSpeedsUp()
    {
        var t = new ReplayTransport(10f);
        t.Seek(5f, 10f);
        t.ShuttleBackward();
        Assert.AreEqual(-1f, t.Rate);
        t.Advance(1f, 10f);
        Assert.AreEqual(4f, t.Time, 1e-5f);
        t.ShuttleBackward();
        Assert.AreEqual(-2f, t.Rate, "もう一度押すと速く");
        t.Advance(5f, 10f);
        Assert.AreEqual(0f, t.Time);
        Assert.IsFalse(t.Playing, "頭で止まる");
        t.ShuttleForward();
        Assert.IsTrue(t.Rate > 0f);
    }

    [TestMethod]
    public void FrameStepping_PausesAndMovesOneFrame()
    {
        var t = new ReplayTransport(10f);
        t.Seek(2f, 10f);
        t.StepFrames(1, 10f);
        Assert.IsFalse(t.Playing);
        Assert.AreEqual(2f + ReplayTransport.FrameStep, t.Time, 1e-5f);
        t.StepFrames(-3, 10f);
        Assert.AreEqual(2f - (2 * ReplayTransport.FrameStep), t.Time, 1e-5f);
        t.Advance(1f, 10f);
        Assert.AreEqual(2f - (2 * ReplayTransport.FrameStep), t.Time, 1e-5f, "止まっている間は動かない");
    }

    [TestMethod]
    public void LoopBetweenAandB_Repeats_InBothDirections()
    {
        var t = new ReplayTransport(10f);
        t.Seek(2f, 10f);
        t.MarkLoopStart();
        t.Seek(3f, 10f);
        t.MarkLoopEnd();
        Assert.IsTrue(t.Looping);
        Assert.AreEqual(2f, t.Time, "B を置くと A へ戻る");
        t.Advance(1.25f, 10f);
        Assert.AreEqual(2.25f, t.Time, 1e-4f, "B を越えたら A から");
        t.ShuttleBackward();
        t.Advance(0.5f, 10f);
        Assert.AreEqual(2.75f, t.Time, 1e-4f, "巻き戻しは A を越えたら B から");
        t.ClearLoop();
        Assert.IsFalse(t.Looping);
    }

    [TestMethod]
    public void Clock_RoundsBeforeSplittingMinutes()
    {
        Assert.AreEqual("1:00.0", MiniRing.Presentation.ReplayHud.Clock(59.96f));
        Assert.AreEqual("0:05.2", MiniRing.Presentation.ReplayHud.Clock(5.2f));
    }

    [TestMethod]
    public void Seek_ClampsToTheMatch()
    {
        var t = new ReplayTransport(10f);
        t.Seek(-3f, 10f);
        Assert.AreEqual(0f, t.Time);
        t.Seek(30f, 6f);
        Assert.AreEqual(6f, t.Time, "走査が済んだ所まで");
        t.Seek(float.NaN, 6f);
        Assert.AreEqual(0f, t.Time);
    }
}
