完了

# 作業指示 15（P0b）報告 — 対にした評価と雑音の測定（実装者: Sonnet、2026-11-16）

## 作った物
- `games/MiniRing.Arena/MatchScore.cs`: 点の式 `MatchScore.Of(wins, losses, dealt, taken)` と、自分の名前への印 `Mark`。`MatchTrainer.Evaluate` もこれを使う（結果は変わらない: テストで確認）。
- `MatchSampler.cs`: 試合の 1 標本 = 1 つの種 × 相手全員 × 両コーナー（`BotArena.Pair` の 2 試合）の点。`maxParallel` に依らず同じ結果（添字の位置に書く）。
- `PairedEvaluation.cs`: `Compute`（n・平均の差・標準誤差・t 分布の 95 % 区間・両側の符号検定 p（差 0 の種は除く、二項分布で正確）・相関 ρ）、`Confirm`（使わなかった種の固定 n）、`Race`（n_min から k ずつ、覗く L 回の各回 α / L、k は `MinBatch` = コアが遊ばない大きさへ引き上げ、結果は「目安」）。t の臨界値は不完全ベータ関数と二分法（外部ライブラリなし）。
- `DefenseTuner.cs`: `Evaluate`・`Aggregate` を internal に、種ごとの結果の配列を返す `EvaluateBySeed`・種ごとの点（差）`SeedPoint`/`SeedPoints` を追加。`DefenseTuner` の点（合計からの比）は変えていない。
- `NoiseLab.cs` と `noiselab` コマンド（`Program.cs`）: (a) 種による揺れ、(b) 小さな変更での揺れ、(c) 差を言うのに要る n、(d) 秒数ごとの検出力（t ÷ √壁時計）。
- `MiniRing.Arena.csproj` に `InternalsVisibleTo MiniRing.Tests`、`MiniRing.Tests.csproj` に Arena への ProjectReference（Arena の internal をテストするため。brief の編集範囲を 2 つの csproj へ少し広げた。P1a の作業もテスト csproj を触る場合は、マージで衝突するので確認が要る）。
- `DefenseDrill.cs` は変更なし（種ごとの結果は `DefenseTuner` 側で足りた）。体の物理・ボットの判断・スタイル JSON は変えていない。
- `tests/MiniRing.Tests/PairedEvaluationTests.cs`（12 件）: 符号検定 10 勝 2 敗 = 2 × 79 / 4096、差 0 の種を除く、t の臨界値（n = 8 → 2.3646、n = 30 → 2.0452 ほか）、既知の値の集計、相関、同じ値どうしは「差なし」、逐次の止まり方と α / L、MinBatch、`MatchSampler` の並列 1 と既定で同じ結果、`MatchScore.Of` を使っても `MatchTrainer.Evaluate` が変わらない、名前の印、`DefenseTuner` の点が直接ドリルを足した値と同じ（短い設定）。

## 実行したテスト
- 新規 `PairedEvaluationTests`: 12 件合格。
- `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName~Replay|FullyQualifiedName~PunchBook|FullyQualifiedName~Drill|FullyQualifiedName~PairedEvaluation"`: 49 件合格・0 失敗（3 分 46 秒）。
- 全テスト（約 22 分）は RAM の都合で実行していない（Claude がマージ後に実行）。README のテスト件数は更新していない（新規 12 件を足す）。

## 結論（`docs/arena/evo_noise.md`）
- 標本 1 つの点の標準偏差: 試合 約 2.7（12 試合 / 標本、45 秒）、ドリル 約 0.28（20 秒）。
- 値を 0.001 動かしても軌道が別になる: ρ = 試合 0.13、ドリル 0.34 と −0.02。同じ種で対にしても分散はほとんど減らない。n と λ で平均する。
- 勝者の呪い: ドリルの逐次は n = 32 で「+0.225、99.4 % 区間が 0 を含まない」と止まったが、使わなかった種 n = 96 の固定 n では +0.033（差なし）。
- 推奨: 試合は 45 秒・1 候補 n = 16・λ = 12・約 30 世代・採否は使わなかった種で n = 40 の固定 n。ドリルは 40 秒・n = 48・λ = 12・約 30 世代・採否は n = 96 以上。
- 壁時計: 試合の noiselab 35.0 分（n = 40、1 標本 約 5 秒 / 値）、ドリル 5.7 分と 9.0 分（n = 96 を 2 回）。別の作業と同時のため壁時計は 1.5 倍ほど揺れる。

## 気づいたこと（要確認）
- champion（2 代目）は今の物理では 6 相手に負け越し（点 −5.8）、champion-v1 のほうが強い（点 +4.3、40 種中 40 種で v1 が上）。`league` でも champion 5-0-7、v1 8-1-3 で一致。CLAUDE.md の champion の総当たり 70-0-0 は今は成り立たない。
- champion の `Slip.HeadX` は 0.8 だが範囲の上限は 0.6。+0.001 しても収め直されて同じ値になる（最初の測定で全差が 0 になって気づいた）。champion′ は収めた後の値から範囲の内側へ 0.001。

## 入れなかった物
- README のテスト件数、`docs/LEARNING_PLAN.md` §9 の状態欄（P0b の状態）の更新（マージ時に Claude が行う）。
- 試合の逐次（`Race`）の実測の動作例（ドリルのみ。試合は費用が大きいので、道具とテストだけ）。
- 試合の n は 40 まで（予定の 12 では (a) の揺れの差が見えないので増やした）。
