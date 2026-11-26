## perf fist（2026-11-25 18:09、DESKTOP-2403GU4）

種 1 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 9.9 s。物理 102・判断 73。
チェックサム（計測あり）: 280f6b68c16111ec / 計測なし: 280f6b68c16111ec（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 2.74 | 1.94 | 6.41 | 7.49 | 9.11 | 0.0% | 11 / 1 |
| 描画の詰め（CPU） | 1.29 | 0.98 | 4.03 | 6.71 | 109.94 | | |
| 　うちボクセル（FillVoxels） | 1.26 | 0.96 | 3.98 | 6.67 | 84.01 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.534 | 19.5% | 0.66 | 1.62 |
| 　Perception | 0.049 | 1.8% | 0.07 | 0.34 |
| 　BotRead | 0.075 | 2.7% | 0.17 | 0.95 |
| 　BotPerceive | 0.036 | 1.3% | 0.11 | 0.90 |
| 　BotRead2 | 0.006 | 0.2% | 0.01 | 0.05 |
| 　BotChoose | 0.003 | 0.1% | 0.01 | 0.10 |
| 　BotExecute | 0.006 | 0.2% | 0.01 | 0.05 |
| 　DriveFighter | 0.396 | 14.5% | 0.40 | 1.56 |
| 　ApplyControl | 0.391 | 14.3% | 0.40 | 1.55 |
| 　FighterStamina | 0.063 | 2.3% | 0.07 | 1.19 |
| 　FighterStanding | 0.248 | 9.1% | 0.29 | 0.40 |
| 　FighterDown | 0.042 | 1.5% | 0.00 | 0.27 |
| 　ControlTail | 0.011 | 0.4% | 0.01 | 0.07 |
| PreBepu | 0.018 | 0.7% | 0.02 | 0.06 |
| 　Remember | 0.010 | 0.4% | 0.01 | 0.06 |
| Bepu | 0.899 | 32.9% | 0.99 | 1.83 |
| CanvasImpacts | 0.011 | 0.4% | 0.01 | 0.31 |
| FistHits | 0.003 | 0.1% | 0.01 | 0.03 |
| Weapons | 1.245 | 45.5% | 6.28 | 7.22 |
| 　MayTouch | 0.020 | 0.7% | 0.02 | 0.06 |
| 　SweepSetup | 0.434 | 15.8% | 2.79 | 4.98 |
| 　SetupRelative | 0.010 | 0.4% | 0.05 | 0.07 |
| 　SetupFar | 0.015 | 0.5% | 0.07 | 0.10 |
| 　SetupCandidates | 0.264 | 9.7% | 1.69 | 2.63 |
| 　SweepRun | 0.253 | 9.2% | 2.28 | 4.17 |
| 　RunScan | 0.149 | 5.4% | 1.06 | 3.47 |
| 　RunFront | 0.060 | 2.2% | 0.66 | 1.72 |
| 　RunOverlaps | 0.001 | 0.0% | 0.02 | 0.48 |
| 　RunPatch | 0.023 | 0.8% | 0.27 | 0.94 |
| 　RunHold | 0.000 | 0.0% | 0.00 | 0.01 |
| 　RunCrush | 0.004 | 0.1% | 0.04 | 0.14 |
| 　RunCut | 0.000 | 0.0% | 0.00 | 0.04 |
| 　RunSeparate | 0.010 | 0.4% | 0.16 | 0.48 |
| 　RunMass | 0.000 | 0.0% | 0.00 | 0.05 |
| 　Snap | 0.000 | 0.0% | 0.00 | 0.01 |
| 　Bend | 0.004 | 0.1% | 0.03 | 0.49 |
| 　Cut | 0.016 | 0.6% | 0.20 | 0.52 |
| 　Debris | 0.000 | 0.0% | 0.00 | 0.06 |
| 　WeaponClash | 0.299 | 10.9% | 1.89 | 2.80 |
| 　ClashAfter | 0.001 | 0.0% | 0.01 | 0.07 |
| 　ClashPush | 0.136 | 5.0% | 0.56 | 0.75 |
| Disconnect | 0.000 | 0.0% | 0.00 | 0.02 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.02 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.02 |
| FighterSplit | 0.013 | 0.5% | 0.01 | 2.25 |
| After | 0.005 | 0.2% | 0.00 | 0.02 |
| PostStep | 0.004 | 0.1% | 0.00 | 0.08 |
| 　PostDebris | 0.002 | 0.1% | 0.00 | 0.08 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.03 |
| 　PostRound | 0.000 | 0.0% | 0.00 | 0.01 |
| 　RenderParts | 0.145 | 5.3% | 0.43 | 11.67 |
| 　RenderBridges | 1.102 | 40.3% | 2.54 | 64.00 |
| （枠の外） | 0.004 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 9 / 57、Candidates 7862 / 49976、ScanSamples 35 / 234、ChunksTested 421 / 3088、CellsTested 0 / 0、BridgeCells 0 / 0、FilledRebuildCells 8 / 208、BeamRebuildCells 16 / 60

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 8.03 s: **9.1**（破片 0、GC0 0）: Weapons 7.2・SweepRun 3.6・WeaponClash 2.5・SweepSetup 2.4・RunFront 1.5・SetupCandidates 1.4・RunScan 1.4・RenderBridges 1.2・Bepu 1.1・Control 0.8・ClashPush 0.5・DriveFighter 0.4・ApplyControl 0.4・RunPatch 0.4・RenderParts 0.3
- 種 1 の 8.68 s: **8.7**（破片 0、GC0 0）: Weapons 7.2・SweepSetup 4.0・SetupCandidates 2.6・WeaponClash 2.4・SweepRun 2.0・RenderBridges 1.1・RunScan 1.0・Bepu 0.9・ClashPush 0.7・Control 0.6・RunFront 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 18.15 s: **8.6**（破片 0、GC0 0）: Weapons 7.2・SweepRun 4.2・RunScan 3.5・WeaponClash 2.4・SweepSetup 1.7・RenderBridges 1.3・SetupCandidates 0.9・Bepu 0.8・Control 0.7・ClashPush 0.7・RenderParts 0.5・DriveFighter 0.4・ApplyControl 0.4・RunFront 0.3
- 種 1 の 0.75 s: **8.5**（破片 0、GC0 1）: Weapons 7.0・SweepSetup 5.0・RenderBridges 3.9・WeaponClash 1.8・SetupCandidates 1.8・SweepRun 0.9・Bepu 0.8・ClashPush 0.7・RunScan 0.6・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 8.65 s: **8.4**（破片 0、GC0 0）: Weapons 7.0・SweepSetup 3.5・WeaponClash 2.8・SetupCandidates 2.3・SweepRun 2.2・RenderBridges 1.1・RunScan 1.0・Bepu 0.8・RunFront 0.7・ClashPush 0.7・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 8.63 s: **8.2**（破片 0、GC0 0）: Weapons 6.7・SweepSetup 3.3・WeaponClash 2.6・SetupCandidates 2.2・SweepRun 2.1・RenderBridges 1.3・RunScan 1.0・Bepu 0.9・ClashPush 0.7・RunFront 0.6・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 8.70 s: **8.2**（破片 0、GC0 0）: Weapons 6.7・SweepSetup 3.7・SetupCandidates 2.4・WeaponClash 2.2・SweepRun 1.7・RenderBridges 1.3・RunScan 0.9・Bepu 0.9・ClashPush 0.7・Control 0.6・RunFront 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 17.07 s: **8.1**（破片 0、GC0 0）: Weapons 5.7・SweepSetup 2.2・SweepRun 2.1・Bepu 1.7・RenderBridges 1.5・SetupCandidates 1.3・RunScan 0.8・WeaponClash 0.8・RunFront 0.7・Control 0.7・Cut 0.5・DriveFighter 0.4・ApplyControl 0.4・ClashPush 0.3・RunPatch 0.3
- 種 1 の 8.72 s: **8.1**（破片 0、GC0 0）: Weapons 6.6・SweepSetup 3.7・SetupCandidates 2.6・WeaponClash 2.5・SweepRun 1.7・RenderBridges 1.2・Bepu 0.9・RunScan 0.8・ClashPush 0.7・Control 0.6・RunFront 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 8.05 s: **8.1**（破片 0、GC0 0）: Weapons 6.4・SweepSetup 2.8・SweepRun 2.5・WeaponClash 2.2・SetupCandidates 1.7・RunScan 1.3・RenderBridges 1.2・Bepu 1.0・RunFront 0.7・ClashPush 0.6・Control 0.6・DriveFighter 0.4・ApplyControl 0.4

## perf fist（2026-11-25 18:09、DESKTOP-2403GU4）

種 2 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 11.7 s。物理 102・判断 73。
チェックサム（計測あり）: 5f96a6a5e72eebb2 / 計測なし: 5f96a6a5e72eebb2（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 3.41 | 3.05 | 6.44 | 7.29 | 14.79 | 0.0% | 15 / 1 |
| 描画の詰め（CPU） | 1.37 | 1.19 | 2.70 | 5.68 | 109.09 | | |
| 　うちボクセル（FillVoxels） | 1.34 | 1.17 | 2.63 | 5.64 | 83.29 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.567 | 16.6% | 0.61 | 4.14 |
| 　Perception | 0.084 | 2.5% | 0.10 | 0.39 |
| 　BotRead | 0.080 | 2.3% | 0.09 | 3.69 |
| 　BotPerceive | 0.036 | 1.1% | 0.04 | 3.63 |
| 　BotRead2 | 0.007 | 0.2% | 0.01 | 0.06 |
| 　BotChoose | 0.004 | 0.1% | 0.01 | 0.04 |
| 　BotExecute | 0.008 | 0.2% | 0.01 | 0.06 |
| 　DriveFighter | 0.389 | 11.4% | 0.40 | 0.50 |
| 　ApplyControl | 0.383 | 11.2% | 0.40 | 0.50 |
| 　FighterStamina | 0.063 | 1.9% | 0.07 | 0.15 |
| 　FighterStanding | 0.267 | 7.8% | 0.29 | 0.37 |
| 　FighterDown | 0.015 | 0.4% | 0.00 | 0.18 |
| 　ControlTail | 0.012 | 0.3% | 0.01 | 0.05 |
| PreBepu | 0.019 | 0.5% | 0.02 | 0.07 |
| 　Remember | 0.009 | 0.3% | 0.01 | 0.05 |
| Bepu | 0.804 | 23.6% | 1.02 | 1.35 |
| CanvasImpacts | 0.010 | 0.3% | 0.01 | 0.05 |
| FistHits | 0.003 | 0.1% | 0.00 | 0.04 |
| Weapons | 1.975 | 57.9% | 6.51 | 13.03 |
| 　MayTouch | 0.021 | 0.6% | 0.02 | 0.06 |
| 　SweepSetup | 0.771 | 22.6% | 2.22 | 3.71 |
| 　SetupRelative | 0.017 | 0.5% | 0.04 | 0.10 |
| 　SetupFar | 0.025 | 0.7% | 0.07 | 0.10 |
| 　SetupCandidates | 0.467 | 13.7% | 1.24 | 3.02 |
| 　SweepRun | 0.509 | 14.9% | 2.99 | 9.37 |
| 　RunScan | 0.281 | 8.3% | 1.52 | 3.82 |
| 　RunFront | 0.130 | 3.8% | 0.60 | 1.41 |
| 　RunOverlaps | 0.002 | 0.1% | 0.14 | 1.66 |
| 　RunPatch | 0.051 | 1.5% | 0.49 | 2.79 |
| 　RunHold | 0.000 | 0.0% | 0.00 | 0.02 |
| 　RunCrush | 0.010 | 0.3% | 0.06 | 0.72 |
| 　RunCut | 0.000 | 0.0% | 0.01 | 0.06 |
| 　RunSeparate | 0.021 | 0.6% | 0.10 | 0.37 |
| 　RunMass | 0.000 | 0.0% | 0.01 | 0.10 |
| 　Snap | 0.000 | 0.0% | 0.00 | 0.01 |
| 　Bend | 0.005 | 0.2% | 0.07 | 0.59 |
| 　Cut | 0.032 | 0.9% | 0.23 | 2.61 |
| 　Debris | 0.000 | 0.0% | 0.01 | 0.12 |
| 　WeaponClash | 0.572 | 16.8% | 1.76 | 2.98 |
| 　ClashAfter | 0.001 | 0.0% | 0.01 | 0.02 |
| 　ClashPush | 0.252 | 7.4% | 0.55 | 0.74 |
| Disconnect | 0.001 | 0.0% | 0.00 | 0.01 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.01 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.02 |
| FighterSplit | 0.012 | 0.4% | 0.04 | 2.25 |
| After | 0.004 | 0.1% | 0.01 | 0.02 |
| PostStep | 0.007 | 0.2% | 0.03 | 0.09 |
| 　PostDebris | 0.006 | 0.2% | 0.03 | 0.09 |
| 　PostRound | 0.000 | 0.0% | 0.00 | 0.04 |
| 　RenderParts | 0.157 | 4.6% | 0.26 | 11.67 |
| 　RenderBridges | 1.169 | 34.3% | 1.39 | 63.28 |
| （枠の外） | 0.004 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 16 / 50、Candidates 14438 / 46165、ScanSamples 62 / 364、ChunksTested 797 / 4995、CellsTested 0 / 0、BridgeCells 0 / 0、FilledRebuildCells 6 / 104、BeamRebuildCells 20 / 592

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 38.85 s: **14.8**（破片 0、GC0 0）: Weapons 13.0・SweepRun 9.4・RunScan 3.8・WeaponClash 3.0・RunPatch 2.8・SweepSetup 2.1・RunOverlaps 1.7・RenderBridges 1.2・SetupCandidates 1.1・Bepu 0.9・Control 0.7・ClashPush 0.7・RunFront 0.7・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 8.03 s: **10.5**（破片 0、GC0 0）: Weapons 8.7・SweepRun 4.0・Cut 2.6・RunPatch 1.8・RenderBridges 1.5・RunScan 1.0・WeaponClash 1.0・Bepu 0.8・SweepSetup 0.7・Control 0.6・Bend 0.6・RunOverlaps 0.5・RunFront 0.4・DriveFighter 0.4・ApplyControl 0.4・ClashPush 0.3
- 種 2 の 15.05 s: **9.4**（破片 0、GC0 1）: Weapons 7.6・SweepSetup 3.7・SweepRun 2.4・WeaponClash 2.4・RunScan 1.6・SetupCandidates 1.5・RenderBridges 1.2・Bepu 1.0・Control 0.8・ClashPush 0.7・RunFront 0.5・RenderParts 0.5・DriveFighter 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 2 の 39.90 s: **8.7**（破片 0、GC0 0）: Weapons 6.7・SweepRun 4.2・RenderBridges 2.3・RunPatch 1.4・SweepSetup 1.3・RunScan 1.2・Bepu 1.1・RunOverlaps 0.7・RenderParts 0.7・RunFront 0.7・SetupCandidates 0.6・WeaponClash 0.6・Control 0.6・ClashPush 0.5・DriveFighter 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 2 の 3.92 s: **8.7**（破片 0、GC0 0）: Weapons 7.0・SweepRun 3.1・WeaponClash 2.6・SweepSetup 2.6・RunScan 2.5・SetupCandidates 1.6・RenderBridges 1.3・Bepu 1.1・ClashPush 0.7・Control 0.6・RenderParts 0.5・DriveFighter 0.4・ApplyControl 0.4・RunFront 0.4
- 種 2 の 39.98 s: **8.4**（破片 0、GC0 0）: Weapons 6.5・SweepRun 3.8・RunScan 1.8・RenderBridges 1.6・SweepSetup 1.6・WeaponClash 1.4・RunFront 1.1・Bepu 1.1・Control 0.7・SetupCandidates 0.6・RunPatch 0.4・DriveFighter 0.4・ApplyControl 0.4・ClashPush 0.3・RenderParts 0.3・Cut 0.3
- 種 2 の 38.83 s: **8.2**（破片 0、GC0 0）: Weapons 6.6・SweepRun 3.1・WeaponClash 2.7・SweepSetup 2.2・SetupCandidates 1.3・RunScan 1.2・RenderBridges 1.2・RunFront 1.2・Bepu 0.9・ClashPush 0.7・Control 0.6・RenderParts 0.5・RunPatch 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 3.95 s: **8.0**（破片 0、GC0 0）: Weapons 6.3・SweepRun 2.7・WeaponClash 2.3・SweepSetup 2.3・RunScan 2.1・RenderBridges 2.1・SetupCandidates 1.3・Bepu 1.0・ClashPush 0.7・Control 0.6・RunFront 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 39.92 s: **8.0**（破片 0、GC0 0）: Weapons 6.1・SweepRun 3.5・RunScan 2.0・SweepSetup 1.6・RenderBridges 1.5・Bepu 1.1・SetupCandidates 0.9・RunPatch 0.8・WeaponClash 0.7・Control 0.6・DriveFighter 0.4・ApplyControl 0.4・ClashPush 0.4・RunOverlaps 0.3
- 種 2 の 38.87 s: **8.0**（破片 0、GC0 0）: Weapons 6.4・SweepRun 3.3・RunScan 2.7・WeaponClash 2.7・SweepSetup 2.1・SetupCandidates 1.2・RenderBridges 1.0・Bepu 0.9・Control 0.6・ClashPush 0.6・DriveFighter 0.4・ApplyControl 0.4・RunFront 0.3

## perf katana（2026-11-25 18:10、DESKTOP-2403GU4）

種 1 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 17.4 s。物理 102・判断 73。
チェックサム（計測あり）: dee9e51fcd693fdc / 計測なし: dee9e51fcd693fdc（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 6.44 | 4.52 | 18.08 | 29.23 | 117.53 | 6.6% | 26 / 1 |
| 描画の詰め（CPU） | 0.76 | 0.63 | 1.65 | 2.56 | 63.48 | | |
| 　うちボクセル（FillVoxels） | 0.72 | 0.61 | 1.62 | 2.42 | 39.27 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.565 | 8.8% | 0.55 | 3.34 |
| 　Perception | 0.067 | 1.0% | 0.09 | 0.38 |
| 　BotRead | 0.019 | 0.3% | 0.04 | 0.08 |
| 　DriveFighter | 0.462 | 7.2% | 0.42 | 3.24 |
| 　ApplyControl | 0.446 | 6.9% | 0.41 | 1.07 |
| 　FighterStamina | 0.065 | 1.0% | 0.07 | 0.69 |
| 　FighterStanding | 0.216 | 3.4% | 0.28 | 0.43 |
| 　FighterDown | 0.122 | 1.9% | 0.01 | 0.29 |
| 　ControlTail | 0.013 | 0.2% | 0.01 | 0.44 |
| PreBepu | 0.021 | 0.3% | 0.02 | 0.04 |
| 　Remember | 0.011 | 0.2% | 0.01 | 0.03 |
| Bepu | 0.856 | 13.3% | 0.79 | 1.21 |
| CanvasImpacts | 0.011 | 0.2% | 0.01 | 0.51 |
| FistHits | 0.002 | 0.0% | 0.00 | 0.02 |
| Weapons | 4.892 | 76.0% | 31.22 | 41.48 |
| 　MayTouch | 0.047 | 0.7% | 0.05 | 0.13 |
| 　SweepSetup | 1.303 | 20.3% | 5.85 | 8.97 |
| 　SetupRelative | 0.035 | 0.5% | 0.10 | 0.14 |
| 　SetupFar | 0.033 | 0.5% | 0.12 | 0.16 |
| 　SetupCandidates | 0.859 | 13.4% | 3.91 | 6.27 |
| 　SweepRun | 2.322 | 36.1% | 19.86 | 34.82 |
| 　RunScan | 1.561 | 24.3% | 13.32 | 26.74 |
| 　RunFront | 0.342 | 5.3% | 1.66 | 3.34 |
| 　RunOverlaps | 0.045 | 0.7% | 1.11 | 7.30 |
| 　RunPatch | 0.237 | 3.7% | 2.85 | 15.55 |
| 　RunHold | 0.001 | 0.0% | 0.03 | 0.45 |
| 　RunCrush | 0.041 | 0.6% | 0.07 | 0.84 |
| 　RunCut | 0.001 | 0.0% | 0.02 | 0.21 |
| 　RunSeparate | 0.059 | 0.9% | 0.47 | 1.58 |
| 　RunMass | 0.003 | 0.1% | 0.05 | 0.38 |
| 　Snap | 0.400 | 6.2% | 1.55 | 6.24 |
| 　SnapStiffness | 0.054 | 0.8% | 0.33 | 4.02 |
| 　SnapFind | 0.344 | 5.3% | 1.21 | 2.44 |
| 　Bend | 0.021 | 0.3% | 0.05 | 0.32 |
| 　Cut | 0.011 | 0.2% | 0.07 | 0.44 |
| 　Debris | 0.001 | 0.0% | 0.01 | 0.07 |
| 　WeaponClash | 0.471 | 7.3% | 3.73 | 9.80 |
| 　ClashAfter | 0.001 | 0.0% | 0.00 | 0.03 |
| 　ClashPush | 0.017 | 0.3% | 0.00 | 0.25 |
| 　Bridges | 0.838 | 13.0% | 6.95 | 12.42 |
| 　BridgeRefresh | 0.588 | 9.1% | 3.61 | 5.58 |
| Disconnect | 0.003 | 0.0% | 0.02 | 0.39 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.01 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.016 | 0.2% | 0.03 | 1.14 |
| After | 0.004 | 0.1% | 0.00 | 0.04 |
| PostStep | 0.059 | 0.9% | 4.78 | 113.46 |
| 　PostDebris | 0.011 | 0.2% | 0.05 | 0.09 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.047 | 0.7% | 4.73 | 113.46 |
| 　RenderParts | 0.124 | 1.9% | 0.26 | 11.24 |
| 　RenderFragments | 0.001 | 0.0% | 0.00 | 0.01 |
| 　RenderBridges | 0.570 | 8.9% | 1.39 | 20.03 |
| （枠の外） | 0.004 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 24 / 93、Candidates 20091 / 96173、ScanSamples 276 / 2087、ChunksTested 4967 / 44938、CellsTested 0 / 0、BridgeCells 469 / 5470、FilledRebuildCells 1669 / 20002、BeamRebuildCells 3416 / 40716

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 31.27 s: **117.5**（破片 0、GC0 1）: PostStep 113.5・PostRound 113.5・RenderBridges 8.0・Weapons 2.4・SweepRun 1.2・Bepu 1.1・RunScan 1.0・Control 0.6・RenderParts 0.6・SweepSetup 0.6・DriveFighter 0.5・ApplyControl 0.5・SetupCandidates 0.4
- 種 1 の 37.37 s: **43.0**（破片 0、GC0 0）: Weapons 41.5・SweepRun 29.9・RunScan 26.7・Bridges 8.4・WeaponClash 7.5・SweepSetup 6.0・BridgeRefresh 4.2・SetupCandidates 4.0・RunFront 1.6・RenderBridges 1.2・Snap 1.2・SnapFind 1.0・RunPatch 0.8・Bepu 0.8・Control 0.5・DriveFighter 0.4・ApplyControl 0.4・RunSeparate 0.3
- 種 1 の 37.35 s: **41.9**（破片 0、GC0 0）: Weapons 40.5・SweepRun 28.5・RunScan 22.1・Bridges 7.3・SweepSetup 6.3・WeaponClash 6.0・SetupCandidates 4.3・BridgeRefresh 4.1・RunPatch 2.6・RunFront 1.8・RunOverlaps 1.4・RenderBridges 1.3・Snap 1.3・SnapFind 1.1・Bepu 0.8・Control 0.5・DriveFighter 0.4・ApplyControl 0.4・RunSeparate 0.4
- 種 1 の 3.15 s: **40.6**（破片 1、GC0 0）: Weapons 39.3・SweepRun 34.8・RunPatch 15.6・RunScan 8.9・RunOverlaps 7.3・WeaponClash 4.4・SweepSetup 2.1・Bridges 1.9・BridgeRefresh 1.8・SetupCandidates 1.3・RenderBridges 0.9・Bepu 0.7・Control 0.5・RunHold 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.33 s: **40.5**（破片 0、GC0 0）: Weapons 39.0・SweepRun 23.3・RunScan 16.2・Bridges 12.4・SweepSetup 9.0・SetupCandidates 6.0・BridgeRefresh 4.9・RunPatch 2.7・RunFront 2.2・Snap 1.3・SnapFind 1.2・WeaponClash 1.2・RenderBridges 0.9・RunSeparate 0.9・Bepu 0.8・RunOverlaps 0.7・Control 0.6・RenderParts 0.5・DriveFighter 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 1 の 37.38 s: **40.2**（破片 0、GC0 0）: Weapons 38.6・SweepRun 27.1・RunScan 20.7・Bridges 7.8・WeaponClash 7.3・SweepSetup 5.8・BridgeRefresh 4.1・SetupCandidates 3.9・RunPatch 3.1・RenderBridges 1.6・RunOverlaps 1.4・RunFront 1.3・Snap 1.3・SnapFind 1.1・Bepu 0.9・Control 0.5・DriveFighter 0.4・ApplyControl 0.4・RunSeparate 0.3
- 種 1 の 37.10 s: **38.6**（破片 0、GC0 0）: Weapons 37.1・SweepRun 25.0・RunPatch 8.8・Bridges 8.0・RunScan 7.8・SweepSetup 6.2・SetupCandidates 4.1・RunOverlaps 3.9・BridgeRefresh 3.3・RunFront 2.4・Snap 2.0・WeaponClash 1.9・SnapFind 1.8・RunSeparate 1.1・RenderBridges 0.9・Bepu 0.8・Control 0.6・RenderParts 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.32 s: **36.1**（破片 0、GC0 0）: Weapons 34.6・SweepRun 19.5・Bridges 10.0・RunScan 9.4・SweepSetup 8.8・SetupCandidates 6.3・RunPatch 4.5・BridgeRefresh 4.1・WeaponClash 2.3・RunFront 2.2・Snap 1.9・SnapFind 1.6・RenderBridges 1.6・RunSeparate 1.6・RunOverlaps 1.3・Bepu 0.8・Control 0.5・RenderParts 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.40 s: **35.5**（破片 0、GC0 0）: Weapons 33.8・SweepRun 23.6・RunScan 16.1・Bridges 7.3・WeaponClash 7.3・SweepSetup 5.0・RunPatch 4.1・BridgeRefresh 3.8・SetupCandidates 3.3・RenderBridges 1.9・RunOverlaps 1.8・Snap 1.2・RunFront 1.0・SnapFind 1.0・Bepu 0.8・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.42 s: **34.7**（破片 0、GC0 0）: Weapons 33.3・SweepRun 21.1・RunScan 13.1・Bridges 8.4・SweepSetup 6.3・WeaponClash 4.8・SetupCandidates 4.1・BridgeRefresh 4.1・RunPatch 3.1・RunFront 2.4・RunOverlaps 1.7・RenderBridges 1.4・Snap 1.4・SnapFind 1.2・Bepu 0.8・RenderParts 0.6・Control 0.5・RunSeparate 0.5・DriveFighter 0.4・ApplyControl 0.4

## perf katana（2026-11-25 18:11、DESKTOP-2403GU4）

種 2 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 21.6 s。物理 102・判断 73。
チェックサム（計測あり）: e1b6fa9397864923 / 計測なし: e1b6fa9397864923（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 8.27 | 7.00 | 19.77 | 32.31 | 117.74 | 7.5% | 37 / 2 |
| 描画の詰め（CPU） | 0.68 | 0.48 | 1.77 | 2.47 | 64.28 | | |
| 　うちボクセル（FillVoxels） | 0.65 | 0.46 | 1.73 | 2.42 | 39.94 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.534 | 6.5% | 0.53 | 0.86 |
| 　Perception | 0.096 | 1.2% | 0.08 | 0.38 |
| 　BotRead | 0.016 | 0.2% | 0.03 | 0.10 |
| 　DriveFighter | 0.406 | 4.9% | 0.40 | 0.54 |
| 　ApplyControl | 0.401 | 4.8% | 0.39 | 0.54 |
| 　FighterStamina | 0.062 | 0.7% | 0.07 | 0.13 |
| 　FighterStanding | 0.123 | 1.5% | 0.24 | 0.38 |
| 　FighterDown | 0.177 | 2.1% | 0.04 | 0.40 |
| 　ControlTail | 0.011 | 0.1% | 0.01 | 0.05 |
| PreBepu | 0.021 | 0.2% | 0.02 | 0.07 |
| 　Remember | 0.009 | 0.1% | 0.01 | 0.06 |
| Bepu | 0.857 | 10.4% | 0.81 | 1.30 |
| CanvasImpacts | 0.012 | 0.1% | 0.02 | 0.29 |
| FistHits | 0.002 | 0.0% | 0.00 | 0.02 |
| Weapons | 6.762 | 81.7% | 36.01 | 58.26 |
| 　MayTouch | 0.042 | 0.5% | 0.05 | 0.13 |
| 　SweepSetup | 2.054 | 24.8% | 6.28 | 9.30 |
| 　SetupRelative | 0.063 | 0.8% | 0.11 | 0.18 |
| 　SetupFar | 0.062 | 0.7% | 0.16 | 0.23 |
| 　SetupCandidates | 1.372 | 16.6% | 4.21 | 5.73 |
| 　SweepRun | 2.785 | 33.7% | 24.89 | 45.99 |
| 　RunScan | 1.767 | 21.4% | 19.52 | 44.83 |
| 　RunFront | 0.516 | 6.2% | 1.38 | 4.15 |
| 　RunOverlaps | 0.051 | 0.6% | 1.13 | 4.51 |
| 　RunPatch | 0.271 | 3.3% | 2.41 | 7.57 |
| 　RunHold | 0.002 | 0.0% | 0.02 | 0.14 |
| 　RunCrush | 0.050 | 0.6% | 0.05 | 0.74 |
| 　RunCut | 0.001 | 0.0% | 0.02 | 0.13 |
| 　RunSeparate | 0.081 | 1.0% | 0.14 | 1.12 |
| 　RunMass | 0.002 | 0.0% | 0.07 | 0.37 |
| 　Snap | 0.559 | 6.8% | 1.05 | 4.59 |
| 　SnapStiffness | 0.070 | 0.9% | 0.16 | 0.83 |
| 　SnapFind | 0.486 | 5.9% | 0.89 | 3.82 |
| 　Bend | 0.039 | 0.5% | 0.04 | 0.71 |
| 　Cut | 0.015 | 0.2% | 0.05 | 0.21 |
| 　Debris | 0.001 | 0.0% | 0.01 | 0.04 |
| 　WeaponClash | 0.388 | 4.7% | 2.45 | 12.30 |
| 　ClashAfter | 0.001 | 0.0% | 0.00 | 0.02 |
| 　ClashPush | 0.002 | 0.0% | 0.00 | 0.01 |
| 　Bridges | 1.757 | 21.2% | 8.52 | 16.80 |
| 　BridgeRefresh | 1.145 | 13.8% | 3.44 | 4.58 |
| Disconnect | 0.001 | 0.0% | 0.03 | 0.40 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.01 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.017 | 0.2% | 0.03 | 1.11 |
| After | 0.004 | 0.1% | 0.01 | 0.02 |
| PostStep | 0.056 | 0.7% | 4.59 | 109.52 |
| 　PostDebris | 0.009 | 0.1% | 0.02 | 0.10 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.02 |
| 　PostRound | 0.046 | 0.6% | 4.56 | 109.52 |
| 　RenderParts | 0.149 | 1.8% | 0.29 | 11.48 |
| 　RenderBridges | 0.480 | 5.8% | 1.69 | 20.57 |
| （枠の外） | 0.004 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 48 / 110、Candidates 29272 / 100088、ScanSamples 321 / 3450、ChunksTested 5780 / 65500、CellsTested 0 / 0、BridgeCells 1323 / 7699、FilledRebuildCells 533 / 26670、BeamRebuildCells 1171 / 54293

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 17.40 s: **117.7**（破片 0、GC0 1）: PostStep 109.5・PostRound 109.5・RenderBridges 8.0・Weapons 6.6・SweepSetup 2.8・Bridges 2.6・SetupCandidates 1.9・BridgeRefresh 1.8・SweepRun 1.6・Bepu 1.0・RunScan 0.9・RenderParts 0.6・Control 0.6・RunFront 0.5・DriveFighter 0.4・ApplyControl 0.4・FighterDown 0.3
- 種 2 の 34.37 s: **60.0**（破片 0、GC0 0）: Weapons 58.3・SweepRun 46.0・RunScan 44.8・Bridges 16.3・SweepSetup 7.1・SetupCandidates 4.6・BridgeRefresh 4.3・RenderBridges 1.7・Bepu 0.9・RunFront 0.8・Control 0.6・Snap 0.5・RenderParts 0.4・DriveFighter 0.4・SnapFind 0.4・ApplyControl 0.4
- 種 2 の 34.38 s: **50.1**（破片 0、GC0 0）: Weapons 48.6・SweepRun 35.6・RunScan 33.7・Bridges 14.7・SweepSetup 7.0・BridgeRefresh 4.6・SetupCandidates 4.4・RenderBridges 1.4・RunFront 1.1・Bepu 0.9・Snap 0.8・SnapFind 0.7・Control 0.5・RunPatch 0.4・DriveFighter 0.4・ApplyControl 0.4・FighterStanding 0.3
- 種 2 の 34.15 s: **43.4**（破片 0、GC0 0）: Weapons 42.2・SweepRun 31.0・RunScan 28.6・Bridges 16.8・SweepSetup 6.6・SetupCandidates 4.3・BridgeRefresh 3.6・RunFront 1.2・RenderBridges 1.1・RunSeparate 0.7・Snap 0.7・Bepu 0.7・SnapFind 0.6・WeaponClash 0.6・Control 0.5・RunPatch 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 23.27 s: **43.2**（破片 0、GC0 0）: Weapons 42.0・SweepRun 29.6・RunScan 21.4・Bridges 9.5・SweepSetup 6.9・SetupCandidates 4.4・BridgeRefresh 3.9・RunPatch 3.5・RunOverlaps 2.4・WeaponClash 2.1・RunFront 1.4・Snap 1.1・SnapFind 0.9・RenderBridges 0.8・RenderParts 0.8・Bepu 0.7・Control 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 2.27 s: **40.7**（破片 0、GC0 0）: Weapons 39.1・SweepRun 26.6・RunScan 19.6・Bridges 10.8・SweepSetup 7.7・SetupCandidates 5.3・RunPatch 3.9・BridgeRefresh 3.7・RunOverlaps 1.6・WeaponClash 1.5・RenderBridges 1.5・RunFront 1.2・Bepu 1.0・Snap 0.9・SnapFind 0.7・Control 0.6・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 34.40 s: **40.6**（破片 0、GC0 0）: Weapons 39.2・SweepRun 28.7・RunScan 27.6・Bridges 9.6・SweepSetup 6.0・SetupCandidates 3.9・BridgeRefresh 3.6・RenderBridges 1.4・Bepu 0.8・RunFront 0.7・Snap 0.6・Control 0.5・SnapFind 0.5・DriveFighter 0.4・ApplyControl 0.4・RenderParts 0.4
- 種 2 の 23.48 s: **40.5**（破片 0、GC0 1）: Weapons 38.7・SweepRun 22.1・WeaponClash 12.3・RunScan 9.9・SweepSetup 8.0・Bridges 6.8・SetupCandidates 5.7・RunPatch 5.2・Snap 4.6・SnapFind 3.7・RunFront 3.7・BridgeRefresh 3.6・RunOverlaps 2.3・Bepu 0.8・SnapStiffness 0.8・Control 0.6・RenderBridges 0.5・DriveFighter 0.4・ApplyControl 0.4・RunMass 0.4・Disconnect 0.4・FighterStanding 0.3
- 種 2 の 34.02 s: **39.9**（破片 0、GC0 0）: Weapons 38.7・SweepRun 27.5・RunScan 17.6・SweepSetup 6.8・Bridges 5.5・RunPatch 5.3・SetupCandidates 5.0・BridgeRefresh 3.1・RunOverlaps 2.5・RunFront 1.3・RenderBridges 1.1・Snap 0.8・SnapFind 0.7・Bepu 0.7・Control 0.5・RenderParts 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 34.10 s: **38.3**（破片 0、GC0 0）: Weapons 37.0・SweepRun 23.3・RunScan 20.1・Bridges 11.3・SweepSetup 8.3・SetupCandidates 5.7・BridgeRefresh 4.2・RenderBridges 1.4・RunFront 1.3・RunPatch 1.1・WeaponClash 1.0・Snap 0.9・SnapFind 0.8・Bepu 0.7・Control 0.5・DriveFighter 0.4・ApplyControl 0.4・RunOverlaps 0.3

## perf vfc2（2026-11-25 18:13、DESKTOP-2403GU4）

種 1 × 最大 90 秒、5400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 54.2 s。物理 102・判断 73。
チェックサム（計測あり）: 89b78a42727dbe8c / 計測なし: 89b78a42727dbe8c（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 9.12 | 8.04 | 21.62 | 31.90 | 81.34 | 11.9% | 70 / 0 |
| 描画の詰め（CPU） | 0.87 | 0.80 | 1.57 | 2.16 | 66.02 | | |
| 　うちボクセル（FillVoxels） | 0.80 | 0.73 | 1.49 | 2.05 | 40.96 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.552 | 6.0% | 0.63 | 1.58 |
| 　Perception | 0.074 | 0.8% | 0.11 | 0.49 |
| 　BotRead | 0.061 | 0.7% | 0.09 | 0.85 |
| 　DriveFighter | 0.399 | 4.4% | 0.42 | 1.42 |
| 　ApplyControl | 0.395 | 4.3% | 0.41 | 1.42 |
| 　FighterStamina | 0.062 | 0.7% | 0.07 | 1.07 |
| 　FighterStanding | 0.214 | 2.3% | 0.15 | 0.36 |
| 　FighterDown | 0.077 | 0.8% | 0.15 | 0.52 |
| 　ControlTail | 0.012 | 0.1% | 0.01 | 0.05 |
| PreBepu | 0.020 | 0.2% | 0.02 | 0.08 |
| 　Remember | 0.009 | 0.1% | 0.01 | 0.06 |
| Bepu | 1.145 | 12.6% | 1.40 | 1.79 |
| CanvasImpacts | 0.011 | 0.1% | 0.02 | 0.43 |
| FistHits | 0.003 | 0.0% | 0.01 | 0.06 |
| Weapons | 7.323 | 80.3% | 39.69 | 78.54 |
| 　MayTouch | 0.049 | 0.5% | 0.06 | 0.13 |
| 　SweepSetup | 2.044 | 22.4% | 4.26 | 8.35 |
| 　SetupRelative | 0.048 | 0.5% | 0.08 | 0.19 |
| 　SetupFar | 0.046 | 0.5% | 0.08 | 0.22 |
| 　SetupCandidates | 1.297 | 14.2% | 2.69 | 5.81 |
| 　SweepRun | 4.028 | 44.2% | 32.68 | 70.12 |
| 　RunScan | 2.509 | 27.5% | 10.63 | 18.95 |
| 　RunFront | 0.574 | 6.3% | 1.88 | 4.01 |
| 　RunOverlaps | 0.175 | 1.9% | 6.05 | 14.09 |
| 　RunPatch | 0.550 | 6.0% | 11.68 | 36.70 |
| 　RunHold | 0.005 | 0.1% | 0.32 | 2.12 |
| 　RunCrush | 0.072 | 0.8% | 0.15 | 1.91 |
| 　RunCut | 0.004 | 0.0% | 0.21 | 1.48 |
| 　RunSeparate | 0.072 | 0.8% | 0.21 | 1.10 |
| 　RunMass | 0.006 | 0.1% | 0.06 | 0.47 |
| 　Snap | 0.364 | 4.0% | 1.01 | 3.52 |
| 　SnapStiffness | 0.047 | 0.5% | 0.13 | 1.13 |
| 　SnapFind | 0.316 | 3.5% | 0.86 | 2.55 |
| 　SnapSplit | 0.000 | 0.0% | 0.01 | 0.68 |
| 　Bend | 0.032 | 0.4% | 0.08 | 1.11 |
| 　Cut | 0.014 | 0.2% | 0.05 | 0.29 |
| 　Debris | 0.001 | 0.0% | 0.01 | 0.12 |
| 　WeaponClash | 0.064 | 0.7% | 0.10 | 7.66 |
| 　ClashAfter | 0.000 | 0.0% | 0.01 | 0.48 |
| 　ClashPush | 0.001 | 0.0% | 0.00 | 0.02 |
| 　Bridges | 0.970 | 10.6% | 2.12 | 12.45 |
| 　BridgeRefresh | 0.672 | 7.4% | 1.35 | 4.74 |
| Disconnect | 0.003 | 0.0% | 0.01 | 0.63 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.03 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.30 |
| FighterSplit | 0.023 | 0.3% | 0.19 | 0.79 |
| After | 0.004 | 0.0% | 0.01 | 0.06 |
| PostStep | 0.030 | 0.3% | 0.06 | 0.87 |
| 　PostDebris | 0.024 | 0.3% | 0.06 | 0.17 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.02 |
| 　PostRound | 0.005 | 0.1% | 0.00 | 0.83 |
| 　RenderParts | 0.129 | 1.4% | 0.19 | 11.94 |
| 　RenderFragments | 0.000 | 0.0% | 0.00 | 0.02 |
| 　RenderBridges | 0.660 | 7.2% | 1.20 | 22.40 |
| （枠の外） | 0.005 | 0.0% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 32 / 56、Candidates 36014 / 78989、ScanSamples 274 / 1129、ChunksTested 8181 / 48566、CellsTested 0 / 0、BridgeCells 694 / 1081、FilledRebuildCells 963 / 2904、BeamRebuildCells 2342 / 5765

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 49.82 s: **81.3**（破片 0、GC0 0）: Weapons 78.5・SweepRun 70.1・RunPatch 36.7・RunOverlaps 13.4・RunScan 9.9・SweepSetup 4.5・Bridges 3.2・SetupCandidates 2.9・BridgeRefresh 2.7・RunFront 2.2・Bepu 1.7・RenderBridges 1.4・RunHold 1.0・Snap 1.0・SnapFind 0.8・RunCut 0.7・Control 0.6・DriveFighter 0.4・ApplyControl 0.4・FighterSplit 0.4
- 種 1 の 49.73 s: **69.1**（破片 0、GC0 0）: Weapons 66.1・SweepRun 60.6・RunPatch 22.3・RunScan 15.7・RunOverlaps 11.9・SweepSetup 3.8・SetupCandidates 2.5・Bepu 1.7・RunFront 1.7・Bridges 1.4・RunHold 1.4・RenderBridges 1.3・RunCut 1.1・BridgeRefresh 0.9・FighterSplit 0.6・Control 0.6・Snap 0.5・DriveFighter 0.4・SnapFind 0.4・ApplyControl 0.4
- 種 1 の 49.70 s: **66.7**（破片 0、GC0 0）: Weapons 64.3・SweepRun 59.5・RunPatch 21.9・RunScan 13.0・RunOverlaps 10.5・SweepSetup 3.4・RunHold 2.1・SetupCandidates 2.1・RunFront 1.6・RenderBridges 1.6・Bepu 1.5・RunCut 1.5・Bridges 1.2・BridgeRefresh 0.6・Control 0.6・Snap 0.5・SnapFind 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 49.67 s: **63.7**（破片 0、GC0 0）: Weapons 60.9・SweepRun 55.8・RunPatch 23.1・RunScan 11.1・RunOverlaps 10.3・SweepSetup 3.8・SetupCandidates 2.6・RunHold 1.8・Bepu 1.5・RenderBridges 1.5・RunCut 1.1・RunFront 1.1・Bridges 1.0・FighterSplit 0.6・BridgeRefresh 0.6・Control 0.6・Snap 0.5・SnapFind 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 78.28 s: **62.7**（破片 0、GC0 0）: Weapons 60.4・SweepRun 54.2・RunPatch 21.7・RunOverlaps 14.1・RunScan 11.1・SweepSetup 4.3・SetupCandidates 2.6・RunFront 2.2・Bepu 1.3・RenderBridges 1.2・RunHold 0.9・Snap 0.9・Bridges 0.8・SnapFind 0.8・BridgeRefresh 0.7・Control 0.6・RunCut 0.5・DriveFighter 0.4・ApplyControl 0.4・FighterSplit 0.4
- 種 1 の 49.68 s: **61.0**（破片 0、GC0 0）: Weapons 58.5・SweepRun 53.0・RunPatch 21.2・RunOverlaps 11.9・RunScan 11.9・SweepSetup 4.2・SetupCandidates 2.8・Bepu 1.5・RenderBridges 1.5・RunFront 1.4・RunHold 1.3・Bridges 1.1・RunCut 0.7・BridgeRefresh 0.6・Control 0.5・Snap 0.5・SnapFind 0.4・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 49.85 s: **55.4**（破片 0、GC0 0）: Weapons 52.7・SweepRun 41.0・RunScan 14.7・RunPatch 13.4・RunOverlaps 6.1・SweepSetup 6.0・Bridges 4.5・SetupCandidates 4.0・WeaponClash 2.8・BridgeRefresh 2.5・Snap 2.5・RunFront 2.4・Bepu 1.7・SnapFind 1.4・RenderBridges 1.3・SnapSplit 0.7・Control 0.6・RunSeparate 0.5・RunHold 0.5・ClashAfter 0.5・DriveFighter 0.4・ApplyControl 0.4・SnapStiffness 0.4
- 種 1 の 49.83 s: **54.3**（破片 0、GC0 0）: Weapons 51.7・SweepRun 43.3・RunPatch 17.3・RunScan 12.2・RunOverlaps 6.4・SweepSetup 4.7・Bridges 3.3・SetupCandidates 3.0・RunFront 2.5・BridgeRefresh 2.5・Bepu 1.8・RenderBridges 1.2・Snap 1.0・SnapFind 0.8・WeaponClash 0.6・RenderParts 0.6・Control 0.6・RunHold 0.5・DriveFighter 0.4・ApplyControl 0.4・RunCut 0.4・RunSeparate 0.3
- 種 1 の 49.63 s: **51.8**（破片 0、GC0 0）: Weapons 49.3・SweepRun 43.7・RunScan 15.6・RunPatch 12.3・RunOverlaps 9.2・SweepSetup 4.4・SetupCandidates 3.0・RenderBridges 1.7・Bepu 1.5・RunHold 1.2・RunFront 1.1・Bridges 1.0・RunCut 0.6・Control 0.6・BridgeRefresh 0.5・Snap 0.5・SnapFind 0.4・DriveFighter 0.4・ApplyControl 0.4・FighterSplit 0.4
- 種 1 の 49.90 s: **51.1**（破片 0、GC0 0）: Weapons 48.5・SweepRun 39.7・RunScan 14.7・RunPatch 11.7・RunOverlaps 8.3・Bridges 4.5・SweepSetup 4.4・RunFront 3.1・BridgeRefresh 3.1・SetupCandidates 2.6・Bepu 1.7・RenderBridges 1.3・Snap 1.1・SnapFind 0.9・RunSeparate 0.7・Control 0.5・DriveFighter 0.4・ApplyControl 0.4・RenderParts 0.3

## perf vfc2（2026-11-25 18:14、DESKTOP-2403GU4）

種 2 × 最大 90 秒、5400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 43.4 s。物理 102・判断 73。
チェックサム（計測あり）: 59aa46444f84d9ab / 計測なし: 59aa46444f84d9ab（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 6.98 | 5.41 | 15.46 | 32.43 | 99.60 | 4.2% | 59 / 1 |
| 描画の詰め（CPU） | 1.00 | 0.91 | 1.69 | 2.39 | 67.86 | | |
| 　うちボクセル（FillVoxels） | 0.94 | 0.85 | 1.62 | 2.28 | 43.13 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.683 | 9.8% | 0.72 | 3.56 |
| 　Perception | 0.078 | 1.1% | 0.11 | 0.90 |
| 　BotRead | 0.077 | 1.1% | 0.11 | 3.07 |
| 　BotPerceive | 0.022 | 0.3% | 0.05 | 0.38 |
| 　BotRead2 | 0.002 | 0.0% | 0.00 | 0.03 |
| 　BotChoose | 0.001 | 0.0% | 0.00 | 0.15 |
| 　BotExecute | 0.003 | 0.0% | 0.00 | 0.04 |
| 　DriveFighter | 0.501 | 7.2% | 0.47 | 3.41 |
| 　ApplyControl | 0.483 | 6.9% | 0.46 | 1.25 |
| 　FighterStamina | 0.069 | 1.0% | 0.07 | 0.82 |
| 　FighterStanding | 0.305 | 4.4% | 0.20 | 0.47 |
| 　FighterDown | 0.065 | 0.9% | 0.14 | 0.26 |
| 　ControlTail | 0.021 | 0.3% | 0.03 | 2.28 |
| PreBepu | 0.023 | 0.3% | 0.02 | 0.08 |
| 　Remember | 0.009 | 0.1% | 0.01 | 0.06 |
| Bepu | 1.561 | 22.4% | 1.47 | 3.28 |
| CanvasImpacts | 0.011 | 0.2% | 0.01 | 0.71 |
| FistHits | 0.004 | 0.1% | 0.00 | 0.03 |
| Weapons | 4.424 | 63.4% | 43.64 | 95.09 |
| 　MayTouch | 0.051 | 0.7% | 0.06 | 0.14 |
| 　SweepSetup | 1.645 | 23.6% | 4.99 | 10.83 |
| 　SetupRelative | 0.043 | 0.6% | 0.08 | 0.15 |
| 　SetupFar | 0.040 | 0.6% | 0.10 | 0.23 |
| 　SetupCandidates | 1.111 | 15.9% | 3.22 | 7.28 |
| 　SweepRun | 1.913 | 27.4% | 36.79 | 88.17 |
| 　RunScan | 1.087 | 15.6% | 10.85 | 35.10 |
| 　RunFront | 0.248 | 3.6% | 2.73 | 5.22 |
| 　RunOverlaps | 0.123 | 1.8% | 6.54 | 21.45 |
| 　RunPatch | 0.358 | 5.1% | 14.81 | 46.92 |
| 　RunHold | 0.003 | 0.0% | 0.17 | 0.90 |
| 　RunCrush | 0.019 | 0.3% | 0.04 | 0.70 |
| 　RunCut | 0.002 | 0.0% | 0.12 | 0.64 |
| 　RunSeparate | 0.036 | 0.5% | 0.37 | 1.38 |
| 　RunMass | 0.002 | 0.0% | 0.06 | 0.56 |
| 　Snap | 0.179 | 2.6% | 1.20 | 3.53 |
| 　SnapStiffness | 0.023 | 0.3% | 0.16 | 1.02 |
| 　SnapFind | 0.155 | 2.2% | 1.04 | 2.70 |
| 　Bend | 0.016 | 0.2% | 0.25 | 1.68 |
| 　Cut | 0.013 | 0.2% | 0.10 | 0.36 |
| 　Debris | 0.001 | 0.0% | 0.02 | 0.05 |
| 　WeaponClash | 0.114 | 1.6% | 0.20 | 13.75 |
| 　ClashAfter | 0.000 | 0.0% | 0.00 | 0.04 |
| 　ClashPush | 0.026 | 0.4% | 0.03 | 0.18 |
| 　Bridges | 1.038 | 14.9% | 5.26 | 19.23 |
| 　BridgeRefresh | 0.586 | 8.4% | 1.63 | 3.39 |
| Disconnect | 0.002 | 0.0% | 0.01 | 0.57 |
| Kicks | 0.214 | 3.1% | 1.68 | 11.08 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.023 | 0.3% | 0.21 | 4.62 |
| After | 0.004 | 0.1% | 0.01 | 0.04 |
| PostStep | 0.025 | 0.4% | 0.10 | 3.13 |
| 　PostDebris | 0.018 | 0.3% | 0.10 | 0.23 |
| 　PostWounds | 0.000 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.02 |
| 　PostRound | 0.006 | 0.1% | 0.00 | 3.12 |
| 　RenderParts | 0.141 | 2.0% | 0.15 | 12.27 |
| 　RenderFragments | 0.000 | 0.0% | 0.00 | 0.01 |
| 　RenderBridges | 0.791 | 11.3% | 1.60 | 24.15 |
| （枠の外） | 0.004 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 28 / 72、Candidates 23938 / 88601、ScanSamples 197 / 1717、ChunksTested 3915 / 52718、CellsTested 0 / 0、BridgeCells 649 / 3188、FilledRebuildCells 515 / 7790、BeamRebuildCells 1172 / 18951

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 12.50 s: **99.6**（破片 0、GC0 0）: Weapons 89.4・SweepRun 82.2・RunPatch 29.5・RunScan 29.1・Bridges 19.2・RunOverlaps 14.2・SweepSetup 8.9・Kicks 7.1・SetupCandidates 5.6・RunFront 4.3・BridgeRefresh 3.2・RenderBridges 1.6・Bepu 1.5・Snap 1.4・SnapFind 1.3・Control 1.2・RunSeparate 0.8・RunHold 0.6・DriveFighter 0.5・ApplyControl 0.5・RenderParts 0.5・RunCut 0.4・BotRead 0.4・BotPerceive 0.4
- 種 2 の 12.47 s: **98.1**（破片 0、GC0 0）: Weapons 88.2・SweepRun 81.5・RunScan 32.3・RunPatch 25.0・RunOverlaps 17.6・Bridges 12.2・SweepSetup 8.7・Kicks 7.2・SetupCandidates 5.7・RunFront 3.8・BridgeRefresh 2.5・Bepu 1.4・RenderBridges 1.4・Snap 1.2・SnapFind 1.0・Control 0.8・Bend 0.5・DriveFighter 0.5・ApplyControl 0.5・RunHold 0.4
- 種 2 の 11.67 s: **97.3**（破片 0、GC0 0）: Weapons 95.1・SweepRun 88.2・RunPatch 46.4・RunOverlaps 19.8・RunScan 11.2・SweepSetup 3.9・RunFront 3.4・Bridges 2.8・SetupCandidates 2.4・RenderBridges 1.8・Snap 1.4・Bepu 1.3・SnapFind 1.2・BridgeRefresh 1.2・RunHold 0.9・RunCut 0.6・Control 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 2 の 11.68 s: **91.0**（破片 0、GC0 0）: Weapons 88.7・SweepRun 82.0・RunPatch 46.9・RunOverlaps 21.4・RunScan 4.7・SweepSetup 4.0・RunFront 3.5・SetupCandidates 2.5・Bridges 2.4・RenderBridges 1.9・Snap 1.5・SnapFind 1.3・Bepu 1.3・RunSeparate 0.8・BridgeRefresh 0.8・Control 0.6・RunHold 0.5・DriveFighter 0.4・ApplyControl 0.4・RunCut 0.4・FighterSplit 0.3
- 種 2 の 12.52 s: **90.5**（破片 0、GC0 0）: Weapons 80.2・SweepRun 72.7・RunScan 35.1・RunPatch 19.4・Bridges 16.9・RunOverlaps 9.6・SweepSetup 9.4・Kicks 7.5・SetupCandidates 6.1・RunFront 5.2・BridgeRefresh 2.8・Snap 1.4・Bepu 1.4・SnapFind 1.2・RenderBridges 1.0・Control 1.0・RunSeparate 0.6・DriveFighter 0.5・ApplyControl 0.5・Bend 0.5・BotRead 0.3・RunHold 0.3
- 種 2 の 12.48 s: **82.4**（破片 0、GC0 1）: Weapons 71.7・SweepRun 63.3・RunPatch 22.5・RunScan 17.8・RunOverlaps 15.1・Bridges 11.0・SweepSetup 9.0・Kicks 7.6・SetupCandidates 5.9・RunFront 5.1・BridgeRefresh 2.7・Bend 1.7・Snap 1.5・Bepu 1.5・SnapFind 1.3・RenderBridges 1.2・Control 1.1・DriveFighter 0.5・ApplyControl 0.5・BotRead 0.4・BotPerceive 0.4・RunSeparate 0.4・RunHold 0.3
- 種 2 の 12.53 s: **81.7**（破片 0、GC0 0）: Weapons 73.0・SweepRun 65.0・RunScan 26.8・RunPatch 21.6・Bridges 11.2・RunOverlaps 9.4・SweepSetup 9.0・Kicks 6.0・SetupCandidates 5.8・RunFront 4.2・BridgeRefresh 2.7・RenderBridges 1.6・Bepu 1.4・RunSeparate 1.2・Snap 1.1・Control 1.0・SnapFind 1.0・DriveFighter 0.5・ApplyControl 0.5・Bend 0.3・BotRead 0.3・RenderParts 0.3
- 種 2 の 12.55 s: **70.8**（破片 0、GC0 0）: Weapons 63.6・SweepRun 52.6・RunScan 26.5・RunPatch 15.8・SweepSetup 10.8・Bridges 9.5・RunOverlaps 7.3・SetupCandidates 7.3・Kicks 4.6・BridgeRefresh 2.7・RunFront 1.7・Bepu 1.5・RenderBridges 1.4・Snap 0.7・Control 0.7・SnapFind 0.6・DriveFighter 0.5・ApplyControl 0.5・Bend 0.4
- 種 2 の 12.43 s: **62.8**（破片 0、GC0 0）: Weapons 53.2・SweepRun 47.4・RunScan 19.8・RunPatch 15.4・SweepSetup 8.5・Bridges 7.7・Kicks 6.9・RunOverlaps 6.8・SetupCandidates 5.6・RunFront 2.9・BridgeRefresh 2.2・Bepu 1.4・RenderBridges 1.2・Control 0.9・Snap 0.8・SnapFind 0.7・DriveFighter 0.5・ApplyControl 0.5・Bend 0.5
- 種 2 の 11.70 s: **61.3**（破片 0、GC0 0）: Weapons 59.1・SweepRun 53.3・RunPatch 27.7・RunOverlaps 13.9・RunScan 5.6・RunFront 3.5・SweepSetup 3.5・SetupCandidates 2.2・RenderBridges 2.2・Bridges 1.8・Snap 1.3・Bepu 1.3・SnapFind 1.2・RunSeparate 0.7・BridgeRefresh 0.6・Control 0.6・DriveFighter 0.4・ApplyControl 0.4

## perf royale（2026-11-25 18:18、DESKTOP-2403GU4）

種 1 × 最大 60 秒、3600 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 108.8 s。物理 102・判断 73。
チェックサム（計測あり）: 2ba61773c7da2c74 / 計測なし: 2ba61773c7da2c74（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 27.48 | 27.16 | 47.53 | 61.64 | 95.38 | 86.2% | 192 / 1 |
| 描画の詰め（CPU） | 2.56 | 2.44 | 4.19 | 5.17 | 85.50 | | |
| 　うちボクセル（FillVoxels） | 2.34 | 2.20 | 3.82 | 4.63 | 60.24 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 1.747 | 6.4% | 1.81 | 3.22 |
| 　Perception | 0.222 | 0.8% | 0.26 | 1.38 |
| 　BotRead | 0.167 | 0.6% | 0.27 | 1.03 |
| 　BotPerceive | 0.044 | 0.2% | 0.11 | 0.89 |
| 　BotRead2 | 0.012 | 0.0% | 0.01 | 0.04 |
| 　BotChoose | 0.006 | 0.0% | 0.01 | 0.06 |
| 　BotExecute | 0.007 | 0.0% | 0.01 | 0.07 |
| 　DriveFighter | 1.199 | 4.4% | 1.13 | 2.36 |
| 　ApplyControl | 1.169 | 4.3% | 1.07 | 2.25 |
| 　FighterStamina | 0.177 | 0.6% | 0.17 | 1.16 |
| 　FighterStanding | 0.556 | 2.0% | 0.69 | 1.08 |
| 　FighterDown | 0.304 | 1.1% | 0.09 | 0.83 |
| 　ControlTail | 0.086 | 0.3% | 0.04 | 1.42 |
| PreBepu | 0.058 | 0.2% | 0.06 | 0.15 |
| 　Remember | 0.030 | 0.1% | 0.03 | 0.12 |
| Bepu | 3.103 | 11.3% | 2.41 | 4.28 |
| CanvasImpacts | 0.029 | 0.1% | 0.03 | 0.23 |
| FistHits | 0.021 | 0.1% | 0.02 | 0.09 |
| Weapons | 21.773 | 79.2% | 66.48 | 90.93 |
| 　MayTouch | 0.708 | 2.6% | 0.69 | 1.04 |
| 　SweepSetup | 6.926 | 25.2% | 15.67 | 24.21 |
| 　SetupRelative | 0.202 | 0.7% | 0.25 | 0.55 |
| 　SetupFar | 0.183 | 0.7% | 0.25 | 0.63 |
| 　SetupCandidates | 4.667 | 17.0% | 10.63 | 16.67 |
| 　SweepRun | 7.681 | 27.9% | 37.24 | 63.73 |
| 　RunScan | 3.957 | 14.4% | 17.98 | 34.37 |
| 　RunFront | 1.433 | 5.2% | 3.94 | 7.03 |
| 　RunOverlaps | 0.332 | 1.2% | 3.87 | 15.34 |
| 　RunPatch | 1.367 | 5.0% | 9.57 | 28.60 |
| 　RunHold | 0.009 | 0.0% | 0.07 | 0.61 |
| 　RunCrush | 0.182 | 0.7% | 0.21 | 2.10 |
| 　RunCut | 0.006 | 0.0% | 0.06 | 0.44 |
| 　RunSeparate | 0.242 | 0.9% | 0.86 | 2.49 |
| 　RunMass | 0.014 | 0.0% | 0.12 | 1.21 |
| 　Snap | 1.286 | 4.7% | 3.98 | 9.36 |
| 　SnapStiffness | 0.170 | 0.6% | 0.60 | 2.42 |
| 　SnapFind | 1.112 | 4.0% | 3.36 | 6.93 |
| 　SnapSplit | 0.000 | 0.0% | 0.00 | 0.27 |
| 　Bend | 0.088 | 0.3% | 0.15 | 1.80 |
| 　Cut | 0.103 | 0.4% | 0.80 | 17.86 |
| 　Debris | 0.003 | 0.0% | 0.02 | 0.77 |
| 　WeaponClash | 3.191 | 11.6% | 8.85 | 32.65 |
| 　ClashAfter | 0.004 | 0.0% | 0.01 | 0.03 |
| 　ClashPush | 0.841 | 3.1% | 1.23 | 2.51 |
| 　Bridges | 4.279 | 15.6% | 11.57 | 23.63 |
| 　BridgeRefresh | 3.057 | 11.1% | 5.17 | 8.77 |
| Disconnect | 0.009 | 0.0% | 0.06 | 0.85 |
| Kicks | 0.438 | 1.6% | 0.08 | 6.98 |
| Elbows | 0.002 | 0.0% | 0.00 | 0.02 |
| FighterSplit | 0.071 | 0.3% | 0.14 | 4.91 |
| After | 0.012 | 0.0% | 0.01 | 0.07 |
| PostStep | 0.216 | 0.8% | 0.28 | 3.07 |
| 　PostDebris | 0.195 | 0.7% | 0.28 | 1.38 |
| 　PostWounds | 0.001 | 0.0% | 0.00 | 0.02 |
| 　PostSwings | 0.002 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.018 | 0.1% | 0.00 | 2.99 |
| 　RenderParts | 0.374 | 1.4% | 0.49 | 13.28 |
| 　RenderFragments | 0.003 | 0.0% | 0.00 | 0.07 |
| 　RenderBridges | 1.916 | 7.0% | 2.67 | 36.58 |
| （枠の外） | 0.006 | 0.0% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 130 / 178、Candidates 102162 / 259048、ScanSamples 621 / 1716、ChunksTested 13538 / 66601、CellsTested 0 / 0、BridgeCells 3140 / 6498、FilledRebuildCells 2127 / 30620、BeamRebuildCells 5833 / 67143

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 4.50 s: **95.4**（破片 0、GC0 0）: Weapons 90.9・SweepRun 63.7・RunPatch 28.6・Bridges 23.1・RunOverlaps 15.3・SweepSetup 12.3・RunScan 12.0・SetupCandidates 8.3・WeaponClash 8.2・BridgeRefresh 6.8・RunFront 5.0・Snap 4.5・SnapFind 3.8・RenderBridges 3.1・Control 1.9・Bepu 1.6・DriveFighter 1.1・ApplyControl 1.0・ClashPush 0.9・FighterStanding 0.8・RunSeparate 0.7・SnapStiffness 0.6・MayTouch 0.6・Perception 0.5・FighterSplit 0.5・RenderParts 0.4・Cut 0.3
- 種 1 の 7.60 s: **93.9**（破片 0、GC0 0）: Weapons 89.5・SweepRun 55.7・RunScan 30.6・SweepSetup 18.0・RunPatch 13.7・SetupCandidates 11.7・Bridges 11.5・WeaponClash 7.2・BridgeRefresh 7.2・RunFront 5.1・Snap 4.7・RunOverlaps 4.5・SnapFind 4.1・RenderBridges 3.6・Bepu 2.3・Control 1.6・ClashPush 1.1・DriveFighter 1.1・RunSeparate 1.1・ApplyControl 1.0・FighterStanding 0.7・MayTouch 0.7・SnapStiffness 0.6・RenderParts 0.4・Cut 0.3・PostStep 0.3・PostDebris 0.3・SetupRelative 0.3
- 種 1 の 7.62 s: **90.1**（破片 0、GC0 0）: Weapons 85.3・SweepRun 50.9・RunScan 26.5・SweepSetup 18.3・RunPatch 13.1・SetupCandidates 12.2・Bridges 11.2・BridgeRefresh 7.1・WeaponClash 5.6・RunFront 5.6・Snap 4.3・RunOverlaps 4.3・SnapFind 3.7・RenderBridges 3.1・Bepu 2.3・Control 1.8・ClashPush 1.2・DriveFighter 1.0・ApplyControl 1.0・RenderParts 0.9・Cut 0.7・MayTouch 0.7・FighterStanding 0.7・SnapStiffness 0.5・RunSeparate 0.5・PostStep 0.4・PostDebris 0.4・BotRead 0.4・Bend 0.4・RunCrush 0.3
- 種 1 の 4.52 s: **88.0**（破片 0、GC0 0）: Weapons 84.0・SweepRun 57.5・RunScan 26.3・Bridges 20.0・RunPatch 15.1・SweepSetup 13.6・RunOverlaps 9.7・SetupCandidates 9.2・WeaponClash 6.9・BridgeRefresh 6.4・RunFront 3.8・Snap 3.0・SnapFind 2.6・RenderBridges 2.5・Control 1.9・RunSeparate 1.7・Bepu 1.6・DriveFighter 1.1・ApplyControl 1.1・ClashPush 0.9・FighterStanding 0.8・MayTouch 0.6・Perception 0.5・SnapStiffness 0.4・RenderParts 0.4
- 種 1 の 4.48 s: **85.8**（破片 0、GC0 0）: Weapons 81.6・SweepRun 50.9・RunScan 25.7・Bridges 23.6・SweepSetup 16.2・RunPatch 14.2・WeaponClash 12.2・SetupCandidates 11.2・BridgeRefresh 6.2・Snap 5.0・RunFront 4.4・SnapFind 4.2・RunOverlaps 4.0・RenderBridges 2.7・Control 1.8・Bepu 1.6・RunSeparate 1.6・DriveFighter 1.1・ApplyControl 1.0・ClashPush 0.8・SnapStiffness 0.8・FighterStanding 0.8・MayTouch 0.7・Perception 0.5・RenderParts 0.5・Disconnect 0.4
- 種 1 の 7.58 s: **82.2**（破片 0、GC0 0）: Weapons 77.7・SweepRun 45.3・RunScan 34.4・SweepSetup 18.7・SetupCandidates 12.3・Bridges 11.4・BridgeRefresh 6.1・WeaponClash 5.2・RunFront 4.6・RunPatch 3.7・Snap 3.5・SnapFind 3.1・RenderBridges 2.4・Bepu 2.3・Control 1.7・ClashPush 1.2・RunOverlaps 1.1・DriveFighter 1.1・RunSeparate 1.0・ApplyControl 1.0・MayTouch 0.7・FighterStanding 0.7・RenderParts 0.6・SnapStiffness 0.5・BotRead 0.4・Cut 0.3・PostStep 0.3・PostDebris 0.3
- 種 1 の 7.87 s: **79.6**（破片 0、GC0 0）: Weapons 74.7・SweepRun 36.9・SweepSetup 24.2・RunScan 21.6・Bridges 17.6・SetupCandidates 16.7・WeaponClash 8.7・RunPatch 7.2・BridgeRefresh 5.5・RunFront 3.8・Snap 3.2・SnapFind 2.8・RenderBridges 2.6・Bepu 2.6・Control 1.9・RunSeparate 1.8・ClashPush 1.8・RunOverlaps 1.7・DriveFighter 1.1・ApplyControl 1.1・FighterStanding 0.7・MayTouch 0.7・SnapStiffness 0.5・Cut 0.5・BotRead 0.4・SetupFar 0.4・SetupRelative 0.3・RenderParts 0.3・PostStep 0.3
- 種 1 の 4.47 s: **78.9**（破片 0、GC0 0）: Weapons 74.8・SweepRun 43.7・Bridges 20.3・RunScan 16.9・SweepSetup 15.9・RunPatch 12.3・SetupCandidates 11.2・WeaponClash 10.1・BridgeRefresh 6.7・RunOverlaps 6.6・Snap 4.9・RunFront 4.8・SnapFind 4.1・RenderBridges 2.4・Control 1.8・RunSeparate 1.7・Bepu 1.6・DriveFighter 1.0・ApplyControl 1.0・ClashPush 0.9・FighterStanding 0.8・SnapStiffness 0.7・MayTouch 0.6・RenderParts 0.6・Perception 0.4・Cut 0.4
- 種 1 の 7.33 s: **77.8**（破片 0、GC0 0）: Weapons 73.2・SweepRun 48.8・RunPatch 17.5・RunScan 15.6・SweepSetup 14.0・Bridges 9.3・SetupCandidates 9.1・RunOverlaps 8.3・RunFront 5.2・WeaponClash 4.3・BridgeRefresh 3.4・Snap 3.3・RenderBridges 3.0・SnapFind 2.9・Bepu 2.2・Control 1.6・ClashPush 1.1・DriveFighter 1.0・ApplyControl 1.0・RunSeparate 0.9・RenderParts 0.7・MayTouch 0.7・FighterStanding 0.6・RunCrush 0.6・SnapStiffness 0.4・Cut 0.4・PostStep 0.3・PostDebris 0.3・FighterSplit 0.3
- 種 1 の 4.45 s: **75.6**（破片 0、GC0 0）: Weapons 71.3・SweepRun 42.4・Bridges 21.4・RunPatch 16.6・SweepSetup 13.9・RunScan 12.6・WeaponClash 10.6・SetupCandidates 9.7・RunOverlaps 6.4・BridgeRefresh 6.3・Snap 5.1・RunFront 4.5・SnapFind 4.3・RenderBridges 3.2・Control 1.9・Bepu 1.6・RunSeparate 1.1・DriveFighter 1.1・ApplyControl 1.0・ClashPush 0.9・FighterStanding 0.8・SnapStiffness 0.8・RenderParts 0.7・MayTouch 0.6・Perception 0.5・Cut 0.4

## perf royale（2026-11-25 18:22、DESKTOP-2403GU4）

種 2 × 最大 60 秒、3600 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 133.2 s。物理 102・判断 73。
チェックサム（計測あり）: 57e565a2a3426c2f / 計測なし: 57e565a2a3426c2f（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 34.42 | 33.79 | 59.41 | 73.51 | 148.46 | 88.3% | 230 / 1 |
| 描画の詰め（CPU） | 2.40 | 2.14 | 4.06 | 5.51 | 89.81 | | |
| 　うちボクセル（FillVoxels） | 2.21 | 1.98 | 3.73 | 5.31 | 64.44 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 1.794 | 5.2% | 2.04 | 5.69 |
| 　Perception | 0.236 | 0.7% | 0.32 | 1.43 |
| 　BotRead | 0.209 | 0.6% | 0.20 | 1.44 |
| 　BotPerceive | 0.079 | 0.2% | 0.07 | 1.23 |
| 　BotRead2 | 0.004 | 0.0% | 0.00 | 0.06 |
| 　BotChoose | 0.001 | 0.0% | 0.00 | 0.05 |
| 　BotExecute | 0.002 | 0.0% | 0.00 | 0.02 |
| 　DriveFighter | 1.166 | 3.4% | 1.19 | 2.35 |
| 　ApplyControl | 1.149 | 3.3% | 1.17 | 2.32 |
| 　FighterStamina | 0.178 | 0.5% | 0.18 | 1.31 |
| 　FighterStanding | 0.489 | 1.4% | 0.45 | 0.93 |
| 　FighterDown | 0.351 | 1.0% | 0.40 | 0.71 |
| 　ControlTail | 0.078 | 0.2% | 0.14 | 4.36 |
| PreBepu | 0.060 | 0.2% | 0.06 | 0.56 |
| 　Remember | 0.031 | 0.1% | 0.03 | 0.08 |
| Bepu | 3.321 | 9.6% | 3.63 | 5.35 |
| CanvasImpacts | 0.029 | 0.1% | 0.03 | 0.50 |
| FistHits | 0.021 | 0.1% | 0.02 | 0.08 |
| Weapons | 28.605 | 83.1% | 78.92 | 140.02 |
| 　MayTouch | 0.695 | 2.0% | 0.80 | 0.94 |
| 　SweepSetup | 8.356 | 24.3% | 13.59 | 22.42 |
| 　SetupRelative | 0.248 | 0.7% | 0.32 | 0.49 |
| 　SetupFar | 0.207 | 0.6% | 0.32 | 0.42 |
| 　SetupCandidates | 5.433 | 15.8% | 8.85 | 16.56 |
| 　SweepRun | 11.598 | 33.7% | 53.80 | 116.05 |
| 　RunScan | 6.134 | 17.8% | 20.12 | 51.28 |
| 　RunFront | 2.435 | 7.1% | 5.65 | 9.87 |
| 　RunOverlaps | 0.350 | 1.0% | 7.62 | 23.46 |
| 　RunPatch | 1.728 | 5.0% | 17.15 | 52.10 |
| 　RunHold | 0.009 | 0.0% | 0.20 | 0.82 |
| 　RunCrush | 0.208 | 0.6% | 0.38 | 2.46 |
| 　RunCut | 0.005 | 0.0% | 0.14 | 0.45 |
| 　RunSeparate | 0.529 | 1.5% | 1.04 | 4.18 |
| 　RunMass | 0.009 | 0.0% | 0.07 | 0.59 |
| 　Snap | 1.637 | 4.8% | 2.96 | 6.93 |
| 　SnapStiffness | 0.208 | 0.6% | 0.38 | 1.30 |
| 　SnapFind | 1.422 | 4.1% | 2.57 | 5.64 |
| 　Bend | 0.103 | 0.3% | 0.47 | 2.20 |
| 　Cut | 0.109 | 0.3% | 1.03 | 23.00 |
| 　Debris | 0.002 | 0.0% | 0.02 | 0.09 |
| 　WeaponClash | 1.963 | 5.7% | 3.38 | 18.81 |
| 　ClashAfter | 0.003 | 0.0% | 0.00 | 0.05 |
| 　ClashPush | 0.741 | 2.2% | 0.78 | 3.15 |
| 　Bridges | 6.251 | 18.2% | 10.36 | 26.46 |
| 　BridgeRefresh | 4.211 | 12.2% | 5.66 | 12.91 |
| Disconnect | 0.006 | 0.0% | 0.01 | 1.51 |
| Kicks | 0.310 | 0.9% | 1.96 | 6.14 |
| Elbows | 0.002 | 0.0% | 0.00 | 0.95 |
| FighterSplit | 0.064 | 0.2% | 0.29 | 2.07 |
| After | 0.011 | 0.0% | 0.01 | 0.06 |
| PostStep | 0.191 | 0.6% | 0.24 | 2.36 |
| 　PostDebris | 0.167 | 0.5% | 0.21 | 0.77 |
| 　PostWounds | 0.001 | 0.0% | 0.00 | 0.04 |
| 　PostSwings | 0.002 | 0.0% | 0.00 | 0.02 |
| 　PostRound | 0.020 | 0.1% | 0.03 | 2.11 |
| 　RenderParts | 0.410 | 1.2% | 0.63 | 13.20 |
| 　RenderFragments | 0.001 | 0.0% | 0.00 | 0.03 |
| 　RenderBridges | 1.766 | 5.1% | 2.33 | 40.42 |
| （枠の外） | 0.006 | 0.0% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 123 / 204、Candidates 133268 / 224876、ScanSamples 682 / 2440、ChunksTested 20293 / 84577、CellsTested 0 / 0、BridgeCells 3149 / 6215、FilledRebuildCells 1491 / 3573、BeamRebuildCells 4080 / 19141

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 14.05 s: **148.5**（破片 1、GC0 0）: Weapons 140.0・SweepRun 116.0・RunScan 51.3・RunPatch 33.8・RunOverlaps 23.5・SweepSetup 14.3・Bridges 12.9・SetupCandidates 9.5・BridgeRefresh 6.1・WeaponClash 4.5・Kicks 2.9・RenderBridges 2.7・Bepu 2.6・Snap 2.6・RunFront 2.3・SnapFind 2.2・Control 1.9・DriveFighter 1.2・ApplyControl 1.2・RunHold 0.8・MayTouch 0.8・ClashPush 0.8・FighterStanding 0.5・PostStep 0.5・RenderParts 0.5・RunCut 0.5・PostDebris 0.5・FighterSplit 0.4・FighterDown 0.4・SnapStiffness 0.3・SetupFar 0.3・RunSeparate 0.3・SetupRelative 0.3
- 種 2 の 13.95 s: **117.3**（破片 1、GC0 0）: Weapons 112.1・SweepRun 88.1・RunPatch 52.1・RunOverlaps 18.1・SweepSetup 11.1・RunScan 10.1・Bridges 8.8・SetupCandidates 7.9・BridgeRefresh 6.4・RunFront 4.1・WeaponClash 3.7・Snap 3.3・RenderBridges 2.9・SnapFind 2.8・Bepu 2.7・Control 1.6・DriveFighter 1.1・ApplyControl 1.1・MayTouch 0.7・ClashPush 0.7・PostStep 0.4・PostDebris 0.4・FighterDown 0.4・SnapStiffness 0.4・FighterStanding 0.4・RunSeparate 0.3・RunHold 0.3
- 種 2 の 14.08 s: **113.3**（破片 1、GC0 0）: Weapons 104.3・SweepRun 77.2・RunScan 28.2・RunPatch 26.1・RunOverlaps 17.7・SweepSetup 16.6・Bridges 15.1・SetupCandidates 11.3・BridgeRefresh 7.9・Kicks 3.1・WeaponClash 3.0・Bepu 2.7・RenderBridges 2.7・RunFront 2.7・Snap 2.2・SnapFind 1.9・Control 1.8・DriveFighter 1.2・ApplyControl 1.1・MayTouch 0.8・FighterSplit 0.7・PostStep 0.5・PostDebris 0.5・FighterStanding 0.4・FighterDown 0.4・ClashPush 0.4・SetupFar 0.3・SetupRelative 0.3・RunSeparate 0.3
- 種 2 の 14.03 s: **110.0**（破片 1、GC0 0）: Weapons 101.6・SweepRun 76.9・RunScan 36.9・RunPatch 23.5・SweepSetup 14.4・Bridges 12.7・RunOverlaps 11.9・SetupCandidates 9.8・BridgeRefresh 6.5・WeaponClash 4.9・RenderBridges 3.6・Kicks 2.9・Snap 2.8・Bepu 2.6・RunFront 2.6・SnapFind 2.4・Control 1.9・DriveFighter 1.2・ApplyControl 1.2・MayTouch 0.8・ClashPush 0.8・PostStep 0.5・PostDebris 0.5・FighterStanding 0.4・FighterDown 0.4・SnapStiffness 0.4・FighterSplit 0.3・SetupRelative 0.3・SetupFar 0.3
- 種 2 の 46.83 s: **106.5**（破片 1、GC0 1）: Weapons 95.0・SweepRun 75.3・RunPatch 34.1・RunOverlaps 15.3・RunScan 15.1・SweepSetup 11.8・SetupCandidates 7.4・RunFront 6.8・Bridges 5.8・Kicks 5.0・BridgeRefresh 3.5・Bepu 3.2・Snap 2.6・RenderBridges 2.5・Control 2.4・SnapFind 2.3・WeaponClash 1.8・Bend 1.5・DriveFighter 1.2・ApplyControl 1.2・ClashPush 1.0・RenderParts 0.9・MayTouch 0.9・RunSeparate 0.7・Cut 0.6・Perception 0.5・FighterSplit 0.5・FighterStanding 0.4・RunHold 0.4・FighterDown 0.4・SnapStiffness 0.3・RunCut 0.3・SetupRelative 0.3
- 種 2 の 14.02 s: **104.2**（破片 1、GC0 0）: Weapons 95.5・SweepRun 69.1・RunScan 31.9・RunPatch 20.2・SweepSetup 15.5・Bridges 14.0・RunOverlaps 11.3・SetupCandidates 10.7・BridgeRefresh 7.2・RenderBridges 3.9・WeaponClash 3.5・Kicks 3.1・RunFront 3.0・Snap 2.8・Bepu 2.7・SnapFind 2.4・Control 2.0・DriveFighter 1.3・ApplyControl 1.2・ClashPush 0.9・MayTouch 0.9・RenderParts 0.8・RunSeparate 0.5・PostStep 0.5・PostDebris 0.5・FighterStanding 0.5・FighterDown 0.4・SetupFar 0.4・SnapStiffness 0.4・SetupRelative 0.3
- 種 2 の 55.83 s: **99.9**（破片 2、GC0 0）: Weapons 92.6・SweepRun 72.2・RunPatch 34.2・RunOverlaps 14.6・SweepSetup 10.2・RunFront 9.4・RunScan 7.9・SetupCandidates 6.4・Bepu 4.8・Snap 3.6・Bridges 3.5・SnapFind 3.1・RenderBridges 2.1・BridgeRefresh 1.9・Control 1.9・DriveFighter 1.1・ApplyControl 1.1・RunSeparate 1.1・Bend 0.8・RunCrush 0.8・MayTouch 0.8・WeaponClash 0.7・Cut 0.7・ClashPush 0.6・RenderParts 0.5・RunHold 0.5・SnapStiffness 0.5・FighterStanding 0.4・FighterSplit 0.4・RunCut 0.4・FighterDown 0.4
- 種 2 の 14.07 s: **96.9**（破片 1、GC0 0）: Weapons 88.4・SweepRun 63.6・RunScan 36.7・SweepSetup 15.7・Bridges 13.4・RunPatch 12.7・SetupCandidates 10.6・RunOverlaps 8.3・BridgeRefresh 6.8・WeaponClash 3.6・RenderBridges 3.2・Kicks 3.1・Bepu 2.7・Snap 2.2・RunFront 2.1・Control 1.9・SnapFind 1.9・DriveFighter 1.2・ApplyControl 1.2・MayTouch 0.8・RunSeparate 0.6・RunHold 0.5・PostStep 0.5・RenderParts 0.5・PostDebris 0.4・FighterStanding 0.4・FighterDown 0.4・ClashPush 0.4・SetupRelative 0.4・SetupFar 0.3・RunCut 0.3
- 種 2 の 14.00 s: **94.5**（破片 1、GC0 0）: Weapons 85.4・SweepRun 59.8・RunPatch 22.9・RunScan 17.8・SweepSetup 15.5・RunOverlaps 12.3・Bridges 12.3・SetupCandidates 11.0・BridgeRefresh 6.9・WeaponClash 3.9・Kicks 3.3・RunFront 3.1・Snap 2.9・Bepu 2.7・RenderBridges 2.7・SnapFind 2.5・Control 2.0・DriveFighter 1.3・ApplyControl 1.2・MayTouch 0.8・ClashPush 0.7・FighterSplit 0.5・FighterStanding 0.5・PostStep 0.5・PostDebris 0.4・FighterDown 0.4・SnapStiffness 0.4・RunHold 0.4・SetupRelative 0.3
- 種 2 の 13.98 s: **92.3**（破片 1、GC0 0）: Weapons 86.5・SweepRun 61.0・RunPatch 29.2・SweepSetup 12.5・RunScan 12.0・RunOverlaps 11.9・Bridges 10.7・SetupCandidates 9.0・BridgeRefresh 5.9・RunFront 4.2・WeaponClash 4.1・RenderBridges 4.1・Snap 3.5・SnapFind 3.0・Bepu 3.0・Control 1.7・DriveFighter 1.2・ApplyControl 1.2・ClashPush 0.7・MayTouch 0.7・RunSeparate 0.6・FighterSplit 0.5・SnapStiffness 0.5・PostStep 0.4・PostDebris 0.4・FighterDown 0.4・FighterStanding 0.4・Cut 0.4・RunHold 0.3

