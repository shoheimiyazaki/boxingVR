using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調査用（スイートの外）: 後ろ蹴り・かかと落としの寄る距離を掃いて、当たり・立ったままかを書く。2026-11-20 に <see cref="KickContactTests"/> から移した。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class KickContactProbe
{
    [TestMethod]
    public void CloseInSweep_Probe()
    {
        foreach (var kind in new[] { KickTechnique.Back, KickTechnique.Axe })
        {
            foreach (var c in new[] { -0.1127f, -0.05634f, 0f, 0.02817f, 0.05634f, 0.08451f, 0.1127f, 0.169f, 0.2254f })
            {
                var r = KickContactTests.Run(kind, closeIn: c);
                Console.WriteLine($"SWEEP {kind} closeIn {c:F2}: hits {r.Hits}, parts [{string.Join(',', r.Parts)}], speed {r.MaxSpeed:F2}, ankle {r.MinAnkleToTarget:F3}, pelvis {r.StartPelvis:F3}->{r.MinPelvis:F3}, {r.AttackerState}, yaw {r.MaxYawDeviation:F3}");
            }
        }
    }
}
