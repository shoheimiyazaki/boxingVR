# 確かめ方の道具（全部）

CLAUDE.md §5 にはよく使う物だけを置き、ここに全部を書く。Arena のコマンドの一覧と引数は `games/MiniRing.Arena/Program.cs` の先頭のコメントにもある。

2026-11-17 に CLAUDE.md §5 から移した（文は元のまま）。

| 目的 | コマンド（リポジトリの根で） |
|---|---|
| 全テスト | `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName!~Probe"` |
| 調査用の Probe | `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName~<名前>" --logger "console;verbosity=detailed"` |
| 総当たり | `dotnet run --project games/MiniRing.Arena -c Release -- league --bouts 4 --seconds 60 --out docs/arena/<名前>.md` |
| 名簿の総当たりと間合いのレポート（勝率・技ごとの間合い・得意 / 不得意の距離・画像入り） | `bash tools/roster-report/run-raytrek.sh media_tmp/roster-<日付>`（raytrek で約 2 時間）→ `shots.sh` → `build-data.mjs` → `analyze.mjs` → `build-images.mjs` → `build-page.mjs` → `preview.mjs` → 公開。手順は `docs/ROSTER_REPORT.md`。部品は `rosterleague --matrix <csv>`（1 試合 1 行）・`maailab [--ranges <接頭辞>]`（1 打ずつの間合いとダメージ、距離の帯ごとの与えた / 受けた）・`portraits --out <dir>`（肖像の PNG） |
| 攻撃のドリル | `... -- attacklab --combos 1,2,1-2 --telegraphs 0,0.1,0.2` |
| 詰めのドリル（弱った相手の顔に当たるか） | `... -- finishlab [--attackers ramon,yoandry,marcus] [--victims dmitri,joao,ramon] [--levels 0.45,0.6] [--bouts 2] [--seconds 20] [--set AimLead=0] [--log docs/arena/<名前>.md]`（相手の意識を決まった深さで止め、拳ごとに顔・胴・ガード・空振りと外れた向き、技ごとの当たり・狙いのずれ。`docs/BOT_PDCA.md`「詰めのドリル」） |
| 空手のドリル | `... -- karatelab [--only shapes,parry,kicks] [--cues 0,0.06] [--bouts N] [--seconds S] [--log docs/arena/<名前>.md]`（手形 × 急所・パリー・5 種の蹴り。`docs/KARATE.md`） |
| 防御・回避のドリル | `... -- drill` / `drilltune` / `basics --punch 2 --slot Duck --style swarmer` / `counterlab` |
| CMA-ES で拳・守りの技を磨く | `... -- cmatune --target punch:2 [--defenses Block,Slip] [--gens 40] [--samples N] [--seconds S] [--resume state.json] [--out docs/arena/cma_<名前>.md] [--json docs/arena/cma/<名前>.json]`（結果はデータとして出すだけ。最後に使わなかった種で始めの値と分布の平均 m を比べ、試合でも確かめる。`docs/BOTS.md` §3） |
| 剣の守りのドリル（武器を選ばない） | `... -- guardlab [--only kesa,棍棒] [--variants 0,7] [--try "ParryAndStep:0.2:0.52:0.35:0.3:0.25:0.3"] [--video out.mp4]`（`docs/WEAPON_DEFENSE.md`） |
| 武器の扱いの物差し・棒術の型の動画 | `... -- handlelab --pairs daichi:jin,daichi:konrad --bouts 4 --seconds 40 [--style-staff x.json] --out docs/arena/<名前>.md`（振りの質・自傷・勝敗。棒の後ろの端で当たった割合も）/ `... -- imitate --weapon staff [--only tsuki] [--slow 4] [--measure yes] --out x.mp4`（棒の型を構えて立つ相手へ。目標とのずれの測定は `--measure yes`。`docs/BOJUTSU.md`） |
| 剣士の守り・間合いを試合で育てる | `... -- guardspar --gens 5 --lambda 3 --bouts 4` / `awaylab`（どちらも世代ごとに種を変え、使わなかった種で比べ直して良いときだけ採る。1 回 3 時間ほど） |
| 首・関節の隙間と首の切断の物差し | `... -- necklab --mode geom`（つながった部位の格子の距離）/ `--mode slash [--bridges off] [--tilt 35] [--arms down]`（静止した的の首の高さを日本刀が通る表）/ `--mode slash --video x.mp4 --offset -0.003`（首の寄りの動画）/ 既定（日本刀の薙ぎを高さを振って）。`docs/VOXEL_DAMAGE.md`「関節のつなぎ」 |
| 剣の自傷・すり抜けの物差し | `... -- selfcuts [--seeds 8 --seconds 30] [--passes 1] [--clearance on] [--pacing on]`（握った武器が自分の体を削った回数を原因別に）/ `tunnel`（刃が体のセルの中にあるのに接触が無かった歩）/ `selfpath`（技の目標の刃の線が自分の体を通るか）。`docs/VOXEL_WEAPONS.md` §5.5。`--passes 1` で掃き直し前の解き方 |
| 試合の動画 | `... -- video --red champion --blue swarmer --seconds 40 --out docs/video/<名前>.mp4`（ffmpeg が要る。`--facecam on` で両コーナーの顔の窓を両脇に出す。`--until-ko yes` で K.O. まで続け、`--seconds` は上限になる。`--speed 0.5 / 0.75 / 1`（既定 0.75）は試合そのものを遅く進めて撮る。`--tv on` はテレビ中継風: 5 ラウンド・歓声・*.events.json） |
| 実況・解説を入れる | `powershell -ExecutionPolicy Bypass -File tools\commentary\dub.ps1 -Video in.mp4 -Script script.json -Out out.mp4`（台本は *.events.json を見て書く。`tools/commentary/README.md`） |
| WVC のテレビ中継（撮影〜結合を札 1 枚で） | `powershell -ExecutionPolicy Bypass -File tools\tvshow\tvshow.ps1 -Card tools\tvshow\cards\wvc-ishu.json`（種の下見・撮影・資料・実況の下書き・声入れ・点検・結合。台詞は札の `"commentary": { "writer": "llm" }` で LLM（Haiku）がテイクとして書く（`-Stage write`・`tools	vshowwrite-commentary.ps1`、テイクは `<out>	akesbN`）か、エージェントが `bN.extras.json` を書く: スキル `tv-commentary`。札に `"autoCommentary": true` なら下書きだけで作る。`docs/TV_BROADCAST.md`） |
| WVC のテレビ中継を生で流す（OBS へ） | `powershell -ExecutionPolicy Bypass -File tools\tvshow\tvshow.ps1 -Card tools\tvshow\cards\live-test.json -Stage liveprep`（準備: 種 → 描かない下回し `video --render off` → 台本 → 声）→ `-Stage live`（試合ごとに `video --tv on --facecam on ... --live srt://127.0.0.1:9000?mode=listener --voices bN_live_voices\manifest.json` で実時間に描いて流す。受け方・遅延・鍵は `docs/TV_BROADCAST.md`「生配信」） |
| 回避の形を体の動きで測る | `... -- formlab --punch 3 --slot Weave --style swarmer [--set "HeadY=-0.8,Step=0.8"] [--cue 0] [--bouts 3]`（頭の横・上下・前の軌跡、腰の沈み、拳と頭の距離。`docs/BOT_PDCA.md`） |
| ボクセルの書き出し・確かめ | `... -- voxexport --out boxer.vox` / `voxcheck --in boxer.vox [--install yes]`（武器・はめる物は `weaponexport --out weapons.vox` / `weaponcheck --in weapons.vox [--install yes]`） |

撮った動画・記録は、世代を区切るとき（`PhysicsVersion` を上げたマージ・大きな機能が master に入った）にワークツリーの分も含めて `media/<世代>/` へ移し、`docs/MEDIA.md` に足す。

動画を縮めるときは**音を残す**: `ffmpeg -i in.mp4 -vcodec libx264 -crf 30 -preset slow -vf scale=960:-2 -c:a aac -b:a 96k out_small.mp4`（`-an` は音を消す。過去にこれで音が消えた）。
