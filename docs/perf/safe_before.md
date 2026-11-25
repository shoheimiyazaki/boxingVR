## perf katana（2026-11-26 00:14、DESKTOP-2403GU4）

種 1 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 20.1 s。物理 102・判断 73。
チェックサム（計測あり）: dee9e51fcd693fdc / 計測なし: dee9e51fcd693fdc（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 7.38 | 5.03 | 23.21 | 33.10 | 134.13 | 8.8% | 26 / 1 |
| 描画の詰め（CPU） | 0.94 | 0.73 | 2.50 | 3.89 | 69.28 | | |
| 　うちボクセル（FillVoxels） | 0.89 | 0.70 | 2.40 | 3.72 | 45.24 | | |
| 飛んでいる破片の数（描画の粒） | 47 | 8 | 195 | 266 | 322 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.681 | 9.2% | 1.30 | 6.77 |
| 　Perception | 0.081 | 1.1% | 0.11 | 3.38 |
| 　BotRead | 0.022 | 0.3% | 0.03 | 2.38 |
| 　DriveFighter | 0.559 | 7.6% | 1.13 | 6.58 |
| 　ApplyControl | 0.540 | 7.3% | 1.12 | 6.58 |
| 　FighterStamina | 0.075 | 1.0% | 0.09 | 3.49 |
| 　FighterStanding | 0.273 | 3.7% | 0.47 | 5.26 |
| 　FighterDown | 0.137 | 1.9% | 0.25 | 3.36 |
| 　ControlTail | 0.015 | 0.2% | 0.02 | 2.35 |
| PreBepu | 0.028 | 0.4% | 0.03 | 2.78 |
| 　Remember | 0.015 | 0.2% | 0.01 | 2.77 |
| Bepu | 1.016 | 13.8% | 1.36 | 7.22 |
| CanvasImpacts | 0.012 | 0.2% | 0.10 | 2.03 |
| FistHits | 0.002 | 0.0% | 0.00 | 0.02 |
| Weapons | 5.535 | 75.0% | 33.57 | 44.55 |
| 　MayTouch | 0.056 | 0.8% | 0.08 | 2.18 |
| 　SweepSetup | 1.481 | 20.1% | 7.34 | 13.66 |
| 　SetupRelative | 0.041 | 0.6% | 0.19 | 3.34 |
| 　SetupFar | 0.037 | 0.5% | 0.22 | 2.63 |
| 　SetupCandidates | 0.976 | 13.2% | 4.73 | 9.97 |
| 　SweepRun | 2.572 | 34.9% | 19.41 | 36.92 |
| 　RunScan | 1.725 | 23.4% | 11.53 | 28.47 |
| 　RunFront | 0.388 | 5.3% | 2.46 | 11.85 |
| 　RunOverlaps | 0.048 | 0.6% | 1.07 | 7.75 |
| 　RunPatch | 0.262 | 3.6% | 3.30 | 16.41 |
| 　RunHold | 0.001 | 0.0% | 0.03 | 0.47 |
| 　RunCrush | 0.046 | 0.6% | 0.22 | 2.31 |
| 　RunCut | 0.001 | 0.0% | 0.02 | 0.23 |
| 　RunSeparate | 0.061 | 0.8% | 0.43 | 1.65 |
| 　RunMass | 0.004 | 0.0% | 0.03 | 0.40 |
| 　Snap | 0.454 | 6.2% | 2.07 | 7.95 |
| 　SnapStiffness | 0.060 | 0.8% | 0.29 | 2.27 |
| 　SnapFind | 0.392 | 5.3% | 1.77 | 7.25 |
| 　Bend | 0.024 | 0.3% | 0.08 | 2.14 |
| 　Cut | 0.013 | 0.2% | 0.08 | 1.35 |
| 　Debris | 0.001 | 0.0% | 0.01 | 0.06 |
| 　WeaponClash | 0.507 | 6.9% | 2.96 | 9.92 |
| 　ClashAfter | 0.001 | 0.0% | 0.00 | 0.07 |
| 　ClashPush | 0.018 | 0.2% | 0.00 | 0.23 |
| 　Bridges | 0.998 | 13.5% | 7.66 | 13.15 |
| 　BridgeRefresh | 0.702 | 9.5% | 4.05 | 8.71 |
| Disconnect | 0.003 | 0.0% | 0.01 | 0.44 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.01 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.019 | 0.3% | 0.04 | 3.63 |
| After | 0.005 | 0.1% | 0.01 | 0.01 |
| PostStep | 0.066 | 0.9% | 5.45 | 129.76 |
| 　PostDebris | 0.010 | 0.1% | 0.04 | 0.08 |
| 　PostWounds | 0.000 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.054 | 0.7% | 5.41 | 129.76 |
| 　RenderParts | 0.160 | 2.2% | 0.69 | 12.69 |
| 　RenderFragments | 0.001 | 0.0% | 0.00 | 0.01 |
| 　RenderBridges | 0.703 | 9.5% | 2.73 | 34.32 |
| （枠の外） | 0.005 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 24 / 78、Candidates 20091 / 89532、ScanSamples 276 / 1443、ChunksTested 4967 / 34098、BridgeCells 469 / 4232、FilledRebuildCells 1669 / 6668、BeamRebuildCells 3416 / 13928

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 31.27 s: **134.1**（破片 0、GC0 1）: PostStep 129.8・PostRound 129.8・RenderBridges 8.5・Weapons 2.5・SweepRun 1.3・Bepu 1.1・RunScan 1.1・Control 0.7・RenderParts 0.7・SweepSetup 0.6・DriveFighter 0.6・ApplyControl 0.5・SetupCandidates 0.4
- 種 1 の 37.37 s: **46.6**（破片 0、GC0 0）: Weapons 44.5・SweepRun 31.9・RunScan 28.5・Bridges 9.0・WeaponClash 7.9・SweepSetup 6.3・BridgeRefresh 4.6・SetupCandidates 4.2・RunFront 1.7・Snap 1.3・RenderBridges 1.3・SnapFind 1.2・Bepu 1.1・RunPatch 0.9・Control 0.7・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4・RunSeparate 0.3
- 種 1 の 37.35 s: **45.2**（破片 0、GC0 0）: Weapons 43.3・SweepRun 30.3・RunScan 23.5・Bridges 7.9・SweepSetup 6.6・WeaponClash 6.3・SetupCandidates 4.6・BridgeRefresh 4.5・RunPatch 2.8・RunFront 1.9・Snap 1.5・RenderBridges 1.4・RunOverlaps 1.4・SnapFind 1.3・Bepu 1.0・Control 0.7・DriveFighter 0.5・ApplyControl 0.5・RunSeparate 0.4・FighterStanding 0.4
- 種 1 の 37.38 s: **43.6**（破片 0、GC0 0）: Weapons 41.6・SweepRun 28.9・RunScan 22.1・Bridges 8.5・WeaponClash 7.8・SweepSetup 6.2・BridgeRefresh 4.6・SetupCandidates 4.2・RunPatch 3.3・RenderBridges 1.8・RunOverlaps 1.5・Snap 1.5・RunFront 1.4・SnapFind 1.3・Bepu 1.1・Control 0.8・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4・RunSeparate 0.3
- 種 1 の 3.15 s: **43.2**（破片 1、GC0 0）: Weapons 41.8・SweepRun 36.9・RunPatch 16.4・RunScan 9.4・RunOverlaps 7.7・WeaponClash 4.6・SweepSetup 2.3・Bridges 2.1・BridgeRefresh 2.0・SetupCandidates 1.4・RenderBridges 1.1・Bepu 0.8・Control 0.5・RunHold 0.5・DriveFighter 0.4・ApplyControl 0.4
- 種 1 の 37.33 s: **43.2**（破片 0、GC0 0）: Weapons 41.3・SweepRun 24.8・RunScan 17.3・Bridges 13.1・SweepSetup 9.4・SetupCandidates 6.3・BridgeRefresh 5.2・RunPatch 2.8・RunFront 2.2・Snap 1.4・WeaponClash 1.3・SnapFind 1.2・RenderBridges 1.0・Bepu 1.0・RunSeparate 0.9・RunOverlaps 0.8・Control 0.7・RenderParts 0.5・DriveFighter 0.5・ApplyControl 0.5・FighterStanding 0.4
- 種 1 の 37.10 s: **41.6**（破片 0、GC0 0）: Weapons 39.7・SweepRun 26.6・RunPatch 9.4・Bridges 8.7・RunScan 8.4・SweepSetup 6.5・SetupCandidates 4.4・RunOverlaps 4.0・BridgeRefresh 3.7・RunFront 2.6・Snap 2.2・WeaponClash 2.0・SnapFind 1.9・RunSeparate 1.2・RenderBridges 1.1・Bepu 1.0・Control 0.7・RenderParts 0.5・DriveFighter 0.5・ApplyControl 0.5・FighterStanding 0.4
- 種 1 の 1.88 s: **41.0**（破片 0、GC0 0）: RenderBridges 34.3・Weapons 32.9・SweepRun 24.8・RunFront 11.8・RenderParts 10.9・RunPatch 9.9・WeaponClash 7.5・Control 6.8・DriveFighter 6.6・ApplyControl 6.6・SweepSetup 3.1・Bridges 2.9・BridgeRefresh 2.8・SetupCandidates 2.1・RunScan 1.8・Snap 1.6・SnapFind 1.4・Bepu 1.3・RunSeparate 0.7・FighterStanding 0.4・RunOverlaps 0.3
- 種 1 の 37.32 s: **38.7**（破片 0、GC0 0）: Weapons 36.7・SweepRun 20.7・Bridges 10.9・RunScan 10.0・SweepSetup 9.1・SetupCandidates 6.5・RunPatch 4.7・BridgeRefresh 4.5・WeaponClash 2.5・RunFront 2.3・Snap 2.0・SnapFind 1.8・RenderBridges 1.8・RunSeparate 1.6・RunOverlaps 1.4・Bepu 1.0・Control 0.7・RenderParts 0.5・DriveFighter 0.5・ApplyControl 0.5・FighterStanding 0.4
- 種 1 の 37.40 s: **38.0**（破片 0、GC0 0）: Weapons 36.0・SweepRun 25.1・RunScan 17.0・Bridges 7.8・WeaponClash 7.7・SweepSetup 5.2・RunPatch 4.4・BridgeRefresh 4.0・SetupCandidates 3.4・RenderBridges 2.1・RunOverlaps 1.9・Snap 1.3・SnapFind 1.1・RunFront 1.1・Bepu 1.0・Control 0.7・DriveFighter 0.5・ApplyControl 0.5・FighterStanding 0.4

## perf katana（2026-11-26 00:15、DESKTOP-2403GU4）

種 2 × 最大 40 秒、2400 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 23.3 s。物理 102・判断 73。
チェックサム（計測あり）: e1b6fa9397864923 / 計測なし: e1b6fa9397864923（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 8.89 | 7.50 | 21.61 | 35.52 | 129.50 | 8.8% | 37 / 2 |
| 描画の詰め（CPU） | 0.77 | 0.54 | 1.97 | 2.75 | 69.71 | | |
| 　うちボクセル（FillVoxels） | 0.72 | 0.51 | 1.91 | 2.66 | 42.85 | | |
| 飛んでいる破片の数（描画の粒） | 39 | 9 | 163 | 242 | 315 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.588 | 6.6% | 0.76 | 3.45 |
| 　Perception | 0.108 | 1.2% | 0.12 | 2.10 |
| 　BotRead | 0.018 | 0.2% | 0.04 | 0.08 |
| 　DriveFighter | 0.445 | 5.0% | 0.57 | 3.34 |
| 　ApplyControl | 0.440 | 5.0% | 0.56 | 3.34 |
| 　FighterStamina | 0.069 | 0.8% | 0.10 | 3.01 |
| 　FighterStanding | 0.135 | 1.5% | 0.35 | 2.27 |
| 　FighterDown | 0.191 | 2.1% | 0.05 | 1.15 |
| 　ControlTail | 0.013 | 0.1% | 0.02 | 0.03 |
| PreBepu | 0.022 | 0.2% | 0.03 | 0.59 |
| 　Remember | 0.009 | 0.1% | 0.01 | 0.57 |
| Bepu | 0.934 | 10.5% | 1.13 | 3.11 |
| CanvasImpacts | 0.014 | 0.2% | 0.03 | 0.35 |
| FistHits | 0.002 | 0.0% | 0.00 | 0.03 |
| Weapons | 7.233 | 81.4% | 39.10 | 63.16 |
| 　MayTouch | 0.047 | 0.5% | 0.08 | 0.41 |
| 　SweepSetup | 2.182 | 24.5% | 6.78 | 9.59 |
| 　SetupRelative | 0.066 | 0.7% | 0.12 | 0.49 |
| 　SetupFar | 0.067 | 0.7% | 0.20 | 0.68 |
| 　SetupCandidates | 1.455 | 16.4% | 4.56 | 6.25 |
| 　SweepRun | 2.969 | 33.4% | 26.74 | 49.36 |
| 　RunScan | 1.873 | 21.1% | 20.72 | 48.01 |
| 　RunFront | 0.554 | 6.2% | 1.54 | 4.49 |
| 　RunOverlaps | 0.055 | 0.6% | 1.25 | 4.90 |
| 　RunPatch | 0.288 | 3.2% | 2.66 | 8.06 |
| 　RunHold | 0.002 | 0.0% | 0.02 | 0.15 |
| 　RunCrush | 0.056 | 0.6% | 0.11 | 1.15 |
| 　RunCut | 0.001 | 0.0% | 0.02 | 0.15 |
| 　RunSeparate | 0.087 | 1.0% | 0.15 | 1.19 |
| 　RunMass | 0.002 | 0.0% | 0.07 | 0.41 |
| 　Snap | 0.601 | 6.8% | 1.18 | 4.20 |
| 　SnapStiffness | 0.078 | 0.9% | 0.20 | 0.99 |
| 　SnapFind | 0.521 | 5.9% | 0.97 | 3.20 |
| 　Bend | 0.045 | 0.5% | 0.05 | 0.79 |
| 　Cut | 0.018 | 0.2% | 0.07 | 0.26 |
| 　Debris | 0.001 | 0.0% | 0.02 | 0.07 |
| 　WeaponClash | 0.420 | 4.7% | 2.54 | 12.77 |
| 　ClashAfter | 0.001 | 0.0% | 0.01 | 0.07 |
| 　ClashPush | 0.002 | 0.0% | 0.00 | 0.01 |
| 　Bridges | 1.889 | 21.2% | 9.59 | 18.49 |
| 　BridgeRefresh | 1.234 | 13.9% | 3.93 | 7.92 |
| Disconnect | 0.002 | 0.0% | 0.05 | 0.45 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.01 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.020 | 0.2% | 0.04 | 1.16 |
| After | 0.006 | 0.1% | 0.01 | 0.03 |
| PostStep | 0.061 | 0.7% | 5.06 | 120.73 |
| 　PostDebris | 0.009 | 0.1% | 0.02 | 0.05 |
| 　PostSwings | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.051 | 0.6% | 5.03 | 120.72 |
| 　RenderParts | 0.165 | 1.9% | 0.33 | 12.56 |
| 　RenderFragments | 0.000 | 0.0% | 0.00 | 0.01 |
| 　RenderBridges | 0.536 | 6.0% | 1.84 | 22.01 |
| （枠の外） | 0.006 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 48 / 109、Candidates 29272 / 100288、ScanSamples 321 / 3437、ChunksTested 5780 / 64703、BridgeCells 1323 / 7796、FilledRebuildCells 533 / 26670、BeamRebuildCells 1171 / 54208

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 17.40 s: **129.5**（破片 0、GC0 1）: PostStep 120.7・PostRound 120.7・RenderBridges 8.4・Weapons 7.0・SweepSetup 3.0・Bridges 2.7・SetupCandidates 2.0・BridgeRefresh 1.9・SweepRun 1.6・Bepu 1.1・RunScan 0.9・RenderParts 0.7・Control 0.6・RunFront 0.5・DriveFighter 0.4・ApplyControl 0.4・FighterDown 0.3
- 種 2 の 34.37 s: **65.7**（破片 0、GC0 0）: Weapons 63.2・SweepRun 49.4・RunScan 48.0・Bridges 17.9・SweepSetup 7.6・BridgeRefresh 5.1・SetupCandidates 4.9・RenderBridges 1.9・Bepu 1.4・Control 0.9・RunFront 0.9・DriveFighter 0.6・ApplyControl 0.6・Snap 0.6・SnapFind 0.5・RenderParts 0.5・FighterStanding 0.4
- 種 2 の 34.38 s: **55.2**（破片 0、GC0 0）: Weapons 52.9・SweepRun 38.3・RunScan 36.1・Bridges 16.2・SweepSetup 7.4・BridgeRefresh 5.3・SetupCandidates 4.7・RenderBridges 1.6・Bepu 1.3・RunFront 1.2・Snap 1.0・SnapFind 0.8・Control 0.8・DriveFighter 0.6・ApplyControl 0.6・RunPatch 0.5・FighterStanding 0.4
- 種 2 の 23.27 s: **47.7**（破片 0、GC0 0）: Weapons 45.7・SweepRun 31.9・RunScan 23.1・Bridges 10.6・SweepSetup 7.4・SetupCandidates 4.8・BridgeRefresh 4.5・RunPatch 3.6・RunOverlaps 2.6・WeaponClash 2.3・RunFront 1.5・Snap 1.2・SnapFind 1.0・Bepu 1.0・RenderBridges 0.9・RenderParts 0.9・Control 0.8・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4
- 種 2 の 34.15 s: **47.6**（破片 0、GC0 0）: Weapons 45.6・SweepRun 33.3・RunScan 30.7・Bridges 18.5・SweepSetup 7.0・SetupCandidates 4.6・BridgeRefresh 4.1・RunFront 1.3・RenderBridges 1.3・Bepu 1.1・Snap 0.8・Control 0.8・RunSeparate 0.7・SnapFind 0.7・WeaponClash 0.6・DriveFighter 0.6・ApplyControl 0.6・RunPatch 0.5・FighterStanding 0.4
- 種 2 の 23.48 s: **45.5**（破片 0、GC0 1）: Weapons 42.8・SweepRun 25.1・WeaponClash 12.8・RunScan 10.6・SweepSetup 8.8・Bridges 7.5・SetupCandidates 6.2・RunPatch 5.6・Snap 4.2・BridgeRefresh 4.1・RunFront 4.1・SnapFind 3.2・RunOverlaps 2.5・Bepu 1.2・RunCrush 1.2・SnapStiffness 1.0・Control 0.9・DriveFighter 0.6・ApplyControl 0.6・RenderBridges 0.5・Disconnect 0.4・FighterStanding 0.4・RunMass 0.4
- 種 2 の 34.40 s: **45.1**（破片 0、GC0 0）: Weapons 42.9・SweepRun 31.0・RunScan 29.7・Bridges 10.8・SweepSetup 6.4・BridgeRefresh 4.4・SetupCandidates 4.3・RenderBridges 1.6・Bepu 1.3・Control 0.8・RunFront 0.7・Snap 0.7・DriveFighter 0.6・SnapFind 0.6・ApplyControl 0.6・RenderParts 0.4・FighterStanding 0.4
- 種 2 の 34.02 s: **44.1**（破片 0、GC0 0）: Weapons 42.0・SweepRun 29.7・RunScan 18.8・SweepSetup 7.3・Bridges 6.1・RunPatch 5.7・SetupCandidates 5.3・BridgeRefresh 3.6・RunOverlaps 2.7・RunFront 1.4・RenderBridges 1.2・Bepu 1.1・Snap 1.0・SnapFind 0.8・Control 0.8・DriveFighter 0.6・ApplyControl 0.6・RenderParts 0.6・FighterStanding 0.4
- 種 2 の 2.27 s: **43.6**（破片 0、GC0 0）: Weapons 41.4・SweepRun 27.9・RunScan 20.4・Bridges 11.7・SweepSetup 8.1・SetupCandidates 5.7・BridgeRefresh 4.1・RunPatch 4.1・RunOverlaps 1.7・RenderBridges 1.6・WeaponClash 1.5・Bepu 1.3・RunFront 1.3・Snap 0.9・SnapFind 0.8・Control 0.7・DriveFighter 0.5・ApplyControl 0.5・FighterStanding 0.4
- 種 2 の 34.10 s: **42.3**（破片 0、GC0 0）: Weapons 40.3・SweepRun 24.9・RunScan 21.4・Bridges 12.5・SweepSetup 9.0・SetupCandidates 6.3・BridgeRefresh 4.8・RenderBridges 1.6・RunFront 1.4・RunPatch 1.3・Snap 1.1・Bepu 1.1・WeaponClash 1.1・SnapFind 1.0・Control 0.8・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4・RunOverlaps 0.3

## perf vfc2（2026-11-26 00:16、DESKTOP-2403GU4）

種 1 × 最大 60 秒、3600 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 32.0 s。物理 102・判断 73。
チェックサム（計測あり）: 45299016a240e907 / 計測なし: 45299016a240e907（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 7.73 | 5.68 | 20.35 | 31.16 | 88.12 | 9.1% | 43 / 1 |
| 描画の詰め（CPU） | 1.09 | 0.99 | 1.77 | 2.34 | 71.55 | | |
| 　うちボクセル（FillVoxels） | 1.00 | 0.91 | 1.67 | 2.23 | 44.83 | | |
| 飛んでいる破片の数（描画の粒） | 74 | 32 | 246 | 265 | 305 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.590 | 7.6% | 0.88 | 1.88 |
| 　Perception | 0.072 | 0.9% | 0.13 | 1.34 |
| 　BotRead | 0.074 | 1.0% | 0.13 | 0.91 |
| 　DriveFighter | 0.426 | 5.5% | 0.60 | 1.67 |
| 　ApplyControl | 0.421 | 5.4% | 0.59 | 1.66 |
| 　FighterStamina | 0.068 | 0.9% | 0.10 | 0.66 |
| 　FighterStanding | 0.272 | 3.5% | 0.23 | 1.56 |
| 　FighterDown | 0.036 | 0.5% | 0.19 | 0.47 |
| 　ControlTail | 0.013 | 0.2% | 0.02 | 0.44 |
| PreBepu | 0.022 | 0.3% | 0.03 | 0.14 |
| 　Remember | 0.010 | 0.1% | 0.01 | 0.13 |
| Bepu | 1.206 | 15.6% | 1.95 | 2.40 |
| CanvasImpacts | 0.011 | 0.1% | 0.02 | 0.69 |
| FistHits | 0.003 | 0.0% | 0.01 | 0.04 |
| Weapons | 5.822 | 75.3% | 42.80 | 84.47 |
| 　MayTouch | 0.043 | 0.6% | 0.08 | 0.27 |
| 　SweepSetup | 1.594 | 20.6% | 5.07 | 8.67 |
| 　SetupRelative | 0.048 | 0.6% | 0.10 | 0.19 |
| 　SetupFar | 0.045 | 0.6% | 0.11 | 0.31 |
| 　SetupCandidates | 1.043 | 13.5% | 3.34 | 6.08 |
| 　SweepRun | 2.893 | 37.4% | 33.96 | 75.03 |
| 　RunScan | 1.844 | 23.8% | 12.68 | 19.52 |
| 　RunFront | 0.376 | 4.9% | 1.91 | 3.55 |
| 　RunOverlaps | 0.131 | 1.7% | 5.44 | 14.59 |
| 　RunPatch | 0.404 | 5.2% | 11.12 | 39.15 |
| 　RunHold | 0.006 | 0.1% | 0.38 | 2.18 |
| 　RunCrush | 0.030 | 0.4% | 0.08 | 1.03 |
| 　RunCut | 0.004 | 0.1% | 0.26 | 1.52 |
| 　RunSeparate | 0.043 | 0.6% | 0.22 | 2.35 |
| 　RunMass | 0.006 | 0.1% | 0.06 | 0.48 |
| 　Snap | 0.269 | 3.5% | 1.10 | 3.77 |
| 　SnapStiffness | 0.039 | 0.5% | 0.16 | 1.00 |
| 　SnapFind | 0.229 | 3.0% | 0.91 | 2.76 |
| 　SnapSplit | 0.000 | 0.0% | 0.02 | 0.87 |
| 　Bend | 0.024 | 0.3% | 0.05 | 0.66 |
| 　Cut | 0.015 | 0.2% | 0.07 | 0.26 |
| 　Debris | 0.002 | 0.0% | 0.02 | 0.10 |
| 　WeaponClash | 0.090 | 1.2% | 0.15 | 8.02 |
| 　ClashAfter | 0.001 | 0.0% | 0.02 | 0.53 |
| 　ClashPush | 0.001 | 0.0% | 0.00 | 0.01 |
| 　Bridges | 1.284 | 16.6% | 3.96 | 13.38 |
| 　BridgeRefresh | 0.872 | 11.3% | 2.21 | 5.27 |
| Disconnect | 0.004 | 0.1% | 0.01 | 0.66 |
| Kicks | 0.001 | 0.0% | 0.00 | 0.01 |
| Elbows | 0.001 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.026 | 0.3% | 0.35 | 5.61 |
| After | 0.005 | 0.1% | 0.01 | 0.02 |
| PostStep | 0.035 | 0.5% | 0.07 | 19.46 |
| 　PostDebris | 0.022 | 0.3% | 0.06 | 0.10 |
| 　PostSwings | 0.002 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.011 | 0.1% | 0.00 | 19.45 |
| 　RenderParts | 0.148 | 1.9% | 0.22 | 13.07 |
| 　RenderFragments | 0.000 | 0.0% | 0.00 | 0.01 |
| 　RenderBridges | 0.832 | 10.8% | 1.39 | 24.27 |
| （枠の外） | 0.006 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 25 / 62、Candidates 23051 / 81168、ScanSamples 261 / 1475、ChunksTested 5707 / 50188、BridgeCells 755 / 1686、FilledRebuildCells 1445 / 4356、BeamRebuildCells 3392 / 7118

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 49.82 s: **88.1**（破片 0、GC0 0）: Weapons 84.5・SweepRun 75.0・RunPatch 39.2・RunOverlaps 14.6・RunScan 10.5・SweepSetup 4.8・Bridges 3.6・SetupCandidates 3.1・BridgeRefresh 3.0・RunFront 2.4・Bepu 2.2・RenderBridges 1.6・Snap 1.2・RunHold 1.0・SnapFind 1.0・Control 0.8・RunCut 0.8・DriveFighter 0.6・ApplyControl 0.6・FighterSplit 0.4
- 種 1 の 49.73 s: **75.0**（破片 0、GC0 0）: Weapons 71.2・SweepRun 65.1・RunPatch 23.9・RunScan 16.8・RunOverlaps 13.0・SweepSetup 4.1・SetupCandidates 2.7・Bepu 2.1・RunFront 1.7・RunHold 1.6・Bridges 1.5・RenderBridges 1.5・RunCut 1.2・BridgeRefresh 0.9・Control 0.8・FighterSplit 0.8・DriveFighter 0.6・ApplyControl 0.6・Snap 0.6・SnapFind 0.5・RenderParts 0.3
- 種 1 の 49.67 s: **73.9**（破片 0、GC0 0）: Weapons 65.4・SweepRun 59.8・RunPatch 24.7・RunScan 11.9・RunOverlaps 11.1・FighterSplit 5.6・SweepSetup 4.0・SetupCandidates 2.8・Bepu 2.0・RunHold 1.9・RenderBridges 1.6・RunCut 1.2・RunFront 1.2・Bridges 1.1・Control 0.8・BridgeRefresh 0.6・DriveFighter 0.6・ApplyControl 0.6・Snap 0.6・SnapFind 0.5
- 種 1 の 49.70 s: **73.1**（破片 0、GC0 0）: Weapons 69.9・SweepRun 64.6・RunPatch 24.0・RunScan 14.3・RunOverlaps 11.4・SweepSetup 3.6・SetupCandidates 2.3・RunHold 2.2・Bepu 2.0・RunFront 1.7・RenderBridges 1.6・RunCut 1.5・Bridges 1.3・Control 0.8・BridgeRefresh 0.7・DriveFighter 0.6・ApplyControl 0.6・Snap 0.6・SnapFind 0.5・FighterSplit 0.3
- 種 1 の 49.68 s: **65.6**（破片 0、GC0 0）: Weapons 62.3・SweepRun 56.3・RunPatch 22.4・RunOverlaps 12.8・RunScan 12.6・SweepSetup 4.5・SetupCandidates 3.0・Bepu 2.0・RenderBridges 1.5・RunFront 1.4・RunHold 1.4・Bridges 1.2・Control 0.8・RunCut 0.8・BridgeRefresh 0.7・DriveFighter 0.6・ApplyControl 0.6・Snap 0.6・SnapFind 0.5・FighterSplit 0.3
- 種 1 の 49.85 s: **61.1**（破片 0、GC0 0）: Weapons 57.4・SweepRun 43.9・RunScan 15.7・RunPatch 14.4・RunOverlaps 6.6・SweepSetup 6.6・Bridges 5.2・SetupCandidates 4.5・WeaponClash 3.2・BridgeRefresh 3.1・Snap 2.9・RunFront 2.6・Bepu 2.2・SnapFind 1.6・RenderBridges 1.3・Control 0.9・SnapSplit 0.9・DriveFighter 0.6・ApplyControl 0.6・RunSeparate 0.5・ClashAfter 0.5・RunHold 0.5・SnapStiffness 0.5
- 種 1 の 49.83 s: **59.2**（破片 0、GC0 0）: Weapons 55.8・SweepRun 46.4・RunPatch 18.2・RunScan 13.2・RunOverlaps 7.0・SweepSetup 5.1・Bridges 3.7・SetupCandidates 3.3・BridgeRefresh 2.8・RunFront 2.7・Bepu 2.2・RenderBridges 1.4・Snap 1.1・SnapFind 0.9・Control 0.9・DriveFighter 0.7・ApplyControl 0.6・WeaponClash 0.6・RenderParts 0.6・RunHold 0.5・RunCut 0.4・RunSeparate 0.3
- 種 1 の 49.63 s: **56.5**（破片 0、GC0 0）: Weapons 53.2・SweepRun 46.9・RunScan 16.6・RunPatch 13.3・RunOverlaps 9.8・SweepSetup 4.8・SetupCandidates 3.2・Bepu 2.0・RenderBridges 1.8・RunHold 1.2・RunFront 1.1・Bridges 1.1・Control 0.8・RunCut 0.7・BridgeRefresh 0.6・DriveFighter 0.6・ApplyControl 0.6・Snap 0.6・SnapFind 0.5・FighterSplit 0.4
- 種 1 の 49.90 s: **55.5**（破片 0、GC0 0）: Weapons 52.0・SweepRun 42.3・RunScan 15.6・RunPatch 12.5・RunOverlaps 8.9・Bridges 4.8・SweepSetup 4.7・BridgeRefresh 3.4・RunFront 3.3・SetupCandidates 2.8・Bepu 2.2・RenderBridges 1.3・Snap 1.2・SnapFind 1.0・Control 0.8・RunSeparate 0.7・DriveFighter 0.6・ApplyControl 0.6・RenderParts 0.4
- 種 1 の 50.40 s: **51.6**（破片 0、GC0 0）: Weapons 47.9・SweepRun 40.2・RunPatch 14.5・RunScan 10.4・RunOverlaps 9.3・SweepSetup 4.7・SetupCandidates 3.0・Bepu 2.2・Bridges 1.8・BridgeRefresh 1.6・RenderBridges 1.5・RunFront 1.2・Snap 0.9・Control 0.9・RunHold 0.8・SnapFind 0.8・DriveFighter 0.6・ApplyControl 0.6・RunCut 0.5・FighterSplit 0.4・RenderParts 0.3

## perf royale（2026-11-26 00:21、DESKTOP-2403GU4）

種 1 × 最大 60 秒、3600 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 135.8 s。物理 102・判断 73。
チェックサム（計測あり）: 2ba61773c7da2c74 / 計測なし: 2ba61773c7da2c74（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 34.03 | 32.50 | 56.20 | 128.60 | 232.25 | 89.5% | 193 / 2 |
| 描画の詰め（CPU） | 3.25 | 2.93 | 5.30 | 10.96 | 256.40 | | |
| 　うちボクセル（FillVoxels） | 2.99 | 2.68 | 5.04 | 10.65 | 189.57 | | |
| 飛んでいる破片の数（描画の粒） | 309 | 287 | 774 | 873 | 919 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 2.722 | 8.0% | 5.08 | 13.27 |
| 　Perception | 0.357 | 1.0% | 0.50 | 6.37 |
| 　BotRead | 0.312 | 0.9% | 1.06 | 6.94 |
| 　BotPerceive | 0.074 | 0.2% | 0.21 | 1.65 |
| 　BotRead2 | 0.033 | 0.1% | 0.05 | 2.04 |
| 　BotChoose | 0.013 | 0.0% | 0.08 | 2.69 |
| 　BotExecute | 0.025 | 0.1% | 0.16 | 5.23 |
| 　DriveFighter | 1.816 | 5.3% | 3.10 | 11.13 |
| 　ApplyControl | 1.756 | 5.2% | 2.85 | 11.10 |
| 　FighterStamina | 0.288 | 0.8% | 0.52 | 5.34 |
| 　FighterStanding | 0.782 | 2.3% | 1.67 | 9.77 |
| 　FighterDown | 0.441 | 1.3% | 0.20 | 3.93 |
| 　ControlTail | 0.117 | 0.3% | 0.04 | 5.30 |
| PreBepu | 0.078 | 0.2% | 0.14 | 2.40 |
| 　Remember | 0.037 | 0.1% | 0.04 | 0.92 |
| Bepu | 4.224 | 12.4% | 6.03 | 14.15 |
| CanvasImpacts | 0.042 | 0.1% | 0.04 | 0.42 |
| FistHits | 0.029 | 0.1% | 0.03 | 0.43 |
| Weapons | 26.077 | 76.6% | 147.30 | 220.45 |
| 　MayTouch | 0.897 | 2.6% | 2.37 | 7.80 |
| 　SweepSetup | 8.137 | 23.9% | 39.87 | 59.05 |
| 　SetupRelative | 0.218 | 0.6% | 0.44 | 5.51 |
| 　SetupFar | 0.239 | 0.7% | 0.70 | 5.25 |
| 　SetupCandidates | 5.536 | 16.3% | 27.36 | 45.06 |
| 　SweepRun | 9.021 | 26.5% | 74.90 | 132.20 |
| 　RunScan | 4.625 | 13.6% | 40.80 | 79.97 |
| 　RunFront | 1.637 | 4.8% | 10.57 | 21.48 |
| 　RunOverlaps | 0.391 | 1.1% | 4.30 | 22.42 |
| 　RunPatch | 1.595 | 4.7% | 14.23 | 46.69 |
| 　RunHold | 0.012 | 0.0% | 0.04 | 0.66 |
| 　RunCrush | 0.242 | 0.7% | 0.85 | 6.59 |
| 　RunCut | 0.009 | 0.0% | 0.03 | 0.70 |
| 　RunSeparate | 0.301 | 0.9% | 2.91 | 11.11 |
| 　RunMass | 0.016 | 0.0% | 0.03 | 1.38 |
| 　Snap | 1.637 | 4.8% | 9.48 | 21.77 |
| 　SnapStiffness | 0.266 | 0.8% | 1.41 | 8.13 |
| 　SnapFind | 1.362 | 4.0% | 7.94 | 20.24 |
| 　SnapSplit | 0.000 | 0.0% | 0.00 | 0.32 |
| 　Bend | 0.123 | 0.4% | 0.27 | 4.08 |
| 　Cut | 0.142 | 0.4% | 1.43 | 18.23 |
| 　Debris | 0.010 | 0.0% | 0.15 | 4.34 |
| 　WeaponClash | 3.883 | 11.4% | 14.54 | 36.79 |
| 　ClashAfter | 0.008 | 0.0% | 0.02 | 0.24 |
| 　ClashPush | 1.007 | 3.0% | 3.09 | 12.01 |
| 　Bridges | 5.213 | 15.3% | 23.96 | 42.25 |
| 　BridgeRefresh | 3.631 | 10.7% | 10.34 | 23.97 |
| Disconnect | 0.012 | 0.0% | 0.01 | 1.00 |
| Kicks | 0.506 | 1.5% | 0.00 | 7.88 |
| Elbows | 0.003 | 0.0% | 0.00 | 0.01 |
| FighterSplit | 0.159 | 0.5% | 0.52 | 5.80 |
| After | 0.027 | 0.1% | 0.03 | 5.95 |
| PostStep | 0.136 | 0.4% | 0.61 | 6.05 |
| 　PostDebris | 0.104 | 0.3% | 0.60 | 6.03 |
| 　PostWounds | 0.002 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.003 | 0.0% | 0.00 | 0.02 |
| 　PostRound | 0.026 | 0.1% | 0.01 | 3.27 |
| 　RenderParts | 0.521 | 1.5% | 0.81 | 66.11 |
| 　RenderFragments | 0.004 | 0.0% | 0.00 | 0.04 |
| 　RenderBridges | 2.399 | 7.1% | 7.05 | 90.92 |
| （枠の外） | 0.015 | 0.0% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 130 / 187、Candidates 102162 / 297680、ScanSamples 621 / 1511、ChunksTested 13538 / 59678、BridgeCells 3140 / 5533、FilledRebuildCells 2127 / 2709、BeamRebuildCells 5833 / 4171

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 7.60 s: **232.2**（破片 0、GC0 0）: Weapons 220.5・SweepRun 132.2・RunScan 69.1・SweepSetup 39.4・SetupCandidates 30.7・RunPatch 28.8・Bridges 23.9・WeaponClash 19.8・Snap 15.6・SnapFind 14.7・BridgeRefresh 14.4・RunOverlaps 13.1・RunFront 9.8・RenderBridges 9.1・RunCrush 6.3・Control 5.9・Bepu 5.2・RunSeparate 4.1・Bend 4.1・BotRead 4.0・ClashPush 1.5・DriveFighter 1.4・ApplyControl 1.4・MayTouch 1.3・SnapStiffness 0.9・FighterStanding 0.9・RenderParts 0.5・Cut 0.5・SetupFar 0.4・SetupRelative 0.3
- 種 1 の 7.62 s: **218.6**（破片 0、GC0 0）: Weapons 212.3・SweepRun 130.9・RunScan 74.7・SweepSetup 41.9・RunPatch 33.9・SetupCandidates 24.6・Bridges 19.8・RunFront 15.5・WeaponClash 10.7・Snap 10.3・BridgeRefresh 9.6・MayTouch 7.4・Cut 6.7・RenderParts 6.2・SnapStiffness 6.0・RunOverlaps 4.7・SnapFind 4.3・RenderBridges 3.5・Bepu 3.1・Control 2.5・ClashPush 1.5・DriveFighter 1.4・ApplyControl 1.4・FighterStanding 0.9・RunSeparate 0.7・BotRead 0.5・RunCrush 0.5・Bend 0.4・SetupFar 0.4・SetupRelative 0.3
- 種 1 の 7.58 s: **202.1**（破片 0、GC0 0）: Weapons 185.2・SweepRun 104.1・RunScan 80.0・SweepSetup 46.6・SetupCandidates 32.0・Bridges 25.1・BridgeRefresh 14.0・Snap 13.9・RunFront 13.3・SnapFind 13.2・WeaponClash 13.2・RenderBridges 9.6・Bepu 9.1・RunPatch 7.6・Control 6.6・Perception 4.5・ClashPush 1.6・DriveFighter 1.4・ApplyControl 1.4・MayTouch 1.3・RunOverlaps 1.2・RunSeparate 1.1・FighterSplit 0.9・FighterStanding 0.9・RenderParts 0.7・SnapStiffness 0.7・BotRead 0.5・Cut 0.5・RunCrush 0.4・SetupFar 0.3・SetupRelative 0.3
- 種 1 の 7.87 s: **200.7**（破片 0、GC0 0）: Weapons 192.6・SweepRun 99.5・RunScan 56.9・SweepSetup 56.4・SetupCandidates 45.1・Bridges 36.7・WeaponClash 27.3・RunPatch 19.4・RunFront 14.0・ClashPush 11.1・BridgeRefresh 8.5・Snap 7.2・SnapFind 6.5・RunSeparate 6.3・RenderBridges 5.9・Bepu 4.9・Cut 4.1・Control 2.6・RunOverlaps 1.9・DriveFighter 1.5・ApplyControl 1.5・MayTouch 1.2・FighterStanding 0.9・SnapStiffness 0.6・BotRead 0.6・SetupFar 0.5・RenderParts 0.4・SetupRelative 0.4・BotPerceive 0.3・Perception 0.3
- 種 1 の 7.33 s: **196.0**（破片 0、GC0 0）: Weapons 190.0・SweepRun 122.8・SweepSetup 48.8・RunPatch 46.7・RunScan 38.6・SetupCandidates 23.1・RunOverlaps 18.7・Bridges 18.5・RunFront 12.0・RenderBridges 8.1・WeaponClash 5.2・BridgeRefresh 4.8・Snap 4.7・MayTouch 4.2・SnapFind 4.1・Bepu 2.9・Control 2.3・ClashPush 1.4・DriveFighter 1.3・ApplyControl 1.3・RenderParts 1.1・RunSeparate 0.9・FighterStanding 0.8・RunCrush 0.7・SnapStiffness 0.6・Cut 0.6・BotRead 0.4・FighterSplit 0.4
- 種 1 の 7.85 s: **184.0**（破片 0、GC0 0）: Weapons 167.9・SweepRun 72.7・SweepSetup 55.5・RunScan 47.0・SetupCandidates 36.6・Bridges 30.5・WeaponClash 21.8・RunFront 14.8・Control 12.3・Snap 10.8・DriveFighter 10.4・ApplyControl 10.4・SnapFind 10.0・FighterStanding 9.8・RenderBridges 8.6・ClashPush 6.8・BridgeRefresh 6.1・RunSeparate 4.3・SetupRelative 4.3・RunPatch 4.3・Bepu 3.3・BotRead 1.4・RunOverlaps 1.3・MayTouch 1.2・BotRead2 1.1・SnapStiffness 0.8・Cut 0.6・RenderParts 0.5・SetupFar 0.4・RunCrush 0.4
- 種 1 の 7.67 s: **178.7**（破片 0、GC0 0）: Weapons 156.6・SweepRun 75.3・RunScan 53.0・SweepSetup 35.8・SetupCandidates 23.5・Snap 21.8・SnapFind 20.2・Bridges 18.9・WeaponClash 18.7・BridgeRefresh 14.4・Bepu 12.9・RunPatch 11.4・Control 8.7・DriveFighter 8.0・ApplyControl 7.9・FighterStanding 7.3・RenderBridges 6.5・RunFront 4.5・RunOverlaps 2.9・RunSeparate 2.4・ClashPush 1.6・SnapStiffness 1.5・MayTouch 1.2・RenderParts 0.8・RunCrush 0.5・Cut 0.4・SetupFar 0.3・SetupRelative 0.3・BotRead 0.3
- 種 1 の 7.88 s: **168.5**（破片 0、GC0 0）: Weapons 155.5・SweepRun 62.4・SweepSetup 52.5・SetupCandidates 38.2・Bridges 26.9・RunScan 22.3・WeaponClash 17.3・RunPatch 16.8・BridgeRefresh 15.7・RunFront 12.5・Snap 11.4・SnapFind 10.7・RunOverlaps 8.8・Bepu 6.5・Control 5.9・DriveFighter 5.1・ApplyControl 5.0・RenderBridges 4.9・FighterStanding 4.5・Cut 3.3・ClashPush 2.1・MayTouch 1.1・RunSeparate 1.1・SnapStiffness 0.7・SetupFar 0.4・BotRead 0.4・RenderParts 0.4・SetupRelative 0.3
- 種 1 の 7.65 s: **166.7**（破片 0、GC0 0）: Weapons 158.7・SweepRun 92.6・RunScan 65.9・SweepSetup 25.1・SetupCandidates 19.1・RunFront 17.8・Bridges 16.1・WeaponClash 13.6・Snap 13.2・SnapFind 12.5・BridgeRefresh 11.5・RunSeparate 5.7・Bepu 5.2・RenderBridges 2.9・RenderParts 2.4・RunPatch 2.3・Control 2.3・ClashPush 1.5・DriveFighter 1.4・ApplyControl 1.4・MayTouch 1.1・FighterStanding 0.8・SnapStiffness 0.7・Cut 0.6・RunCrush 0.5・BotRead 0.4・SetupFar 0.3
- 種 1 の 7.70 s: **164.4**（破片 0、GC0 0）: Weapons 147.3・SweepRun 84.2・RunScan 55.4・SweepSetup 31.7・Bridges 28.8・SetupCandidates 21.7・BridgeRefresh 19.8・RunFront 16.5・Bepu 14.1・RenderBridges 8.4・WeaponClash 6.7・RunPatch 5.3・Snap 4.6・SnapFind 3.9・RunSeparate 3.0・Control 2.4・MayTouch 1.8・ClashPush 1.7・SetupFar 1.5・DriveFighter 1.4・ApplyControl 1.4・FighterStanding 0.8・SnapStiffness 0.7・RunCrush 0.5・BotRead 0.5・Cut 0.4・RenderParts 0.4・SetupRelative 0.4・Perception 0.3

## perf royale（2026-11-26 00:26、DESKTOP-2403GU4）

種 2 × 最大 60 秒、3600 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 147.0 s。物理 102・判断 73。
チェックサム（計測あり）: 57e565a2a3426c2f / 計測なし: 57e565a2a3426c2f（一致）

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 37.91 | 37.38 | 65.12 | 79.78 | 160.08 | 89.5% | 229 / 1 |
| 描画の詰め（CPU） | 2.73 | 2.43 | 4.63 | 6.44 | 114.64 | | |
| 　うちボクセル（FillVoxels） | 2.54 | 2.27 | 4.30 | 6.08 | 81.84 | | |
| 飛んでいる破片の数（描画の粒） | 186 | 90 | 834 | 1100 | 1145 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 2.368 | 6.2% | 2.72 | 4.78 |
| 　Perception | 0.326 | 0.9% | 0.44 | 1.83 |
| 　BotRead | 0.294 | 0.8% | 0.29 | 2.12 |
| 　BotPerceive | 0.099 | 0.3% | 0.09 | 1.61 |
| 　BotRead2 | 0.008 | 0.0% | 0.01 | 1.06 |
| 　BotChoose | 0.003 | 0.0% | 0.00 | 0.08 |
| 　BotExecute | 0.004 | 0.0% | 0.00 | 0.03 |
| 　DriveFighter | 1.513 | 4.0% | 1.58 | 4.49 |
| 　ApplyControl | 1.492 | 3.9% | 1.55 | 4.48 |
| 　FighterStamina | 0.247 | 0.7% | 0.26 | 1.29 |
| 　FighterStanding | 0.584 | 1.5% | 0.54 | 4.10 |
| 　FighterDown | 0.469 | 1.2% | 0.55 | 1.06 |
| 　ControlTail | 0.099 | 0.3% | 0.16 | 1.55 |
| PreBepu | 0.073 | 0.2% | 0.07 | 2.10 |
| 　Remember | 0.037 | 0.1% | 0.03 | 1.07 |
| Bepu | 3.893 | 10.3% | 4.29 | 8.11 |
| CanvasImpacts | 0.038 | 0.1% | 0.04 | 1.39 |
| FistHits | 0.027 | 0.1% | 0.03 | 0.06 |
| Weapons | 30.928 | 81.6% | 84.24 | 150.10 |
| 　MayTouch | 0.773 | 2.0% | 0.96 | 2.46 |
| 　SweepSetup | 8.904 | 23.5% | 14.41 | 23.77 |
| 　SetupRelative | 0.269 | 0.7% | 0.34 | 1.82 |
| 　SetupFar | 0.249 | 0.7% | 0.38 | 1.17 |
| 　SetupCandidates | 5.763 | 15.2% | 9.38 | 17.51 |
| 　SweepRun | 12.378 | 32.6% | 57.21 | 123.73 |
| 　RunScan | 6.556 | 17.3% | 21.31 | 54.33 |
| 　RunFront | 2.593 | 6.8% | 6.15 | 10.32 |
| 　RunOverlaps | 0.372 | 1.0% | 8.13 | 24.79 |
| 　RunPatch | 1.810 | 4.8% | 17.98 | 54.58 |
| 　RunHold | 0.011 | 0.0% | 0.22 | 0.88 |
| 　RunCrush | 0.234 | 0.6% | 0.44 | 1.99 |
| 　RunCut | 0.007 | 0.0% | 0.17 | 0.81 |
| 　RunSeparate | 0.563 | 1.5% | 1.17 | 4.32 |
| 　RunMass | 0.010 | 0.0% | 0.07 | 0.64 |
| 　Snap | 1.844 | 4.9% | 3.42 | 7.89 |
| 　SnapStiffness | 0.266 | 0.7% | 0.50 | 1.78 |
| 　SnapFind | 1.570 | 4.1% | 2.90 | 6.34 |
| 　Bend | 0.124 | 0.3% | 0.53 | 1.82 |
| 　Cut | 0.121 | 0.3% | 0.48 | 24.10 |
| 　Debris | 0.005 | 0.0% | 0.04 | 0.20 |
| 　WeaponClash | 2.167 | 5.7% | 4.24 | 20.35 |
| 　ClashAfter | 0.004 | 0.0% | 0.01 | 0.22 |
| 　ClashPush | 0.810 | 2.1% | 0.89 | 3.33 |
| 　Bridges | 6.840 | 18.0% | 11.24 | 29.67 |
| 　BridgeRefresh | 4.625 | 12.2% | 6.18 | 14.23 |
| Disconnect | 0.008 | 0.0% | 0.01 | 1.77 |
| Kicks | 0.344 | 0.9% | 2.07 | 6.73 |
| Elbows | 0.003 | 0.0% | 0.00 | 0.02 |
| FighterSplit | 0.110 | 0.3% | 0.38 | 2.58 |
| After | 0.019 | 0.0% | 0.02 | 0.04 |
| PostStep | 0.090 | 0.2% | 0.16 | 3.33 |
| 　PostDebris | 0.057 | 0.2% | 0.11 | 1.01 |
| 　PostWounds | 0.001 | 0.0% | 0.00 | 0.01 |
| 　PostSwings | 0.003 | 0.0% | 0.00 | 0.01 |
| 　PostRound | 0.027 | 0.1% | 0.04 | 3.28 |
| 　RenderParts | 0.468 | 1.2% | 0.68 | 16.34 |
| 　RenderFragments | 0.003 | 0.0% | 0.00 | 1.11 |
| 　RenderBridges | 2.029 | 5.4% | 2.57 | 51.52 |
| （枠の外） | 0.014 | 0.0% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 123 / 204、Candidates 133268 / 225854、ScanSamples 682 / 2463、ChunksTested 20293 / 85116、BridgeCells 3149 / 6095、FilledRebuildCells 1491 / 3573、BeamRebuildCells 4080 / 18974

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 2 の 14.05 s: **160.1**（破片 1、GC0 0）: Weapons 150.1・SweepRun 123.7・RunScan 54.3・RunPatch 36.5・RunOverlaps 24.8・SweepSetup 15.3・Bridges 13.8・SetupCandidates 10.1・BridgeRefresh 6.6・WeaponClash 5.0・Kicks 3.3・Bepu 3.3・RenderBridges 3.0・Snap 3.0・SnapFind 2.5・Control 2.5・RunFront 2.5・DriveFighter 1.6・ApplyControl 1.5・MayTouch 1.0・ClashPush 0.9・RunHold 0.9・FighterDown 0.6・FighterSplit 0.6・RenderParts 0.5・RunCut 0.5・FighterStanding 0.5・SnapStiffness 0.5・SetupFar 0.4・Cut 0.4・SetupRelative 0.3・RunSeparate 0.3・Bend 0.3
- 種 2 の 13.95 s: **126.0**（破片 1、GC0 0）: Weapons 119.6・SweepRun 93.0・RunPatch 54.6・RunOverlaps 19.6・SweepSetup 12.0・RunScan 10.6・Bridges 9.6・SetupCandidates 8.4・BridgeRefresh 6.9・RunFront 4.4・WeaponClash 4.1・Snap 3.7・Bepu 3.4・RenderBridges 3.3・SnapFind 3.1・Control 2.3・DriveFighter 1.5・ApplyControl 1.5・MayTouch 0.9・ClashPush 0.8・FighterDown 0.6・SnapStiffness 0.5・FighterStanding 0.5・FighterSplit 0.4・RunHold 0.4・RunSeparate 0.4・RenderParts 0.3・RunCut 0.3
- 種 2 の 14.08 s: **120.8**（破片 1、GC0 0）: Weapons 110.4・SweepRun 80.6・RunScan 29.2・RunPatch 27.2・RunOverlaps 18.7・SweepSetup 17.7・Bridges 16.2・SetupCandidates 12.0・BridgeRefresh 8.7・Bepu 3.3・Kicks 3.3・WeaponClash 3.2・RenderBridges 2.8・RunFront 2.8・Control 2.5・Snap 2.5・SnapFind 2.1・DriveFighter 1.6・ApplyControl 1.5・MayTouch 1.0・FighterSplit 0.8・FighterDown 0.6・FighterStanding 0.5・ClashPush 0.5・SetupFar 0.4・SetupRelative 0.4・SnapStiffness 0.4・Cut 0.3・RunSeparate 0.3・Perception 0.3
- 種 2 の 14.03 s: **114.9**（破片 1、GC0 0）: Weapons 105.4・SweepRun 79.0・RunScan 37.4・RunPatch 24.4・SweepSetup 14.9・Bridges 13.2・RunOverlaps 12.2・SetupCandidates 10.1・BridgeRefresh 6.9・WeaponClash 5.3・RenderBridges 3.7・Bepu 3.2・Snap 3.2・Kicks 3.1・RunFront 2.8・SnapFind 2.7・Control 2.5・DriveFighter 1.6・ApplyControl 1.5・MayTouch 0.9・ClashPush 0.9・FighterDown 0.6・FighterStanding 0.5・SnapStiffness 0.5・FighterSplit 0.4・SetupFar 0.4・SetupRelative 0.3・RunSeparate 0.3
- 種 2 の 46.83 s: **114.6**（破片 1、GC0 0）: Weapons 101.0・SweepRun 80.0・RunPatch 36.0・RunOverlaps 16.3・RunScan 16.0・SweepSetup 12.6・SetupCandidates 8.0・RunFront 7.2・Bridges 5.5・Kicks 5.4・BridgeRefresh 3.9・Bepu 3.9・Control 3.3・Snap 3.0・RenderBridges 2.7・SnapFind 2.6・WeaponClash 2.1・Bend 1.6・DriveFighter 1.6・ApplyControl 1.6・ClashPush 1.2・MayTouch 1.0・RenderParts 1.0・RunSeparate 0.8・Cut 0.7・FighterSplit 0.7・Perception 0.7・FighterDown 0.6・FighterStanding 0.5・RunHold 0.5・SnapStiffness 0.4・RunCut 0.4・SetupFar 0.4・RunCrush 0.3・SetupRelative 0.3
- 種 2 の 55.83 s: **108.4**（破片 2、GC0 0）: Weapons 99.6・SweepRun 76.9・RunPatch 35.9・RunOverlaps 15.8・SweepSetup 11.0・RunFront 10.2・RunScan 8.6・SetupCandidates 6.9・Bepu 5.4・Snap 4.0・Bridges 4.0・SnapFind 3.4・Control 2.5・RenderBridges 2.3・BridgeRefresh 2.2・DriveFighter 1.6・ApplyControl 1.5・RunSeparate 1.0・MayTouch 1.0・Bend 0.9・RunCrush 0.9・WeaponClash 0.9・Cut 0.8・ClashPush 0.7・RenderParts 0.6・SnapStiffness 0.6・FighterDown 0.6・RunHold 0.5・FighterSplit 0.5・FighterStanding 0.5・RunCut 0.4・Perception 0.4
- 種 2 の 14.02 s: **107.5**（破片 1、GC0 0）: Weapons 97.9・SweepRun 70.1・RunScan 32.1・RunPatch 20.5・SweepSetup 15.8・Bridges 14.5・RunOverlaps 11.6・SetupCandidates 10.9・BridgeRefresh 7.5・RenderBridges 4.1・WeaponClash 3.8・Kicks 3.3・Bepu 3.2・Snap 3.1・RunFront 3.1・SnapFind 2.6・Control 2.5・DriveFighter 1.6・ApplyControl 1.5・MayTouch 1.0・ClashPush 0.9・RenderParts 0.8・FighterDown 0.6・FighterStanding 0.5・RunSeparate 0.5・SnapStiffness 0.5・SetupFar 0.4・SetupRelative 0.4
- 種 2 の 14.07 s: **106.3**（破片 1、GC0 1）: Weapons 96.4・SweepRun 67.2・RunScan 38.1・SweepSetup 17.0・Bridges 15.9・RunPatch 13.4・SetupCandidates 11.4・RunOverlaps 8.8・BridgeRefresh 7.6・WeaponClash 4.1・RenderBridges 3.6・Kicks 3.4・Bepu 3.4・Control 2.5・Snap 2.5・RunFront 2.2・SnapFind 2.1・DriveFighter 1.6・ApplyControl 1.6・MayTouch 1.0・RunCut 0.8・RunSeparate 0.7・FighterDown 0.6・RenderParts 0.5・RunHold 0.5・FighterStanding 0.5・ClashPush 0.5・SetupFar 0.4・SnapStiffness 0.4・SetupRelative 0.4
- 種 2 の 14.00 s: **98.7**（破片 1、GC0 0）: Weapons 88.3・SweepRun 61.1・RunPatch 23.3・RunScan 17.9・SweepSetup 16.1・RunOverlaps 13.0・Bridges 12.7・SetupCandidates 11.5・BridgeRefresh 7.2・WeaponClash 4.3・Kicks 3.6・Bepu 3.3・RunFront 3.1・Snap 3.1・RenderBridges 3.0・SnapFind 2.6・Control 2.5・DriveFighter 1.6・ApplyControl 1.5・MayTouch 0.9・ClashPush 0.8・FighterSplit 0.6・FighterDown 0.6・FighterStanding 0.5・SnapStiffness 0.5・RunHold 0.4・SetupFar 0.3・Cut 0.3・SetupRelative 0.3・RenderParts 0.3
- 種 2 の 46.72 s: **98.3**（破片 1、GC0 0）: Weapons 85.6・SweepRun 65.6・RunScan 24.6・RunPatch 21.0・SweepSetup 13.5・RunOverlaps 10.1・SetupCandidates 8.5・RunFront 7.2・Bridges 6.1・Kicks 5.4・BridgeRefresh 3.7・Bepu 3.6・WeaponClash 3.1・Control 3.1・Snap 2.9・SnapFind 2.5・RenderBridges 2.4・DriveFighter 1.6・ApplyControl 1.6・RunSeparate 1.1・MayTouch 1.1・ClashPush 1.0・Bend 0.7・Perception 0.7・RunCrush 0.6・FighterDown 0.6・FighterStanding 0.5・RenderParts 0.5・SnapStiffness 0.4・SetupFar 0.4・SetupRelative 0.3

