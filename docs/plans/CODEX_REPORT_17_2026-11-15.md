完了（ただし全テストは最後まで回せていない。下の「全テスト」）

# 作業指示 17（P1b）報告: `cmatune` — CMA-ES をドリルにつなぐ（実装者: Sonnet）

## 作った物
- `games/MiniRing/Bots/DefenseDrill.cs`
  - `Punch` に `BotStyle? attackerStyle = null` を足した（最後の引数。渡さなければ今と同じ `PunchAttacker`）。`DrillCue.For` にも同じ攻め手を渡す。
  - 受け手から攻め手への当たりを `CounterDamage` に数える（防御ドリルの `Bout` と同じ式）。`PunchDrillResult` の末尾に `CounterDamage` と `TorsoDamage`（胴 = Heart・Liver・SolarPlexus・Body への被ダメージ）を足し、`operator +`・`Empty` を直した。`AttackPoint(seconds)` = (HeadDamage + TorsoDamage − CounterDamage) / 秒。
  - `DrillMeter`: 予算の数え方（`Run`・`Punch`・`MeasureBaseline` を呼んだ回数 × 秒数。キャッシュで呼ばなかった分は数えない）。
- `games/MiniRing.Arena/CmaTune.cs`（新規）: `cmatune` 本体。
  - 対象 `PunchTarget`（攻め手 = `PunchAttacker` に champion の技表の中でその技だけ候補にしたもの、受け手 = champion の構えと守りの表、守りは固定、合図 0.04 s、打ち返しあり、間合い 1.15）と `DefenseTarget`（`DefenseTuner.SeedPoints`）。
  - 守りの組: `punch:2` → Block・Slip・Duck、`punch:3` → Block・Weave・Duck、`punch:1b` → Block・StepBack、他は `--defenses` が無ければ `ArgumentException`。
  - 同じ世代の候補は同じ種の組（`seed + 1000 × (世代 × 標本数 + k)`）、結果は添字の位置に書くので並列の数に依らない。状態（`CmaEs.SaveState` + 世代の記録 + 予算）は世代ごとに書く。設定が違う状態では再開を拒む。
  - 最後に使わなかった種（`seed + 1000 × (500000 + k)`）で、始めの値と分布の平均 m を `PairedEvaluation.Confirm` で比べ、区間の下限が 0 より上のときだけ「改善」。続けて champion の技表のその技だけ替えたスタイル対 champion を boxer・outboxer・swarmer・counter・slugger・jabber 相手に `MatchSampler` で比べる（種は `500 000` と別の `600 000` の組）。
  - `--target` はカンマ区切りで複数可（その場合「探索的」と明記し、有意水準を 0.05 / 対象の数にする）。`--json` に候補の `PunchBook`/`DefenseBook`、同じ場所に `*.style.json`（champion に重ねたスタイル）。
- `Program.cs`: `cmatune` コマンド、`drilltune` の最後に同じ数え方の予算の表示、`video --drill --attacker-style <json|名前>`（攻め手の技の表をそのスタイルの物にする。構え・型・間合いはドリルのまま）。
- テスト `tests/MiniRing.Tests/CmaTuneTests.cs`（新規 11 件）、`docs/BOTS.md` に `cmatune` の節、`CLAUDE.md` §5 の表に 1 行、README のテスト件数 616 → 627。

## テスト
- 新規 11 件（`CmaTuneTests`）: 攻め手を渡さない呼び出しと既定の攻め手を渡した呼び出しが同じ結果／攻め手の `PunchBook` を変えると結果が変わる／`CounterDamage` は打ち返しありで > 0・なしで 0／`PunchDrillResult` の合算／`DrillMeter`／守りの組と `--defenses` の無い単発以外の型で止まる／不正な対象の拒否／守りの対象の点が有限／`cmatune` を 2 世代・λ 4・1 標本・5 秒で回し、`--resume`（1 世代 → 続き）と続けた場合で CMA-ES の状態と世代の記録が一致／並列 1 と 3 で同じ／種が 1000 以上離れる。11 件合格。
- 全テスト: 下の「全テスト」欄。

## `cmatune --target punch:2 --gens 3` の実行（動くことの確認。数字の良し悪しはまだ見ない）
`--lambda 6 --samples 4 --seconds 20 --confirm-n 8 --match-n 2 --match-seconds 20 --seed 1`（短い設定）。壁時計 **2.2 分**（12 コア）。生の記録: `docs/arena/cma_punch2_demo.md`、`docs/arena/cma/punch2_demo.json`（候補の `PunchBook`）、`punch2_demo.style.json`。

| 世代 | σ | 最良 | 平均 | 最悪 |
|---:|---:|---:|---:|---:|
| 1 | 0.1500 | 5.954 | 2.706 | 0.121 |
| 2 | 0.1478 | 7.127 | 4.926 | 1.265 |
| 3 | 0.1575 | 8.945 | 6.221 | 4.337 |

- 世代の点は世代の間で比べられない（種が違う）。
- 最後の比べ直し（n = 8、使わなかった種）: m − 始めの値 = +0.271 ± 2.716（95% 区間 −6.152〜6.694）、符号検定 p = 0.727（+3/−5） → 改善とは言えない（n が小さい）。
- 試合の確認（n = 2、20 秒）: +1.459（区間 −10.7〜13.6）→ 差が言えない（n = 2 は動作確認のみ）。
- 予算: 264 回（ドリル 3 守り × λ 6 × 標本 4 × 3 世代 = 216 + 比べ直し 8 × 2 × 3 = 48）、合計 5280 秒（シミュレーション上）。

## 注意・ブリーフとのずれ
- 点の式の「頭と胴」: 既存の `PunchDrillResult.HeadDamage` は頭だけなので、胴の分 `TorsoDamage` を足した（ブリーフは `CounterDamage` のみ）。ガード（腕・グローブ）に当たった分は数えない。
- `--lambda 0`（自動）は CMA-ES の既定（n = 9 で 10）でなく `evo_noise.md` の推奨の 12 にした。既定の標本 48・40 秒・比べ直し n = 96・試合 n = 16（推奨の 40 は費用が大きいので 16）。
- 受け手の体（`BotStyle.Build`）はドリルの仕組み（`DefenseDrill.Punch` は既定の体で作る）のままで、champion の構えと守りの表だけ使う。`video --drill --style champion --counters true` と同じ条件。
- `--attacker-style` の動画（ffmpeg）は実行して撮ってはいない（ビルドのみ）。ドリルの条件（守り `--defense`・合図 `--cue`・打ち返し `--counters`・間合い `--range`・種 `--seed`・溜め `--telegraph`）は全部引数で渡せることをコードで確認した。ただし動画は 1 つの守りずつ（Block は動画に使えない）。
- champion は champion-v1 に試合で負け越す（作業 15 の記録）が、指示どおり champion を受け手・基準にした。試合の確認は「champion との比べ」であって強さの絶対値ではない。
- `docs/LEARNING_PLAN.md` §9 の状態欄は更新していない。コミット・プッシュはしていない。

## 全テスト
- 全テスト（`dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName!~Probe"`）は**最後まで回せていない**。この PC が他の作業と競合して非常に遅く、225 件まで進んだ時点（失敗 0）で止めた。そのあとの絞った再実行（CmaTune・CmaEs・PairedEvaluation・EvasionBasics・Prediction・PunchBook・Replay・DefenseTests・BotExecution）も、終わる前に止めた。
- 確かめたのは、新規 `CmaTuneTests` の 11 件（全て合格）と、全体の最初の 225 件（失敗 0）、Release の Arena のビルド。`DefenseDrill.Punch` の既存の呼び出し（`PunchDrillResult` を作る所は 1 か所だけ）は新しい引数が省略可能なので変わらない。
- README のテスト件数は 616 → 627（`--list-tests` の件数。新規 11 件）。全体の合格は Claude が実行して確かめてほしい。
