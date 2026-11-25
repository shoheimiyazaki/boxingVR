## perf fist（2026-11-25 17:04、DESKTOP-2403GU4）

種 1,2 × 最大 40 秒、4800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 45.4 s。物理 102・判断 73。
チェックサム（計測あり）: 280f6b68c16111ec 5f96a6a5e72eebb2 / 計測なし: 280f6b68c16111ec 5f96a6a5e72eebb2（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 4.29 | 2.55 | 9.56 | 11.02 | 22.28 | 0.1% | 219 / 1 |
| 描画の詰め（CPU） | 5.14 | 5.27 | 8.15 | 8.88 | 115.84 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.539 | 12.6% | 0.59 | 1.20 |
| 　Perception | 0.063 | 1.5% | 0.07 | 0.37 |
| 　BotRead | 0.074 | 1.7% | 0.11 | 0.44 |
| 　BotPerceive | 0.034 | 0.8% | 0.05 | 0.37 |
| 　BotRead2 | 0.006 | 0.1% | 0.01 | 0.06 |
| 　BotChoose | 0.004 | 0.1% | 0.01 | 0.06 |
| 　BotExecute | 0.007 | 0.2% | 0.01 | 0.08 |
| 　DriveFighter | 0.389 | 9.1% | 0.40 | 1.03 |
| 　ApplyControl | 0.383 | 8.9% | 0.39 | 1.02 |
| 　FighterStamina | 0.061 | 1.4% | 0.06 | 0.66 |
| 　FighterStanding | 0.257 | 6.0% | 0.29 | 0.36 |
| 　FighterDown | 0.028 | 0.7% | 0.00 | 0.27 |
| 　ControlTail | 0.010 | 0.2% | 0.01 | 0.07 |
| PreBepu | 0.018 | 0.4% | 0.02 | 0.07 |
| 　Remember | 0.009 | 0.2% | 0.01 | 0.06 |
| Bepu | 0.819 | 19.1% | 0.99 | 1.56 |
| CanvasImpacts | 0.010 | 0.2% | 0.01 | 0.56 |
| FistHits | 0.003 | 0.1% | 0.01 | 0.02 |
| Weapons | 2.873 | 67.0% | 10.54 | 20.60 |
| 　MayTouch | 0.021 | 0.5% | 0.02 | 0.10 |
| 　SweepSetup | 1.247 | 29.1% | 5.02 | 6.27 |
| 　SetupRelative | 0.015 | 0.3% | 0.05 | 0.10 |
| 　SetupFar | 0.003 | 0.1% | 0.01 | 0.02 |
| 　SetupCandidates | 0.945 | 22.0% | 3.81 | 4.77 |
| 　SweepRun | 0.397 | 9.3% | 2.42 | 10.92 |
| 　RunScan | 0.266 | 6.2% | 1.60 | 5.29 |
| 　RunFront | 0.062 | 1.5% | 0.29 | 1.21 |
| 　RunOverlaps | 0.002 | 0.0% | 0.08 | 1.90 |
| 　RunPatch | 0.037 | 0.9% | 0.29 | 2.82 |
| 　RunHold | 0.000 | 0.0% | 0.00 | 0.02 |
| 　RunCrush | 0.007 | 0.2% | 0.02 | 0.24 |
| 　Snap | 0.000 | 0.0% | 0.00 | 0.01 |
| 　Bend | 0.010 | 0.2% | 0.63 | 17.00 |
| 　Cut | 0.043 | 1.0% | 0.23 | 1.40 |
| 　Debris | 0.000 | 0.0% | 0.00 | 0.06 |
| Disconnect | 0.001 | 0.0% | 0.00 | 0.01 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.01 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.52 |
| FighterSplit | 0.012 | 0.3% | 0.02 | 1.08 |
| After | 0.004 | 0.1% | 0.00 | 0.02 |
| PostStep | 0.005 | 0.1% | 0.02 | 0.10 |
| 　PostDebris | 0.004 | 0.1% | 0.01 | 0.10 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| （枠の外） | 0.004 | 0.1% | | |

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 7.43 s: **22.3**（破片 0、GC0 1）: Weapons 20.6・Bend 17.0・SweepSetup 1.8・SetupCandidates 1.4・Bepu 1.0・Control 0.6・SweepRun 0.5・DriveFighter 0.4・ApplyControl 0.4・RunScan 0.3
- 種 1 の 2.63 s: **20.2**（破片 0、GC0 0）: Weapons 18.7・Bend 12.0・SweepSetup 3.8・SetupCandidates 2.9・SweepRun 1.3・Bepu 0.9・RunScan 0.8・Control 0.6・DriveFighter 0.4・ApplyControl 0.4・RunFront 0.3
- 種 2 の 38.85 s: **20.2**（破片 0、GC0 0）: Weapons 18.5・SweepRun 10.9・RunScan 5.3・SweepSetup 4.9・SetupCandidates 3.7・RunPatch 2.8・RunOverlaps 1.9・Bepu 0.9・Control 0.7・RunFront 0.4・DriveFighter 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 2 の 39.98 s: **13.0**（破片 0、GC0 1）: Weapons 11.2・SweepSetup 5.0・SetupCandidates 3.7・SweepRun 3.6・RunScan 2.2・Bepu 1.0・Cut 0.6・Control 0.6・RunFront 0.6・RunPatch 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 8.03 s: **13.0**（破片 0、GC0 0）: Weapons 11.2・SweepSetup 5.3・SetupCandidates 4.0・SweepRun 3.4・RunScan 1.7・Bepu 1.0・RunFront 1.0・Control 0.8・Cut 0.5・DriveFighter 0.4・ApplyControl 0.4・RunPatch 0.4
- 種 2 の 39.90 s: **12.9**（破片 0、GC0 0）: Weapons 10.9・SweepRun 4.9・SweepSetup 3.8・SetupCandidates 2.8・RunScan 1.8・RunPatch 1.5・Bepu 1.1・RunOverlaps 1.0・Control 0.6・RunFront 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 39.97 s: **12.2**（破片 0、GC0 0）: Weapons 10.4・SweepSetup 4.9・SetupCandidates 3.6・SweepRun 3.3・RunScan 2.5・Bepu 1.1・Control 0.6・Cut 0.4・DriveFighter 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 1 の 8.07 s: **12.2**（破片 0、GC0 1）: Weapons 10.6・SweepSetup 5.5・SetupCandidates 4.2・SweepRun 2.3・RunScan 1.6・Bepu 1.0・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 0.77 s: **12.2**（破片 0、GC0 1）: Weapons 10.8・SweepSetup 4.3・SetupCandidates 3.4・SweepRun 1.3・RunScan 0.9・Bepu 0.7・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 38.87 s: **12.1**（破片 0、GC0 0）: Weapons 10.6・SweepSetup 4.7・SweepRun 3.8・SetupCandidates 3.6・RunScan 3.4・Bepu 0.8・Control 0.6・DriveFighter 0.4・ApplyControl 0.4

## perf katana（2026-11-25 17:06、DESKTOP-2403GU4）

種 1,2 × 最大 40 秒、4800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 55.7 s。物理 102・判断 73。
チェックサム（計測あり）: dee9e51fcd693fdc e1b6fa9397864923 / 計測なし: dee9e51fcd693fdc e1b6fa9397864923（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 8.82 | 6.16 | 24.97 | 41.85 | 78.04 | 11.0% | 69 / 1 |
| 描画の詰め（CPU） | 2.76 | 2.39 | 7.09 | 8.62 | 61.69 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.559 | 6.3% | 0.54 | 3.88 |
| 　Perception | 0.085 | 1.0% | 0.09 | 3.41 |
| 　BotRead | 0.018 | 0.2% | 0.03 | 0.13 |
| 　DriveFighter | 0.441 | 5.0% | 0.41 | 3.19 |
| 　ApplyControl | 0.431 | 4.9% | 0.40 | 1.06 |
| 　FighterStamina | 0.064 | 0.7% | 0.07 | 0.69 |
| 　FighterStanding | 0.172 | 2.0% | 0.27 | 0.41 |
| 　FighterDown | 0.152 | 1.7% | 0.02 | 0.48 |
| 　ControlTail | 0.011 | 0.1% | 0.01 | 0.03 |
| PreBepu | 0.022 | 0.2% | 0.02 | 0.07 |
| 　Remember | 0.010 | 0.1% | 0.01 | 0.05 |
| Bepu | 0.875 | 9.9% | 0.81 | 1.40 |
| CanvasImpacts | 0.011 | 0.1% | 0.02 | 0.67 |
| FistHits | 0.002 | 0.0% | 0.00 | 0.02 |
| Weapons | 7.282 | 82.6% | 46.52 | 76.33 |
| 　MayTouch | 0.045 | 0.5% | 0.05 | 0.13 |
| 　SweepSetup | 2.395 | 27.2% | 9.45 | 12.79 |
| 　SetupRelative | 0.051 | 0.6% | 0.11 | 0.16 |
| 　SetupFar | 0.010 | 0.1% | 0.02 | 0.06 |
| 　SetupCandidates | 1.861 | 21.1% | 7.39 | 10.00 |
| 　SweepRun | 2.811 | 31.9% | 27.14 | 56.89 |
| 　RunScan | 2.030 | 23.0% | 21.17 | 55.84 |
| 　RunFront | 0.313 | 3.6% | 1.33 | 2.92 |
| 　RunOverlaps | 0.053 | 0.6% | 1.25 | 7.99 |
| 　RunPatch | 0.253 | 2.9% | 2.67 | 15.33 |
| 　RunHold | 0.002 | 0.0% | 0.02 | 0.44 |
| 　RunCrush | 0.046 | 0.5% | 0.10 | 3.04 |
| 　Snap | 0.475 | 5.4% | 1.29 | 5.73 |
| 　SnapStiffness | 0.061 | 0.7% | 0.19 | 1.58 |
| 　SnapFind | 0.412 | 4.7% | 1.09 | 4.94 |
| 　Bend | 0.030 | 0.3% | 0.05 | 0.47 |
| 　Cut | 0.023 | 0.3% | 0.20 | 0.79 |
| 　Debris | 0.001 | 0.0% | 0.01 | 0.04 |
| Disconnect | 0.002 | 0.0% | 0.02 | 0.40 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.20 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.016 | 0.2% | 0.04 | 1.14 |
| After | 0.005 | 0.1% | 0.01 | 0.06 |
| PostStep | 0.038 | 0.4% | 2.64 | 63.28 |
| 　PostDebris | 0.010 | 0.1% | 0.03 | 0.09 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.02 |
| 　PostRound | 0.026 | 0.3% | 2.61 | 63.28 |
| （枠の外） | 0.004 | 0.1% | | |

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 34.37 s: **78.0**（破片 0、GC0 0）: Weapons 76.3・SweepRun 56.9・RunScan 55.8・SweepSetup 11.0・SetupCandidates 8.6・Bepu 0.9・RunFront 0.6・Control 0.6・Cut 0.5・Snap 0.5・DriveFighter 0.4・SnapFind 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 2 の 17.40 s: **73.5**（破片 0、GC0 1）: PostStep 63.3・PostRound 63.3・Weapons 8.6・SweepSetup 3.8・SetupCandidates 2.9・SweepRun 1.6・Bepu 1.0・RunScan 0.9・Control 0.6・DriveFighter 0.4・ApplyControl 0.4・RunFront 0.4・FighterDown 0.3
- 種 1 の 31.27 s: **66.8**（破片 0、GC0 1）: PostStep 61.8・PostRound 61.8・Weapons 3.2・SweepRun 1.3・RunScan 1.2・Bepu 1.1・SweepSetup 0.9・SetupCandidates 0.6・Control 0.6・DriveFighter 0.5・ApplyControl 0.5
- 種 2 の 34.38 s: **64.8**（破片 0、GC0 0）: Weapons 63.3・SweepRun 43.3・RunScan 41.9・SweepSetup 11.1・SetupCandidates 8.6・Bepu 0.9・Snap 0.8・SnapFind 0.7・RunFront 0.7・Control 0.5・RunPatch 0.4・DriveFighter 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 1 の 37.37 s: **64.8**（破片 0、GC0 0）: Weapons 63.3・SweepRun 41.3・RunScan 38.7・SweepSetup 9.0・SetupCandidates 7.1・Snap 1.2・RunFront 1.0・SnapFind 1.0・Bepu 0.9・RunPatch 0.8・Control 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 23.27 s: **61.6**（破片 0、GC0 0）: Weapons 60.2・SweepRun 37.7・RunScan 29.7・SweepSetup 10.7・SetupCandidates 8.2・RunPatch 3.4・RunOverlaps 2.5・RunFront 1.1・Snap 1.1・SnapFind 0.9・Bepu 0.7・Cut 0.6・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.33 s: **59.8**（破片 0、GC0 0）: Weapons 58.3・SweepRun 30.4・RunScan 24.0・SweepSetup 12.8・SetupCandidates 10.0・RunPatch 2.6・RunFront 1.5・Snap 1.3・SnapFind 1.1・Bepu 0.9・RunOverlaps 0.8・Cut 0.5・Control 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 34.15 s: **59.1**（破片 0、GC0 0）: Weapons 57.8・SweepRun 38.2・RunScan 36.3・SweepSetup 9.5・SetupCandidates 7.4・Snap 0.7・Bepu 0.7・RunFront 0.6・SnapFind 0.6・Control 0.5・RunPatch 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.38 s: **58.1**（破片 0、GC0 0）: Weapons 56.6・SweepRun 35.8・RunScan 29.8・SweepSetup 8.5・SetupCandidates 6.8・RunPatch 3.0・RunOverlaps 1.7・Snap 1.3・SnapFind 1.1・Bepu 0.9・RunFront 0.8・Control 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.35 s: **57.4**（破片 0、GC0 0）: Weapons 55.9・SweepRun 35.8・RunScan 29.7・SweepSetup 8.9・SetupCandidates 7.0・RunPatch 2.6・RunOverlaps 1.4・RunFront 1.4・Snap 1.3・SnapFind 1.1・Bepu 0.9・Control 0.5・DriveFighter 0.4・ApplyControl 0.4

FAILED vfc2 
FAILED royale 
