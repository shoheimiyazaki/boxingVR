### base royale ()
## perf royale（2026-11-25 22:02、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 105.0 s。物理 102・判断 73。
チェックサム（計測あり）: d56f241a235b3cd4

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 50.28 | 45.03 | 100.49 | 144.45 | 390.69 | 91.6% | 745 / 7 |
| 描画の詰め（CPU） | 6.97 | 4.99 | 17.93 | 29.75 | 401.28 | | |
| 　うちボクセル（FillVoxels） | 6.54 | 4.62 | 17.49 | 29.51 | 325.90 | | |
| 飛んでいる破片の数（描画の粒） | 331 | 330 | 851 | 900 | 919 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 4.480 | 8.9% | 13.96 | 45.96 |
| 　Perception | 0.571 | 1.1% | 2.16 | 16.23 |
| 　BotRead | 0.763 | 1.5% | 2.35 | 42.89 |
| 　BotPerceive | 0.171 | 0.3% | 0.26 | 7.16 |
| 　BotRead2 | 0.079 | 0.2% | 0.12 | 6.17 |
| 　BotChoose | 0.048 | 0.1% | 0.01 | 38.14 |
| 　BotExecute | 0.045 | 0.1% | 0.90 | 15.61 |
| 　DriveFighter | 2.672 | 5.3% | 9.10 | 30.07 |
| 　ApplyControl | 2.558 | 5.1% | 8.90 | 29.91 |
| 　FighterStamina | 0.564 | 1.1% | 2.34 | 19.85 |
| 　FighterStanding | 1.183 | 2.4% | 4.53 | 28.13 |
| 　FighterDown | 0.370 | 0.7% | 1.40 | 20.18 |
| 　ControlTail | 0.240 | 0.5% | 0.08 | 12.59 |
| PreBepu | 0.194 | 0.4% | 1.01 | 12.87 |
| 　Remember | 0.113 | 0.2% | 0.21 | 4.02 |
| Bepu | 4.384 | 8.7% | 10.46 | 42.07 |
| CanvasImpacts | 0.110 | 0.2% | 0.59 | 17.59 |
| FistHits | 0.062 | 0.1% | 0.08 | 4.00 |
| Weapons | 39.633 | 78.8% | 161.83 | 337.88 |
| 　MayTouch | 2.138 | 4.3% | 6.35 | 26.00 |
| 　SweepSetup | 10.449 | 20.8% | 31.85 | 66.04 |
| 　SetupRelative | 0.386 | 0.8% | 1.92 | 20.44 |
| 　SetupFar | 0.344 | 0.7% | 0.79 | 8.21 |
| 　SetupCandidates | 7.123 | 14.2% | 22.46 | 51.87 |
| 　SweepRun | 11.855 | 23.6% | 64.26 | 162.58 |
| 　RunScan | 6.012 | 12.0% | 27.46 | 100.76 |
| 　RunFront | 1.806 | 3.6% | 8.07 | 30.37 |
| 　RunOverlaps | 0.472 | 0.9% | 5.21 | 21.42 |
| 　RunPatch | 2.081 | 4.1% | 17.20 | 53.77 |
| 　RunHold | 0.025 | 0.1% | 0.09 | 3.88 |
| 　RunCrush | 0.422 | 0.8% | 0.43 | 6.71 |
| 　RunCut | 0.030 | 0.1% | 0.88 | 13.76 |
| 　RunSeparate | 0.402 | 0.8% | 2.34 | 10.72 |
| 　RunMass | 0.026 | 0.1% | 0.16 | 5.11 |
| 　Snap | 2.921 | 5.8% | 16.06 | 88.65 |
| 　SnapStiffness | 0.463 | 0.9% | 2.54 | 15.72 |
| 　SnapFind | 2.422 | 4.8% | 13.45 | 72.81 |
| 　SnapSplit | 0.002 | 0.0% | 0.00 | 3.21 |
| 　Bend | 0.242 | 0.5% | 2.45 | 25.29 |
| 　Cut | 0.438 | 0.9% | 2.24 | 32.34 |
| 　Debris | 0.051 | 0.1% | 0.17 | 29.15 |
| 　WeaponClash | 6.712 | 13.3% | 33.18 | 113.63 |
| 　ClashAfter | 0.040 | 0.1% | 0.10 | 15.26 |
| 　ClashPush | 1.977 | 3.9% | 6.23 | 28.96 |
| 　Bridges | 8.613 | 17.1% | 36.41 | 103.81 |
| 　BridgeRefresh | 5.442 | 10.8% | 21.86 | 69.76 |
| Disconnect | 0.037 | 0.1% | 1.09 | 14.85 |
| Kicks | 0.840 | 1.7% | 0.01 | 14.70 |
| Elbows | 0.016 | 0.0% | 0.01 | 8.91 |
| FighterSplit | 0.289 | 0.6% | 0.60 | 17.49 |
| After | 0.061 | 0.1% | 0.05 | 5.69 |
| PostStep | 0.140 | 0.3% | 0.18 | 32.06 |
| 　PostDebris | 0.074 | 0.1% | 0.09 | 2.12 |
| 　PostWounds | 0.005 | 0.0% | 0.00 | 3.72 |
| 　PostSwings | 0.006 | 0.0% | 0.07 | 1.24 |
| 　PostRound | 0.051 | 0.1% | 0.01 | 31.94 |
| 　RenderParts | 0.744 | 1.5% | 2.51 | 19.39 |
| 　RenderFragments | 0.004 | 0.0% | 0.00 | 0.21 |
| 　RenderBridges | 5.628 | 11.2% | 7.61 | 282.40 |
| （枠の外） | 0.037 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 137 / 137、Candidates 106391 / 177775、ScanSamples 699 / 1253、ChunksTested 14581 / 40701、BridgeCells 4149 / 6369、FilledRebuildCells 2731 / 24536、BeamRebuildCells 7013 / 50992

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 5.18 s: **390.7**（破片 0、GC0 1）: Weapons 337.9・SweepRun 152.0・Bridges 103.8・RunScan 100.8・WeaponClash 82.5・BridgeRefresh 69.8・RunPatch 42.5・Control 35.2・ClashPush 29.0・SweepSetup 28.8・Snap 27.7・SnapFind 26.2・SetupCandidates 24.7・DriveFighter 18.1・ApplyControl 16.2・MayTouch 16.2・FighterStanding 15.4・Bepu 13.6・BotRead 13.5・Cut 10.5・RenderBridges 7.9・RunFront 4.5・Perception 3.2・CanvasImpacts 2.6・RunOverlaps 2.3・SnapStiffness 1.4・PreBepu 0.6・RunSeparate 0.6・BotPerceive 0.6・Remember 0.6・Bend 0.5・RenderParts 0.5・FighterStamina 0.4・Disconnect 0.4・SetupRelative 0.4・SetupFar 0.3
- 種 1 の 29.60 s: **322.4**（破片 0、GC0 1）: Weapons 277.6・SweepRun 162.6・RunPatch 53.8・Bridges 41.4・Snap 26.4・RunFront 26.0・WeaponClash 25.2・SweepSetup 22.5・RunScan 20.0・RunOverlaps 19.0・ClashPush 18.6・Control 18.0・DriveFighter 16.4・ApplyControl 16.3・Disconnect 14.9・SnapStiffness 14.5・FighterStamina 14.4・BridgeRefresh 14.3・RunCut 13.8・SnapFind 11.8・SetupCandidates 11.7・RenderParts 11.2・RunSeparate 10.7・Bepu 10.7・RenderBridges 10.3・MayTouch 1.9・Cut 1.4・FighterStanding 1.1・Perception 0.8・FighterSplit 0.6・BotRead 0.5・FighterDown 0.4・SetupRelative 0.3・RunHold 0.3・SetupFar 0.3
- 種 1 の 4.95 s: **196.9**（破片 0、GC0 1）: Weapons 177.4・WeaponClash 113.6・Snap 88.6・SnapFind 72.8・SweepRun 50.9・RunScan 25.0・SweepSetup 18.0・SnapStiffness 15.7・RunPatch 14.2・SetupCandidates 13.1・Bridges 12.3・Bepu 9.2・Control 5.8・BridgeRefresh 5.3・RunOverlaps 5.1・MayTouch 5.0・DriveFighter 4.2・ApplyControl 4.2・RunFront 3.7・FighterStanding 3.5・Disconnect 3.1・ClashPush 3.0・RenderBridges 2.9・Perception 1.0・Cut 0.7・FistHits 0.7・SetupRelative 0.5・RunSeparate 0.5・BotRead 0.4・FighterStamina 0.4・RunMass 0.4・RenderParts 0.3
- 種 1 の 29.48 s: **185.7**（破片 0、GC0 0）: Weapons 156.1・SweepSetup 53.1・SweepRun 37.8・SetupCandidates 33.0・WeaponClash 28.9・MayTouch 26.0・RenderBridges 23.6・RunScan 18.5・SetupRelative 16.7・Control 15.7・ClashPush 15.1・Bridges 14.7・RunPatch 14.2・DriveFighter 12.9・ApplyControl 12.9・FighterDown 9.9・Snap 7.1・SnapFind 7.0・CanvasImpacts 6.8・Bepu 5.6・BridgeRefresh 4.8・RenderParts 2.7・RunFront 2.6・RunOverlaps 1.4・FighterStanding 1.0・FighterSplit 0.9・BotRead 0.8・Perception 0.6・FighterStamina 0.5・SetupFar 0.3
- 種 1 の 29.82 s: **185.5**（破片 0、GC0 1）: Weapons 130.0・Bridges 40.7・SweepRun 38.9・Control 27.8・SweepSetup 27.2・Bepu 26.8・RenderBridges 26.7・DriveFighter 26.3・ApplyControl 26.2・SetupCandidates 25.2・BridgeRefresh 23.7・MayTouch 17.0・RunPatch 13.4・FighterStanding 12.9・FighterDown 12.6・RunScan 12.2・RunSeparate 10.5・Cut 9.1・RenderParts 3.4・WeaponClash 1.4・RunFront 1.1・RunOverlaps 1.0・Snap 0.9・ClashPush 0.9・Perception 0.8・SnapFind 0.8・BotRead 0.4・FighterStamina 0.4・FighterSplit 0.3
- 種 1 の 8.28 s: **180.8**（破片 0、GC0 0）: Weapons 148.5・SweepRun 63.4・RunScan 56.5・WeaponClash 38.6・Bridges 34.8・SweepSetup 32.7・SetupCandidates 27.6・Control 27.3・DriveFighter 23.9・ApplyControl 23.7・FighterStamina 19.8・Snap 17.3・BridgeRefresh 17.1・SnapFind 16.4・Bepu 3.7・ClashPush 3.4・RenderBridges 3.1・MayTouch 2.8・RunFront 2.6・RunPatch 1.8・BotRead 1.8・FighterStanding 1.5・Perception 1.2・BotRead2 1.1・RunSeparate 1.0・PreBepu 0.9・Remember 0.8・SnapStiffness 0.8・Cut 0.6・RenderParts 0.5・SetupRelative 0.5・SetupFar 0.4・RunCrush 0.3・RunOverlaps 0.3
- 種 1 の 29.58 s: **180.5**（破片 0、GC0 1）: Weapons 166.2・SweepRun 62.5・SweepSetup 40.8・Bridges 37.8・BridgeRefresh 29.4・SetupCandidates 28.5・RunFront 23.9・WeaponClash 23.5・RunScan 21.7・MayTouch 16.1・Bepu 10.5・RenderParts 8.3・SetupFar 8.2・RunSeparate 6.2・Snap 3.5・RenderBridges 3.4・SnapFind 3.0・Control 2.9・RunPatch 2.1・DriveFighter 1.9・ApplyControl 1.8・ClashPush 1.2・FighterStanding 0.8・RunOverlaps 0.5・Perception 0.5・SnapStiffness 0.5・FighterSplit 0.4・BotRead 0.4・FighterStamina 0.3・FighterDown 0.3・SetupRelative 0.3
- 種 1 の 29.57 s: **172.2**（破片 0、GC0 0）: Weapons 141.5・SweepRun 47.6・Bridges 44.9・BridgeRefresh 42.8・Control 22.6・WeaponClash 21.1・RunFront 20.9・SweepSetup 19.8・Perception 16.2・RunPatch 15.7・SetupRelative 10.2・ClashPush 8.7・Snap 7.9・SnapFind 7.1・SetupCandidates 6.2・Bepu 6.1・DriveFighter 5.8・ApplyControl 5.6・RenderBridges 4.4・FighterStanding 4.4・RunScan 3.6・MayTouch 3.1・RunSeparate 2.8・RunOverlaps 2.3・PreBepu 1.0・Remember 0.9・SnapStiffness 0.7・RenderParts 0.7・FighterSplit 0.6・FighterStamina 0.4・BotRead 0.4・RunCrush 0.4・FighterDown 0.4・Cut 0.4・SetupFar 0.3
- 種 1 の 4.50 s: **171.4**（破片 0、GC0 0）: Weapons 142.3・SweepRun 77.5・Bridges 48.1・RunPatch 31.8・BridgeRefresh 28.4・RunOverlaps 21.4・Bepu 20.4・SweepSetup 15.7・RunScan 14.4・WeaponClash 11.7・SetupCandidates 10.4・Snap 7.3・SnapFind 6.0・RunFront 4.8・FighterSplit 4.3・RenderBridges 4.2・Control 3.8・Cut 2.2・DriveFighter 2.0・ApplyControl 1.9・MayTouch 1.9・ClashPush 1.6・Debris 1.2・SnapStiffness 1.2・FighterStanding 1.2・Perception 1.0・RunMass 0.8・RunSeparate 0.8・RunCut 0.7・SetupFar 0.5・RenderParts 0.5・BotRead 0.5・FighterStamina 0.4・RunCrush 0.4・SetupRelative 0.4・Bend 0.4
- 種 1 の 4.47 s: **168.5**（破片 0、GC0 0）: Weapons 159.0・Bridges 54.6・SweepRun 53.0・BridgeRefresh 37.8・Bend 25.3・SweepSetup 22.6・RunScan 20.7・SetupCandidates 17.0・WeaponClash 16.0・RunPatch 13.2・Snap 9.2・SnapFind 8.0・RunOverlaps 6.5・RunFront 6.0・Control 4.2・RenderBridges 3.8・RunSeparate 3.1・Bepu 2.3・DriveFighter 2.1・ApplyControl 2.0・MayTouch 1.9・ClashPush 1.8・Cut 1.6・PostStep 1.3・FighterStanding 1.3・PostSwings 1.2・SnapStiffness 1.2・Perception 1.2・FighterSplit 1.1・RunMass 0.7・RenderParts 0.7・BotRead 0.6・Debris 0.6・SetupRelative 0.6・RunCrush 0.6・SetupFar 0.5・FighterStamina 0.4・RunCut 0.4・Disconnect 0.4

### base katana ()
## perf katana（2026-11-25 22:02、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 26.9 s。物理 102・判断 73。
チェックサム（計測あり）: 74207d56c9b1922

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 12.47 | 8.41 | 37.21 | 70.44 | 195.63 | 17.8% | 151 / 3 |
| 描画の詰め（CPU） | 2.31 | 0.88 | 9.12 | 20.87 | 220.28 | | |
| 　うちボクセル（FillVoxels） | 2.19 | 0.82 | 8.97 | 20.69 | 144.19 | | |
| 飛んでいる破片の数（描画の粒） | 19 | 1 | 86 | 141 | 156 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 1.765 | 14.2% | 6.64 | 37.91 |
| 　Perception | 0.222 | 1.8% | 0.50 | 5.71 |
| 　BotRead | 0.047 | 0.4% | 0.08 | 6.77 |
| 　DriveFighter | 1.425 | 11.4% | 5.78 | 37.58 |
| 　ApplyControl | 1.309 | 10.5% | 3.71 | 20.82 |
| 　FighterStamina | 0.236 | 1.9% | 0.67 | 9.89 |
| 　FighterStanding | 0.490 | 3.9% | 1.28 | 9.66 |
| 　FighterDown | 0.383 | 3.1% | 0.75 | 12.18 |
| 　ControlTail | 0.046 | 0.4% | 0.26 | 3.67 |
| PreBepu | 0.068 | 0.5% | 0.10 | 2.92 |
| 　Remember | 0.034 | 0.3% | 0.05 | 1.84 |
| Bepu | 2.051 | 16.4% | 3.81 | 40.91 |
| CanvasImpacts | 0.075 | 0.6% | 3.05 | 27.49 |
| FistHits | 0.010 | 0.1% | 0.03 | 1.40 |
| Weapons | 8.319 | 66.7% | 74.05 | 188.05 |
| 　MayTouch | 0.174 | 1.4% | 0.49 | 3.53 |
| 　SweepSetup | 1.785 | 14.3% | 15.02 | 52.42 |
| 　SetupRelative | 0.072 | 0.6% | 0.17 | 2.87 |
| 　SetupFar | 0.067 | 0.5% | 0.34 | 3.06 |
| 　SetupCandidates | 1.218 | 9.8% | 10.97 | 41.09 |
| 　SweepRun | 3.211 | 25.8% | 28.63 | 66.32 |
| 　RunScan | 2.019 | 16.2% | 16.90 | 43.58 |
| 　RunFront | 0.449 | 3.6% | 2.16 | 12.13 |
| 　RunOverlaps | 0.046 | 0.4% | 1.32 | 9.87 |
| 　RunPatch | 0.340 | 2.7% | 4.96 | 26.09 |
| 　RunHold | 0.008 | 0.1% | 0.36 | 5.41 |
| 　RunCrush | 0.116 | 0.9% | 0.22 | 5.36 |
| 　RunCut | 0.003 | 0.0% | 0.13 | 1.33 |
| 　RunSeparate | 0.082 | 0.7% | 0.71 | 8.33 |
| 　RunMass | 0.003 | 0.0% | 0.04 | 0.54 |
| 　Snap | 0.687 | 5.5% | 2.67 | 14.37 |
| 　SnapStiffness | 0.132 | 1.1% | 0.67 | 7.00 |
| 　SnapFind | 0.546 | 4.4% | 1.72 | 14.15 |
| 　Bend | 0.083 | 0.7% | 0.16 | 39.49 |
| 　Cut | 0.115 | 0.9% | 7.17 | 125.69 |
| 　Debris | 0.009 | 0.1% | 0.44 | 7.41 |
| 　WeaponClash | 0.809 | 6.5% | 17.35 | 63.39 |
| 　ClashAfter | 0.009 | 0.1% | 0.43 | 7.47 |
| 　ClashPush | 0.120 | 1.0% | 2.47 | 42.96 |
| 　Bridges | 1.763 | 14.1% | 17.18 | 36.88 |
| 　BridgeRefresh | 1.375 | 11.0% | 15.63 | 36.58 |
| Disconnect | 0.007 | 0.1% | 0.03 | 2.79 |
| Kicks | 0.007 | 0.1% | 0.01 | 1.10 |
| Elbows | 0.004 | 0.0% | 0.01 | 0.66 |
| FighterSplit | 0.088 | 0.7% | 0.60 | 7.58 |
| After | 0.027 | 0.2% | 0.05 | 2.25 |
| PostStep | 0.019 | 0.2% | 0.08 | 4.51 |
| 　PostDebris | 0.009 | 0.1% | 0.02 | 4.50 |
| 　PostWounds | 0.001 | 0.0% | 0.00 | 0.51 |
| 　PostSwings | 0.006 | 0.0% | 0.01 | 3.01 |
| 　PostRound | 0.002 | 0.0% | 0.01 | 0.57 |
| 　RenderParts | 0.218 | 1.8% | 0.39 | 20.43 |
| 　RenderFragments | 0.002 | 0.0% | 0.00 | 0.38 |
| 　RenderBridges | 1.921 | 15.4% | 5.85 | 114.88 |
| （枠の外） | 0.026 | 0.2% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 21 / 55、Candidates 17507 / 45398、ScanSamples 254 / 1315、ChunksTested 4490 / 24303、BridgeCells 336 / 1686、FilledRebuildCells 447 / 8890、BeamRebuildCells 961 / 18260

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 2.10 s: **195.6**（破片 1、GC0 0）: Weapons 188.0・Cut 125.7・SweepRun 28.6・Bridges 24.6・BridgeRefresh 19.9・RunScan 16.1・RenderBridges 12.8・SweepSetup 10.0・SetupCandidates 7.5・RunPatch 5.9・Control 4.8・DriveFighter 4.2・WeaponClash 3.7・ApplyControl 3.4・RunFront 2.3・RunOverlaps 1.8・Bepu 1.7・FighterStanding 1.5・Snap 1.4・SnapFind 1.0・FighterSplit 0.8・Bend 0.6・RunSeparate 0.5・SetupFar 0.5・FighterStamina 0.4・SnapStiffness 0.4・RenderParts 0.4・BotRead 0.4
- 種 1 の 3.17 s: **120.0**（破片 1、GC0 1）: Weapons 80.5・WeaponClash 46.6・ClashPush 43.0・Control 37.9・DriveFighter 37.6・SweepRun 24.1・RunPatch 9.3・Bridges 8.3・BridgeRefresh 8.0・RenderBridges 7.4・RunScan 6.4・RunOverlaps 4.1・SweepSetup 3.5・SetupCandidates 2.5・ApplyControl 2.0・FighterStanding 1.6・Bepu 1.3・RunCut 0.5・RunFront 0.5・RunHold 0.4・Snap 0.4
- 種 1 の 1.30 s: **116.3**（破片 0、GC0 0）: Weapons 103.2・WeaponClash 63.4・SweepSetup 52.4・SetupCandidates 41.1・Bridges 33.8・BridgeRefresh 33.4・SweepRun 13.9・RunScan 13.9・Control 10.7・DriveFighter 10.2・ApplyControl 10.1・RenderBridges 9.8・FighterStanding 9.7・Bepu 1.9・MayTouch 0.3・Perception 0.3
- 種 1 の 1.72 s: **90.7**（破片 0、GC0 0）: Weapons 85.9・SweepRun 54.5・WeaponClash 39.2・RunScan 38.2・Bridges 19.1・BridgeRefresh 18.8・RunPatch 8.9・RenderBridges 8.4・SweepSetup 8.1・SetupCandidates 6.5・RunFront 3.3・RunOverlaps 2.7・Bepu 2.5・Control 1.7・DriveFighter 1.3・Snap 1.3・ApplyControl 1.3・Cut 1.1・SnapFind 0.9・FighterStanding 0.9・Bend 0.7・SnapStiffness 0.3
- 種 1 の 1.37 s: **84.8**（破片 0、GC0 0）: Weapons 39.1・CanvasImpacts 27.5・WeaponClash 24.0・SweepSetup 18.6・SetupCandidates 17.6・Control 16.2・DriveFighter 15.8・ApplyControl 15.7・Bridges 11.5・BridgeRefresh 11.2・SweepRun 7.9・RunScan 6.9・RenderBridges 4.4・Bepu 1.7・FighterStanding 1.5・RunFront 0.6・Snap 0.4・SnapFind 0.3
- 種 1 の 2.63 s: **83.6**（破片 1、GC0 0）: Weapons 79.9・SweepRun 44.6・RunScan 25.5・Bridges 21.6・BridgeRefresh 20.7・SweepSetup 10.6・SetupCandidates 8.8・RenderBridges 4.6・RunPatch 2.4・Snap 2.4・RunFront 2.2・Bepu 2.0・SnapFind 1.4・Control 1.3・DriveFighter 1.0・SnapStiffness 1.0・ApplyControl 1.0・RenderParts 0.7・RunOverlaps 0.6・FighterStanding 0.6・RunCrush 0.4・SetupFar 0.3
- 種 1 の 3.15 s: **80.5**（破片 1、GC0 1）: Weapons 78.0・SweepRun 66.3・RunPatch 26.1・RunScan 12.7・RunOverlaps 9.9・Bridges 6.6・RenderBridges 6.4・BridgeRefresh 6.4・RunHold 5.4・WeaponClash 4.6・SweepSetup 3.5・SetupCandidates 2.4・RunCut 1.3・Bepu 1.2・Control 1.0・DriveFighter 0.7・ApplyControl 0.7・RunFront 0.5・Snap 0.5・FighterStanding 0.4・SetupFar 0.3・Cut 0.3・SnapFind 0.3・RenderParts 0.3
- 種 1 の 5.45 s: **78.7**（破片 1、GC0 0）: Weapons 59.3・SweepRun 38.7・RunScan 37.3・Control 17.5・DriveFighter 13.6・ApplyControl 13.4・FighterDown 12.1・Snap 9.2・SweepSetup 8.7・SetupCandidates 7.1・WeaponClash 5.9・SnapStiffness 3.7・ControlTail 3.7・Bridges 2.6・Bepu 1.7・RenderBridges 1.2・SnapFind 1.0・BridgeRefresh 1.0・FighterStamina 0.8・RunFront 0.5・ClashPush 0.4
- 種 1 の 2.25 s: **77.2**（破片 1、GC0 0）: Weapons 73.4・SweepRun 32.4・Bridges 17.6・SweepSetup 16.8・BridgeRefresh 15.8・WeaponClash 12.4・RunPatch 11.3・RunScan 8.9・RunSeparate 8.3・RenderBridges 6.1・SetupCandidates 5.0・MayTouch 3.5・RunFront 2.7・Snap 2.4・SnapFind 2.0・Bepu 1.8・Control 1.5・DriveFighter 1.2・ApplyControl 1.2・FighterStanding 0.7・SnapStiffness 0.4・FighterStamina 0.3
- 種 1 の 16.77 s: **77.0**（破片 1、GC0 0）: Weapons 59.0・SweepRun 20.8・RunScan 15.8・SweepSetup 15.6・Bridges 15.4・Snap 14.4・SnapFind 14.2・Control 10.4・SetupCandidates 8.2・Bepu 7.4・BridgeRefresh 6.6・DriveFighter 6.3・ApplyControl 6.3・FighterStamina 4.5・Perception 4.0・RunFront 4.0・FighterDown 1.4・RenderBridges 0.5・RunPatch 0.4

### c1 royale (MINIRING_SCAN=16 MINIRING_PASSES=2 MINIRING_SAMPLE=1)
## perf royale（2026-11-25 22:05、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 127.0 s。物理 102・判断 73。
チェックサム（計測あり）: 3fa98cd51b0245f7

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 60.70 | 42.32 | 186.46 | 413.82 | 645.26 | 88.0% | 689 / 7 |
| 描画の詰め（CPU） | 8.87 | 5.11 | 26.17 | 64.29 | 422.59 | | |
| 　うちボクセル（FillVoxels） | 8.28 | 4.76 | 25.08 | 52.76 | 374.46 | | |
| 飛んでいる破片の数（描画の粒） | 421 | 334 | 1145 | 1198 | 1241 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 5.992 | 9.9% | 50.31 | 111.71 |
| 　Perception | 0.877 | 1.4% | 7.77 | 40.65 |
| 　BotRead | 1.058 | 1.7% | 11.15 | 63.23 |
| 　BotPerceive | 0.243 | 0.4% | 5.57 | 51.79 |
| 　BotRead2 | 0.077 | 0.1% | 0.28 | 11.56 |
| 　BotChoose | 0.061 | 0.1% | 0.07 | 34.51 |
| 　BotExecute | 0.096 | 0.2% | 2.34 | 41.57 |
| 　DriveFighter | 3.559 | 5.9% | 28.91 | 73.59 |
| 　ApplyControl | 3.462 | 5.7% | 28.84 | 73.54 |
| 　FighterStamina | 0.743 | 1.2% | 6.55 | 35.58 |
| 　FighterStanding | 1.561 | 2.6% | 8.11 | 49.89 |
| 　FighterDown | 0.511 | 0.8% | 4.79 | 37.57 |
| 　ControlTail | 0.256 | 0.4% | 2.27 | 26.08 |
| PreBepu | 0.313 | 0.5% | 0.18 | 50.50 |
| 　Remember | 0.214 | 0.4% | 0.12 | 46.43 |
| Bepu | 4.992 | 8.2% | 38.05 | 119.01 |
| CanvasImpacts | 0.137 | 0.2% | 2.85 | 32.94 |
| FistHits | 0.064 | 0.1% | 0.07 | 13.40 |
| Weapons | 48.070 | 79.2% | 405.98 | 574.09 |
| 　MayTouch | 2.646 | 4.4% | 23.66 | 90.82 |
| 　SweepSetup | 15.363 | 25.3% | 106.49 | 256.80 |
| 　SetupRelative | 0.475 | 0.8% | 3.45 | 49.24 |
| 　SetupFar | 0.426 | 0.7% | 2.51 | 49.79 |
| 　SetupCandidates | 9.737 | 16.0% | 63.95 | 213.18 |
| 　SweepRun | 12.510 | 20.6% | 126.70 | 268.20 |
| 　RunScan | 4.855 | 8.0% | 45.16 | 116.43 |
| 　RunFront | 3.444 | 5.7% | 32.95 | 105.72 |
| 　RunOverlaps | 0.366 | 0.6% | 8.20 | 95.12 |
| 　RunPatch | 2.357 | 3.9% | 29.78 | 106.67 |
| 　RunHold | 0.032 | 0.1% | 0.02 | 20.91 |
| 　RunCrush | 0.299 | 0.5% | 3.22 | 46.47 |
| 　RunCut | 0.025 | 0.0% | 0.69 | 11.99 |
| 　RunSeparate | 0.557 | 0.9% | 1.43 | 39.44 |
| 　RunMass | 0.053 | 0.1% | 0.04 | 24.94 |
| 　Snap | 2.591 | 4.3% | 11.29 | 59.21 |
| 　SnapStiffness | 0.409 | 0.7% | 0.74 | 22.19 |
| 　SnapFind | 2.135 | 3.5% | 10.52 | 59.00 |
| 　SnapSplit | 0.007 | 0.0% | 0.00 | 7.17 |
| 　Bend | 0.130 | 0.2% | 0.09 | 18.36 |
| 　Cut | 0.551 | 0.9% | 5.11 | 40.57 |
| 　Debris | 0.034 | 0.1% | 0.02 | 7.27 |
| 　WeaponClash | 8.652 | 14.3% | 72.44 | 144.21 |
| 　ClashAfter | 0.043 | 0.1% | 0.02 | 13.29 |
| 　ClashPush | 2.204 | 3.6% | 32.21 | 95.83 |
| 　Bridges | 10.754 | 17.7% | 98.77 | 196.20 |
| 　BridgeRefresh | 6.517 | 10.7% | 55.30 | 193.21 |
| Disconnect | 0.033 | 0.1% | 0.01 | 6.59 |
| Kicks | 0.469 | 0.8% | 0.81 | 55.07 |
| Elbows | 0.010 | 0.0% | 0.01 | 2.49 |
| FighterSplit | 0.373 | 0.6% | 0.91 | 42.94 |
| After | 0.059 | 0.1% | 0.10 | 3.50 |
| PostStep | 0.159 | 0.3% | 0.55 | 15.85 |
| 　PostDebris | 0.107 | 0.2% | 0.51 | 12.29 |
| 　PostWounds | 0.003 | 0.0% | 0.00 | 0.41 |
| 　PostSwings | 0.007 | 0.0% | 0.00 | 1.36 |
| 　PostRound | 0.040 | 0.1% | 0.03 | 15.84 |
| 　RenderParts | 0.988 | 1.6% | 6.47 | 69.58 |
| 　RenderFragments | 0.005 | 0.0% | 0.01 | 0.37 |
| 　RenderBridges | 7.017 | 11.6% | 27.94 | 329.51 |
| （枠の外） | 0.027 | 0.0% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 103 / 130、Candidates 179527 / 217455、ScanSamples 583 / 817、ChunksTested 7164 / 10379、BridgeCells 2534 / 4708、FilledRebuildCells 1754 / 1003、BeamRebuildCells 5058 / 2700

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 16.72 s: **645.3**（破片 0、GC0 1）: Weapons 574.1・SweepRun 268.2・RunOverlaps 95.1・RunPatch 79.2・Bridges 76.9・MayTouch 76.5・Control 56.7・RunFront 51.1・SweepSetup 50.0・DriveFighter 45.3・ApplyControl 45.2・WeaponClash 37.9・BridgeRefresh 37.9・Snap 32.4・SetupCandidates 32.1・SnapFind 31.5・FighterStamina 23.8・RunScan 21.5・RunSeparate 17.6・Bepu 9.7・Perception 9.4・RenderBridges 6.9・FighterSplit 4.2・RunCrush 2.2・BotRead 1.7・ClashPush 1.6・FighterStanding 1.3・SnapStiffness 0.7・BotChoose 0.7・RenderParts 0.6・SetupFar 0.5・Cut 0.4・SetupRelative 0.4・FighterDown 0.3
- 種 1 の 16.23 s: **617.9**（破片 0、GC0 0）: Weapons 440.4・Bridges 196.2・BridgeRefresh 193.2・Control 111.7・WeaponClash 101.2・MayTouch 67.2・Bepu 64.8・BotRead 48.0・BotPerceive 47.6・SweepRun 41.6・RenderBridges 40.9・Snap 36.4・SnapFind 36.1・RunScan 34.5・Perception 33.7・SweepSetup 32.4・DriveFighter 29.9・ApplyControl 29.8・SetupCandidates 29.2・RunPatch 2.9・RunFront 2.2・RunCrush 1.1・ClashPush 0.9・FighterStanding 0.9・RunSeparate 0.6・RenderParts 0.4・FighterStamina 0.4・FighterSplit 0.4
- 種 1 の 17.92 s: **609.6**（破片 0、GC0 0）: Weapons 465.1・SweepSetup 256.8・SetupCandidates 213.2・Bepu 119.0・SweepRun 81.4・Bridges 69.0・RunFront 67.2・BridgeRefresh 62.5・Control 24.5・DriveFighter 20.5・ApplyControl 20.4・FighterDown 18.1・RenderBridges 17.1・RunScan 12.8・MayTouch 7.3・WeaponClash 5.4・BotRead 3.3・ClashPush 1.6・Snap 1.4・SnapFind 1.0・FighterStanding 0.9・FighterStamina 0.7・RunPatch 0.5・Perception 0.5
- 種 1 の 16.03 s: **589.3**（破片 0、GC0 1）: Weapons 520.0・SweepRun 167.0・SweepSetup 141.0・WeaponClash 122.0・RunScan 116.4・Bridges 106.1・BridgeRefresh 60.4・SetupCandidates 57.7・RenderBridges 53.8・RunCrush 46.5・Bepu 46.0・Cut 38.7・Snap 30.9・SnapFind 30.6・MayTouch 27.1・Control 22.5・DriveFighter 20.8・ApplyControl 20.8・FighterStanding 19.3・ClashPush 3.6・RunFront 2.5・RunPatch 1.0・Perception 0.7・RenderParts 0.6・BotRead 0.6・FighterStamina 0.4・SetupRelative 0.3・SetupFar 0.3
- 種 1 の 22.38 s: **571.6**（破片 0、GC0 0）: Weapons 523.7・SweepSetup 245.6・SweepRun 193.2・SetupCandidates 139.9・Bridges 125.3・RunScan 94.7・WeaponClash 68.5・RunFront 50.5・RunPatch 44.8・Control 41.4・ClashPush 39.6・MayTouch 30.7・BotRead 26.0・BotPerceive 25.7・DriveFighter 14.5・ApplyControl 14.4・FighterStamina 6.3・RenderBridges 5.8・Bepu 5.1・Snap 5.1・BridgeRefresh 4.5・SnapFind 4.4・RunSeparate 1.8・FighterStanding 0.9・RunCrush 0.8・Kicks 0.8・SnapStiffness 0.7・SetupFar 0.6・SetupRelative 0.5・Perception 0.5・FighterDown 0.5・RenderParts 0.5・ControlTail 0.3
- 種 1 の 16.58 s: **555.6**（破片 0、GC0 0）: Weapons 452.8・SweepSetup 143.7・Bridges 135.4・SweepRun 130.3・Control 95.6・SetupCandidates 70.4・BridgeRefresh 69.9・RunFront 66.8・RunScan 61.7・WeaponClash 59.2・MayTouch 48.8・Perception 40.7・DriveFighter 29.3・ApplyControl 29.2・FighterStanding 27.7・BotRead 25.3・RenderBridges 22.1・Bepu 6.6・Snap 1.4・SnapFind 1.3・SetupRelative 1.2・ClashPush 1.1・RunPatch 1.0・RenderParts 0.8・SetupFar 0.8・FighterStamina 0.7・FighterDown 0.5・BotPerceive 0.4
- 種 1 の 15.77 s: **519.1**（破片 0、GC0 1）: Weapons 438.4・SweepRun 148.6・SweepSetup 109.5・SetupCandidates 91.9・WeaponClash 88.2・Bridges 81.5・BridgeRefresh 77.0・RunFront 65.7・Control 51.5・RunScan 46.6・ClashPush 35.5・RunPatch 35.4・ControlTail 24.5・Bepu 20.0・Perception 16.6・FighterSplit 8.8・DriveFighter 7.3・ApplyControl 7.2・RenderBridges 3.2・BotRead 3.0・Snap 2.8・BotRead2 2.6・SnapFind 2.6・MayTouch 1.9・FighterStanding 1.0・RenderParts 0.5・FighterStamina 0.4・SetupRelative 0.3
- 種 1 の 17.63 s: **475.5**（破片 0、GC0 1）: Weapons 328.3・Bridges 129.6・BridgeRefresh 81.7・SweepRun 79.2・RunFront 70.7・WeaponClash 58.6・ClashPush 57.8・Control 56.9・Bepu 56.8・DriveFighter 55.7・ApplyControl 55.4・SweepSetup 52.1・FighterDown 34.3・CanvasImpacts 32.9・SetupCandidates 28.4・MayTouch 28.4・RenderBridges 18.2・RunScan 6.4・Snap 3.0・SnapFind 2.7・FighterStanding 1.1・RunPatch 0.9・FighterStamina 0.6・SetupRelative 0.6・Perception 0.5・RunSeparate 0.4・SetupFar 0.4・RenderParts 0.3
- 種 1 の 16.20 s: **468.7**（破片 0、GC0 0）: Weapons 343.8・SweepSetup 166.2・Bridges 114.5・WeaponClash 108.7・Control 81.8・SetupCandidates 80.0・SweepRun 68.4・RunScan 62.7・DriveFighter 60.2・ApplyControl 60.2・BridgeRefresh 51.4・Bepu 42.5・RenderBridges 41.1・SetupFar 37.8・FighterStamina 35.6・ClashPush 27.0・BotRead 21.0・BotPerceive 20.7・SetupRelative 6.0・RunFront 3.0・Snap 1.8・MayTouch 1.8・SnapFind 1.5・FighterStanding 1.0・RunSeparate 1.0・RunPatch 1.0・RenderParts 0.8・Cut 0.5・Perception 0.4
- 種 1 の 16.68 s: **452.1**（破片 0、GC0 1）: Weapons 411.5・SweepRun 197.8・Bridges 89.6・RunScan 86.9・WeaponClash 66.0・ClashPush 64.0・SweepSetup 61.5・RunPatch 53.1・Snap 50.2・SnapFind 49.8・RunFront 38.3・RenderBridges 25.6・RunOverlaps 17.5・RenderParts 17.5・CanvasImpacts 17.1・Bepu 16.3・SetupCandidates 7.7・Cut 6.8・Control 6.0・DriveFighter 3.9・ApplyControl 3.8・BridgeRefresh 3.7・FighterStanding 2.5・MayTouch 1.6・BotRead 1.4・RunCrush 1.0・FighterStamina 0.6・Perception 0.4・FighterSplit 0.4・FighterDown 0.3・FistHits 0.3

### c1 katana (MINIRING_SCAN=16 MINIRING_PASSES=2 MINIRING_SAMPLE=1)
## perf katana（2026-11-25 22:05、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 15.4 s。物理 102・判断 73。
チェックサム（計測あり）: eaf3d75fe62afdae

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 6.78 | 5.79 | 13.12 | 23.25 | 54.22 | 2.9% | 160 / 1 |
| 描画の詰め（CPU） | 1.67 | 0.80 | 6.60 | 10.05 | 119.44 | | |
| 　うちボクセル（FillVoxels） | 1.60 | 0.76 | 6.53 | 9.74 | 88.01 | | |
| 飛んでいる破片の数（描画の粒） | 14 | 1 | 91 | 93 | 93 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 1.021 | 15.1% | 3.17 | 21.34 |
| 　Perception | 0.158 | 2.3% | 0.18 | 2.20 |
| 　BotRead | 0.015 | 0.2% | 0.04 | 0.50 |
| 　DriveFighter | 0.797 | 11.8% | 2.70 | 21.13 |
| 　ApplyControl | 0.787 | 11.6% | 2.66 | 21.12 |
| 　FighterStamina | 0.143 | 2.1% | 0.22 | 4.27 |
| 　FighterStanding | 0.360 | 5.3% | 0.60 | 2.58 |
| 　FighterDown | 0.190 | 2.8% | 1.68 | 20.67 |
| 　ControlTail | 0.032 | 0.5% | 0.04 | 1.30 |
| PreBepu | 0.044 | 0.6% | 0.07 | 0.91 |
| 　Remember | 0.019 | 0.3% | 0.03 | 0.86 |
| Bepu | 1.366 | 20.2% | 5.34 | 50.72 |
| CanvasImpacts | 0.042 | 0.6% | 1.29 | 22.57 |
| FistHits | 0.006 | 0.1% | 0.02 | 0.09 |
| Weapons | 4.189 | 61.8% | 19.36 | 31.24 |
| 　MayTouch | 0.088 | 1.3% | 0.11 | 0.93 |
| 　SweepSetup | 1.366 | 20.2% | 3.83 | 13.47 |
| 　SetupRelative | 0.045 | 0.7% | 0.07 | 0.46 |
| 　SetupFar | 0.041 | 0.6% | 0.11 | 2.00 |
| 　SetupCandidates | 0.913 | 13.5% | 2.71 | 12.12 |
| 　SweepRun | 1.553 | 22.9% | 8.79 | 25.13 |
| 　RunScan | 0.456 | 6.7% | 1.60 | 3.52 |
| 　RunFront | 0.498 | 7.4% | 1.56 | 7.17 |
| 　RunOverlaps | 0.013 | 0.2% | 0.37 | 3.84 |
| 　RunPatch | 0.231 | 3.4% | 1.20 | 8.79 |
| 　RunHold | 0.003 | 0.1% | 0.01 | 0.25 |
| 　RunCrush | 0.174 | 2.6% | 3.59 | 22.27 |
| 　RunCut | 0.001 | 0.0% | 0.02 | 0.20 |
| 　RunSeparate | 0.086 | 1.3% | 0.16 | 8.12 |
| 　RunMass | 0.003 | 0.0% | 0.05 | 2.39 |
| 　Snap | 0.460 | 6.8% | 1.08 | 6.63 |
| 　SnapStiffness | 0.088 | 1.3% | 0.17 | 5.00 |
| 　SnapFind | 0.367 | 5.4% | 0.90 | 6.58 |
| 　Bend | 0.032 | 0.5% | 0.04 | 0.95 |
| 　Cut | 0.036 | 0.5% | 0.23 | 3.08 |
| 　Debris | 0.002 | 0.0% | 0.04 | 0.26 |
| 　WeaponClash | 0.143 | 2.1% | 2.67 | 11.67 |
| 　ClashAfter | 0.001 | 0.0% | 0.02 | 0.10 |
| 　ClashPush | 0.001 | 0.0% | 0.01 | 0.03 |
| 　Bridges | 0.544 | 8.0% | 5.33 | 10.87 |
| 　BridgeRefresh | 0.397 | 5.9% | 4.81 | 10.65 |
| Disconnect | 0.004 | 0.1% | 0.01 | 1.02 |
| Kicks | 0.004 | 0.1% | 0.01 | 0.43 |
| Elbows | 0.003 | 0.0% | 0.00 | 0.12 |
| FighterSplit | 0.053 | 0.8% | 0.09 | 3.84 |
| After | 0.019 | 0.3% | 0.06 | 0.57 |
| PostStep | 0.011 | 0.2% | 0.13 | 1.53 |
| 　PostDebris | 0.004 | 0.1% | 0.01 | 0.08 |
| 　PostWounds | 0.001 | 0.0% | 0.02 | 0.37 |
| 　PostSwings | 0.003 | 0.0% | 0.01 | 0.03 |
| 　PostRound | 0.002 | 0.0% | 0.00 | 0.83 |
| 　RenderParts | 0.174 | 2.6% | 0.27 | 11.57 |
| 　RenderFragments | 0.001 | 0.0% | 0.00 | 0.20 |
| 　RenderBridges | 1.396 | 20.6% | 3.47 | 70.23 |
| （枠の外） | 0.016 | 0.2% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 26 / 31、Candidates 22791 / 44567、ScanSamples 102 / 277、ChunksTested 1156 / 2927、BridgeCells 322 / 1453、FilledRebuildCells 178 / 0、BeamRebuildCells 389 / 156

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 3.85 s: **54.2**（破片 0、GC0 0）: Bepu 50.7・RenderBridges 3.3・Weapons 2.6・SweepSetup 1.2・SetupCandidates 0.9・SweepRun 0.9・Control 0.7・DriveFighter 0.5・ApplyControl 0.5・RunFront 0.3
- 種 1 の 2.12 s: **36.0**（破片 0、GC0 0）: CanvasImpacts 22.6・Weapons 9.1・Bridges 6.5・RenderBridges 6.4・BridgeRefresh 5.8・SweepSetup 1.9・PostStep 1.5・Bepu 1.3・Control 1.1・SweepRun 0.9・SetupCandidates 0.9・RunScan 0.9・DriveFighter 0.8・ApplyControl 0.8・FighterStanding 0.5・PostWounds 0.4・RenderParts 0.3
- 種 1 の 17.15 s: **33.5**（破片 0、GC0 0）: Weapons 31.2・SweepRun 18.2・RunPatch 8.8・SweepSetup 6.6・Bridges 5.6・SetupCandidates 3.9・RunOverlaps 3.8・BridgeRefresh 3.5・RunScan 2.5・RunFront 1.8・Snap 1.6・RenderBridges 1.4・SnapFind 1.3・Bepu 1.2・Control 0.8・WeaponClash 0.6・DriveFighter 0.6・ApplyControl 0.6・Cut 0.5・FighterStanding 0.4・SetupFar 0.3・RunMass 0.3
- 種 1 の 1.93 s: **31.6**（破片 0、GC0 0）: Weapons 18.6・Bepu 11.7・Bridges 6.4・BridgeRefresh 5.8・SweepRun 5.7・RenderBridges 4.1・SweepSetup 3.7・Cut 2.3・SetupCandidates 2.2・RunScan 1.9・RunFront 1.5・RunPatch 1.4・Control 1.0・WeaponClash 0.9・DriveFighter 0.8・ApplyControl 0.7・Snap 0.6・RunOverlaps 0.5・FighterStanding 0.5・SnapFind 0.4
- 種 1 の 17.13 s: **29.9**（破片 0、GC0 0）: Weapons 26.4・SweepRun 12.2・SweepSetup 7.2・Bridges 6.6・SetupCandidates 4.4・RunPatch 4.3・BridgeRefresh 4.0・RunScan 2.7・RunOverlaps 2.1・RunFront 1.9・RenderBridges 1.8・Control 1.6・Bepu 1.5・Snap 1.2・DriveFighter 1.2・ApplyControl 1.2・SnapFind 1.0・WeaponClash 1.0・Cut 0.9・FighterStanding 0.8・RenderParts 0.6・SetupFar 0.4・RunMass 0.3
- 種 1 の 8.32 s: **29.3**（破片 0、GC0 0）: Weapons 27.3・SweepRun 25.1・RunCrush 22.3・RunFront 1.3・SweepSetup 1.2・Bepu 1.1・SetupCandidates 0.8・Control 0.8・RenderBridges 0.7・RunPatch 0.6・Snap 0.6・DriveFighter 0.6・ApplyControl 0.6・SnapFind 0.5・RunScan 0.4
- 種 1 の 7.77 s: **29.3**（破片 0、GC0 0）: Weapons 27.0・SweepRun 24.1・RunCrush 20.3・RenderBridges 2.2・RunFront 1.7・SweepSetup 1.6・Bepu 1.1・Control 1.0・SetupCandidates 1.0・Snap 0.8・DriveFighter 0.7・ApplyControl 0.7・RunPatch 0.7・RunScan 0.7・SnapFind 0.6・RunSeparate 0.3・FighterStanding 0.3
- 種 1 の 2.32 s: **28.9**（破片 0、GC0 0）: Control 15.4・DriveFighter 11.5・ApplyControl 11.0・Weapons 10.5・FighterDown 8.6・RenderBridges 5.4・Bridges 5.2・BridgeRefresh 5.0・SweepRun 2.3・SweepSetup 2.1・Bepu 1.8・FighterStanding 1.3・SetupCandidates 0.9・RunFront 0.7・RunPatch 0.7・RunScan 0.5・After 0.5・PostStep 0.4・Snap 0.4・SnapFind 0.3・FighterStamina 0.3
- 種 1 の 8.43 s: **28.4**（破片 0、GC0 0）: Weapons 26.4・SweepRun 23.9・RunCrush 21.3・SweepSetup 1.4・RunFront 1.2・Bepu 1.0・SetupCandidates 1.0・Control 0.9・DriveFighter 0.6・ApplyControl 0.6・RunPatch 0.6・Snap 0.6・SnapFind 0.5・RunScan 0.5・RenderBridges 0.4
- 種 1 の 1.40 s: **27.6**（破片 0、GC0 0）: Weapons 23.4・WeaponClash 10.7・Bridges 9.3・BridgeRefresh 9.1・SweepRun 8.8・RenderBridges 5.1・RunFront 3.7・RunScan 3.5・SweepSetup 3.3・SetupCandidates 2.1・Control 1.9・Bepu 1.7・DriveFighter 1.5・ApplyControl 1.5・Snap 1.4・SnapFind 1.0・RunPatch 0.9・FighterStanding 0.9・RenderParts 0.9・RunSeparate 0.4・SnapStiffness 0.3・FighterStamina 0.3

### c2 royale (MINIRING_SCAN=16 MINIRING_PASSES=2 MINIRING_SAMPLE=3)
## perf royale（2026-11-25 22:07、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 108.0 s。物理 102・判断 73。
チェックサム（計測あり）: 372b995854e6d7e0

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 51.55 | 41.88 | 101.32 | 303.29 | 844.86 | 93.9% | 699 / 5 |
| 描画の詰め（CPU） | 7.54 | 4.49 | 22.22 | 62.19 | 408.91 | | |
| 　うちボクセル（FillVoxels） | 7.11 | 4.21 | 21.37 | 61.25 | 354.28 | | |
| 飛んでいる破片の数（描画の粒） | 311 | 255 | 685 | 786 | 809 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 5.227 | 10.1% | 33.68 | 157.52 |
| 　Perception | 0.816 | 1.6% | 8.31 | 60.88 |
| 　BotRead | 0.716 | 1.4% | 0.65 | 70.00 |
| 　BotPerceive | 0.130 | 0.3% | 0.27 | 36.89 |
| 　BotRead2 | 0.063 | 0.1% | 0.06 | 10.20 |
| 　BotChoose | 0.044 | 0.1% | 0.02 | 37.76 |
| 　BotExecute | 0.028 | 0.1% | 0.02 | 4.27 |
| 　DriveFighter | 3.162 | 6.1% | 23.21 | 108.00 |
| 　ApplyControl | 3.007 | 5.8% | 23.12 | 107.94 |
| 　FighterStamina | 0.614 | 1.2% | 3.78 | 32.81 |
| 　FighterStanding | 1.445 | 2.8% | 17.04 | 107.27 |
| 　FighterDown | 0.488 | 0.9% | 0.16 | 15.05 |
| 　ControlTail | 0.316 | 0.6% | 1.31 | 18.38 |
| PreBepu | 0.187 | 0.4% | 0.14 | 39.92 |
| 　Remember | 0.103 | 0.2% | 0.09 | 20.67 |
| Bepu | 4.621 | 9.0% | 20.94 | 71.12 |
| CanvasImpacts | 0.130 | 0.3% | 2.37 | 23.41 |
| FistHits | 0.052 | 0.1% | 0.04 | 3.56 |
| Weapons | 39.802 | 77.2% | 440.31 | 678.16 |
| 　MayTouch | 2.336 | 4.5% | 23.04 | 75.86 |
| 　SweepSetup | 15.639 | 30.3% | 155.08 | 238.43 |
| 　SetupRelative | 0.311 | 0.6% | 2.43 | 22.31 |
| 　SetupFar | 0.326 | 0.6% | 3.11 | 39.53 |
| 　SetupCandidates | 8.194 | 15.9% | 78.06 | 155.68 |
| 　SweepRun | 10.055 | 19.5% | 122.13 | 235.53 |
| 　RunScan | 4.679 | 9.1% | 45.77 | 85.60 |
| 　RunFront | 2.482 | 4.8% | 31.45 | 85.67 |
| 　RunOverlaps | 0.126 | 0.2% | 0.22 | 9.88 |
| 　RunPatch | 1.477 | 2.9% | 28.41 | 93.54 |
| 　RunHold | 0.026 | 0.1% | 0.04 | 23.59 |
| 　RunCrush | 0.290 | 0.6% | 3.40 | 45.45 |
| 　RunCut | 0.031 | 0.1% | 1.49 | 21.81 |
| 　RunSeparate | 0.556 | 1.1% | 5.80 | 98.67 |
| 　RunMass | 0.030 | 0.1% | 1.04 | 17.11 |
| 　Snap | 1.596 | 3.1% | 23.42 | 107.36 |
| 　SnapStiffness | 0.260 | 0.5% | 4.18 | 29.97 |
| 　SnapFind | 1.321 | 2.6% | 19.20 | 97.92 |
| 　SnapSplit | 0.002 | 0.0% | 0.00 | 3.39 |
| 　Bend | 0.143 | 0.3% | 1.78 | 28.56 |
| 　Cut | 0.291 | 0.6% | 2.94 | 64.13 |
| 　Debris | 0.027 | 0.1% | 0.98 | 15.50 |
| 　WeaponClash | 7.273 | 14.1% | 73.99 | 164.51 |
| 　ClashAfter | 0.030 | 0.1% | 0.89 | 15.28 |
| 　ClashPush | 2.146 | 4.2% | 14.73 | 42.64 |
| 　Bridges | 6.476 | 12.6% | 120.45 | 272.13 |
| 　BridgeRefresh | 3.198 | 6.2% | 46.62 | 149.09 |
| Disconnect | 0.014 | 0.0% | 0.07 | 4.41 |
| Kicks | 0.938 | 1.8% | 0.24 | 37.73 |
| Elbows | 0.011 | 0.0% | 0.01 | 2.00 |
| FighterSplit | 0.336 | 0.7% | 1.89 | 49.48 |
| After | 0.059 | 0.1% | 0.04 | 14.51 |
| PostStep | 0.143 | 0.3% | 0.06 | 32.04 |
| 　PostDebris | 0.075 | 0.1% | 0.04 | 10.26 |
| 　PostWounds | 0.002 | 0.0% | 0.00 | 0.21 |
| 　PostSwings | 0.006 | 0.0% | 0.00 | 0.74 |
| 　PostRound | 0.056 | 0.1% | 0.01 | 32.01 |
| 　RenderParts | 0.916 | 1.8% | 3.10 | 75.34 |
| 　RenderFragments | 0.026 | 0.0% | 2.06 | 37.09 |
| 　RenderBridges | 5.988 | 11.6% | 34.34 | 305.51 |
| （枠の外） | 0.028 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 121 / 145、Candidates 350328 / 614682、ScanSamples 318 / 429、ChunksTested 8931 / 12032、BridgeCells 1325 / 7599、FilledRebuildCells 539 / 139、BeamRebuildCells 1524 / 8044

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 4.37 s: **844.9**（破片 0、GC0 1）: Weapons 678.2・Bridges 272.1・SweepSetup 238.4・SweepRun 190.9・BridgeRefresh 118.9・Control 117.4・WeaponClash 111.5・SetupCandidates 99.6・DriveFighter 92.7・ApplyControl 92.7・RunScan 66.5・RunFront 65.9・FighterStanding 59.2・Snap 56.9・SnapFind 56.2・Bepu 48.2・ClashPush 32.2・RunPatch 29.3・RunSeparate 28.0・Perception 23.5・RenderBridges 4.0・MayTouch 2.2・BotRead 0.8・RenderParts 0.8・SnapStiffness 0.6・Cut 0.5・FighterSplit 0.4・FighterStamina 0.4・RunOverlaps 0.3
- 種 1 の 4.27 s: **834.2**（破片 0、GC0 0）: Weapons 674.1・Bridges 221.8・SweepSetup 207.8・SweepRun 188.5・WeaponClash 164.5・Control 157.5・SetupCandidates 127.1・DriveFighter 108.0・ApplyControl 107.9・FighterStanding 107.3・RunPatch 93.5・BridgeRefresh 89.5・RunScan 67.7・Perception 48.8・RenderBridges 47.9・MayTouch 39.7・Snap 33.5・SnapFind 32.9・ClashPush 27.4・RunMass 17.1・RunFront 5.6・RunSeparate 2.4・Bepu 1.8・RunOverlaps 0.9・Cut 0.6・BotRead 0.5・SnapStiffness 0.5・FighterSplit 0.5・FighterStamina 0.4・RunCrush 0.3・Debris 0.3・RenderParts 0.3・SetupRelative 0.3
- 種 1 の 10.22 s: **709.9**（破片 0、GC0 0）: Weapons 604.4・SweepRun 235.5・SweepSetup 214.1・Bridges 136.9・SetupCandidates 90.8・RunScan 85.6・Control 73.6・RunPatch 71.4・DriveFighter 54.1・ApplyControl 54.1・WeaponClash 53.5・RunFront 52.2・MayTouch 41.9・ClashPush 33.2・BridgeRefresh 31.4・Bepu 30.3・FighterStamina 28.0・RunSeparate 25.3・FighterStanding 25.2・ControlTail 18.4・SetupRelative 17.9・RenderParts 13.4・RenderBridges 8.4・Snap 2.2・SnapFind 1.8・Kicks 1.0・SetupFar 0.6・FighterDown 0.5・Perception 0.5・BotRead 0.4・SnapStiffness 0.3・Cut 0.3
- 種 1 の 4.38 s: **621.3**（破片 0、GC0 0）: Weapons 615.3・Bridges 233.4・SweepSetup 171.1・BridgeRefresh 149.1・RenderBridges 134.0・SweepRun 115.6・SetupCandidates 89.7・RunScan 75.9・MayTouch 75.9・RunFront 33.0・WeaponClash 22.1・Cut 17.2・Snap 9.6・SnapFind 8.8・RunPatch 4.3・Control 3.4・DriveFighter 2.1・ApplyControl 2.0・Bepu 1.9・ClashPush 1.7・FighterStanding 1.4・RunSeparate 1.2・SnapStiffness 0.7・Perception 0.6・BotRead 0.5・RenderParts 0.4・FighterStamina 0.4・RunOverlaps 0.3・SetupRelative 0.3・SetupFar 0.3
- 種 1 の 4.25 s: **617.1**（破片 0、GC0 1）: Weapons 611.2・Bridges 215.2・SweepSetup 166.1・SweepRun 107.8・RenderBridges 95.1・WeaponClash 94.0・RunScan 78.1・MayTouch 45.2・Snap 40.1・SnapFind 39.9・SetupCandidates 36.6・RunPatch 26.7・BridgeRefresh 20.7・SetupRelative 19.7・Control 3.4・RunFront 2.2・DriveFighter 2.0・ApplyControl 2.0・Bepu 1.8・ClashPush 1.4・FighterStanding 1.4・Perception 0.7・BotRead 0.4・RenderParts 0.4・FighterStamina 0.3
- 種 1 の 4.20 s: **509.1**（破片 0、GC0 0）: Weapons 456.4・SweepSetup 161.7・SetupCandidates 155.7・Bridges 122.3・WeaponClash 105.7・Snap 70.4・SweepRun 69.0・RunPatch 47.7・SnapFind 41.8・MayTouch 33.9・Control 29.3・SnapStiffness 28.6・Perception 26.8・ClashPush 26.2・Cut 24.0・RenderBridges 22.7・CanvasImpacts 18.1・RunFront 17.3・BridgeRefresh 14.9・Bepu 4.8・RunScan 3.2・DriveFighter 1.8・ApplyControl 1.8・FighterStanding 1.2・BotRead 0.4・RenderParts 0.4・FighterStamina 0.4・RunSeparate 0.3・FighterSplit 0.3
- 種 1 の 4.18 s: **475.1**（破片 0、GC0 0）: Weapons 443.4・SweepRun 145.1・SweepSetup 97.5・Bridges 94.0・RunPatch 78.1・WeaponClash 77.7・RenderBridges 61.7・SetupCandidates 55.5・MayTouch 47.2・BridgeRefresh 42.4・RunFront 20.9・Bepu 18.0・FighterSplit 11.0・RunScan 3.1・Control 2.4・Cut 2.0・Snap 1.6・DriveFighter 1.5・ClashPush 1.4・ApplyControl 1.4・SnapFind 1.3・FighterStanding 0.9・RunSeparate 0.5・BotRead 0.4・Perception 0.3・FighterStamina 0.3
- 種 1 の 4.35 s: **474.4**（破片 0、GC0 1）: Weapons 401.4・SweepSetup 194.7・SetupCandidates 137.3・SweepRun 84.8・Bridges 68.6・BridgeRefresh 61.8・Control 45.6・DriveFighter 44.1・ApplyControl 44.1・FighterStanding 43.3・MayTouch 36.2・RenderParts 33.6・WeaponClash 31.3・RunScan 29.1・Bepu 26.8・RenderBridges 25.4・RunCut 21.8・Snap 17.2・SnapFind 16.4・RunPatch 8.6・RunFront 4.7・ClashPush 1.5・RunSeparate 1.1・Perception 0.8・SnapStiffness 0.7・Cut 0.5・SetupFar 0.5・FighterStamina 0.4・BotRead 0.4・Bend 0.4・SetupRelative 0.3
- 種 1 の 10.35 s: **460.6**（破片 0、GC0 1）: Weapons 379.1・SweepSetup 172.4・Bridges 94.7・SweepRun 77.7・RunScan 73.0・WeaponClash 59.0・SetupCandidates 54.3・Bepu 44.4・Control 35.9・DriveFighter 34.5・ApplyControl 34.5・FighterStanding 33.3・ClashPush 33.3・BridgeRefresh 27.0・MayTouch 19.1・SetupFar 5.4・RenderBridges 3.2・RunFront 1.8・RunPatch 1.5・Snap 1.4・SnapFind 1.2・RenderParts 0.8・RunSeparate 0.8・Kicks 0.6・FighterDown 0.5・SetupRelative 0.5・ControlTail 0.4・BotRead 0.4・FighterStamina 0.4・Perception 0.4
- 種 1 の 4.23 s: **448.6**（破片 0、GC0 0）: Weapons 415.7・SweepSetup 200.4・SetupCandidates 115.3・SweepRun 103.6・Bridges 80.7・RenderBridges 68.5・RunScan 60.0・WeaponClash 53.6・BridgeRefresh 46.6・RunPatch 22.8・RunFront 19.3・Bepu 18.4・ClashPush 15.4・Control 13.9・DriveFighter 12.7・ApplyControl 12.6・FighterStamina 11.0・Snap 3.0・SnapFind 2.6・MayTouch 1.9・FighterStanding 1.4・RenderParts 0.9・Perception 0.5・BotRead 0.5・RunSeparate 0.5・SnapStiffness 0.4

### c2 katana (MINIRING_SCAN=16 MINIRING_PASSES=2 MINIRING_SAMPLE=3)
## perf katana（2026-11-25 22:08、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 27.4 s。物理 102・判断 73。
チェックサム（計測あり）: 9e5996f70e1eadd1

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 12.60 | 9.86 | 31.10 | 48.11 | 250.84 | 19.4% | 176 / 5 |
| 描画の詰め（CPU） | 2.45 | 1.26 | 8.89 | 14.87 | 182.06 | | |
| 　うちボクセル（FillVoxels） | 2.34 | 1.20 | 8.69 | 14.79 | 144.07 | | |
| 飛んでいる破片の数（描画の粒） | 22 | 3 | 99 | 121 | 127 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 1.508 | 12.0% | 3.94 | 21.90 |
| 　Perception | 0.248 | 2.0% | 1.01 | 16.33 |
| 　BotRead | 0.042 | 0.3% | 0.17 | 1.78 |
| 　DriveFighter | 1.144 | 9.1% | 2.71 | 21.69 |
| 　ApplyControl | 1.116 | 8.9% | 2.53 | 21.67 |
| 　FighterStamina | 0.223 | 1.8% | 0.18 | 6.24 |
| 　FighterStanding | 0.544 | 4.3% | 1.02 | 7.24 |
| 　FighterDown | 0.207 | 1.6% | 1.19 | 21.02 |
| 　ControlTail | 0.047 | 0.4% | 0.04 | 1.55 |
| PreBepu | 0.065 | 0.5% | 0.07 | 3.49 |
| 　Remember | 0.028 | 0.2% | 0.02 | 1.32 |
| Bepu | 1.888 | 15.0% | 6.83 | 54.33 |
| CanvasImpacts | 0.062 | 0.5% | 2.16 | 38.44 |
| FistHits | 0.010 | 0.1% | 0.01 | 1.60 |
| Weapons | 8.729 | 69.3% | 44.53 | 73.56 |
| 　MayTouch | 0.147 | 1.2% | 0.23 | 11.78 |
| 　SweepSetup | 3.550 | 28.2% | 13.84 | 29.07 |
| 　SetupRelative | 0.072 | 0.6% | 0.21 | 1.86 |
| 　SetupFar | 0.073 | 0.6% | 0.30 | 4.42 |
| 　SetupCandidates | 1.883 | 14.9% | 6.94 | 25.61 |
| 　SweepRun | 2.952 | 23.4% | 16.73 | 51.83 |
| 　RunScan | 1.096 | 8.7% | 4.42 | 8.26 |
| 　RunFront | 1.071 | 8.5% | 7.14 | 39.03 |
| 　RunOverlaps | 0.015 | 0.1% | 0.17 | 3.01 |
| 　RunPatch | 0.365 | 2.9% | 2.30 | 15.06 |
| 　RunHold | 0.009 | 0.1% | 0.50 | 8.77 |
| 　RunCrush | 0.107 | 0.9% | 0.20 | 5.79 |
| 　RunCut | 0.002 | 0.0% | 0.02 | 0.28 |
| 　RunSeparate | 0.161 | 1.3% | 1.24 | 6.29 |
| 　RunMass | 0.007 | 0.1% | 0.02 | 3.37 |
| 　Snap | 0.583 | 4.6% | 2.95 | 17.93 |
| 　SnapStiffness | 0.111 | 0.9% | 0.85 | 12.88 |
| 　SnapFind | 0.465 | 3.7% | 2.05 | 17.88 |
| 　Bend | 0.059 | 0.5% | 2.37 | 41.68 |
| 　Cut | 0.061 | 0.5% | 0.26 | 4.32 |
| 　Debris | 0.007 | 0.1% | 0.03 | 2.70 |
| 　WeaponClash | 0.635 | 5.0% | 4.75 | 35.80 |
| 　ClashAfter | 0.006 | 0.0% | 0.01 | 2.73 |
| 　ClashPush | 0.005 | 0.0% | 0.03 | 1.51 |
| 　Bridges | 1.249 | 9.9% | 10.51 | 22.61 |
| 　BridgeRefresh | 0.890 | 7.1% | 6.74 | 19.41 |
| Disconnect | 0.010 | 0.1% | 0.01 | 1.78 |
| Kicks | 0.008 | 0.1% | 0.01 | 1.28 |
| Elbows | 0.005 | 0.0% | 0.00 | 0.87 |
| FighterSplit | 0.108 | 0.9% | 0.16 | 11.44 |
| After | 0.032 | 0.3% | 0.02 | 3.29 |
| PostStep | 0.151 | 1.2% | 13.66 | 244.63 |
| 　PostDebris | 0.008 | 0.1% | 0.01 | 0.68 |
| 　PostWounds | 0.001 | 0.0% | 0.02 | 0.28 |
| 　PostSwings | 0.004 | 0.0% | 0.00 | 0.08 |
| 　PostRound | 0.138 | 1.1% | 13.59 | 244.62 |
| 　RenderParts | 0.240 | 1.9% | 0.38 | 19.48 |
| 　RenderFragments | 0.002 | 0.0% | 0.00 | 0.92 |
| 　RenderBridges | 2.049 | 16.3% | 3.39 | 114.69 |
| （枠の外） | 0.025 | 0.2% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 27 / 77、Candidates 89979 / 269007、ScanSamples 96 / 273、ChunksTested 2068 / 5927、BridgeCells 427 / 3718、FilledRebuildCells 1156 / 0、BeamRebuildCells 2466 / 1320

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 16.88 s: **250.8**（破片 0、GC0 5）: PostStep 244.6・PostRound 244.6・RenderBridges 13.0・Weapons 4.1・SweepSetup 2.0・SweepRun 1.6・Bepu 1.1・Control 0.9・SetupCandidates 0.8・RunScan 0.8・DriveFighter 0.7・ApplyControl 0.6・RenderParts 0.6・RunFront 0.5・FighterStanding 0.3
- 種 1 の 23.73 s: **83.4**（破片 0、GC0 0）: Weapons 73.6・SweepRun 51.8・RunFront 39.0・WeaponClash 35.8・SweepSetup 12.4・RunPatch 9.5・Control 7.2・DriveFighter 6.9・ApplyControl 6.9・FighterStanding 6.6・Bridges 4.6・BridgeRefresh 4.1・Snap 3.6・SetupCandidates 3.4・SnapFind 3.1・Bepu 2.2・RenderBridges 1.8・RunScan 1.8・RunSeparate 0.6・SnapStiffness 0.5・MayTouch 0.4・SetupFar 0.3
- 種 1 の 26.45 s: **78.8**（破片 0、GC0 0）: Bepu 54.3・Control 21.9・DriveFighter 21.7・ApplyControl 21.7・FighterDown 21.0・Weapons 2.4・SweepSetup 1.4・SetupCandidates 0.9・SweepRun 0.6・FighterStanding 0.4・RunScan 0.4・RenderBridges 0.3
- 種 1 の 22.85 s: **77.8**（破片 0、GC0 1）: Weapons 71.4・SweepSetup 29.1・SweepRun 26.1・SetupCandidates 15.7・Bridges 12.5・RunFront 8.8・WeaponClash 8.0・Snap 7.0・RunSeparate 6.3・RunScan 6.0・BridgeRefresh 6.0・SnapFind 4.4・Bepu 3.3・Control 2.8・SnapStiffness 2.3・BotRead 1.8・RunPatch 1.6・RenderBridges 1.2・DriveFighter 0.7・ApplyControl 0.7・SetupFar 0.7・FighterStanding 0.5・MayTouch 0.3
- 種 1 の 22.63 s: **69.8**（破片 0、GC0 0）: Weapons 49.3・SweepSetup 20.0・Control 18.1・SweepRun 17.2・Perception 14.3・Bridges 14.1・SetupCandidates 11.3・BridgeRefresh 6.7・RunScan 6.2・RunFront 5.3・DriveFighter 3.4・ApplyControl 3.4・FighterStanding 3.1・Snap 2.9・WeaponClash 2.4・RunPatch 2.0・SnapFind 2.0・RunSeparate 2.0・Bepu 1.9・RenderBridges 1.1・SnapStiffness 0.8・RenderParts 0.5・Cut 0.4・SetupRelative 0.4・BotRead 0.3・RunCrush 0.3・PreBepu 0.3
- 種 1 の 23.23 s: **65.8**（破片 0、GC0 0）: Weapons 63.0・SweepSetup 19.3・Bridges 17.7・SweepRun 15.8・BridgeRefresh 13.7・SetupCandidates 12.9・Snap 12.1・SnapStiffness 7.2・RunFront 6.8・RunScan 5.6・SnapFind 4.9・RenderBridges 3.1・WeaponClash 2.5・RenderParts 1.9・RunPatch 1.7・Control 1.3・Bepu 1.2・DriveFighter 1.0・ApplyControl 0.9・FighterStanding 0.7・RunSeparate 0.5・Cut 0.4・SetupRelative 0.3
- 種 1 の 15.72 s: **62.4**（破片 0、GC0 0）: Weapons 58.9・Bend 41.7・SweepRun 8.7・SweepSetup 5.6・RunFront 3.7・SetupCandidates 3.0・RunScan 1.9・RunPatch 1.6・Snap 1.4・Control 1.3・Bepu 1.3・SnapFind 1.1・RenderBridges 1.0・DriveFighter 0.8・ApplyControl 0.8・FighterSplit 0.8・Cut 0.6・FighterStanding 0.4・Perception 0.3
- 種 1 の 22.87 s: **60.6**（破片 0、GC0 0）: Weapons 57.8・SweepSetup 27.6・SweepRun 19.6・Bridges 12.7・SetupCandidates 11.1・RunScan 8.3・WeaponClash 7.2・BridgeRefresh 6.1・RunFront 5.6・Snap 2.6・RunPatch 2.5・RunSeparate 2.3・SnapFind 2.2・RenderBridges 2.1・Bepu 1.6・Control 1.0・DriveFighter 0.7・ApplyControl 0.7・FighterStanding 0.5・SnapStiffness 0.3
- 種 1 の 22.90 s: **60.3**（破片 0、GC0 0）: Weapons 57.3・SweepRun 27.3・SweepSetup 16.8・Bridges 16.6・RunFront 15.9・SetupCandidates 9.3・BridgeRefresh 8.6・RunScan 8.2・WeaponClash 5.8・Snap 3.1・SnapFind 2.7・Bepu 1.8・RunPatch 1.5・RunSeparate 1.0・RenderBridges 1.0・Control 0.9・DriveFighter 0.7・ApplyControl 0.7・FighterStanding 0.4・SnapStiffness 0.3
- 種 1 の 23.70 s: **58.1**（破片 0、GC0 1）: Weapons 39.5・SweepRun 26.3・Bepu 16.4・RunHold 8.8・RunPatch 6.4・SweepSetup 6.4・RunFront 5.6・RenderBridges 4.2・SetupCandidates 4.0・Bridges 4.0・BridgeRefresh 3.7・Snap 1.7・RunOverlaps 1.6・Control 1.5・SnapFind 1.4・WeaponClash 1.2・RunScan 1.2・DriveFighter 1.1・ApplyControl 1.1・FighterStanding 0.7・FighterSplit 0.5・Cut 0.5・SnapStiffness 0.3

### c3 royale (MINIRING_SCAN=16 MINIRING_PASSES=2 MINIRING_SAMPLE=3 MINIRING_BRIDGES=0)
## perf royale（2026-11-25 22:10、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 83.0 s。物理 102・判断 73。
チェックサム（計測あり）: 8c6b28df9b51f428

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 38.31 | 34.73 | 68.38 | 160.36 | 381.37 | 87.5% | 476 / 6 |
| 描画の詰め（CPU） | 7.06 | 4.91 | 18.32 | 31.05 | 355.86 | | |
| 　うちボクセル（FillVoxels） | 6.64 | 4.51 | 17.84 | 29.67 | 312.98 | | |
| 飛んでいる破片の数（描画の粒） | 397 | 387 | 813 | 1193 | 1209 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 4.567 | 11.9% | 29.34 | 114.96 |
| 　Perception | 0.637 | 1.7% | 4.46 | 57.79 |
| 　BotRead | 0.701 | 1.8% | 4.69 | 47.33 |
| 　BotPerceive | 0.116 | 0.3% | 2.74 | 44.24 |
| 　BotRead2 | 0.085 | 0.2% | 0.07 | 10.83 |
| 　BotChoose | 0.042 | 0.1% | 0.01 | 25.94 |
| 　BotExecute | 0.040 | 0.1% | 0.18 | 9.53 |
| 　DriveFighter | 2.753 | 7.2% | 15.32 | 44.29 |
| 　ApplyControl | 2.635 | 6.9% | 14.94 | 44.20 |
| 　FighterStamina | 0.570 | 1.5% | 3.62 | 33.33 |
| 　FighterStanding | 1.050 | 2.7% | 3.72 | 27.19 |
| 　FighterDown | 0.603 | 1.6% | 6.43 | 41.55 |
| 　ControlTail | 0.228 | 0.6% | 0.76 | 19.55 |
| PreBepu | 0.167 | 0.4% | 1.12 | 10.75 |
| 　Remember | 0.099 | 0.3% | 0.49 | 3.48 |
| Bepu | 4.482 | 11.7% | 33.41 | 101.60 |
| CanvasImpacts | 0.100 | 0.3% | 0.65 | 15.29 |
| FistHits | 0.068 | 0.2% | 0.09 | 12.02 |
| Weapons | 27.607 | 72.1% | 159.83 | 270.56 |
| 　MayTouch | 1.397 | 3.6% | 10.38 | 45.21 |
| 　SweepSetup | 12.342 | 32.2% | 69.92 | 134.33 |
| 　SetupRelative | 0.279 | 0.7% | 2.96 | 36.75 |
| 　SetupFar | 0.224 | 0.6% | 0.95 | 12.87 |
| 　SetupCandidates | 6.252 | 16.3% | 38.13 | 113.81 |
| 　SweepRun | 7.945 | 20.7% | 38.03 | 78.03 |
| 　RunScan | 3.674 | 9.6% | 20.58 | 78.01 |
| 　RunFront | 2.189 | 5.7% | 8.31 | 43.87 |
| 　RunOverlaps | 0.103 | 0.3% | 0.18 | 8.76 |
| 　RunPatch | 1.080 | 2.8% | 4.76 | 55.99 |
| 　RunHold | 0.011 | 0.0% | 0.01 | 0.63 |
| 　RunCrush | 0.199 | 0.5% | 0.30 | 55.95 |
| 　RunCut | 0.009 | 0.0% | 0.01 | 1.00 |
| 　RunSeparate | 0.406 | 1.1% | 3.17 | 29.13 |
| 　RunMass | 0.023 | 0.1% | 0.04 | 7.88 |
| 　Snap | 1.118 | 2.9% | 4.61 | 31.53 |
| 　SnapStiffness | 0.197 | 0.5% | 2.22 | 28.46 |
| 　SnapFind | 0.902 | 2.4% | 2.37 | 18.69 |
| 　SnapSplit | 0.007 | 0.0% | 0.00 | 11.51 |
| 　Bend | 0.156 | 0.4% | 0.09 | 46.94 |
| 　Cut | 0.319 | 0.8% | 3.01 | 34.97 |
| 　Debris | 0.029 | 0.1% | 0.02 | 9.04 |
| 　WeaponClash | 5.547 | 14.5% | 37.56 | 80.28 |
| 　ClashAfter | 0.034 | 0.1% | 0.03 | 9.09 |
| 　ClashPush | 1.464 | 3.8% | 7.41 | 46.54 |
| Disconnect | 0.022 | 0.1% | 0.06 | 1.42 |
| Kicks | 0.698 | 1.8% | 0.04 | 17.19 |
| Elbows | 0.010 | 0.0% | 0.01 | 0.69 |
| FighterSplit | 0.323 | 0.8% | 3.41 | 53.55 |
| After | 0.048 | 0.1% | 0.09 | 1.08 |
| PostStep | 0.152 | 0.4% | 0.12 | 20.69 |
| 　PostDebris | 0.086 | 0.2% | 0.07 | 4.11 |
| 　PostWounds | 0.003 | 0.0% | 0.00 | 0.66 |
| 　PostSwings | 0.020 | 0.1% | 0.00 | 20.24 |
| 　PostRound | 0.040 | 0.1% | 0.04 | 9.97 |
| 　RenderParts | 0.714 | 1.9% | 3.05 | 30.54 |
| 　RenderFragments | 0.008 | 0.0% | 0.01 | 0.80 |
| 　RenderBridges | 5.760 | 15.0% | 11.37 | 266.88 |
| （枠の外） | 0.067 | 0.2% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 100 / 98、Candidates 306258 / 308281、ScanSamples 257 / 244、ChunksTested 7658 / 7690、BridgeCells 0 / 0、FilledRebuildCells 1871 / 4507、BeamRebuildCells 4819 / 9816

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 26.25 s: **381.4**（破片 1、GC0 0）: Weapons 270.6・Bepu 101.6・WeaponClash 80.3・SweepRun 77.3・SweepSetup 71.0・SetupCandidates 64.2・MayTouch 45.2・RunFront 43.9・RunScan 32.6・ClashPush 26.0・Control 5.1・RenderBridges 4.0・PreBepu 3.5・Remember 3.5・DriveFighter 3.4・ApplyControl 3.2・Snap 2.8・SnapFind 2.5・FighterStanding 1.2・FighterDown 0.9・BotRead 0.8・RenderParts 0.7・FighterStamina 0.5・Perception 0.5・RunPatch 0.3・SetupRelative 0.3
- 種 1 の 27.75 s: **364.2**（破片 1、GC0 0）: Weapons 254.0・SweepSetup 88.9・WeaponClash 71.9・Bepu 62.9・Control 46.6・SetupCandidates 46.2・DriveFighter 44.3・ApplyControl 44.2・FighterDown 41.6・SweepRun 41.5・SetupRelative 36.8・ClashPush 36.3・RenderBridges 34.2・RunSeparate 22.7・RunScan 18.1・MayTouch 13.3・Snap 2.4・SnapFind 2.2・Perception 1.8・FighterStanding 1.7・RunFront 0.5・FighterStamina 0.5・RenderParts 0.3
- 種 1 の 27.90 s: **326.9**（破片 1、GC0 0）: Weapons 167.5・Control 115.0・SweepRun 78.0・RunScan 78.0・SweepSetup 69.9・WeaponClash 66.9・SetupCandidates 65.9・Perception 57.8・Bepu 43.7・DriveFighter 4.5・ApplyControl 4.3・RenderBridges 2.6・ClashPush 2.3・FighterStanding 2.0・MayTouch 1.5・FighterStamina 1.0・FighterDown 1.0・RenderParts 0.7・BotRead 0.5
- 種 1 の 28.22 s: **308.2**（破片 1、GC0 1）: Weapons 211.5・SweepSetup 71.7・WeaponClash 61.5・Bepu 49.9・Control 45.8・DriveFighter 31.1・MayTouch 30.8・SweepRun 30.7・RunScan 30.6・ApplyControl 30.5・SetupCandidates 27.7・FighterStanding 27.2・Cut 20.5・RenderBridges 14.9・ClashPush 7.2・Perception 4.6・FighterStamina 2.2・BotRead 0.8・FighterDown 0.7・RenderParts 0.5・SetupRelative 0.4
- 種 1 の 27.17 s: **307.6**（破片 1、GC0 0）: Weapons 261.7・SweepSetup 113.1・SweepRun 71.3・WeaponClash 67.9・RunScan 31.8・Snap 31.5・RunSeparate 29.1・Bepu 28.7・SnapStiffness 28.5・SetupCandidates 23.2・MayTouch 17.6・Control 14.2・DriveFighter 12.9・ApplyControl 12.7・FighterStamina 10.3・RunFront 4.9・RunPatch 4.7・ClashPush 3.3・SnapFind 3.1・RenderBridges 2.5・PreBepu 1.9・Remember 1.8・RenderParts 1.3・FighterDown 0.8・FighterStanding 0.8・Perception 0.6・BotRead 0.4・SetupRelative 0.4・SetupFar 0.4・FistHits 0.3
- 種 1 の 25.50 s: **258.1**（破片 1、GC0 0）: Weapons 158.6・SweepSetup 96.1・Control 61.5・DriveFighter 38.3・Bepu 36.7・ApplyControl 34.3・SweepRun 31.1・RunScan 31.1・SetupCandidates 24.5・WeaponClash 23.4・FighterDown 21.2・ControlTail 11.9・MayTouch 8.8・BotRead 8.1・BotPerceive 3.7・RenderBridges 3.3・Perception 2.9・ClashPush 2.3・FighterStamina 2.0・FighterStanding 1.8・SetupFar 1.2・RenderParts 0.9・PreBepu 0.7・SetupRelative 0.4・Remember 0.4・FighterSplit 0.3
- 種 1 の 21.67 s: **226.8**（破片 1、GC0 0）: Weapons 176.4・SweepSetup 134.3・SetupCandidates 113.8・SweepRun 28.3・WeaponClash 21.8・RunFront 19.5・Control 17.8・DriveFighter 16.6・ApplyControl 16.5・FighterDown 13.0・RunScan 8.1・ClashPush 6.0・Bepu 4.4・RenderBridges 3.7・FighterStanding 2.8・MayTouch 2.1・RenderParts 0.7・Perception 0.6・Snap 0.5・BotRead 0.4・SnapFind 0.4・FighterStamina 0.4・RunPatch 0.3
- 種 1 の 27.77 s: **220.7**（破片 1、GC0 0）: Weapons 129.8・SweepSetup 40.5・SetupCandidates 36.3・MayTouch 33.5・Bepu 28.6・WeaponClash 15.3・CanvasImpacts 10.3・Control 7.4・DriveFighter 6.1・ApplyControl 6.0・Snap 4.6・SweepRun 4.2・SnapStiffness 4.2・RunScan 2.8・RenderBridges 2.3・FighterDown 1.4・ClashPush 1.2・FighterStanding 0.8・RenderParts 0.8・FighterStamina 0.7・Perception 0.7・RunFront 0.6・SnapFind 0.4・BotRead 0.4
- 種 1 の 20.43 s: **211.6**（破片 1、GC0 0）: Weapons 140.4・SweepSetup 58.8・WeaponClash 47.6・SetupCandidates 42.8・Control 36.9・DriveFighter 33.9・ApplyControl 33.6・Bepu 33.0・FighterStamina 29.4・Cut 23.1・SweepRun 12.9・MayTouch 11.7・ClashPush 8.8・RunFront 6.0・RenderBridges 4.1・RunScan 3.9・Snap 2.5・SnapFind 2.2・RunPatch 2.1・Perception 1.8・FighterStanding 1.7・FighterDown 1.7・RenderParts 0.8・SetupFar 0.6・SetupRelative 0.5・FighterSplit 0.5・BotRead 0.4・PreBepu 0.4
- 種 1 の 26.22 s: **209.7**（破片 1、GC0 0）: Weapons 159.1・WeaponClash 71.5・SweepRun 47.0・SweepSetup 41.2・SetupCandidates 29.5・RunFront 29.4・Bepu 28.5・Snap 23.7・SnapFind 18.7・Control 17.7・DriveFighter 10.9・ApplyControl 10.7・FighterStanding 8.1・RunScan 5.8・SnapStiffness 4.9・RunPatch 4.5・ClashPush 3.5・BotRead 3.3・FighterSplit 2.8・Perception 2.8・RunCrush 2.8・BotExecute 2.7・RenderBridges 2.2・SetupFar 1.9・MayTouch 1.8・FighterDown 0.9・FighterStamina 0.9・RunOverlaps 0.7・SetupRelative 0.6・RunMass 0.5・After 0.4・RunSeparate 0.3

### c3 katana (MINIRING_SCAN=16 MINIRING_PASSES=2 MINIRING_SAMPLE=3 MINIRING_BRIDGES=0)
## perf katana（2026-11-25 22:10、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 25.4 s。物理 102・判断 73。
チェックサム（計測あり）: 31d543b938951810

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 11.68 | 10.24 | 24.15 | 33.25 | 229.06 | 17.0% | 189 / 5 |
| 描画の詰め（CPU） | 2.31 | 1.23 | 9.07 | 17.09 | 135.27 | | |
| 　うちボクセル（FillVoxels） | 2.21 | 1.17 | 8.77 | 16.25 | 96.82 | | |
| 飛んでいる破片の数（描画の粒） | 38 | 8 | 134 | 162 | 165 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 1.249 | 10.7% | 4.79 | 27.06 |
| 　Perception | 0.202 | 1.7% | 1.70 | 25.77 |
| 　BotRead | 0.049 | 0.4% | 0.09 | 1.41 |
| 　DriveFighter | 0.933 | 8.0% | 2.59 | 12.93 |
| 　ApplyControl | 0.919 | 7.9% | 2.54 | 12.85 |
| 　FighterStamina | 0.185 | 1.6% | 0.49 | 12.36 |
| 　FighterStanding | 0.505 | 4.3% | 0.99 | 3.49 |
| 　FighterDown | 0.111 | 0.9% | 0.72 | 8.73 |
| 　ControlTail | 0.042 | 0.4% | 0.07 | 0.88 |
| PreBepu | 0.054 | 0.5% | 0.10 | 1.46 |
| 　Remember | 0.023 | 0.2% | 0.04 | 0.36 |
| Bepu | 1.495 | 12.8% | 3.16 | 15.00 |
| CanvasImpacts | 0.053 | 0.5% | 1.87 | 32.36 |
| FistHits | 0.010 | 0.1% | 0.03 | 5.96 |
| Weapons | 8.532 | 73.1% | 28.25 | 57.13 |
| 　MayTouch | 0.071 | 0.6% | 0.10 | 0.54 |
| 　SweepSetup | 3.655 | 31.3% | 12.91 | 30.07 |
| 　SetupRelative | 0.068 | 0.6% | 0.13 | 1.46 |
| 　SetupFar | 0.061 | 0.5% | 0.12 | 1.22 |
| 　SetupCandidates | 1.908 | 16.3% | 8.55 | 28.55 |
| 　SweepRun | 3.740 | 32.0% | 9.93 | 24.15 |
| 　RunScan | 1.452 | 12.4% | 4.75 | 17.56 |
| 　RunFront | 1.382 | 11.8% | 2.87 | 10.49 |
| 　RunOverlaps | 0.016 | 0.1% | 0.06 | 0.68 |
| 　RunPatch | 0.458 | 3.9% | 1.31 | 10.99 |
| 　RunHold | 0.005 | 0.0% | 0.05 | 0.85 |
| 　RunCrush | 0.125 | 1.1% | 0.23 | 4.53 |
| 　RunCut | 0.002 | 0.0% | 0.01 | 0.52 |
| 　RunSeparate | 0.172 | 1.5% | 0.29 | 4.00 |
| 　RunMass | 0.007 | 0.1% | 0.13 | 2.23 |
| 　Snap | 0.617 | 5.3% | 1.90 | 17.39 |
| 　SnapStiffness | 0.110 | 0.9% | 0.96 | 14.82 |
| 　SnapFind | 0.500 | 4.3% | 0.92 | 4.73 |
| 　Bend | 0.052 | 0.4% | 2.01 | 20.35 |
| 　Cut | 0.056 | 0.5% | 0.26 | 2.77 |
| 　Debris | 0.006 | 0.1% | 0.03 | 2.07 |
| 　WeaponClash | 0.619 | 5.3% | 3.77 | 56.80 |
| 　ClashAfter | 0.005 | 0.0% | 0.02 | 2.09 |
| 　ClashPush | 0.007 | 0.1% | 0.02 | 1.31 |
| Disconnect | 0.009 | 0.1% | 0.01 | 0.91 |
| Kicks | 0.006 | 0.1% | 0.01 | 0.37 |
| Elbows | 0.005 | 0.0% | 0.01 | 0.69 |
| FighterSplit | 0.082 | 0.7% | 0.34 | 5.08 |
| After | 0.027 | 0.2% | 0.07 | 4.84 |
| PostStep | 0.138 | 1.2% | 12.27 | 220.04 |
| 　PostDebris | 0.009 | 0.1% | 0.01 | 0.37 |
| 　PostWounds | 0.001 | 0.0% | 0.03 | 0.53 |
| 　PostSwings | 0.003 | 0.0% | 0.00 | 0.27 |
| 　PostRound | 0.125 | 1.1% | 12.23 | 220.03 |
| 　RenderParts | 0.218 | 1.9% | 0.32 | 14.17 |
| 　RenderFragments | 0.001 | 0.0% | 0.00 | 0.30 |
| 　RenderBridges | 1.942 | 16.6% | 5.63 | 75.01 |
| （枠の外） | 0.020 | 0.2% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 32 / 43、Candidates 101159 / 138273、ScanSamples 104 / 134、ChunksTested 2364 / 3029、BridgeCells 0 / 0、FilledRebuildCells 1778 / 8890、BeamRebuildCells 3718 / 36383

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 16.88 s: **229.1**（破片 0、GC0 5）: PostStep 220.0・PostRound 220.0・RenderBridges 14.2・Weapons 5.8・SweepSetup 4.0・SetupCandidates 2.4・Bepu 1.6・Control 1.4・SweepRun 1.4・DriveFighter 0.9・ApplyControl 0.9・RunScan 0.8・RenderParts 0.7・FighterStanding 0.4・RunFront 0.4
- 種 1 の 0.92 s: **68.0**（破片 0、GC0 0）: Weapons 57.1・WeaponClash 56.8・SweepSetup 28.3・SetupCandidates 27.3・Snap 17.4・SnapStiffness 14.8・RenderBridges 12.0・SweepRun 10.7・RunFront 8.6・Bepu 7.8・Control 2.7・SnapFind 2.6・DriveFighter 2.0・ApplyControl 2.0・RunScan 1.6・FighterStanding 1.4・ClashAfter 0.3・RunPatch 0.3
- 種 1 の 15.90 s: **53.4**（破片 0、GC0 0）: Control 27.1・Perception 25.8・Weapons 23.7・SweepRun 11.8・SweepSetup 8.3・SetupCandidates 5.3・RunFront 4.9・RunPatch 3.2・Bepu 2.5・RunScan 2.4・Snap 1.7・SnapFind 1.4・DriveFighter 0.7・ApplyControl 0.7・RenderBridges 0.6・Cut 0.6・BotRead 0.6・RunSeparate 0.5
- 種 1 の 26.97 s: **48.8**（破片 0、GC0 0）: Weapons 46.4・SweepRun 24.1・SweepSetup 19.7・SetupCandidates 13.8・RunFront 8.1・RunScan 7.7・RunPatch 6.3・WeaponClash 3.4・RunCrush 1.4・Snap 1.4・Bepu 1.3・SnapFind 1.1・Control 0.9・RenderBridges 0.8・DriveFighter 0.7・ApplyControl 0.7・RenderParts 0.6・Cut 0.4・FighterStanding 0.4・SetupFar 0.3
- 種 1 の 2.10 s: **46.3**（破片 0、GC0 0）: CanvasImpacts 32.4・RenderBridges 11.0・Weapons 4.6・FighterSplit 4.4・SweepSetup 2.7・Control 2.1・Bepu 2.0・DriveFighter 1.6・ApplyControl 1.6・SetupCandidates 1.5・SweepRun 1.3・RunScan 1.2・FighterStanding 1.1・PostStep 0.6・RenderParts 0.5・PostWounds 0.5・Perception 0.3
- 種 1 の 2.73 s: **41.0**（破片 0、GC0 0）: Weapons 35.3・SweepRun 22.7・RunScan 17.0・SweepSetup 8.4・SetupCandidates 4.3・RenderBridges 3.6・Control 3.3・DriveFighter 2.6・ApplyControl 2.5・RunFront 2.2・RunPatch 1.9・Bepu 1.8・FighterStanding 1.2・RenderParts 1.0・FighterDown 0.8・Snap 0.7・RunSeparate 0.7・Perception 0.6・SnapFind 0.5・RunCrush 0.3・FighterStamina 0.3
- 種 1 の 1.23 s: **39.3**（破片 0、GC0 0）: Weapons 31.9・SweepSetup 30.1・SetupCandidates 28.6・RenderBridges 5.4・Control 3.7・Bepu 3.2・DriveFighter 3.1・ApplyControl 3.1・WeaponClash 2.2・FighterStanding 1.7・SweepRun 1.1・RunScan 1.1・FighterStamina 0.8・Perception 0.4
- 種 1 の 3.02 s: **38.6**（破片 0、GC0 1）: Weapons 33.5・SweepSetup 20.4・SetupCandidates 14.1・SweepRun 11.3・RunScan 10.6・RenderBridges 5.7・Control 2.5・DriveFighter 2.2・ApplyControl 2.1・Bepu 1.7・FighterStanding 0.9・FistHits 0.4・FighterDown 0.3
- 種 1 の 19.08 s: **38.4**（破片 0、GC0 1）: Weapons 36.1・Bend 15.3・SweepSetup 9.4・SweepRun 8.4・SetupCandidates 4.7・RunPatch 3.4・RenderBridges 3.1・RunFront 2.4・Snap 1.8・RunScan 1.7・SnapFind 1.5・Bepu 1.1・Control 0.9・WeaponClash 0.8・Cut 0.6・DriveFighter 0.6・ApplyControl 0.6・FighterStanding 0.4・RunOverlaps 0.4
- 種 1 の 2.85 s: **37.7**（破片 0、GC0 0）: Weapons 28.4・SweepRun 14.3・SweepSetup 12.6・RunScan 11.3・SetupCandidates 7.2・RenderBridges 7.0・Control 6.4・DriveFighter 5.9・ApplyControl 5.9・FighterStamina 3.9・Bepu 2.2・RunFront 1.5・FighterDown 0.9・FighterStanding 0.8・RunPatch 0.7・Snap 0.5・Perception 0.4・SnapFind 0.4・RunSeparate 0.3

### c4 royale (MINIRING_SCAN=16 MINIRING_PASSES=2 MINIRING_SAMPLE=3 MINIRING_BRIDGES=0 MINIRING_HZ=180)
## perf royale（2026-11-25 22:11、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 68.8 s。物理 102・判断 73。
チェックサム（計測あり）: 8b63aac01d75a31a

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 29.49 | 26.55 | 55.73 | 86.10 | 142.04 | 82.8% | 396 / 1 |
| 描画の詰め（CPU） | 7.81 | 4.58 | 26.56 | 40.40 | 650.62 | | |
| 　うちボクセル（FillVoxels） | 7.45 | 4.31 | 25.86 | 40.15 | 564.59 | | |
| 飛んでいる破片の数（描画の粒） | 198 | 91 | 594 | 793 | 835 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 3.809 | 12.9% | 16.62 | 67.48 |
| 　Perception | 0.499 | 1.7% | 1.22 | 8.47 |
| 　BotRead | 0.590 | 2.0% | 4.53 | 59.57 |
| 　BotPerceive | 0.125 | 0.4% | 0.40 | 11.48 |
| 　BotRead2 | 0.031 | 0.1% | 0.04 | 7.58 |
| 　BotChoose | 0.040 | 0.1% | 0.01 | 51.11 |
| 　BotExecute | 0.023 | 0.1% | 0.02 | 8.01 |
| 　DriveFighter | 2.291 | 7.8% | 8.30 | 44.00 |
| 　ApplyControl | 2.194 | 7.4% | 8.18 | 43.94 |
| 　FighterStamina | 0.447 | 1.5% | 1.13 | 11.67 |
| 　FighterStanding | 0.815 | 2.8% | 5.73 | 24.01 |
| 　FighterDown | 0.561 | 1.9% | 0.86 | 42.67 |
| 　ControlTail | 0.260 | 0.9% | 2.09 | 22.04 |
| PreBepu | 0.144 | 0.5% | 0.74 | 8.86 |
| 　Remember | 0.087 | 0.3% | 0.67 | 8.66 |
| Bepu | 3.899 | 13.2% | 10.44 | 33.76 |
| CanvasImpacts | 0.086 | 0.3% | 0.26 | 16.45 |
| FistHits | 0.058 | 0.2% | 0.04 | 9.00 |
| Weapons | 20.335 | 69.0% | 71.17 | 103.95 |
| 　MayTouch | 1.095 | 3.7% | 3.68 | 15.42 |
| 　SweepSetup | 8.236 | 27.9% | 20.57 | 70.45 |
| 　SetupRelative | 0.203 | 0.7% | 1.06 | 7.59 |
| 　SetupFar | 0.182 | 0.6% | 0.33 | 8.23 |
| 　SetupCandidates | 4.239 | 14.4% | 11.86 | 46.10 |
| 　SweepRun | 6.723 | 22.8% | 24.03 | 74.15 |
| 　RunScan | 2.716 | 9.2% | 7.50 | 30.97 |
| 　RunFront | 2.017 | 6.8% | 8.27 | 23.27 |
| 　RunOverlaps | 0.096 | 0.3% | 0.24 | 6.41 |
| 　RunPatch | 0.951 | 3.2% | 2.85 | 14.20 |
| 　RunHold | 0.014 | 0.0% | 0.06 | 1.41 |
| 　RunCrush | 0.274 | 0.9% | 3.43 | 56.76 |
| 　RunCut | 0.006 | 0.0% | 0.02 | 1.07 |
| 　RunSeparate | 0.353 | 1.2% | 0.98 | 16.45 |
| 　RunMass | 0.016 | 0.1% | 0.06 | 5.45 |
| 　Snap | 1.202 | 4.1% | 5.86 | 47.41 |
| 　SnapStiffness | 0.238 | 0.8% | 0.87 | 33.48 |
| 　SnapFind | 0.944 | 3.2% | 4.95 | 40.86 |
| 　SnapSplit | 0.005 | 0.0% | 0.00 | 5.17 |
| 　Bend | 0.084 | 0.3% | 2.58 | 44.85 |
| 　Cut | 0.189 | 0.6% | 1.82 | 28.96 |
| 　Debris | 0.023 | 0.1% | 0.05 | 10.60 |
| 　WeaponClash | 3.244 | 11.0% | 16.31 | 66.00 |
| 　ClashAfter | 0.020 | 0.1% | 0.06 | 10.24 |
| 　ClashPush | 0.681 | 2.3% | 1.50 | 6.97 |
| Disconnect | 0.014 | 0.0% | 0.08 | 1.01 |
| Kicks | 0.728 | 2.5% | 0.85 | 20.89 |
| Elbows | 0.009 | 0.0% | 0.01 | 1.61 |
| FighterSplit | 0.244 | 0.8% | 0.27 | 42.15 |
| After | 0.047 | 0.2% | 0.07 | 8.87 |
| PostStep | 0.090 | 0.3% | 0.14 | 4.42 |
| 　PostDebris | 0.053 | 0.2% | 0.07 | 4.34 |
| 　PostWounds | 0.001 | 0.0% | 0.02 | 0.26 |
| 　PostSwings | 0.006 | 0.0% | 0.00 | 0.95 |
| 　PostRound | 0.028 | 0.1% | 0.04 | 1.99 |
| 　RenderParts | 0.614 | 2.1% | 0.55 | 32.43 |
| 　RenderFragments | 0.007 | 0.0% | 0.01 | 0.64 |
| 　RenderBridges | 6.675 | 22.6% | 10.24 | 511.46 |
| （枠の外） | 0.023 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 69 / 84、Candidates 182261 / 283796、ScanSamples 190 / 247、ChunksTested 5269 / 7075、BridgeCells 0 / 0、FilledRebuildCells 1020 / 8904、BeamRebuildCells 2050 / 17515

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 2.13 s: **142.0**（破片 0、GC0 1）: Weapons 92.8・RenderBridges 41.4・SweepSetup 36.0・SetupCandidates 35.4・Control 29.2・DriveFighter 25.9・ApplyControl 25.7・FighterStanding 23.4・Bepu 19.1・MayTouch 15.4・SweepRun 12.6・RunScan 12.6・BotRead 2.8・FighterStamina 1.8・WeaponClash 1.4・RenderParts 1.2・BotPerceive 0.7・ClashPush 0.5・PostStep 0.3・Perception 0.3
- 種 1 の 17.13 s: **120.0**（破片 0、GC0 0）: Weapons 101.9・SweepSetup 70.4・SetupCandidates 46.1・WeaponClash 18.1・SweepRun 10.4・Bepu 9.4・MayTouch 8.7・Control 7.8・RunScan 6.6・Snap 5.8・SnapFind 5.5・BotRead 4.5・RenderBridges 4.4・BotPerceive 3.3・DriveFighter 2.7・ApplyControl 2.6・RunFront 2.4・ClashPush 1.4・SetupFar 1.3・FighterDown 0.6・FighterStanding 0.6・RunPatch 0.5・SetupRelative 0.5・Perception 0.4・FighterStamina 0.4・RenderParts 0.4・RunSeparate 0.4・CanvasImpacts 0.4
- 種 1 の 11.70 s: **111.1**（破片 0、GC0 0）: Control 52.4・Bepu 29.0・Weapons 23.5・DriveFighter 22.7・ApplyControl 22.2・ControlTail 22.0・FighterStanding 15.0・SweepSetup 12.0・RenderBridges 6.9・BotRead 6.5・SetupCandidates 6.2・FighterStamina 5.5・Kicks 5.4・SweepRun 5.3・RunScan 4.5・WeaponClash 3.0・MayTouch 2.4・BotPerceive 1.8・ClashPush 1.7・SetupRelative 1.5・FighterDown 1.2・SetupFar 1.1・Perception 0.9・RenderParts 0.8・FighterSplit 0.5・RunFront 0.3
- 種 1 の 5.00 s: **110.0**（破片 0、GC0 0）: Weapons 103.9・Bend 44.8・SweepRun 32.2・WeaponClash 21.0・SweepSetup 15.9・RunFront 14.1・RunPatch 9.7・SetupCandidates 7.5・Snap 5.2・RenderBridges 4.6・SnapFind 4.0・RunScan 3.6・Control 2.9・Bepu 2.1・DriveFighter 1.9・ApplyControl 1.8・RunSeparate 1.3・SnapStiffness 1.2・ClashPush 1.1・MayTouch 0.9・Cut 0.7・FighterStanding 0.7・FighterSplit 0.6・RenderParts 0.6・RunCrush 0.6・FighterStamina 0.5・BotRead 0.5・Perception 0.3・FighterDown 0.3
- 種 1 の 22.67 s: **106.9**（破片 1、GC0 0）: Weapons 74.8・RenderBridges 33.3・Control 21.5・DriveFighter 18.7・ApplyControl 18.5・FighterStanding 15.6・SweepSetup 13.7・SweepRun 11.8・RunScan 5.6・Kicks 5.3・SetupCandidates 5.3・Bepu 4.8・SetupRelative 3.7・RunFront 3.7・Perception 2.1・MayTouch 2.1・WeaponClash 1.6・FighterStamina 1.4・Snap 1.2・RunPatch 1.1・FighterDown 1.1・SnapFind 1.0・RunSeparate 0.6・RunCrush 0.5・RenderParts 0.4・Cut 0.3
- 種 1 の 5.25 s: **104.8**（破片 0、GC0 0）: Weapons 94.9・WeaponClash 66.0・Snap 47.4・SnapFind 40.9・SweepRun 27.4・RunFront 14.7・SweepSetup 11.4・RenderBridges 8.7・Control 6.6・SnapStiffness 6.5・SetupCandidates 4.8・MayTouch 4.8・RunPatch 4.7・RunScan 3.9・Bepu 2.0・RunSeparate 2.0・DriveFighter 1.6・ApplyControl 1.6・ClashPush 1.0・RenderParts 0.7・FighterSplit 0.7・RunOverlaps 0.6・Perception 0.5・FighterStanding 0.5・RunCrush 0.5・FighterDown 0.5・BotRead 0.4・Bend 0.4・Cut 0.3・FighterStamina 0.3
- 種 1 の 15.03 s: **101.9**（破片 0、GC0 0）: Weapons 79.3・SweepRun 53.4・WeaponClash 42.9・RunScan 31.0・RunFront 21.4・Control 15.0・MayTouch 14.4・ControlTail 12.6・Bepu 7.0・SweepSetup 5.5・ClashPush 3.7・RenderBridges 3.7・SetupCandidates 2.8・DriveFighter 1.8・ApplyControl 1.7・RunPatch 0.8・FighterStanding 0.7・FighterDown 0.4・FighterStamina 0.3・Kicks 0.3
- 種 1 の 21.10 s: **100.9**（破片 1、GC0 0）: Weapons 41.6・Bepu 28.8・Control 19.7・DriveFighter 16.8・ApplyControl 16.7・FighterStanding 14.3・PreBepu 8.9・Remember 8.7・SweepSetup 5.8・SweepRun 3.9・RenderBridges 3.9・SetupCandidates 2.2・RunScan 2.1・WeaponClash 1.9・MayTouch 1.7・FighterDown 1.4・ControlTail 1.3・Kicks 1.3・RunFront 1.0・RenderParts 1.0・Snap 0.8・FighterStamina 0.6・SnapStiffness 0.5・Perception 0.5・BotRead 0.3・SnapFind 0.3・ClashPush 0.3・RunSeparate 0.3
- 種 1 の 16.00 s: **97.7**（破片 0、GC0 0）: Weapons 64.7・Bepu 27.8・SweepSetup 16.3・SweepRun 16.0・RunScan 15.1・SetupCandidates 8.6・RenderBridges 4.6・Control 4.0・DriveFighter 3.0・ApplyControl 2.9・MayTouch 1.9・WeaponClash 1.7・FighterStamina 1.2・RenderParts 0.8・Snap 0.8・FighterStanding 0.7・SnapFind 0.7・SetupFar 0.7・FighterSplit 0.7・FighterDown 0.7・Perception 0.4・RunFront 0.4・SetupRelative 0.4
- 種 1 の 5.30 s: **96.0**（破片 0、GC0 0）: Weapons 91.1・SweepRun 74.1・RunCrush 56.8・WeaponClash 17.8・RunFront 8.7・SweepSetup 7.4・Snap 5.4・RenderBridges 4.9・SnapFind 4.4・SetupCandidates 4.3・RunScan 3.5・RunPatch 3.1・Control 2.2・Bepu 1.8・DriveFighter 1.2・ApplyControl 1.2・RunSeparate 1.2・RenderParts 1.0・SnapStiffness 1.0・ClashPush 0.8・MayTouch 0.7・FighterStanding 0.5・Perception 0.4・BotRead 0.4・PostStep 0.3・RunOverlaps 0.3

### c4 katana (MINIRING_SCAN=16 MINIRING_PASSES=2 MINIRING_SAMPLE=3 MINIRING_BRIDGES=0 MINIRING_HZ=180)
## perf katana（2026-11-25 22:12、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 22.0 s。物理 102・判断 73。
チェックサム（計測あり）: 23038abd72f839a9

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 8.24 | 5.19 | 22.01 | 63.01 | 236.35 | 6.8% | 106 / 2 |
| 描画の詰め（CPU） | 3.83 | 1.31 | 16.55 | 27.72 | 191.20 | | |
| 　うちボクセル（FillVoxels） | 3.72 | 1.25 | 16.37 | 27.50 | 142.57 | | |
| 飛んでいる破片の数（描画の粒） | 32 | 9 | 115 | 133 | 137 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 1.158 | 14.1% | 6.63 | 43.41 |
| 　Perception | 0.206 | 2.5% | 3.04 | 42.13 |
| 　BotRead | 0.035 | 0.4% | 0.14 | 2.75 |
| 　DriveFighter | 0.854 | 10.4% | 3.38 | 32.04 |
| 　ApplyControl | 0.842 | 10.2% | 3.36 | 32.03 |
| 　FighterStamina | 0.156 | 1.9% | 0.23 | 2.73 |
| 　FighterStanding | 0.409 | 5.0% | 1.18 | 7.46 |
| 　FighterDown | 0.172 | 2.1% | 1.76 | 31.60 |
| 　ControlTail | 0.046 | 0.6% | 0.06 | 10.28 |
| PreBepu | 0.049 | 0.6% | 0.08 | 3.76 |
| 　Remember | 0.021 | 0.3% | 0.03 | 2.84 |
| Bepu | 1.532 | 18.6% | 6.99 | 84.05 |
| CanvasImpacts | 0.045 | 0.5% | 0.04 | 16.00 |
| FistHits | 0.007 | 0.1% | 0.01 | 1.65 |
| Weapons | 5.178 | 62.8% | 60.84 | 102.89 |
| 　MayTouch | 0.051 | 0.6% | 0.19 | 1.30 |
| 　SweepSetup | 2.069 | 25.1% | 15.91 | 42.38 |
| 　SetupRelative | 0.048 | 0.6% | 0.21 | 1.31 |
| 　SetupFar | 0.046 | 0.6% | 0.25 | 2.01 |
| 　SetupCandidates | 1.204 | 14.6% | 8.72 | 32.45 |
| 　SweepRun | 2.323 | 28.2% | 32.82 | 61.35 |
| 　RunScan | 0.956 | 11.6% | 10.43 | 21.75 |
| 　RunFront | 0.734 | 8.9% | 8.78 | 22.70 |
| 　RunOverlaps | 0.052 | 0.6% | 2.85 | 35.45 |
| 　RunPatch | 0.278 | 3.4% | 4.02 | 12.84 |
| 　RunHold | 0.006 | 0.1% | 0.08 | 2.52 |
| 　RunCrush | 0.096 | 1.2% | 2.67 | 35.81 |
| 　RunCut | 0.005 | 0.1% | 0.31 | 4.95 |
| 　RunSeparate | 0.087 | 1.1% | 0.78 | 4.84 |
| 　RunMass | 0.015 | 0.2% | 1.02 | 15.42 |
| 　Snap | 0.373 | 4.5% | 6.78 | 63.22 |
| 　SnapStiffness | 0.070 | 0.9% | 1.12 | 5.22 |
| 　SnapFind | 0.293 | 3.6% | 5.60 | 58.07 |
| 　Bend | 0.032 | 0.4% | 1.78 | 26.41 |
| 　Cut | 0.055 | 0.7% | 2.16 | 30.29 |
| 　Debris | 0.021 | 0.3% | 1.32 | 22.18 |
| 　WeaponClash | 0.570 | 6.9% | 8.09 | 64.27 |
| 　ClashAfter | 0.008 | 0.1% | 0.06 | 7.19 |
| 　ClashPush | 0.004 | 0.0% | 0.01 | 0.58 |
| Disconnect | 0.009 | 0.1% | 0.10 | 3.24 |
| Kicks | 0.009 | 0.1% | 0.01 | 7.25 |
| Elbows | 0.003 | 0.0% | 0.01 | 0.43 |
| FighterSplit | 0.069 | 0.8% | 0.33 | 5.66 |
| After | 0.019 | 0.2% | 0.04 | 0.54 |
| PostStep | 0.147 | 1.8% | 12.88 | 230.83 |
| 　PostDebris | 0.010 | 0.1% | 0.04 | 1.04 |
| 　PostWounds | 0.001 | 0.0% | 0.00 | 0.18 |
| 　PostSwings | 0.004 | 0.0% | 0.01 | 0.47 |
| 　PostRound | 0.131 | 1.6% | 12.83 | 230.82 |
| 　RenderParts | 0.249 | 3.0% | 2.37 | 19.43 |
| 　RenderFragments | 0.001 | 0.0% | 0.00 | 0.10 |
| 　RenderBridges | 3.415 | 41.4% | 15.81 | 115.01 |
| （枠の外） | 0.016 | 0.2% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 15 / 46、Candidates 36263 / 151837、ScanSamples 58 / 184、ChunksTested 982 / 4278、BridgeCells 0 / 0、FilledRebuildCells 1334 / 44450、BeamRebuildCells 2706 / 125172

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 20.93 s: **236.4**（破片 0、GC0 5）: PostStep 230.8・PostRound 230.8・RenderBridges 9.7・Weapons 2.2・Bepu 1.9・Control 1.3・SweepSetup 1.2・DriveFighter 0.9・ApplyControl 0.9・SetupCandidates 0.7・SweepRun 0.6・RunScan 0.6・FighterStanding 0.5・RenderParts 0.3
- 種 1 の 8.13 s: **118.7**（破片 0、GC0 0）: Bepu 84.1・Control 32.2・DriveFighter 32.0・ApplyControl 32.0・FighterDown 31.6・Weapons 2.3・SweepSetup 1.7・SetupCandidates 1.2・RenderBridges 1.0・SweepRun 0.4・RunScan 0.4
- 種 1 の 2.75 s: **111.9**（破片 0、GC0 0）: Weapons 102.9・SweepRun 50.1・SweepSetup 42.4・RunScan 21.8・RenderBridges 16.9・WeaponClash 12.6・SetupCandidates 12.2・RunFront 11.6・RenderParts 9.5・RunPatch 8.6・Snap 8.5・SnapFind 7.2・Bepu 5.3・Control 3.0・RunSeparate 1.7・DriveFighter 1.5・ApplyControl 1.5・Perception 1.4・FighterStanding 1.1・SnapStiffness 1.0・RunOverlaps 0.8・SetupRelative 0.6・RunCrush 0.5・SetupFar 0.3・Disconnect 0.3
- 種 1 の 1.80 s: **105.6**（破片 0、GC0 0）: Weapons 82.6・SweepRun 43.8・Cut 30.3・Debris 22.2・RunMass 15.4・Control 15.3・RenderBridges 14.4・RunFront 11.5・Perception 7.6・DriveFighter 7.2・ApplyControl 7.2・Bepu 7.1・FighterStanding 6.4・SweepSetup 5.4・RunCut 5.0・RunScan 3.5・SetupCandidates 3.4・RunPatch 3.1・RenderParts 1.9・WeaponClash 1.6・RunOverlaps 1.4・Bend 1.3・Snap 1.0・SnapFind 0.7・RunSeparate 0.6・FighterStamina 0.5・BotRead 0.3
- 種 1 の 2.80 s: **92.2**（破片 0、GC0 0）: Weapons 86.2・SweepRun 55.6・RunFront 18.5・SweepSetup 18.3・RunScan 17.0・SetupCandidates 10.6・RenderBridges 10.4・Snap 8.7・WeaponClash 7.9・RunPatch 6.7・SnapFind 4.4・SnapStiffness 4.1・Bepu 3.7・MayTouch 1.2・Control 1.1・RunSeparate 1.1・RunCrush 0.8・DriveFighter 0.8・ApplyControl 0.7・FighterSplit 0.6・Cut 0.6・SetupFar 0.5・FighterStanding 0.5・RenderParts 0.4・RunOverlaps 0.3・SetupRelative 0.3
- 種 1 の 1.95 s: **89.1**（破片 0、GC0 0）: Weapons 81.7・SweepRun 61.4・RenderBridges 36.9・RunOverlaps 35.4・SweepSetup 12.7・RunScan 10.4・SetupCandidates 7.1・RunPatch 7.0・Snap 5.4・SnapFind 5.0・RunFront 4.9・FighterSplit 3.1・Control 2.2・Bepu 1.7・DriveFighter 1.5・ApplyControl 1.5・RunMass 1.5・WeaponClash 1.3・FighterStanding 1.0・RenderParts 0.9・Bend 0.8・RunSeparate 0.6・Cut 0.5・SnapStiffness 0.4・Perception 0.3・FighterStamina 0.3
- 種 1 の 2.78 s: **77.3**（破片 0、GC0 0）: Weapons 72.0・SweepRun 34.6・SweepSetup 24.5・RenderBridges 18.7・RunScan 16.2・SetupCandidates 14.6・WeaponClash 11.9・RunFront 10.8・Snap 7.4・SnapFind 5.9・RunPatch 3.4・Control 2.8・DriveFighter 2.5・ApplyControl 2.5・FighterStanding 1.8・Bepu 1.6・SnapStiffness 1.4・Cut 1.3・RunSeparate 1.3・RenderParts 0.8・RunCrush 0.5・SetupFar 0.5・FighterStamina 0.5・RunOverlaps 0.5・Disconnect 0.4
- 種 1 の 2.70 s: **75.0**（破片 0、GC0 0）: Weapons 71.4・SweepRun 41.5・RenderBridges 33.7・SweepSetup 22.4・RunFront 19.7・RunScan 12.5・SetupCandidates 12.1・RunPatch 5.5・WeaponClash 3.9・Snap 3.4・SnapFind 2.6・Bend 1.8・Control 1.6・Bepu 1.6・RunCrush 1.5・DriveFighter 1.2・ApplyControl 1.2・RunSeparate 0.9・SnapStiffness 0.8・FighterStanding 0.5・RenderParts 0.5・Cut 0.3
- 種 1 の 1.97 s: **74.2**（破片 0、GC0 0）: Weapons 66.9・SweepRun 35.6・SweepSetup 26.1・RunScan 15.0・RenderBridges 14.4・SetupCandidates 13.4・RunPatch 10.9・RunFront 5.6・WeaponClash 5.5・Control 4.4・DriveFighter 3.9・ApplyControl 3.8・FighterStanding 3.3・Bepu 2.3・RunOverlaps 1.7・Snap 1.4・Cut 1.2・SnapFind 0.9・RenderParts 0.8・Bend 0.7・RunSeparate 0.7・SnapStiffness 0.4・Perception 0.3・FighterSplit 0.3・SetupFar 0.3
- 種 1 の 2.37 s: **72.1**（破片 0、GC0 0）: Weapons 69.5・SweepRun 54.8・RunCrush 35.8・SweepSetup 12.0・RunScan 10.2・RenderBridges 7.7・SetupCandidates 7.5・RunFront 5.6・WeaponClash 3.4・RunPatch 1.8・Snap 1.7・SnapFind 1.3・Control 1.2・Bepu 1.1・DriveFighter 0.9・ApplyControl 0.8・RunSeparate 0.8・FighterStanding 0.6・RenderParts 0.4・SnapStiffness 0.4

### c5 royale (MINIRING_SCAN=16 MINIRING_PASSES=2 MINIRING_SAMPLE=3 MINIRING_BRIDGES=0 MINIRING_HZ=120)
## perf royale（2026-11-25 22:13、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 53.3 s。物理 102・判断 73。
チェックサム（計測あり）: b549ca0f102fe115

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 18.96 | 15.93 | 40.52 | 59.64 | 157.97 | 46.7% | 243 / 1 |
| 描画の詰め（CPU） | 9.71 | 6.02 | 31.99 | 45.27 | 448.77 | | |
| 　うちボクセル（FillVoxels） | 9.29 | 5.61 | 31.71 | 44.89 | 375.27 | | |
| 飛んでいる破片の数（描画の粒） | 223 | 192 | 520 | 579 | 583 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 2.236 | 11.8% | 4.41 | 27.62 |
| 　Perception | 0.277 | 1.5% | 0.62 | 4.52 |
| 　BotRead | 0.464 | 2.4% | 0.66 | 26.46 |
| 　BotPerceive | 0.076 | 0.4% | 0.33 | 4.48 |
| 　BotRead2 | 0.049 | 0.3% | 0.03 | 7.50 |
| 　BotChoose | 0.034 | 0.2% | 0.01 | 21.65 |
| 　BotExecute | 0.016 | 0.1% | 0.01 | 2.38 |
| 　DriveFighter | 1.241 | 6.5% | 2.55 | 19.85 |
| 　ApplyControl | 1.188 | 6.3% | 2.52 | 11.66 |
| 　FighterStamina | 0.261 | 1.4% | 0.76 | 8.32 |
| 　FighterStanding | 0.560 | 3.0% | 1.23 | 5.97 |
| 　FighterDown | 0.168 | 0.9% | 0.16 | 6.26 |
| 　ControlTail | 0.135 | 0.7% | 0.09 | 12.73 |
| PreBepu | 0.088 | 0.5% | 0.29 | 4.05 |
| 　Remember | 0.050 | 0.3% | 0.09 | 2.21 |
| Bepu | 2.192 | 11.6% | 6.36 | 46.94 |
| CanvasImpacts | 0.050 | 0.3% | 0.05 | 8.55 |
| FistHits | 0.028 | 0.1% | 0.02 | 1.84 |
| Weapons | 13.759 | 72.6% | 63.72 | 147.86 |
| 　MayTouch | 0.614 | 3.2% | 1.47 | 5.63 |
| 　SweepSetup | 5.586 | 29.5% | 18.58 | 34.65 |
| 　SetupRelative | 0.110 | 0.6% | 0.20 | 3.60 |
| 　SetupFar | 0.100 | 0.5% | 0.13 | 3.00 |
| 　SetupCandidates | 2.870 | 15.1% | 9.64 | 17.53 |
| 　SweepRun | 4.426 | 23.3% | 21.68 | 47.76 |
| 　RunScan | 1.559 | 8.2% | 5.66 | 15.46 |
| 　RunFront | 1.386 | 7.3% | 7.57 | 22.31 |
| 　RunOverlaps | 0.082 | 0.4% | 0.47 | 4.44 |
| 　RunPatch | 0.839 | 4.4% | 4.85 | 19.50 |
| 　RunHold | 0.011 | 0.1% | 0.04 | 1.84 |
| 　RunCrush | 0.106 | 0.6% | 0.87 | 5.57 |
| 　RunCut | 0.006 | 0.0% | 0.03 | 0.79 |
| 　RunSeparate | 0.250 | 1.3% | 1.45 | 6.79 |
| 　RunMass | 0.020 | 0.1% | 0.13 | 3.43 |
| 　Snap | 0.887 | 4.7% | 4.74 | 16.65 |
| 　SnapStiffness | 0.154 | 0.8% | 0.85 | 7.74 |
| 　SnapFind | 0.717 | 3.8% | 3.83 | 11.13 |
| 　SnapSplit | 0.007 | 0.0% | 0.00 | 11.60 |
| 　Bend | 0.105 | 0.6% | 2.88 | 47.74 |
| 　Cut | 0.216 | 1.1% | 9.01 | 70.94 |
| 　Debris | 0.026 | 0.1% | 1.31 | 18.29 |
| 　WeaponClash | 2.916 | 15.4% | 18.55 | 40.42 |
| 　ClashAfter | 0.021 | 0.1% | 1.11 | 18.22 |
| 　ClashPush | 0.560 | 3.0% | 1.60 | 7.47 |
| Disconnect | 0.013 | 0.1% | 0.13 | 2.21 |
| Kicks | 0.279 | 1.5% | 0.41 | 18.37 |
| Elbows | 0.005 | 0.0% | 0.00 | 1.02 |
| FighterSplit | 0.161 | 0.9% | 0.52 | 9.66 |
| After | 0.023 | 0.1% | 0.07 | 1.48 |
| PostStep | 0.107 | 0.6% | 0.17 | 2.59 |
| 　PostDebris | 0.068 | 0.4% | 0.05 | 2.08 |
| 　PostWounds | 0.003 | 0.0% | 0.06 | 0.97 |
| 　PostSwings | 0.008 | 0.0% | 0.02 | 2.47 |
| 　PostRound | 0.026 | 0.1% | 0.02 | 2.50 |
| 　RenderParts | 0.801 | 4.2% | 1.69 | 27.86 |
| 　RenderFragments | 0.006 | 0.0% | 0.01 | 0.42 |
| 　RenderBridges | 8.329 | 43.9% | 17.27 | 327.23 |
| （枠の外） | 0.020 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 39 / 57、Candidates 134500 / 277368、ScanSamples 162 / 280、ChunksTested 2967 / 5673、BridgeCells 0 / 0、FilledRebuildCells 2484 / 9353、BeamRebuildCells 5357 / 19509

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 4.70 s: **158.0**（破片 0、GC0 0）: Weapons 147.9・Cut 54.4・SweepRun 47.8・SweepSetup 34.7・WeaponClash 20.8・SetupCandidates 17.5・RunScan 15.5・RunFront 10.8・RunPatch 10.3・RenderBridges 8.8・Control 6.8・Snap 6.3・RunCrush 5.6・SnapFind 5.2・BotRead 5.2・BotPerceive 4.5・Debris 4.4・RunOverlaps 3.2・PostStep 1.5・Bepu 1.4・RenderParts 1.3・MayTouch 1.1・DriveFighter 1.1・RunSeparate 1.1・ApplyControl 1.1・SnapStiffness 1.0・PostWounds 1.0・ClashPush 0.9・Bend 0.7・FighterStanding 0.6
- 種 1 の 24.30 s: **99.9**（破片 1、GC0 1）: Weapons 91.6・Cut 70.9・SweepSetup 8.4・RenderBridges 8.4・SweepRun 8.1・RunScan 6.4・Control 4.4・SetupCandidates 4.1・DriveFighter 2.9・ApplyControl 2.8・Bepu 2.6・RenderParts 1.3・WeaponClash 1.2・MayTouch 1.0・RunFront 1.0・FighterStanding 0.9・FighterDown 0.9・FighterStamina 0.8・After 0.6・ControlTail 0.6・Snap 0.5・BotRead 0.4・ClashPush 0.4・SnapFind 0.4・Perception 0.4
- 種 1 の 4.53 s: **78.5**（破片 0、GC0 0）: Weapons 73.2・SweepRun 45.1・RunFront 22.3・WeaponClash 21.5・RenderBridges 13.5・SweepSetup 10.7・RunPatch 8.7・RunSeparate 6.8・Snap 5.9・Cut 5.5・RunScan 5.2・RenderParts 5.1・SnapFind 4.9・SetupCandidates 4.5・Control 2.4・Bepu 2.4・MayTouch 2.0・DriveFighter 1.1・ApplyControl 1.1・SnapStiffness 0.9・BotRead 0.9・FighterStanding 0.8・RunCrush 0.7・ClashPush 0.6・Bend 0.6・FighterSplit 0.4
- 種 1 の 8.25 s: **76.9**（破片 0、GC0 0）: Weapons 65.8・WeaponClash 40.4・SweepRun 26.0・SweepSetup 19.5・SetupCandidates 13.5・Snap 12.0・SnapFind 10.3・RunScan 9.6・RunFront 9.6・RenderBridges 7.7・Control 7.4・Bepu 3.3・ClashPush 3.3・RunPatch 3.1・RunSeparate 2.9・RenderParts 2.8・DriveFighter 1.8・ApplyControl 1.8・SnapStiffness 1.7・FighterStamina 0.9・MayTouch 0.9・BotRead 0.8・SetupRelative 0.7・FighterStanding 0.7・ClashAfter 0.6
- 種 1 の 4.48 s: **76.2**（破片 0、GC0 0）: Weapons 66.7・RenderBridges 54.5・WeaponClash 32.8・SweepRun 25.2・Debris 18.3・ClashAfter 18.2・SweepSetup 16.9・RunPatch 11.4・Bepu 6.2・SetupCandidates 5.8・RunFront 5.7・RunScan 5.4・Snap 2.6・Control 2.6・SnapFind 2.0・DriveFighter 1.5・ApplyControl 1.5・RenderParts 1.2・FighterStanding 1.0・ClashPush 0.7・MayTouch 0.6・SnapStiffness 0.6・FighterSplit 0.5・RunSeparate 0.5・BotRead 0.4・Perception 0.4・FighterStamina 0.4・RunOverlaps 0.3
- 種 1 の 8.23 s: **74.8**（破片 0、GC0 0）: Weapons 66.0・WeaponClash 31.7・SweepSetup 27.5・RenderBridges 18.9・SweepRun 18.3・Snap 11.8・SetupCandidates 11.5・SnapFind 11.1・RunFront 7.4・RunPatch 4.4・RunScan 4.1・PreBepu 4.0・Control 2.0・Bepu 1.9・DriveFighter 1.3・ApplyControl 1.3・MayTouch 1.0・ClashPush 1.0・FighterStanding 1.0・Remember 1.0・RunSeparate 1.0・RunOverlaps 0.8・FighterSplit 0.6・SnapStiffness 0.6・RenderParts 0.5・Debris 0.4・Cut 0.3
- 種 1 の 7.97 s: **74.3**（破片 0、GC0 0）: Weapons 62.9・SweepSetup 23.5・WeaponClash 22.1・SweepRun 17.4・Snap 10.2・Control 9.9・SnapFind 9.8・SetupCandidates 9.3・DriveFighter 9.3・ApplyControl 9.2・FighterStamina 8.3・RunScan 8.3・RenderBridges 8.0・Cut 5.6・RunFront 5.1・RunCrush 1.8・RunPatch 1.2・Bepu 1.1・ClashPush 1.0・RenderParts 0.8・FighterStanding 0.7・RunSeparate 0.7・MayTouch 0.6・SnapStiffness 0.4
- 種 1 の 13.78 s: **74.3**（破片 0、GC0 0）: Bepu 46.9・Weapons 19.2・SweepSetup 11.3・Kicks 5.6・SetupCandidates 5.5・RenderBridges 5.3・SweepRun 4.3・MayTouch 2.4・Control 2.2・WeaponClash 1.5・RenderParts 1.4・RunScan 1.4・DriveFighter 1.3・ApplyControl 1.3・RunFront 1.2・RunSeparate 0.7・RunPatch 0.6・FighterDown 0.5・ClashPush 0.5・FighterStanding 0.4・BotRead 0.4・Snap 0.3
- 種 1 の 4.58 s: **73.1**（破片 0、GC0 0）: Weapons 70.1・SweepSetup 20.3・SweepRun 19.9・Cut 19.7・WeaponClash 18.8・SetupCandidates 12.4・RunPatch 8.1・RenderBridges 7.4・Snap 7.3・SnapFind 6.6・RunFront 6.0・RunScan 3.2・RenderParts 2.5・Control 1.6・Bepu 1.1・DriveFighter 1.1・ApplyControl 1.0・RunSeparate 0.8・RunCrush 0.8・ClashPush 0.6・SnapStiffness 0.6・MayTouch 0.6・FighterStanding 0.5・FighterStamina 0.4
- 種 1 の 11.55 s: **71.4**（破片 0、GC0 0）: Weapons 38.1・WeaponClash 25.9・Bepu 22.0・SweepRun 13.1・SweepSetup 10.0・RunFront 9.3・SetupCandidates 8.2・RenderBridges 6.5・FighterSplit 5.5・Snap 3.5・MayTouch 3.2・Control 2.8・SnapFind 2.6・Disconnect 2.2・ClashPush 1.7・RunScan 1.4・RunSeparate 1.0・DriveFighter 0.9・ApplyControl 0.9・RunPatch 0.8・Perception 0.8・SnapStiffness 0.7・BotRead 0.7・RenderParts 0.6・FighterStanding 0.5・SetupRelative 0.3・ClashAfter 0.3・PostStep 0.3

### c5 katana (MINIRING_SCAN=16 MINIRING_PASSES=2 MINIRING_SAMPLE=3 MINIRING_BRIDGES=0 MINIRING_HZ=120)
## perf katana（2026-11-25 22:13、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 22.5 s。物理 102・判断 73。
チェックサム（計測あり）: 5c9db53011bc0c28

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 9.22 | 7.68 | 18.50 | 28.99 | 295.47 | 7.7% | 107 / 3 |
| 描画の詰め（CPU） | 3.08 | 1.05 | 14.51 | 25.32 | 281.68 | | |
| 　うちボクセル（FillVoxels） | 2.93 | 0.99 | 14.23 | 24.73 | 206.48 | | |
| 飛んでいる破片の数（描画の粒） | 12 | 1 | 65 | 65 | 65 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.954 | 10.4% | 7.28 | 62.56 |
| 　Perception | 0.247 | 2.7% | 4.86 | 59.75 |
| 　BotRead | 0.023 | 0.3% | 0.20 | 3.13 |
| 　DriveFighter | 0.640 | 6.9% | 1.70 | 11.94 |
| 　ApplyControl | 0.631 | 6.8% | 1.62 | 10.82 |
| 　FighterStamina | 0.122 | 1.3% | 0.21 | 3.09 |
| 　FighterStanding | 0.296 | 3.2% | 0.61 | 4.82 |
| 　FighterDown | 0.129 | 1.4% | 0.61 | 8.97 |
| 　ControlTail | 0.027 | 0.3% | 0.09 | 1.39 |
| PreBepu | 0.038 | 0.4% | 0.08 | 1.37 |
| 　Remember | 0.018 | 0.2% | 0.03 | 1.29 |
| Bepu | 1.037 | 11.3% | 3.44 | 31.56 |
| CanvasImpacts | 0.072 | 0.8% | 4.46 | 76.74 |
| FistHits | 0.007 | 0.1% | 0.01 | 2.57 |
| Weapons | 6.826 | 74.0% | 35.09 | 112.78 |
| 　MayTouch | 0.054 | 0.6% | 0.21 | 1.48 |
| 　SweepSetup | 3.532 | 38.3% | 9.57 | 31.19 |
| 　SetupRelative | 0.044 | 0.5% | 0.08 | 1.32 |
| 　SetupFar | 0.043 | 0.5% | 0.20 | 1.95 |
| 　SetupCandidates | 2.168 | 23.5% | 6.72 | 30.82 |
| 　SweepRun | 2.571 | 27.9% | 21.76 | 91.92 |
| 　RunScan | 1.194 | 12.9% | 6.18 | 26.40 |
| 　RunFront | 0.678 | 7.4% | 3.80 | 20.33 |
| 　RunOverlaps | 0.014 | 0.2% | 0.96 | 11.49 |
| 　RunPatch | 0.284 | 3.1% | 3.65 | 27.65 |
| 　RunHold | 0.007 | 0.1% | 0.28 | 4.23 |
| 　RunCrush | 0.162 | 1.8% | 5.27 | 82.16 |
| 　RunCut | 0.001 | 0.0% | 0.10 | 1.41 |
| 　RunSeparate | 0.126 | 1.4% | 0.32 | 10.32 |
| 　RunMass | 0.004 | 0.0% | 0.33 | 2.75 |
| 　Snap | 0.365 | 4.0% | 0.88 | 12.81 |
| 　SnapStiffness | 0.075 | 0.8% | 0.25 | 10.51 |
| 　SnapFind | 0.285 | 3.1% | 0.62 | 6.46 |
| 　Bend | 0.029 | 0.3% | 0.89 | 14.31 |
| 　Cut | 0.042 | 0.5% | 0.78 | 9.65 |
| 　Debris | 0.002 | 0.0% | 0.03 | 0.30 |
| 　WeaponClash | 0.155 | 1.7% | 3.83 | 31.43 |
| 　ClashAfter | 0.000 | 0.0% | 0.00 | 0.39 |
| 　ClashPush | 0.001 | 0.0% | 0.01 | 0.23 |
| Disconnect | 0.006 | 0.1% | 0.15 | 2.49 |
| Kicks | 0.004 | 0.0% | 0.01 | 0.42 |
| Elbows | 0.003 | 0.0% | 0.00 | 0.22 |
| FighterSplit | 0.059 | 0.6% | 0.70 | 5.05 |
| After | 0.018 | 0.2% | 0.07 | 1.55 |
| PostStep | 0.179 | 1.9% | 15.95 | 284.20 |
| 　PostDebris | 0.011 | 0.1% | 0.03 | 1.24 |
| 　PostWounds | 0.002 | 0.0% | 0.04 | 0.63 |
| 　PostSwings | 0.004 | 0.0% | 0.01 | 0.18 |
| 　PostRound | 0.161 | 1.7% | 15.79 | 284.20 |
| 　RenderParts | 0.266 | 2.9% | 2.35 | 24.93 |
| 　RenderFragments | 0.002 | 0.0% | 0.00 | 0.68 |
| 　RenderBridges | 2.620 | 28.4% | 22.90 | 167.02 |
| （枠の外） | 0.014 | 0.2% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 20 / 18、Candidates 70453 / 68986、ScanSamples 51 / 109、ChunksTested 1616 / 1992、BridgeCells 0 / 0、FilledRebuildCells 356 / 0、BeamRebuildCells 735 / 392

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 24.87 s: **295.5**（破片 0、GC0 5）: PostStep 284.2・PostRound 284.2・RenderBridges 10.3・Weapons 9.6・SweepSetup 5.4・SweepRun 3.4・SetupCandidates 2.5・RunFront 1.3・RunScan 1.1・Bepu 0.8・Control 0.8・RunPatch 0.7・RenderParts 0.5・DriveFighter 0.5・ApplyControl 0.5・Snap 0.4・SnapFind 0.3
- 種 1 の 1.92 s: **178.9**（破片 0、GC0 0）: Weapons 112.8・SweepRun 75.4・Control 62.6・Perception 59.7・RunPatch 27.7・Bend 14.3・RenderBridges 13.5・RunFront 12.1・RunOverlaps 11.5・SweepSetup 10.1・Cut 9.7・WeaponClash 6.5・RunScan 6.4・SetupCandidates 5.8・RunHold 4.2・Bepu 2.8・RunMass 2.0・Snap 1.8・BotRead 1.5・RunCut 1.4・DriveFighter 1.2・SnapFind 1.2・RenderParts 1.2・ApplyControl 1.1・RunSeparate 1.1・RunCrush 0.8・FighterStanding 0.8・SnapStiffness 0.6
- 種 1 の 2.27 s: **103.3**（破片 0、GC0 0）: Weapons 99.3・SweepRun 91.9・RunCrush 82.2・RenderBridges 16.9・SweepSetup 3.3・RunPatch 2.8・RunScan 2.6・Snap 2.1・RenderParts 2.0・RunFront 1.9・Control 1.7・SetupCandidates 1.6・SnapStiffness 1.2・Bepu 1.2・DriveFighter 1.2・ApplyControl 1.2・Bend 1.0・RunMass 1.0・SnapFind 0.8・RunOverlaps 0.6・Perception 0.4・CanvasImpacts 0.4・FighterStanding 0.4・Cut 0.4・FighterDown 0.4
- 種 1 の 2.10 s: **96.7**（破片 0、GC0 0）: CanvasImpacts 76.7・RenderBridges 25.3・Weapons 9.4・SweepSetup 6.1・FighterSplit 5.1・SetupCandidates 3.5・SweepRun 2.6・RunScan 2.6・Control 1.9・PostStep 1.9・Bepu 1.5・DriveFighter 1.3・ApplyControl 1.3・FighterStanding 0.8・RenderParts 0.8・PostWounds 0.6・Perception 0.5
- 種 1 の 2.20 s: **60.8**（破片 0、GC0 0）: Bepu 31.6・RenderBridges 18.7・Weapons 15.8・Control 12.3・DriveFighter 11.9・ApplyControl 10.8・SweepRun 9.1・FighterDown 9.0・SweepSetup 5.5・RunScan 5.2・SetupCandidates 2.9・RunPatch 1.9・RunFront 1.5・FighterStanding 1.1・After 0.4・Snap 0.4・RenderParts 0.3
- 種 1 の 1.90 s: **50.0**（破片 0、GC0 0）: Weapons 47.0・SweepRun 30.3・RenderBridges 17.7・SweepSetup 14.3・RunScan 12.2・SetupCandidates 11.1・RunPatch 9.3・RunFront 3.4・RunOverlaps 3.3・Control 1.7・DriveFighter 1.4・ApplyControl 1.3・Bepu 1.0・FighterStanding 1.0・RenderParts 0.9・WeaponClash 0.8・Cut 0.7・Snap 0.6・RunSeparate 0.5・SnapFind 0.4
- 種 1 の 6.00 s: **40.4**（破片 0、GC0 1）: Weapons 38.5・SweepSetup 24.9・SetupCandidates 18.7・SweepRun 12.4・RunScan 10.0・RenderBridges 2.3・RunFront 1.2・RenderParts 1.0・Bepu 0.8・Control 0.8・Snap 0.6・DriveFighter 0.5・ApplyControl 0.5・RunSeparate 0.4・RunPatch 0.4
- 種 1 の 2.25 s: **39.5**（破片 0、GC0 0）: Weapons 34.1・RenderBridges 28.7・SweepRun 22.7・RunCrush 5.9・SweepSetup 5.5・RunFront 5.3・RunScan 4.2・RunPatch 3.5・SetupCandidates 2.9・Control 2.8・Snap 2.1・RenderParts 1.8・SnapFind 1.5・Bepu 1.5・DriveFighter 1.5・ApplyControl 1.4・Cut 1.2・RunOverlaps 1.2・FighterStanding 0.7・ControlTail 0.7・Perception 0.6・SnapStiffness 0.5・RunSeparate 0.4・FighterSplit 0.4・CanvasImpacts 0.4・RunHold 0.3・FighterDown 0.3
- 種 1 の 1.03 s: **37.7**（破片 0、GC0 0）: Weapons 29.3・WeaponClash 29.0・SweepRun 26.4・RunScan 26.4・RenderBridges 9.6・Bepu 5.2・Control 2.9・SweepSetup 2.6・DriveFighter 1.5・ApplyControl 1.5・SetupFar 1.5・Perception 1.2・FighterStanding 1.0・SetupCandidates 0.9
- 種 1 の 2.37 s: **36.7**（破片 0、GC0 0）: Weapons 32.2・SweepRun 18.7・RenderBridges 14.1・SweepSetup 9.9・RunScan 8.5・RunFront 7.4・SetupCandidates 6.0・RenderParts 3.2・Bepu 2.6・Snap 2.5・SnapFind 2.1・Control 1.5・RunPatch 1.3・DriveFighter 1.1・ApplyControl 1.1・RunSeparate 0.5・RunCrush 0.4・FighterStanding 0.4・Perception 0.4・SnapStiffness 0.3

### base2 royale ()
## perf royale（2026-11-25 22:15、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 96.9 s。物理 102・判断 73。
チェックサム（計測あり）: d56f241a235b3cd4

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 46.24 | 34.19 | 112.35 | 189.06 | 639.02 | 88.8% | 734 / 1 |
| 描画の詰め（CPU） | 6.73 | 4.28 | 20.18 | 36.69 | 293.01 | | |
| 　うちボクセル（FillVoxels） | 6.33 | 3.92 | 19.76 | 35.86 | 220.00 | | |
| 飛んでいる破片の数（描画の粒） | 331 | 330 | 851 | 900 | 919 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 4.211 | 9.1% | 22.93 | 85.00 |
| 　Perception | 0.528 | 1.1% | 1.08 | 26.21 |
| 　BotRead | 0.768 | 1.7% | 6.17 | 79.17 |
| 　BotPerceive | 0.149 | 0.3% | 1.02 | 15.59 |
| 　BotRead2 | 0.075 | 0.2% | 0.05 | 6.31 |
| 　BotChoose | 0.063 | 0.1% | 0.04 | 73.44 |
| 　BotExecute | 0.035 | 0.1% | 0.03 | 6.41 |
| 　DriveFighter | 2.470 | 5.3% | 13.37 | 47.99 |
| 　ApplyControl | 2.381 | 5.1% | 13.23 | 47.19 |
| 　FighterStamina | 0.522 | 1.1% | 3.10 | 34.61 |
| 　FighterStanding | 1.265 | 2.7% | 9.20 | 45.85 |
| 　FighterDown | 0.238 | 0.5% | 0.15 | 9.41 |
| 　ControlTail | 0.236 | 0.5% | 1.80 | 24.94 |
| PreBepu | 0.161 | 0.3% | 0.15 | 13.50 |
| 　Remember | 0.101 | 0.2% | 0.09 | 13.45 |
| Bepu | 4.084 | 8.8% | 20.16 | 72.00 |
| CanvasImpacts | 0.094 | 0.2% | 0.32 | 23.49 |
| FistHits | 0.046 | 0.1% | 0.06 | 1.35 |
| Weapons | 36.184 | 78.3% | 188.83 | 613.29 |
| 　MayTouch | 1.789 | 3.9% | 4.38 | 48.08 |
| 　SweepSetup | 9.982 | 21.6% | 55.66 | 248.27 |
| 　SetupRelative | 0.389 | 0.8% | 3.64 | 36.86 |
| 　SetupFar | 0.280 | 0.6% | 1.39 | 16.43 |
| 　SetupCandidates | 6.902 | 14.9% | 38.40 | 168.51 |
| 　SweepRun | 11.039 | 23.9% | 64.19 | 162.29 |
| 　RunScan | 5.623 | 12.2% | 29.46 | 77.43 |
| 　RunFront | 1.706 | 3.7% | 11.83 | 52.05 |
| 　RunOverlaps | 0.429 | 0.9% | 1.47 | 18.27 |
| 　RunPatch | 1.870 | 4.0% | 9.72 | 46.61 |
| 　RunHold | 0.024 | 0.1% | 0.03 | 8.34 |
| 　RunCrush | 0.438 | 0.9% | 3.80 | 45.75 |
| 　RunCut | 0.036 | 0.1% | 2.00 | 19.15 |
| 　RunSeparate | 0.393 | 0.8% | 1.60 | 67.03 |
| 　RunMass | 0.027 | 0.1% | 0.04 | 6.74 |
| 　Snap | 2.651 | 5.7% | 10.99 | 57.14 |
| 　SnapStiffness | 0.438 | 0.9% | 1.07 | 14.96 |
| 　SnapFind | 2.184 | 4.7% | 9.89 | 49.68 |
| 　SnapSplit | 0.001 | 0.0% | 0.00 | 2.32 |
| 　Bend | 0.257 | 0.6% | 2.08 | 27.76 |
| 　Cut | 0.390 | 0.8% | 2.57 | 35.49 |
| 　Debris | 0.072 | 0.2% | 0.05 | 57.31 |
| 　WeaponClash | 6.204 | 13.4% | 35.98 | 189.58 |
| 　ClashAfter | 0.058 | 0.1% | 0.04 | 57.36 |
| 　ClashPush | 1.891 | 4.1% | 8.85 | 45.34 |
| 　Bridges | 7.359 | 15.9% | 30.24 | 125.95 |
| 　BridgeRefresh | 4.657 | 10.1% | 20.29 | 107.33 |
| Disconnect | 0.026 | 0.1% | 0.04 | 7.32 |
| Kicks | 0.892 | 1.9% | 4.39 | 22.55 |
| Elbows | 0.018 | 0.0% | 0.02 | 16.64 |
| FighterSplit | 0.327 | 0.7% | 6.07 | 92.04 |
| After | 0.045 | 0.1% | 0.05 | 3.67 |
| PostStep | 0.115 | 0.2% | 0.49 | 8.82 |
| 　PostDebris | 0.083 | 0.2% | 0.47 | 8.78 |
| 　PostWounds | 0.003 | 0.0% | 0.00 | 0.27 |
| 　PostSwings | 0.006 | 0.0% | 0.01 | 1.07 |
| 　PostRound | 0.022 | 0.0% | 0.01 | 7.21 |
| 　RenderParts | 0.686 | 1.5% | 1.87 | 26.87 |
| 　RenderFragments | 0.023 | 0.0% | 1.94 | 34.89 |
| 　RenderBridges | 5.481 | 11.9% | 11.23 | 185.61 |
| （枠の外） | 0.032 | 0.1% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 137 / 187、Candidates 106391 / 173229、ScanSamples 699 / 1180、ChunksTested 14581 / 25689、BridgeCells 4149 / 5583、FilledRebuildCells 2731 / 3220、BeamRebuildCells 7013 / 7796

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 11.37 s: **639.0**（破片 0、GC0 1）: Weapons 613.3・SweepSetup 248.3・WeaponClash 189.6・SetupCandidates 168.5・SweepRun 121.5・RunScan 59.6・RunCrush 45.8・ClashPush 45.3・Bend 27.8・Bridges 26.8・BridgeRefresh 22.8・Bepu 13.2・RenderBridges 9.8・Control 9.8・Snap 7.9・DriveFighter 6.4・ApplyControl 6.3・RunFront 5.7・MayTouch 5.3・SnapFind 4.2・SnapStiffness 3.7・RunSeparate 3.5・FighterStanding 3.0・SetupRelative 2.6・RenderParts 2.4・RunPatch 2.2・Kicks 2.1・BotRead 1.8・RunOverlaps 1.8・FighterStamina 1.8・Cut 0.9・Perception 0.9・ControlTail 0.6・SetupFar 0.5
- 種 1 の 11.38 s: **327.3**（破片 0、GC0 0）: Weapons 294.7・SweepRun 162.3・RenderBridges 79.6・SweepSetup 77.5・SetupCandidates 65.6・WeaponClash 59.9・RunScan 58.8・RunFront 44.4・RenderFragments 34.9・Control 24.0・Bridges 22.2・RunPatch 20.5・RunCut 19.1・BridgeRefresh 18.3・RenderParts 17.0・ClashPush 12.6・DriveFighter 9.4・ApplyControl 9.3・BotRead 9.0・FighterStanding 7.7・RunCrush 6.4・Cut 6.1・ControlTail 5.0・RunOverlaps 4.9・Kicks 3.8・Bepu 3.8・RunSeparate 3.7・MayTouch 3.2・SetupRelative 3.1・Snap 2.9・Bend 2.6・SnapFind 2.3・SetupFar 0.9・FighterStamina 0.7・FighterSplit 0.6・SnapStiffness 0.5・Perception 0.4
- 種 1 の 4.05 s: **264.0**（破片 0、GC0 1）: Weapons 211.4・Bridges 125.9・BridgeRefresh 107.3・Bepu 36.3・WeaponClash 34.9・SweepRun 34.7・RunScan 18.1・SweepSetup 18.0・ClashPush 17.6・SetupCandidates 16.3・Control 15.7・DriveFighter 14.4・ApplyControl 14.3・RunSeparate 13.3・FighterStanding 11.4・Cut 10.4・RenderBridges 3.9・Snap 1.5・RunPatch 1.4・FighterStamina 1.3・MayTouch 1.3・RunFront 1.3・SnapFind 1.3・RenderParts 0.7・Bend 0.5・BotRead 0.5・Perception 0.4・FighterSplit 0.3
- 種 1 の 11.35 s: **244.0**（破片 0、GC0 0）: Weapons 226.1・SweepRun 101.0・RunScan 76.7・SweepSetup 58.0・SetupCandidates 41.8・WeaponClash 36.4・Bridges 24.8・BridgeRefresh 17.5・Snap 16.6・SnapFind 16.3・ClashPush 16.0・RunFront 15.0・RenderBridges 13.8・Control 7.0・MayTouch 6.9・Bepu 6.2・RunPatch 5.8・Kicks 4.0・DriveFighter 3.7・ApplyControl 3.6・BotRead 1.9・FighterStanding 1.6・RunCrush 1.3・RenderParts 1.0・Bend 0.8・ControlTail 0.8・Cut 0.7・SetupRelative 0.6・SetupFar 0.5・FighterStamina 0.5・Perception 0.4・FighterSplit 0.3
- 種 1 の 13.00 s: **230.1**（破片 0、GC0 1）: Weapons 146.0・SweepRun 75.5・Bepu 52.2・Bridges 32.2・Control 31.2・DriveFighter 29.3・ApplyControl 29.3・FighterStanding 28.5・SweepSetup 25.6・SetupCandidates 17.4・RunPatch 14.3・RenderBridges 8.0・BridgeRefresh 6.3・WeaponClash 5.8・ClashPush 5.2・MayTouch 4.2・RunScan 2.8・RenderParts 2.6・Snap 1.9・RunFront 1.7・SnapFind 1.6・Perception 1.2・RunCrush 1.2・Cut 0.6・BotRead 0.4・FighterStamina 0.4・SnapStiffness 0.3・RunSeparate 0.3
- 種 1 の 10.65 s: **225.9**（破片 0、GC0 0）: Weapons 147.5・SweepSetup 74.0・Bepu 43.6・SetupRelative 36.9・SweepRun 27.9・Control 27.8・RunScan 23.7・DriveFighter 23.3・ApplyControl 23.2・FighterStanding 22.0・Bridges 14.1・SetupCandidates 13.9・BridgeRefresh 10.6・Cut 7.7・Kicks 6.3・WeaponClash 5.5・RenderBridges 4.9・MayTouch 3.8・ClashPush 2.9・Snap 2.3・SnapFind 1.9・RunFront 1.4・RunPatch 1.3・RenderParts 0.8・BotRead 0.6・FighterStamina 0.6・ControlTail 0.5・RunCrush 0.5・Perception 0.4・SetupFar 0.4・RunSeparate 0.4・SnapStiffness 0.4
- 種 1 の 11.65 s: **224.0**（破片 0、GC0 1）: Weapons 158.7・SweepRun 79.3・SweepSetup 52.4・Control 50.7・RunScan 48.2・DriveFighter 36.2・ApplyControl 36.2・FighterStamina 34.6・RunFront 29.1・SetupCandidates 27.2・SetupFar 16.4・Bridges 16.2・ControlTail 12.7・WeaponClash 12.1・BridgeRefresh 8.1・Bepu 7.3・RenderBridges 5.8・Kicks 5.7・ClashPush 4.0・RenderParts 2.2・MayTouch 2.0・Snap 1.2・FighterStanding 1.2・SnapFind 1.0・Perception 1.0・FighterSplit 1.0・RunPatch 0.8・SetupRelative 0.8・BotRead 0.6・RunSeparate 0.5・RunCrush 0.4
- 種 1 の 7.55 s: **216.2**（破片 0、GC0 0）: FighterSplit 92.0・Weapons 90.2・SweepRun 34.4・Control 26.5・SweepSetup 23.1・SetupCandidates 17.6・RunScan 16.8・Bridges 16.5・BotRead 16.1・BotPerceive 15.6・WeaponClash 12.5・Snap 11.3・SnapFind 10.2・RunPatch 7.6・DriveFighter 7.3・BridgeRefresh 7.0・Bepu 6.9・ApplyControl 6.5・RunFront 5.4・RenderBridges 3.8・Perception 2.6・MayTouch 2.2・ClashPush 2.1・FighterStanding 1.9・RunOverlaps 1.7・RunSeparate 1.5・SnapStiffness 1.1・Cut 0.6・RunCrush 0.5・SetupRelative 0.4・FighterStamina 0.4・SetupFar 0.4・RenderParts 0.3
- 種 1 の 4.48 s: **214.4**（破片 0、GC0 0）: Weapons 198.1・SweepRun 76.9・WeaponClash 64.6・Snap 57.1・SnapFind 49.7・RunScan 44.2・Bridges 40.2・SweepSetup 36.9・SetupCandidates 27.0・RunPatch 18.5・BridgeRefresh 16.2・Control 11.4・DriveFighter 8.5・ApplyControl 8.4・SnapStiffness 7.4・FighterStanding 7.3・RunFront 5.0・RunOverlaps 4.9・RenderBridges 4.1・Bepu 3.9・MayTouch 2.1・RunSeparate 2.1・Perception 2.0・ClashPush 1.5・Cut 1.0・BotRead 0.7・Bend 0.6・FighterStamina 0.6・RenderParts 0.5・Debris 0.5・SetupRelative 0.4・Disconnect 0.4・SetupFar 0.4
- 種 1 の 6.63 s: **212.1**（破片 0、GC0 0）: Weapons 192.1・SweepSetup 116.8・SetupCandidates 110.3・WeaponClash 91.4・SweepRun 11.7・Control 10.8・Bridges 10.1・MayTouch 8.0・RunScan 7.0・Snap 5.9・Perception 5.5・SnapFind 5.5・CanvasImpacts 4.6・DriveFighter 4.4・RenderBridges 4.3・ApplyControl 4.3・Bepu 4.1・FighterStanding 3.1・BridgeRefresh 2.9・ClashPush 2.8・RunFront 2.7・RunPatch 0.9・BotRead 0.6・SetupFar 0.5・FighterStamina 0.5・RunSeparate 0.5・RenderParts 0.4・SnapStiffness 0.4・FighterDown 0.3

### base2 katana ()
## perf katana（2026-11-25 22:16、GRANDEDESKTOP）

種 1 × 最大 30 秒、1800 フレーム（1 フレーム = `Step(1/60)` = 240 Hz の 4 歩）。壁時計 15.6 s。物理 102・判断 73。
チェックサム（計測あり）: 74207d56c9b1922

| フレーム [ms] | 平均 | 中央値 | p95 | p99 | 最大 | 16.7 ms 超の割合 | GC0 / GC2 |
|---|---:|---:|---:|---:|---:|---:|---|
| 物理 | 7.19 | 4.47 | 20.35 | 48.62 | 108.56 | 6.9% | 150 / 1 |
| 描画の詰め（CPU） | 1.38 | 0.53 | 5.63 | 8.71 | 121.11 | | |
| 　うちボクセル（FillVoxels） | 1.32 | 0.50 | 5.49 | 8.60 | 89.10 | | |
| 飛んでいる破片の数（描画の粒） | 19 | 1 | 86 | 141 | 156 | | |

処理ごと（ms/フレーム。字下げは外側の枠に含まれる内訳。「重い 1%」は合計の重い上位 1% のフレームの平均）:

| 処理 | 平均 | 割合 | 重い 1% の平均 | 最大 |
|---|---:|---:|---:|---:|
| Control | 0.949 | 13.2% | 3.14 | 25.11 |
| 　Perception | 0.106 | 1.5% | 0.24 | 0.73 |
| 　BotRead | 0.022 | 0.3% | 0.07 | 1.43 |
| 　DriveFighter | 0.786 | 10.9% | 2.75 | 24.88 |
| 　ApplyControl | 0.710 | 9.9% | 1.40 | 9.55 |
| 　FighterStamina | 0.102 | 1.4% | 0.29 | 1.31 |
| 　FighterStanding | 0.302 | 4.2% | 0.82 | 3.30 |
| 　FighterDown | 0.217 | 3.0% | 0.00 | 4.61 |
| 　ControlTail | 0.023 | 0.3% | 0.06 | 0.31 |
| PreBepu | 0.032 | 0.4% | 0.09 | 0.40 |
| 　Remember | 0.014 | 0.2% | 0.04 | 0.17 |
| Bepu | 1.108 | 15.4% | 2.88 | 23.22 |
| CanvasImpacts | 0.030 | 0.4% | 0.06 | 11.09 |
| FistHits | 0.006 | 0.1% | 0.02 | 0.10 |
| Weapons | 4.987 | 69.3% | 52.90 | 102.19 |
| 　MayTouch | 0.076 | 1.1% | 0.19 | 0.45 |
| 　SweepSetup | 1.069 | 14.9% | 8.73 | 26.55 |
| 　SetupRelative | 0.036 | 0.5% | 0.19 | 0.29 |
| 　SetupFar | 0.036 | 0.5% | 0.38 | 1.62 |
| 　SetupCandidates | 0.727 | 10.1% | 5.73 | 22.36 |
| 　SweepRun | 2.020 | 28.1% | 21.49 | 40.12 |
| 　RunScan | 1.234 | 17.2% | 10.48 | 20.72 |
| 　RunFront | 0.308 | 4.3% | 2.90 | 14.25 |
| 　RunOverlaps | 0.035 | 0.5% | 1.33 | 5.94 |
| 　RunPatch | 0.225 | 3.1% | 4.74 | 18.25 |
| 　RunHold | 0.005 | 0.1% | 0.16 | 1.22 |
| 　RunCrush | 0.085 | 1.2% | 0.14 | 25.82 |
| 　RunCut | 0.002 | 0.0% | 0.11 | 0.68 |
| 　RunSeparate | 0.049 | 0.7% | 0.32 | 4.37 |
| 　RunMass | 0.002 | 0.0% | 0.11 | 0.35 |
| 　Snap | 0.366 | 5.1% | 1.98 | 5.35 |
| 　SnapStiffness | 0.069 | 1.0% | 0.67 | 4.30 |
| 　SnapFind | 0.293 | 4.1% | 1.27 | 3.01 |
| 　Bend | 0.051 | 0.7% | 0.98 | 18.43 |
| 　Cut | 0.050 | 0.7% | 2.66 | 41.69 |
| 　Debris | 0.008 | 0.1% | 0.51 | 7.27 |
| 　WeaponClash | 0.496 | 6.9% | 7.45 | 31.44 |
| 　ClashAfter | 0.008 | 0.1% | 0.42 | 7.32 |
| 　ClashPush | 0.073 | 1.0% | 1.31 | 23.13 |
| 　Bridges | 1.106 | 15.4% | 15.36 | 23.41 |
| 　BridgeRefresh | 0.897 | 12.5% | 13.75 | 19.35 |
| Disconnect | 0.004 | 0.1% | 0.03 | 1.31 |
| Kicks | 0.003 | 0.0% | 0.01 | 0.33 |
| Elbows | 0.003 | 0.0% | 0.01 | 0.47 |
| FighterSplit | 0.037 | 0.5% | 0.20 | 2.59 |
| After | 0.012 | 0.2% | 0.04 | 0.97 |
| PostStep | 0.011 | 0.2% | 0.10 | 0.98 |
| 　PostDebris | 0.005 | 0.1% | 0.03 | 0.08 |
| 　PostWounds | 0.001 | 0.0% | 0.00 | 0.24 |
| 　PostSwings | 0.002 | 0.0% | 0.01 | 0.05 |
| 　PostRound | 0.002 | 0.0% | 0.01 | 0.87 |
| 　RenderParts | 0.138 | 1.9% | 0.38 | 13.52 |
| 　RenderFragments | 0.002 | 0.0% | 0.00 | 0.40 |
| 　RenderBridges | 1.152 | 16.0% | 6.23 | 69.07 |
| （枠の外） | 0.011 | 0.2% | | |

数（フレームあたりの平均 / 重い 1% の平均）: NearSweeps 21 / 66、Candidates 17507 / 65672、ScanSamples 254 / 1336、ChunksTested 4490 / 29401、BridgeCells 336 / 2489、FilledRebuildCells 447 / 8890、BeamRebuildCells 961 / 20079

重いフレームの上位 10（ms、0.3 ms 以上の枠だけ）:

- 種 1 の 2.10 s: **108.6**（破片 1、GC0 0）: Weapons 102.2・Cut 41.7・SweepRun 24.9・Bridges 23.4・BridgeRefresh 19.3・RunScan 14.1・RenderBridges 12.8・SweepSetup 11.3・SetupCandidates 6.3・RunPatch 5.3・Control 3.6・DriveFighter 3.3・WeaponClash 3.0・ApplyControl 2.8・RunFront 2.2・Snap 1.6・Bepu 1.6・FighterStanding 1.2・SnapFind 1.2・RunOverlaps 1.1・FighterSplit 0.8・Bend 0.6・RunSeparate 0.5・SetupFar 0.5・RenderParts 0.4・SnapStiffness 0.4
- 種 1 の 3.17 s: **71.4**（破片 1、GC0 0）: Weapons 44.9・WeaponClash 25.6・Control 25.1・DriveFighter 24.9・ClashPush 23.1・SweepRun 13.1・Bridges 5.5・BridgeRefresh 5.3・RenderBridges 5.2・RunPatch 4.5・RunScan 4.3・SweepSetup 2.3・RunOverlaps 1.7・ApplyControl 1.7・SetupCandidates 1.7・FighterStanding 1.4・Bepu 1.1・RunFront 0.4・RenderParts 0.3・RunHold 0.3
- 種 1 の 2.15 s: **68.2**（破片 1、GC0 0）: Weapons 64.6・SweepSetup 26.5・SetupCandidates 22.4・SweepRun 18.3・Bridges 17.9・BridgeRefresh 16.0・RunScan 11.5・RenderBridges 6.0・RunPatch 3.0・WeaponClash 2.8・RunFront 1.8・Bepu 1.6・Control 1.6・Snap 1.3・DriveFighter 1.3・ApplyControl 1.2・SnapFind 0.9・FighterStanding 0.7・RunOverlaps 0.4・Bend 0.4・SetupFar 0.4・SnapStiffness 0.4・RunSeparate 0.3・FighterStamina 0.3・Cut 0.3
- 種 1 の 2.05 s: **64.9**（破片 0、GC0 0）: Weapons 61.4・SweepRun 20.0・Bridges 17.3・BridgeRefresh 14.8・Bend 11.9・RunScan 10.1・SweepSetup 7.5・RenderBridges 5.9・WeaponClash 5.4・Snap 5.3・RunPatch 5.3・SetupCandidates 4.4・SnapStiffness 4.3・RunFront 1.9・Bepu 1.5・Control 1.5・DriveFighter 1.0・RunOverlaps 1.0・SnapFind 1.0・ApplyControl 1.0・FighterStanding 0.7・Cut 0.6・RenderParts 0.4・SetupFar 0.4・Perception 0.3
- 種 1 の 1.90 s: **64.1**（破片 0、GC0 0）: Weapons 38.4・Bepu 23.2・SweepRun 18.6・Bridges 12.0・BridgeRefresh 11.8・RenderBridges 7.1・WeaponClash 6.9・RunScan 6.3・RunPatch 4.7・RunFront 4.1・SweepSetup 3.7・Snap 2.6・Control 2.1・SetupCandidates 1.9・SnapFind 1.8・DriveFighter 1.5・ApplyControl 1.5・RunOverlaps 1.2・RunSeparate 1.0・FighterStanding 0.8・SnapStiffness 0.7・SetupFar 0.4・Perception 0.4・RunCrush 0.4・FighterStamina 0.4
- 種 1 の 2.12 s: **61.9**（破片 1、GC0 0）: Weapons 58.5・SweepRun 25.6・Bridges 21.3・BridgeRefresh 17.4・RunScan 17.0・SweepSetup 11.9・SetupCandidates 6.6・RenderBridges 5.9・RunPatch 4.2・WeaponClash 3.0・RunFront 2.2・Bepu 1.6・Control 1.5・Snap 1.3・DriveFighter 1.1・ApplyControl 1.1・SnapFind 0.9・RunOverlaps 0.7・FighterStanding 0.6・SetupFar 0.5・Cut 0.4・RenderParts 0.4・SnapStiffness 0.3
- 種 1 の 2.13 s: **58.6**（破片 1、GC0 0）: Weapons 54.8・SweepRun 23.7・Bridges 19.0・BridgeRefresh 15.7・RunScan 12.9・SweepSetup 10.8・RenderBridges 7.6・SetupCandidates 6.2・RunPatch 5.4・RunFront 2.6・WeaponClash 2.6・Control 1.8・Snap 1.7・Bepu 1.6・DriveFighter 1.4・ApplyControl 1.3・SnapFind 1.2・RunOverlaps 0.9・RenderParts 0.8・FighterStanding 0.8・Bend 0.6・RunSeparate 0.5・SnapStiffness 0.4・SetupFar 0.4・Cut 0.4・FighterStamina 0.3
- 種 1 の 2.03 s: **57.6**（破片 0、GC0 1）: Weapons 53.8・SweepRun 29.0・RunScan 14.9・Bridges 14.2・BridgeRefresh 13.9・WeaponClash 9.0・RenderBridges 7.5・RunPatch 6.9・SweepSetup 5.8・RunOverlaps 3.1・SetupCandidates 2.9・Snap 1.9・RunFront 1.7・Bepu 1.7・Control 1.5・SnapFind 1.4・Cut 1.1・DriveFighter 1.0・ApplyControl 1.0・FighterStanding 0.7・Bend 0.6・Debris 0.6・SnapStiffness 0.5・RunMass 0.4・RenderParts 0.3・Perception 0.3
- 種 1 の 2.17 s: **55.1**（破片 1、GC0 1）: Weapons 50.9・SweepRun 19.9・Bridges 19.3・BridgeRefresh 15.7・RunScan 11.2・RenderBridges 10.0・SweepSetup 9.4・SetupCandidates 5.5・RunPatch 4.6・WeaponClash 2.3・Control 2.0・RunFront 1.8・DriveFighter 1.6・Bepu 1.6・ApplyControl 1.6・Snap 1.5・SnapFind 1.1・FighterStanding 0.9・RunOverlaps 0.7・Bend 0.7・Cut 0.5・FighterStamina 0.4・SetupFar 0.4・SnapStiffness 0.4
- 種 1 の 2.47 s: **54.1**（破片 1、GC0 0）: Weapons 50.7・SweepRun 26.3・Bridges 16.5・BridgeRefresh 14.7・RunFront 14.3・RunScan 9.8・SweepSetup 6.8・SetupCandidates 5.0・RenderBridges 3.2・Control 1.6・Bepu 1.5・RunPatch 1.2・DriveFighter 1.1・ApplyControl 1.1・Snap 0.9・SnapFind 0.7・FighterStanding 0.6・SetupFar 0.4・WeaponClash 0.4・FighterStamina 0.3・Perception 0.3・RenderParts 0.3

