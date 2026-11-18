# エージェントの開発しやすさの改善計画（計画と記録を兼ねる）

2026-11-17 開始。ブランチ `claude/agent-dx`（ワークツリー `.claude/worktrees/agent-dx`）。
元のレビュー: 常に読む文書の肥大・変更の集中・散文の決まりが道具や機械の強制になっていない、の 3 つが速さ・トークン・構造の頑丈さを削っている。

**書き方**: 段ごとに「目的・やること・完了の条件・触るファイル」を先に書き、作業したら「記録」に日付・やったこと・測った数字・残したことを足す。計画を変えたら、変えた理由を記録に書く（消さない）。

**守ること**: ゲームの物理・判断の結果は変えない（`PhysicsVersion`・`LogicVersion` は上げない。上げる段があれば明記）。各段の終わりに全テスト（または段で決めた範囲）を通す。

**衝突の注意**: 開始時点の master に未コミットの作業がある（`games/MiniRing.Arena/Program.cs`・`RosterLeague.cs`・`MaaiLab.cs`（新規）・`games/MiniRing/Bots/BoxerBot.cs` の 105 行付近・`docs/MEDIA.md`）。これらのファイルを大きく触る段（3・4・8）は、その作業が master に入ってから行う。

| 段 | 内容 | 状態 |
|---|---|---|
| 1 | 常に読む文書を軽くする（CLAUDE.md・版の履歴） | 済（b33afd8） |
| 2 | テストを段に分ける（速い段と slow の印）、raytrek で全部を回す道具 | ほぼ済（5054896）。raytrek での全部の 1 回と並列の測定が残り |
| 3 | 試合の設定の可変 static を流れごとに持つ（AsyncLocal） | 済（4413ec8）。並列のテストの測定は未確定 |
| 4 | 見た目を 1 枚で確かめる `tools/sheet.py`（コマの格子 PNG） | 済（4413ec8） |
| 5 | 終わった plans を `plans/done/` へ・文書の表に「記録」の読み方 | 済（4413ec8）。大きい文書の分割は見送り |
| 6 | skill 3 つ・hook のスクリプト | 済（1b3aabb）。`.claude/settings.json` はユーザーが置く |
| 7 | 軸を機械で守る（ソースの検査のテスト） | 済（1b3aabb） |
| 8 | Arena の `help <cmd>`（組み込みの Usage.txt と switch の一致のテスト） | 済（2d0b600） |
| 9 | 採否の比べ直しを `docs/arena/confirm.jsonl` に 1 行ずつ | 済（43e265b） |
| 10 | Bots を別アセンブリへ・`Fighter` を部分クラスに分ける | 地図だけ作った。実行は grappling・spacing のマージの後（ユーザーと決める） |
| 11 | ワークツリー・ブランチの掃除 | 一覧を作った。消すのはユーザーの承認の後 |

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
- 2026-11-18: 段 1 をコミット（b33afd8）。
- 2026-11-18: 時間を測った。直近の全テスト（ワークツリー agent-a9d77545… の `full.trx`、2026-11-17）は 906 件・**63 分**（計画を書いたときの 16 分は 11-15 の 551 件の値で、その後テストが増えた）。MSTest の並列は設定が無く、全部直列。クラスごとの合計は上位 12 クラスで 75%（`CharacterSamenessTests` 678 s・`GroundStrikeTests` 600 s・`HandWearTests` 261 s・`BotTests` 212 s・`ShieldTests` 200 s・`ReplayTests` 165 s…）、3 秒未満のクラスを全部足しても約 55 s。
- 2026-11-18: **計画の変更**: 速い・物理・試合・調査の 4 段の予定を、`slow` の印 1 つ（速い段 = 印なし）にした。4 段に分けても回し方は「変更中」と「完了前」の 2 通りしかなく、印の付け方で迷う所を増やすだけのため。調査用は既存の `[TestCategory("Probe")]` と名前の `Probe` のまま。
- 2026-11-18: やったこと: 上の trx でクラスの合計が 3 秒以上の 59 クラスに `[TestCategory("slow")]` を付けた（`[TestClass]` の次の行。スクリプトで、クラス名が 1 回だけ見つかる所だけ）。`run.cmd test` を速い段、`run.cmd test all` を全部にした（**前の `run.cmd test` は Probe を外していなかった**ので、調査用まで回っていた）。CLAUDE.md §4.6・§5 を 2 段に書き直し、新しいテストの印の目安（クラスで 3 秒以上）を書いた。
- 2026-11-18: `tools/remote-test.sh`（raytrek で回す。前回送った所からの差分だけ bundle で送る・未コミットの差分と未追跡のファイルも送る・専用の `C:\Users\smiya\repos\remote-test` で回す・trx を持ち帰って要約）と `tools/trx-summary.py`（件数・失敗の 1 行目・遅いクラス）を作った。raytrek の `repos` には過去の手作業の bundle・patch・ワークツリーが 100 個ほど散らばっている（この道具で今後は増えない。掃除は段 11）。
- 2026-11-18: 速い段をこの PC で回した: **403 件・全部合格・テストの実行 59 秒**（ビルドを除く）。クラスに付けた `TestCategory` がクラスの全メソッドに効いている（遅いクラスは 1 つも走っていない）。
- 2026-11-18: 書いたスクリプトを別のエージェント（Sonnet）にレビューしてもらい、5 件を直した: bundle の取り込みを強制更新（`+HEAD:`。rebase・別の枝のあと止まった）、`dotnet test` の終了コードを返す（ビルド失敗・テストの落ちが見えなかった）、trx の要約で Timeout・Aborted・未実行も失敗に数える、空白だけのメッセージで落ちる、時刻の読み取り（Python 3.10 以前・小数なし）。ssh に keepalive と `< /dev/null` を足した。
- 2026-11-18: raytrek で `remote-test.sh fast` を 3 回: 初回（全部の履歴の bundle）・同じコミット（bundle なし）・1 コミット先（差分の bundle）の 3 つの経路がどれも通り、**403 件全部合格、送ってから要約まで約 2 分**。
- 2026-11-18: raytrek で `all` を回しかけて止めた。raytrek では別の作業（`rosterleague --parallel 14`・`maailab --parallel 12`・`handlelab`）が CPU を使っていて、1 時間のテストはそれを遅らせ、時間も比べられないため。そのとき分かったこと: こちらの ssh を止めても向こうの `dotnet test` は残る（自分の実行の PID だけを `taskkill /T` で止めた）。スクリプトに「raytrek で他の dotnet が動いている」警告と、止め方の案内を足した。
- 全部の段の件数: `all` のフィルタは `slow` の印を見ないので、印を付けても件数は変わらない（作りで保証される）。速い段 403 件 ＋ slow の 59 クラス。
- 2026-11-18（master の取り込み後）: 速い段が **552 件・10 分**に戻っていた。master で増えたテストのクラスに印が無かった（`KickShinTests` 435 秒・`BatFormsTests` 45 秒・`VfcCoreTests` 36 秒・`BreakRoutineTests` 31 秒・`ElbowContactTests` 8 秒・`ContinuousDefenseTests` 5 秒）。6 クラスに印を足した。**同じことが起きる**: 印は人（エージェント）が付けるので、付け忘れが速い段を遅くする。対策を 2 つ入れた: `tools/trx-summary.py` が「印が無いのに 3 秒以上かかったクラス」を名前で出す、`run.cmd test` が trx を `TestResults\local\` に書いてこの要約を出す（hook のスクリプトもテストを触ったときに思い出させる）。
- 2026-11-18: 付け直した後に `run.cmd test` で回した: **502 件・全部合格・テストの実行 36 秒**、要約が出る。3 秒の境目の近くで揺れるクラス（`FaceCamTests`・`BodyToFistTests` 3 秒前後）が毎回警告に出て雑音になるので、警告は 5 秒以上にした（印の目安は 3 秒のまま）。6 秒の `BreakCameraTests` には印を付けた。
- 残したこと: (1) raytrek が空いているときに `remote-test.sh all` を 1 回通し、全部の件数と時間をここに書く。(2) 並列（`--parallel N`、MSTest のクラス単位）は試していない。可変の static を書き換えるテスト（段 3）を直してから測る。1 時間の全テストが数分の 1 になる見込みで、段 3 の後の最初の仕事にする。

## 段 3 — 試合の設定の可変 static を引数に

**目的**: `BoxingGame.DefaultSubstepRate`・`DefaultMaxContactPasses`・`VoxelContact.SampleSpacingScale` は可変の static で、Probe が書き換え、並列に走るテストが読む（`WeaponContactPassTests`）。試合を作るときの設定（不変の record）に移す。

**完了の条件**: 上の 3 つが static でなくなる。全テストのチェックサムが変わらない（物理は同じ）。

**記録**
- 2026-11-18: **計画の変更**: 「試合を作るときの引数にする」をやめ、3 つの static の中身を `AsyncLocal` にした（`BoxingGame.DefaultSubstepRate`・`DefaultMaxContactPasses`・`VoxelContact.SampleSpacingScale`）。引数にするには Arena の約 20 の lab が試合を作る全経路へ設定を通す必要があり、大きく・ほかの枝と衝突する。`AsyncLocal` なら、書き換えは「書き換えた流れと、その後に始めたスレッド・タスク」にだけ効く: Arena（最初に 1 回設定 → 並列の試合へ流れる）はそのまま動き、Probe の書き換えは並列に走る別のテストに漏れない。既定値・物理は同じ（版は上げない）。読むたびに `AsyncLocal` を引く費用は接触の組 1 つに数十 ns。
- 2026-11-18: `SimulationDefaultsIsolationTests`（並行する 2 つの流れで、書き換えた側とその子だけが新しい値を見る）を足した。`WeaponContactPassTests` と合わせて合格。
- 2026-11-18: 並列（MSTest のクラス単位・4 並び）で速い段を回したが、直列で 1 分のものが十数分たっても終わらず止めた。この PC では同時に別のセッションのテスト（別ワークツリーの testhost）も走っていてメモリが足りなかった可能性が高く、詰まり（デッドロック）かは**未確定**。raytrek が空いたときに `bash tools/remote-test.sh fast --parallel 4` で測り直す。

## 段 4 — 見た目を 1 枚で確かめる `--sheet`

**目的**: 動画は mp4 なのでエージェントが中身を見るには毎回 ffmpeg でコマを抜いていた。`video` と lab に `--sheet out.png` を足し、決まった時刻のコマを格子に並べ、時刻と主要な値（両者の意識・スタミナ・直前の打撃）を重ねた 1 枚を出す。

**完了の条件**: `video --seconds 10 --sheet x.png` で 1 枚が出て、自分で読んで中身が分かる。visual テスト 1 本。

**記録**
- 2026-11-18: **計画の変更**: `video` と各 lab に `--sheet` を足す代わりに、どの mp4 にも使える `tools/sheet.py` にした（動画を作る経路が `video`・`kickvideo`・`imitate`・`guardlab --video`・`intentvideo`・`kickmocap` など 15 ほどあり、全部に足すより 1 本の道具の方が漏れない）。値（意識・スタミナ）は動画の HUD・中継の画面に出ているので、それを読む。visual テストは足していない（GPU を使わない道具なので、動画から 1 枚を作れたことを手で確かめた）。
- 2026-11-18: `media/gen02_1110_bots/video/miniring_champion_vs_jabber.mp4`（50 秒）を 8 コマの 1 枚（1920 × 540）にして読んだ: 時刻・コーナーの名前・ダメージ・技の名前（「1-2 ジャブ → ストレート」）・勝敗まで読める。ffprobe はこの PC に無いので、長さは ffmpeg の出力から読む。CLAUDE.md §4.7・§5 と skill `verify-visual` に使い方を書いた。

## 段 5 — 文書を「今の仕組み」と「記録」に分ける

**目的**: `BOT_PDCA.md`（149KB）・`LEARNING_PLAN.md`（58KB）・`VOXEL_DAMAGE.md`（54KB）などに今の真実と経緯が混ざり、エージェントが全部を読む。`docs/plans/` には終わった BRIEF/REPORT が 82 ファイル。

**やること**: 大きい文書ごとに「今の仕組み（短い）」を先頭か別ファイルにし、記録は `docs/log/` へ。終わった plans は `docs/plans/done/` へ（git に履歴は残る）。`GUIDE.html` の更新義務を見直す（ユーザーと相談）。

**記録**
- 2026-11-18: 報告（`*_REPORT_*`）が出た指示書と報告の 77 件を `docs/plans/done/` へ `git mv` した（102 → 25 件。残したのは計画・引き継ぎ・報告の無い指示書。報告の無い指示書の中にもマージ済みの物はあるが、終わったと言い切れないので残した）。参照を 40 ファイルで直した（`docs/plans/<名前>` → `docs/plans/done/<名前>`、移したファイルの相対リンクを 1 段上へ）。移したファイルに残るリンク切れ 21 件は、移す前から切れていた物（git の外の動画・根からの相対の道）。
- 2026-11-18: CLAUDE.md §3 の文書の表の下に「読み方」を足した: 表は今の仕組み、記録（`BOT_PDCA`・`arena/*`・`LEARNING_PLAN` の記録・`plans/done/`・`VERSIONS`）は要るときだけ見出しで探す、20KB を超える文書は `Grep` で節を探して読む。
- **見送ったこと**: 大きい文書（`BOT_PDCA` 149KB・`VOXEL_DAMAGE` 54KB…）を「今の仕組み」と「記録」へ分けること。分けるには各文書を全部読んで書き直す必要があり（数十万字）、その費用に対して、上の「Grep で節だけ読む」の決まりで読む量はほぼ同じだけ減らせるため。`BOT_PDCA` はもともと記録だけの文書で、今の仕組みは `BOTS.md` にある。`GUIDE.html` の更新の義務はユーザーの判断が要るので変えていない（提案: 世代を区切るマージのときだけ直す）。
- 指示書の運用: 報告が出たら指示書と報告を `plans/done/` へ移す（skill `delegate` の 5）。

## 段 6 — 繰り返す作業を道具にする

候補: `bump-version`（版を上げて VERSIONS.md に 1 行）・`verify-visual`（段 4 の 1 枚を撮って読む）・`codex-run`（指示書から起動・ログの進みの確認・止め方）・`remote-test`（段 2）・`pdca-entry`。hook: `ShaderSource.cs` を変えたら SPIR-V の作り直しを促す、物理のファイルを変えたら版の確認を促す。`.claude/settings.json` に dotnet・arena・ffmpeg の許可。

**記録**
- 2026-11-18: master（fdd6c08、agent-dx の段 1・2 はユーザーがマージ済み）へ早送りしてから続けた。
- 2026-11-18: skill を 3 つ足した: `verify-visual`（撮る → `tools/sheet.py` で 1 枚 → 読む → 伝える）・`bump-version`（どちらの版か・番号の衝突・VERSIONS.md・チェックサムの取り直し）・`delegate`（指示書の項目・起動・確かめ方・終わったら `plans/done/` へ）。`remote-test` と `pdca-entry` は skill にしなかった（前者は 1 行のコマンドで CLAUDE.md §5 にある。後者は BOT_PDCA.md の書式を見れば足りる）。
- 2026-11-18: hook のスクリプト `tools/hooks/post-edit.sh` を作った（編集したファイルの道で、シェーダーの SPIR-V・PhysicsVersion・LogicVersion・slow の印を、セッションに 1 回ずつ思い出させる。試した: 1 回目は出て exit 2、2 回目は黙る、関係ないファイルは黙る）。
- 2026-11-18: **プロジェクトの `.claude/settings.json`（許可と hook の登録）は書けなかった**（自動の権限判定で拒否。権限・hook はユーザーが決めること）。入れるなら次の内容（ユーザーが置く）:

```json
{
  "permissions": { "allow": [
    "Bash(dotnet build:*)", "Bash(dotnet test:*)", "Bash(dotnet run --project games/MiniRing.Arena:*)",
    "Bash(py -3 tools/sheet.py:*)", "Bash(py -3 tools/trx-summary.py:*)",
    "Bash(bash tools/remote-test.sh:*)", "Bash(bash tools/codex-usage.sh:*)", "Bash(ffmpeg:*)",
    "Bash(git status:*)", "Bash(git diff:*)", "Bash(git log:*)", "Bash(git show:*)" ] },
  "hooks": { "PostToolUse": [ { "matcher": "Edit|Write|MultiEdit",
    "hooks": [ { "type": "command", "command": "bash \"$CLAUDE_PROJECT_DIR/tools/hooks/post-edit.sh\"" } ] } ] }
}
```

## 段 7 — 軸を機械で守る

`Microsoft.CodeAnalysis.BannedApiAnalyzers` で試合のコード（`games/MiniRing`・`src/Aigis.Physics`）から `DateTime.Now`・種なしの `Random`・`Parallel` を禁止（§1.6）。依存の向きのテスト（Bepu の型が `Aigis.Physics` の外に出ない、Bots が `Fighter` の書き込みに触らない）。

**記録**
- 2026-11-18: **計画の変更**: NuGet の解析器ではなく、ソースを検査する速いテスト `ArchitectureRulesTests` にした（新しいパッケージが要らず、違反の一覧と直し方を日本語で出せる）。禁じたのは `games/MiniRing`・`src/Aigis.Physics`・`src/Aigis.Foundation` の種なしの `Random`・`Random.Shared`・`DateTime.Now/UtcNow/Today`・`Environment.TickCount`・`Guid.NewGuid`。試合の外の用途は `// determinism-ok: 理由` を書けば通る。`Parallel` と `Stopwatch` は禁じなかった（今の使い方は独立した試合の並列と計測だけで、試合の中の計算に入っていない）。もう 1 本は、BepuPhysics・BepuUtilities を参照するのが `src/Aigis.Physics` だけであること。
- 2026-11-18: 最初の実行で違反 3 件（記録のファイル名 2・記録の見出しの日時 1。どれも試合の外）が出たので印を付け、2 本とも合格。今のコードに本当の違反は無かった。
- 「Bots が Fighter の書き込みに触らない」は段 10（Bots を別アセンブリにする）で型で守る方がよいので、テストにはしていない。

## 段 8 — Arena のコマンドのレジストリ

`Program.cs`（1,783 行・55 コマンドの switch、使い方は先頭のコメントだけ）を、コマンドごとの名前・引数・説明を持つ表にし、`help` と `help <cmd>` を出す。知らない引数はエラーにする（今もしているかを確かめる）。

**記録**
- 2026-11-18: 知らない引数は `Options.Parse` がすでに誤字として拒んでいた。
- 2026-11-18: **計画の変更**: switch をコマンドのクラスの表に作り直すのはやめた（58 のコマンドを全部動かす大きな変更で、コマンドを足すほかの枝と毎回衝突する）。代わりに、先頭のコメントの使い方（41 行・23 コマンド分）を組み込みの `games/MiniRing.Arena/Usage.txt` へ移し、`help`・`help <cmd>`・知らないコマンドのときのコマンドの一覧を出すようにした。説明の無かった 35 のコマンドは、switch から引数の名前を書き出した行を「その他のコマンド」の節に足した（説明は後で足す）。`ArenaUsageTests` が switch と Usage.txt のコマンドが一致することを確かめるので、コマンドを足して使い方を書き忘れると速い段で落ちる。
- 2026-11-18: `help tune` が使い方と説明の 2 行を出し、`nosuch` がコマンドの一覧を出すことを確かめた。

## 段 9 — 測定の出力を共通にする

lab ごとに違う md の表を出している。`PairedEvaluation` を出口にして「始めの値と候補・使った種と使っていない種・差と揺らぎ」を同じ JSON（＋ md）で出し、採否を 1 つの道具で判定する。全 lab 共通の `--telemetry out.json`。

**記録**
- 2026-11-18: 統計の芯（`PairedEvaluation.Confirm`: 対にした差・t の区間・符号検定・判定）はすでにあり、採否の比べ直しは `cmatune`（2 か所）・`defconfirm`・`noiselab`（動作例）から呼ばれていた。
- 2026-11-18: **計画の変更**: 19 の lab の md を作り直すのでなく、採否を決める比べ直しの結果だけを同じ形で残す台帳 `docs/arena/confirm.jsonl` にした（`ConfirmLog.Append`。1 行 = 日時・道具・問い・A・B・版・種・`PairedResult` の全部と判定・t 値）。`cmatune` の 2 か所と `defconfirm` から呼ぶ。リポジトリの根の外（テストの出力の場所）では書かない。`ConfirmLogTests` を足した。CLAUDE.md §4.8 に「新しい比べ直しの道具は `ConfirmLog.Append` を呼ぶ」と書いた。
- 2026-11-18: 別のエージェント（Sonnet）のレビューで 1 件の本物の不具合: 差が全部の種で同じだと t 値が ±∞ になり、標準の JSON が書けずに例外 → `cmatune`・`defconfirm` の長い実行の最後で報告が出ないまま落ちる。±∞ を文字列で書く設定にし、捕まえる例外を広げ、そのテスト（`IdenticalDifferences_GiveAnInfiniteTValue_AndStillOneLine`）を足した。ほかの指摘: `AsyncLocal` で試合の結果が変わる経路は無い（全部の書き換えが同期で、`Parallel.For`・`Task.Run` は値を引き継ぐ）、Usage の読み取りと hook は問題なし、`sheet.py` で範囲外の時刻のコマが黙って落ちて番号がずれる → 警告を出し・時刻を切り詰め・頼んだ番号のまま描くよう直した。`ArchitectureRulesTests` の見逃し（文字列の中の `//` の後ろ・`/* */` の中のコード）は、誤って通す方向だけで、今の使い方では問題にならないので直していない。
- 見送ったこと: 全 lab 共通の `--telemetry`。中継（`--tv on`）の `*.events.json` が打撃・ダウンの時刻をすでに出しているので、要る lab が出てきたらそれを広げる。

## 段 10 — 構造の分割（Bots の別アセンブリ・Fighter の部分クラス）

`Fighter.cs`（3,669 行）は並行作業がいちばん衝突する所。部分クラス（意識・ダウン／握り・武器／体のバネ／脚／腕のサーボ）に分ける。Bots・IntentControl を別アセンブリにして、観測は読み取りのインターフェースだけ見せる。大きな移動なので、並行のブランチが master に入った後に一度にやる。

**記録**
- 2026-11-18: 実行していない。まだマージされていない枝が `Fighter.cs` を変えている: `claude/merge-grappling`・`claude/throwing-grappling-mechanics-de662d`（＋119 −45 行。マージ保留中）、`claude/spacing-control-features-a34e7d`（＋38 −4 行）。今メソッドを別のファイルへ移すと、git は移った塊を追えず、その枝のマージが手作業の解き直しになる。
- 分割の地図（`Fighter.cs` 3,719 行、2026-11-18 の master。メソッドだけを移し、フィールドは移さない（部分クラスのフィールドの初期化の順はファイルの順に依存するため））:

| 新しいファイル | 中身（今の行） |
|---|---|
| `Fighter.cs` | フィールド・生成（`Spawn`・`AddToes`）・`ApplyControl`・部位の問い合わせ（1〜870 の大半）・小さな計算の道具（3039〜） |
| `Fighter.Wield.cs` | `Wield`・`Support`・`Unwield`・`ReleaseSupport`・`LetGoSupport`・`HoldsOneHanded`・`BareFist`（697〜800） |
| `Fighter.Consciousness.cs` | `MeasureHeadJolt`・`TwistHead`・`RefreshOrgans`・`BruiseOrgans`・`Smooth`（934〜1110） |
| `Fighter.Grapple.cs` | `Push`・`Knockdown`・`SetGrabbed`・`UpdateGrab`・`ReleaseAll`・`TryGrab`・`Release`・`ClosestPointOnBox`・`HalfExtentsOf`（1110〜1280） |
| `Fighter.Down.cs` | `TickStanding`・`TouchesCanvas`・`UpdateRise`・`UpdateStir`・`Hash01`・`MaxPartSpeed`・`TickDown`・`PostOnTheCanvas`・`Enter`（1280〜1522） |
| `Fighter.Legs.cs` | `DriveBody`〜`ApplyGait`（足の運び・脚・膝の制御・ホバー・直立・歩き、1522〜2060） |
| `Fighter.Arms.cs` | `DriveArms`〜`SetTwistStrength`（腕・握りの滑り・体から拳へ・サーボの強さ、2060〜2449） |
| `Fighter.Injury.cs` | `Die`・`Break`・`BreakNeck`・肉のつなぎ・力の抜ける手（2449〜3003） |

- 実行の手順（そのとき）: (1) 上の 2 つの枝を master に入れる。(2) 行の範囲でなくメソッドの名前で塊を切り出すスクリプトで移す（各塊が 1 回だけ見つかることを確かめる、CLAUDE.md §6）。(3) ビルド → 速い段 → `ReplayTests`・`BotExecutionBaselineTests` のチェックサムが変わらないこと（純粋な移動なので 1 ビットも変わらない）。(4) その直後に Bots を別アセンブリにするかを、Bots から `Fighter` の `internal` への参照の数を数えてから決める。
- 時期はユーザーと決める。

## 段 11 — ワークツリー・ブランチの掃除

ワークツリー 34・ブランチ 35（`agent-*` の残り 10）。master へマージ済みかを一覧にして、ユーザーの承認を得てから消す。

**記録**
- 2026-11-18: 一覧を作った（`git worktree list` と `git branch --merged master`）。ワークツリーは 55（`.claude/worktrees` の外に 2: `E:/repos/ClaudeProjects/myvrgame1_nodelay`・一時の `mchk`）。
  - **master へマージ済み 32**（消す候補）: `boxer-lowpoly-impact-deform-4b1273`・`outfit`・`a-3d0203`・`voxel-damage`・`evo-p1a`・`evo-p0b`・`rush-physics`・`sword-combat-techniques-51f876`・`tvprogram`・`staff-vr-motion-kick-d8bd9a`・`integrate` と `agent-*` 21 個。
  - **マージされていない 21**: 残す（`roster`・`boots`・`referee`・`tvcard2`・`build-tactics`・`charfiles`・`tonfa`・`swordshield`・`bojutsu`・`fingers`・`neck`・`taekwondo`・`weapons`・`throwing-grappling-mechanics-de662d`・`vfc`・`spacing-control-features-a34e7d`・`wound-cam`・`agent-dx` と動いている `agent-*` 3 つ）。このうち 11/16 の作業の枝（`roster`〜`weapons`）は内容が別の道で master に入っているものが多いが、枝としては未マージなので、消すかは中身を比べてから。
- 消し方（承認の後）: `git worktree remove <道>`（`--force` は使わない: 未コミットの変更があるワークツリーは git が拒むので、作業の残りを消すことは無い）→ `git branch -d <枝>`（マージ済みでなければ git が拒む）。raytrek の `C:\Users\smiya\repos` の散らかり（bundle・patch・古いワークツリー 100 ほど）も同じく承認の後。
- 2026-11-18: ユーザーの指示「他のセッションに消していいか聞いてから消して」で、このリポジトリで動いている 9 セッションに候補の一覧を送って尋ねた。**分かったこと（尋ねてよかった）**:
  - `rush-physics` は「VGV連打問題」の作業中のワークツリー（未コミットの変更がある）。枝にまだコミットが無いので master と同じ所を指し、**マージ済みに見えていただけ**。消さない・枝も消さない。→ 「マージ済み」の判定だけでは消す候補を決められない。
  - `evo-p0b` は「PPOと進化アルゴリズムの検討」の作業場所。git が無視するコマの PNG（約 17 MB）と動画 4 本がある。**`git worktree remove` は無視されたファイルがあっても止まらずに消す**（上の「作業の残りを消すことは無い」は、無視されたファイルについては誤りだった）。消さない。
  - 「VGV WVC/VFCの差別化案」は、候補の外の `vfc`・`agent-a790ca3a…`・`agent-ab0e31be…`・`agent-a5eba920…` を作業中なので消さないでと。
  - 消してよいと返事: 「VGV VR動き・キック改善」（`staff-vr-motion-kick-d8bd9a`・`agent-a9402fb2…`）・「VGV WVC/VFCの差別化案」（`agent-acf3ee68…`・`agent-a8b403f6…`）・「PPOと進化アルゴリズムの検討」（`evo-p1a`・`agent-a1959952…`）。「VGV 距離コントロール機能」「VRボクシングゲーム POC (fork)」「VGV 物理簡易モデルの研究」は 32 個のどれも使っていない。
  - 返事待ち: 「剣術の技と防御システム」（`sword-combat-techniques-51f876` の持ち主。中に `media/` へ移していない動画 6 本・約 200 MB）・「VGB POC」。届かない: `a-3d0203`・`boxer-lowpoly-impact-deform-4b1273` の持ち主のセッション（動いていない）→ 返事が無いので消さない。
  - 消す前に、候補ごとに git が守らないファイル（無視・未追跡）を調べた（スクリプト）。ほとんどは作り直せる `.visualcheck` の確認画像。`media/` に無い動画: `sword-combat-techniques` の 6 本、`agent-a1f9ebe8…/.visualcheck/eyes_facecam.mp4`、`agent-a8e00fc3…/.visualcheck/emote_tv.mp4`。
- 2026-11-18: 消した: `staff-vr-motion-kick-d8bd9a`（持ち主のセッションがそこを作業ディレクトリにしているため空のフォルダだけ残った。持ち主に伝えた）・`agent-a8b403f6045b4eaa0`・`evo-p1a`。消せなかった: `agent-a9402fb2…`・`agent-acf3ee68…`・`agent-a1959952…` は持ち主のセッションのプロセス（生きている）が「使用中」のロックを掛けていて git が拒んだ。強制（`-f -f`）はしない。持ち主が片付けるまで残る。
- 2026-11-18: 残りの返事: 「剣術の技と防御システム」は `sword-combat-techniques-51f876` を残す（作業ディレクトリ。raytrek の動画 28 本を取り込んだばかり）、自分の Sonnet の 4 個は消してよい。「VGB POC」は `integrate` を残す（マージ作業でこれからも使う。候補の外の `wound-cam` は撮影中）、自分のサブエージェントの 7 個（動画 `eyes_facecam.mp4`・`emote_tv.mp4` を含む）は確認済みで消してよい、`tvprogram` はフォークのセッションの物（そのセッションは「どれも使っていない・未追跡のファイルも要らない」と返事済み）。
- 2026-11-18: 消した（合わせて 10）: `staff-vr-motion-kick-d8bd9a`・`agent-a8b403f6…`・`evo-p1a`・`agent-a94d7c75…`・`agent-adc65f53…`・`agent-aec43b30…`・`agent-ac5e365d…`・`agent-aff57442…`・`agent-a1f9ebe8…`・`tvprogram`。その枝 10 本も `git branch -d` で消した。ロックで消せなかった（持ち主のプロセスが生きている）: `agent-a9402fb2…`・`agent-acf3ee68…`・`agent-a1959952…`・`agent-ab893856…`・`agent-a9d77545…`・`agent-a53f41a7…`・`agent-a72b6b31…`・`agent-a8e00fc3…`（8 個。持ち主の許可は出ている）。
- 残す（持ち主が作業中）: `rush-physics`・`evo-p0b`・`sword-combat-techniques-51f876`・`integrate`。返事の届かない持ち主: `a-3d0203`・`boxer-lowpoly-impact-deform-4b1273`。持ち主が分からない 7 個（`outfit`・`voxel-damage`・`agent-aa499444…`・`agent-a420dd56…`・`agent-acff6231…`・`agent-a5637c51…`・`agent-a260c864…`）: 届くセッションの全部が「使っていない」と答え、中身は `.visualcheck`・`.claude` だけだが、返事の届かないセッション（「ワークツリーのアップデートをマージ」「動画作成」「足の断絶・骨折時の戦闘継続」）が持ち主の可能性があるので、消すかはユーザーが決める。
- 学んだこと: 「master へマージ済み」は消してよい条件にならない（コミットの無い作業中の枝もマージ済みに見える）。`git worktree remove` は無視されたファイルを消す。Claude のエージェントのワークツリーは使用中のロックが掛かっていて、持ち主のプロセスが生きている間は git が拒む（安全）。
