完了

# 作業指示 14（P0a）報告: 拳の技表 `PunchBook` をデータへ

## 直した所
- `games/MiniRing/Bots/PunchBook.cs`（新規）: `PunchBook`（`Default`・`Of`・`MoveOf`・`With`・`Validated`）、JSON 用の `PunchMove`（`HeadShiftX/Y/Z` は float）、範囲の表 `PunchRanges` と共通の `ValueRange`。書いていない技・値は既定（`JsonObjectCreationHandling.Populate`）。`Validated()` は `Lead`・`ToBody` を既定へ戻し、`Extend` を Feint 以外 1（Feint は 0.2〜0.6）に固定。
- `Technique.cs`: `PunchSpec.Of(t)` は `PunchBook.Default.Of(t)` を返すだけ。
- `BotStyle.cs`: `Punches` を追加し `Validated()` で検証（既存 JSON は未変更）。
- 呼び出し側: `BoxerBot`（1005・1020・1023・1108 付近）→ `Style.Punches.Of`、`IntentControl`（HandOf・Lead 判定・経路の spec）→ `_style.Punches`、`DefenseDrill.DrillCue.For` → `attacker.Punches`。`BoxerBot.KindOf` は `PunchBook.Default` のままでコメントを追記。`Combo.HasBodyShot` は既定のまま。
- `DefenseBook.cs`: `DefenseRanges` を追加し `DefenseMove.Validated()` が参照。`Mutation.Move` も同じ表を参照（`Tweak` に `ValueRange` のオーバーロードを追加）。両者の範囲は元々一致していたので値は不変。
- `docs/BOTS.md` に「技表はデータ」の節、README のテスト件数を 577 → 585。
- `LogicVersion`・`PhysicsVersion` は上げていない。

## 前後のチェックサム（変更前に取得、変更後に比較）
`BotExecutionBaselineTests`（`MINIRING_VERIFY_EXTRACTION=1`、2400 点）: 一致（合格）。`tests/MiniRing.Tests/bot-execution-baseline.txt` は変更前の値で取り直して置き換えた（`MINIRING_CAPTURE_BASELINE=<パス>` で書き出せるようにテストへ数行足した）。

拳の試合 5 組（champion 対 各スタイル、seed 1、1800 歩 = 30 秒、`PunchBookChecksumProbe`）:

| 相手 | 前 | 後 |
|---|---|---|
| swarmer | 94908D866CA0F45A | 94908D866CA0F45A |
| jabber | 90C22F0617AEC914 | 90C22F0617AEC914 |
| slugger | A08C6C53BE9FD9B5 | A08C6C53BE9FD9B5 |
| counter | 73721B21838CD8F9 | 73721B21838CD8F9 |
| outboxer | 73F5A009E0257108 | 73F5A009E0257108 |

## テスト
- 新規 8 件: `PunchBookTests`（既定が移す前の値と同じ・範囲内、JSON 往復（HeadShift 含む）、部分 JSON、Punches 無しの JSON、`Validated` の範囲と正体の復元、Cross の Duration だけ変えると試合が変わる・`KindOf` 不変、決定性）と `ReplayTests.ReplayStoresTheCpuPunchBook_…`。
- 全テスト `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName!~Probe"`: 合格 585、失敗 0、14 分 34 秒（`ReplayTests` 全通過）。
- 調査用に `PunchBookChecksumProbe.cs`（Probe なので通常の実行から外れる）を追加。

## 入れなかった物
- 学習・進化のアルゴリズム（指示どおり）。`Tunable` の表は次の作業（遺伝子への写し）用に置いただけ。
- コミット・プッシュはしていない。
