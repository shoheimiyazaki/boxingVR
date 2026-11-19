#!/usr/bin/env bash
# ワークツリーと枝の状態を 1 表にする（読むだけ。何も消さない）。docs/WORKFLOW.md §5 の片付けの前に使う。
#   bash tools/wt-status.sh            # 速い: 未コミットの数を数えない
#   bash tools/wt-status.sh --dirty    # 未コミットの数も数える（ワークツリーが多いと数分）
#   bash tools/wt-status.sh --ignored  # 無視されたファイル（動画・コマの PNG）の数も数える。git worktree remove はこれを黙って消す
# 列: 状態 / 先(master に無いコミット) / 後(master に遅れた数) / 最後のコミット日 / 未コミット / 無視 / 枝 / 道
# 状態: open = master に入っていないコミットがある / merged = 枝の先頭が master に入っている / detached / locked はエージェントが使用中
# 「merged かつ 先 0」でも作業中のことがある（コミット前の変更がある・コミットの無い新しい枝）。--dirty で確かめ、持ち主のセッションに聞いてから消す。
set -euo pipefail

count_dirty=0
count_ignored=0
for arg in "$@"; do
  case "$arg" in
    --dirty) count_dirty=1 ;;
    --ignored) count_dirty=1; count_ignored=1 ;;
    -h|--help) sed -n 2,9p "$0"; exit 0 ;;
    *) echo "知らない引数: $arg（--dirty / --ignored）" >&2; exit 2 ;;
  esac
done

root="$(git rev-parse --show-toplevel)"
base="${WT_BASE:-master}"

printf '%-7s %5s %5s %-10s %5s %5s  %-45s %s\n' 状態 先 後 最後 未 無視 枝 道
git -C "$root" worktree list --porcelain | awk '
  /^worktree /{ if (p) print p "\t" b "\t" l; p=substr($0,10); b="(detached)"; l="" }
  /^branch /{ b=substr($2,12) }
  /^locked/{ l="locked" }
  /^bare/{ p="" }
  END{ if (p) print p "\t" b "\t" l }' |
while IFS=$'\t' read -r path branch locked; do
  if [[ "$branch" == "(detached)" ]]; then
    state="detach"; ahead="-"; behind="-"
    head="$(git -C "$path" rev-parse HEAD 2>/dev/null || echo "")"
  else
    head="refs/heads/$branch"
    ahead="$(git -C "$root" rev-list --count "$base..$head")"
    behind="$(git -C "$root" rev-list --count "$head..$base")"
    if git -C "$root" merge-base --is-ancestor "$head" "$base"; then state="merged"; else state="open"; fi
  fi
  [[ -n "$locked" ]] && state="$state*"
  last="$(git -C "$root" log -1 --format=%cs "$head" 2>/dev/null || echo "?")"
  dirty="-"; ignored="-"
  if (( count_dirty )); then
    dirty="$(git -C "$path" status --porcelain 2>/dev/null | wc -l | tr -d ' ' || echo "?")"
  fi
  if (( count_ignored )); then
    ignored="$(git -C "$path" status --porcelain --ignored 2>/dev/null | grep -c '^!!' || true)"
  fi
  printf '%-7s %5s %5s %-10s %5s %5s  %-45s %s\n' "$state" "$ahead" "$behind" "$last" "$dirty" "$ignored" "$branch" "$path"
done | sort -k1,1r -k4,4r

echo
echo "* = locked（エージェント・セッションが使用中。git が remove を拒む）"
echo "枝だけ（ワークツリーの無い枝）で $base に入っている物:"
wts="$(git -C "$root" worktree list --porcelain)"
git -C "$root" for-each-ref --format='%(refname:short)' refs/heads | while read -r b; do
  [[ "$b" == "$base" ]] && continue
  # shelf/* は退避した作業（中身が master で打ち消されていても、再開の元なので消さない）
  [[ "$b" == shelf/* ]] && { echo "  $b（棚: 消さない）"; continue; }
  if ! grep -qxF "branch refs/heads/$b" <<<"$wts"; then
    if git -C "$root" merge-base --is-ancestor "refs/heads/$b" "$base"; then echo "  $b"; fi
  fi
done
