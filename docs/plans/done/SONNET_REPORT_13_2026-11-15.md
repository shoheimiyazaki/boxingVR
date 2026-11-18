完了

# 作業 13 の報告（2026-11-15）: 240 Hz のまま 1 歩の決まった費用と最大の歩を減らす

詳しい表・原因・入れなかった案は `docs/arena/gen13_step_fixed.md`。コミット・プッシュはしていない。`LogicVersion`・`PhysicsVersion` は上げていない（結果が同じ）。

## 結果

| 刀の試合（5 シード × 2400 歩） | 前 中央値 / p95 / 最大 [ms] | 後 中央値 / p95 / 最大 [ms] | 前 → 後 割り当て 中央値 / 平均 [KB/歩] |
|---|---|---|---|
| 全部 | 1.28 / 3.01 / 15.2 | 0.62〜0.64 / 2.25 / 10.5〜12.9 | 125 / 153 → 22.6 / 55 |
| 触れた | 1.68 / 3.59 / 15.2 | 0.95〜0.98 / 2.80 / 10.5〜12.9 | 184 / 216 → 73 / 95 |
| 触れなかった | 1.08 / 1.86 / 4.35 | 0.47〜0.48 / 1.09 / 3.7〜4.5 | 88 / 96 → 18.8 / 19 |

| 拳の試合（champion 対 swarmer） | 前 | 後 | 割り当て 中央値 / 平均 [KB/歩] |
|---|---|---|---|
| 全部 | 0.123 / 0.993 / 9.5 | 0.117〜0.119 / 0.87〜0.89 / 9.3〜12.4 | 6.1 / 37.7 → 2.6 / 7.2 |
| 触れた | 0.910 / 1.54 / 9.5 | 0.81〜0.83 / 1.26〜1.33 / 9.3〜12.4 | 188 / 202 → 36 / 37 |
| 触れなかった | 0.121 / 0.551 / 2.2 | 0.116〜0.117 / 0.48〜0.51 / 2.2〜3.6 | 6.0 / 19 → 2.6 / 3.8 |

最大は GC・JIT の 1 歩が入り、同じ版でも ±30 % ゆれる（拳の「後」は 9.3 と 12.4）。

## チェックサム（前後で一致）

- 刀 5 シード: `bf23ffc3e767a185 800bae1b07b6bec2 ad6c5bee20893259 305461fa1b33617f b53759b4e5b34cc3`（指示書の値のまま。毎回の測り直しで確認）。
- 拳 5 シード（champion 対 swarmer）: `93839ac096dd8ec2 164eb222287a3048 c33d2f08cf4a93f0 68308efb9afbabd4 a86a7ef72f84bc08`（前後で一致）。
- 計測あり・なしの走りの一致も毎回テスト内で確認。

## テスト

`dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`: 552 件全通過（2 分 14 秒。`ReplayTests`・`IntentReplayTests` を含む）。そのあとで再発防止のテスト `StepCostRegressionTests`（刀シード 0 のチェックサムの固定と 1 歩の割り当ての上限）を 1 件足し、単独で通過（合計 553 件、README の件数を 553 に）。全テストは Release で走らせた（指示書のコマンドに `-c Release` を足しただけ）。

## 直したファイル

- `games/MiniRing/BoxingGame.Weapons.cs`: `FullReachOf`（既定値の引数 `FarEdge` を毎回評価していた。1 歩 0.55 ms の正体）、`FarEdge`（端のスライスで止める）、`WeaponOf`・`GloveOf`・`RememberBeforeStepCore`・`ResolveWeaponsCore`・`ResolveBodies` の LINQ・毎歩の割り当て。
- `src/Aigis.Physics/Voxels/VoxelContact.cs`: `Sweep.Candidates`・`BuildChunks` の割り当て（スレッドごとの使い回しと数の持ち方）。
- `src/Aigis.Foundation/Voxels/VoxelGrid.cs`: `Split`・`OccupiedBounds` の全セルの歩き（動くスライスだけ・割り算なし）。
- `src/Aigis.Foundation/Voxels/StepProfile.cs`・`games/MiniRing/BoxingGame.cs`・`games/MiniRing/Fighter.cs`・`games/MiniRing/Bots/BoxerBot.cs`: 計測の枠（知覚・判断・体への指示・スタミナ・立つ更新、ボクサーの判断の内側）と枠ごとの割り当て。
- `tests/MiniRing.Tests/KatanaStepCostProbe.cs`（拳の試合・割り当て・GC 回数）、`tests/MiniRing.Tests/StepCostRegressionTests.cs`（新規）、`README.md`（件数）、`docs/arena/gen13_step_fixed.md`（新規）。

## 入れなかった案（理由は `docs/arena/gen13_step_fixed.md`）

- 相手の部位の「占有箱」で走査を絞る: 結果は同じだが RunScan 8 % しか減らず、戻した。
- 箱に丸ごと入るブリックのセルテストを飛ばす: 高さ場の範囲がセルごとの値から決まるので結果が変わりうる。
- 重い歩（最大 10〜13 ms、RunScan 3〜6 ms）の時間方向のまとめ飛ばし・標本の粗さの変更: 作業 11 で効かなかった／結果が変わる。残った最大の歩はここ（刃が相手の部位の箱の中を通る標本が大半）。
- `RawArmedSense` を 1 歩 1 回にまとめる: `FullReachOf` を直したあと 0.005 ms で不要。
- ボットの判断の 60 Hz 間引き: 指示書どおり入れていない。

## 残り

触れなかった歩の中央値 0.48 ms のうち、Bepu 0.10〜0.13、候補セルの絞り込み 0.20、RunScan 0.13（1 歩に非 Far の Sweep が約 12 本、1 本約 600 セル × 4 姿勢の変換）が大半。ここを減らすには結果が変わる変更（候補の定義・標本間隔・掃き直しの回数）が要る。
