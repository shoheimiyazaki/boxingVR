## perf fist（2026-11-25 17:49、DESKTOP-2403GU4）

種 1 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 20.8 s。物理 102・判断 73。
チェックサム（計測あり）: 280f6b68c16111ec / 計測なし: 280f6b68c16111ec（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 3.72 | 2.29 | 9.48 | 11.07 | 13.00 | 0.0% | 97 / 1 |
| 描画の詰め（CPU） | 4.90 | 4.55 | 8.01 | 9.02 | 125.69 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.527 | 14.1% | 0.60 | 1.18 |
| 　Perception | 0.048 | 1.3% | 0.06 | 0.34 |
| 　BotRead | 0.073 | 2.0% | 0.13 | 0.73 |
| 　BotPerceive | 0.035 | 0.9% | 0.07 | 0.68 |
| 　BotRead2 | 0.005 | 0.1% | 0.01 | 0.03 |
| 　BotChoose | 0.003 | 0.1% | 0.01 | 0.10 |
| 　BotExecute | 0.006 | 0.2% | 0.01 | 0.01 |
| 　DriveFighter | 0.392 | 10.5% | 0.40 | 0.90 |
| 　ApplyControl | 0.386 | 10.4% | 0.39 | 0.89 |
| 　FighterStamina | 0.060 | 1.6% | 0.06 | 0.52 |
| 　FighterStanding | 0.247 | 6.6% | 0.29 | 0.64 |
| 　FighterDown | 0.041 | 1.1% | 0.00 | 0.27 |
| 　ControlTail | 0.011 | 0.3% | 0.01 | 0.06 |
| PreBepu | 0.018 | 0.5% | 0.02 | 0.54 |
| 　Remember | 0.009 | 0.3% | 0.01 | 0.53 |
| Bepu | 0.864 | 23.2% | 0.97 | 1.59 |
| CanvasImpacts | 0.011 | 0.3% | 0.01 | 0.34 |
| FistHits | 0.003 | 0.1% | 0.01 | 0.02 |
| Weapons | 2.272 | 61.1% | 9.88 | 11.26 |
| 　MayTouch | 0.020 | 0.5% | 0.03 | 0.07 |
| 　SweepSetup | 0.925 | 24.8% | 5.23 | 6.28 |
| 　SetupRelative | 0.012 | 0.3% | 0.06 | 0.08 |
| 　SetupFar | 0.002 | 0.1% | 0.01 | 0.02 |
| 　SetupCandidates | 0.704 | 18.9% | 4.00 | 4.81 |
| 　SweepRun | 0.267 | 7.2% | 2.10 | 4.80 |
| 　RunScan | 0.187 | 5.0% | 1.41 | 4.21 |
| 　RunFront | 0.038 | 1.0% | 0.32 | 1.21 |
| 　RunOverlaps | 0.001 | 0.0% | 0.01 | 0.55 |
| 　RunPatch | 0.022 | 0.6% | 0.20 | 0.93 |
| 　RunHold | 0.000 | 0.0% | 0.00 | 0.01 |
| 　RunCrush | 0.004 | 0.1% | 0.03 | 0.13 |
| 　Bend | 0.003 | 0.1% | 0.02 | 0.50 |
| 　Cut | 0.030 | 0.8% | 0.31 | 1.05 |
| 　Debris | 0.000 | 0.0% | 0.00 | 0.06 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.58 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.012 | 0.3% | 0.01 | 2.25 |
| After | 0.005 | 0.1% | 0.00 | 0.05 |
| PostStep | 0.004 | 0.1% | 0.00 | 0.08 |
| 　PostDebris | 0.002 | 0.1% | 0.00 | 0.08 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| （枠の外） | 0.004 | 0.1% | | |

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 8.03 s: **13.0**（破片 0、GC0 0）: Weapons 11.3・SweepSetup 5.4・SetupCandidates 4.1・SweepRun 3.5・RunScan 1.7・RunFront 1.0・Bepu 0.9・Control 0.7・Cut 0.5・RunPatch 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 0.72 s: **12.8**（破片 0、GC0 1）: Weapons 11.2・SweepSetup 4.4・SetupCandidates 3.4・SweepRun 1.5・Bepu 0.8・RunScan 0.8・Control 0.7・RunFront 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 17.03 s: **11.9**（破片 0、GC0 2）: Weapons 9.7・SweepSetup 5.0・SetupCandidates 3.8・SweepRun 1.7・Bepu 1.2・RunScan 1.1・Control 0.9・Cut 0.6・DriveFighter 0.4・ApplyControl 0.4・BotRead 0.4・BotPerceive 0.3・FighterStanding 0.3
- 種 1 の 8.08 s: **11.8**（破片 0、GC0 0）: Weapons 10.3・SweepSetup 5.4・SetupCandidates 4.1・SweepRun 2.5・RunScan 1.8・Bepu 0.9・Control 0.6・DriveFighter 0.4・ApplyControl 0.4・Cut 0.4・RunFront 0.3
- 種 1 の 5.08 s: **11.8**（破片 0、GC0 0）: Weapons 10.2・SweepSetup 5.4・SetupCandidates 4.1・SweepRun 2.1・RunScan 1.7・Bepu 1.0・Control 0.6・Cut 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 18.15 s: **11.7**（破片 0、GC0 0）: Weapons 10.3・SweepRun 4.8・RunScan 4.2・SweepSetup 3.0・SetupCandidates 2.3・Bepu 0.7・Control 0.7・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 8.07 s: **11.7**（破片 0、GC0 0）: Weapons 10.1・SweepSetup 5.5・SetupCandidates 4.2・SweepRun 2.3・RunScan 1.6・Bepu 0.9・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 8.10 s: **11.6**（破片 0、GC0 0）: Weapons 10.1・SweepSetup 5.6・SetupCandidates 4.4・SweepRun 2.2・RunScan 1.7・Bepu 0.9・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 1.85 s: **11.5**（破片 0、GC0 0）: Weapons 9.8・SweepSetup 5.7・SetupCandidates 4.4・SweepRun 1.9・RunScan 1.1・Bepu 1.1・Control 0.6・DriveFighter 0.4・ApplyControl 0.4・RunFront 0.4
- 種 1 の 5.07 s: **11.5**（破片 0、GC0 0）: Weapons 9.9・SweepSetup 5.7・SetupCandidates 4.4・SweepRun 1.8・RunScan 1.6・Bepu 0.9・Control 0.6・DriveFighter 0.4・ApplyControl 0.4

## perf fist（2026-11-25 17:50、DESKTOP-2403GU4）

種 2 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 26.4 s。物理 102・判断 73。
チェックサム（計測あり）: 5f96a6a5e72eebb2 / 計測なし: 5f96a6a5e72eebb2（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 4.98 | 5.00 | 9.63 | 11.07 | 20.32 | 0.0% | 123 / 1 |
| 描画の詰め（CPU） | 5.95 | 5.83 | 8.39 | 9.63 | 125.19 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.565 | 11.4% | 0.61 | 1.20 |
| 　Perception | 0.082 | 1.6% | 0.10 | 0.66 |
| 　BotRead | 0.078 | 1.6% | 0.09 | 0.46 |
| 　BotPerceive | 0.034 | 0.7% | 0.04 | 0.41 |
| 　BotRead2 | 0.007 | 0.1% | 0.01 | 0.04 |
| 　BotChoose | 0.004 | 0.1% | 0.01 | 0.04 |
| 　BotExecute | 0.008 | 0.2% | 0.01 | 0.05 |
| 　DriveFighter | 0.392 | 7.9% | 0.41 | 1.02 |
| 　ApplyControl | 0.387 | 7.8% | 0.40 | 1.01 |
| 　FighterStamina | 0.063 | 1.3% | 0.07 | 0.69 |
| 　FighterStanding | 0.269 | 5.4% | 0.30 | 0.36 |
| 　FighterDown | 0.016 | 0.3% | 0.00 | 0.20 |
| 　ControlTail | 0.010 | 0.2% | 0.01 | 0.05 |
| PreBepu | 0.019 | 0.4% | 0.02 | 0.07 |
| 　Remember | 0.009 | 0.2% | 0.01 | 0.04 |
| Bepu | 0.804 | 16.1% | 1.02 | 1.36 |
| CanvasImpacts | 0.010 | 0.2% | 0.01 | 0.05 |
| FistHits | 0.003 | 0.1% | 0.01 | 0.02 |
| Weapons | 3.546 | 71.2% | 10.36 | 18.59 |
| 　MayTouch | 0.021 | 0.4% | 0.02 | 0.07 |
| 　SweepSetup | 1.601 | 32.2% | 4.75 | 5.77 |
| 　SetupRelative | 0.019 | 0.4% | 0.05 | 0.08 |
| 　SetupFar | 0.004 | 0.1% | 0.01 | 0.04 |
| 　SetupCandidates | 1.213 | 24.4% | 3.58 | 4.37 |
| 　SweepRun | 0.534 | 10.7% | 2.93 | 10.77 |
| 　RunScan | 0.350 | 7.0% | 1.86 | 5.26 |
| 　RunFront | 0.088 | 1.8% | 0.28 | 0.99 |
| 　RunOverlaps | 0.003 | 0.1% | 0.17 | 1.85 |
| 　RunPatch | 0.052 | 1.0% | 0.47 | 2.81 |
| 　RunHold | 0.000 | 0.0% | 0.00 | 0.02 |
| 　RunCrush | 0.010 | 0.2% | 0.02 | 0.69 |
| 　Snap | 0.000 | 0.0% | 0.00 | 0.01 |
| 　Bend | 0.006 | 0.1% | 0.06 | 0.58 |
| 　Cut | 0.059 | 1.2% | 0.30 | 2.43 |
| 　Debris | 0.000 | 0.0% | 0.01 | 0.07 |
| Disconnect | 0.001 | 0.0% | 0.00 | 0.02 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.02 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.014 | 0.3% | 0.04 | 2.23 |
| After | 0.004 | 0.1% | 0.00 | 0.02 |
| PostStep | 0.007 | 0.1% | 0.03 | 0.08 |
| 　PostDebris | 0.006 | 0.1% | 0.03 | 0.08 |
| 　PostWounds | 0.000 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| （枠の外） | 0.004 | 0.1% | | |

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 38.85 s: **20.3**（破片 0、GC0 0）: Weapons 18.6・SweepRun 10.8・RunScan 5.3・SweepSetup 5.1・SetupCandidates 3.9・RunPatch 2.8・RunOverlaps 1.9・Bepu 0.9・Control 0.7・RunFront 0.4・DriveFighter 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 2 の 8.03 s: **12.9**（破片 0、GC0 0）: Weapons 11.2・SweepRun 4.5・Cut 2.4・SweepSetup 2.1・RunPatch 1.9・SetupCandidates 1.5・RunScan 1.4・Bepu 0.7・RunOverlaps 0.7・Control 0.6・Bend 0.6・DriveFighter 0.4・ApplyControl 0.4・FighterSplit 0.3
- 種 2 の 39.98 s: **12.9**（破片 0、GC0 0）: Weapons 11.0・SweepSetup 5.1・SetupCandidates 3.8・SweepRun 3.6・RunScan 2.2・Bepu 1.1・Cut 0.6・Control 0.6・RunFront 0.6・RunPatch 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 39.90 s: **12.8**（破片 0、GC0 0）: Weapons 10.9・SweepRun 4.8・SweepSetup 4.0・SetupCandidates 2.9・RunScan 1.7・RunPatch 1.4・Bepu 1.1・RunOverlaps 0.9・Control 0.6・RunFront 0.5・DriveFighter 0.4・ApplyControl 0.4・Cut 0.3
- 種 2 の 15.05 s: **12.3**（破片 0、GC0 1）: Weapons 10.7・SweepSetup 5.0・SetupCandidates 3.8・SweepRun 2.7・RunScan 2.0・Bepu 0.9・Control 0.7・DriveFighter 0.4・ApplyControl 0.4・RunFront 0.3
- 種 2 の 38.87 s: **12.2**（破片 0、GC0 0）: Weapons 10.7・SweepSetup 4.7・SweepRun 3.8・SetupCandidates 3.6・RunScan 3.4・Bepu 0.8・Control 0.6・DriveFighter 0.5・ApplyControl 0.5・FighterStanding 0.3
- 種 2 の 38.83 s: **12.1**（破片 0、GC0 0）: Weapons 10.5・SweepSetup 4.8・SetupCandidates 3.7・SweepRun 3.1・RunScan 1.6・Bepu 0.9・RunFront 0.8・Control 0.6・DriveFighter 0.4・RunPatch 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 2 の 3.92 s: **12.1**（破片 0、GC0 0）: Weapons 10.3・SweepSetup 4.3・SweepRun 3.6・SetupCandidates 3.3・RunScan 3.1・Bepu 1.1・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 38.75 s: **12.1**（破片 0、GC0 1）: Weapons 10.2・SweepSetup 5.7・SetupCandidates 4.3・SweepRun 1.5・Bepu 1.1・RunScan 1.0・Cut 0.8・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 39.97 s: **12.0**（破片 0、GC0 0）: Weapons 10.2・SweepSetup 4.8・SetupCandidates 3.6・SweepRun 3.3・RunScan 2.5・Bepu 1.0・Control 0.6・DriveFighter 0.4・ApplyControl 0.4・Cut 0.4・FighterStanding 0.3

## perf katana（2026-11-25 17:51、DESKTOP-2403GU4）

種 1 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 26.3 s。物理 102・判断 73。
チェックサム（計測あり）: dee9e51fcd693fdc / 計測なし: dee9e51fcd693fdc（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 7.75 | 5.07 | 23.18 | 39.05 | 112.98 | 11.5% | 37 / 1 |
| 描画の詰め（CPU） | 3.19 | 2.86 | 7.27 | 8.86 | 64.34 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.562 | 7.3% | 0.55 | 3.42 |
| 　Perception | 0.067 | 0.9% | 0.09 | 0.36 |
| 　BotRead | 0.019 | 0.2% | 0.04 | 0.08 |
| 　DriveFighter | 0.461 | 6.0% | 0.41 | 3.32 |
| 　ApplyControl | 0.446 | 5.8% | 0.40 | 0.62 |
| 　FighterStamina | 0.063 | 0.8% | 0.07 | 0.14 |
| 　FighterStanding | 0.216 | 2.8% | 0.28 | 0.41 |
| 　FighterDown | 0.124 | 1.6% | 0.01 | 0.29 |
| 　ControlTail | 0.011 | 0.1% | 0.01 | 0.37 |
| PreBepu | 0.022 | 0.3% | 0.02 | 0.07 |
| 　Remember | 0.011 | 0.1% | 0.01 | 0.04 |
| Bepu | 0.859 | 11.1% | 0.76 | 1.25 |
| CanvasImpacts | 0.010 | 0.1% | 0.01 | 0.43 |
| FistHits | 0.002 | 0.0% | 0.00 | 0.02 |
| Weapons | 6.207 | 80.1% | 44.25 | 66.97 |
| 　MayTouch | 0.048 | 0.6% | 0.06 | 0.14 |
| 　SweepSetup | 1.906 | 24.6% | 9.06 | 13.24 |
| 　SetupRelative | 0.037 | 0.5% | 0.10 | 0.14 |
| 　SetupFar | 0.008 | 0.1% | 0.02 | 0.04 |
| 　SetupCandidates | 1.478 | 19.1% | 7.07 | 10.34 |
| 　SweepRun | 2.643 | 34.1% | 24.32 | 43.63 |
| 　RunScan | 1.987 | 25.6% | 17.78 | 40.95 |
| 　RunFront | 0.232 | 3.0% | 1.20 | 2.52 |
| 　RunOverlaps | 0.051 | 0.7% | 1.32 | 8.19 |
| 　RunPatch | 0.238 | 3.1% | 3.04 | 15.62 |
| 　RunHold | 0.001 | 0.0% | 0.03 | 0.45 |
| 　RunCrush | 0.039 | 0.5% | 0.08 | 1.12 |
| 　Snap | 0.406 | 5.2% | 1.46 | 4.38 |
| 　SnapStiffness | 0.053 | 0.7% | 0.19 | 2.03 |
| 　SnapFind | 0.352 | 4.5% | 1.27 | 2.72 |
| 　Bend | 0.021 | 0.3% | 0.05 | 0.30 |
| 　Cut | 0.020 | 0.3% | 0.19 | 0.76 |
| 　Debris | 0.001 | 0.0% | 0.01 | 0.07 |
| Disconnect | 0.003 | 0.0% | 0.01 | 0.39 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.34 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.016 | 0.2% | 0.03 | 1.08 |
| After | 0.004 | 0.1% | 0.00 | 0.05 |
| PostStep | 0.057 | 0.7% | 4.57 | 108.42 |
| 　PostDebris | 0.011 | 0.1% | 0.05 | 0.13 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.045 | 0.6% | 4.52 | 108.42 |
| （枠の外） | 0.004 | 0.1% | | |

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 31.27 s: **113.0**（破片 0、GC0 1）: PostStep 108.4・PostRound 108.4・Weapons 2.9・SweepRun 1.3・RunScan 1.2・Bepu 1.1・SweepSetup 0.9・SetupCandidates 0.6・Control 0.6・DriveFighter 0.5・ApplyControl 0.5
- 種 1 の 37.37 s: **68.5**（破片 0、GC0 0）: Weapons 67.0・SweepRun 43.6・RunScan 40.9・SweepSetup 9.6・SetupCandidates 7.6・Snap 1.2・SnapFind 1.1・RunFront 1.0・RunPatch 0.9・Bepu 0.8・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.33 s: **61.0**（破片 0、GC0 0）: Weapons 59.5・SweepRun 30.7・RunScan 24.1・SweepSetup 13.2・SetupCandidates 10.3・RunPatch 2.7・RunFront 1.5・Snap 1.3・SnapFind 1.2・RunOverlaps 0.8・Bepu 0.8・Cut 0.6・Control 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.35 s: **60.6**（破片 0、GC0 0）: Weapons 59.2・SweepRun 37.7・RunScan 31.5・SweepSetup 9.5・SetupCandidates 7.5・RunPatch 2.6・RunOverlaps 1.5・Snap 1.4・RunFront 1.4・SnapFind 1.2・Bepu 0.8・Control 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.38 s: **59.4**（破片 0、GC0 0）: Weapons 58.0・SweepRun 36.6・RunScan 30.4・SweepSetup 8.8・SetupCandidates 7.0・RunPatch 3.1・RunOverlaps 1.8・Snap 1.3・SnapFind 1.2・Bepu 0.8・RunFront 0.8・Control 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 2.12 s: **52.3**（破片 1、GC0 0）: Weapons 50.9・SweepRun 32.1・RunScan 28.5・SweepSetup 10.0・SetupCandidates 7.8・RunPatch 1.9・RunOverlaps 0.8・Bepu 0.7・Snap 0.7・SnapFind 0.6・Control 0.6・RunFront 0.5・DriveFighter 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 1 の 37.42 s: **51.8**（破片 0、GC0 0）: Weapons 50.4・SweepRun 27.8・RunScan 20.3・SweepSetup 9.1・SetupCandidates 7.1・RunPatch 3.1・RunOverlaps 2.0・RunFront 1.5・Snap 1.4・SnapFind 1.2・Bepu 0.7・Control 0.5・DriveFighter 0.4・ApplyControl 0.4・Cut 0.3
- 種 1 の 37.40 s: **51.1**（破片 0、GC0 0）: Weapons 49.5・SweepRun 30.9・RunScan 23.6・SweepSetup 7.6・SetupCandidates 6.0・RunPatch 4.1・RunOverlaps 2.2・Snap 1.2・SnapFind 1.0・Bepu 0.8・Control 0.5・RunFront 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.10 s: **47.7**（破片 0、GC0 0）: Weapons 46.2・SweepRun 26.4・RunScan 9.6・SweepSetup 9.3・RunPatch 8.8・SetupCandidates 7.2・RunOverlaps 4.5・Snap 2.1・SnapFind 1.8・RunFront 1.4・Bepu 0.8・Cut 0.6・Control 0.6・DriveFighter 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 1 の 2.13 s: **46.7**（破片 1、GC0 0）: Weapons 45.2・SweepRun 26.1・RunScan 21.4・SweepSetup 9.5・SetupCandidates 7.4・RunPatch 2.4・Snap 1.1・RunOverlaps 1.0・SnapFind 1.0・RunFront 0.8・Bepu 0.7・Control 0.6・DriveFighter 0.5・ApplyControl 0.4

## perf katana（2026-11-25 17:52、DESKTOP-2403GU4）

種 2 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 30.1 s。物理 102・判断 73。
チェックサム（計測あり）: e1b6fa9397864923 / 計測なし: e1b6fa9397864923（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 10.01 | 8.29 | 26.12 | 44.97 | 118.30 | 10.9% | 33 / 2 |
| 描画の詰め（CPU） | 2.48 | 1.73 | 7.30 | 8.78 | 64.35 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.541 | 5.4% | 0.53 | 1.06 |
| 　Perception | 0.099 | 1.0% | 0.08 | 0.57 |
| 　BotRead | 0.017 | 0.2% | 0.04 | 0.09 |
| 　DriveFighter | 0.410 | 4.1% | 0.40 | 0.88 |
| 　ApplyControl | 0.406 | 4.1% | 0.39 | 0.87 |
| 　FighterStamina | 0.061 | 0.6% | 0.06 | 0.51 |
| 　FighterStanding | 0.124 | 1.2% | 0.28 | 0.40 |
| 　FighterDown | 0.180 | 1.8% | 0.01 | 0.41 |
| 　ControlTail | 0.011 | 0.1% | 0.01 | 0.05 |
| PreBepu | 0.021 | 0.2% | 0.02 | 0.07 |
| 　Remember | 0.009 | 0.1% | 0.01 | 0.06 |
| Bepu | 0.860 | 8.6% | 0.78 | 1.47 |
| CanvasImpacts | 0.012 | 0.1% | 0.01 | 0.36 |
| FistHits | 0.002 | 0.0% | 0.00 | 0.02 |
| Weapons | 8.484 | 84.8% | 49.17 | 76.76 |
| 　MayTouch | 0.042 | 0.4% | 0.05 | 0.08 |
| 　SweepSetup | 2.926 | 29.2% | 9.71 | 12.70 |
| 　SetupRelative | 0.066 | 0.7% | 0.11 | 0.16 |
| 　SetupFar | 0.013 | 0.1% | 0.02 | 0.07 |
| 　SetupCandidates | 2.281 | 22.8% | 7.61 | 9.89 |
| 　SweepRun | 3.020 | 30.2% | 29.80 | 57.36 |
| 　RunScan | 2.116 | 21.1% | 23.96 | 56.39 |
| 　RunFront | 0.393 | 3.9% | 1.30 | 2.94 |
| 　RunOverlaps | 0.055 | 0.6% | 1.31 | 4.61 |
| 　RunPatch | 0.275 | 2.7% | 2.72 | 7.79 |
| 　RunHold | 0.002 | 0.0% | 0.02 | 0.15 |
| 　RunCrush | 0.050 | 0.5% | 0.06 | 0.53 |
| 　Snap | 0.558 | 5.6% | 1.20 | 3.66 |
| 　SnapStiffness | 0.071 | 0.7% | 0.18 | 0.86 |
| 　SnapFind | 0.485 | 4.9% | 1.02 | 2.79 |
| 　Bend | 0.038 | 0.4% | 0.05 | 0.46 |
| 　Cut | 0.025 | 0.3% | 0.23 | 0.64 |
| 　Debris | 0.000 | 0.0% | 0.01 | 0.04 |
| Disconnect | 0.002 | 0.0% | 0.03 | 0.42 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.01 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.58 |
| FighterSplit | 0.017 | 0.2% | 0.03 | 1.14 |
| After | 0.004 | 0.0% | 0.01 | 0.02 |
| PostStep | 0.056 | 0.6% | 4.53 | 107.97 |
| 　PostDebris | 0.009 | 0.1% | 0.03 | 0.06 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.045 | 0.5% | 4.50 | 107.97 |
| （枠の外） | 0.004 | 0.0% | | |

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 17.40 s: **118.3**（破片 0、GC0 1）: PostStep 108.0・PostRound 108.0・Weapons 8.6・SweepSetup 3.8・SetupCandidates 3.0・SweepRun 1.5・Bepu 1.1・RunScan 0.9・Control 0.6・DriveFighter 0.4・ApplyControl 0.4・RunFront 0.4・FighterDown 0.3
- 種 2 の 34.37 s: **78.4**（破片 0、GC0 0）: Weapons 76.8・SweepRun 57.4・RunScan 56.4・SweepSetup 11.1・SetupCandidates 8.8・Bepu 0.9・RunFront 0.6・Control 0.5・Cut 0.5・Snap 0.5・SnapFind 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 34.38 s: **65.2**（破片 0、GC0 0）: Weapons 63.6・SweepRun 43.7・RunScan 42.2・SweepSetup 11.0・SetupCandidates 8.6・Bepu 0.9・Snap 0.9・SnapFind 0.7・RunFront 0.7・Control 0.5・RunPatch 0.5・DriveFighter 0.4・ApplyControl 0.4・Cut 0.3
- 種 2 の 23.27 s: **61.8**（破片 0、GC0 0）: Weapons 60.4・SweepRun 38.1・RunScan 30.1・SweepSetup 10.6・SetupCandidates 8.2・RunPatch 3.4・RunOverlaps 2.4・RunFront 1.1・Snap 1.1・SnapFind 0.9・Bepu 0.7・Cut 0.6・Control 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 34.15 s: **61.3**（破片 0、GC0 0）: Weapons 60.0・SweepRun 39.8・RunScan 37.8・SweepSetup 9.9・SetupCandidates 7.7・Snap 0.8・Bepu 0.7・RunFront 0.7・SnapFind 0.7・Control 0.5・RunPatch 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 2.27 s: **55.4**（破片 0、GC0 0）: Weapons 53.7・SweepRun 33.4・RunScan 26.6・SweepSetup 10.6・SetupCandidates 8.4・RunPatch 3.8・RunOverlaps 1.6・Bepu 1.1・RunFront 1.0・Snap 0.8・SnapFind 0.7・Control 0.5・Cut 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 34.10 s: **55.1**（破片 0、GC0 0）: Weapons 53.7・SweepRun 29.7・RunScan 26.8・SweepSetup 11.9・SetupCandidates 9.4・RunPatch 1.2・Snap 1.0・RunFront 0.9・SnapFind 0.8・Bepu 0.8・Control 0.6・DriveFighter 0.5・ApplyControl 0.4・RunOverlaps 0.3・FighterStanding 0.3
- 種 2 の 34.17 s: **54.5**（破片 0、GC0 0）: Weapons 53.2・SweepRun 37.2・RunScan 32.3・SweepSetup 8.0・SetupCandidates 6.3・RunPatch 2.6・RunOverlaps 1.5・Bepu 0.7・Control 0.5・Snap 0.5・RunFront 0.4・SnapFind 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 1.85 s: **52.6**（破片 0、GC0 0）: Weapons 51.1・SweepRun 31.4・RunScan 25.4・SweepSetup 10.0・SetupCandidates 7.9・RunPatch 2.9・RunFront 1.2・RunOverlaps 1.1・Bepu 0.8・Snap 0.7・SnapFind 0.6・Control 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 34.02 s: **52.3**（破片 0、GC0 0）: Weapons 51.1・SweepRun 33.3・RunScan 23.3・SweepSetup 9.0・SetupCandidates 7.1・RunPatch 5.5・RunOverlaps 2.5・RunFront 1.0・Snap 0.8・SnapFind 0.7・Bepu 0.7・Cut 0.6・Control 0.5・DriveFighter 0.4・ApplyControl 0.4

## perf vfc2（2026-11-25 17:55、DESKTOP-2403GU4）

種 1 × 最大 90 秒、5400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 78.6 s。物理 102・判断 73。
チェックサム（計測あり）: 89b78a42727dbe8c / 計測なし: 89b78a42727dbe8c（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 10.91 | 9.70 | 26.42 | 38.60 | 89.12 | 18.7% | 66 / 1 |
| 描画の詰め（CPU） | 3.60 | 3.62 | 6.38 | 7.87 | 67.96 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.562 | 5.2% | 0.67 | 2.24 |
| 　Perception | 0.074 | 0.7% | 0.11 | 0.51 |
| 　BotRead | 0.062 | 0.6% | 0.10 | 0.72 |
| 　DriveFighter | 0.410 | 3.8% | 0.44 | 1.97 |
| 　ApplyControl | 0.406 | 3.7% | 0.43 | 1.96 |
| 　FighterStamina | 0.064 | 0.6% | 0.07 | 0.40 |
| 　FighterStanding | 0.220 | 2.0% | 0.19 | 0.99 |
| 　FighterDown | 0.079 | 0.7% | 0.13 | 0.51 |
| 　ControlTail | 0.011 | 0.1% | 0.01 | 0.12 |
| PreBepu | 0.021 | 0.2% | 0.02 | 0.08 |
| 　Remember | 0.009 | 0.1% | 0.01 | 0.06 |
| Bepu | 1.149 | 10.5% | 1.49 | 1.96 |
| CanvasImpacts | 0.011 | 0.1% | 0.02 | 0.49 |
| FistHits | 0.003 | 0.0% | 0.01 | 0.05 |
| Weapons | 9.095 | 83.4% | 46.49 | 86.13 |
| 　MayTouch | 0.050 | 0.5% | 0.07 | 0.14 |
| 　SweepSetup | 3.272 | 30.0% | 8.25 | 13.55 |
| 　SetupRelative | 0.050 | 0.5% | 0.10 | 0.17 |
| 　SetupFar | 0.010 | 0.1% | 0.02 | 0.07 |
| 　SetupCandidates | 2.544 | 23.3% | 6.44 | 10.83 |
| 　SweepRun | 4.273 | 39.2% | 32.68 | 74.54 |
| 　RunScan | 2.924 | 26.8% | 14.26 | 27.49 |
| 　RunFront | 0.393 | 3.6% | 1.30 | 4.44 |
| 　RunOverlaps | 0.186 | 1.7% | 5.11 | 15.80 |
| 　RunPatch | 0.556 | 5.1% | 9.71 | 38.27 |
| 　RunHold | 0.005 | 0.0% | 0.30 | 2.11 |
| 　RunCrush | 0.071 | 0.7% | 0.10 | 1.90 |
| 　Snap | 0.369 | 3.4% | 1.10 | 4.01 |
| 　SnapStiffness | 0.048 | 0.4% | 0.14 | 1.18 |
| 　SnapFind | 0.319 | 2.9% | 0.95 | 3.76 |
| 　SnapSplit | 0.000 | 0.0% | 0.01 | 0.76 |
| 　Bend | 0.033 | 0.3% | 0.06 | 1.80 |
| 　Cut | 0.024 | 0.2% | 0.09 | 0.91 |
| 　Debris | 0.001 | 0.0% | 0.02 | 0.71 |
| Disconnect | 0.003 | 0.0% | 0.01 | 0.57 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.02 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.02 |
| FighterSplit | 0.024 | 0.2% | 0.17 | 0.89 |
| After | 0.005 | 0.0% | 0.01 | 0.02 |
| PostStep | 0.031 | 0.3% | 0.07 | 0.91 |
| 　PostDebris | 0.024 | 0.2% | 0.07 | 0.20 |
| 　PostWounds | 0.000 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.02 |
| 　PostRound | 0.005 | 0.0% | 0.00 | 0.86 |
| （枠の外） | 0.005 | 0.0% | | |

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 49.82 s: **89.1**（破片 0、GC0 1）: Weapons 86.1・SweepRun 74.5・RunPatch 38.3・RunOverlaps 15.8・RunScan 10.9・SweepSetup 6.7・SetupCandidates 5.2・Bepu 1.8・RunFront 1.5・Snap 1.0・RunHold 1.0・Cut 0.9・SnapFind 0.9・Debris 0.7・Control 0.6・DriveFighter 0.5・ApplyControl 0.5・FighterSplit 0.4
- 種 1 の 49.73 s: **77.2**（破片 0、GC0 0）: Weapons 74.0・SweepRun 65.9・RunPatch 23.8・RunScan 18.1・RunOverlaps 13.3・SweepSetup 6.5・SetupCandidates 5.1・Bepu 1.7・RunHold 1.5・RunFront 1.2・FighterSplit 0.7・Control 0.6・Snap 0.5・DriveFighter 0.5・ApplyControl 0.5・SnapFind 0.4
- 種 1 の 49.70 s: **74.1**（破片 0、GC0 0）: Weapons 71.5・SweepRun 63.6・RunPatch 22.9・RunScan 15.1・RunOverlaps 11.4・SweepSetup 6.5・SetupCandidates 5.0・RunHold 2.1・Bepu 1.6・RunFront 1.2・Control 0.6・Snap 0.5・SnapFind 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 49.67 s: **71.9**（破片 0、GC0 0）: Weapons 68.9・SweepRun 61.2・RunPatch 24.6・RunScan 13.3・RunOverlaps 11.6・SweepSetup 6.4・SetupCandidates 5.1・RunHold 1.9・Bepu 1.6・RunFront 0.8・FighterSplit 0.7・Control 0.6・Snap 0.5・SnapFind 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 49.68 s: **68.0**（破片 0、GC0 0）: Weapons 65.4・SweepRun 57.1・RunPatch 22.2・RunScan 14.3・RunOverlaps 12.7・SweepSetup 6.9・SetupCandidates 5.4・Bepu 1.6・RunHold 1.4・RunFront 0.9・Control 0.6・Snap 0.5・SnapFind 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 49.85 s: **65.5**（破片 0、GC0 0）: Weapons 62.6・SweepRun 47.4・RunScan 19.7・RunPatch 14.0・SweepSetup 9.2・RunOverlaps 7.7・SetupCandidates 7.2・Snap 2.6・Bepu 1.9・RunFront 1.5・SnapFind 1.5・SnapSplit 0.8・Control 0.6・RunHold 0.5・DriveFighter 0.4・ApplyControl 0.4・SnapStiffness 0.4
- 種 1 の 78.28 s: **65.1**（破片 0、GC0 0）: Weapons 62.8・SweepRun 54.1・RunPatch 21.9・RunOverlaps 14.0・RunScan 11.6・SweepSetup 6.8・SetupCandidates 5.2・RunFront 1.5・Bepu 1.2・RunHold 0.9・Snap 0.9・SnapFind 0.8・Control 0.6・DriveFighter 0.4・ApplyControl 0.4・FighterSplit 0.4
- 種 1 の 49.83 s: **60.1**（破片 0、GC0 0）: Weapons 57.5・SweepRun 45.9・RunPatch 18.1・RunScan 13.5・RunOverlaps 7.5・SweepSetup 7.3・SetupCandidates 5.7・Bepu 1.8・RunFront 1.7・Snap 1.0・SnapFind 0.9・Control 0.6・RunHold 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 49.90 s: **57.9**（破片 0、GC0 0）: Weapons 55.0・SweepRun 42.4・RunScan 17.4・RunPatch 12.5・RunOverlaps 8.5・SweepSetup 7.8・SetupCandidates 6.0・RunFront 2.1・Bepu 1.9・Snap 1.1・SnapFind 1.0・Control 0.6・DriveFighter 0.5・ApplyControl 0.5
- 種 1 の 49.63 s: **57.2**（破片 0、GC0 0）: Weapons 54.5・SweepRun 46.2・RunScan 17.1・RunPatch 13.1・RunOverlaps 9.5・SweepSetup 6.9・SetupCandidates 5.4・Bepu 1.6・RunHold 1.2・RunFront 0.6・Control 0.6・Snap 0.5・SnapFind 0.4・DriveFighter 0.4・ApplyControl 0.4・FighterSplit 0.4

## perf vfc2（2026-11-25 17:57、DESKTOP-2403GU4）

種 2 × 最大 90 秒、5400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 71.5 s。物理 102・判断 73。
チェックサム（計測あり）: 59aa46444f84d9ab / 計測なし: 59aa46444f84d9ab（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 8.63 | 6.37 | 21.16 | 38.63 | 120.03 | 9.9% | 87 / 1 |
| 描画の詰め（CPU） | 4.58 | 4.43 | 7.61 | 9.22 | 70.78 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.688 | 8.0% | 0.78 | 4.02 |
| 　Perception | 0.079 | 0.9% | 0.11 | 1.04 |
| 　BotRead | 0.078 | 0.9% | 0.13 | 3.19 |
| 　BotPerceive | 0.022 | 0.3% | 0.07 | 0.38 |
| 　BotRead2 | 0.002 | 0.0% | 0.00 | 0.03 |
| 　BotChoose | 0.001 | 0.0% | 0.00 | 0.18 |
| 　BotExecute | 0.003 | 0.0% | 0.00 | 0.02 |
| 　DriveFighter | 0.505 | 5.8% | 0.51 | 3.87 |
| 　ApplyControl | 0.486 | 5.6% | 0.49 | 3.86 |
| 　FighterStamina | 0.069 | 0.8% | 0.07 | 3.53 |
| 　FighterStanding | 0.307 | 3.6% | 0.25 | 0.45 |
| 　FighterDown | 0.067 | 0.8% | 0.12 | 0.25 |
| 　ControlTail | 0.021 | 0.2% | 0.03 | 0.08 |
| PreBepu | 0.023 | 0.3% | 0.02 | 0.48 |
| 　Remember | 0.009 | 0.1% | 0.01 | 0.03 |
| Bepu | 1.581 | 18.3% | 1.50 | 3.50 |
| CanvasImpacts | 0.011 | 0.1% | 0.01 | 0.74 |
| FistHits | 0.004 | 0.0% | 0.00 | 0.03 |
| Weapons | 5.940 | 68.9% | 50.42 | 106.52 |
| 　MayTouch | 0.051 | 0.6% | 0.07 | 2.46 |
| 　SweepSetup | 2.469 | 28.6% | 8.74 | 17.98 |
| 　SetupRelative | 0.047 | 0.5% | 0.10 | 0.15 |
| 　SetupFar | 0.010 | 0.1% | 0.02 | 0.04 |
| 　SetupCandidates | 1.909 | 22.1% | 6.75 | 14.17 |
| 　SweepRun | 2.310 | 26.8% | 38.38 | 90.82 |
| 　RunScan | 1.552 | 18.0% | 15.79 | 44.20 |
| 　RunFront | 0.163 | 1.9% | 1.75 | 3.80 |
| 　RunOverlaps | 0.134 | 1.6% | 6.04 | 21.05 |
| 　RunPatch | 0.360 | 4.2% | 13.10 | 44.04 |
| 　RunHold | 0.003 | 0.0% | 0.14 | 0.86 |
| 　RunCrush | 0.020 | 0.2% | 0.04 | 0.78 |
| 　Snap | 0.182 | 2.1% | 1.17 | 3.58 |
| 　SnapStiffness | 0.024 | 0.3% | 0.16 | 0.96 |
| 　SnapFind | 0.158 | 1.8% | 1.00 | 2.70 |
| 　Bend | 0.016 | 0.2% | 0.25 | 1.34 |
| 　Cut | 0.025 | 0.3% | 0.18 | 0.98 |
| 　Debris | 0.001 | 0.0% | 0.02 | 0.05 |
| Disconnect | 0.002 | 0.0% | 0.01 | 0.63 |
| Kicks | 0.314 | 3.6% | 2.62 | 15.98 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.024 | 0.3% | 0.17 | 5.18 |
| After | 0.005 | 0.1% | 0.01 | 0.02 |
| PostStep | 0.028 | 0.3% | 0.11 | 3.22 |
| 　PostDebris | 0.020 | 0.2% | 0.11 | 0.26 |
| 　PostWounds | 0.000 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.006 | 0.1% | 0.00 | 3.21 |
| （枠の外） | 0.005 | 0.1% | | |

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 12.50 s: **120.0**（破片 0、GC0 0）: Weapons 106.5・SweepRun 90.8・RunScan 37.6・RunPatch 30.1・SweepSetup 15.9・RunOverlaps 14.6・SetupCandidates 12.3・Kicks 10.3・RunFront 3.1・Bepu 1.5・Snap 1.5・SnapFind 1.3・Control 1.2・DriveFighter 0.6・ApplyControl 0.6・RunHold 0.6・BotRead 0.4・BotPerceive 0.4
- 種 2 の 12.47 s: **113.9**（破片 0、GC0 0）: Weapons 101.0・SweepRun 88.5・RunScan 38.8・RunPatch 26.0・RunOverlaps 18.3・SweepSetup 14.7・SetupCandidates 11.4・Kicks 10.0・RunFront 2.4・Bepu 1.5・Snap 1.3・SnapFind 1.1・Control 0.9・Bend 0.6・DriveFighter 0.6・ApplyControl 0.6・RunHold 0.4
- 種 2 の 12.52 s: **110.2**（破片 0、GC0 0）: Weapons 96.9・SweepRun 81.3・RunScan 44.2・RunPatch 19.7・SweepSetup 16.0・SetupCandidates 12.6・Kicks 10.2・RunOverlaps 10.0・RunFront 3.8・Bepu 1.5・Snap 1.5・SnapFind 1.3・Control 1.1・DriveFighter 0.6・ApplyControl 0.6・Bend 0.5・BotRead 0.4・RunHold 0.3
- 種 2 の 12.53 s: **105.4**（破片 0、GC0 0）: Weapons 93.7・SweepRun 77.7・RunScan 39.2・RunPatch 21.4・SweepSetup 16.1・SetupCandidates 12.6・RunOverlaps 11.1・Kicks 8.8・RunFront 2.9・Bepu 1.5・Snap 1.1・Control 1.1・SnapFind 1.0・DriveFighter 0.6・ApplyControl 0.5・Bend 0.4・BotRead 0.3
- 種 2 の 11.67 s: **96.6**（破片 0、GC0 0）: Weapons 94.6・SweepRun 84.4・RunPatch 43.4・RunOverlaps 19.5・RunScan 12.4・SweepSetup 6.4・SetupCandidates 4.9・RunFront 2.0・Snap 1.3・SnapFind 1.1・Bepu 1.1・RunHold 0.9・Control 0.5・Cut 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 12.48 s: **95.5**（破片 0、GC0 0）: Weapons 81.4・SweepRun 67.3・RunPatch 23.4・RunScan 21.6・RunOverlaps 15.8・SweepSetup 15.1・SetupCandidates 11.7・Kicks 11.0・RunFront 3.5・Snap 1.6・Bepu 1.5・SnapFind 1.4・Control 1.2・Bend 0.7・DriveFighter 0.6・ApplyControl 0.5・Cut 0.5・BotRead 0.4・BotPerceive 0.4・RunHold 0.3
- 種 2 の 12.55 s: **95.1**（破片 0、GC0 0）: Weapons 84.8・SweepRun 66.8・RunScan 40.3・SweepSetup 18.0・RunPatch 15.5・SetupCandidates 14.2・RunOverlaps 8.6・Kicks 7.5・Bepu 1.5・RunFront 1.1・Control 0.8・Snap 0.8・SnapFind 0.7・Cut 0.6・DriveFighter 0.5・ApplyControl 0.5・Bend 0.5
- 種 2 の 11.68 s: **89.5**（破片 0、GC0 0）: Weapons 87.4・SweepRun 77.7・RunPatch 44.0・RunOverlaps 21.0・SweepSetup 6.4・RunScan 5.2・SetupCandidates 5.0・RunFront 2.1・Snap 1.4・SnapFind 1.2・Bepu 1.1・Control 0.5・RunHold 0.5・DriveFighter 0.4・ApplyControl 0.4・FighterSplit 0.3
- 種 2 の 12.45 s: **80.3**（破片 0、GC0 0）: Weapons 68.9・SweepRun 53.4・RunScan 39.0・SweepSetup 15.0・SetupCandidates 11.6・Kicks 8.7・RunPatch 7.4・RunOverlaps 3.3・RunFront 2.0・Bepu 1.5・Snap 1.0・SnapFind 0.9・Control 0.7・DriveFighter 0.5・ApplyControl 0.5・Bend 0.4・Cut 0.4
- 種 2 の 12.43 s: **74.2**（破片 0、GC0 0）: Weapons 62.4・SweepRun 51.4・RunScan 23.7・RunPatch 16.1・SweepSetup 13.6・SetupCandidates 10.6・Kicks 9.0・RunOverlaps 7.0・RunFront 2.0・Bepu 1.4・Control 0.9・Snap 0.9・SnapFind 0.8・DriveFighter 0.5・ApplyControl 0.5・Bend 0.5・RunHold 0.3

## perf royale（2026-11-25 18:02、DESKTOP-2403GU4）

種 1 × 最大 60 秒、3600 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 163.2 s。物理 102・判断 73。
チェックサム（計測あり）: 2ba61773c7da2c74 / 計測なし: 2ba61773c7da2c74（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 34.62 | 34.04 | 60.58 | 78.33 | 115.43 | 87.6% | 550 / 2 |
| 描画の詰め（CPU） | 10.51 | 10.49 | 16.52 | 18.89 | 91.67 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 1.786 | 5.2% | 1.79 | 6.36 |
| 　Perception | 0.232 | 0.7% | 0.26 | 1.03 |
| 　BotRead | 0.175 | 0.5% | 0.27 | 4.32 |
| 　BotPerceive | 0.046 | 0.1% | 0.11 | 1.63 |
| 　BotRead2 | 0.012 | 0.0% | 0.01 | 0.57 |
| 　BotChoose | 0.007 | 0.0% | 0.01 | 0.12 |
| 　BotExecute | 0.008 | 0.0% | 0.01 | 0.05 |
| 　DriveFighter | 1.222 | 3.5% | 1.12 | 2.30 |
| 　ApplyControl | 1.191 | 3.4% | 1.08 | 2.03 |
| 　FighterStamina | 0.179 | 0.5% | 0.17 | 1.08 |
| 　FighterStanding | 0.562 | 1.6% | 0.68 | 1.15 |
| 　FighterDown | 0.316 | 0.9% | 0.10 | 0.85 |
| 　ControlTail | 0.084 | 0.2% | 0.04 | 1.13 |
| PreBepu | 0.058 | 0.2% | 0.06 | 0.94 |
| 　Remember | 0.029 | 0.1% | 0.03 | 0.32 |
| Bepu | 2.999 | 8.7% | 2.33 | 8.60 |
| CanvasImpacts | 0.029 | 0.1% | 0.04 | 0.24 |
| FistHits | 0.021 | 0.1% | 0.02 | 0.54 |
| Weapons | 28.718 | 83.0% | 85.46 | 111.07 |
| 　MayTouch | 0.722 | 2.1% | 0.70 | 2.86 |
| 　SweepSetup | 10.307 | 29.8% | 23.73 | 36.79 |
| 　SetupRelative | 0.214 | 0.6% | 0.27 | 0.38 |
| 　SetupFar | 0.044 | 0.1% | 0.06 | 0.13 |
| 　SetupCandidates | 7.993 | 23.1% | 18.54 | 28.93 |
| 　SweepRun | 8.479 | 24.5% | 41.94 | 71.68 |
| 　RunScan | 5.162 | 14.9% | 25.80 | 48.98 |
| 　RunFront | 0.948 | 2.7% | 2.07 | 4.88 |
| 　RunOverlaps | 0.368 | 1.1% | 3.69 | 18.20 |
| 　RunPatch | 1.397 | 4.0% | 8.67 | 28.97 |
| 　RunHold | 0.008 | 0.0% | 0.05 | 0.66 |
| 　RunCrush | 0.185 | 0.5% | 0.23 | 2.14 |
| 　Snap | 1.305 | 3.8% | 3.50 | 9.67 |
| 　SnapStiffness | 0.173 | 0.5% | 0.49 | 2.54 |
| 　SnapFind | 1.127 | 3.3% | 3.00 | 7.11 |
| 　SnapSplit | 0.000 | 0.0% | 0.00 | 0.28 |
| 　Bend | 0.091 | 0.3% | 0.15 | 1.10 |
| 　Cut | 0.175 | 0.5% | 1.13 | 17.55 |
| 　Debris | 0.003 | 0.0% | 0.01 | 0.09 |
| Disconnect | 0.009 | 0.0% | 0.04 | 0.87 |
| Kicks | 0.687 | 2.0% | 0.14 | 10.63 |
| Elbows | 0.002 | 0.0% | 0.00 | 0.86 |
| FighterSplit | 0.072 | 0.2% | 0.13 | 4.98 |
| After | 0.012 | 0.0% | 0.01 | 0.06 |
| PostStep | 0.220 | 0.6% | 0.30 | 3.29 |
| 　PostDebris | 0.198 | 0.6% | 0.29 | 1.85 |
| 　PostWounds | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.002 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.019 | 0.1% | 0.00 | 3.20 |
| （枠の外） | 0.006 | 0.0% | | |

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 4.50 s: **115.4**（破片 0、GC0 0）: Weapons 111.1・SweepRun 71.7・RunPatch 29.0・RunScan 19.5・SweepSetup 19.1・RunOverlaps 18.2・SetupCandidates 14.9・Snap 4.4・SnapFind 3.8・RunFront 2.2・Control 1.9・Bepu 1.5・DriveFighter 1.0・ApplyControl 1.0・FighterStanding 0.8・MayTouch 0.6・SnapStiffness 0.6・Perception 0.5・FighterSplit 0.5・Cut 0.5
- 種 1 の 7.60 s: **115.0**（破片 0、GC0 0）: Weapons 110.8・SweepRun 62.1・RunScan 38.9・SweepSetup 27.9・SetupCandidates 21.8・RunPatch 13.8・Snap 4.8・RunOverlaps 4.4・SnapFind 4.1・RunFront 3.1・Bepu 2.1・Control 1.6・DriveFighter 1.1・ApplyControl 1.0・Cut 0.8・FighterStanding 0.7・MayTouch 0.7・SnapStiffness 0.6・PostStep 0.3・PostDebris 0.3・SetupRelative 0.3
- 種 1 の 4.48 s: **112.8**（破片 0、GC0 1）: Weapons 108.7・SweepRun 61.8・RunScan 37.8・SweepSetup 23.5・SetupCandidates 18.4・RunPatch 14.3・Snap 4.8・RunOverlaps 4.3・SnapFind 4.0・RunFront 2.6・Control 1.9・Bepu 1.5・DriveFighter 1.1・ApplyControl 1.1・FighterStanding 0.8・SnapStiffness 0.8・MayTouch 0.7・Perception 0.5・Disconnect 0.4・Cut 0.3
- 種 1 の 7.87 s: **111.4**（破片 0、GC0 1）: Weapons 106.3・SweepRun 47.1・SweepSetup 36.8・RunScan 32.1・SetupCandidates 28.9・RunPatch 7.5・Snap 3.4・SnapFind 2.9・Bepu 2.7・RunFront 2.4・Control 2.0・RunOverlaps 1.9・DriveFighter 1.2・ApplyControl 1.1・Cut 0.9・FighterStanding 0.8・MayTouch 0.8・RunCrush 0.7・BotRead 0.5・SnapStiffness 0.4・SetupRelative 0.3・PostStep 0.3・BotPerceive 0.3・PostDebris 0.3
- 種 1 の 4.52 s: **111.1**（破片 0、GC0 0）: Weapons 107.0・SweepRun 67.4・RunScan 36.9・SweepSetup 19.8・SetupCandidates 15.4・RunPatch 15.3・RunOverlaps 10.8・Snap 3.1・SnapFind 2.7・Control 2.0・RunFront 1.7・Bepu 1.5・DriveFighter 1.1・ApplyControl 1.1・FighterStanding 0.8・MayTouch 0.6・Perception 0.6・Cut 0.4・SnapStiffness 0.4
- 種 1 の 7.62 s: **111.0**（破片 0、GC0 0）: Weapons 106.4・SweepRun 56.0・RunScan 33.3・SweepSetup 28.7・SetupCandidates 22.3・RunPatch 13.2・RunOverlaps 4.3・Snap 4.3・RunFront 3.8・SnapFind 3.7・Bepu 2.1・Control 1.8・Cut 1.3・DriveFighter 1.1・ApplyControl 1.0・MayTouch 0.7・FighterStanding 0.7・SnapStiffness 0.5・PostStep 0.4・PostDebris 0.4・BotRead 0.4・Bend 0.4・RunCrush 0.3・SetupRelative 0.3
- 種 1 の 7.58 s: **107.5**（破片 0、GC0 1）: Weapons 103.1・SweepRun 51.5・RunScan 42.3・SweepSetup 29.6・SetupCandidates 23.1・RunPatch 3.8・Snap 3.6・SnapFind 3.1・RunFront 2.6・Bepu 2.1・Control 1.8・RunOverlaps 1.1・DriveFighter 1.1・ApplyControl 1.0・Cut 0.7・MayTouch 0.7・FighterStanding 0.7・SnapStiffness 0.5・BotRead 0.4・PostStep 0.3・PostDebris 0.3
- 種 1 の 12.30 s: **98.8**（破片 0、GC0 1）: Weapons 94.3・SweepRun 53.5・RunScan 49.0・SweepSetup 21.3・SetupCandidates 16.4・Bepu 2.5・Snap 1.9・RunPatch 1.8・SnapFind 1.7・Control 1.7・DriveFighter 1.1・ApplyControl 1.1・RunFront 1.0・Cut 0.7・FighterStanding 0.7・MayTouch 0.7・RunOverlaps 0.7・RunCrush 0.4・SetupRelative 0.3
- 種 1 の 7.33 s: **97.5**（破片 0、GC0 0）: Weapons 93.1・SweepRun 54.0・SweepSetup 22.2・RunScan 22.2・RunPatch 17.9・SetupCandidates 17.3・RunOverlaps 9.0・Snap 3.3・SnapFind 2.9・RunFront 2.7・Bepu 2.0・Control 1.6・DriveFighter 1.0・ApplyControl 1.0・Cut 0.9・MayTouch 0.7・FighterStanding 0.7・RunCrush 0.6・SnapStiffness 0.4・PostStep 0.3・PostDebris 0.3・FighterSplit 0.3
- 種 1 の 4.47 s: **97.3**（破片 0、GC0 0）: Weapons 93.3・SweepRun 48.2・RunScan 22.9・SweepSetup 22.2・SetupCandidates 17.4・RunPatch 12.3・RunOverlaps 7.4・Snap 4.8・SnapFind 4.1・RunFront 2.4・Control 1.8・Bepu 1.5・DriveFighter 1.1・ApplyControl 1.0・FighterStanding 0.8・SnapStiffness 0.7・Cut 0.7・MayTouch 0.7・Perception 0.5

## perf royale（2026-11-25 18:07、DESKTOP-2403GU4）

種 2 × 最大 60 秒、3600 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 192.5 s。物理 102・判断 73。
チェックサム（計測あり）: 57e565a2a3426c2f / 計測なし: 57e565a2a3426c2f（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 42.01 | 41.35 | 73.50 | 87.35 | 186.13 | 89.9% | 527 / 2 |
| 描画の詰め（CPU） | 11.23 | 10.33 | 20.02 | 23.72 | 103.21 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 1.752 | 4.2% | 2.00 | 2.58 |
| 　Perception | 0.230 | 0.5% | 0.29 | 0.82 |
| 　BotRead | 0.205 | 0.5% | 0.24 | 0.79 |
| 　BotPerceive | 0.076 | 0.2% | 0.11 | 0.66 |
| 　BotRead2 | 0.004 | 0.0% | 0.00 | 0.07 |
| 　BotChoose | 0.002 | 0.0% | 0.00 | 0.14 |
| 　BotExecute | 0.002 | 0.0% | 0.00 | 0.06 |
| 　DriveFighter | 1.148 | 2.7% | 1.16 | 1.93 |
| 　ApplyControl | 1.130 | 2.7% | 1.14 | 1.90 |
| 　FighterStamina | 0.173 | 0.4% | 0.18 | 0.92 |
| 　FighterStanding | 0.479 | 1.1% | 0.43 | 0.88 |
| 　FighterDown | 0.348 | 0.8% | 0.40 | 0.93 |
| 　ControlTail | 0.070 | 0.2% | 0.13 | 0.76 |
| PreBepu | 0.058 | 0.1% | 0.06 | 0.59 |
| 　Remember | 0.030 | 0.1% | 0.03 | 0.56 |
| Bepu | 3.253 | 7.7% | 3.56 | 5.11 |
| CanvasImpacts | 0.030 | 0.1% | 0.03 | 0.52 |
| FistHits | 0.021 | 0.0% | 0.02 | 0.09 |
| Weapons | 36.229 | 86.2% | 93.71 | 176.23 |
| 　MayTouch | 0.682 | 1.6% | 0.79 | 0.95 |
| 　SweepSetup | 12.854 | 30.6% | 21.77 | 31.16 |
| 　SetupRelative | 0.263 | 0.6% | 0.36 | 0.61 |
| 　SetupFar | 0.052 | 0.1% | 0.07 | 0.12 |
| 　SetupCandidates | 9.882 | 23.5% | 16.76 | 24.79 |
| 　SweepRun | 12.123 | 28.9% | 54.61 | 142.10 |
| 　RunScan | 7.631 | 18.2% | 26.21 | 79.55 |
| 　RunFront | 1.503 | 3.6% | 3.64 | 6.94 |
| 　RunOverlaps | 0.371 | 0.9% | 6.82 | 23.48 |
| 　RunPatch | 1.695 | 4.0% | 14.77 | 51.61 |
| 　RunHold | 0.009 | 0.0% | 0.17 | 0.81 |
| 　RunCrush | 0.194 | 0.5% | 0.34 | 1.84 |
| 　Snap | 1.612 | 3.8% | 3.05 | 7.25 |
| 　SnapStiffness | 0.206 | 0.5% | 0.39 | 1.34 |
| 　SnapFind | 1.399 | 3.3% | 2.65 | 5.89 |
| 　Bend | 0.099 | 0.2% | 0.40 | 1.62 |
| 　Cut | 0.167 | 0.4% | 1.20 | 22.15 |
| 　Debris | 0.002 | 0.0% | 0.02 | 0.06 |
| Disconnect | 0.006 | 0.0% | 0.01 | 1.50 |
| Kicks | 0.390 | 0.9% | 2.25 | 7.17 |
| Elbows | 0.002 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.063 | 0.1% | 0.26 | 2.04 |
| After | 0.011 | 0.0% | 0.01 | 0.06 |
| PostStep | 0.189 | 0.5% | 0.22 | 2.81 |
| 　PostDebris | 0.166 | 0.4% | 0.19 | 0.77 |
| 　PostWounds | 0.001 | 0.0% | 0.00 | 0.02 |
| 　PostSwings | 0.002 | 0.0% | 0.00 | 0.05 |
| 　PostRound | 0.020 | 0.0% | 0.02 | 2.56 |
| （枠の外） | 0.006 | 0.0% | | |

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 14.05 s: **186.1**（破片 1、GC0 0）: Weapons 176.2・SweepRun 142.1・RunScan 79.6・RunPatch 32.8・RunOverlaps 23.5・SweepSetup 22.6・SetupCandidates 17.5・Kicks 4.6・Bepu 2.5・Snap 2.5・SnapFind 2.2・Control 1.8・RunFront 1.2・DriveFighter 1.1・ApplyControl 1.1・RunHold 0.8・MayTouch 0.8・PostStep 0.5・PostDebris 0.4・FighterSplit 0.4・FighterDown 0.4・FighterStanding 0.4・Cut 0.4・SetupRelative 0.3・SnapStiffness 0.3
- 種 2 の 14.08 s: **152.4**（破片 1、GC0 0）: Weapons 142.3・SweepRun 100.4・RunScan 51.9・RunPatch 25.8・SweepSetup 25.4・SetupCandidates 19.7・RunOverlaps 19.0・Kicks 4.3・Bepu 2.7・Snap 2.2・SnapFind 1.9・Control 1.8・RunFront 1.3・DriveFighter 1.1・ApplyControl 1.1・MayTouch 0.8・FighterSplit 0.6・PostStep 0.5・PostDebris 0.5・FighterDown 0.4・FighterStanding 0.4・SetupRelative 0.4・Cut 0.3
- 種 2 の 14.07 s: **130.0**（破片 1、GC0 0）: Weapons 120.2・SweepRun 83.4・RunScan 57.5・SweepSetup 23.6・SetupCandidates 18.3・RunPatch 12.6・RunOverlaps 8.3・Kicks 4.6・Bepu 2.7・Snap 2.1・SnapFind 1.8・Control 1.8・RunFront 1.2・DriveFighter 1.2・ApplyControl 1.1・MayTouch 0.8・Cut 0.5・RunHold 0.5・PostStep 0.5・PostDebris 0.5・FighterDown 0.4・FighterStanding 0.4・SetupRelative 0.3
- 種 2 の 14.03 s: **129.7**（破片 1、GC0 1）: Weapons 120.0・SweepRun 85.7・RunScan 47.9・RunPatch 22.8・SweepSetup 21.0・SetupCandidates 16.4・RunOverlaps 11.6・Kicks 4.3・Snap 2.7・Bepu 2.6・SnapFind 2.3・Control 1.9・RunFront 1.4・Cut 1.3・DriveFighter 1.2・ApplyControl 1.2・MayTouch 0.8・PostStep 0.5・PostDebris 0.5・FighterDown 0.4・FighterStanding 0.4・SnapStiffness 0.3・SetupRelative 0.3・FighterSplit 0.3
- 種 2 の 13.95 s: **122.8**（破片 1、GC0 0）: Weapons 117.7・SweepRun 87.8・RunPatch 51.6・RunOverlaps 18.4・SweepSetup 15.0・RunScan 11.8・SetupCandidates 11.8・Snap 3.2・SnapFind 2.8・Bepu 2.7・RunFront 2.5・Control 1.6・DriveFighter 1.1・ApplyControl 1.1・MayTouch 0.7・PostStep 0.4・PostDebris 0.4・FighterDown 0.4・SnapStiffness 0.4・FighterStanding 0.4・RunHold 0.3
- 種 2 の 14.02 s: **121.7**（破片 1、GC0 0）: Weapons 112.4・SweepRun 77.9・RunScan 43.7・SweepSetup 20.6・RunPatch 19.3・SetupCandidates 16.0・RunOverlaps 10.7・Kicks 4.1・Snap 2.7・Bepu 2.6・SnapFind 2.3・Control 1.8・RunFront 1.6・DriveFighter 1.1・ApplyControl 1.1・MayTouch 0.8・Cut 0.6・PostStep 0.5・PostDebris 0.5・FighterDown 0.4・FighterStanding 0.4・SnapStiffness 0.3・SetupRelative 0.3
- 種 2 の 46.83 s: **110.8**（破片 1、GC0 0）: Weapons 98.7・SweepRun 72.6・RunPatch 33.5・SweepSetup 18.2・RunOverlaps 15.6・RunScan 15.0・SetupCandidates 13.9・Kicks 5.7・RunFront 4.6・Bepu 3.2・Snap 2.6・Control 2.4・SnapFind 2.2・Bend 1.4・DriveFighter 1.2・ApplyControl 1.2・MayTouch 0.8・Cut 0.7・Perception 0.5・FighterSplit 0.5・FighterDown 0.4・RunHold 0.4・FighterStanding 0.4・SnapStiffness 0.3・SetupRelative 0.3
- 種 2 の 55.83 s: **109.4**（破片 2、GC0 1）: Weapons 102.3・SweepRun 70.8・RunPatch 34.2・SweepSetup 17.0・RunOverlaps 15.7・SetupCandidates 13.0・RunScan 8.5・RunFront 6.7・Bepu 4.6・Snap 3.5・SnapFind 3.0・Cut 2.0・Control 1.9・DriveFighter 1.1・ApplyControl 1.1・MayTouch 0.8・Bend 0.8・RunCrush 0.7・RunHold 0.5・FighterSplit 0.5・SnapStiffness 0.4・FighterStanding 0.4・FighterDown 0.4
- 種 2 の 22.12 s: **105.8**（破片 1、GC0 0）: Weapons 100.4・SweepRun 46.9・RunScan 41.0・SweepSetup 27.2・SetupCandidates 21.1・Bepu 3.1・RunFront 2.4・Snap 2.4・SnapFind 2.1・Control 2.0・RunPatch 1.5・DriveFighter 1.2・ApplyControl 1.1・MayTouch 0.7・BotRead 0.5・FighterDown 0.4・Cut 0.4・SetupRelative 0.4・FighterStanding 0.4・BotPerceive 0.4・RunCrush 0.3
- 種 2 の 9.43 s: **101.7**（破片 0、GC0 1）: Weapons 96.8・SweepRun 43.5・SweepSetup 22.6・RunScan 17.7・SetupCandidates 17.4・RunPatch 14.2・RunOverlaps 5.0・RunFront 3.0・Snap 3.0・SnapFind 2.6・Bepu 2.1・Control 1.7・DriveFighter 1.1・ApplyControl 1.1・FighterSplit 0.8・FighterStanding 0.8・MayTouch 0.7・Cut 0.5・SetupRelative 0.4・SnapStiffness 0.4・RunHold 0.3

