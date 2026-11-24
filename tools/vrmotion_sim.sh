#!/usr/bin/env bash
# 切り出した振り（.vrmotion）を順に人形になぞらせ、右手のずれと自分を斬ったセルを CSV にする（tools/vrmotion_pick.py --sim が読む）。
# 使い方: [WEAPON=ironsword WEAR=shield] bash tools/vrmotion_sim.sh <切り出しのフォルダ> <out.csv> [既にある CSV（測った分は飛ばす）]
# 先に dotnet build games/MiniRing.Arena -c Release。1 本 10 秒ほど。読めない行は止めずに stderr へ出し、最後に数を言う。
set -uo pipefail
dir="$1"; out="$2"; done_csv="${3:-}"
arena="dotnet games/MiniRing.Arena/bin/Release/net10.0/miniring-arena.dll"
weapon="${WEAPON:-katana}"
wear_args=(); [ -n "${WEAR:-}" ] && wear_args=(--wear "$WEAR")
shopt -s nullglob
files=("$dir"/*.vrmotion)
[ ${#files[@]} -gt 0 ] || { echo "no .vrmotion in $dir" >&2; exit 1; }
[ -f "$out" ] || echo "file,right_error,left_error,self_cells" > "$out"
failed=0
for f in "${files[@]}"; do
  key=$(basename "$f" .vrmotion)
  if grep -q "^$key," "$out" ${done_csv:+"$done_csv"} 2>/dev/null; then continue; fi
  line=$($arena imitate --vrmotion "$f" --weapon "$weapon" "${wear_args[@]}" --measure yes 2>&1 | grep "self cells lost" | tail -1)
  # 例: name: right hand error 0.69 left 0.57 (457 steps), self cells lost 18
  if [[ "$line" =~ right\ hand\ error\ ([0-9.NaN]+)\ left\ ([0-9.NaN]+).*self\ cells\ lost\ (-?[0-9]+) ]]; then
    echo "$key,${BASH_REMATCH[1]},${BASH_REMATCH[2]},${BASH_REMATCH[3]}" >> "$out"
  else
    # なぞりが落ちた（例外）: 自傷セル -2 で残す（vrmotion_pick.py は選びから外す）
    echo "$key,NaN,NaN,-2" >> "$out"
    echo "failed: $key: $line" >&2; failed=$((failed + 1))
  fi
done
echo "measured into $out ($failed failed)"
[ "$failed" -eq 0 ]
