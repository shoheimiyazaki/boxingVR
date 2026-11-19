using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>中継のバイタルモニター（見た目だけ）: 値が体のモデルから写されていること。</summary>
[TestClass]
public sealed class VitalsMonitorTests
{
    [TestMethod]
    public void PressureBeatPeaksEarlyAndFallsWithANotch()
    {
        Assert.AreEqual(1f, VitalsMonitor.PressureShape(0.12f), 1e-3f);
        Assert.IsTrue(VitalsMonitor.PressureShape(0.05f) < VitalsMonitor.PressureShape(0.12f));
        Assert.IsTrue(VitalsMonitor.PressureShape(0.9f) < VitalsMonitor.PressureShape(0.3f));
        // 重複切痕: 0.42 の所が、その少し前より持ち上がる。
        Assert.IsTrue(VitalsMonitor.PressureShape(0.42f) > VitalsMonitor.PressureShape(0.36f));
    }

    [TestMethod]
    public void RestedFighterShowsCalmVitalsAndTheHeartBeats()
    {
        using var game = new BoxingGame(seed: 1);
        for (var i = 0; i < 240; i++)
        {
            game.Step(default, 1f / 240f);
        }

        var f = game.Player;
        Assert.IsTrue(f.HeartRate >= Fighter.RestPulse && f.HeartRate < 120f, $"HR {f.HeartRate}");
        Assert.IsTrue(VitalsMonitor.SpO2(f) > 95f, $"SpO2 {VitalsMonitor.SpO2(f)}");
        Assert.IsTrue(f.HeartPhase > 0.5f, "1 秒で 1 拍ほど進む");
        Assert.IsTrue(VitalsMonitor.ArterialPressure(f) >= 74f);
    }
}
