# 駆動の DOE 作業ログ（物理の統合 段 3 の 1c）

指示書: `CODEX_BRIEF_PHYSICS_DRIVE_DOE.md`。順序は固定（設計 → 網羅データ → 駆動の関数から縮約モデル → 実測との比較 → 分析（コードを変える前に書く）→ 最小の変更 → ホールドアウト）。
コミットしない・テストを緩めない・ローカルのみ。生データは `docs/arena/drive/`。

## 1. 測定の設計

### 1.1 駆動の要素への分解（`Fighter.cs`・`ArmMuscle.cs`・`PunchMechanics.cs` から）

拳に力が届く経路（1 歩 1/240 s）:

1. 指令: `FighterCommand`（拳の目標・Spring・胴のひねり・踏み込み）。
2. PD（脳）: `HandAccel(glove, v, target, targetVelocity, HandKp, kd)`、kd は Spring で `LooseHandDamping` へ下がる。欲しい力 = 加速度 × 腕の質量（上腕 + 前腕 3.3 kg）。
3. 興奮: `ArmMuscle.Excitation(欲しい力, 上限)`。上限 = `MaxHandForce` × `ForceMultiplier`（連結度 × 体の有効質量 / 全質量 × `BodyDrive`）。
4. 活性化: `ArmMuscle.Activate`（立ち上がり `MuscleActivationTime`・緩み）。
5. 力: `ArmMuscle.Force` = 活性化 × 上限 × （肘の力の出しやすさ × Hill 速さの関係、肩の力の出しやすさ × Hill 速さの関係）。肘の速さの尺度は肘のてこ（伸びきりに近いほど小さく、速さの比が大きい）で変わる。
6. 前腕へ力積（上腕は肘の関節で、胸は肩の関節で連なる）、胸へ反作用（`HandReactionShare`・内力は全部）。
7. 腕の形のサーボ: 肩 40 N·m・肘 30 N·m（`ArmServoHz`）が IK の目標へ。
8. 体から拳への移送 `TransferBodyMotion`（`BodyToFistTime`・`BodyToFistEfficiency`、拳の目標の先走り `Lead` を通る）。
9. 胴のひねり `ApplyFacingAndUpright`（`TorsoTwist` と `TwistBias`、`MaxTorsoTwistRadians`・`HipTwistShare`・`UprightKp`）。
10. 足の運び・踏み込み（移動指令の前向き成分 `StepIn`）。

独立に動かせる要素（DOE の要素 20+3 個）:

| 群 | 要素 | 水準（下限〜上限、基準） |
|---|---|---|
| 腕の筋 | `MaxHandForce` 175〜700（350）、`ElbowMax` 20〜80（39.9）、`ShoulderMax` 20〜80（39.9）、`ActTime` 8〜40 ms（15.1）、`Curvature` 0.12〜0.5（0.25）、`MinLeverDeg` 4〜30（10） | 腕だけの駆動の強さ・速さ・立ち上がり |
| 腕の指令（PD） | `HandKp` 200〜1000（390.5、kd は √Kp 比で減衰比を保つ）、`LooseDamp` 0.1〜1（0.3） | 脳の指令の固さ |
| 腕の形 | `ArmServoHz` 4〜20（8.94） | 肩・肘サーボの固さ |
| 反作用・体からの借り | `ReactShare` 0〜1（0.5）、`BorrowSpeed` 0.3〜2（0.839）、`BodyDrive` 0〜3（1.5） | 拳の力で胸がどれだけ動くか・体を借りる上限 |
| 体から拳への移送 | `BodyEff` 0〜1（0.5）、`BodyTime` 0.02〜0.2 s（0.067）、`LeadHz` 0.5〜6（1.787）、`Spring` 0〜1（1、指令） | 溜め・移送のエネルギー |
| 胴のひねり | `MaxTwistDeg` 20〜70（35）、`HipShare` 0.2〜1（0.45）、`UprightKpRatio` 0.5〜3（1）、`TwistBias` 0〜0.6 rad（0、指令） | ひねりの大きさ・固さ・腰の分担 |
| 足・踏み込み | `StepInScale` 0〜3（1、指令） | 肩の前進 |
| 条件 | `Range` 0.9〜1.4 腕（1.05）、`Telegraph` 0〜0.3 s（0.134） | 目標までの距離・溜め |

水準は**体の値そのものを動かす反事実**で、製品へ入れる値ではない（感度を測るため。入れる値は分析のあとに体の寸法・物理から決める）。条件（構えの姿勢・乱数の種 1・疲れ無し・守らない相手）は固定し、ホールドアウトは種 4〜6 と別の LHS。

### 1.2 設計

- **A. 腕だけ**（`frozen = 1`）: 胸と骨盤を、毎歩、打ち出し前の位置・向きへ戻して速度を 0 にする（`SetDynamicPose`）。肩は動かない。足の運び・ひねり・移送は効かない（胸が動かないので）。
- **B. 腕 + 体が自由**（`frozen = 0`）: 通常の試合の物理。
- 各要素について、(1) **空間充填（LHS 300 点）** を frozen / free の両方で同じ点（対）で回す（主効果・相互作用・対の差 = 体の寄与）、(2) **1 要素ずつの格子（9 水準 × 23 要素、基準を足して 208 点）** を frozen / free で回す（応答曲線と傾き）。計 1,016 本。
- 1 点 = 1 本の右ストレート（`punchlab --paths standard --defenders still` と同じ `PunchLab.Bout`）。測定器は `tests/MiniRing.Tests/DriveDoeProbe.cs`（`PunchLab.DriveKnobs` で指令の差し替え）。世界ごと別スレッド（5 並び）、1 つの世界は単スレッド・決定的。
- 1 点 1 行の特徴量表: `docs/arena/drive/doe_lhs.csv`・`doe_oat.csv`・`targets.csv`・（ホールドアウト）`doe_holdout.csv`。基準の時系列: `series_baseline_free.csv`・`series_baseline_frozen.csv`。要素表: `factors.csv`。実行: `run_doe.sh`。

### 1.3 縮約モデル

`DriveDoeProbe.Model`: 剛体 1 つ（質量 = 上腕 + 前腕、または前腕）を、実測の**肩の位置・速度**と**拳の目標**・**力の上限**・Spring（kd）を外から入れて、実物と同じ `FighterMath.HandAccel` → `ArmMuscle.Excitation` → `Activate` → `ArmMuscle.Force`（`Pose`・`Constants` は実測の腕の長さ・今の肩との相対）で動かす。体の拘束・サーボ・接触・反作用は無い。実測の拳の速さの時系列との差が「モデルに無い効果」。変種: (0) 質量 = 上腕 + 前腕、(1) 質量 = 前腕の 0.4 倍に当たる値、(Ideal) Hill の力と速さの関係なし（欲しい力を上限まで出す）。

## 2. 進み具合

- [x] 指示書・前の報告・関数を読んだ。
- [x] 測定器（`DriveDoeProbe`、`PunchLab.DriveKnobs`、`Fighter.ArmLengths`）を作り、基準が前の報告と一致（拳 4.434 m/s・質量 6.343 kg・エネルギー 63.594 J）。
- [x] 全データ取得（LHS 1,822 本 + 縮約モデルの列つき 500 本 + 1 要素ずつの格子 832 本）。1 本は約 3 CPU 秒（寄せる 10 秒の世界が支配的）で、5〜6 並びで 0.5〜1 本/秒。
- [x] 縮約モデル（腕: 質量の格子、胴: 胸の鉛直軸 1 自由度の PD）の当たりを確認（腕: 最適質量 1.4〜2.0 kg・RMSE 0.85、胴: 有効慣性の補正 4 で相関 0.94・RMSE 0.9 rad/s）。
- [x] 分析を **コードを変える前に** `DRIVE_DOE_ANALYSIS.md` に書いた（結論: 足りない拳の速さの 7〜8 割は胴の回旋。ひねりのバネが人の 1/5 のトルク（28 N·m）で、目標の速さの先行が無い）。
- [x] 実装の足場: `FighterTuning.TrunkRotatorTorque`（既定 28.2 = 今と同じ）・`TrunkTwistFeedForward`（既定 0 = 今と同じ）・`TrunkTwistMaxRate`、`Fighter.ApplyUprightImpulse` の鉛直軸まわりだけ分けた（既定値で `punchlab` が 4.4344 → 4.4342 m/s と一致）。
- [ ] 実測の掃引（`Doe_TrunkGrid`）で水準を選ぶ・ホールドアウト・テスト・動画・文書。

### 2.1 基準の最初の観測（網羅の前の予備）

- 欲しい力（PD）は 590 N に達するが、実際に拳へ掛かる力は最大 115 N（基準、当たる直前は 46 N）。活性化は立ち上がっていて上限（563 N）に対し 0.2〜1.3（6 群の和）。力が小さいのは Hill の速さの関係と肘のてこ（伸びきりに近いほど肘の筋の速さが足りなくなる）のため。
- 縮約モデルで Hill の速さの関係を外す（Ideal）と拳は 8.2 m/s まで出る。Hill の関係があると 3.3 m/s。つまり**腕の筋の速さの関係が拳を 5 m/s ほど落としている**。実測は 4.4 m/s（モデルより速いのはサーボ・IK・体からの移送・肩の動き）。
- 肩の前進は 0.43 m/s、胸の鉛直軸まわりは 2.1 rad/s（前の報告と一致）。胴を固定しても最高速は 4.56 → 4.47 m/s で、今は胴はほとんど寄与していない。
