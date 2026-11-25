#!/usr/bin/env bash
# Run the Arena `perf` scenes (docs/PERFORMANCE.md) on a quiet remote PC (default tower32) against HEAD of this tree
# (committed work only), and append the tables to a local markdown file.
#
#   bash tools/remote-perf.sh <out.md> [--host tower32] [--scenes "fist|--seconds 40 --seeds 1,2;katana|..."]
#
# Times are only comparable on the same machine with nothing else running (it warns when other dotnet processes run there).
# Remote layout: %USERPROFILE%\repos\rt-perf-<tree>\src (a clone used only by this script, detached at the sent commit).
set -euo pipefail

OUT=${1:?usage: remote-perf.sh <out.md> [--host H] [--scenes list]}
shift
HOST=tower32
SCENES='fist|--seconds 40 --seeds 1,2;katana|--seconds 40 --seeds 1,2;vfc2|--seconds 90 --seeds 1,2;royale|--seconds 90 --seeds 1,2'
while [ $# -gt 0 ]; do
  case "$1" in
    --host) HOST="$2"; shift 2 ;;
    --scenes) SCENES="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

REMOTE_HOME=$(ssh "$HOST" "echo %USERPROFILE%" | tr -d '\r')
ROOT=$(git rev-parse --show-toplevel)
cd "$ROOT"
TREE=$(basename "$ROOT" | tr -c 'A-Za-z0-9_.\n-' '_')
REMOTE="$REMOTE_HOME\\repos\\rt-perf-$TREE"
REMOTE_FWD=$(printf '%s' "$REMOTE" | tr '\\' '/')
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
git bundle create -q "$WORK/rt.bundle" HEAD
{
  echo '@echo off'
  echo "set R=$REMOTE"
  echo 'set DOTNET_CLI_UI_LANGUAGE=en'
  echo 'if not exist %R%\src\.git git init -q %R%\src'
  echo 'cd /d %R%\src || exit /b 3'
  echo 'git fetch -q %R%\rt.bundle +HEAD:refs/rt/incoming || exit /b 4'
  echo 'git checkout -q -f --detach refs/rt/incoming || exit /b 5'
  echo 'git clean -fdq'
  echo 'del /q %R%\perf.md 2>nul'
  echo 'dotnet build games\MiniRing.Arena -c Release -nologo -v q > %R%\build.log 2>&1 || exit /b 6'
  IFS=';' read -ra LIST <<< "$SCENES"
  for item in "${LIST[@]}"; do
    scene=${item%%|*}
    args=${item#*|}
    echo "games\\MiniRing.Arena\\bin\\Release\\net10.0\\miniring-arena.exe perf --render 1 --scene $scene $args --out %R%\\perf.md > nul 2>&1 || echo FAILED $scene >> %R%\\perf.md"
  done
  echo 'exit /b 0'
} | sed 's/$/\r/' > "$WORK/run.cmd"

ssh "$HOST" "if not exist $REMOTE mkdir $REMOTE" >/dev/null
scp -q "$WORK/run.cmd" "$WORK/rt.bundle" "$HOST:$REMOTE_FWD/"
BUSY=$(ssh "$HOST" 'tasklist /fi "imagename eq dotnet.exe" /fo csv /nh' 2>/dev/null | grep -c '^"dotnet.exe"' || true)
[ "${BUSY:-0}" -gt 0 ] && echo "remote-perf: warning: $BUSY dotnet process(es) already run on $HOST: times are not comparable" >&2
echo "remote-perf: $(git rev-parse --short HEAD) on $HOST ($(date +%H:%M))" >&2
ssh -o ServerAliveInterval=30 -o ServerAliveCountMax=10 "$HOST" "$REMOTE\\run.cmd" < /dev/null
scp -q "$HOST:$REMOTE_FWD/perf.md" "$WORK/perf.md"
cat "$WORK/perf.md" >> "$OUT"
grep -E '^## perf|^\| 物理|^\| 描画|^FAILED' "$WORK/perf.md"
