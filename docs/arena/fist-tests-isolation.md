# 新規失敗と共有集計の再確認

全て同じRelease。allocationとmeterは1メソッドずつの実行。regressionsは残る新規5件だけを含む実行。製品・既存テスト・期待値の変更なし。

| 対象 | 版 | Passed | Failed | NotExecuted |
|---|---|---:|---:|---:|
| single-allocation | before | 1 | 0 | 0 |
| single-allocation | after | 0 | 1 | 0 |
| single-meter | before | 1 | 0 | 0 |
| single-meter | after | 1 | 0 | 0 |
| regressions | before | 5 | 0 | 0 |
| regressions | after | 0 | 5 | 0 |

## KatanaMatch_AllocatesLittlePerStep (after)

```text
Assertion failed. 予想される条件は true です。
1 歩の割り当ての中央値が 109512 byte（上限 70000）

actual: false

Assert.IsTrue(median < MedianAllocationLimit)
```

## AClubBlow_CrushesMoreOfTheBody_ThanAPunchCarvesAtMost (after)

```text
Assertion failed. 下限より大きい値が必要です。
a hard club crushes the flesh it strikes

lower bound: 42
actual:      37

Assert.IsGreaterThan(FighterTuning.Default.MaxCarve * 0.35f, r.Cells)
```

## Roundhouse_AtItsStandoff_LandsWithTheLowerShinOrAnkle_AcrossSeedsAndBothLegs (after)

```text
Assertion failed. 値は等しい必要があります。
the kicker is still standing

expected: Standing
actual:   GettingUp

Assert.AreEqual(FighterState.Standing, attacker.State)
```

## ReleasingTheSecondHand_LeavesAOneHandedHold_AndGrippingNearTheHandleJoinsAgain (after)

```text
Assertion failed. 値は等しい必要があります。
the left hand grips again

expected: Left
actual:   null

Assert.AreEqual(Hand.Left, game.Player.SupportOf(Hand.Right))
```

## ASwordSweepingAtTheGuard_MeetsTheWoodOfTheStick_BeforeTheFleshOfTheForearm (after)

```text
Assertion failed. 値が上限以下であることが必要です。
the wood in front of the forearm takes the blow the flesh would have

upper bound: 0
actual:      2

Assert.IsLessThanOrEqualTo(glove.Forearm, tonfa.Forearm)
```

## WalkingDiagonallyOnTheCanvas_NeverStallsAndFinishesTheCourse (after)

```text
Assertion failed. 上限より小さい値が必要です。
circle (seed 1): stalled 1.87 s over 2 events

upper bound: 1.678
actual:      1.8708334

Assert.IsLessThan(1.678f, r.StallSeconds)
```
