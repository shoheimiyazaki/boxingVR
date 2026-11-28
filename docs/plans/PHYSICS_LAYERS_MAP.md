# 物理の層の地図（今の状態。変わっていく物）

2026-11-28、master 7b97599d（物理 107・判断 77）の上で読んだ。憲法は [docs/PHYSICS_CONSTITUTION.md](../PHYSICS_CONSTITUTION.md)、作り直しの段は [PHYSICS_FOUNDATION_PLAN.md](PHYSICS_FOUNDATION_PLAN.md)（以下「計画」）。コードは読んだだけで変えていない。重さ: **大** = 自由な体（P3・P4）を直接妨げる、中 = 一つの原則を局所で破る、小 = 整理。

---

## 1. 今のファイルを層に割り当てる

| 層 | 主なファイル・クラス・関数 | 混ざり方 |
|---|---|---|
| L0 | `src/Aigis.Physics/PhysicsWorld*.cs`（`Contacts`・`Joints`・`Queries`・`Probes`・`Observation`）、`BepuCallbacks`・`CollisionFilter`・`PairExclusions`・`ContactPairBudget`、`src/Aigis.Foundation`、`Replay/` の固定ステップ | `PhysicsWorld.Wheels.cs` は乗り物（L0 には不要だが無害） |
| L0 に居る L2 | `src/Aigis.Physics/HumanoidRagdoll*.cs`・`Ragdoll.cs`・`SkeletonState.cs` | **L0 のアセンブリが人体を知っている**（V1） |
| L1 | `src/Aigis.Physics/Voxels/VoxelContact.cs`（圧力のモデル）・`VoxelBending.cs`（曲げ折れ）・`VoxelBody.cs`・`VoxelClumps.cs`、材料の値は `games/MiniRing/Voxels/BoxerAnatomy.cs`（`SkinMechanics`・`MuscleMechanics`… と `Doll` の相似）、`Fighter.Ligaments.cs`（肘の靭帯の降伏）、`Weapons/Weapon*.cs` の材質 | 材料の表がゲームの体の解剖のファイルに混ざる（V6） |
| L2 | `Fighter.Joints.cs`・`Fighter.Split.cs`・`Voxels/BoxerBody.Split.cs`・`JointBridge.cs`・`BodyMassTable.cs`・`Characters/BodySizing.cs`、`BoxingGame.cs` の `ApplyHit`（運動量の受け渡し）・`BoxingGame.Canvas.cs`（床の衝撃） | 体のグラフが無く、部位は名前と数が固定（`HumanoidRagdoll` の `ArmChain`/`LegChain`、`BodyPartKind`） |
| L3a | `ArmMuscle.cs`・`StandardBodyStrength.cs`・`Fighter.Stamina.cs`・`Voxels/TissuePhysiology.cs`・`BodyCondition.cs`・`BoxerBody.cs`（臓器・死）・`Fighter.TwistHead`（意識）、`Voxels/BoxerSpine.cs` | 部位 5 つの酸素の蓄え（DEBT 47） |
| L3b | `Fighter.cs`（目標の追従・`ApplyHover` 等）・`Fighter.JointDrive.cs`・`Fighter.Balance.cs`・`Fighter.Support.cs`・`Fighter.Step.cs`・`Fighter.Quadruped.cs`・`Fighter.Grapple.cs`・`Footwork.cs`・`ArmIk.cs`・`LegIk.cs`・`SupportPolygon.cs`・`StandGuard.cs`、`PunchMechanics.cs`・`Fighter.Impact.cs`（本来 L2 の結果であるべき有効質量の式） | `Fighter.cs` は 3000 行超で L2・L3a・L3b が一つのクラス |
| L3c | `VisionPerception.cs`・`Voxels/EyeLook.cs`、`FighterSense`（`FighterCommand.cs`）の組み立て（`BoxingGame.cs`） | 感覚の出口に真の値が混ざる（V7） |
| L4 | `BodyBuild.cs`・`Characters/*`（`CharacterFile`・`EducationResolver`・`roster/*.json`）・`Voxels/Face*`・`Bots/*.json`（スタイル） | `BodyBuild.FourLegged` が寸法の分岐を持つ（V2） |
| L5 | `Bots/*`（`BoxerBot`・`TechniqueExecution`・`OpponentRead`・`DefenseBook`・`PunchBook`）・`IntentControl.cs`・`PuppetMapping.cs`・`Motion/VrPlayerMapper.cs`・`AiControl.cs` | 出力は `FighterCommand`（守れている）。入力は `FighterSense` |
| L6 | `BoxingGame*.cs`（ラウンド・`Corners`・`Break`・`Royale`・`Weapons` の支給）・`RingLayout.cs`・`HexCage.cs`・`LivingRoom.cs`（当たり）・`games/MiniRing.Arena/Orgs/*.json` | `BoxingGame.cs` が L2（`ApplyHit`）と L6 を一緒に持つ |
| 外 | `BoxingRenderer.cs`・`Presentation/`・`Audio/`・`TvHud.cs`・`FaceCam.cs`・`ArenaLook.cs`・`RoomAssets.cs`・`HitLook.cs`・`TiredLook.cs`・`Highlights/` | `TiredLook` は見た目の手がかり（外）で良い |

## 2. 約束を破っている所（重さ順）

| # | 破っている約束 | どこ | 重さ |
|---|---|---|---|
| V1 | 外の力（P4・§2.3）: 骨盤のホバー・起こすねじり・体を運ぶ力・体を足の上へ | `Fighter.ApplyHover`（Fighter.cs:3202）・`ApplyUprightImpulse`（3460）・`ApplyFacingAndUpright`（3247）・`ApplyBodyOverFeet`（2828）・`ApplyMovement`（3562）、`FighterTuning.HybridPunch`・`HipDrive` | **大** |
| V2 | 体の種類の分岐（P3）: `FourLegged` が 20 ファイル・約 60 か所（`Fighter.cs` 12・`BodyBuild` 10・`BodyMassTable` 6・`BoxerBody` 5・`CharacterMaker` 5・`BoxerBot` 2・`VrPlayerMapper` 1…）、`Fighter.Quadruped.cs` | 全層に散る | **大** |
| V3 | L0 が人体を知る（§2.1）: `HumanoidRagdoll`（`ArmChain`・`LegChain`・`AddLegServos`・`HipPivotOffsetX`・`ChestFrontPanel*`）が `Aigis.Physics` に | `src/Aigis.Physics/HumanoidRagdoll*.cs` | **大** |
| V4 | 一つの現象に二つの式（P6）: 拳の重さを `PunchMechanics.ImpactMass`・`Fighter.ImpactMass` の有効質量の式で出し、接触の解の力積と別 | `PunchMechanics.cs`・`Fighter.Impact.cs` | **大** |
| V5 | L6 が体を直接書き換える（§2.2）: 休みの再生が `fighter.Voxels?.Regenerate` を団体の値 `RegenVoxelsPerSecond` で直接呼ぶ | `BoxingGame.cs:1574-1581`・`MatchRules` | 中 |
| V6 | 見えない倍率（P1）: 材料に相似の倍率 `BoxerAnatomy.Doll`（応力 × 0.4033 …）。材料の表がゲームの解剖のファイルに | `Voxels/BoxerAnatomy.cs:111-123`、DEBT 9 | 中 |
| V7 | 感覚を通らない真の値（§2.2 L3c → L5）: `FighterSense` が相手の頭・胸・肩・拳の世界の位置、`OpponentInjury`・`OpponentTargets`・`OpponentBuild` を目の状態と無関係に渡す（`VisionState` は一部だけ） | `FighterCommand.cs:133`、組み立ては `BoxingGame.cs` | 中 |
| V8 | 足運びが 2 本脚前提（P3） | `Footwork.cs`（`_swinging` / `1 - _swinging`・`_ball[2]`） | 中（V2 と一緒に消える） |
| V9 | 反作用の無い腕の力（P4）: 腕の PD を世界から | `Fighter.DriveArms`、DEBT 29 | 中 |
| V10 | 層の混ざり（§1）: `Fighter.cs` が L2・L3a・L3b を、`BoxingGame.cs` が L2 の `ApplyHit` と L6 を一つのクラスに | 2 ファイル | 中（読み・テストの層分けを妨げる） |
| V11 | 関節の拘束が柔らかく開く（L0/L2 の物差し）: Bepu 既定の柔らかさ | DEBT 89 | 中 |
| V12 | 質量の近似（L2）: 重心 = 箱の中心・慣性は対角だけ | `BodyMassTable`・`BodySizing`、DEBT 22 | 小（だが全部の前提） |
| V13 | 起き上がり・倒れている間の手の近似の力（P4） | `Fighter.RiseOnLimbs`・`ApplyRiseLegs`・`PostOnTheCanvas` | 中 |
| V14 | 依存の向きのテストが Bepu だけ | `ArchitectureRulesTests`（時計・乱数と Bepu の参照だけを見る） | 小（仕組みの穴） |

守れている所: L5 の出力は `FighterCommand` だけ（ボットは `WorldToLocal` で読むだけで、剛体へ書かない）、Bepu の型は `Aigis.Physics` の外へ出ない、時計・種なし乱数のテストがある、記録の再生の決定性（`ReplayTests`）。

## 3. 近道の台帳（DEBT の物理・体の項目を層と原則へ。行番号は 2026-11-28）

| DEBT の項目（行） | 層 | 破る原則 | 段（計画 §4） |
|---|---|---|---|
| 重い物を持つ腕の共収縮（8） | L3b | P4 の境目（手首だけ別の力） | 3 |
| 実物大の相似の換算（9） | L1 | P1（見えない倍率） | L1 の試験片の後（REAL_SCALE） |
| キャラ: 重心・慣性・手の質量（22） | L2 | P6 | 1 |
| 腰の回りが拳に乗らない（27） | L3b/L2 | P4 | 3・5 |
| 拳に乗る体の質量（28） | L2 | P6（二つの式） | 5 |
| 拳を引く筋肉（29） | L3a | P4（反作用無し）・P1（筋が解剖でない） | 2・3 |
| 台本の技のぎりぎりの値（30） | L5 | —（下が動くたび測り直し） | 毎段 |
| 拳の打撃の削れ方（31） | L1/L2 | P6（`massBehind`） | 5 |
| 体を支える力（32） | L3b | **P4** | 3 |
| 試合用ハイブリッド（33） | L3b | **P4** | 3・5 |
| 縦の支持の見積もり（34） | L3b | P4 | 3 |
| 脚を失った体（36） | L3b | P4・P3 | 3・6 |
| 意識（41） | L3a | P2 寄り（脳の材料でなく量のモデル） | 残す（台帳） |
| 首と脊椎（42） | L1/L2 | P3（頭の箱の寸法） | 2 |
| ダウンの判定（43） | L6 | —（決まり。支えの連続量へ） | 6 |
| つながりで分かれる体（44） | L2 | P3（作った時の姿勢） | 2 |
| ステップ（46） | L3b | P4（容量を外から放つ） | 3・4 |
| スタミナと臓器（47） | L3a | P3（部位 5 つ） | 2 以降 |
| 骨の曲げの実効値（54） | L1 | P2（支えを見ない梁） | L1 の試験片 |
| 折れた骨の肉（55） | L1/L2 | P2（60 Hz の近似） | L1 の試験片 |
| 力の抜けていく手（56） | L3a | 手の値 | 残す |
| 倒れている間の手（58） | L3b | P4 | 6 |
| 起き上がる試みの拍子（59） | L3b/L5 | 意志のモデル | 残す |
| 曲げの判定の最適化（60） | L0 | P5 の保護 | 残す |
| 関節のつなぎ（66） | L1/L2 | P6（質量の無い箱） | 2 |
| 起き上がり（70） | L3b | **P4** | 6 |
| 床に落ちた衝撃（71） | L2 | P6（全身の運動量から逆算） | 5 |
| 釣り合いの PID（73） | L3b | 手の値 | 3 |
| 関節の靭帯（74） | L1 | P3（肘だけ）・P2（η が測定でない） | 2 |
| 腰から打つ体（86） | L3b | **P4** | 3 |
| 脚の関節の開き（89） | L0/L2 | 拘束の誤差の上限 | 2 |
| 鎌の向き（90）・HMD（91）・かかと（92）・腰の位相（93）・konrad（94） | L3b/L5 | 測り直し待ち | 3・4 |

DEBT にまだ無い物（地図で見つけた物）: V3（L0 の人体）、V5（L6 の直接の再生）、V7（感覚を通らない真の値）。DEBT に足すかはユーザーの判断。

## 4. 層ごとのテストの組（今と足りない物差し）

| 層 | 今あるテスト（例） | 足りない物差し |
|---|---|---|
| L0 | `FixedSimulationTests`・`ReplayTests`・`ArchitectureRulesTests`・`SimulationDefaultsIsolationTests`・`PerformanceTests` | エネルギーが増えない（落下・振り子・弾む球）、拘束の誤差の上限、依存の向き（名前空間の参照） |
| L1 | `VoxelContactTests`・`VoxelDamageTests`・`SubsurfaceCrushTests`・`BoneBreakTests`・`DamageModelTests`・`DeadSkinTests`・`MaterialStrengthProbe`（Probe） | **試験片の組**（引張・圧縮・3 点曲げ・切断・押し込み）を材料ごとに実物の範囲と相似で。`MaterialStrengthProbe` を常設のテストへ |
| L2 | `ArticulatedMassTests`・`VoxelMassTests`・`VoxelSplitTests`・`JointLayoutTests`・`JointBridgeTests`・`LegJointGapTests`・`ContactEventTests`・`AlienTests` | 空中の運動量・角運動量の保存、静置の反力 = 重さ（M2）、打撃の前後の運動量（M3）、**体の種類を問わない**同じテスト（M6） |
| L3a | `ArmMuscleTests`・`StandardBodyStrengthTests`・`StaminaBudgetTests`・`OxygenModelTests`・`TissuePhysiologyTests`・`ConcussionScaleTests`・`NeckMuscleTests` | 筋の仕事 ≤ 蓄えの減り、削れた断面 ∝ 上限の落ち、体格 k 倍の相似 |
| L3b | `FighterPhysicsTests`・`UprightBalanceTests`・`FootworkTests`・`StepTests`・`JointDriveTests`・`LimbDriveTests`・`SelfFallTests`・`DownRecoveryTests`・`IntentPhysicsTests` | **M1（外の力の合計）**、トルク ≤ 上限、M5（倒れる向き）、足の数を問わない立つ・歩く |
| L3c | `VisionTests`・`AttackPerceptionTests`・`EyeLookTests`・`RangeSenseTests` | 目を削る → 情報の遅れ・ぶれが単調、`FighterSense` に真の値が入らないことの型の検査 |
| L4 | `CharacterFileTests`・`CharacterRosterTests`・`CharacterSamenessTests` | 名簿の全員が L2・L3 の不変量を通る |
| L5 | `BotTests`・`DefenseTests`・`PunchBookTests`・`IntentControlTests`・`PairedEvaluationTests` | （足りている。下が動いたら測り直す） |
| L6 | `RoundRulesTests`・`OrganizationTests`・`MatchFlowTests`・`IvfTests`・`VfcCoreTests` | 決まりが体を直接書かないこと（V5 の後） |
| 外 | visual・`HitSoundTests` など | — |

`TestCategory("L0")`〜`("L6")` の印はまだ無い。付け方は `TestSuiteRulesTests` に足す（段 0 と一緒）。

## 5. 憲法に近づける順番（計画の段と合わせて）

物差しと L1 を先に置き、計画の段 0〜6 の前後へ層の整理を挟む。

| 順 | 中身 | 層 | 計画の段 | 物理の版 |
|---|---|---|---|---|
| 1 | **物差し**: M1〜M5 の記録（計画の段 0）＋ L0 のエネルギー・拘束誤差、層の印、依存の向きのテスト（V14） | L0〜L3b | 0 | 変えない |
| 2 | **L1 の試験片**: 材料ごとの引張・圧縮・曲げ・切断・押し込みを `src/Aigis.Physics/Voxels` の純関数の上で。実物の範囲・相似を記録（`Doll` の倍率の効きを数で見る） | L1 | 新（段 0 と並べて） | 変えない |
| 3 | 材料の表を `BoxerAnatomy` から L1 のデータへ分ける（値は同じ） | L1 | 新 | 変えない（チェックサム一致で確かめる） |
| 4 | 質量の正しさ（重心・慣性・手） | L2 | 1 | +1 |
| 5 | 体のグラフ・関節を硬く・`HumanoidRagdoll` を `Aigis.Physics` から外す（V3）・`FourLegged` を生成から消す | L2 | 2 | +1 |
| 6 | スパイク `TorqueOnlyStandProbe`（計画 §5）→ 支えの関節トルク化（V1・V9） | L3b | 3 | +1 |
| 7 | N 本の足運び（V2・V8） | L3b | 4 | +1 |
| 8 | 打撃の一本化（V4） | L2 | 5 | +1 |
| 9 | 倒れる・起き上がる（V13） | L3b | 6 | +1 |
| 10 | 感覚の出口を目の状態に通す（V7）、L6 の再生を L3a の式経由に（V5）、`Fighter.cs`・`BoxingGame.cs` を層で分ける（V10） | L3c・L6 | 新（3 以降いつでも） | 判断の版 / 物理 +1 |
| 11 | 相似の倍率（DEBT 9）を消す: 実物の材料の値で試験片と試合が成り立つか | L1 | REAL_SCALE | +1 |

2・3 を段 1 より前に置く理由: 段 1〜6 はすべて「材料が正しい」前提で測り直しになるので、L1 の物差しが先に無いと、後の段で威力・壊れ方がずれたとき原因が L1 か L2 か分からない（CLAUDE.md §6「全部を同時に変えて原因が分からない」）。

## 6. ユーザーに決めてほしいこと

1. 層の分け方（L3 を生理・運動制御・感覚の 3 つ、描画・音を外）でよいか。
2. 版を分けるか（`PhysicsVersion` を Material・Body に）。
3. V3・V5・V7 を DEBT に載せるか。
4. 感覚の出口（V7）をどこまで絞るか: 目の状態に通すと、今のボットの強さが落ちる（教育の合わせ直し）。
5. 順 2・3（L1 の試験片と材料の表の分離）を計画の段 1 より先に入れてよいか。
