# エージェントの開発しやすさの改善計画（計画と記録を兼ねる）

2026-11-17 開始。ブランチ `claude/agent-dx`（ワークツリー `.claude/worktrees/agent-dx`）。
元のレビュー: 常に読む文書の肥大・変更の集中・散文の決まりが道具や機械の強制になっていない、の 3 つが速さ・トークン・構造の頑丈さを削っている。

**書き方**: 段ごとに「目的・やること・完了の条件・触るファイル」を先に書き、作業したら「記録」に日付・やったこと・測った数字・残したことを足す。計画を変えたら、変えた理由を記録に書く（消さない）。

**守ること**: ゲームの物理・判断の結果は変えない（`PhysicsVersion`・`LogicVersion` は上げない。上げる段があれば明記）。各段の終わりに全テスト（または段で決めた範囲）を通す。

**衝突の注意**: 開始時点の master に未コミットの作業がある（`games/MiniRing.Arena/Program.cs`・`RosterLeague.cs`・`MaaiLab.cs`（新規）・`games/MiniRing/Bots/BoxerBot.cs` の 105 行付近・`docs/MEDIA.md`）。これらのファイルを大きく触る段（3・4・8）は、その作業が master に入ってから行う。

| 段 | 内容 | 状態 |
|---|---|---|
| 1 | 常に読む文書を軽くする（CLAUDE.md・版の履歴） | 済（未コミット） |
| 2 | テストを段に分ける（速い・物理・試合・調査）、raytrek で全部を回す道具 | 未着手 |
| 3 | 試合の設定の可変 static を、試合を作るときの引数にする | 未着手（Program.cs 待ち） |
| 4 | 見た目を 1 枚で確かめる `--sheet`（コマの格子 PNG ＋ 値） | 未着手（Program.cs 待ち） |
| 5 | 文書を「今の仕組み」と「記録」に分ける・終わった plans を片付ける | 未着手 |
| 6 | 繰り返す作業を skill・スクリプト・hook にする | 未着手 |
| 7 | 軸を機械で守る（禁止 API の解析器・依存の向きのテスト） | 未着手 |
| 8 | Arena のコマンドをレジストリにして `help <cmd>` | 未着手（Program.cs 待ち） |
| 9 | 測定の出力を共通の JSON にする（採否の比べ方を 1 つに） | 未着手 |
| 10 | Bots を別アセンブリへ・`Fighter` を部分クラスに分ける | 未着手（並行作業が落ち着いてから。時期はユーザーと決める） |
| 11 | ワークツリー・ブランチの掃除 | 未着手（消すものはユーザーの承認を得てから） |

---

## 段 1 — 常に読む文書を軽くする

**目的**: 毎セッション読まれる CLAUDE.md（62KB）と、`Fighter.cs`・`BoxerBot.cs` の 1 行 1 万字の版の注釈を減らし、トークンと読み違いを減らす。版の注釈はマージで履歴が重複し、39・40・41 番が 2 つの意味を持っていた。

**やること**
1. `PhysicsVersion`・`LogicVersion` の履歴を `docs/VERSIONS.md` へ移す（重複を 1 つにし、番号が 2 つの意味を持つ所は枝の名前を添えて並べる）。定数の注釈は「今の版の 1 行 ＋ 履歴は docs/VERSIONS.md」にする。
2. CLAUDE.md の §8（軸から外れている所、34KB）を `docs/DEBT.md` へ移す。CLAUDE.md には「項目の名前と 1 行の要点 ＋ 詳しい所」だけを残す。
3. CLAUDE.md の §7（並行作業）を、今も動いている物だけの短い表にする（マージ済みの物は handoff の文書へのリンク 1 行）。
4. §5 の道具の表を、よく使う物に絞り、残りは `docs/TOOLS.md` へ（Arena の全コマンドの使い方）。
5. §4 の決まりに「版の履歴は docs/VERSIONS.md に書く」「新しい負債は docs/DEBT.md に書く」を入れる。

**完了の条件**: CLAUDE.md が 15KB 以下で、軸（§1）の中身は削っていない。移した文書の内容に抜けが無い（移す前後で項目の数を数える）。ビルドが通る（コメントだけの変更）。

**触るファイル**: `CLAUDE.md`・`docs/DEBT.md`（新）・`docs/VERSIONS.md`（新）・`docs/TOOLS.md`（新）・`games/MiniRing/Fighter.cs`（176 行の注釈だけ）・`games/MiniRing/Bots/BoxerBot.cs`（215 行の注釈だけ。master の未コミットの変更は 105 行付近で重ならない）。

**記録**
- 2026-11-17: ワークツリーを master の 49f8381 から作った。計画書を書いた。
- 2026-11-17: **計画の変更**: 完了の条件「15KB 以下」を「25KB 以下」に改めた。節ごとに測ると §1（軸）だけで 8.7KB、§3（構成と文書の表）4.5KB あり、軸を削らずに 15KB にはできないため。
- 2026-11-17: やったこと（スクリプトで機械的に移し、文は書き換えていない）:
  - `docs/VERSIONS.md`（新）: `Fighter.PhysicsVersion`（1 行 1 万字超）と `BoxerBot.LogicVersion`（19 行）の注釈を項目ごとに分けて新しい順に並べた。項目の数は Physics 55 → 重複を除いて 22（23〜42 番、39・40 は 2 つずつ）、Logic 36 ＋ 最初の注釈のまとめ 1 = 37。完全に同じ文の重複（38 番が 3 回など）は 1 つにした。同じ番号で別の意味の所（Physics 39・40、Logic の一部）に ⚠ を付けた。定数の注釈は今の版の 1 行だけにした。
  - `docs/DEBT.md`（新）: CLAUDE.md §8 の 28 項目をそのまま移した。§8 には DEBT.md へのリンクと項目の名前だけを残した。
  - `docs/TOOLS.md`（新）: §5 の道具の表を全部移した。§5 にはよく使う 6 行だけを残した。
  - `docs/DELEGATION.md`（新）: §10（Codex の起動・raytrek の手順）を移した。§10 には要点 4 行だけを残した。
  - §7（並行作業）をマージ済みの流れを除いた 3 行にした（元の文は git の 49f8381 にある）。
  - §4 の版の決まりに「履歴は docs/VERSIONS.md に書く・番号がぶつかったらマージで次の番号にして両方の枝の名前を書く」を足した。§3 の文書の表に新しい 4 つの文書を足した。
- 2026-11-17: 確かめたこと: CLAUDE.md 62KB → 24.4KB（移した先は DEBT 29.7KB・TOOLS 6.1KB・DELEGATION 4.2KB。元の節より大きいのは見出しの分だけで、抜けは無い）。C# は注釈だけの変更で、`dotnet build games/MiniRing.Arena -c Release` は警告 0・エラー 0、`arena version` は `physics 42 logic 41`（変わっていない）。注釈だけなのでテストは回していない。
- 残したこと: コードやほかの文書の「CLAUDE.md §8」という参照（`FighterTuning.cs`・`Weapon.cs`・`GroundStrike.cs`・`KARATE.md` など）はそのままにした。§8 から DEBT.md へリンクしているのでたどれる。並行の枝との衝突を避けるため、書き換えは段 10 などで該当のファイルを触るときに行う。並行の枝が CLAUDE.md §8 に項目を足していたら、マージのときに DEBT.md へ移す。

## 段 2 — テストを段に分ける

**目的**: 全テストが約 16 分（2026-11-15 の trx）かかり、この PC では回すたびに止まる。変更中は速い段だけ、完了の前に全部（raytrek で）を回す流れにする。

**やること**
1. 各テストクラスの時間を測る（trx から）。
2. `[Trait("Speed", "fast")]` 等で分ける: `fast`（純関数、合わせて 1 分未満）・`physics`（ヘッドレスの物理、短い）・`match`（試合を回す、長い）・Probe は `Category=probe`（名前の慣習に頼らない）。
3. 決まったフィルタを `run.cmd test fast|all` と CLAUDE.md に書く。
4. `tools/remote-test.sh`: 今のコミット＋未コミットの差分を raytrek へ送り、全テストを回して trx の要約（件数・失敗・時間）を返す。

**完了の条件**: fast 段が 1 分台で終わる。全テストの件数が分ける前と同じ。raytrek で 1 回通して要約が返る。

**記録**
- （未着手）

## 段 3 — 試合の設定の可変 static を引数に

**目的**: `BoxingGame.DefaultSubstepRate`・`DefaultMaxContactPasses`・`VoxelContact.SampleSpacingScale` は可変の static で、Probe が書き換え、並列に走るテストが読む（`WeaponContactPassTests`）。試合を作るときの設定（不変の record）に移す。

**完了の条件**: 上の 3 つが static でなくなる。全テストのチェックサムが変わらない（物理は同じ）。

**記録**
- （未着手。`Program.cs` の `ApplySimplify` を触るので master の作業が入ってから）

## 段 4 — 見た目を 1 枚で確かめる `--sheet`

**目的**: 動画は mp4 なのでエージェントが中身を見るには毎回 ffmpeg でコマを抜いていた。`video` と lab に `--sheet out.png` を足し、決まった時刻のコマを格子に並べ、時刻と主要な値（両者の意識・スタミナ・直前の打撃）を重ねた 1 枚を出す。

**完了の条件**: `video --seconds 10 --sheet x.png` で 1 枚が出て、自分で読んで中身が分かる。visual テスト 1 本。

**記録**
- （未着手）

## 段 5 — 文書を「今の仕組み」と「記録」に分ける

**目的**: `BOT_PDCA.md`（149KB）・`LEARNING_PLAN.md`（58KB）・`VOXEL_DAMAGE.md`（54KB）などに今の真実と経緯が混ざり、エージェントが全部を読む。`docs/plans/` には終わった BRIEF/REPORT が 82 ファイル。

**やること**: 大きい文書ごとに「今の仕組み（短い）」を先頭か別ファイルにし、記録は `docs/log/` へ。終わった plans は `docs/plans/done/` へ（git に履歴は残る）。`GUIDE.html` の更新義務を見直す（ユーザーと相談）。

**記録**
- （未着手）

## 段 6 — 繰り返す作業を道具にする

候補: `bump-version`（版を上げて VERSIONS.md に 1 行）・`verify-visual`（段 4 の 1 枚を撮って読む）・`codex-run`（指示書から起動・ログの進みの確認・止め方）・`remote-test`（段 2）・`pdca-entry`。hook: `ShaderSource.cs` を変えたら SPIR-V の作り直しを促す、物理のファイルを変えたら版の確認を促す。`.claude/settings.json` に dotnet・arena・ffmpeg の許可。

**記録**
- （未着手）

## 段 7 — 軸を機械で守る

`Microsoft.CodeAnalysis.BannedApiAnalyzers` で試合のコード（`games/MiniRing`・`src/Aigis.Physics`）から `DateTime.Now`・種なしの `Random`・`Parallel` を禁止（§1.6）。依存の向きのテスト（Bepu の型が `Aigis.Physics` の外に出ない、Bots が `Fighter` の書き込みに触らない）。

**記録**
- （未着手）

## 段 8 — Arena のコマンドのレジストリ

`Program.cs`（1,783 行・55 コマンドの switch、使い方は先頭のコメントだけ）を、コマンドごとの名前・引数・説明を持つ表にし、`help` と `help <cmd>` を出す。知らない引数はエラーにする（今もしているかを確かめる）。

**記録**
- （未着手）

## 段 9 — 測定の出力を共通にする

lab ごとに違う md の表を出している。`PairedEvaluation` を出口にして「始めの値と候補・使った種と使っていない種・差と揺らぎ」を同じ JSON（＋ md）で出し、採否を 1 つの道具で判定する。全 lab 共通の `--telemetry out.json`。

**記録**
- （未着手）

## 段 10 — 構造の分割（Bots の別アセンブリ・Fighter の部分クラス）

`Fighter.cs`（3,669 行）は並行作業がいちばん衝突する所。部分クラス（意識・ダウン／握り・武器／体のバネ／脚／腕のサーボ）に分ける。Bots・IntentControl を別アセンブリにして、観測は読み取りのインターフェースだけ見せる。大きな移動なので、並行のブランチが master に入った後に一度にやる。

**記録**
- （未着手）

## 段 11 — ワークツリー・ブランチの掃除

ワークツリー 34・ブランチ 35（`agent-*` の残り 10）。master へマージ済みかを一覧にして、ユーザーの承認を得てから消す。

**記録**
- （未着手）
