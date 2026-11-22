# 報告 肩の前進の DOE（物理の統合 段 3 の 2）

v68 の肩前進は、基準の接触直前で **0.896 m/s = 胸ヨー 0.732 + 胸の並進 0.122 + その他の回転 0.042**。胸・骨盤のヨーを固定した反事実は、格子 162 対で拳ピークを平均 0.409 m/s、肩ピークを 0.625 m/s 下げた。肩甲帯は縮約モデル上で肩を速くできるが、拳への効果は手の制御によって正負が変わる。候補を評価し、物理コードへの採用は見送った。ワークツリーでローカル実行、コミットなし。

## 右ストレートの比較

接触直前の値。エネルギーはイベント値。120 N·m とヨー固定は Probe 内の比較条件。

| 条件 | 拳 [m/s] | 肩前進 [m/s] | 胸ヨー [rad/s] | 乗る質量 [kg] | エネルギー [J] |
|---|---:|---:|---:|---:|---:|
| v68 の基準・作業前後に共通 | 4.360 | 0.896 | 4.812 | 9.678 | 97.32 |
| ヨー固定 | 3.727 | 0.183 | 0 | 6.459 | 46.75 |
| トルクを 120 N·m に変更した試行 | 3.901 | 0.953 | 5.503 | 9.715 | 78.30 |
| 半歩遠い v68 | 3.798 | 1.090 | 5.614 | 10.358 | 72.85 |
| 半歩遠い・ヨー固定 | 0.864 | 0.340 | 0 | 11.891 | 4.19 |

今回の 1.05 腕の拳ピークは 4.671 m/s。前回報告の 4.92 m/s はピークの過去記録で、同報告の接触直前は 4.19 m/s。今回の値と履歴の差を確認するため、既存の `DriveDoeProbe.Targets_BaselineBody` も同じビルドで実行し、今回の接触時の拳・肩・質量・エネルギーと CSV の丸め精度で一致した。過去の差が生じた理由は未特定。

## 検証

- 種 4〜6、1.05 / 1.30 腕。胴のモデルのピーク角速度誤差は RMSE 0.359 rad/s。肩甲帯は基準時系列への感度予測なので、実装した肩甲帯への外部検証はできていない。
- ジャブと左右フックは同じ種系列で `attacklab` を実行。ジャブは 6/9・8/11、左フックは 0/9・6/10、右フックは 3/6・2/8 が命中（1.05・1.30 腕の順）。v68 の基準値であり、候補の効果検証には使用していない。
- 速い段は 622 件中 614 合格、8 失敗。8 件すべて `physics_consolidate_failing_after_m1.txt` に記載があり、新規 0 件。同時実行した `ShoulderDoeProbe.Measure` は合格。記録は `tests/MiniRing.Tests/TestResults/shoulder_v68.trx`。
- VR の一人称物理・VR シミュレーション・起き上がり・体幹の追加確認は 15 件すべて合格。
- 40 秒設定の champion 対 swarmer をレンダリング。導入・結果込みで 48.6 秒、AAC 音声付き。時刻付き 12 コマを目視し、6.1・10.1 秒のガード、14.2・34.4 秒の接触、18.2〜26.3 秒のダウン、30.3 秒の起立後を確認。写っている肩に明確な胴との隙間は見当たらなかった。試合は v68 の基準で、VR 実機の確認は含まない。動画とシートは `media_tmp/shoulder/`。

既存の速い段で再現した失敗は次の 8 件。

- `DebrisLookTests.ABladeCut_SpraysTheFleshTheWayTheBladeSwept`
- `KarateFistTests.SameBlow_KnucklesConcentrateDamageComparedWithPalm_AndHurtTheirOwnSkin (13.43)`
- `NeckMuscleTests.ABracedNeck_HoldsMoreOfTheGloveBlowThatBentItClosest_ThanASlackOne`
- `NeckMuscleTests.AFaintedNeck_IsWeakerThanABracedOne`
- `NeckMuscleTests.ANeckWithItsMuscleCarvedAway_IsWeaker`
- `NeckMuscleTests.OnlyOneSidesMuscle_Holds_AndTheSideFlipsWithThePush`
- `NeckMuscleTests.MuscleInTheForearm_HoldsSomeOfTheBending_LikeTheNeck`
- `StepTests.AStepForward_CoversMoreGroundThanWalking_AndPeaksFaster`

## 再現とファイル

- 設計・文献・計算式・採否は [SHOULDER_DOE_ANALYSIS.md](SHOULDER_DOE_ANALYSIS.md)。
- 実測は `docs/arena/drive/shoulder_series.csv`。肩甲帯の格子と LHS の出力は `shoulder_girdle.csv`。集計は `shoulder_summary.txt`、参照計測は `shoulder_reference_local.csv`、ジャブとフックは `shoulder_attacklab.md`。
- 既存の復元済み資産を使った実行は `dotnet test tests/MiniRing.Tests -c Release --no-restore -m:1 --filter FullyQualifiedName~ShoulderDoeProbe`。解析は `python tools/shoulder_doe.py`。Windows の VSTest はローカル接続を許可して実行した。
