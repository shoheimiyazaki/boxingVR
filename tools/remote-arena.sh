#!/usr/bin/env bash
# Run one Arena command on a remote PC (default tower32) against a commit of this tree, and copy its --out file back.
# (committed work only), and append the tables to a local markdown file.
#
#   bash tools/remote-perf.sh <out.md> [--host tower32] [--rev <commit>] [--scenes "fist|--seconds 40 --seeds 1;katana|..."]
#
# Each scene line runs in its own process (one seed per line keeps a crash or a mismatch to that line).
#
# Times are only comparable on the same machine with nothing else running (it warns when other dotnet processes run there).
# Remote layout: %USERPROFILE%\repos\rt-perf-<tree>\src (a clone used only by this script, detached at the sent commit).
set -euo pipefail

OUT=${1:?usage: remote-arena.sh <out.md> [--host H] [--rev X] -- <arena args without --out>}
shift
HOST=tower32
ARGS=
ENVS=()
REV=HEAD
while [ $# -gt 0 ]; do
  case "$1" in
    --host) HOST="$2"; shift 2 ;;
    --) shift; ARGS="$*"; break ;;
    --rev) REV="$2"; shift 2 ;;
    --env) ENVS+=("$2"); shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

REMOTE_HOME=$(ssh "$HOST" "echo %USERPROFILE%" | tr -d '\r')
ROOT=$(git rev-parse --show-toplevel)
cd "$ROOT"
TREE=$(basename "$ROOT" | tr -c 'A-Za-z0-9_.\n-' '_')
REMOTE="$REMOTE_HOME\\repos\\rt-arena-$TREE"
REMOTE_FWD=$(printf '%s' "$REMOTE" | tr '\\' '/')
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
git branch -f rt-perf-send "$REV" >/dev/null
git bundle create -q "$WORK/rt.bundle" rt-perf-send
git branch -D rt-perf-send >/dev/null
{
  echo '@echo off'
  echo "set R=$REMOTE"
  echo 'set DOTNET_CLI_UI_LANGUAGE=en'
  echo 'if not exist %R%\src\.git git init -q %R%\src'
  echo 'cd /d %R%\src || exit /b 3'
  echo 'git fetch -q %R%\rt.bundle +refs/heads/rt-perf-send:refs/rt/incoming || exit /b 4'
  echo 'git checkout -q -f --detach refs/rt/incoming || exit /b 5'
  echo 'git clean -fdq'
  echo 'del /q %R%\perf.md 2>nul'
  for e in "${ENVS[@]}"; do echo "set $e"; done
  echo 'dotnet build games\MiniRing.Arena -c Release -nologo -v q > %R%\build.log 2>&1 || exit /b 6'
  echo "dotnet games\MiniRing.Arena\bin\Release\net10.0\miniring-arena.dll $ARGS --out %R%\perf.md > %R%\run.log 2>&1"
  echo 'exit /b 0'
} | sed 's/$/\r/' > "$WORK/run.cmd"

ssh "$HOST" "if not exist $REMOTE mkdir $REMOTE" >/dev/null
scp -q "$WORK/run.cmd" "$WORK/rt.bundle" "$HOST:$REMOTE_FWD/"
BUSY=$(ssh "$HOST" 'tasklist /fo csv /nh' 2>/dev/null | grep -c '^"\(dotnet\|miniring-arena\)\.exe"' || true)
[ "${BUSY:-0}" -gt 0 ] && echo "remote-perf: warning: $BUSY dotnet / arena process(es) already run on $HOST: times are not comparable" >&2
echo "remote-perf: $(git rev-parse --short "$REV") on $HOST ($(date +%H:%M))" >&2
ssh -o ServerAliveInterval=30 -o ServerAliveCountMax=10 "$HOST" "$REMOTE\\run.cmd" < /dev/null
scp -q "$HOST:$REMOTE_FWD/perf.md" "$WORK/perf.md"
cat "$WORK/perf.md" >> "$OUT"
cat "$WORK/perf.md"
