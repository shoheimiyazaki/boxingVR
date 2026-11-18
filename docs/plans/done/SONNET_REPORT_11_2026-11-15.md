# 完了 — 作業 11 報告（刀が当たった歩の重さ）

詳細な表・内訳・提案: `docs/arena/gen13_step_cost.md`。

- 固定 5 シードの最終チェックサム: 前後で一致（`bf23ffc3e767a185 800bae1b07b6bec2 ad6c5bee20893259 305461fa1b33617f b53759b4e5b34cc3`）。結果を変える変更は入れていない（PhysicsVersion は上げていない）。
- 全テスト（Probe 除く）: 552 件すべて成功（8 分 22 秒）。
- 240 Hz の 1 歩（触れた歩）: 中央値 3.01 → 1.76 ms、p95 6.6 → 3.7 ms、最大 約 25 → 15 ms。触れなかった歩も 中央値 1.40 → 1.18。
- 直した所: FieldFront を辞書から平らな配列へ / VoxelBending の剛性・断面の覚え込み / 候補セルの Sort をやめ bitset / 走査のチャンク・まとめ球での足切り。
- 入れなかった提案（掃き直し回数・候補定義の変更・Control の軽量化・標本間隔）は表に記載。
- 確かめていない所: 動画・見た目の確認はしていない（結果ビット一致のため）。コードレビュー用エージェントは使わず自己点検のみ。測定は別の Codex が同じ機で動いていた時間帯で ±10〜15 % ゆれる。旧 `KatanaPerformanceProbe`（1/60 s 更新単位）の前後比較はしていない。
- 追加したもの: `src/Aigis.Foundation/Voxels/StepProfile.cs`（計測の枠。切っていれば何もしない）、`tests/MiniRing.Tests/KatanaStepCostProbe.cs`、BoxingGame / VoxelContact に枠の呼び出し。README の件数は未更新（Probe は件数に入らない）。
