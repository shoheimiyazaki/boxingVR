# 指示書 B: VFC のテレビ中継（団体のファイル・紋章・画面・音・語り）

先に読む: リポジトリの根の `CLAUDE.md`（§1・§4・§5）、`docs/plans/VFC_PLAN.md`、`docs/TV_BROADCAST.md`、`tools/tvshow/tvshow.ps1`、`games/MiniRing.Arena/TvShowGraphics.cs`・`WvcEmblem.cs`・`TvShowRules.cs`・`TvDraft.cs`、`games/MiniRing/Audio/Broadcast.cs`。

## 目的

テレビ中継の道具（撮影の画面・紋章・音・照明の雰囲気・下見の点数・台本の資料と下書き・LLM のプロンプト・札）を、**団体のファイル**で WVC と VFC に作り分けられるようにする。WVC の中継は今と同じ見た目・同じ出力のまま。

並行して作業 A が、試合の芯（`MatchRules.Vfc`: ダウンを数えない・判定しない・治さない、六角形の檻 `RingLayout.Shape = HexCage`）を作っている。**試合の決まり・檻を撮影に通す配線（`VideoMaker` の試合の組み立て・`TvShowRules.Rules`・`BoxingGame` の照明の呼び出し）はこの作業ではしない**（A とマージした後に指示する側がする）。この作業の `--org vfc` は、見せ方（画面・音・語り・点数）だけを切り替える。

## 触ってよいファイル

`games/MiniRing.Arena/`（`VideoMaker.cs` は画面・音・照明の選択を通す所だけ。試合の組み立て `new BoxingGame(...)`・`BoxingCorners`・`MatchRules` を作る所は触らない）、`games/MiniRing/Audio/`、`games/MiniRing/ArenaLook.cs`（雰囲気の値を引数で選べるようにするだけ。`BoxingGame.cs` は触らない）、`games/MiniRing/Characters/`（所属）、`tools/tvshow/`・`tools/commentary/`、`.claude/skills/tv-commentary/`、`docs/TV_BROADCAST.md`・`docs/CHARACTERS.md`・`docs/commentary/`、`tests/MiniRing.Tests/`。
**触らない**: `games/MiniRing/BoxingGame.cs`・`Ring.cs`・`RingLayout.cs`・`LivingRoom.cs`・`Replay/`（A が触る）、`Bots/BoxerBot.cs`（メインで別作業が未コミット）。

## やること（優先順）

### 1. 団体のファイル（データ）
- `games/MiniRing.Arena/Orgs/wvc.json`・`vfc.json`（ビルド出力へコピー。名簿 `roster/*.json` の運び方に倣う）と、読み込む型（例 `Organization` record）とスキーマの検査（足りない・型違いは例外で止める）。
- 中身（例。必要に応じて足す。**コードに団体名の分岐を書かず、値で切り替える**）:
  - `id`・`name`（"Wonderful Voxing Council" / "Voxel Fighting Championship"）・`logoText`（"WVC" / "VFC"）・`sport`（"Voxing" / "Voxel Fighting"）
  - `emblem`（紋章の描き方の id: `"wvc"` / `"vfc"`）
  - `colors`: 画面の主色・差し色・文字色（WVC は今の金・白など今の値そのまま、VFC は赤 `#E8231F` 前後・黒・クリーム `#F3E9D6` 前後）
  - `arenaMood`: 照明の雰囲気（WVC = 今の明るい会場、VFC = 暗い会場で檻だけを照らすスポット）
  - `bell`: ラウンドの合図の音の id（WVC = 今のゴング、VFC = 低いホーン）。`crowd`: 歓声の荒さの値（今の `CrowdSound` に足せる範囲で）
  - `rulesSheet`: 台本の資料の「決まり」の行（`Broadcast.cs` の `RulesSheet` を団体ごとに。VFC: レフェリーなし・ダウンを取らない・倒れた相手も打てる・相手が死ぬまで・5 ラウンドを終えて両者が生きていれば両者生還の引き分け・休みでも体は治らない・檻なのでリングアウトは無い）
  - `countsDowns`: 下書き・画面がダウンのカウントを言う・描くか
  - `drama`: 下見の点数の重み（下の 4）
  - `commentary`: プロンプトに入れる団体の紹介の行と語り口（WVC = 品のある実況と理由を言う解説、VFC = 熱く荒い実況、解説は「どこが壊れたか・あと何が残っているか」）
- `--org <id>`（既定 `wvc`）を `video`・`tvscout`・`tvdigest`・`tvdraft`・`tvlint` など中継の道具が受け取る。`events.json` に団体の id を残し、後の段（digest・draft・lint）は events から読めるならそれを使う。

### 2. VFC の紋章（`VfcEmblem`、`WvcEmblem` と同じ描き方: `UiShapeDraw` の四角を積む）
- 手本はユーザーの画像（説明）: 太い赤いボクセルの六角形の枠（上下に平らな辺。枠は奥行きのある赤い立方体を積んだ見た目）、枠の中の背景に暗い灰の金網（菱形の格子）、手前に大きなクリーム色の太いブロック文字「VFC」（少し斜めに崩した力強い字形）、その下に黒地にクリームの文字「VOXEL FIGHTING CHAMPIONSHIP」の帯（帯の縁はクリーム、帯の下に赤い段、帯の左右に赤い横縞が 3 本ずつ伸びる）。全体は黒の背景に映える。
- 題字（タイトル画面）と角の小さなロゴの札（`TvShowGraphics.Logo`）が、団体の `emblem` で WVC か VFC を描く。光の帯の演出は WVC と同じ仕組みを使ってよい。
- **確かめ方**: 紋章とタイトル画面・ロゴの札を PNG に描き出し（既存の visual テストか、`video` の短い撮影のコマ）、**自分で開いて見る**。WVC の画面が前と同じであることも見比べる。

### 3. 中継の画面の色・名前
- `TvShowGraphics.OrganizationName`・`LogoText`・`SportName` の固定の文字列を、団体のファイルの値に置き換える（呼び出しの側へ団体を渡す。グローバルな可変の状態にしない）。
- 角の札・帯・字幕の色を団体の `colors` から。WVC は今の色そのまま（見比べて同じ）。
- `countsDowns` が false の団体では、ダウンのカウントの表示（あれば）を出さない。

### 4. 下見の点数（`TvShowRules.Drama`）
- 重みを団体のファイルから読む形にし、WVC の重みは今の値そのまま（既存のテストが通る）。
- VFC の重みの考え方: 長く続く（5 ラウンドまで）・死で決着（後半ほど高い）・削られていた側の逆転・両者が深く傷んでいる（与えたダメージの両方が大きい）で上がり、第 1 ラウンド 15 秒より前の決着で大きく下がり、両者生還（引き分け）で少し下がる。ダウンの数は数えない（0 の重み）。`TvScoutResult` に足すべき量（例: 死で終わったか）があれば足す（下見の試合の結果から取れる範囲で）。
- テスト: VFC の重みで、後半の死 > 序盤の死、逆転 > 一方的、などの順序。

### 5. 音・照明
- `games/MiniRing/Audio/` に VFC のラウンドの合図（低く太いホーン、合成。`GongSound` に倣う）を足し、団体の `bell` で選ぶ。WVC は今のゴングのまま（音のバイト列が同じ）。
- `ArenaLook` に雰囲気の値（例 `ArenaMood`）を引数で渡せる形を足す（既定は今のまま）。VFC の雰囲気: 周りを暗く、檻の上から強いスポット。**`BoxingGame.cs` の呼び出しは変えない**（合わせる段で指示する側がつなぐ）。単体で照明の値を返す関数として作り、テストか PNG で確かめられる形にする。描画だけで試合に響かないこと。

### 6. 台本（資料・下書き・プロンプト・札・スキル）
- `Broadcast.cs` の `RulesSheet` を団体のファイルの `rulesSheet` から。
- `TvDraft`: `countsDowns` が false の団体では、カウント・「ダウン」を数える言い方をしない（倒れた・それでも止まらない、と言う）。決着の言い方: 死なら「決着」、5 ラウンドの終わりは「両者生還、引き分け」。言い回しは団体のファイルか、今の下書きの表と同じ置き場に。
- `tools/tvshow/prompts/*.md`・`write-commentary.ps1`: WVC と固定で書いた行を `{{org}}` などの差し込みにし、団体のファイルの値で埋める。VFC 用の語り口の行を足す。
- `tools/tvshow/tvshow.ps1`: 札の `"org"`（既定 `"wvc"`）を読み、すべての arena の呼び出しに `--org` を渡す。札の例 `tools/tvshow/cards/vfc-test.json`（1 試合、VFC の選手同士、例 jin 対 konrad、`"rounds": 5, "break": 25`）を作る（撮るのは合わせた後）。
- `.claude/skills/tv-commentary/`: VFC の語り口の型を足す（決まり: 死・傷・残った体を言ってよい、ゲームの裏側の言葉と数値は言わない、は WVC と同じ）。
- `tvlint` の決まりで WVC 固有の物があれば団体ごとに。

### 7. 選手の所属
- キャラクターのファイルに `"org": "wvc" | "vfc"` を足す（読み込みと検査。無ければ wvc）。
- VFC へ: daichi・edgar・gaston・jin・joao・konrad・sean・tsuyoshi・vikram（武器・グローブ以外のはめる物）。WVC: それ以外（素手・グローブの拳・空手・テコンドー・ムエタイ）。
- VFC へ移した選手の育ちの記録（`history` などの文）で「WVC へ入団」「Voxing の武器ありルール」と書いた行を、VFC の設定に合う文に直す（全員架空。名前・国・顔は変えない）。ramon の「WVC の後輩テユン」は WVC 同士なのでそのまま。
- `docs/CHARACTERS.md` に所属の値を書く。

## 完了の条件
1. WVC の中継は前と同じ（既存のテストが通る・画面の PNG を見比べて同じ）。
2. VFC の紋章・タイトル・ロゴの札の PNG を自分で見て、手本の説明に合っている。
3. 全テスト `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName!~Probe"` が通る（件数を報告。README の件数を合わせる）。
4. `docs/TV_BROADCAST.md` に団体の節（WVC と VFC の違い、`--org`、札の `org`）を足す。
5. worktree のブランチにコミットする（`feat: ...` 形式、付記なし）。プッシュしない。

## 報告に書くこと
団体のファイルの項目一覧・`--org` を受ける道具の一覧・PNG のパスと見えたこと・WVC が変わっていないことの確かめ方・テスト件数・合わせる段で指示する側がつなぐ所（関数名と、渡す値）・残った問題。
