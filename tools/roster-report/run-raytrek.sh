#!/usr/bin/env bash
# 総当たり（rosterleague --matrix）と間合いの測定（maailab --ranges）を raytrek で回し、結果を RUN/league・RUN/maai へ持ち帰る。
# 今のコミット（HEAD）をそのまま送る。作業ツリーに未コミットの変更があっても送らない（測る物をコミットで固定するため）。
# 17 人・各組 20 試合・60 秒で、総当たり約 80 分 + 間合い約 30 分（raytrek の 14 並列）。長いので run_in_background で回すこと。
# 使い方: bash tools/roster-report/run-raytrek.sh <RUN> [各組の試合数（既定 20）] [間合いの各組の試合数（既定 4）] [秒（既定 60）]
set -euo pipefail
RUN="${1:?usage: run-raytrek.sh <RUN> [boutsPerPair] [maaiBouts] [seconds]}"
PAIR="${2:-20}"
MAAI="${3:-4}"
SECONDS_PER_BOUT="${4:-60}"
PARALLEL=14
# raytrek の置き場。パスは / で書く（cmd・git・dotnet とも / を受ける。\ は送る側のシェルで逃がし方が変わって壊れた）。
REMOTE='C:/Users/smiya/repos'
STAMP="$(git rev-parse --short HEAD)"
WT="rr-$STAMP"

if [ -n "$(git status --porcelain -- games src)" ]; then
  echo "注意: games/ src/ に未コミットの変更があります。送るのはコミット $STAMP だけです。" >&2
fi
mkdir -p "$RUN/league" "$RUN/maai"
git bundle create "$RUN/repo.bundle" HEAD > /dev/null 2>&1
scp -q "$RUN/repo.bundle" "raytrek:$REMOTE/$WT.bundle"

# raytrek の本体のリポジトリへ取り込み、別のワークツリー rr-<コミット> でビルドする（本体の作業ツリーには触れない）。
ssh raytrek "cd /d $REMOTE/myvrgame1 && (if not exist $REMOTE/$WT (git fetch -q $REMOTE/$WT.bundle HEAD:refs/heads/$WT && git worktree add -q $REMOTE/$WT $WT)) && cd /d $REMOTE/$WT && dotnet build games/MiniRing.Arena -c Release -v q -nologo"

ARENA='dotnet games/MiniRing.Arena/bin/Release/net10.0/miniring-arena.dll'
if [ "${SETUP_ONLY:-0}" = "1" ]; then echo "setup only: raytrek の $REMOTE/$WT をビルドした"; exit 0; fi
ssh raytrek "cd /d $REMOTE/$WT && $ARENA rosterleague --bouts $PAIR --seconds $SECONDS_PER_BOUT --parallel $PARALLEL --out league.md --matrix league.csv > league.stdout 2> league.log"
ssh raytrek "cd /d $REMOTE/$WT && $ARENA maailab --bouts $MAAI --seconds $SECONDS_PER_BOUT --parallel $PARALLEL --out maai.md --json maai.json --csv maai.csv --ranges ranges > maai.stdout 2> maai.log"

scp -q "raytrek:$REMOTE/$WT/league.md" "raytrek:$REMOTE/$WT/league.csv" "raytrek:$REMOTE/$WT/league.log" "$RUN/league/"
scp -q "raytrek:$REMOTE/$WT/maai.md" "raytrek:$REMOTE/$WT/maai.json" "raytrek:$REMOTE/$WT/maai.csv" "raytrek:$REMOTE/$WT/maai.log" \
  "raytrek:$REMOTE/$WT/ranges_bins.csv" "raytrek:$REMOTE/$WT/ranges_summary.csv" "$RUN/maai/"
echo "$STAMP" > "$RUN/commit.txt"
tail -1 "$RUN/league/league.log"
tail -1 "$RUN/maai/maai.log"
