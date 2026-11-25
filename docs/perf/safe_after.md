## perf katana（2026-11-26 00:29、DESKTOP-2403GU4）

種 1 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 16.5 s。物理 104・判断 73。
チェックサム（計測あり）: fbe45c5d86ff14b4 / 計測なし: fbe45c5d86ff14b4（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 5.83 | 4.58 | 14.58 | 22.81 | 125.18 | 3.2% | 23 / 1 |
| 描画の詰め（CPU） | 0.96 | 0.78 | 1.94 | 6.07 | 114.51 | | |
| 　うちボクセル（FillVoxels） | 0.91 | 0.74 | 1.88 | 6.02 | 87.83 | | |
| 飛んでいる破片の数（描画の粒） | 32 | 9 | 130 | 153 | 167 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.600 | 10.3% | 0.73 | 3.88 |
| 　Perception | 0.071 | 1.2% | 0.10 | 0.42 |
| 　BotRead | 0.022 | 0.4% | 0.04 | 0.07 |
| 　DriveFighter | 0.488 | 8.4% | 0.56 | 3.73 |
| 　ApplyControl | 0.472 | 8.1% | 0.55 | 1.95 |
| 　FighterStamina | 0.068 | 1.2% | 0.11 | 0.87 |
| 　FighterStanding | 0.239 | 4.1% | 0.36 | 0.43 |
| 　FighterDown | 0.120 | 2.1% | 0.01 | 1.61 |
| 　ControlTail | 0.014 | 0.2% | 0.02 | 0.87 |
| PreBepu | 0.023 | 0.4% | 0.03 | 0.04 |
| 　Remember | 0.011 | 0.2% | 0.01 | 0.03 |
| Bepu | 0.958 | 16.4% | 1.02 | 6.58 |
| CanvasImpacts | 0.011 | 0.2% | 0.02 | 0.48 |
| FistHits | 0.002 | 0.0% | 0.00 | 0.02 |
| Weapons | 4.144 | 71.1% | 23.77 | 40.22 |
| 　MayTouch | 0.033 | 0.6% | 0.04 | 0.07 |
| 　SweepSetup | 1.134 | 19.4% | 4.40 | 9.17 |
| 　SetupRelative | 0.033 | 0.6% | 0.09 | 0.12 |
| 　SetupFar | 0.031 | 0.5% | 0.10 | 0.13 |
| 　SetupCandidates | 0.737 | 12.6% | 2.82 | 5.45 |
| 　SweepRun | 2.319 | 39.8% | 17.55 | 37.27 |
| 　RunScan | 1.487 | 25.5% | 13.11 | 22.11 |
| 　RunFront | 0.366 | 6.3% | 0.95 | 3.07 |
| 　RunOverlaps | 0.055 | 0.9% | 0.90 | 7.93 |
| 　RunPatch | 0.266 | 4.6% | 2.03 | 16.50 |
| 　RunHold | 0.002 | 0.0% | 0.03 | 0.47 |
| 　RunCrush | 0.046 | 0.8% | 0.06 | 0.91 |
| 　RunCut | 0.001 | 0.0% | 0.02 | 0.26 |
| 　RunSeparate | 0.057 | 1.0% | 0.16 | 0.97 |
| 　RunMass | 0.003 | 0.0% | 0.09 | 0.48 |
| 　Snap | 0.444 | 7.6% | 1.21 | 3.68 |
| 　SnapStiffness | 0.059 | 1.0% | 0.24 | 0.99 |
| 　SnapFind | 0.383 | 6.6% | 0.96 | 2.68 |
| 　Bend | 0.024 | 0.4% | 0.08 | 1.30 |
| 　Cut | 0.013 | 0.2% | 0.07 | 0.49 |
| 　Debris | 0.001 | 0.0% | 0.02 | 0.13 |
| 　WeaponClash | 0.408 | 7.0% | 3.07 | 10.61 |
| 　ClashAfter | 0.001 | 0.0% | 0.01 | 0.12 |
| 　ClashPush | 0.018 | 0.3% | 0.01 | 0.22 |
| Disconnect | 0.003 | 0.1% | 0.07 | 0.46 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.01 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.47 |
| FighterSplit | 0.017 | 0.3% | 0.04 | 1.19 |
| After | 0.005 | 0.1% | 0.01 | 0.02 |
| PostStep | 0.060 | 1.0% | 5.09 | 121.46 |
| 　PostDebris | 0.007 | 0.1% | 0.03 | 0.05 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.051 | 0.9% | 5.06 | 121.46 |
| 　RenderParts | 0.142 | 2.4% | 0.28 | 12.96 |
| 　RenderFragments | 0.001 | 0.0% | 0.00 | 0.01 |
| 　RenderBridges | 0.748 | 12.8% | 1.52 | 67.06 |
| （枠の外） | 0.005 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 21 / 73、Candidates 16734 / 64432、ScanSamples 247 / 1809、ChunksTested 4442 / 41831、BridgeCells 0 / 0、FilledRebuildCells 1269 / 53340、BeamRebuildCells 2696 / 107864

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 31.27 s: **125.2**（破片 0、GC0 1）: PostStep 121.5・PostRound 121.5・RenderBridges 11.4・Weapons 1.9・Bepu 1.1・SweepRun 0.9・RunScan 0.7・SweepSetup 0.6・Control 0.6・RenderParts 0.6・DriveFighter 0.5・ApplyControl 0.5・SetupCandidates 0.4
- 種 1 の 3.15 s: **41.7**（破片 1、GC0 0）: Weapons 40.2・SweepRun 37.3・RunPatch 16.5・RunScan 9.5・RunOverlaps 7.9・WeaponClash 4.6・SweepSetup 2.3・SetupCandidates 1.3・RenderBridges 1.1・Bepu 0.8・Control 0.5・RunHold 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 2.12 s: **33.4**（破片 1、GC0 1）: Weapons 31.4・SweepRun 21.1・RunScan 16.9・SweepSetup 9.2・SetupCandidates 3.3・WeaponClash 2.9・RunPatch 2.1・RenderBridges 1.1・Bepu 1.0・RunFront 0.9・Snap 0.8・RunOverlaps 0.8・Control 0.8・SnapFind 0.7・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4
- 種 1 の 2.60 s: **31.3**（破片 1、GC0 0）: Weapons 29.4・SweepRun 24.3・RunScan 22.1・SweepSetup 3.9・SetupCandidates 2.5・Bepu 1.0・RunPatch 1.0・Snap 0.9・SnapFind 0.8・Control 0.8・RunFront 0.6・DriveFighter 0.6・RenderBridges 0.6・ApplyControl 0.6・FighterStanding 0.4・RenderParts 0.4
- 種 1 の 33.30 s: **28.8**（破片 0、GC0 1）: Weapons 26.2・SweepRun 9.7・WeaponClash 7.7・SweepSetup 7.0・SetupCandidates 5.4・RunScan 5.0・Snap 3.7・SnapFind 2.7・RunFront 1.7・RunPatch 1.4・Bepu 1.2・SnapStiffness 1.0・RenderBridges 0.9・Control 0.8・RunOverlaps 0.6・DriveFighter 0.5・ApplyControl 0.5・Disconnect 0.4・RunMass 0.4・FighterStanding 0.4
- 種 1 の 2.33 s: **28.3**（破片 1、GC0 0）: Weapons 26.4・SweepRun 21.7・RunScan 20.9・SweepSetup 3.9・SetupCandidates 2.6・WeaponClash 2.4・Bepu 1.0・Control 0.7・RenderBridges 0.6・DriveFighter 0.6・ApplyControl 0.5・Snap 0.5・RunFront 0.5・SnapFind 0.5・FighterStanding 0.4
- 種 1 の 2.10 s: **27.7**（破片 1、GC0 0）: Weapons 25.7・SweepRun 18.7・RunScan 13.1・SweepSetup 5.1・SetupCandidates 3.1・WeaponClash 2.6・RunPatch 2.6・RenderBridges 2.4・RunOverlaps 1.3・Bepu 1.0・RunFront 1.0・Snap 0.9・SnapFind 0.8・Control 0.7・DriveFighter 0.6・ApplyControl 0.6・Cut 0.5・RunSeparate 0.4・FighterStanding 0.4
- 種 1 の 2.42 s: **27.5**（破片 1、GC0 0）: Weapons 25.5・SweepRun 20.6・RunScan 20.1・SweepSetup 4.3・SetupCandidates 2.9・WeaponClash 1.0・Bepu 1.0・Control 0.8・RenderBridges 0.8・DriveFighter 0.6・ApplyControl 0.6・Snap 0.4・FighterStanding 0.4・SnapFind 0.4
- 種 1 の 2.13 s: **27.0**（破片 1、GC0 0）: Weapons 25.0・SweepRun 17.9・RunScan 12.5・SweepSetup 5.4・SetupCandidates 3.4・RunPatch 2.6・WeaponClash 2.4・RenderBridges 1.5・RunFront 1.4・Snap 1.2・RenderParts 1.2・Bepu 1.0・SnapFind 1.0・RunOverlaps 1.0・Control 0.8・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4
- 種 1 の 2.58 s: **26.8**（破片 1、GC0 0）: Weapons 25.2・SweepRun 21.3・RunScan 20.2・SweepSetup 3.0・SetupCandidates 1.8・Bepu 0.9・RenderBridges 0.8・Control 0.7・RunFront 0.6・Snap 0.6・DriveFighter 0.5・ApplyControl 0.5・SnapFind 0.5・FighterStanding 0.3

## perf katana（2026-11-26 00:29、DESKTOP-2403GU4）

種 2 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 16.0 s。物理 104・判断 73。
チェックサム（計測あり）: da2a0304c2bbb0af / 計測なし: da2a0304c2bbb0af（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 5.89 | 5.23 | 10.54 | 20.26 | 127.72 | 2.0% | 24 / 1 |
| 描画の詰め（CPU） | 0.69 | 0.32 | 1.89 | 5.75 | 118.35 | | |
| 　うちボクセル（FillVoxels） | 0.64 | 0.29 | 1.84 | 5.51 | 89.67 | | |
| 飛んでいる破片の数（描画の粒） | 26 | 0 | 139 | 243 | 309 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.571 | 9.7% | 0.99 | 5.73 |
| 　Perception | 0.083 | 1.4% | 0.14 | 0.42 |
| 　BotRead | 0.016 | 0.3% | 0.05 | 0.09 |
| 　DriveFighter | 0.455 | 7.7% | 0.78 | 5.48 |
| 　ApplyControl | 0.450 | 7.6% | 0.77 | 5.47 |
| 　FighterStamina | 0.071 | 1.2% | 0.30 | 4.94 |
| 　FighterStanding | 0.135 | 2.3% | 0.39 | 0.45 |
| 　FighterDown | 0.199 | 3.4% | 0.01 | 0.42 |
| 　ControlTail | 0.013 | 0.2% | 0.02 | 0.04 |
| PreBepu | 0.023 | 0.4% | 0.03 | 0.08 |
| 　Remember | 0.010 | 0.2% | 0.01 | 0.07 |
| Bepu | 0.997 | 16.9% | 1.11 | 1.51 |
| CanvasImpacts | 0.014 | 0.2% | 0.01 | 0.57 |
| FistHits | 0.002 | 0.0% | 0.01 | 0.03 |
| Weapons | 4.194 | 71.2% | 24.52 | 40.09 |
| 　MayTouch | 0.029 | 0.5% | 0.04 | 0.09 |
| 　SweepSetup | 1.620 | 27.5% | 4.67 | 7.36 |
| 　SetupRelative | 0.045 | 0.8% | 0.08 | 0.14 |
| 　SetupFar | 0.046 | 0.8% | 0.11 | 0.16 |
| 　SetupCandidates | 1.088 | 18.5% | 3.00 | 4.55 |
| 　SweepRun | 2.036 | 34.6% | 16.59 | 31.96 |
| 　RunScan | 1.427 | 24.2% | 11.23 | 25.78 |
| 　RunFront | 0.245 | 4.2% | 1.48 | 2.66 |
| 　RunOverlaps | 0.038 | 0.7% | 1.04 | 3.79 |
| 　RunPatch | 0.187 | 3.2% | 2.24 | 7.26 |
| 　RunHold | 0.001 | 0.0% | 0.02 | 0.08 |
| 　RunCrush | 0.051 | 0.9% | 0.09 | 1.62 |
| 　RunCut | 0.001 | 0.0% | 0.02 | 0.09 |
| 　RunSeparate | 0.051 | 0.9% | 0.19 | 0.52 |
| 　RunMass | 0.001 | 0.0% | 0.05 | 0.25 |
| 　Snap | 0.376 | 6.4% | 2.24 | 25.50 |
| 　SnapStiffness | 0.052 | 0.9% | 0.31 | 3.39 |
| 　SnapFind | 0.323 | 5.5% | 1.93 | 22.11 |
| 　Bend | 0.035 | 0.6% | 0.68 | 15.19 |
| 　Cut | 0.014 | 0.2% | 0.11 | 0.53 |
| 　Debris | 0.001 | 0.0% | 0.03 | 0.18 |
| 　WeaponClash | 0.197 | 3.3% | 3.54 | 26.98 |
| 　ClashAfter | 0.001 | 0.0% | 0.00 | 0.03 |
| 　ClashPush | 0.002 | 0.0% | 0.00 | 0.02 |
| Disconnect | 0.002 | 0.0% | 0.00 | 0.44 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.01 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.02 |
| FighterSplit | 0.018 | 0.3% | 0.08 | 1.15 |
| After | 0.005 | 0.1% | 0.01 | 0.02 |
| PostStep | 0.059 | 1.0% | 5.12 | 122.39 |
| 　PostDebris | 0.005 | 0.1% | 0.02 | 0.04 |
| 　PostSwings | 0.002 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.051 | 0.9% | 5.10 | 122.39 |
| 　RenderParts | 0.125 | 2.1% | 0.39 | 13.66 |
| 　RenderFragments | 0.000 | 0.0% | 0.00 | 0.01 |
| 　RenderBridges | 0.502 | 8.5% | 1.83 | 67.79 |
| （枠の外） | 0.006 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 29 / 76、Candidates 22262 / 78528、ScanSamples 185 / 1204、ChunksTested 4422 / 36516、BridgeCells 0 / 0、FilledRebuildCells 267 / 0、BeamRebuildCells 598 / 27201

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 19.78 s: **127.7**（破片 0、GC0 1）: PostStep 122.4・PostRound 122.4・RenderBridges 8.3・Weapons 3.6・SweepSetup 2.2・SetupCandidates 1.4・SweepRun 1.3・RunScan 1.3・Bepu 1.1・RenderParts 0.6・Control 0.5・DriveFighter 0.4・ApplyControl 0.4・FighterDown 0.3
- 種 2 の 26.23 s: **42.2**（破片 0、GC0 0）: Weapons 40.1・SweepRun 32.0・RunScan 25.8・SweepSetup 6.4・SetupCandidates 4.0・RunPatch 2.8・RunFront 1.6・RenderBridges 1.6・Snap 1.4・WeaponClash 1.3・Bepu 1.2・SnapFind 1.2・RunOverlaps 1.1・Control 0.8・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4・RenderParts 0.3
- 種 2 の 1.85 s: **39.4**（破片 0、GC0 0）: Weapons 37.1・SweepRun 29.1・RunScan 15.0・RunPatch 7.3・SweepSetup 6.0・SetupCandidates 4.1・RunOverlaps 3.2・RunFront 2.7・RenderBridges 1.8・WeaponClash 1.8・Snap 1.4・Bepu 1.3・SnapFind 1.2・Control 0.9・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4・RenderParts 0.4・Cut 0.3
- 種 2 の 26.22 s: **35.9**（破片 0、GC0 0）: Weapons 33.8・SweepRun 26.1・RunScan 14.5・SweepSetup 5.6・RunPatch 4.9・SetupCandidates 3.8・RunOverlaps 3.8・WeaponClash 2.5・RunFront 2.0・Snap 1.5・RenderBridges 1.5・SnapFind 1.3・Bepu 1.1・RenderParts 1.0・Control 0.8・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4・Cut 0.3
- 種 2 の 26.25 s: **35.2**（破片 0、GC0 0）: Weapons 33.2・SweepRun 24.9・RunScan 22.8・SweepSetup 6.7・SetupCandidates 4.3・WeaponClash 3.1・RenderBridges 1.8・RunFront 1.3・Snap 1.2・Bepu 1.1・SnapFind 1.0・Control 0.8・DriveFighter 0.6・ApplyControl 0.6・RenderParts 0.5・RunPatch 0.4・FighterStanding 0.4
- 種 2 の 1.83 s: **35.0**（破片 0、GC0 0）: Weapons 32.8・SweepRun 26.4・RunScan 14.8・RunPatch 5.7・SweepSetup 4.6・RunOverlaps 3.2・SetupCandidates 3.2・WeaponClash 2.9・RunFront 1.9・RenderBridges 1.7・Bepu 1.2・RenderParts 1.0・Control 0.9・Snap 0.8・SnapFind 0.7・DriveFighter 0.6・ApplyControl 0.6・Cut 0.5・FighterStanding 0.4
- 種 2 の 26.27 s: **34.8**（破片 0、GC0 0）: Weapons 32.8・SweepRun 26.0・RunScan 24.0・WeaponClash 6.0・SweepSetup 5.6・SetupCandidates 3.3・RenderBridges 1.6・Bepu 1.1・RunFront 1.0・Snap 0.9・Control 0.7・SnapFind 0.7・DriveFighter 0.6・RunPatch 0.6・ApplyControl 0.6・FighterStanding 0.4
- 種 2 の 26.28 s: **34.0**（破片 0、GC0 0）: Weapons 31.9・SweepRun 24.5・RunScan 22.6・WeaponClash 6.5・SweepSetup 6.2・SetupCandidates 3.8・RenderBridges 1.6・RunFront 1.2・Bepu 1.1・Snap 1.0・SnapFind 0.8・Control 0.8・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4・RunPatch 0.3
- 種 2 の 21.68 s: **33.0**（破片 0、GC0 0）: Weapons 30.8・Bend 15.2・SweepRun 11.7・RunScan 4.6・RunPatch 3.7・SweepSetup 2.7・SetupCandidates 1.9・WeaponClash 1.8・RenderBridges 1.8・RunOverlaps 1.8・Bepu 1.1・RunFront 0.9・Snap 0.8・Control 0.8・SnapFind 0.7・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4・RenderParts 0.3
- 種 2 の 26.30 s: **30.3**（破片 0、GC0 0）: Weapons 28.3・SweepRun 20.9・RunScan 19.2・WeaponClash 7.1・SweepSetup 6.1・SetupCandidates 3.7・RenderBridges 1.5・Bepu 1.2・RunFront 1.1・Snap 1.0・SnapFind 0.8・Control 0.7・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4・RunPatch 0.3

## perf vfc2（2026-11-26 00:30、DESKTOP-2403GU4）

種 1 × 最大 60 秒、3600 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 31.9 s。物理 104・判断 73。
チェックサム（計測あり）: c09cca6729af6d75 / 計測なし: c09cca6729af6d75（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 7.52 | 5.54 | 19.65 | 34.95 | 108.33 | 7.1% | 38 / 1 |
| 描画の詰め（CPU） | 1.27 | 1.04 | 2.61 | 4.92 | 116.16 | | |
| 　うちボクセル（FillVoxels） | 1.17 | 0.96 | 2.48 | 4.79 | 88.44 | | |
| 飛んでいる破片の数（描画の粒） | 108 | 34 | 450 | 567 | 583 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.672 | 8.9% | 1.02 | 4.18 |
| 　Perception | 0.122 | 1.6% | 0.28 | 1.18 |
| 　BotRead | 0.057 | 0.8% | 0.06 | 0.76 |
| 　BotPerceive | 0.001 | 0.0% | 0.00 | 0.03 |
| 　DriveFighter | 0.470 | 6.3% | 0.64 | 3.90 |
| 　ApplyControl | 0.460 | 6.1% | 0.63 | 1.39 |
| 　FighterStamina | 0.074 | 1.0% | 0.11 | 1.00 |
| 　FighterStanding | 0.286 | 3.8% | 0.23 | 0.45 |
| 　FighterDown | 0.052 | 0.7% | 0.21 | 0.29 |
| 　ControlTail | 0.017 | 0.2% | 0.02 | 0.10 |
| PreBepu | 0.024 | 0.3% | 0.03 | 0.09 |
| 　Remember | 0.011 | 0.1% | 0.01 | 0.08 |
| Bepu | 1.422 | 18.9% | 1.97 | 3.58 |
| CanvasImpacts | 0.011 | 0.1% | 0.03 | 0.86 |
| FistHits | 0.004 | 0.1% | 0.01 | 0.02 |
| Weapons | 5.241 | 69.7% | 42.81 | 103.77 |
| 　MayTouch | 0.030 | 0.4% | 0.04 | 0.11 |
| 　SweepSetup | 1.622 | 21.6% | 4.11 | 7.08 |
| 　SetupRelative | 0.051 | 0.7% | 0.07 | 0.15 |
| 　SetupFar | 0.042 | 0.6% | 0.07 | 0.15 |
| 　SetupCandidates | 1.076 | 14.3% | 2.74 | 5.00 |
| 　SweepRun | 3.218 | 42.8% | 36.99 | 98.89 |
| 　RunScan | 1.708 | 22.7% | 8.38 | 21.19 |
| 　RunFront | 0.416 | 5.5% | 2.61 | 5.70 |
| 　RunOverlaps | 0.252 | 3.3% | 7.44 | 24.67 |
| 　RunPatch | 0.670 | 8.9% | 16.01 | 52.77 |
| 　RunHold | 0.007 | 0.1% | 0.31 | 1.42 |
| 　RunCrush | 0.034 | 0.5% | 0.07 | 1.01 |
| 　RunCut | 0.006 | 0.1% | 0.23 | 0.80 |
| 　RunSeparate | 0.057 | 0.8% | 0.27 | 1.52 |
| 　RunMass | 0.006 | 0.1% | 0.07 | 0.25 |
| 　Snap | 0.269 | 3.6% | 1.33 | 2.70 |
| 　SnapStiffness | 0.038 | 0.5% | 0.19 | 0.48 |
| 　SnapFind | 0.230 | 3.1% | 1.13 | 2.32 |
| 　Bend | 0.024 | 0.3% | 0.09 | 0.63 |
| 　Cut | 0.018 | 0.2% | 0.10 | 0.27 |
| 　Debris | 0.002 | 0.0% | 0.04 | 0.13 |
| 　WeaponClash | 0.133 | 1.8% | 0.05 | 6.35 |
| 　ClashAfter | 0.000 | 0.0% | 0.00 | 0.03 |
| 　ClashPush | 0.002 | 0.0% | 0.00 | 0.11 |
| Disconnect | 0.001 | 0.0% | 0.00 | 0.21 |
| Kicks | 0.053 | 0.7% | 0.00 | 3.27 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.031 | 0.4% | 0.29 | 1.17 |
| After | 0.005 | 0.1% | 0.02 | 0.02 |
| PostStep | 0.045 | 0.6% | 0.10 | 20.99 |
| 　PostDebris | 0.030 | 0.4% | 0.10 | 0.38 |
| 　PostWounds | 0.000 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.013 | 0.2% | 0.00 | 20.99 |
| 　RenderParts | 0.144 | 1.9% | 0.20 | 13.45 |
| 　RenderFragments | 0.001 | 0.0% | 0.00 | 0.02 |
| 　RenderBridges | 1.013 | 13.5% | 1.66 | 67.44 |
| （枠の外） | 0.007 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 24 / 48、Candidates 22618 / 65343、ScanSamples 218 / 784、ChunksTested 5747 / 42988、BridgeCells 0 / 0、FilledRebuildCells 81 / 0、BeamRebuildCells 314 / 1384

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 47.83 s: **108.3**（破片 1、GC0 0）: Weapons 103.8・SweepRun 98.9・RunPatch 52.8・RunOverlaps 24.7・RunScan 12.2・SweepSetup 3.4・SetupCandidates 2.3・RunFront 2.1・Bepu 2.1・RenderBridges 1.7・Snap 1.2・FighterSplit 1.2・RunHold 1.1・SnapFind 1.0・Control 1.0・DriveFighter 0.7・ApplyControl 0.7・RunCut 0.7・RenderParts 0.3
- 種 1 の 47.08 s: **76.8**（破片 0、GC0 0）: Weapons 73.0・SweepRun 67.4・RunPatch 32.2・RunOverlaps 18.9・RunScan 5.3・SweepSetup 3.4・RunFront 2.6・SetupCandidates 2.3・Bepu 1.9・RenderBridges 1.9・Snap 1.4・RunHold 1.4・SnapFind 1.2・Control 1.2・RunCut 0.8・DriveFighter 0.7・ApplyControl 0.6・Perception 0.5・FighterSplit 0.4・RunSeparate 0.3
- 種 1 の 47.80 s: **67.7**（破片 0、GC0 0）: Weapons 64.1・SweepRun 59.0・RunPatch 31.3・RunOverlaps 16.4・RunScan 3.8・SweepSetup 3.2・RunFront 2.8・SetupCandidates 2.2・Bepu 2.1・Snap 1.6・SnapFind 1.3・RenderBridges 1.1・Control 1.0・DriveFighter 0.7・ApplyControl 0.6・RunHold 0.6・RunCut 0.5
- 種 1 の 47.07 s: **60.4**（破片 0、GC0 0）: Weapons 57.0・SweepRun 51.7・RunPatch 26.3・RunOverlaps 12.0・RunScan 4.6・SweepSetup 3.4・RunFront 3.0・SetupCandidates 2.3・Bepu 1.9・RenderBridges 1.5・Snap 1.4・SnapFind 1.2・Control 1.0・RunHold 0.9・DriveFighter 0.6・ApplyControl 0.6・RunCut 0.5・FighterSplit 0.3・RenderParts 0.3・Perception 0.3
- 種 1 の 47.13 s: **58.3**（破片 0、GC0 0）: Weapons 54.6・SweepRun 50.1・RunPatch 23.0・RunOverlaps 12.6・RunScan 4.3・SweepSetup 3.0・RunFront 2.4・Bepu 2.1・SetupCandidates 2.0・RenderBridges 1.7・RunHold 1.3・Snap 1.2・SnapFind 1.0・Control 1.0・RunCut 0.8・DriveFighter 0.7・ApplyControl 0.7・FighterSplit 0.5
- 種 1 の 45.55 s: **51.4**（破片 0、GC0 0）: Weapons 48.0・SweepRun 40.4・RunPatch 17.5・RunScan 9.0・RunOverlaps 7.9・SweepSetup 5.1・RunFront 4.3・SetupCandidates 3.1・Snap 2.1・Bepu 2.0・SnapFind 1.8・RenderBridges 1.7・Control 1.1・DriveFighter 0.6・ApplyControl 0.6・Perception 0.3
- 種 1 の 47.78 s: **51.0**（破片 0、GC0 0）: Weapons 47.6・SweepRun 42.9・RunPatch 19.0・RunOverlaps 9.8・RunScan 8.6・SweepSetup 3.0・RunFront 2.4・Bepu 2.0・SetupCandidates 2.0・Snap 1.4・RenderBridges 1.3・SnapFind 1.2・Control 1.0・DriveFighter 0.6・ApplyControl 0.6・RenderParts 0.3・RunHold 0.3
- 種 1 の 47.12 s: **50.9**（破片 0、GC0 0）: Weapons 47.1・SweepRun 43.4・RunPatch 16.6・RunOverlaps 10.2・RunScan 8.5・SweepSetup 2.4・Bepu 2.0・RenderBridges 1.9・SetupCandidates 1.6・RunFront 1.5・Control 1.0・RunHold 0.9・Snap 0.9・SnapFind 0.7・FighterSplit 0.7・DriveFighter 0.7・ApplyControl 0.7・RunCut 0.6
- 種 1 の 45.50 s: **50.5**（破片 0、GC0 0）: Weapons 47.3・SweepRun 39.5・RunScan 21.2・RunPatch 10.5・SweepSetup 5.6・RunFront 3.9・SetupCandidates 3.5・RunOverlaps 2.8・Bepu 2.1・Snap 1.8・SnapFind 1.6・RenderBridges 1.1・Control 1.0・DriveFighter 0.7・ApplyControl 0.7・RunSeparate 0.4
- 種 1 の 45.48 s: **48.8**（破片 0、GC0 0）: Weapons 45.4・SweepRun 36.5・RunScan 20.0・RunPatch 8.9・SweepSetup 6.3・SetupCandidates 4.1・RunFront 4.1・RenderBridges 2.3・RunOverlaps 2.2・Bepu 2.0・Snap 1.8・SnapFind 1.5・Control 1.0・DriveFighter 0.7・ApplyControl 0.7・RunSeparate 0.5・RenderParts 0.4・Bend 0.4

## perf royale（2026-11-26 00:34、DESKTOP-2403GU4）

種 1 × 最大 60 秒、3600 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 96.8 s。物理 104・判断 73。
チェックサム（計測あり）: aa84c4f93d8e624f / 計測なし: aa84c4f93d8e624f（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 24.22 | 23.44 | 40.85 | 51.43 | 127.87 | 85.5% | 124 / 1 |
| 描画の詰め（CPU） | 2.48 | 2.06 | 4.38 | 10.03 | 219.12 | | |
| 　うちボクセル（FillVoxels） | 2.31 | 1.91 | 4.16 | 9.77 | 190.38 | | |
| 飛んでいる破片の数（描画の粒） | 164 | 76 | 548 | 690 | 730 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 2.245 | 9.3% | 2.47 | 6.14 |
| 　Perception | 0.372 | 1.5% | 0.45 | 3.73 |
| 　BotRead | 0.207 | 0.9% | 0.34 | 4.30 |
| 　BotPerceive | 0.057 | 0.2% | 0.13 | 3.97 |
| 　BotRead2 | 0.018 | 0.1% | 0.03 | 0.06 |
| 　BotChoose | 0.005 | 0.0% | 0.01 | 0.18 |
| 　BotExecute | 0.007 | 0.0% | 0.01 | 0.03 |
| 　DriveFighter | 1.392 | 5.7% | 1.46 | 2.87 |
| 　ApplyControl | 1.355 | 5.6% | 1.43 | 2.50 |
| 　FighterStamina | 0.219 | 0.9% | 0.24 | 1.33 |
| 　FighterStanding | 0.606 | 2.5% | 0.69 | 1.76 |
| 　FighterDown | 0.361 | 1.5% | 0.30 | 1.03 |
| 　ControlTail | 0.158 | 0.7% | 0.10 | 1.84 |
| PreBepu | 0.065 | 0.3% | 0.07 | 0.77 |
| 　Remember | 0.032 | 0.1% | 0.04 | 0.09 |
| Bepu | 3.869 | 16.0% | 3.87 | 6.06 |
| CanvasImpacts | 0.033 | 0.1% | 0.04 | 0.33 |
| FistHits | 0.019 | 0.1% | 0.02 | 1.17 |
| Weapons | 16.865 | 69.6% | 56.17 | 119.84 |
| 　MayTouch | 0.601 | 2.5% | 0.63 | 1.56 |
| 　SweepSetup | 6.576 | 27.2% | 13.02 | 24.08 |
| 　SetupRelative | 0.156 | 0.6% | 0.25 | 0.35 |
| 　SetupFar | 0.155 | 0.6% | 0.28 | 0.44 |
| 　SetupCandidates | 4.402 | 18.2% | 8.79 | 16.91 |
| 　SweepRun | 7.805 | 32.2% | 38.81 | 96.93 |
| 　RunScan | 5.291 | 21.8% | 18.17 | 35.33 |
| 　RunFront | 1.037 | 4.3% | 3.56 | 7.39 |
| 　RunOverlaps | 0.178 | 0.7% | 4.17 | 23.24 |
| 　RunPatch | 0.804 | 3.3% | 10.02 | 44.07 |
| 　RunHold | 0.007 | 0.0% | 0.21 | 1.06 |
| 　RunCrush | 0.148 | 0.6% | 0.17 | 1.78 |
| 　RunCut | 0.005 | 0.0% | 0.17 | 0.91 |
| 　RunSeparate | 0.210 | 0.9% | 0.83 | 2.12 |
| 　RunMass | 0.007 | 0.0% | 0.09 | 0.29 |
| 　Snap | 0.773 | 3.2% | 1.83 | 5.31 |
| 　SnapStiffness | 0.116 | 0.5% | 0.27 | 0.98 |
| 　SnapFind | 0.651 | 2.7% | 1.54 | 4.34 |
| 　SnapSplit | 0.001 | 0.0% | 0.00 | 3.14 |
| 　Bend | 0.087 | 0.4% | 0.21 | 1.57 |
| 　Cut | 0.092 | 0.4% | 0.30 | 0.93 |
| 　Debris | 0.003 | 0.0% | 0.04 | 0.10 |
| 　WeaponClash | 1.256 | 5.2% | 2.96 | 13.90 |
| 　ClashAfter | 0.002 | 0.0% | 0.00 | 0.21 |
| 　ClashPush | 0.458 | 1.9% | 0.77 | 1.53 |
| Disconnect | 0.004 | 0.0% | 0.00 | 0.57 |
| Kicks | 0.906 | 3.7% | 0.95 | 10.97 |
| Elbows | 0.003 | 0.0% | 0.00 | 0.02 |
| FighterSplit | 0.098 | 0.4% | 0.33 | 1.68 |
| After | 0.017 | 0.1% | 0.03 | 0.04 |
| PostStep | 0.082 | 0.3% | 0.19 | 3.27 |
| 　PostDebris | 0.052 | 0.2% | 0.17 | 0.27 |
| 　PostWounds | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.003 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.024 | 0.1% | 0.01 | 3.18 |
| 　RenderParts | 0.449 | 1.9% | 0.65 | 15.15 |
| 　RenderFragments | 0.002 | 0.0% | 0.00 | 0.02 |
| 　RenderBridges | 1.832 | 7.6% | 3.05 | 163.19 |
| （枠の外） | 0.012 | 0.0% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 101 / 164、Candidates 97043 / 198380、ScanSamples 452 / 1851、ChunksTested 16485 / 68457、BridgeCells 0 / 0、FilledRebuildCells 216 / 0、BeamRebuildCells 695 / 2083

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 12.68 s: **127.9**（破片 1、GC0 0）: Weapons 119.8・SweepRun 96.9・RunPatch 43.3・RunOverlaps 23.2・SweepSetup 15.3・RunScan 12.1・SetupCandidates 10.0・RunFront 7.4・WeaponClash 4.8・RenderBridges 3.8・Bepu 3.4・Snap 3.0・Control 2.7・SnapFind 2.5・RunSeparate 2.1・DriveFighter 1.5・ApplyControl 1.4・FighterSplit 1.4・ClashPush 1.4・RunHold 1.0・RenderParts 1.0・RunCut 0.9・FighterStanding 0.8・MayTouch 0.7・BotRead 0.6・Cut 0.6・SnapStiffness 0.5・Perception 0.5・SetupFar 0.4・SetupRelative 0.3・BotPerceive 0.3
- 種 1 の 12.62 s: **116.2**（破片 0、GC0 0）: Weapons 109.5・SweepRun 89.5・RunPatch 44.1・RunOverlaps 19.7・SweepSetup 12.6・RunScan 10.0・SetupCandidates 8.6・RunFront 6.5・WeaponClash 5.1・RenderBridges 4.0・Bepu 3.3・Snap 3.0・Control 2.9・SnapFind 2.5・DriveFighter 1.5・ApplyControl 1.5・ClashPush 1.3・RunHold 1.0・RunSeparate 0.9・RunCut 0.9・FighterStanding 0.9・MayTouch 0.6・BotRead 0.6・RenderParts 0.5・Perception 0.5・SnapStiffness 0.5・Cut 0.4・BotPerceive 0.4・Bend 0.3
- 種 1 の 12.63 s: **95.8**（破片 0、GC0 0）: Weapons 88.2・SweepRun 67.4・RunPatch 23.6・RunScan 15.5・SweepSetup 13.5・RunOverlaps 12.6・SetupCandidates 9.0・WeaponClash 7.3・RunFront 5.7・RenderBridges 4.1・Bepu 3.5・Control 3.2・Snap 2.7・SnapFind 2.3・DriveFighter 1.5・ApplyControl 1.5・ClashPush 1.3・RunHold 1.1・RunSeparate 1.0・FighterStanding 0.9・Perception 0.8・RunCut 0.8・BotRead 0.7・RenderParts 0.6・Cut 0.6・MayTouch 0.6・FighterSplit 0.5・SnapStiffness 0.4・BotPerceive 0.4・Bend 0.3
- 種 1 の 12.60 s: **82.8**（破片 0、GC0 0）: Weapons 76.1・SweepRun 57.9・RunPatch 27.4・RunOverlaps 11.8・SweepSetup 11.5・RunScan 9.3・SetupCandidates 7.8・RunFront 5.2・RenderBridges 3.7・Bepu 3.2・Control 2.7・Snap 2.6・SnapFind 2.2・WeaponClash 2.0・DriveFighter 1.5・ApplyControl 1.5・ClashPush 1.2・FighterStanding 0.9・RenderParts 0.8・MayTouch 0.6・RunSeparate 0.6・BotRead 0.5・Perception 0.5・RunHold 0.4・FighterSplit 0.4・SnapStiffness 0.4・RunCut 0.4・Cut 0.3
- 種 1 の 12.98 s: **76.6**（破片 1、GC0 0）: Weapons 70.1・SweepRun 52.6・RunScan 29.0・SweepSetup 12.5・RunPatch 12.2・SetupCandidates 8.5・RunOverlaps 3.7・Bepu 3.5・RenderBridges 3.3・RunFront 3.2・Control 2.1・Snap 1.9・SnapFind 1.6・RunSeparate 1.4・DriveFighter 1.4・ApplyControl 1.4・WeaponClash 1.4・RenderParts 0.8・FighterStanding 0.7・MayTouch 0.7・ClashPush 0.5・RunHold 0.4・FighterSplit 0.4・Perception 0.4・RunCut 0.4
- 種 1 の 12.70 s: **73.3**（破片 1、GC0 1）: Weapons 66.7・SweepRun 45.0・RunScan 29.3・SweepSetup 16.1・SetupCandidates 10.6・RunPatch 5.7・WeaponClash 5.3・RunFront 4.3・Bepu 3.6・RenderBridges 3.0・Control 2.5・RunOverlaps 2.0・Snap 1.6・RunSeparate 1.6・SnapFind 1.4・DriveFighter 1.4・ApplyControl 1.4・ClashPush 1.2・FighterStanding 0.7・RenderParts 0.7・Perception 0.7・MayTouch 0.6・SetupFar 0.4・BotRead 0.3・SetupRelative 0.3
- 種 1 の 12.72 s: **72.4**（破片 1、GC0 0）: Weapons 65.9・SweepRun 42.4・RunScan 20.7・SweepSetup 17.4・SetupCandidates 11.6・RunPatch 10.0・RunOverlaps 5.2・RunFront 4.3・RenderBridges 4.0・WeaponClash 3.9・Bepu 3.6・Control 2.2・Snap 2.0・SnapFind 1.7・DriveFighter 1.4・ApplyControl 1.4・RunSeparate 1.3・ClashPush 1.3・RenderParts 0.8・FighterStanding 0.7・MayTouch 0.6・Cut 0.4・SetupFar 0.4・Perception 0.4・SetupRelative 0.3・BotRead 0.3・SnapStiffness 0.3
- 種 1 の 16.97 s: **69.9**（破片 1、GC0 0）: Weapons 62.7・SweepRun 53.4・RunPatch 28.7・RunOverlaps 13.1・SweepSetup 5.5・SetupCandidates 3.9・Bepu 3.9・RunFront 3.2・RenderBridges 2.7・Control 2.1・RunScan 2.0・DriveFighter 1.4・ApplyControl 1.4・Snap 1.2・WeaponClash 1.0・SnapFind 1.0・RunHold 1.0・FighterSplit 0.9・RunCut 0.7・MayTouch 0.6・ClashPush 0.5・FighterDown 0.5・FighterStanding 0.5・RenderParts 0.3・Perception 0.3
- 種 1 の 13.13 s: **69.2**（破片 1、GC0 0）: Weapons 62.7・SweepRun 34.2・RunScan 23.8・SweepSetup 22.0・SetupCandidates 15.4・RunFront 4.4・WeaponClash 4.0・RenderBridges 4.0・Bepu 3.6・RunPatch 3.5・Snap 2.7・SnapFind 2.3・Control 2.1・DriveFighter 1.4・ApplyControl 1.3・RenderParts 1.0・RunOverlaps 1.0・RunSeparate 1.0・ClashPush 0.9・FighterStanding 0.7・MayTouch 0.6・SnapStiffness 0.4・Bend 0.4・SetupFar 0.3・SetupRelative 0.3・Cut 0.3・Perception 0.3
- 種 1 の 13.12 s: **66.2**（破片 1、GC0 0）: Weapons 60.0・SweepRun 29.8・SweepSetup 24.1・RunScan 21.4・SetupCandidates 16.9・WeaponClash 4.5・RunFront 4.0・Bepu 3.6・RenderBridges 2.8・Snap 2.5・RunPatch 2.2・SnapFind 2.1・Control 2.1・RunSeparate 1.6・DriveFighter 1.4・ApplyControl 1.3・RenderParts 1.0・ClashPush 0.9・FighterStanding 0.7・MayTouch 0.6・Bend 0.4・SnapStiffness 0.4・SetupRelative 0.3・SetupFar 0.3・Perception 0.3

## perf royale（2026-11-26 00:37、DESKTOP-2403GU4）

種 2 × 最大 60 秒、3600 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 100.9 s。物理 104・判断 73。
チェックサム（計測あり）: 793c1a150eca9089 / 計測なし: 793c1a150eca9089（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 24.52 | 22.92 | 47.78 | 65.72 | 179.03 | 65.1% | 118 / 1 |
| 描画の詰め（CPU） | 3.27 | 3.02 | 4.52 | 10.81 | 223.89 | | |
| 　うちボクセル（FillVoxels） | 3.08 | 2.84 | 4.32 | 10.51 | 193.93 | | |
| 飛んでいる破片の数（描画の粒） | 288 | 248 | 723 | 764 | 781 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 2.265 | 9.2% | 2.80 | 4.44 |
| 　Perception | 0.309 | 1.3% | 0.43 | 1.17 |
| 　BotRead | 0.253 | 1.0% | 0.29 | 1.23 |
| 　BotPerceive | 0.062 | 0.3% | 0.07 | 0.89 |
| 　BotRead2 | 0.028 | 0.1% | 0.03 | 0.11 |
| 　BotChoose | 0.011 | 0.0% | 0.01 | 0.19 |
| 　BotExecute | 0.016 | 0.1% | 0.02 | 0.05 |
| 　DriveFighter | 1.509 | 6.2% | 1.71 | 4.18 |
| 　ApplyControl | 1.465 | 6.0% | 1.64 | 4.15 |
| 　FighterStamina | 0.229 | 0.9% | 0.26 | 3.11 |
| 　FighterStanding | 0.694 | 2.8% | 0.76 | 1.01 |
| 　FighterDown | 0.372 | 1.5% | 0.42 | 0.94 |
| 　ControlTail | 0.082 | 0.3% | 0.16 | 1.47 |
| PreBepu | 0.066 | 0.3% | 0.07 | 0.85 |
| 　Remember | 0.032 | 0.1% | 0.03 | 0.15 |
| Bepu | 3.057 | 12.5% | 3.87 | 5.10 |
| CanvasImpacts | 0.034 | 0.1% | 0.06 | 0.46 |
| FistHits | 0.016 | 0.1% | 0.03 | 0.86 |
| Weapons | 18.052 | 73.6% | 70.10 | 171.28 |
| 　MayTouch | 0.619 | 2.5% | 0.75 | 0.97 |
| 　SweepSetup | 6.530 | 26.6% | 16.48 | 32.57 |
| 　SetupRelative | 0.154 | 0.6% | 0.27 | 0.38 |
| 　SetupFar | 0.173 | 0.7% | 0.33 | 0.50 |
| 　SetupCandidates | 4.434 | 18.1% | 11.07 | 22.88 |
| 　SweepRun | 7.661 | 31.2% | 49.06 | 158.82 |
| 　RunScan | 4.151 | 16.9% | 19.01 | 51.74 |
| 　RunFront | 1.422 | 5.8% | 3.85 | 6.89 |
| 　RunOverlaps | 0.326 | 1.3% | 6.71 | 36.53 |
| 　RunPatch | 1.245 | 5.1% | 15.39 | 66.84 |
| 　RunHold | 0.013 | 0.1% | 0.47 | 5.17 |
| 　RunCrush | 0.109 | 0.4% | 0.18 | 2.04 |
| 　RunCut | 0.009 | 0.0% | 0.32 | 2.96 |
| 　RunSeparate | 0.233 | 0.9% | 0.60 | 2.43 |
| 　RunMass | 0.010 | 0.0% | 0.06 | 0.60 |
| 　Snap | 1.043 | 4.3% | 2.61 | 5.74 |
| 　SnapStiffness | 0.158 | 0.6% | 0.39 | 1.37 |
| 　SnapFind | 0.879 | 3.6% | 2.17 | 4.50 |
| 　SnapSplit | 0.000 | 0.0% | 0.03 | 1.03 |
| 　Bend | 0.084 | 0.3% | 0.32 | 1.02 |
| 　Cut | 0.113 | 0.5% | 0.36 | 3.13 |
| 　Debris | 0.005 | 0.0% | 0.04 | 0.20 |
| 　WeaponClash | 4.064 | 16.6% | 7.19 | 29.32 |
| 　ClashAfter | 0.006 | 0.0% | 0.01 | 0.57 |
| 　ClashPush | 1.406 | 5.7% | 1.89 | 2.95 |
| Disconnect | 0.005 | 0.0% | 0.01 | 0.49 |
| Kicks | 0.771 | 3.1% | 3.21 | 14.81 |
| Elbows | 0.003 | 0.0% | 0.00 | 1.09 |
| FighterSplit | 0.102 | 0.4% | 0.27 | 2.29 |
| After | 0.020 | 0.1% | 0.03 | 0.08 |
| PostStep | 0.119 | 0.5% | 0.23 | 3.45 |
| 　PostDebris | 0.087 | 0.4% | 0.20 | 0.28 |
| 　PostWounds | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.003 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.028 | 0.1% | 0.03 | 3.29 |
| 　RenderParts | 0.499 | 2.0% | 0.49 | 16.24 |
| 　RenderFragments | 0.001 | 0.0% | 0.00 | 0.01 |
| 　RenderBridges | 2.539 | 10.4% | 2.69 | 163.58 |
| （枠の外） | 0.011 | 0.0% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 102 / 196、Candidates 90984 / 254558、ScanSamples 577 / 1806、ChunksTested 12597 / 70306、BridgeCells 0 / 0、FilledRebuildCells 795 / 5528、BeamRebuildCells 1881 / 11723

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 49.23 s: **179.0**（破片 0、GC0 0）: Weapons 171.3・SweepRun 158.8・RunPatch 66.8・RunOverlaps 36.5・RunScan 25.7・SweepSetup 7.1・RunHold 5.2・SetupCandidates 4.5・Bepu 4.3・RunCut 3.0・Control 2.6・RenderBridges 2.6・RunFront 1.8・DriveFighter 1.7・ApplyControl 1.7・WeaponClash 1.3・ClashPush 1.1・Bend 0.8・FighterStanding 0.7・Snap 0.7・MayTouch 0.7・SnapFind 0.6・FighterDown 0.5・Cut 0.5・FighterSplit 0.4・Perception 0.4
- 種 2 の 49.22 s: **114.9**（破片 0、GC0 0）: Weapons 107.2・SweepRun 93.8・RunPatch 49.6・RunOverlaps 19.1・SweepSetup 7.4・RunScan 6.3・SetupCandidates 4.7・Bepu 4.2・RunFront 2.9・RenderBridges 2.7・Control 2.6・RunHold 1.9・DriveFighter 1.8・ApplyControl 1.7・Snap 1.5・RunCut 1.5・WeaponClash 1.3・SnapFind 1.3・ClashPush 1.0・FighterStanding 0.8・RunSeparate 0.7・MayTouch 0.7・Bend 0.7・Cut 0.5・FighterDown 0.5・Perception 0.4・FighterSplit 0.4
- 種 2 の 48.53 s: **105.9**（破片 0、GC0 0）: Weapons 96.8・SweepRun 82.5・RunPatch 35.0・RunOverlaps 18.7・RunScan 9.4・SweepSetup 7.7・RunFront 6.3・Bepu 4.9・SetupCandidates 4.7・RenderBridges 4.2・Control 3.2・WeaponClash 2.9・Snap 2.7・RunHold 2.2・SnapFind 2.2・DriveFighter 1.7・ApplyControl 1.6・RunCut 1.4・Perception 1.2・RenderParts 0.8・Cut 0.7・ClashPush 0.7・MayTouch 0.7・FighterSplit 0.6・FighterStanding 0.6・FighterDown 0.6・SnapStiffness 0.4
- 種 2 の 49.13 s: **101.7**（破片 0、GC0 0）: Weapons 93.4・SweepRun 78.5・RunPatch 43.5・RunOverlaps 18.5・SweepSetup 7.3・RunFront 6.6・RunScan 5.8・SetupCandidates 4.4・Bepu 4.3・RenderBridges 3.9・Control 3.2・Snap 3.2・SnapFind 2.7・DriveFighter 1.7・ApplyControl 1.7・WeaponClash 1.2・RunSeparate 0.8・Perception 0.8・FighterStanding 0.7・MayTouch 0.7・ClashPush 0.7・Bend 0.7・Cut 0.5・SnapStiffness 0.5・FighterDown 0.4・FighterSplit 0.4・RenderParts 0.4・RunHold 0.4
- 種 2 の 10.47 s: **98.0**（破片 0、GC0 1）: Weapons 86.6・SweepRun 69.9・RunScan 42.2・RunPatch 15.0・SweepSetup 13.5・SetupCandidates 7.9・RunOverlaps 7.1・WeaponClash 4.9・Kicks 4.3・RunFront 3.9・Control 3.3・Bepu 3.3・DriveFighter 2.3・Snap 2.2・ClashPush 2.1・RenderBridges 1.9・SnapFind 1.9・ApplyControl 1.6・RunSeparate 1.0・FighterStanding 0.9・MayTouch 0.8・SnapStiffness 0.4・Perception 0.3
- 種 2 の 48.55 s: **97.1**（破片 0、GC0 0）: Weapons 88.3・SweepRun 74.0・RunPatch 32.0・RunOverlaps 15.7・RunScan 10.8・SweepSetup 7.6・RunFront 6.2・Bepu 5.0・SetupCandidates 4.6・Control 3.1・RenderBridges 2.9・Snap 2.9・SnapFind 2.4・WeaponClash 2.1・DriveFighter 1.7・ApplyControl 1.6・RunHold 1.3・Perception 1.0・RunCut 0.9・RenderParts 0.8・MayTouch 0.7・ClashPush 0.7・FighterDown 0.6・FighterStanding 0.6・Cut 0.6・RunSeparate 0.6・SnapStiffness 0.5・FighterSplit 0.3
- 種 2 の 49.20 s: **91.3**（破片 0、GC0 1）: Weapons 83.6・SweepRun 69.5・RunPatch 32.1・RunOverlaps 14.0・RunScan 7.8・SweepSetup 7.7・SetupCandidates 4.9・Bepu 4.1・RenderBridges 3.5・Control 2.7・RunFront 2.2・DriveFighter 1.8・ApplyControl 1.7・RunHold 1.7・RunCut 1.4・WeaponClash 1.3・Snap 1.1・SnapFind 0.9・ClashPush 0.9・FighterStanding 0.7・RunSeparate 0.7・MayTouch 0.7・Bend 0.6・RenderParts 0.5・FighterSplit 0.5・Cut 0.5・FighterDown 0.5・Perception 0.4
- 種 2 の 10.45 s: **89.9**（破片 0、GC0 0）: Weapons 79.2・SweepRun 64.1・RunScan 38.9・RunPatch 13.7・SweepSetup 11.6・SetupCandidates 7.4・RunOverlaps 6.6・WeaponClash 5.4・Kicks 4.4・RunFront 3.8・Bepu 3.3・Snap 2.6・Control 2.5・RenderBridges 2.5・SnapFind 2.2・ClashPush 2.2・DriveFighter 1.6・ApplyControl 1.6・MayTouch 0.9・FighterStanding 0.9・RunSeparate 0.4・SnapStiffness 0.4
- 種 2 の 49.12 s: **85.2**（破片 0、GC0 0）: Weapons 76.8・SweepRun 62.8・RunPatch 31.0・RunOverlaps 14.2・RunScan 10.9・SweepSetup 7.3・RunFront 5.0・SetupCandidates 4.3・Bepu 4.3・RenderBridges 4.2・Control 2.8・Snap 2.5・SnapFind 2.1・DriveFighter 1.7・ApplyControl 1.7・WeaponClash 1.1・RenderParts 1.1・ClashPush 0.7・FighterStanding 0.7・MayTouch 0.7・Bend 0.6・Cut 0.5・FighterSplit 0.5・Perception 0.5・CanvasImpacts 0.5・FighterDown 0.5・SnapStiffness 0.4・RunSeparate 0.4
- 種 2 の 10.73 s: **84.1**（破片 0、GC0 0）: Weapons 77.2・SweepRun 61.0・RunScan 51.7・SweepSetup 9.8・SetupCandidates 6.0・WeaponClash 4.4・RunPatch 3.6・RunFront 3.4・Bepu 3.4・Control 2.6・ClashPush 1.9・Snap 1.8・RenderBridges 1.8・DriveFighter 1.6・ApplyControl 1.6・SnapFind 1.5・RunOverlaps 1.0・RunSeparate 0.9・FighterStanding 0.9・MayTouch 0.8・Kicks 0.4

