---
name: delegate
description: 実装・測定・動画づくりを Codex（無ければ Sonnet のサブエージェント）に渡す。指示書（docs/plans/CODEX_BRIEF_*.md）を書くとき、Codex を起動・監視・停止するとき、報告を確かめて取り込むときに使う。
---

# 指示書を書いて渡す

手順の詳細（起動のコマンド・止め方・残量・raytrek）は [docs/DELEGATION.md](../../../docs/DELEGATION.md)。ここは流れだけ。

1. **大きさ**: 1 回の実行で終わる粒度に分ける。触るファイルが重なる作業は並列にしない。並列にするときは作業ごとに別の git worktree。
2. **指示書** `docs/plans/CODEX_BRIEF_<題>_<日付>.md`: 目的・軸（CLAUDE.md §1 のどこに関わるか）・やること（優先順）・触ってよいファイル・**完了の条件**（数値のテスト・動画のコマ・`run.cmd test`）・版を上げるか（bump-version）・報告の書き先 `docs/plans/CODEX_REPORT_<題>.md`。
3. **起動**: 残量を `bash tools/codex-usage.sh` で見てから。`codex exec ... < /dev/null`（stdin を閉じないと止まる）。起動後にログが進むことを確かめる。尽きていれば同じ指示書を Sonnet（`Agent`、`model: sonnet`、`isolation: worktree`）へ。
4. **確かめる**: 報告はうのみにしない。差分を読み、緩めたテスト・測った数字・動画（verify-visual）を自分で確かめる。確かめていない数字は「報告では」と書く。
5. **片付け**: 報告が出たら、指示書と報告を `docs/plans/done/` へ `git mv` する。
