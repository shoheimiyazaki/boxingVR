#!/usr/bin/env bash
# Run the test suite on raytrek (the faster PC, `ssh raytrek`) against this working tree, and print a summary.
#
#   bash tools/remote-test.sh [all|fast|probe:<Name>|filter:<expr>] [--parallel N]
#
#   all (default)   everything except visual and probes (about an hour serially)
#   fast            the quick tier (no slow/visual/probe)
#   probe:<Name>    one probe class, e.g. probe:StaminaFeelProbe
#   filter:<expr>   any dotnet test filter
#   --parallel N    run test classes in parallel with N workers (MSTest ClassLevel). Experimental:
#                   tests that change static settings can disturb each other (docs/plans/AGENT_DX_PLAN.md 段 3).
#
# What it sends: the commits of HEAD (as a git bundle; only the part raytrek does not have yet), the uncommitted
# diff (tracked files) and the untracked, non-ignored files. Remote layout (C:\Users\smiya\repos):
#   remote-test\        a dedicated clone used only by this script (detached at the sent commit, then patched)
#   remote-test-in\     what was sent;   remote-test-out\  trx and the log of the last run
# Writes the trx to TestResults/remote/<time>.trx here and prints counts, failures and the slowest classes.
# Runs in the foreground for the whole test run; start it in the background from an agent.
set -euo pipefail

HOST=raytrek
REMOTE='C:\Users\smiya\repos'
REMOTE_FWD='C:/Users/smiya/repos'
MODE=all
PARALLEL=0
while [ $# -gt 0 ]; do
  case "$1" in
    --parallel) PARALLEL="$2"; shift 2 ;;
    all|fast|probe:*|filter:*) MODE="$1"; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

case "$MODE" in
  all) FILTER='TestCategory!=visual&TestCategory!=Probe&FullyQualifiedName!~Probe' ;;
  fast) FILTER='TestCategory!=slow&TestCategory!=visual&TestCategory!=Probe&FullyQualifiedName!~Probe' ;;
  probe:*) FILTER="FullyQualifiedName~${MODE#probe:}" ;;
  filter:*) FILTER="${MODE#filter:}" ;;
esac
case "$FILTER" in *'"'*|*'%'*|*'^'*) echo "the filter must not contain \" % ^ (cmd.exe quoting)" >&2; exit 2 ;; esac

ROOT=$(git rev-parse --show-toplevel)
cd "$ROOT"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
HEAD_SHA=$(git rev-parse HEAD)

# 1. Bundle only what raytrek lacks. The remote remembers the last sent commit in refs/rt/last.
ssh "$HOST" "if not exist $REMOTE\\remote-test\\.git git init -q $REMOTE\\remote-test" >/dev/null
LAST=$(ssh "$HOST" "git -C $REMOTE\\remote-test rev-parse -q --verify refs/rt/last" 2>/dev/null | tr -d '\r' || true)
if [ -n "$LAST" ] && git cat-file -e "$LAST^{commit}" 2>/dev/null && git merge-base --is-ancestor "$LAST" HEAD 2>/dev/null; then
  if [ "$LAST" = "$HEAD_SHA" ]; then BUNDLE=0; else git bundle create -q "$WORK/rt.bundle" HEAD "^$LAST"; BUNDLE=1; fi
else
  git bundle create -q "$WORK/rt.bundle" HEAD; BUNDLE=1
fi
git diff HEAD --binary > "$WORK/rt.patch"
git ls-files --others --exclude-standard -z > "$WORK/untracked.lst"
if [ -s "$WORK/untracked.lst" ]; then tar --null -T "$WORK/untracked.lst" -cf "$WORK/rt-untracked.tar"; fi

# 2. The remote script (cmd.exe). Kept in a file so the filter's & and ! are not re-parsed by ssh.
{
  echo '@echo off'
  echo "set R=$REMOTE"
  echo 'if not exist %R%\remote-test-out mkdir %R%\remote-test-out'
  echo 'del /q %R%\remote-test-out\* 2>nul'
  echo 'cd /d %R%\remote-test || exit /b 3'
  if [ "$BUNDLE" = 1 ]; then echo 'git fetch -q %R%\remote-test-in\rt.bundle +HEAD:refs/rt/incoming || exit /b 4'; else echo 'git update-ref refs/rt/incoming refs/rt/last'; fi
  echo 'git checkout -q -f --detach refs/rt/incoming || exit /b 5'
  echo 'git update-ref refs/rt/last refs/rt/incoming'
  echo 'git clean -fdq'
  echo 'for %%F in (%R%\remote-test-in\rt.patch) do if %%~zF GTR 0 git apply --whitespace=nowarn %R%\remote-test-in\rt.patch || exit /b 6'
  echo 'if exist %R%\remote-test-in\rt-untracked.tar tar -xf %R%\remote-test-in\rt-untracked.tar || exit /b 7'
  ARGS=''
  if [ "$PARALLEL" != 0 ]; then ARGS=" -- MSTest.Parallelize.Workers=$PARALLEL MSTest.Parallelize.Scope=ClassLevel"; fi
  echo "dotnet test tests\\MiniRing.Tests -c Release -nologo --filter \"$FILTER\" --logger \"trx;LogFileName=remote.trx\" --results-directory %R%\\remote-test-out$ARGS > %R%\\remote-test-out\\test.log 2>&1"
  echo 'set RC=%errorlevel%'
  echo 'echo dotnet test exit %RC% >> %R%\remote-test-out\test.log'
  echo 'exit /b %RC%'
} | sed 's/$/\r/' > "$WORK/run.cmd"

ssh "$HOST" "if exist $REMOTE\\remote-test-in rmdir /s /q $REMOTE\\remote-test-in & mkdir $REMOTE\\remote-test-in" >/dev/null
FILES=("$WORK/run.cmd" "$WORK/rt.patch")
[ "$BUNDLE" = 1 ] && FILES+=("$WORK/rt.bundle")
[ -f "$WORK/rt-untracked.tar" ] && FILES+=("$WORK/rt-untracked.tar")
scp -q "${FILES[@]}" "$HOST:$REMOTE_FWD/remote-test-in/"

BUSY=$(ssh "$HOST" 'tasklist /fi "imagename eq dotnet.exe" /fo csv /nh' 2>/dev/null | grep -c '^"dotnet.exe"' || true)
if [ "${BUSY:-0}" -gt 0 ]; then
  echo "remote-test: warning: $BUSY dotnet process(es) already run on $HOST (other jobs?): tests compete for the CPU and times are not comparable" >&2
fi
echo "remote-test: if this ssh is killed, the run goes on over there: find the 'dotnet test ... remote.trx' process on $HOST and stop only it with taskkill /PID <pid> /T /F" >&2
echo "remote-test: $MODE on $HOST at $(git rev-parse --short HEAD)$( [ -s "$WORK/rt.patch" ] && echo ' + uncommitted diff')$( [ "$PARALLEL" != 0 ] && echo " parallel $PARALLEL") ($(date +%H:%M))" >&2
START=$(date +%s)
set +e
ssh -o ServerAliveInterval=30 -o ServerAliveCountMax=10 "$HOST" "$REMOTE\\remote-test-in\\run.cmd" < /dev/null
STATUS=$?
set -e
echo "remote-test: finished in $(( ($(date +%s) - START) / 60 )) min (remote script exit $STATUS; 1 = some test failed, 3-7 = setup step, 255 = ssh dropped)" >&2

mkdir -p TestResults/remote
STAMP=$(date +%Y%m%d-%H%M%S)
scp -q "$HOST:$REMOTE_FWD/remote-test-out/test.log" "TestResults/remote/$STAMP.log" || true
if ! scp -q "$HOST:$REMOTE_FWD/remote-test-out/remote.trx" "TestResults/remote/$STAMP.trx"; then
  echo "no trx came back; the end of the log:" >&2
  tail -n 40 "TestResults/remote/$STAMP.log" 2>/dev/null >&2 || true
  exit 1
fi
set +e
PYTHONIOENCODING=utf-8 py -3 -I "$ROOT/tools/trx-summary.py" "TestResults/remote/$STAMP.trx"
SUMMARY=$?
set -e
# A build failure or a crashed test host can leave a partial trx: fail if either side says so.
[ "$STATUS" = 0 ] && [ "$SUMMARY" = 0 ] || exit 1
