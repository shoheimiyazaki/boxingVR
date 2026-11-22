# 拳の物理: テスト前後比較

allは前後ともRelease、同じrunsettings（3クラス並列）。beforeはHEADのFighter/StickReviewを隔離ビルドし、追加したArmSpeedLimitTests 2件を除外。Probe/visualは前後とも除外。fastは初期調査のDebug-before/Release-afterで、同条件の最終判定はallを使う。

| 段 | 総数 | 合格 | 失敗 | スキップ／未確定 |
|---|---:|---:|---:|---:|
| fast-before | 622 | 615 | 7 | 0 |
| fast-after | 622 | 615 | 7 | 0 |
| all-before | 1283 | 1248 | 30 | 5 |
| all-after | 1285 | 1248 | 31 | 6 |

## fast

新しい失敗:

なし。

合格へ変わった失敗:

なし。

失敗から未実行・未確定へ（改善扱いにしない）:

なし。

前後共通の失敗:

- `MiniRing.Tests.KarateFistTests.SameBlow_KnucklesConcentrateDamageComparedWithPalm_AndHurtTheirOwnSkin [SameBlow_KnucklesConcentrateDamageComparedWithPalm_AndHurtTheirOwnSkin (13.43)]`
- `MiniRing.Tests.NeckMuscleTests.ABracedNeck_HoldsMoreOfTheGloveBlowThatBentItClosest_ThanASlackOne`
- `MiniRing.Tests.NeckMuscleTests.AFaintedNeck_IsWeakerThanABracedOne`
- `MiniRing.Tests.NeckMuscleTests.ANeckWithItsMuscleCarvedAway_IsWeaker`
- `MiniRing.Tests.NeckMuscleTests.MuscleInTheForearm_HoldsSomeOfTheBending_LikeTheNeck`
- `MiniRing.Tests.NeckMuscleTests.OnlyOneSidesMuscle_Holds_AndTheSideFlipsWithThePush`
- `MiniRing.Tests.StepTests.AStepForward_CoversMoreGroundThanWalking_AndPeaksFaster`

## all

新しい失敗:

- `MiniRing.Tests.BootSnagTests.WalkingDiagonallyOnTheCanvas_NeverStallsAndFinishesTheCourse`
- `MiniRing.Tests.ClubSwingTests.AClubBlow_CrushesMoreOfTheBody_ThanAPunchCarvesAtMost`
- `MiniRing.Tests.KneeControlTests.Roundhouse_AtItsStandoff_LandsWithTheLowerShinOrAnkle_AcrossSeedsAndBothLegs`
- `MiniRing.Tests.StepCostRegressionTests.KatanaMatch_AllocatesLittlePerStep`
- `MiniRing.Tests.TonfaTests.ASwordSweepingAtTheGuard_MeetsTheWoodOfTheStick_BeforeTheFleshOfTheForearm`
- `MiniRing.Tests.WeaponGraspTests.ReleasingTheSecondHand_LeavesAOneHandedHold_AndGrippingNearTheHandleJoinsAgain`

合格へ変わった失敗:

- `MiniRing.Tests.BojutsuTests.TheForwardForms_HitWithTheFrontEnd_AndGaeshiAlsoWithTheRear`
- `MiniRing.Tests.CmaTuneTests.Punch_CounterDamage_IsPositiveWithCountersAndZeroWithout`
- `MiniRing.Tests.PredictionTests.PredictedInsideSlip_AvoidsALoadedRightCross_ThatReactionCannot`
- `MiniRing.Tests.SwordTests.AHeldSword_CutsThePartItIsAimedAt`

失敗から未実行・未確定へ（改善扱いにしない）:

- `MiniRing.Tests.KickContactTests.TurningKicks_TurnTheBody_AndLandWithTheFootPartTheirPathPoints` → NotExecuted

前後共通の失敗:

- `MiniRing.Tests.BoneBreakTests.AGloveBlow_BreaksTheForearmBone_OnlyWhenItIsHardAndHasTheArmBehindIt`
- `MiniRing.Tests.CharacterTests.MuscleLoss_WeakensTheAffectedPart_AndRegenerationRestoresIt`
- `MiniRing.Tests.CmaTuneTests.DrillMeter_CountsPunchAndBaseline`
- `MiniRing.Tests.GrapplingTests.SasaeTsurikomiAshi_BreaksAStandingOpponentsBalance_ByPhysicsAlone`
- `MiniRing.Tests.HighlightRegressionTests.Excitement_StaysAboveTodaysFloor`
- `MiniRing.Tests.JointBridgeTests.ABladeThroughTheBaseOfATippedNeck_TakesTheHeadOff_OnlyWithTheBridge`
- `MiniRing.Tests.JointBridgeTests.ABladeThroughTheOpenedKneeGap_CutsTheKnee_OnlyWithTheBridge`
- `MiniRing.Tests.KarateFistTests.SameBlow_KnucklesConcentrateDamageComparedWithPalm_AndHurtTheirOwnSkin [SameBlow_KnucklesConcentrateDamageComparedWithPalm_AndHurtTheirOwnSkin (13.43)]`
- `MiniRing.Tests.LegLossTests.BothLegsGone_SitsOnThePelvis_AndIsNotCountedDown`
- `MiniRing.Tests.LegLossTests.TheLegBones_AreAsThickAtTheJointsAsInTheShaft_SoATouchNearTheKneeDoesNotSnapThem`
- `MiniRing.Tests.LegLossTests.WithoutTheShortcut_BothLegsGone_CannotSitBackUp_AndIsCountedOut`
- `MiniRing.Tests.NeckMuscleTests.ABracedNeck_HoldsMoreOfTheGloveBlowThatBentItClosest_ThanASlackOne`
- `MiniRing.Tests.NeckMuscleTests.AFaintedNeck_IsWeakerThanABracedOne`
- `MiniRing.Tests.NeckMuscleTests.ANeckWithItsMuscleCarvedAway_IsWeaker`
- `MiniRing.Tests.NeckMuscleTests.MuscleInTheForearm_HoldsSomeOfTheBending_LikeTheNeck`
- `MiniRing.Tests.NeckMuscleTests.OnlyOneSidesMuscle_Holds_AndTheSideFlipsWithThePush`
- `MiniRing.Tests.NeckSpineTests.TheSpine_AddsNoCellsAndNoMass_OnlyReplacesInnerMaterials`
- `MiniRing.Tests.NeckSpineTests.ThinnedNeckBone_BreaksUnderASideBlow_ThatAnIntactNeckHolds`
- `MiniRing.Tests.ShieldTests.TheShield_TakesTheSwordsSwing_AndTheBodyBehindItLosesLessThanWithTheShieldDown`
- `MiniRing.Tests.StaminaBudgetTests.EmptyReserve_FirstPunchIsWeak_RecoversAtSustainablePace_WhileFullReserveDoesNotFade`
- `MiniRing.Tests.StaminaRushTests.AlternatingRush_FullForTenSeconds_ThenFadesOverTheNextTen`
- `MiniRing.Tests.StepTests.AStepForward_CoversMoreGroundThanWalking_AndPeaksFaster`
- `MiniRing.Tests.VoxelDamageTests.CarvedFlesh_WeakensTheArm_ButDoesNotDisableIt`
- `MiniRing.Tests.WeaponGraspTests.ReleasingTheFirstHand_LeavesTheOtherHandHoldingTheSwordAsItWas`
- `MiniRing.Tests.WeaponTests.TheSwordsEdge_CutsIntoTheForearm_ButItsFlat_IsStopped`
