# 剣同士の守り 2 周目（間に合う受け）作業報告 — 2026-11-18（Sonnet）

## 結果

H1（届く受けだけ）・H2（構えが線を覆う）・H3（弧で読む）・H4（1 周目の連続を重ねる）を実装し、同じ物差しで比べた。**教科書には採らない**（既定は Step のまま、`LogicVersion` は 44 のまま）。新しい値はすべて既定オフの選択肢として残す。

- H1 + H2（手 6 腕/s・動き 0.5）はドリルで Step と並んだ（練習 1.59 / 1.53 vs Step 1.52 / 1.57、確かめ 1.43 / 1.08 vs 1.73 / 1.27）。受けを試みた本の遮り率は 96〜100%（1 周目は 50〜64%）で、1 周目の主因（体の刀が間に合わない）は大きく解消した。
- 試合では採れなかった: 刀の自傷・セル差が種 3001 と 4001 で逆向き、ロングソードの自傷が 2〜2.6 倍。
- H3（弧）・H4 は悪化した（採らない）。

詳細（式・表・コマの所見・残り）は [docs/WEAPON_DEFENSE.md §12](../../WEAPON_DEFENSE.md)、仮説と採否は [docs/BOT_PDCA.md](../../BOT_PDCA.md) の「剣の守り 2 周目」、生の記録は `docs/arena/sword_defense2_*.md`。

## 実装

- `WeaponDefense.ReachableParry`（届く範囲へ置き直す。遮れなければ null）、`WeaponLine.Predict` / `PredictVelocity`（弧の読み）、`KeepClear`（`Parry` の体から離す処理を切り出しただけ）。
- `SwordDefense` に `Reach`・`HandSpeed`・`ReactDelay`・`TurnSpeed`・`InterceptGap`・`Economy`・`Arc`（既定オフ）。`SwordsmanBot.MakeParry` が接続し、遮れなければ受けず足と頭だけにする。`Blocks` は受けを実際に出した回数に数える（今までの判断では同じ値）。
- `guardlab`: 遮り率の列（受けを試みた本・遮った本）、`--compare cycle2`（`GuardLab.Cycle2`、`--variants` はその番号）。
- テスト: `ReachableDefenseTests`（届く範囲を越えない・時間が長いほど理想に近い・遮れなければ受けない・構えの重み・決定性・円運動への弧の一致・突きの直線・回りの上限）。

## 検証の注意

- 打ち手の一覧は技の追加（`SwordForms.Additional.cs`）で増えたため、1 周目と同じ 21 本は `--techniques kesa,gyaku-kesa,oberhau,nuki-do,zwerchhau,kiriage,unterhau,tsuki,stich,Chop,Side,Thrust` で選ぶ（`--only` の部分一致だと新しい型も入る）。現行 Step の練習は 1.52（1 周目の記録 1.62 から、技の追加と教科書の変更で少し動いた）。
- 測定は raytrek（`C:\Users\smiya\repos\sword-defense`）で行った。結果の揺らぎは大きい（kiriage は 1.7〜11）ので、総和の数 % は差と言えない。

## 残り

突き・横で受けを試みる割合を増やすこと（向きを回さない候補）、ロングソードの自傷、弧に手首・肘の回りを含めること、`HandSpeed`・`TurnSpeed` を実体の握りの追従で測ること。

## 全テスト

Probe を除く全体（raytrek、Release）: 954 件中 成功 945・スキップ 5・失敗 4。失敗 4 件（`WeaponTextbooksInJson_WithTheOldValues_PlayTheSameMatchesAsTheHardCodedWeaponBots`・`AdoptedWeaponTextbooks_PlayTheRecordedMatches_AndDifferFromTheOldOnes`・`EmptyEducation_PlaysTheSameMatchesAsBeforeTheFilesWereSplit`・`WeaponStyleTests.Json_RoundTripsEveryValue_IncludingTheOptionalSections`）は、この作業の前の fb460ba（型の WIP のマージ）で既に同じく失敗する（確認済み）。この作業の既定の判断は変えていない。技の作業の側で直すもの。新しい `ReachableDefenseTests` 8 件と `ContinuousDefenseTests`・再生のテストは成功。README の件数は、並行の作業と合わせて変わるので触っていない。
