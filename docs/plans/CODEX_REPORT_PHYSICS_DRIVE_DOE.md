# 報告: 腕・胴から拳への駆動の DOE（物理の統合 段 3 の 1c）

コミットなし。実施は Claude（Codex の枠切れで引き継ぎ）。分析は `DRIVE_DOE_ANALYSIS.md`（変更前に §1〜5、実装後の追記 §6）、進み具合は `PHYSICS_DRIVE_DOE_LOG.md`、生データは `docs/arena/drive/`。

## 結論
目標（拳 7〜10 m/s・質量 2〜5 kg・エネルギー 60〜200 J）のうち**エネルギーだけが中**。拳の速さ・質量は届かない。足りない拳の速さの 7〜8 割は胴の回旋（肩の前進 0.43 vs 人 3〜4 m/s）で、原因は体の値（胴のひねりのバネが人の約 1/4〜1/5 のトルク 28 N·m で、目標の速さの先行も無い）。これを直したが、肩の幅・体の前進（技の StepIn）・肩甲骨が残り、届かない。倍率・分岐・技の入力の書き換えは入れていない。

## 測定の設計とデータ
測定器 `tests/MiniRing.Tests/DriveDoeProbe.cs`（Probe）、`PunchLab.DriveKnobs`（指令の差し替え）、`Fighter.ArmLengths`。23 要素（腕の筋・PD・サーボ・反作用・移送・ひねり・踏み込み・間合い・溜め）。自由 / 胸骨盤固定の対。
- LHS 911 対（1,822 本）+ 縮約モデルの列つき 250 対、1 要素ずつ 9 水準 × {自由,固定} × {標準経路,Instant} 832 本、胴の完全格子 288 本（トルク × 先行 × 可動域 × 腰 × 間合い）、ホールドアウト 100 対（別 LHS・種 4〜6）。
## モデルと当たり
- 恒等式: 拳 = 肩の速さ + 肩に対する拳の速さ（差 4e-8）。
- 腕: 縮約モデル（実物と同じ HandAccel→Excitation→Activate→ArmMuscle.Force）。相関 0.7〜0.83・RMSE 0.85 m/s、最適な質量 1.4〜2.0 kg。腕の伸び 4 m/s は人に近い（肘 24 rad/s は人の 17〜26）。力は MaxHandForce に依らず（欲しい力 × Hill 係数 0.09）。
- 胴: 胸の鉛直軸 1 自由度の PD モデル、有効慣性 ×4 で相関 0.94・RMSE 0.9 rad/s。
## 寄与の順位（拳の速さ）
Range（条件）> **胴を回すバネ** > MaxHandForce/Curvature/ArmServoHz/ElbowMax（腕）> Telegraph/MaxTwist/TwistBias。効かない: 移送（BodyEff/BodyTime/LeadHz）・BorrowSpeed・ReactShare・ShoulderMax・StepInScale。23 要素を範囲いっぱいに動かした 911 点の最良でも拳 6.6・肩 1.9 m/s。
## 変えたこと（PhysicsVersion 67 → 68）
`FighterTuning.TrunkRotatorTorque` 70 N·m（人の静的 70〜120 の下の端。Kp = トルク/(胸の慣性×可動域)、減衰比は傾きと同じ、上限 = トルク/慣性）、`TrunkTwistFeedForward` 1、`TrunkTwistMaxRate` 15。傾きは前のまま。70 を選んだのは、80/90/120 では BuildReadTests.SameStyle_LongArms… か ElbowContactTests.AFasterElbow… のどちらかが縁で外れるため（70・60 は両方通る）。120 だと効きは大きい（下表）。1 行で変えられる。
| punchlab 標準・守らない・種 1〜6 | 67 | 68（70 N·m） | 参考: 120 N·m |
|---|---|---|---|
| 拳の最高速（届く）| 4.56 | 4.92 | 4.98 |
| 当たる直前の拳 | 4.43 | 4.19（拳が 8 ms 早く着き、目標の減速で制動中に当たる）| 4.84 |
| 質量 [kg] | 6.34 | 8.49 | 8.65 |
| エネルギー [J] | 63.6 | 76.7 | 105 |
| 胸の最大角速度 [rad/s] | 2.1 | 4.9 | 5.9 |
| 肩の前進 [m/s] | 0.43 | 0.84 | 0.94 |
| 半歩遠い 拳 / E | 3.56 / 57.8 | 4.78 / 97.9 | 4.97 / 93.5 |
**ホールドアウト**（100 点、種 4〜6、70 N·m 対 変更前）: 拳の最高速 5.01→5.43（+0.42、97 % の点で改善）、肩 +0.39（99 %）、胸 3.4→6.7 rad/s、エネルギー 75.7→114.6 J（91 %）、質量 +1.0 kg（68 %）。縮約モデルの胸の角速度（慣性 ×4）: 偏り +0.56・RMSE 1.26・相関 0.73、増分は予測 +3.8 に対し実測 +3.3。拳への換算は外れた（肩は胸 1 rad/s あたり 0.12 m/s で、分析 §4 の 0.21 は誤り、予測の拳は約 0.5 m/s 楽観的）。
ジャブ・フック（attacklab 12 本、変更前 `--legacytrunk true` 対 後）: 当たり 24→29 / 14→15 / 16→19、拳の速さ 2.90→2.83 / 3.78→3.69 / 4.20→3.94 m/s。壊れていないが改善も小さい。
## 届かない理由（倍率で埋めない）
肩 3〜4 m/s のうち胴の回旋は 1 m/s 前後（肩が胸の軸から 0.11〜0.15 m）。体の前進は技の StepIn 0.35 × WalkSpeed 0.92 = 0.32 m/s で拳の間ほぼ 0（StepInScale 3 倍でも +0.1）、肩甲骨の前方突出と後ろ足を軸にした全身の回りは剛体に無い。当たる 0.12 秒は技の経路の時間で、胴が最高速に乗る前に当たる。質量の増加（体の運び BodyCarry・腕 3.3 kg 全加算）は DEBT「拳に乗る体の質量」。可動域 35°・腰 0.45 は効きが小さい（+0.07 m/s）ので前のまま。
## テスト
- 速い段: 622 件・615 合格・7 失敗。7 件は全て baseline の既存失敗（新規 0）。
- 新規 `TrunkTwistTests`（slow）2 件合格。FixedSimulation・Replay・Step・Footwork・Down*・BodyToFist・BodyCarry・PunchMechanics・PunchAim・StepCost・HitAndAway の部分実行: 新規失敗なし（StepTests.AStepForward は baseline）。
- 全 slow 段は**完了できず**（この PC で 1 プロセスが極端に遅い）。4 分割で約 470 件実行、失敗 9: baseline にあるもの（MuscleLoss、Bojutsu Forward、BoneBreak AGloveBlow）、HEAD でも失敗するもの（EveryElbow…、ArmSpeedLimitTests 2 件 = 取り消した試行のテスト、DrillMeter は全件実行時のみ）、**120 N·m で新たに落ちた 2 件は 70 N·m で通ることを確認**（SameStyle_LongArms…、AFasterElbow…）。残りの slow 全件は tower32 で要確認（指示により使っていない）。
- コードレビュー（別エージェント）: 指摘の数字の不一致・ループの上限・弱い断言を反映。残り: 慣性が局所 Y（傾いた胸で軸ずれ、小）、骨盤は胸の慣性で Kp（DEBT 記載）。
- 動画 40 秒と sheet は未作成。
## その他
`TrunkScaleProbe.cs` は壊れていたコンパイルを直した（`Frame.YawRadians`）。`AttackLab` に `--legacytrunk true`（反事実、測るだけ）。docs: VERSIONS・BOXING 1-4・DEBT（拳に乗る体の質量(3)・拳を引く筋肉(8)）。README の件数は未更新。
