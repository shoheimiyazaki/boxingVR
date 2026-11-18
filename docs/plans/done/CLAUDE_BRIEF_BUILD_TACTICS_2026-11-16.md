# 作業指示（2026-11-16）: 体格に応じた戦い方

作業場所: ワークツリー `E:\repos\ClaudeProjects\myvrgame1\.claude\worktrees\build-tactics`（ブランチ `claude/build-tactics`）だけで作業する。master の作業ツリー・他のワークツリーには触らない。コミットはワークツリーの中でだけ（途中の区切りに使ってよい）。プッシュしない。日本語。

最初に読む: `CLAUDE.md`（§1 全部、特に §1.3・§1.7、§4、§6、§8 の「ボットの読み」）、`docs/BOTS.md`、`docs/BOT_PDCA.md`（書き方の例）、`docs/CHARACTERS.md` §5（名簿）、`games/MiniRing/Characters/characters.json`、`games/MiniRing/FighterCommand.cs`（`FighterSense`）、`games/MiniRing/BoxingGame.cs` の `Sense`、`games/MiniRing/Bots/BoxerBot.cs`・`RangeSense.cs`・`BotStyle.cs`・`OpponentRead.cs`、`BodyBuild.cs`、`games/MiniRing.Arena/Program.cs`（`league`・`video --red-char`）、`games/MiniRing/Bots/BotArena.cs`。

## ユーザーの指示
- 別の試合で「ザ・ハンマー」（マーカス: 身長 1.12・腕 1.1・筋肉 1.15、champion）と「アキラ」（比嘉 明: 身長 0.95・筋肉 1.05、karate）が善戦していた。
- キャラクターごとに、体格に応じた戦い方を最適化してほしい。

## Claude が決めたこと（軸との整合）
1. **キャラ名・体格の種類で分けない**（§1.3）。「大きいキャラならアウトボクシング」「小さいなら潜る」という表や `if` を書かない。戦い方は、**見えている体の寸法**（自分と相手の腕の長さ・身長・太さ）から**連続に**変わる 1 つのモデルにする。同じスタイル（例: champion を使うジンとマーカス）でも、体が違えば自然に違う戦い方になる。
2. **キャラごとの能力値・個別の数値表は持たない**（§1.7）。強さは体から出る。大きいキャラが強いのは正しい（勝率をそろえるのが目的ではない）。目的は「その体を活かした戦い方をする」こと。
3. 判断はボットの側（入力元）。出力は `FighterCommand` だけ。物理・技の経路は変えない（`PhysicsVersion` は上げない）。`BoxerBot.LogicVersion` を上げる。
4. ボットが見てよいのは見えている寸法: `FighterSense` の末尾に自分と相手の体格（`BodyBuild? SelfBuild = null, BodyBuild? OpponentBuild = null`。見えている箱の寸法そのもの）を足す。重さは見た目の大きさから見積もる（`H³ × M²` の比。体積に比例する質量のモデルと同じ式。`BodyBuild` に 1 つの式として置き、他で書き直さない）。

## やること（優先順）

### 1. 物差しを先に作る（直す前）
- Arena に `rosterleague` を足す: 名簿の全員（または `--chars jin,marcus,...`）の総当たり。キャラクターの値（スタイル・武器・はめる物・体格・顔）は `video --red-char` と同じ決め方。`--bouts N --seconds S --seed X --referee on|off --out docs/arena/<名前>.md`。
- 表に出す量（キャラごと）: 勝ち・K.O.・与えた/受けた打撃（ApplyHit の数と量）、**相手との距離の分布を自分の腕の長さと相手の腕の長さで割った値**（自分の届く所の外/両方の届く所の間/相手の届く所の内側にいた時間の割合）、手数・当たった数、ダウンの数、スタミナの平均。
- まず 1 試合の時間を測り、全体が 2 時間以内に収まる大きさに決める（この PC はメモリが少ない。Release で、並列に何本も走らせない）。
- **基準の表**（今のまま）を `docs/arena/build_tactics_baseline.md` に。マーカスとアキラが何をしていて勝てているか（間合い・手数・受けた量）を表から読み、仮説として `docs/BOT_PDCA.md` に書く。

### 2. 体格を読む戦い方の層（`BoxerBot`。拳・グローブ・素手・空手・はめる物のキャラ）
連続の値で変える（係数は `BotStyle` に置き、意味と測った根拠をコメントに。既定は全スタイル共通の値でよい。スタイル JSON は必要な所だけ）:
- **居たい距離**: 腕の長さの比 ρ = 自分 / 相手。長い方は自分の届く所の端（相手の届かない所）に居て、相手が入ってくる所を打つ。短い方は「相手だけが届く帯」に居る時間を短くし、相手の腕の内側（長い腕が窮屈な所）まで一気に入る。`RangeSense` の危険度・`RangePress` と同じ連続の量に、ρ で重みを掛ける形が望ましい（別の距離の式を作らない、§1.5）。
- **前に出る強さ・打ち合う気**: 見積もった重さの比が大きいほど前へ・打ち合いを受け入れる（重い体は押し負けない・一発が重いのは物理で出る）。軽い方は打って離れる。
- **守りの選び方の重み**: 自分の頭が相手より低いほど、潜る・かがむ（ダッキング・ウィービング）を、高いほど引く（スウェー・下がる）を選びやすく（`DefenseBook` の表の中から選ぶ重みだけ。回避の経路そのものは変えない）。
- **狙う高さの好み**: 相手が高いほど体（ボディ）を狙う重みを上げる（狙いの位置そのものは今の `MotionAim` が見た位置から出す）。
- 他に表から有効そうなもの（例: 溜めの大きさ・手数）は、見た量 → 仮説 → 測って採否、で足してよい。
- 剣士（`SwordsmanBot`）・こんぼう（`ClubBot`）は対象外。表には出す（報告に、続きでやるべきことを書く）。

### 3. 測って採否
- 係数は少数（4〜6 個）。探すときは基準と同じ種で比べ、**採る前に使っていない種で比べ直す**（`guardspar` と同じ考え）。
- 採る条件: (a) 距離の分布が体格に沿って変わる（腕の長い方ほど自分の届く所の端に、短い方ほど相手の内側に居る割合が増える。表の数値で示す）。(b) 拳のキャラ全体の成績（勝ち点・与えた/受けた打撃の差）が基準より悪くならない（使っていない種で）。(c) どのキャラも基準より大きく崩れない（崩れたら理由を書く）。
- 生の記録を `docs/arena/build_tactics_*.md`、サイクルごとの仮説 → 結果 → 採否を `docs/BOT_PDCA.md` に。

### 4. 確かめ
- テスト: (1) `BodyBuild` の重さの見積もりの式。(2) 同じスタイルで、腕の長い体と短い体を同じ相手に当てると、居る距離（腕の長さで割った値）が数値として違う向きに変わる（ヘッドレスの短い試合）。(3) 体格が同じ同士では今と同じ振る舞いに近い（係数が ρ = 1 で効かない形にする）。(4) リプレイの往復（`ReplayTests`）が通る。
- 動画（`video --red-char A --blue-char B --seconds 40 --speed 1`）を 2 本: `dmitri` 対 `ramon`（大と小の拳）、`marcus` 対 `akira`。前と後。間合いの違いが見える秒を報告に書く。ファイルは `media_tmp/`（git に入れない）。Claude が見て確かめる。
- 全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`、README の件数。
- 文書: `docs/BOTS.md`（体格を読む層）、`docs/CHARACTERS.md`（体格が戦い方に効く所）、`docs/BOT_PDCA.md`、`BoxerBot.LogicVersion` の履歴のコメント。

## 触らない
- 技の経路の表（`PunchBook.cs`・`DefenseBook.cs` の経路・`Technique.cs`）: 別の作業（技の進化）が書き換えている。選ぶ重みを足すのは `BoxerBot`/`BotStyle` の側で。
- `games/MiniRing.Arena/TvShowGraphics.cs`・`TvScout.cs`、`docs/plans/CODEX_BRIEF_14`〜`18`、`characters.json` の体格の値（変えたくなったら報告で提案）。

## 報告
最終報告 `docs/plans/done/SONNET_REPORT_BUILD_TACTICS_2026-11-16.md`（ワークツリーの中。先頭に「完了」か「未完了」）: マーカス・アキラが強かった理由（表から）、入れた層と式、係数と採否の表、基準と後の表（キャラごと）、動画の場所と見るべき秒、確かめていない所。最終メッセージは 1 回だけ。背景の処理を残さない。
