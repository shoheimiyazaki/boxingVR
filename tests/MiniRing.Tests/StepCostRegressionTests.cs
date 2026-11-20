using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 1 歩の費用を減らす最適化（作業 13: 武器の長さの毎歩の全セル走査・候補セル配列の毎 Sweep の割り当て・LINQ）の割り当てが戻っていないことを確かめる。
/// 2026-11-20: 刀の試合の最終チェックサムの金型（版ごとに取り直していた）はやめた。最適化が結果を変えないこと（決定性）は ReplayTests・FixedSimulationTests が見る。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class StepCostRegressionTests
{
    /// <summary>1 歩の割り当ての中央値の上限 [byte]。最適化前は 125 KB、後は 23 KB（刀の試合・Release）。戻ったら気づくための余裕付きの上限。</summary>
    private const long MedianAllocationLimit = 70_000;

    [TestMethod]
    public void KatanaMatch_AllocatesLittlePerStep()
    {
        var corners = BoxingCorners.Default with
        {
            Player = SwordBot.Basic, Cpu = SwordBot.Basic,
            PlayerWeapon = WeaponKind.Katana, CpuWeapon = WeaponKind.Katana,
            PlayerTwoHanded = true, CpuTwoHanded = true,
        };
        using var game = new BoxingGame(seed: 0, corners: corners);
        var allocations = new List<long>(4028);
        for (var i = 0; i < 4028; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            game.Step(default, 1f / 240f);
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        }

        var median = allocations.Order().ElementAt(allocations.Count / 2);
        Assert.IsTrue(median < MedianAllocationLimit, $"1 歩の割り当ての中央値が {median} byte（上限 {MedianAllocationLimit}）");
    }
}
