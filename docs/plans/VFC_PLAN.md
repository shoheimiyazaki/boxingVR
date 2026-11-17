# VFC（Voxel Fighting Championship）の計画（2026-11-18、ユーザーの指定）

WVC とは別の団体 VFC を作り、テレビ中継のモードを持たせる。

## 1. 2 つの団体（ユーザーの設定）

| | WVC（Wonderful Voxing Council） | VFC（Voxel Fighting Championship） |
|---|---|---|
| 性格 | 格式の高いボクシング団体。ボクサー同士がメイン、まれに素手の異種格闘技戦（空手・テコンドー・ムエタイ） | 何でもあり（武器・はめる物・素手のどれでも） |
| 競技名 | Voxing | Voxel Fighting |
| レフェリー | いる（10 カウント・一呼吸） | いない。**ダウンは取らない**（10 カウントも動かないままの K.O. も無い）。倒れた相手も打てる |
| 決着 | K.O.・判定 | **死だけ**。5 ラウンドを終えて両者が生きていれば「両者生還・引き分け」（ユーザーの決定: 案 1(a)、最大 5 ラウンド） |
| インターバル | あり。削れた体が治る | あり。**体は治らない**（スタミナ・意識は今の生理の速さで戻る） |
| 場 | 四角いリング・ロープ | **六角形の金網の檻**（UFC は八角形なので形を写さない。ユーザー了承）。リングアウトは起きない |
| 見た目・音 | 金・王冠・盾、明るい会場、ゴング | 赤・黒・クリームのボクセル、金網の模様、暗い会場に檻だけのスポット、低いホーン |
| 語り | 品のある実況と、理由を言う解説 | 熱く荒い実況。解説は「どこが壊れたか・何が残っているか」 |
| 下見の点数 | ダウンの応酬・逆転・K.O. | 長く持つ・後半の決着・削られていた側の逆転。序盤の即死・両者生還は低い |

所属: 武器・はめる物（グローブ以外）の選手は VFC へ移す（daichi・edgar・gaston・jin・joao・konrad・sean・tsuyoshi・vikram）。素手・グローブの拳の選手（ボクサー・空手・テコンドー・ムエタイ）は WVC。育ちの記録の文も合わせて直す。

YouTube の扱い（年齢制限など）は気にせず進める（ユーザー）。

## 2. 軸との整合

- 体・物理・材料は両団体で同じ（§1.5）。違いは**ルールの値**（`MatchRules`・`BoxingCorners.Referee`）と**場の形**（`RingLayout.Shape`）と**中継の見せ方**（団体のファイル）だけ。
- 団体の違いはデータ（団体のファイル `games/MiniRing.Arena/Orgs/<id>.json`）に置き、コードに `if (vfc)` を散らさない（§1.3・§1.8 の考え方）。
- 檻への押し付け・追い詰めは、檻の静的な当たり判定から物理で出る。姿勢を書かない（§1.2）。
- 死の判定は今の材料のモデル（脳・臓器・首）のまま。VFC のための死の近道は足さない。

## 3. 作業の分け方

| 段 | 中身 | 触るファイル | 指示書 |
|---|---|---|---|
| A 試合の芯 | `MatchRules` のダウンを数えない・判定しない・治さない値、`MatchRules.Vfc`、六角形の檻（`RingLayout.Shape`・`Ring`・`LivingRoom`）、休みのコーナー、記録・再生、テスト | `games/MiniRing/`（Audio・Characters・ArenaLook を除く）、`tests/` | `CLAUDE_BRIEF_VFC_CORE_2026-11-18.md` |
| B 中継 | 団体のファイルと読み込み、VFC の紋章、中継の画面の色・名前、照明の雰囲気（`ArenaLook`）、ホーンの音、下見の点数、台本の資料・プロンプト・下書き、tvshow.ps1 の `org`、選手の所属 | `games/MiniRing.Arena/`（`VideoMaker` の試合の組み立ては除く）、`games/MiniRing/Audio/`、`games/MiniRing/ArenaLook.cs`、`games/MiniRing/Characters/`、`tools/tvshow/`、`.claude/skills/tv-commentary/`、docs | `CLAUDE_BRIEF_VFC_BROADCAST_2026-11-18.md` |
| C 合わせ | A・B をマージし、`--org vfc` で試合の決まり・檻・レフェリーなし・照明を通す（`VideoMaker`・`TvShowRules.Rules`・`BoxingGame` の照明の呼び出し）、VFC の札で 1 試合を撮って見る | 指示する側（Claude） | — |

A と B は触るファイルが重ならないので、別の worktree で並列に回す。

## 4. 記録

- 2026-11-18 段 A（Sonnet、worktree）: `MatchRules.CountsDowns`・`JudgesDecision`・`MatchRules.Vfc`、`ArenaShape.HexCage`（`RingLayout.Cage` 1.3 m・`WeaponsCage` 2.6 m、金網 0.5 m・当たりの厚み 6 cm）。12 方向へ 4 秒歩かせても骨盤は檻の内側（縁まで最小 4.2 cm）。既存の ReplayTests はそのまま通り、`PhysicsVersion` は上げない（既定の道は不変）。
- 2026-11-18 段 B（Sonnet、worktree）: 団体のファイル `Orgs/wvc.json`・`vfc.json`、`VfcEmblem`、画面の色・名前、ホーン、会場の雰囲気（`ArenaMood`）、下見の点数の重み、台本の資料・下書き・プロンプト、札の `org`、選手の所属（VFC 9 人）。WVC の画面は描画の図形・文字のダンプがバイト一致（報告）。
- 2026-11-18 段 C（Claude）: 団体のファイルに `match`（判定・治る・レフェリー・場の形）を足し、`TvShowRules.Rules(rounds, break, org)`・`TvShowRules.Stage(corners, org)` で撮影・下見・リプレイの試合に通した。`BoxingGame(..., mood)` で照明と画面の仕上げ。死因 NECK の訳（首）。全テスト 942 合格・3 スキップ。
- 試し撮り（jin 対 konrad、種 1、`docs/video/vfc-test/`、git の外）: 1R 16.9 秒、ジンがコンラートの首を断って決着。檻・暗い会場・VFC のロゴ・「決着」の勝敗画面を目で確かめた。残り: 実況の台本（tvshow の通し）は未実行、イベントの文の一部（「ノックアウト」「ダウン → K.O.」）は団体の言い回しになっていない、部屋の床の線が四角いリングの図柄のまま（CLAUDE.md §8）。
