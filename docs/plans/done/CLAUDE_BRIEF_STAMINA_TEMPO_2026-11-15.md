# 作業指示（2026-11-15）: スタミナのテンポを直す（回復が始まらない・消費が激しすぎる）

最初に `CLAUDE.md`（§1・§1.7・§4・§8 のスタミナの行）、`docs/LEARNING_PLAN.md`（A4）、`docs/arena/gen13_stamina.md`、`games/MiniRing/Fighter.Stamina.cs`、`FighterTuning.cs` のスタミナの節を読む。日本語。コミット・プッシュはしない。作業ツリーの未コミットの変更（メニュー・コントローラの直し、`TvShowGraphics.cs` など）は戻さない。

## ユーザーの指摘
- 回復が遅くテンポが悪い。一度スタミナ切れを起こすと、回復が始まるまで 10 秒以上何もできない。
- パンチの消費が激しすぎる。試合のほとんどが回復待ちになる。

## 原因（Claude の見立て。コードを読んだもの、測って確かめること）
1. 回復 `RecoveryPower = 15 W × Breath × exp(−(P / 0.7 W)²)` の `StaminaRestPower` 0.7 W が小さすぎる。歩く（`WalkCost` 0.15 × 体重 × 速さで数 W〜10 W）・構えを保つ（`IsometricCost`）・拳を動かすだけで P が 0.7 W を超え、回復がほぼ 0 になる。
2. P（`_recentMusclePower`）に入るのが**出そうとした仕事率**（`arm.RequestedPower`・保持の力）で、実際に出せた仕事ではない。スタミナ 0 でボタンを押す・動こうとするだけで P が高いまま → 回復しない → また動けない、の行き詰まり。これがエネルギーの出入りとして説明できない（出していない仕事が回復を止める）。

## 決めたこと（モデル。場合分け・閾値を足さない）
1. **回復を止める負債は、実際にした仕事で数える**: P は 1 歩で実際に使った仕事（`SpendMuscleEnergy` で出た分・サーボの精算 `servoEnergy`・歩きの実際の分）÷ dt をならした値にする。押しても出なかった仕事は回復を止めない。
2. **回復は動きながらも続く（有酸素の供給）**: 心肺の供給は「使う仕事率が増えるほど減る」連続の式のまま、尺度を体の仕事率の大きさに合わせる。目安: 構えて立つ・ゆっくり歩く程度では最大の 7 割以上、連打（数十 W）で 2 割以下。`StaminaRestPower` の値を測った仕事率から決め直す（式の形 exp(−比²) を 1/(1+比²) などへ変えてもよい。根拠をコメントに）。
3. **値の目標（テンポ）**:
   - 空から構えて立つだけ: 3 秒で 5 割、6 秒でほぼ満タン。足を使って下がりながらでも 8 秒以内に 8 割。
   - 空になってから最初のまともなパンチ（1 発目の満タン時の拳速の 8 割）が出るまで 2 秒以内。
   - 満タンから連打し続けて空になるまで 6〜10 秒（強打・ワンツーを交ぜた CPU の普通の攻め、ならし 1 分で回復待ちの時間が 3 割未満）。
   - 連打の不利は残す: 指示書 9 の「空で連打すると 10 発目は 1 発目の半分以下」「空から始めた側が不利（リーグ）」は保つ。
   - 調整は `StaminaCapacity`・`StaminaRecoveryPower`・`StaminaRestPower`・`StaminaRecoveryWindow`・`IsometricCost`・`WalkCost` で。新しい定数は足さない（足すなら物理量として説明する）。
4. `Fighter.PhysicsVersion` を上げる。ボットの休む判断（`StaminaRestDecision` 系）が回復の速さに合わなくなったら最小限直し `LogicVersion` を上げる。

## 確かめ
- 数値のテスト（既存の `StaminaBudgetTests`・`StaminaRestDecisionTests`・`StaminaIdleServoTests` を新しい目標に合わせ、上の目標それぞれを 1 本ずつ）。期待値を変えたら理由を書く。
- 試合で: CPU 同士 60 秒 × 数試合、スタミナの時系列（平均・空の時間の割合・回復待ちの割合）を前後で表に（`docs/arena/gen13_stamina_tempo.md`）。`league --bouts 4 --seconds 60` で空から始める側の不利が残るか。
- 全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`。README の件数。文書（`docs/BOXING.md` のスタミナ、`docs/LEARNING_PLAN.md` A4 に仮説 → 結果 → 採否、`CLAUDE.md` §8 のスタミナの行の数値）。
- 最終報告 `docs/plans/done/SONNET_REPORT_STAMINA_TEMPO_2026-11-15.md`（先頭に「完了」か「未完了」）: 式・値・目標ごとの測った数・表・確かめていない所。最終メッセージは 1 回だけ。背景の処理を残さない。
