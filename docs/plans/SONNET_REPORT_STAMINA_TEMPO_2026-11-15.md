完了

# 報告: スタミナのテンポ（2026-11-15）

PhysicsVersion 32 / LogicVersion 28。コミット・プッシュなし。詳しい表は [docs/arena/gen13_stamina_tempo.md](../arena/gen13_stamina_tempo.md)。

## 変えた式と値

- 回復を止める P を「実際にした仕事」で数える: `_stepWork`（`SpendMuscleEnergy` が出した分）＋サーボの精算 ÷ dt を `StaminaRecoveryWindow` でならす。`RequestedPower`・`_armPower`・`_transferDemand`・`StaminaPowerWindow` は廃止。
- 回復 R = 15 W × Breath × exp(−(P / 5 W)²)（形は元のまま）。
- `StaminaRestPower` 0.7 → 5 W、`StaminaRecoveryWindow` 2 → 1 s、`StaminaCapacity` 90 → 60 J。`IsometricCost`・`WalkCost`・`StaminaRecoveryPower` は変更なし。
- ボット: `RestBelow` 0.25 → 0.5（BotStyle 既定と Mutate の中心）。

## 目標ごとの測った数（前 → 後）

- 空から構えて 3 秒: 42.4 % → 74.2 %、6 秒: 91.5 % → 100 %
- 空から下がりながら 8 秒: 99.2 %（5 秒 59.8 %）→ 100 %（5 秒 100 %）
- 空から最初のまともな一発: 弱い一発は 0 秒で 0.86 → 0.994 倍。強い一発は 0.5 秒後の次の一発で 8 割以上（テスト）
- 全力の連打で空まで: 11.1 秒 → 8.1 秒。全力の攻めの試合で空まで: 10.7 秒 → 10.4 秒
- 通常の CPU 試合 18 本の回復待ち（蓄え 25 % 未満）: 62.9 % → 26.2 %、空（5 % 未満）15.4 % → 2.7 %、平均蓄え 0.336 → 0.539
- 空の連打の不利: 10 発目が満タンの 1 発目の 0.05 倍 → 0.585 倍の速さ・0.445 倍のエネルギー（残るが弱まった）
- 空から始めた側（4 試合 × 4 スタイル）の勝ち点: 0.00 → 0.25〜0.50（平均 0.34）。不利は残る

## 試合の表（CPU 同士 60 秒 × 6 組 × 種 3 = 18 本）

| | 平均蓄え | 空 | 回復待ち | 満タン→空 |
|---|---|---|---|---|
| 通常 前 | 0.336 | 15.4 % | 62.9 % | 12.7 s |
| 通常 後 | 0.539 | 2.7 % | 26.2 % | 18.4 s |
| 全力 前 | 0.221 | 60.1 % | 73.3 % | 10.7 s |
| 全力 後 | 0.474 | 27.0 % | 48.5 % | 10.4 s |

league（`--bouts 4 --seconds 60`、180 試合）は `docs/arena/gen13_stamina_tempo_league.md`（後のみ。前は取っていない）。

## テスト

全テスト（Probe 除く、`dotnet test -c Release`）: 582 件、成功 582・失敗 0（2 分 57 秒）。新規 `StaminaTempoTests` 5 本、`StaminaBudgetTests` の連打の型と閾値を更新、`StepCostRegressionTests` の刀のチェックサムを更新（PhysicsVersion 32）。

## 直したファイル

games/MiniRing/Fighter.Stamina.cs・Fighter.cs・FighterTuning.cs・Bots/BoxerBot.cs・Bots/BotStyle.cs、tests/MiniRing.Tests の StaminaBudgetTests・StaminaIdleServoTests（コメント）・StepCostRegressionTests・StaminaTempoTests（新）・StaminaTempoProbe（新、Probe）、docs/arena/gen13_stamina_tempo.md（新）・gen13_stamina_tempo_league.md（新）・gen13_stamina.md（注記）、docs/BOXING.md・LEARNING_PLAN.md（A4c）・GUIDE.html（スタミナの 1 文）・CLAUDE.md（§8 のスタミナの行）・README.md（件数）。

## 確かめていない所

- 通常の CPU 試合の回復待ちは試合ごとのばらつきが大きい（0〜70 %）。平均 26 % は 18 本の値で、champion vs slugger のように殴り合いが続く試合は 6〜7 割が回復待ちのまま。
- 通常の CPU 試合で満タンから空まで 18.4 秒（目標 6〜10 秒は全力の攻めの試合・リグで満たし、通常の攻めでは満たしていない。通常の攻めは空まで行く試合が少なく n=8）。
- 空の連打の不利は旧式より大幅に弱まった（0.05 → 0.585 倍）。「10 発目は 1 発目の半分以下」は、1 発目を満タンの 1 発目と読んで満たしている。尺度 7 W なら回復待ちは 22 % だが不利が 0.72 倍に薄れるため 5 W を採った。
- ボットの休息 0.5 への変更が、リーグの強さの並び（スタイル間の勝率）に与える影響は前後比較していない（league は後のみ）。
- 目で見る確認（動画・コマ）はしていない。見た目に関わる変更ではなく、体の動きを書き換えていないため。VR のプレイヤーはスタミナの疲れが効かない設定のまま（影響を測っていない）。
- code-reviewer（別の目）のレビューは、この環境では取っていない。
