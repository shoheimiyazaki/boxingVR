# セッションとワークツリーの進め方（ワークフロー）

2026-11-19 に、それまでのセッション・ワークツリー・master の履歴を数えて決めた。CLAUDE.md §4（変更の決まり）・§10（分担）と [DELEGATION.md](DELEGATION.md)（Codex・Sonnet・raytrek への渡し方）の上に立つ、「並べて進めて、1 本ずつ入れる」決まり。

## 0. 要点

1. **役は 3 つ**: 統合役（1 本だけ、master の根の作業ツリーを書く）・作業セッション（1 作業 = 1 セッション = 1 ワークツリー = 1 枝）・読むだけのセッション（master の根で読む。書かない）。
2. **置き場所は 1 つ**: ワークツリーは `.claude/worktrees/<題>`、枝は `claude/<題>`。兄弟のディレクトリ（`myvrgame1-*`）は作らない。
3. **衝突しやすい値は統合役だけが書く**: README のテスト件数・CLAUDE.md §7・版の番号の確定（2026-11-20 からテストは固定のチェックサムの金型を持たないので、版を上げても取り直しは無い）。
4. **1 本ずつ入れ、全部のテストが通るまで次を入れない**（止める決まり。落ちたまま積まない）。
5. **書く作業は同時に 3〜4 本まで**。共有の芯（下の §6 の表）を 2 本の枝が同時に大きく触らない。
6. **片付けは統合の一部**: 入れたらセッションをアーカイブし、`bash tools/wt-status.sh --ignored` を見てからワークツリーと枝を消す。

## 1. 今の進み方（2026-11-19 に測った）

| 量 | 値 | 意味 |
|---|---|---|
| ワークツリー | 35（`.claude/worktrees` 22・兄弟のディレクトリ `myvrgame1-*` 11・一時 2） | 置き場所が 2 つあり、どれが生きているか一覧で分からない |
| `.claude/worktrees` の大きさ | 16 GB（兄弟のディレクトリは別に数える） | ビルドの出力と動画がワークツリーごとに溜まる |
| そのうち master に入っていて先のコミット 0 | 22 | 片付け待ち（ただし未コミットの変更が残る物が 3: `myvrgame1-glove` 25 件・`throwing-grappling-mechanics` 27 件・`evo-p0b`） |
| master の直のコミット（first-parent） | 11-17: 26・11-18: 39・11-19: 9 | 統合が 1 日に数十回 |
| 11-13 以降に変わった回数の多いファイル | README.md 103・CLAUDE.md 80・Arena/Program.cs 71・Fighter.cs 63・BoxerBot.cs 52・BoxingGame.cs 50 | 上の 2 つはコードでなく**件数と表**。どの枝も書くので毎回ぶつかる |
| 統合の後始末のコミット | README の件数 20 回・チェックサムの取り直し 18 回 | 枝が書いた値を統合でまた書き直している |
| 版の番号の衝突 | VERSIONS.md の 43・44 が 2 つの意味（⚠ の印） | 枝で番号を確定したため |
| 統合のための枝 | `integrate`・`merge-four`・`merge-hand`・`merge-grappling` | 作るたびに片付けが増える |
| master の根で同時に動くセッション | 11-19 に 5 本以上（統合の「VGB POC」と、その子の session-2・session-3・totalsegmentator、「テスト実行の最適な場所」など） | 統合の作業ツリーで別のセッションが書くと、互いの途中の変更が混ざる（DELEGATION.md の「同じ作業ツリーで 2 つ動かすと止まった」と同じ） |
| 全テストを通す前に次を入れた | `d4ed4c1`（face-f2 の統合）の時点で 4 件が落ちている（AGENT_DX_PLAN 段 2） | 落ちたテストの上に次の統合が積まれ、原因の枝が分かりにくくなる |
| master から遠く離れた枝 | 245 遅れ（throwing-grappling）・144 遅れ（spacing） | 長く離れるほど取り込みが重い。版が 2 つの意味になったのもここ |
| hook | `tools/hooks/post-edit.sh` はあるが `.claude/settings.json` が無く、登録されていない | 版・シェーダーの思い出させが働いていない |

うまく行っていること（続ける）: 指示書と報告（進行中 40・done 77）、計画と記録を兼ねる `*_PLAN.md`、退避の `shelf/<名前>` と `*_SHELVED.md`、速い段・全部の 2 段のテスト、`bump-version`・`verify-visual`・`delegate` の skill、ロックの掛かったワークツリーを git が消させないこと。

## 2. 役

### 統合役（1 本だけ）
- master の根（`E:/repos/ClaudeProjects/myvrgame1`）の作業ツリーに**書いてよいのはこの役だけ**。今は「VGB POC」のセッション。
- 仕事: 枝を 1 本ずつ取り込む（§4）・版の番号を確定する・README の件数・CLAUDE.md §7 の表・指示書を `done/` へ・片付け（§5）。
- 自分では機能を書かない。書く作業が出たら作業セッションを立てる（小さい直し・統合で要る直しは可）。

### 作業セッション
- **1 作業 = 1 セッション = 1 ワークツリー = 1 枝**。アプリで「ワークツリー付き」で作るか、手で `git worktree add .claude/worktrees/<題> -b claude/<題> master`。
- 題は短い英字（`face-f3`・`vitals`）。日付や乱数の付いた自動の名前でもよいが、セッションの題と対応が分かるように。
- サブエージェント（`Agent` の `isolation: worktree`、`worktree-agent-*`）は親の作業の部品。親が取り込み、親が片付ける。

### 読むだけのセッション
- 調査・相談・動画の確認・YouTube の編集などは master の根で動いてよい。**追跡されているファイルを書かない**。出す物は git の外（`media/`・scratchpad）だけ。
- 書く必要が出たら、その時点でワークツリーへ移る（アプリの「ワークツリーへ移す」か、新しい作業セッション）。

## 2.5 セッションの作り方と指示の型（ユーザー向け）

- **作業セッション**: アプリで新しいセッションを作るときにワークツリーを使う設定にする（`.claude/worktrees/<題>` に `claude/<題>` の枝ができる）。または統合役に「〜を別のセッションで」と頼む（統合役がワークツリー付きで立てる）。
- **読むだけのセッション**: ワークツリー無しで master の根に作る。最初の指示に「読むだけ・ファイルを書かない」と入れる。
- **ユーザーは型を書かなくてよい**（2026-11-19 の指定、CLAUDE.md §9「既定の進め方」）。例外を言わない限り、エージェントが下の型の中身（完了の条件・触る範囲）を自分で決めて最初に言い、ワークツリーへ移り、終わったら枝にコミットして「出せる」と報告する。下の型は、エージェントが自分で埋める項目の一覧として残す。
- 最初の指示の型（作業セッション）:

```
<やりたいこと>。
完了の条件: <見た目・数字・動画など、どうなっていれば終わりか>
触ってよい範囲: <分かれば。分からなければ「先に調べて領域を言って」>
docs/WORKFLOW.md の作業セッションとして進めて。終わったらコミットして「出せる」と報告して。
```

- 統合役への指示の型: 「`claude/<題>` を取り込んで」「出せる枝を全部 1 本ずつ取り込んで」「今日の片付けをして（一覧を見せて）」。
- 1 つのセッションで別の話を始めない。別の作業は別のセッションに（そのセッションの文脈を使う続きなら同じセッションで）。

## 3. 1 本の作業の流れ

**始め**
1. 題・目的・**触るファイル（領域）**を決める。`bash tools/wt-status.sh` とセッションの一覧で、生きている枝と領域が重ならないかを見る。重なる → 並列にせず順に（DELEGATION.md の「並列」）。
2. master の最新から枝を切る。
3. 大きい作業は計画を `docs/plans/<題>_PLAN.md` に書く（計画と記録を兼ねる。完了の条件と確かめ方を先に書く）。

**途中**
- 小さくコミットし、速い段（`run.cmd test`）で確かめる。
- master に **30 コミット以上**遅れたら、または共有の芯（§6）が master で動いたら、`git merge master` を枝の中で行い、枝の側で解く。遠く離れてからまとめて解かない。
- 版: 物理・判断を変えたら枝の中でも版を上げる（チェックサムのテストに要る）。ただし VERSIONS.md には「**次の版（枝 `claude/<題>`）**: 中身」と書き、番号は統合役が master の次の番号に付け直す（`bump-version`）。
- **枝では書かない**: README のテスト件数・CLAUDE.md §7・`docs/GUIDE.html`。必要なら報告に「README の件数 +N」「§7 に 1 行」と書いて統合役へ渡す。

**終わり（出せる条件）**
- 速い段が通る。見た目・動きは `verify-visual` で自分で見た。`code-review` の指摘を直したか、直さない理由を書いた。
- 計画（または報告）に、やったこと・測った数字・残したこと・統合役への頼み（版・件数・§7）を書いた。
- コミットして、統合役（かユーザー）に「出せる」と伝える。未コミットの変更を残したまま「終わった」と言わない。

## 4. 統合（統合役の手順）

1. master の根が clean（`git status` が空）なことを確かめる。汚れていたら、誰の変更かを先に突き止める（読むだけのセッションが書いていないか）。
2. **1 本ずつ** `git merge --no-ff claude/<題>`。統合のための別の枝（`integrate`・`merge-*`）は作らない（作るたびに片付けと二重の差分が増える。ゲートは 3. で取る）。
3. 衝突を解く。版の番号は `bump-version` で master の次に確定する。
4. 速い段 → **全部**（`bash tools/remote-test.sh`、tower32 で約 5 分）。
   - 落ちたら**次の枝を入れない**。原因がその枝なら、直せる小さい物は直す。大きい物はマージを取り消して（`git revert -m 1 <マージ>`、履歴を書き換えない）枝の持ち主へ返す。
   - 前から落ちていたテストなら、その事実を計画か報告に書き、直す作業を立ててから進む。
5. 後始末を 1 コミットで: README の件数・CLAUDE.md §7・指示書と報告を `docs/plans/done/` へ。
6. §5 の片付け。
7. まとめて入れるときも 1 本ずつ 1〜4 を回す。全部（5 分）は最後に 1 回でもよいが、落ちたら 1 本ずつ戻して原因の枝を探す。

## 5. 片付け

- 統合が済んだ枝: 持ち主のセッションに残りが無いか聞く → セッションをアーカイブ → `bash tools/wt-status.sh --ignored` → 無視されたファイル（動画・コマの PNG）があれば `media/<世代>/` へ移す → `git worktree remove <道>`（`--force` を使わない）→ `git branch -d <枝>`（`-D` を使わない）。
- **消さない**: ロック（`*`）の掛かった物・未コミットの変更がある物・`shelf/*`・生きているセッションの作業場所。「master に入っている」だけでは消す理由にならない（コミットの無い作業中の枝もそう見える。AGENT_DX_PLAN 段 11 の `rush-physics`）。
- `git worktree remove` は**無視されたファイルを黙って消す**。だから `--ignored` を先に見る。
- 統合役が 1 日の終わりに 1 回。

## 6. 並列の数と、触る場所の分け方

- 書く作業は同時に **3〜4 本まで**。理由: この PC は RAM が少なく、別のワークツリーのテストが重なると止まる（AGENT_DX_PLAN 段 3）・統合は 1 本ずつで、待ち行列が長いほど枝が離れる・Remote Control で見ているユーザーの注意も資源。小さい作業は並列にせず順に流す（DELEGATION.md）。
- 重い計算（全テスト・リーグ・動画）は tower32 へ。ワークツリーの中から本体のスクリプトを呼ぶと、そのワークツリーが送られる: `bash /e/repos/ClaudeProjects/myvrgame1/tools/remote-test.sh [all|fast|probe:<名前>|filter:<式>]`（`all` は 12 並びで約 4.6 分。相手の PC の置き場所は `rt-<作業ツリーの名前>-<1〜4>` で、回すたびに空いている枠を取るので、同時に回しても取り合わない）。raytrek（`--host raytrek`）は頼まれたときだけ。
- **共有の芯**（11-13 以降に枝が多く触った順）: `Fighter.cs`・`BoxerBot.cs`・`BoxingGame.cs`・`BoxingGame.Weapons.cs`・`Footwork.cs`・`FighterTuning.cs`・`BotStyle.cs`・`Arena/Program.cs`・`VideoMaker.cs`・`CharacterSamenessTests.cs`。2 本の枝が同時に、ここを大きく（メソッドの移動・書き直し）触らない。足すだけなら並列でよい。
- 新しい仕組みは、芯に書き足すより新しいファイル（部分クラス `Fighter.<題>.cs`・新しいコマンドのファイル）に置く方がぶつからない。

## 7. AI エージェントで開発するときの定石と、ここでの状態

調べた先: Anthropic の公式の best practices（code.claude.com/docs/en/best-practices）と、2026 年の実践の記事（ワークツリーで並列・仕様を先に・統合の層）。出典の一覧は末尾。

| 定石 | ここでの状態 |
|---|---|
| コンテキストは資源。常に読む文書は短く、コードから推測できないことだけ | 済（agent-dx 段 1・5: CLAUDE.md を軽くし、記録は節だけ読む） |
| 探る → 計画 → 書く → コミット | 済（`*_PLAN.md`・指示書） |
| エージェントに自分で確かめる手段を与える（テスト・画像） | 済（2 段のテスト・`verify-visual`・`sheet.py`）。全部を統合のゲートにしていないのが穴 → §4 |
| 仕様を先に、小さく分ける（ファイルと境目をはっきり） | 済（指示書の「1 回で終わる粒度」）。「触るファイル」を始めに宣言するのを足した → §3 |
| 1 作業 1 ワークツリー、重ならない作業だけ並列 | 半分。置き場所が 2 つ・master の根に書くセッションが同居 → §2 |
| 統合の前に検査（入れた後でなく、入れる所で） | 未 → §4 の止める決まり |
| 生きている枝の重なりを見る統合の層 | 新しく `tools/wt-status.sh` |
| 枝を短く、幹の近くに保つ（trunk-based） | 未（245 遅れの枝）→ §3 の 30 コミットの決まり |
| 派生する値（件数・表）を手で何度も書かない | 未（README 103 回）→ 統合役だけが書く。さらに件数を README から消すか自動にするのはユーザーの判断 |
| 機械で守る（hook・構造のテスト） | 半分（`ArchitectureRulesTests` は済。hook は未登録） |
| 並列は費用が掛け算。並べる意味があるのは独立した作業だけ | §6 の 3〜4 本 |
| 報告をうのみにしない | 済（DELEGATION.md） |

## 8. まだ決めていないこと（ユーザーの判断）

- `.claude/settings.json` に hook と許可を置くか（中身は AGENT_DX_PLAN 段 6 にある）。
- 片付け待ちの 22 個を消すか（未コミットの 3 つは持ち主に聞いてから）。兄弟のディレクトリ 11 個も同じ。
- README のテスト件数を残すか（残すなら統合役だけが書く。やめるなら `trx-summary.py` の出力で足りる）。

## 出典

- Anthropic, Claude Code best practices: https://code.claude.com/docs/en/best-practices
- Augment Code, How to run a multi-agent coding workspace: https://www.augmentcode.com/guides/how-to-run-a-multi-agent-coding-workspace
- Addy Osmani, How to write a good spec for AI agents: https://addyosmani.com/blog/good-spec/
- Why AI coding agents need smart integration workflows: https://nameocean.net/article/why-ai-coding-agents-need-smart-integration-workflows-not-just-merge-queues/
- Xebia AI playbooks (development): https://ai-playbooks.xebia.com/development/
- KDnuggets, Git worktrees for AI development: https://www.kdnuggets.com/git-worktrees-for-ai-development
