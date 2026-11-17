#!/usr/bin/env bash
# 選手ごとの試合の 1 コマ（ゲームの描画）と、メニューの肖像を作る。GPU と ffmpeg が要る（デスクトップの PC で回す）。
#   RUN/portraits/<id>_face.png・<id>_body.png   試合の体のボクセルから写した肖像（miniring-arena portraits）
#   RUN/shots/<id>.mp4・<id>_<秒>.png              <id> を赤、相手を青にした 6 秒の試合と、動画の 6・7.5・9 秒のコマ
#   RUN/shots/sheet_*.png                          コマを並べた確認用の一覧（どのコマを使うかを目で選ぶ）
# 相手は既定 gaston（gaston 本人は marcus）。種は 3。
# 使い方: bash tools/roster-report/shots.sh <RUN> [相手 id] [種]
set -euo pipefail
RUN="${1:?usage: shots.sh <RUN> [opponent] [seed]}"
OPP="${2:-gaston}"
SEED="${3:-3}"
ARENA="dotnet games/MiniRing.Arena/bin/Release/net10.0/miniring-arena.dll"
mkdir -p "$RUN/portraits" "$RUN/shots"
dotnet build games/MiniRing.Arena -c Release -v q -nologo > /dev/null
$ARENA portraits --out "$RUN/portraits" --scale 4

ids=$(ls games/MiniRing/Characters/roster/*.json | xargs -n1 basename | sed 's/\.json$//')
for id in $ids; do
  opp="$OPP"; [ "$id" = "$OPP" ] && opp=marcus
  # 3 つのコマがもうあれば描き直さない（途中から続ける）。描き直すときは RUN/shots/<id>_*.png を消す。
  if [ ! -f "$RUN/shots/${id}_6.png" ] || [ ! -f "$RUN/shots/${id}_7.5.png" ] || [ ! -f "$RUN/shots/${id}_9.png" ]; then
    $ARENA video --red-char "$id" --blue-char "$opp" --seconds 6 --speed 1 --width 960 --height 540 --seed "$SEED" --out "$RUN/shots/$id.mp4" > /dev/null 2>&1
    # 動画の始めの数秒はタイトル。6〜9 秒が試合の中。
    for t in 6 7.5 9; do ffmpeg -loglevel error -y -ss "$t" -i "$RUN/shots/$id.mp4" -frames:v 1 "$RUN/shots/${id}_$t.png"; done
  fi
  ffmpeg -loglevel error -y -i "$RUN/shots/${id}_6.png" -i "$RUN/shots/${id}_7.5.png" -i "$RUN/shots/${id}_9.png" \
    -filter_complex "[0]scale=320:-2[a];[1]scale=320:-2[b];[2]scale=320:-2[c];[a][b][c]hstack=3" "$RUN/shots/row_$id.png"
  echo "$id done"
done

# 6 人ずつ縦に並べた一覧（左から 6・7.5・9 秒、行の順は名前の順）。
set -- $ids
i=0
while [ $# -gt 0 ]; do
  args=(); n=0; names=""
  for k in 1 2 3 4 5 6; do [ $# -gt 0 ] || break; args+=(-i "$RUN/shots/row_$1.png"); names="$names $1"; shift; n=$((n+1)); done
  i=$((i+1))
  # 一覧には名前を描かない（ffmpeg の文字の描画はこの PC で字の設定が無い）。行の順をここに出す。
  echo "sheet_$i (上から):$names" | tee -a "$RUN/shots/sheets.txt"
  if [ $n -gt 1 ]; then ffmpeg -loglevel error -y "${args[@]}" -filter_complex "vstack=$n" "$RUN/shots/sheet_$i.png"; else cp "${args[1]}" "$RUN/shots/sheet_$i.png"; fi
done
echo "sheets: $(ls "$RUN"/shots/sheet_*.png | tr '\n' ' ')"
