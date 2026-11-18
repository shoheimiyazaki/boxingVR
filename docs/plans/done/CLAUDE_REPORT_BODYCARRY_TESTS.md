# 報告: 体の運び（PhysicsVersion 45）で外れた 13 個のテストの測り直し（2026-11-18）

版 43 の写し（baseline）と今（このワークツリー）を並べて測った。ゲームのコード・調整値は触っていない（テストと Probe のみ）。分類: (a) 物理として正しい変化、(b) 退行、T = 調整の判断が要る（落ちたまま）。

## 一覧

| # | テスト | 分類 | したこと |
|---|---|---|---|
| 1 | Punch_CounterDamage_IsPositiveWithCountersAndZeroWithout（CmaTuneTests） | (a) | 上限を 0.05 → 0.2 倍 |
| 2 | AKnockedDownFighter_GetsUpOnceHisHeadClears | (a) | 当てる質量を 4.5 → 0.7 kg、速さ 2.2 |
| 3a | Punches_AreAimedWhenThrown_SoAnEarlySlipBeatsThem | (a) | 下限 0.5 → 0.4、守った側 ≤ 開いた側の半分を追加 |
| 3b | PredictedInsideSlip_AvoidsALoadedRightCross_ThatReactionCannot | (a) | ドリルの観測で受け手の脳しんとうを毎歩 0 に |
| 4 | AlternatingRush_FullForTenSeconds_ThenFadesOverTheNextTen | T | 直さず（落ちたまま） |
| 5a, 5b | ElbowContactTests 2 個 | (a) | 寄る間合い 0.9 倍、遅い肘を 1.8 → 1.4 倍 |
| 6a | AClubBlow_CrushesMoreOfTheBody_ThanAPunchCarvesAtMost | T | 直さず（落ちたまま） |
| 6b | NoReferee_WeaponBotWoundsADownedOpponent（Longsword・WoodenClub） | T | 直さず（落ちたまま） |
| 6c | ASwordSweepingAtTheGuard_MeetsTheWoodOfTheStick…（TonfaTests） | (a)、判断あり | 比べる量を「体全体の切れ」→「棒の後ろの腕の切れ」に |
| 6d | TechniqueWeights_DecideWhichFormsTheSwordsmanThrows | (a) | 最初の 1 振りを待つ（最大 100 秒）＋その後 20 秒 |
| 6e | TheForwardForms_HitWithTheFrontEnd…（BojutsuTests） | (a)、判断あり | 「前の端 ≥ 後ろの端」→「返しは後ろの端でも打つ」 |

## 原因（測った数字）

1. 打ち返さない受け手: 種 6・14 秒で 版 43 は 0.0 点（打ち返す側 120.4）、今は 9.3 点（9 回、打ち返す側 68.9）。当たる速さが相対速度になり、構えた受け手の拳へ攻め手が踏み込む接触（攻め手のガード・肘、1 回 1.0〜1.4 点、押し出し 0 で腕だけ 0.33 kg）が数えられる。比 0.135。
2. 起き上がり: テストが決める 4.5 kg は版 43 の「体の質量が湧く」水準で、ConcussionGain ×100 により脳しんとう 1.15 → 114.7（意識 0 のまま起きない）。0.7 kg × 2.2 m/s で 1.09・0.23 秒でダウン・5.94 秒で起き上がり（版 43: 0.2 秒・6.75 秒）。GetUpScanProbe。
3a. 構えて止まった攻め手の拳は腕だけの 0.33 kg で当たり、ダメージ (√運動量 − 0.5) × 11 が 1 点未満（速さ約 1 m/s 未満）なら「触れただけ」で数えない。版 43 は体の質量 3〜5 kg（体の割合 9 割）が乗って数えた。種 5: 5/7 → 3/7、種 1〜8: 37/56 → 25/53。
3b. 側頭への 1 発（12〜16 点）で受け手が倒れ（脳しんとう ×100）、起き上がるまで 9 秒ほど攻めが出ず、読みが倒れたときに捨てられて学び直せない。種 2・3 は予測の有無で同一結果。受け手の脳しんとうを除くと reacting 0.53/守り 0.04、predicting 0.34/1.05（版 43: 0.52/0.21、0.32/0.83）。
4. 拳の最高速度の 10 秒平均 2.90 → 2.45（10〜20 秒 1.65 → 1.42、20〜30 秒 1.28 → 1.31）。最初の 1〜6 秒の最高速度は同じ（3.36 m/s）だが、プレイヤーの 1 秒あたりの仕事が 1.4 倍（t3: 49.7 J 対 30.4 J）で蓄えが速く減り（t5: 0.36 対 0.61）、蓄えの 45 %（TiredKnee）を割るのが約 7 秒 → 約 4.5 秒。間合いは 0.31 → 0.19（版 43 は 0.3 を保つ）。体の質量が当たりで受け手へ渡らなくなり、受け手が押し下げられず押し合いの仕事を腕が払う、と読める（衝突を切る実験は握りの当たりが別経路で隔離できず未確認）。10 秒全力という目標（ユーザー）はスタミナの調整（容量・臨界パワー等）の判断。T。
5. 肘: 腕だけの質量（0.3〜0.5 kg）で、上向きの肘（0.84 m/s 前後）は 1 点未満で数えない（版 43: 2.4 kg）。寄る距離 0.9 倍で 3 つとも当たる（横 1.95・上 1.74・下 1.86 m/s）。遅い肘 1.8 倍は 1 点未満で当たらず、1.4 倍（1.72 m/s・0.751 J）に。速い 0.8 倍は 2.31 m/s・1.278 J。
6a. 棍棒の縦振り（グローブ無し）: 潰したセル 58 → 17、運んだ運動量 0.98 → 0.19、当たった所の速さ 4.09 → 4.20 m/s（同じ）。武器の打撃が全体に弱くなった。T。
6b. 倒れた相手への傷（6 種の合計）: 刀 299 → 16、こんぼう 76 → 2.9、ロングソード 11.7 → 3.5。刀は 2 種以上の条件を満たす。T（ロングソード 1 種、こんぼう 1 種）。
6c. 台本は決定的（種・間合いを変えても同じ。10 秒に 4 発）で、合計は途中の胸への 1 発が決める（トンファー 17 セル 1.39 m/s、手袋 6 セル 1.00 m/s）。木の削れ 15 → 74 と増え、腕の切れ 手袋 3 対 トンファー 2。6 つの技の合計は手袋 82 → 19、トンファー 41 → 100 で、強さが一様に下がったのではなく軌道の違い。比べる量を腕の切れに替えたのは判断（弱い比較になる点に注意）。
6d. 最初の 1 振りまで 版 43 約 15 秒 → 今約 50 秒（両者立ったまま間合い 0.2〜0.4 m、振り手の意識 0.78 以上）。立ち回りの揺らぎ。
6e. 返しの当たり: 前の端 6 打・後ろ 0 → 前 1 打・後ろ 3 打。題名の「返しは後ろの端でも打つ」を確かめる形に。

## 気づいた点（直していない）

- games/MiniRing/BoxingGame.Weapons.cs 約 1145 行目 ScoreKick で `var damage = ...;        if (damage < MinimumDamage)` が 1 行に並んでいる（書式のみ。挙動は同じ）。
- 武器の体の運びは握った拳の速さ（歩の前）を基準にしており、武器の当たり点の速さ（棍棒 4 m/s）ではない。武器の打撃が 3〜10 倍弱くなる（6a・6b）主因の候補。調整か設計の判断（WeaponCarveScale などは触っていない）。

## 追加・変更したファイル（tests/MiniRing.Tests）

CmaTuneTests・DamageModelTests・EvasionBasicsTests・PredictionTests・ElbowContactTests・TonfaTests・WeaponStyleTests・BojutsuTests、Probe: BodyCarryRepairProbe・GetUpScanProbe・DrillCompareProbe・RushRangeProbe。

## 全テスト

下の結果（落ちたものは 4・6a・6b のみで、上で説明済み）。

```
dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"
合計 913、成功 906、失敗 4、スキップ 3（既存の Ignore）
失敗: AClubBlow_CrushesMoreOfTheBody_ThanAPunchCarvesAtMost (6a)
      NoReferee_WeaponBotWoundsADownedOpponent (Longsword) (6b)
      NoReferee_WeaponBotWoundsADownedOpponent (WoodenClub) (6b)
      AlternatingRush_FullForTenSeconds_ThenFadesOverTheNextTen (4)
```
