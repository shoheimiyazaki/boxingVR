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

        // 2026-11-25（PhysicsVersion 105）: 中央値は試合の前の方の 1000 歩で見る。試合が進むと斬られた破片が増え、その扱いの割り当てが増える（切っ先を目標へ合わせる
        // 手首の力で剣が当たるようになり、最後の 500 歩の中央値は 66 → 80 KB。最初の 500 歩はどちらも 20.5 KB）。割り当ての上限は 1 歩の処理の重さを見るもので、
        // 試合の結果（どれだけ斬れたか）で変わる量は別に見る（docs/plans/FORM_DECAY_STUDY.md §10 の残り）。
        var early = allocations.Take(1000).ToList();
        var median = early.Order().ElementAt(early.Count / 2);
        Assert.IsTrue(median < MedianAllocationLimit, $"1 歩の割り当ての中央値が {median} byte（上限 {MedianAllocationLimit}）");
    }
}
