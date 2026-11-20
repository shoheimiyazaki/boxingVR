
namespace MiniRing.Tests;

/// <summary>調査用（スイートの外）: 2 つのボットの拳で、はめる物ごとの削れ方を並べる（docs/VOXEL_WEAPONS.md の表）。2026-11-20 に <see cref="HandWearTests"/> から移した。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class HandWearProbe
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>調べもの: 2 つのボットの拳で、はめる物ごとの削れ方を並べる（docs/VOXEL_WEAPONS.md の表）。</summary>
    [TestMethod]
    public void HandWearBoutProbe()
    {
        foreach (var bot in new[] { "champion", "swarmer" })
        {
            foreach (var wear in HandWearTests.All)
            {
                TestContext.WriteLine($"{bot,-9} {wear,-14} {HandWearTests.Bout(wear, bot)}");
            }
        }
    }
}
