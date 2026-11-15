#!/usr/bin/env bash
# Codex の残量を表示する（最新のセッション記録の rate_limits から）。
# primary = 5 時間の枠、secondary = 1 週間の枠。used_percent が使った割合。
f=$(find "$HOME/.codex/sessions" -name '*.jsonl' -print0 2>/dev/null | xargs -0 ls -t 2>/dev/null | head -1)
[ -z "$f" ] && { echo "Codex のセッション記録が見つかりません"; exit 1; }
line=$(grep -o '"rate_limits":{[^}]*}[^}]*}[^}]*}' "$f" | tail -1)
for w in primary secondary; do
  used=$(echo "$line" | grep -o "\"$w\":{[^}]*}" | grep -o '"used_percent":[0-9.]*' | cut -d: -f2)
  mins=$(echo "$line" | grep -o "\"$w\":{[^}]*}" | grep -o '"window_minutes":[0-9]*' | cut -d: -f2)
  reset=$(echo "$line" | grep -o "\"$w\":{[^}]*}" | grep -o '"resets_at":[0-9]*' | cut -d: -f2)
  echo "$w: 使用 ${used}% / 枠 ${mins} 分 / リセット $(date -d @"$reset" '+%m/%d %H:%M')"
done
echo "（記録: $(basename "$f")。最後に Codex を動かした時点の値）"
