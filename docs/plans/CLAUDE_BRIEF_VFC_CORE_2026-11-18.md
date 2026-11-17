# 指示書 A: VFC の試合の芯（ルールの値と六角形の檻）

先に読む: リポジトリの根の `CLAUDE.md`（特に §1・§4）と `docs/plans/VFC_PLAN.md`。

## 目的

VFC の試合を `BoxingGame` で行えるようにする。**ルールの値と場の形だけを足し、体・物理・材料・ボットの判断は変えない。** WVC（今の TV モード・四角いリング）の試合は、ビット単位で今のまま（既存の ReplayTests・チェックサムが変わらないこと）。

## 触ってよいファイル

`games/MiniRing/` の `BoxingGame.cs`・`Ring.cs`・`RingLayout.cs`・`LivingRoom.cs`・`Replay/*`、新しいファイル（例 `HexCage.cs`）、`tests/MiniRing.Tests/`、`docs/BOXING.md`（または新しい節）。
**触らない**: `games/MiniRing/Audio/`・`ArenaLook.cs`・`Characters/`・`games/MiniRing.Arena/`・`tools/`（別の作業 B が並行して触る）。`Bots/BoxerBot.cs` はメインの作業ツリーで別の作業が未コミットの変更を持っているので触らない。

## やること（優先順）

### 1. ルールの値（`MatchRules`）
- `MatchRules` に init プロパティを足す（既定は今の振る舞い）:
  - `CountsDowns`（bool、既定 true）: false なら `CheckFinish` で「動かないままの K.O.（`StillKoSeconds`）」と「10 カウント K.O.」で決着しない。死とリングアウトは今のまま決着する。ダウンの状態（`Fighter` の倒れた状態・`DownTime`）そのものは体の状態なので残す（数えないだけ）。
  - `JudgesDecision`（bool、既定 true）: false なら最終ラウンドの終わりは判定せず、両者生還の引き分け（画面の文 `"DRAW — BOTH SURVIVE"`、勝ち数を増やさない、`Decision` にその文）。
- `MatchRules.Vfc`（static）: 30 秒 × 5 ラウンド、休み 25 秒、`RegenVoxelsPerSecond = 0`（治らない）、`CountsDowns = false`、`JudgesDecision = false`。値の横に「何の量か・なぜか（ユーザーの指定 2026-11-18）」を書く。
- 治らないとき（Regen 0）、休みの間の `Regenerate` を呼ばない（0 個なら今も呼ばれないはずだが確かめる）。スタミナ・意識・打撲の戻りは今のまま。
- 休みに入ったとき倒れている体は、`RestCommand` の歩く指令を出してよい（立てなければ物理で動けないだけ）。何も書き込まない。
- `BoxingCorners.Referee` は別の値のまま（VFC では呼ぶ側が false を渡す）。`CountsDowns=false` と `Referee=false` を組んだときに、レフェリーの一呼吸（`_resumeHold`）が起きないこと。

### 2. 六角形の檻（`RingLayout.Shape`）
- `public enum ArenaShape { RopedSquare, HexCage }` と `RingLayout` の位置引数の後ろに `ArenaShape Shape = ArenaShape.RopedSquare` を足す（既定で今の形）。
- 六角形の寸法: `Size` を**向かい合う辺と辺の距離（内接円の直径）**とする。外接円の半径 R = Size / √3。向き: 2 人が始めに立つ ±Z 方向に辺（平らな面）が向く（頂点は ±X 軸上）。
- プリセット: `RingLayout.Cage`（素手・はめる物）= 1.3 m（面積 ≈ 1.46 m²、四角 1.2 m の 1.44 m² と同程度）・始めの間 0.44 m、`RingLayout.WeaponsCage` = 2.6 m（≈ 5.85 m²、武器の四角 2.4 m の 5.76 m² と同程度）・1.0 m。
- `TableHalf`・`PlinthHalf`・`Center`・`LivingRoom.GroundAt` が、六角形の外へ張り出す頂点（X 方向に R）を覆うようにする（例: `OuterHalf` = 四角なら Half、檻なら R を足し、テーブル・台座はそれに余白を足す）。四角の値は 1 ビットも変えない。
- `Ring`（または新しい `HexCage` クラス。`BoxingGame` が形で作り分ける所は 1 か所にする）:
  - キャンバス: 六角形（幅 2·(Size/2)、長さ R の箱を 0°・60°・120° 回して 3 つ重ねると正六角形になる）。台・エプロンも六角形に見えるように。当たり判定は今と同じく面の高さで支える。
  - 金網の壁: 6 枚の辺それぞれに静的な箱（回転付き `AddStaticBox(position, orientation, halfExtents)`）。高さは人形（身長 60 cm）に対して UFC の金網（人 1.8 m に対し約 1.75 m）を縮めた 0.5 m 前後。当たりの厚みは、240 Hz で拳・武器・破片が抜けない厚さ（2 cm 以上を目安に、実測で決める）。頂点に柱（パッド付きの見た目）、上端に縁のパッド。
  - 見た目: 金網は細い暗い棒の菱形の格子（ボクセルの図柄に合う太さ）。テレビのカメラ越しに選手が見えるよう、隙間を広めに。色は黒〜暗い灰、柱と上端のパッドは黒、キャンバスは明るい灰（中央に何も描かない。ロゴは中継の側）。
  - `IsOut` は今のまま（面より下へ落ちたら）。
- 休むコーナー（`RestCommand`）: 檻なら、赤は (−X, −Z) 側の頂点 240°、青は 60° の頂点へ、`CornerInset` だけ内側。形ごとの計算は `RingLayout` 側に置く（例 `RingLayout.RestCorner(bool red, float inset)`）。
- 記録・再生（`Replay/ReplayMatchSettings`）: 檻の形が記録に入り、再生で同じ形になること。`MatchRules` を記録が持っているかを確かめ、持っているなら新しい値（`CountsDowns`・`JudgesDecision`）も入れる。古い記録は既定値（今の形・今のルール）で読めること。
- 版: 既定の道（四角・今のルール）の物理が変わらないので、**既存の ReplayTests のチェックサムがそのままなら `PhysicsVersion` は上げない**。変わってしまったら原因を直す（既定の道を変えない）。檻は新しい形の追加として `Fighter.PhysicsVersion` の履歴コメントに「44 にはしない理由」ではなく、`docs` に「檻は既定の道を変えない追加」と書けばよい。迷ったら上げずに報告に書く。

### 3. テスト（`tests/MiniRing.Tests`、ヘッドレス物理）
- 檻: 檻の中で片方の体に壁へ向かう移動指令を数秒出し続けても、骨盤が六角形の内側（辺までの距離 > 0）に留まる。6 方向（各辺）で。
- 檻: 檻の試合でも始めの位置・休みのコーナーが六角形の内側。
- ルール: `MatchRules.Vfc` で、相手が倒れて 10 秒より長く・動かずに 4 秒より長くいても試合が終わらない。死（`Die` を直接呼ぶ、または首を断つ既存のテストの道）で終わる。5 ラウンドの終わりで `"DRAW — BOTH SURVIVE"`・勝ち数は増えない。
- ルール: 休みの間にボクセルの数が増えない（治らない）。WVC の `MatchRules.Tv` では今どおり増える（既存のテストがあればそれで足りる）。
- 再生: 檻 + `MatchRules.Vfc` + `Referee=false` の短い試合が、記録 → 再生で全チェックサム一致。
- 既定の道の不変: 既存の ReplayTests がそのまま通る。

### 4. 目で見る
- 檻の試合を静止画で確かめる（visual テストの仕組み `.visualcheck/*.png`、または `miniring-arena video` に檻を選ぶ一時的な道が無ければ visual テストを 1 本足す）。**自分で PNG を開いて見る**: 六角形・金網の格子・柱・選手が金網越しに見えること。

### 5. 文書
- `docs/BOXING.md`（または `docs/VFC.md` を新しく）に、ルールの値・檻の寸法と根拠・休みのコーナーを書く。CLAUDE.md §8 に足すべき近道があれば、報告に書く（CLAUDE.md は指示する側が直す）。

## 完了の条件
1. 上のテストが通る。
2. 全テスト `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName!~Probe"` が通る（件数を報告。README のテスト件数を合わせる）。
3. 檻の PNG を自分で見て、見えたことを報告に書く（PNG のパスも）。
4. worktree のブランチにコミットする（`feat: ...` 形式、付記なし）。プッシュしない。

## 報告に書くこと
変えたファイル・足した API（名前と意味）・金網の高さと当たりの厚み（決めた理由・測った結果）・テスト件数・`PhysicsVersion` を上げたか・PNG のパス・残った問題。
