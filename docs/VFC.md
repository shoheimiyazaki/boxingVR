# VFC の試合の芯（ルールの値と六角形の檻）

2026-11-18、`docs/plans/VFC_PLAN.md` の段 A。体・物理・材料・ボットの判断は変えず、**ルールの値と場の形だけ**を足した。既定の道（四角いリング・今のルール・`PhysicsVersion`）は 1 ビットも変わらない（檻は既定の道を変えない追加なので `PhysicsVersion` は上げない）。

## ルールの値（`MatchRules`）

| 値 | 既定（WVC） | `MatchRules.Vfc` | 意味 |
|---|---|---|---|
| `CountsDowns` | true | false | false なら「動かないままの K.O.」と「10 カウント K.O.」で決着しない。死・リングアウトは今のまま。倒れた状態（`DownTime` など）は体の状態として残り、レフェリーの一呼吸（`_resumeHold`）も起きない。10 カウントの HUD（`DownCountHud`）も出さない |
| `JudgesDecision` | true | false | false なら最終ラウンドの終わりは判定せず `"DRAW — BOTH SURVIVE"`（勝ち数は増やさない） |
| `RegenVoxelsPerSecond` | 150 | 0 | 0 なら休みの間 `Regenerate` を呼ばない（体は治らない）。スタミナ・意識・打撲の戻りは今のまま |
| `RoundSeconds` / `Rounds` / `BreakSeconds` | 30 / 5 / 10 | 30 / 5 / 25 | ユーザーの指定（2026-11-18） |

`BoxingCorners.Referee` は別の値のまま（VFC では呼ぶ側が false を渡す）。

## 六角形の檻（`RingLayout.Shape = ArenaShape.HexCage`）

- `Size` は向かい合う辺と辺の距離（内接円の直径）。外接円の半径 R = Size / √3。辺（平らな面）が ±Z（2 人が向き合う方向）を向き、頂点は ±X 軸上。角は X 軸から Z 軸の向きへ測る（頂点 0°・60°…、面の法線 90°・150°…）。
- `RingLayout.Cage` = 1.3 m（面積 ≈ 1.46 m²、四角 1.2 m の 1.44 m² と同程度）・始めの間 0.44 m。`RingLayout.WeaponsCage` = 2.6 m（≈ 5.85 m²、四角 2.4 m の 5.76 m²）・1.0 m。
- `OuterHalf`（四角 = Half、檻 = R）に余白を足して `TableHalf`（+15 cm）・`PlinthHalf`（+8 cm）を出す（頂点が台座・天板からはみ出さない）。`LivingRoom.GroundAt` は檻のキャンバスを六角形で判定する。
- 当たり判定・見た目は `HexCage`（`Ring` が形で作り分ける 1 か所）。台・エプロン・キャンバスは長さ R・幅 2·(Size/2) の箱を 0°・60°・120° に回して 3 枚重ねる。壁は 6 辺に静的な箱 1 枚ずつ。
  - 高さ 0.5 m（`HexCage.WallHeight`）: UFC の金網は人 1.8 m に対し約 1.75 m だが、身長 60 cm の人形では拳・武器を隠しすぎないよう縮めた近道。
  - 厚み 0.06 m（`HexCage.WallThickness`）: 240 Hz で 1 歩に進む距離（拳 6 m/s で 2.5 cm、剣先 10 m/s で 4 cm）より厚い。内側の面が辺に付き、角の外側は t·tan30° だけ長くして隙間を埋める。
  - 見た目は 45° の細い棒の菱形格子（間隔 6 cm）・頂点の柱・上端と下端のパッド（黒）。キャンバスは明るい灰で中央に何も描かない。
- `IsOut` は今のまま（面より下へ落ちたら）。
- `RingLayout.EdgeDistance(point)`（縁から内側への距離）と `RingLayout.RestCorner(red, inset)`（休むコーナー）は形ごとの計算をここに置いた。檻の休むコーナーは赤 240°・青 60° の頂点から `CornerInset` だけ中心へ寄せた点。
- 記録・再生: `ReplayMatchSettings` は `RingLayout`・`MatchRules` をそのまま JSON で持つので、`Shape`・`CountsDowns`・`JudgesDecision` も入る。古い記録は既定値（四角・今のルール）で読める（テストあり）。

## 近道・残った点（CLAUDE.md §8 へ足す候補）

- 金網の高さは人形の縮尺に合わせた見た目の値で、UFC の比ではない。壁は箱 1 枚（網目では当たらない）。
- 檻の壁は静的な箱なので、押し付け・追い詰めは物理から出るが、`BoxerBot` のロープ際の判断（`RopeBehind`・`StayInside`・`WeaponDefense` の `ring.Half` 判定）は四角のまま（檻の斜めの面を知らない）。段 C で `RingLayout.EdgeDistance` を使うよう直す余地がある。
- `ArenaLook.Dressing` の床の線（コーナーの赤・青・白の線）は四角いリングの図柄のまま。
