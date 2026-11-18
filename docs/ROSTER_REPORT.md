# 名簿の総当たりと間合いのレポート（作り方と読み方）

名簿の全員を総当たりで戦わせて **組ごとの勝率** を出し、別に **技ごとの間合いとダメージ**・**選手ごとの得意な距離と不得意な距離** を測って、ゲームの画像入りのレポート（claude.ai の Artifact、Data 型のダッシュボード）にまとめる手順。道具は `tools/roster-report/`。物理・ボットの判断・名簿を変えたあとに回し直して、前の回と比べるために使う。

- 初回（2026-11-18、PhysicsVersion 43・LogicVersion 42、17 人）のレポート: https://claude.ai/artifact/3TvMt5VwWjaeAfh7qk1jA1 （勝率だけの古い版: https://claude.ai/artifact/G8aLR3jtEShUYDpKV8vGa8）
- 初回の生の記録: `docs/arena/league_roster_pv43*`（総当たり）・`docs/arena/maai_roster*`（間合い）。初回の考察の文と使ったコマの選び: `tools/roster-report/examples/2026-11-18/`

---

## 1. 何を測るか（定義）

### 1.1 総当たり（`miniring-arena rosterleague --matrix <csv>`）
- 名簿の全員の組（17 人なら 136 組）を、各組 **20 試合**（10 の種 × コーナー入れ替え）、**1 試合 60 秒**、レフェリーあり、キャラの値（体格・教科書と教育・武器・はめる物）はテレビ中継の試合と同じ決め方（`VideoMaker.Corners` / `WithCharacters`）で戦わせる。
- 勝敗（`BoutResult.Winner`）: K.O. で取ったラウンド数、同じなら与えたダメージの差（差が 5% 以内は引き分け）。**勝ち点 = 勝ち 1・引き分け 0.5**。
- 確からしさ: 勝ち点率に Wilson の 95% 区間（引き分けを半分の勝ちとみなす近似）。20 試合で幅はおよそ ±22 ポイント。区間が 50% をまたがない組を「はっきり有利・不利」とする。
- `--matrix` は 1 試合 1 行（赤・青・種・勝敗・取ったラウンド・ダメージ・ダウン・当たり）。集計は `build-data.mjs`。

### 1.2 技ごとの間合い（`miniring-arena maailab`、`games/MiniRing.Arena/MaaiLab.cs`）
- 各組 4 試合 × 60 秒の総当たり。ボットの「いま出している技」を毎歩読み、**1 打 = 技を出した 1 回**（当たらなかった打ちもダメージ 0 として数える。溜めの途中でやめた拳は数えない）。
- **間合い d** = 打ち出した瞬間の 2 人の胸と胸の水平距離。拳は溜めを終えて前へ出し始めた歩（`PunchAction.Time >= 0`）、蹴り・武器は技を始めた歩。d / 腕の長さ も出す。
- 当たりは、打った手（蹴りは脚、武器は握った武器）がその時に出していた技に付ける。技の時計が終わってから 0.15 秒までの当たりもその打ちに入れ、1 打で何度か触れたら合計する。技を出していない間（構え・守り）の当たりは「(構えの拳 / 脚 / 武器が当たった)」として別に数える。倒れた相手への打ち下ろしは「倒れた相手へ:…」。
- ダメージは試合の得点の物差し（`HitEvent.Damage` / `WeaponHitEvent.Damage`、急所の倍率込み）。**当たり率** はガードの上も含む、**クリーン率** はガード以外に 1 度でも当たった割合。
- 技ごとに: 打ちの数・当たり率・クリーン率・間合いの平均 / 中央 / 最小 / 最大 / P10 / P90・当たったときのダメージの平均 / 中央 / 最大 / P90 / 標準偏差・**1 打あたり**（空振り込みの期待値）。距離の曲線は腕の長さの 0.1 倍の帯ごと。**最適な間合い** = 打ちが 8 以上の帯で 1 打あたりが最大の帯。

### 1.3 得意な距離・不得意な距離（`maailab --ranges <接頭辞>`）
- **両方が立っている歩だけ**、胸と胸の距離を **2 cm の帯** に分け、選手ごとにその帯にいた時間・与えたダメージ・受けたダメージを数える（帯は歩の始めの距離）。
- **差し引き / 秒** = (与えた − 受けた) / その帯にいた秒。**得意** = 差し引きが最大の帯、**不得意** = 最小の帯（全試合でその帯に 30 秒以上いた帯だけ）。± は試合を単位にした比の推定の標準誤差。
- **いつもいる距離** = 立っている時間の中央の帯。得意の帯の真ん中との差（`best_gap_cm`）が「得意な距離で戦えているか」。

### 1.4 画像
- **肖像**（`miniring-arena portraits`、`PortraitExport.cs`）: メニューの顔・全身の絵（`CharacterPortraits`）。試合の体のボクセルから写したもので、描き起こしていない。武器・グローブは写らない。
- **試合の 1 コマ**（`shots.sh`）: 各選手を赤、既定の相手（gaston、gaston 本人は marcus）を青にした 6 秒の試合を `video` で描き、動画の 6・7.5・9 秒のコマを取る（始めの数秒はタイトル）。その選手がよく見えるコマを目で選ぶ（勝っている場面とは限らない）。

---

## 2. 手順

作業フォルダ RUN は `media_tmp/roster-<日付>`（`media_tmp/` は git の外）。以下はリポジトリの根で。

| 段 | コマンド | どこで・時間 |
|---|---|---|
| 0 | 測る物をコミットする（`run-raytrek.sh` は HEAD のコミットだけを送る） | |
| 1 | `bash tools/roster-report/run-raytrek.sh media_tmp/roster-<日付>`（各組の試合数・間合いの試合数・秒は引数で変えられる） | raytrek。17 人で約 2 時間。Bash の `run_in_background` で回す |
| 2 | `bash tools/roster-report/shots.sh media_tmp/roster-<日付>` | このデスクトップ（GPU・ffmpeg）。17 人で約 15 分。段 1 と並べて回してよい |
| 3 | `node tools/roster-report/build-data.mjs media_tmp/roster-<日付>` | 数秒。RUN/data に 7 つの CSV |
| 4 | `node tools/roster-report/analyze.mjs media_tmp/roster-<日付>` | 考察の材料（相関・武器 対 素手・距離のずれ・相性・選手ごとの効いた技） |
| 5 | `RUN/shots/sheet_*.png`（行の順は `RUN/shots/sheets.txt`、左から 6・7.5・9 秒）を見て選手ごとのコマを選び、`RUN/choices.json`（`{"akira":"6", …}`）に書く → `node tools/roster-report/build-images.mjs <RUN> <RUN>/choices.json` | 画像は目で見て選ぶ（CLAUDE.md §4-7） |
| 6 | 段 4 の出力を見て `RUN/findings.html`（考察の `<ol class="rp-findings">`）を書く。形は `examples/2026-11-18/findings.html` | 文は毎回書き直す（§3） |
| 7 | `node tools/roster-report/build-page.mjs <RUN>` | `RUN/page/` にページと、store へ書く `doc_*.json` |
| 8 | `node tools/roster-report/preview.mjs <RUN>`（バックグラウンドで）→ 内蔵ブラウザで `http://localhost:8765/` | タブの題が `ERR:0`・`window.__errors` が空・図と画像が出ていることを見る。長いページで画面写しが止まるときは `?only=distance` のように節を絞る |
| 9 | 公開（§2.1） | |
| 10 | 記録を残す: `RUN/league/league.md`・`league.csv`・`RUN/data/matchups.csv` → `docs/arena/league_roster_pv<版>*`、`RUN/maai/maai.md`・`RUN/data/techniques.csv`・`curve.csv`・`range_*.csv` → `docs/arena/maai_roster*`、使ったコマの選びと考察 → `tools/roster-report/examples/<日付>/`。コミットする | |

### 2.1 公開（Artifact の Data 型ダッシュボード）
1. 新しく作るなら `Artifact` の `action: "quickstart"`（`intent: "other"`）から Data 型で作る。前の回を更新するなら、その URL を使う（`ArtifactData` で `files`・`datasets`・`dash` を `list` して version を読む）。
2. `RUN/data` の 6 つの CSV（`profiles`・`matchups`・`techniques`・`curve`・`range_bins`・`range_summary`）を `Artifact` の publish（`url` + `asset: true`、CSV は 1 回に 1 つ）で上げ、返った `/_blob/<id>` を `RUN/assets.json` に書く。
3. `node tools/roster-report/make-writes.mjs <RUN> <RUN>/assets.json [<RUN>/versions.json] > <RUN>/writes.json` → その中身を `ArtifactData` の `batch` の `writes` に渡す（既存の文書には versions.json で `if_version` を付ける）。
4. `ArtifactData` で `data/users/me` の `check` を読む。誰もページを開いていないと `check` は出ない（そのときは段 8 の確認を頼りにし、「ダッシュボード上の点検はまだ」と伝える）。
5. 画像は data: URL を `images<N>.js` に分けて入れている（ダッシュボードの 1 文書は 256 KB まで。store が「ファイルは asset に」と注意を出すが、Data 型のページは画像を data: URL で持つ決まりなのでこのままでよい）。

---

## 3. 考察の書き方

`analyze.mjs` の出力から、少なくとも次を確かめて書く（初回の 6 項目が例）。数字はページの drawing / `dash.calc` で出せる物（相関・武器 対 素手）はそちらに任せ、文に書き写した数字は段 4 の出力と合っていることを確かめる。

1. **何が勝ちを決めているか**: 勝ち点率とダウン（取った・取られた）・与えたダメージ・受けたダメージの相関。初回はダウン 0.92 / −0.92、与えたダメージ 0.23（60 秒の試合は K.O. のラウンドが先に数えられるため）。
2. **体と強さ**: 素手・はめる物の選手で、体の重さ（身長³ × 筋肉²、`BodyBuild.MassRatio`）・腕の長さとの相関（CLAUDE.md §1.7 のとおり体から強さが出ているか）。
3. **武器 対 素手** の勝ち点率と、武器ごとの差（強い武器・弱い武器と、その理由を測定で示す）。
4. **得意な距離で戦えているか**: `best_gap_cm`（得意の真ん中 − いつもいる距離）。素手は遠すぎ・武器は近すぎ、のような傾向は教育（足の運びの目標距離）で直せる所（§1.8）。
5. **型ごとの傾向**（蹴り・ムエタイ・武器など）と、1 打あたり・当たり率・ダウンの関係。
6. **相性**: 上位の選手がはっきり負け越す相手、有利・不利がはっきり出た組の数。
7. **注意**: 60 秒の試合（5 ラウンドならスタミナで変わりうる）、20 試合の区間の幅、相関は人数が少ないので目安、試合のコマは勝っている場面とは限らない。

前の回と比べるときは、`docs/arena/` の前の記録と同じ表（勝ち点率・得意 / 不得意・技ごと）を並べ、物理・判断の版の違いを書く。

---

## 4. 近道・注意（測り方の限界）

- 間合いの測定は打ち出しの瞬間の距離で、当たった瞬間の距離（`impact_median_cm`）とは違う。
- 武器の選手の拳の技は、武器を落とした・折られた後にだけ出る（`DisarmedFallback`）。棒の「構えの武器が当たった」は技の外の接触で、得点にはなる。
- 得意・不得意は「その距離にいた間の差し引き」で、その距離に来た理由（追い込まれた・自分から詰めた）は分けていない。相手の混ざり方にも影響される。
- `maailab` は試合に触れない（ボットの状態を読むだけ。`BoxerBot.ActionOf` / `KickTime` は読み取り専用）。ラウンドが替わってボットが作り直されても、追跡は剣のボットの個体の替わりで数え直す。
- 1 回の総当たりは決定的（同じコミット・同じ種なら同じ結果）。`run-raytrek.sh` は HEAD のコミットを `rr-<コミット>` のワークツリーで回すので、何を測ったかは `RUN/commit.txt` で分かる。

## 5. ファイル

| ファイル | 役目 |
|---|---|
| `games/MiniRing.Arena/MaaiLab.cs` | `maailab`: 1 打ずつの追跡・技ごとの統計・距離の帯・得意 / 不得意 |
| `games/MiniRing.Arena/RosterLeague.cs` | `rosterleague --matrix`: 1 試合 1 行の書き出し（`OutcomeCsv`） |
| `games/MiniRing.Arena/PortraitExport.cs` | `portraits`: 肖像の PNG |
| `tools/roster-report/run-raytrek.sh` | raytrek で総当たりと間合いを回して持ち帰る |
| `tools/roster-report/shots.sh` | 肖像と試合のコマ・確認用の一覧 |
| `tools/roster-report/build-data.mjs` | 生の出力 → レポートの CSV |
| `tools/roster-report/analyze.mjs` | 考察の材料 |
| `tools/roster-report/build-images.mjs` | 選んだコマ・顔 → `images<N>.js` |
| `tools/roster-report/build-page.mjs` | ページ（`page/index.html`・`page/report.js` の型）を RUN の数字と考察で組む |
| `tools/roster-report/preview.mjs` | 手元の確認（`preview/mock.js` がダッシュボードの代わり） |
| `tools/roster-report/make-writes.mjs` | ダッシュボードの store へ書く文書の一覧 |
| `tools/roster-report/examples/<日付>/` | その回の考察（`findings.html`）とコマの選び（`choices.json`） |
