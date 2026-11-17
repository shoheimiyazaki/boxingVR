# 作業の分担と外部の実行（Codex・Sonnet・raytrek）

2026-11-17 に CLAUDE.md §10 から移した（文は元のまま）。CLAUDE.md §10 には要点だけを置く。

- **役割**: 重い判断（設計・軸との整合・採否・結果の検証）は Claude（指示する側）が行い、指示書 `docs/plans/CODEX_BRIEF_*.md` に決めたことを書く。実装・測定・動画は Codex に渡す。Codex の残量が無いときは Sonnet（Claude Code のサブエージェント、`model: sonnet`）に渡す。
- **モデルと推論の強さは依頼ごとに選ぶ**:
  - Codex `gpt-6.1-sol`: 設計の判断が混ざる・原因調査・物理の新しい仕組み（推論 medium〜high）。
  - Codex `gpt-6.1-terra`: 範囲がはっきりした実装・移植・テスト追加・文書（推論 medium。low は進みが小さく空回りした）。
  - Sonnet: Codex が尽きたときの代わり。同じ指示書を渡す。
- **粒度**: 1 回の実行で終わる大きさに分けてから渡す（例: ゲームパッド対応は 1 回で終わった。空手家 1 本に拳・指・パリー・蹴り 5 種・ドリルを詰めたら何度も再開になった）。指示書には優先順・完了の条件・確かめ方（数値のテスト・動画のコマ・全テスト）を書く。
- **並列**: 触るファイルが重ならない作業だけを並列にし、それぞれ別の git worktree で動かす（同じ作業ツリーで 2 つ動かすと、互いの途中の変更を「他人の変更」と見て止まった）。開発の速さだけでなくトークンの効率も見る: 合わせる作業・版の番号の整理が要るので、小さい作業は並列にせず順に流す。
- **Codex の残量**: `bash tools/codex-usage.sh`（最新のセッション記録の 5 時間枠・1 週間枠の使用率とリセット時刻）。依頼の前に見て、1 週間枠が残り少なければ terra・小さい粒度・Sonnet へ寄せる。
- **raytrek の Codex（別の PC・別の枠）**: `ssh raytrek`（ユーザーの全体の CLAUDE.md）。Codex はデスクトップアプリの `C:\Program Files\WindowsApps\OpenAI.Codex_*\app\resources\codex.exe` を使う（CLI は未導入）。**ssh から `codex exec` を直に叩くとサンドボックスが立ち上がらない**（`timed out ... connecting runner pipe-in`。ssh はログインした画面の外で動くため）。代わりに、指示書を `C:\Users\smiya\repos\p_test.txt` に置いて `ssh raytrek "schtasks /run /tn codexjob"` で起動する（ログイン中の画面の中で `C:\Users\smiya\repos\run-codex.ps1` が動き、`test1.log`・`test1.report.md`・終わりの印 `test1.done` を `C:\Users\smiya\repos\` に書く。モデルの既定は sol・medium）。サンドボックスの中の git は、raytrek の `.codex\config.toml` の `[shell_environment_policy.set]` で `safe.directory = C:/Users/smiya/Repos/myvrgame1`（大文字の `Repos`。小文字では一致しなかった）を渡して通している。リポジトリは `C:\Users\smiya\repos\myvrgame1`（git bundle と未コミットの差分を送って作る）。こちらの Codex が尽きたとき・並列で別の作業を回すときに使い、終わったら差分を持ち帰って確かめてから取り込む。
- **Codex の起動**: `codex exec -m <model> -c model_reasoning_effort="<effort>" -s workspace-write -C . --output-last-message <報告> "<依頼>" < /dev/null > <ログ> 2>&1`。`< /dev/null` を必ず付ける（無いと入力待ちで止まった）。起動後にログが進むことを確かめる。止めるときはプロセスの木ごと（`taskkill //PID <pid> //T //F`）止め、`codex.js exec` が残っていないことを確かめてから次を起動する（残った実行が同じファイルを同時に書き換えた）。ログに `usage limit` が出たら、Sonnet に引き継ぐか回復を待つ。
- **報告はうのみにしない**: テストを緩めた所・測った数字・動画は、Claude が確かめてからユーザーに伝える。確かめていない数字は「報告では」と書く。
