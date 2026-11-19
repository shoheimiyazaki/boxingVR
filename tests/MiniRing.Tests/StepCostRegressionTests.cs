using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 1 歩の費用を減らす最適化（作業 13: 武器の長さの毎歩の全セル走査・候補セル配列の毎 Sweep の割り当て・LINQ）が、結果を変えず、
/// 割り当てが戻っていないことを確かめる。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class StepCostRegressionTests
{
    /// <summary>刀の試合（日本刀・両手持ち・<see cref="SwordBot.Basic"/> 同士）、シード 0 を 10 秒（240 Hz の 2400 歩）進めた最終チェックサム。作業 11・13 の前後で変わらない値。</summary>
    private const ulong KatanaSeed0Checksum = 0xca3bab4d7dff7154UL; // 59/57: master の顔・手の層とこの枝のつなぎ・滑りの直しのマージで更新（前は 0xb18db50e86bba51c）。以前の注: PhysicsVersion 57（頭の顔の層: 頭の当たりの形・削れ方が変わる。claude/face-f2、claude/face-f3 の名簿の顔を合わせた後）で更新。前は 0x1ac3ce4297248530: PhysicsVersion 50（master の 49 と投げ技の枝の 48 を合わせた）で更新。47 は 0xcfd780bf77758764: PhysicsVersion 47（ブランチ claude/rush-physics の体の運びを合わせた）で更新。前は 0x4f44863caa6d2efd: PhysicsVersion 44（剣の柄に鉄の芯を通した: 刀の質量・柄の材質が変わる）で更新。40 は 0x089d46963c40be9c: PhysicsVersion 40（脚の骨の端を骨の胴と同じ太さにした: 腿・すねのセルの数・質量が変わる）で更新。39 は 0x06a0ecc721ed5573（PhysicsVersion 34 と同じ。脚を失わない試合）。34（首の骨・脊髄のセルを足した: 無傷のセルの数・質量は同じだが、背骨の柱の真ん中の骨が脊髄（弱い・肉の組織）になったので、胴を斬る刃の接触と胸・腰の肉の残りの数え方が変わる）で更新。33 は 0xf410d6eee99181a0（PhysicsVersion 32 と同じ）。32（スタミナの回復を実際にした仕事で数える・容量 60 J）で更新。31 は 0x09959a17dbf47803、30 は 0xaaf619b2fb414c4f、それより前は 0xbf23ffc3e767a185。

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
