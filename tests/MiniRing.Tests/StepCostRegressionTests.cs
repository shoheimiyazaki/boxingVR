using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 1 歩の費用を減らす最適化（作業 13: 武器の長さの毎歩の全セル走査・候補セル配列の毎 Sweep の割り当て・LINQ）が、結果を変えず、
/// 割り当てが戻っていないことを確かめる。
/// </summary>
[TestClass]
public sealed class StepCostRegressionTests
{
    /// <summary>刀の試合（日本刀・両手持ち・<see cref="SwordBot.Basic"/> 同士）、シード 0 を 10 秒（240 Hz の 2400 歩）進めた最終チェックサム。作業 11・13 の前後で変わらない値。</summary>
    private const ulong KatanaSeed0Checksum = 0x09959a17dbf47803UL; // PhysicsVersion 31（直した頭 boxer.vox の組み込み）で更新。30 は 0xaaf619b2fb414c4f、それより前は 0xbf23ffc3e767a185。

    /// <summary>1 歩の割り当ての中央値の上限 [byte]。最適化前は 125 KB、後は 23 KB（刀の試合・Release）。戻ったら気づくための余裕付きの上限。</summary>
    private const long MedianAllocationLimit = 70_000;

    [TestMethod]
    public void KatanaMatch_KeepsItsChecksum_AndAllocatesLittlePerStep()
    {
        var corners = BoxingCorners.Default with
        {
            Player = SwordBot.Basic, Cpu = SwordBot.Basic,
            PlayerWeapon = WeaponKind.Katana, CpuWeapon = WeaponKind.Katana,
            PlayerTwoHanded = true, CpuTwoHanded = true,
        };
        using var game = new BoxingGame(seed: 0, corners: corners);
        var allocations = new List<long>(2400);
        for (var i = 0; i < 2400; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            game.Step(default, 1f / 240f);
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.AreEqual(KatanaSeed0Checksum, game.StateChecksum(), "最適化が試合の結果を変えた（決定的な再現が崩れた）");
        var median = allocations.Order().ElementAt(allocations.Count / 2);
        Assert.IsTrue(median < MedianAllocationLimit, $"1 歩の割り当ての中央値が {median} byte（上限 {MedianAllocationLimit}）");
    }
}
