using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>調査用（スイートの外）: こんぼうの振り下ろしの間合いを掃いて、当たり・潰れたセルを書く。2026-11-20 に <see cref="ClubSwingTests"/> から移した。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class ClubSwingProbe
{
    [TestMethod]
    public void RangeSweep_Probe()
    {
        foreach (var range in new[] { 0.30f, 0.32f, 0.34f, 0.36f, 0.38f, 0.40f, 0.42f, 0.44f })
        {
            var r = SwingTest.Run(WeaponSwing.Default(SwingFamily.Chop), range, (game, _, _) =>
            {
                game.Player.Stamina = 1f;
                game.Cpu.Stamina = 1f;
            }, gloves: false);
            Console.WriteLine($"RSWEEP range {range:F2}: landed {r.Landed} cells {r.Cells}");
        }
    }
}
