# 確かめ方の道具（全部）

CLAUDE.md §5 にはよく使う物だけを置き、ここに全部を書く。Arena のコマンドの一覧と引数は `games/MiniRing.Arena/Program.cs` の先頭のコメントにもある。

2026-11-17 に CLAUDE.md §5 から移した（文は元のまま）。

| 目的 | コマンド（リポジトリの根で） |
|---|---|
| 全テスト | `dotnet test tests/MiniRing.Tests --filter "TestCategory!=visual&TestCategory!=Probe"`（= `run.cmd test all`。tower32 で: `bash tools/remote-test.sh`） |
| ワークツリーと枝の状態（片付けの前） | `bash tools/wt-status.sh [--dirty] [--ignored]`（状態・master との先 / 後・最後のコミット・未コミット・無視されたファイル・ロック。読むだけ。`docs/WORKFLOW.md` §5） |
| 調査用の Probe | `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName~<名前>" --logger "console;verbosity=detailed"`（tower32 で: `bash tools/remote-test.sh probe:<名前>`。Probe は `[TestCategory("Probe")]` で通常の実行から外れる） |
| 名簿の組の試合のチェックサムを並べる（ある変更で変わる組・変わらない組を調べる） | `bash tools/remote-test.sh probe:CharacterSamenessProbe`（変更の前後で回して比べる。`CharacterSamenessTests` は 2026-11-20 から金型を持たないので、版を上げても取り直しは要らない） |
| デスクトップのメニューを実際のウィンドウで動かす（画面の確かめ） | `games/MiniRing.Desktop/bin/Release/net10.0/miniring-desktop.exe --no-record --menu-script "wait,enter,down,enter,..."`（ゲームの中で 0.5 秒に 1 つ操作を渡す: left/right/up/down/enter/back/wait/scroll-up/scroll-down。外からキーを送らない。撮るのはウィンドウの矩形の画面写し。ページの PNG は `MenuPagesVisualTests`） |
| リプレイで見られる記録を作る（ボットどうし、今のビルド） | `... recordbout --red champion --blue swarmer --seconds 100 [--tv 1] [--seed 3] --out replays/<名前>.mrrep` → `run.cmd replay replays/<名前>.mrrep`（操作は docs/REPLAY.md「リプレイを見る」） |
| 総当たり | `dotnet run --project games/MiniRing.Arena -c Release -- league --bouts 4 --seconds 60 --out docs/arena/<名前>.md` |
| 試合の面白さ（見どころ・ドラマ・緩急、docs/EXCITEMENT.md） | `... excitement --red champion --blue swarmer --seconds 40 --seeds 4 [--curve <接頭辞>]`（出来事の一覧とまとめ。`--curve` で曲線の CSV → `py -3 tools/excitement_curve.py <csv>... --out <png>`）・`... excitementleague [--pairs a:b,c:d] --seeds 2 --seconds 40 --out docs/arena/excitement_<日付>.md --label <名前>`（組ごとの表）・`... excitementfit --seeds 8 --out docs/arena/winmodel_<日付>.json`（観客の見込みのモデルを当てはめ直す。係数は `WinModel.Default` へ写す）。`video` は `<out>.highlights.txt`（映像の秒つきの一覧）も書く |
| 面白さの A/B（調整値・ボットのスタイルを差し替えて比べる） | `... excitementleague --tune "Preset=Legacy,TrunkRotatorTorque=240,Footwork.StepPushTime=0.03" --style "Aggression*1.4,counter.CounterRate=0.8" --pairs … --seeds 8 --seed 1`（`--tune` は `FighterTuning` の float / int / bool と `Preset=<DynamicPunch など>`、`--style` は `BotStyle` の float。`キー*倍率` は各スタイルの値の倍率。`excitement` にも同じ引数。出力に出来事の点の内訳・打たれずに倒れた回数・試合が止まっている時間。採否は 48 試合以上で、使っていない種でも比べる: docs/arena/excitement_generations_2026-11-22.md） |
| 名簿の総当たりと間合いのレポート（勝率・技ごとの間合い・得意 / 不得意の距離・画像入り） | `bash tools/roster-report/run-raytrek.sh media_tmp/roster-<日付>`（raytrek で約 2 時間）→ `shots.sh` → `build-data.mjs` → `analyze.mjs` → `build-images.mjs` → `build-page.mjs` → `preview.mjs` → 公開。手順は `docs/ROSTER_REPORT.md`。部品は `rosterleague --matrix <csv>`（1 試合 1 行）・`maailab [--ranges <接頭辞>]`（1 打ずつの間合いとダメージ、距離の帯ごとの与えた / 受けた）・`portraits --out <dir>`（肖像の PNG） |
| 攻撃のドリル | `... -- attacklab --combos 1,2,1-2 --telegraphs 0,0.1,0.2` |
| 拳の経路の実験（右ストレート 1 本: 今の経路 / 打ち出しの瞬間にあごへ目標を置く / 打ち抜く点へ置く。守らない・ガードの相手 × 届く・半歩遠い。拳の速さの時系列・当たるまでの時間・乗る質量・エネルギー・ダメージ・頭の回転・脳しんとう・光・あざ・倒れたか） | `... -- punchlab [--paths standard,instant,instantthrough] [--defenders still,guard] [--reach 1.05 --far 1.3] [--seeds 1,2,3] [--nocap true] --log docs/arena/<名前>.md`、左右に並べた動画は `--video docs/video/<名前>.mp4 [--slow 8]`（docs/BOXING.md「拳の経路」）。`--study beyond [--depths 0.05,0.1,0.2,0.4]` は伸びきりであごの間合いで目標を頭の向こう側へ置き、拳を引くバネの中身（縮める前と後の距離・欲しい力・力の上限）と、縮めない反事実を測る（BOXING.md 1-8） |
| 詰めのドリル（弱った相手の顔に当たるか） | `... -- finishlab [--attackers ramon,yoandry,marcus] [--victims dmitri,joao,ramon] [--levels 0.45,0.6] [--bouts 2] [--seconds 20] [--set AimLead=0] [--log docs/arena/<名前>.md]`（相手の意識を決まった深さで止め、拳ごとに顔・胴・ガード・空振りと外れた向き、技ごとの当たり・狙いのずれ。`docs/BOT_PDCA.md`「詰めのドリル」） |
| 空手のドリル | `... -- karatelab [--only shapes,parry,kicks] [--cues 0,0.06] [--bouts N] [--seconds S] [--log docs/arena/<名前>.md]`（手形 × 急所・パリー・5 種の蹴り。`docs/KARATE.md`） |
| 防御・回避のドリル | `... -- drill` / `drilltune` / `basics --punch 2 --slot Duck --style swarmer` / `counterlab` |
| CMA-ES で拳・守りの技を磨く | `... -- cmatune --target punch:2 [--defenses Block,Slip] [--gens 40] [--samples N] [--seconds S] [--resume state.json] [--out docs/arena/cma_<名前>.md] [--json docs/arena/cma/<名前>.json]`（結果はデータとして出すだけ。最後に使わなかった種で始めの値と分布の平均 m を比べ、試合でも確かめる。`docs/BOTS.md` §3） |
| 剣の守りのドリル（武器を選ばない） | `... -- guardlab [--only kesa,棍棒] [--variants 0,7] [--try "ParryAndStep:0.2:0.52:0.35:0.3:0.25:0.3"] [--video out.mp4]`（`docs/WEAPON_DEFENSE.md`） |
| 武器の扱いの物差し・棒術の型の動画 | `... -- handlelab --pairs daichi:jin,daichi:konrad --bouts 4 --seconds 40 [--style-staff x.json] --out docs/arena/<名前>.md`（振りの質・自傷・勝敗。棒の後ろの端で当たった割合も）/ `... -- imitate --weapon staff [--only tsuki] [--slow 4] [--measure yes] --out x.mp4`（棒の型を構えて立つ相手へ。目標とのずれの測定は `--measure yes`。`docs/BOJUTSU.md`） |
| 剣士の守り・間合いを試合で育てる | `... -- guardspar --gens 5 --lambda 3 --bouts 4` / `awaylab`（どちらも世代ごとに種を変え、使わなかった種で比べ直して良いときだけ採る。1 回 3 時間ほど） |
| 剣士の型を練習する（無防備に立つ相手を 1 振りで無力化する型の経路の差分: 時計の速さ・ひねり・手の高さ・頭の前・間合い） | `... -- swordpractice --who jin --forms harai-men --gens 25 --victims standard,heavy --held-victims ramon,dmitri [--write]` / 教科書との前後の動画 `... -- swordpractice --who jin,konrad,edgar --forms harai-men,oberhau,kiri-oroshi --victim konrad --seeds 11 --video out.mp4`（採った差分は名簿の `education.weaponBot.Forms`。tower32 では `probe:SwordPracticeProbe.FinalCheck` が名簿の全部の体で比べる。`docs/BOT_PDCA.md` の「剣士 3 人の 1 振り」） |
| 首・関節の隙間と首の切断の物差し | `... -- jointlab [--joints LeftKnee,Neck] [--bend 40] [--speeds 3.4,9] --out docs/arena/jointlab_bend40.md`（どの関節でも、隙間を開けた姿勢の的を日本刀が通る表: つなぎの有無で、関節のそばで削れたセルと関節で断たれた本数を比べる。`--dims 1` で部位の格子とつなぎの箱の大きさ、`--video x.mp4 --joint LeftKnee --bend 40 --dir front --offset -0.005 --speed 3.4 [--bridges off]` で動画）、`... -- necklab --mode geom`（つながった部位の格子の距離）/ `--mode slash [--bridges off] [--tilt 35] [--arms down]`（静止した的の首の高さを日本刀が通る表）/ `--mode slash --video x.mp4 --offset -0.003`（首の寄りの動画）/ 既定（日本刀の薙ぎを高さを振って）。`docs/VOXEL_DAMAGE.md`「関節のつなぎ」 |
| つながりで分かれる体 | `... -- splitlab --at hip\|waist\|chest\|neck [--dir side\|back] [--offset 0.05] [--speed 25] [--seconds 4]`（その場で打ち続ける青の選んだ高さを日本刀が横から通る: 時刻ごとの状態・破片・外れた部位、最後に関節の接し目の残りとつながっていない格子）、`--video x.mp4 --slow 4` で動画（刃が通る始めの 0.5 秒をスロー、音なし）、`splitlab --scan jin,konrad --seeds 3 --seconds 60`（名簿の 2 人の試合を描かずに回し、胴・首・頭が分かれた時刻）。`video ...` は終わりに両者の体（胴・頭の破片の数・外れた部位）を出す。`docs/VOXEL_DAMAGE.md`「つながりで分かれる体」 |
| 刃の切れ味（ラグドールを使わない 1 関節の円弧の振り） | `... -- bladecut`（刃物 3 種 × 前腕・上腕・首・胴・腿 × 刃の速さ 2〜50 m/s と、質量・打つ点・押す手・角速度保持の比べ。表 `docs/arena/bladecut.md` と生の数字 `.csv`）、`--mode one --weapon katana --target neck --speed 18 [--drive 300] [--mass 2] [--fromtip 0.3] [--imposed on]`（1 振り）、`--mode film ... --out x.mp4 [--slow 20]`（動画）、`--mode ingame`（名簿の刃物の使い手どうしの試合で、刃の速さ・押す手の力を集める）。`docs/BLADE_CUT_STUDY.md` |
| 剣の自傷・すり抜けの物差し | `... -- selfcuts [--seeds 8 --seconds 30] [--passes 1] [--clearance on] [--pacing on]`（握った武器が自分の体を削った回数を原因別に）/ `tunnel`（刃が体のセルの中にあるのに接触が無かった歩）/ `selfpath`（技の目標の刃の線が自分の体を通るか）。`docs/VOXEL_WEAPONS.md` §5.5。`--passes 1` で掃き直し前の解き方 |
| ボクサーを作る（キャラメイク: 体重・身長・体型・ルーツ・顔・写真・教科書 → 名簿の形の 1 ファイル） | `... -- maker [--port 5180] [--save-dir DIR] [--open no]`（localhost の Web 画面。保存したファイルは `video --red-char <id か f.json>` にそのまま渡せる。docs/CHARACTERS.md §12） |
| 試合の動画 | `... -- video --red champion --blue swarmer --seconds 40 --out docs/video/<名前>.mp4`（ffmpeg が要る。`--facecam on` で両コーナーの顔の窓を両脇に出す。`--until-ko yes` で K.O. まで続け、`--seconds` は上限になる。`--speed 0.5 / 0.75 / 1`（既定 0.75）は試合そのものを遅く進めて撮る。`--tv on` はテレビ中継風: 5 ラウンド・歓声・*.events.json。`--facecam on` の練習試合は WVC の中継と同じ配置（上の真ん中に SPARRING と時計、顔の窓は左下・右下で窓の下に名札とスタミナ、窓の内側に部位ごとの酸素の体の図: 左腕・右腕・脚・胴を緑・黄・赤で下から満たし、減ると上から色が抜け、空なら枠だけ）。`--ring cage|weaponscage|boxing|weapons` で団体・武器から決まる既定の場の代わりにその場で撮る（例: `--org vfc --ring cage` で武器どうしを狭い檻に）。`--camera-scale 0.7` で寄る（既定 1。`intentvideo` も同じ）。疲れ方の確認用に `--red rush`（その場で弱・強を交互に打ち続ける）と `--blue stand`（構えて立つだけの的）、`--camera-focus red`（赤だけを写す）。同じ体を真横から近く撮って時刻ごとに並べる visual テストは `FatigueLookVisualTests`（`.visualcheck/fatigue_look_timeline.png`）。顔と体の肌の比べは `SkinToneVisualTests`） |
| 実況・解説を入れる | `powershell -ExecutionPolicy Bypass -File tools\commentary\dub.ps1 -Video in.mp4 -Script script.json -Out out.mp4`（台本は *.events.json を見て書く。`tools/commentary/README.md`） |
| WVC のテレビ中継（撮影〜結合を札 1 枚で） | `powershell -ExecutionPolicy Bypass -File tools\tvshow\tvshow.ps1 -Card tools\tvshow\cards\wvc-ishu.json`（種の下見・撮影・資料・実況の下書き・声入れ・点検・結合。台詞は札の `"commentary": { "writer": "llm" }` で LLM（Haiku）がテイクとして書く（`-Stage write`・`tools	vshowwrite-commentary.ps1`、テイクは `<out>	akesbN`）か、エージェントが `bN.extras.json` を書く: スキル `tv-commentary`。札に `"autoCommentary": true` なら下書きだけで作る。`docs/TV_BROADCAST.md`） |
| WVC のテレビ中継を生で流す（OBS へ） | `powershell -ExecutionPolicy Bypass -File tools\tvshow\tvshow.ps1 -Card tools\tvshow\cards\live-test.json -Stage liveprep`（準備: 種 → 描かない下回し `video --render off` → 台本 → 声）→ `-Stage live`（試合ごとに `video --tv on --facecam on ... --live srt://127.0.0.1:9000?mode=listener --voices bN_live_voices\manifest.json` で実時間に描いて流す。受け方・遅延・鍵は `docs/TV_BROADCAST.md`「生配信」） |
| 組み技のドリル（投げ・関節技） | `... -- throwlab [--only sasae-tsurikomi-ashi] [--victims stand,champion] [--bouts N] [--seconds S] [--trace yes] [--video out.mp4] [--log docs/arena/<名前>.md]`（柔術家が 1 技だけをかけ続け、投げ・掴めず・取りの転倒・受けの肘の荷重と靭帯・崩れの最大を数える。`docs/GRAPPLING.md` §6） |
| 軸足に乗って蹴る（蹴った側が倒れないか） | `... -- kickbalance [--style karate] [--kicks Front,Side] [--teachings 0:0:0,0.35:0.3:0] [--bouts N] [--seconds S] [--seed X] [--confirm Y] [--video out.mp4]`（教育の値 = 体重を移す間:頭を前へ:頭を前足の側へ。蹴り 1 本ごとの倒れた・当たり・J。`docs/TAEKWONDO.md`「軸足に乗って蹴る」） |
| VFC の倒れてから決着まで（名簿の組の試合） | `... -- groundtime --seeds 1,2 --out a.jsonl [--chars id,...] [--shard k/n]`、比べ: `--compare 前.jsonl,後.jsonl [--confirm yes] [--log out.md]`（倒れてから死・起き上がり・ゴングまでの秒・死の割合・打ち始め・攻め手も倒れていた割合。同じ組・同じ種で対に比べる。`docs/BOT_PDCA.md`「VFC の倒れた相手への攻めを短く」） |
| VFC のバトルロイヤル（N 人、既定 6 人） | `... -- royale [--fighters jin,konrad,sean,edgar,joao,vikram] [--count 3] [--teams 0,0,1,1,1,1] [--team-names エイリアン,人間] [--seed 1] [--seconds 300] [--rounds 5] [--break 15] [--trace 5] [--replay r.mrrep] [--out docs/video/royale.mp4]`、記録の再現 `... -- royale --verify r.mrrep`（脱落の順・とどめと、とどめを刺した人が受けたダメージのうち与えた割合・席ごとの手数・背中からの打撃・静止の時間・狙われた割合・1 秒あたりの計算時間。`--trace` は全員の様子（立つ / 倒れ・中心からの距離・狙い・速さ・倒れた相手への攻め）を何秒ごとに 1 行で。`docs/plans/VFC_ROYALE_PLAN.md`） |
| 物理の 1 フレームの時間を処理ごとに（docs/PERFORMANCE.md） | `... -- perf --scene fist\|katana\|vfc2\|royale [--seconds 60] [--seeds 1] [--render 1] [--alloc 1] [--check 0] [--out docs/perf/x.md]`（`Step(1/60)` ごとの合計と `StepProfile` の枠の平均・重い 1%・最大、数える量、GC。既定で計測を切った同じ試合を先に回し、チェックサムが同じか確かめる。`--render 1` で描画の詰め（`BoxingRenderer.Fill`・`FillVoxels`）も）。空いた tower32 で前後を比べる: `bash tools/remote-perf.sh docs/perf/<名前>.md [--rev <コミット>]`（1 種 1 プロセス） |
| とどめのドリル・練習（名簿のキャラ） | `... -- groundtime --drill id,... --victims id,... --seeds 1 --seconds 12 [--video out.mp4]`（受け手を気絶させて倒したまま、死ぬまでの秒）、`--practice id [--gens 6 --lambda 8 --parallel 2 --out id.json --log id.md]`（CMA-ES で教育を練習し、使わなかった受け手・種で比べ直す）→ `py -3 tools/ground-education.py id.json` で名簿の教育へ |
| フィニッシュ（立っている相手のチャンス） | `... -- groundtime --org wvc --chars ... --seeds 1,2 --out a.jsonl`（チャンスから倒すまで。`--compare` が表と対の比べを出す）、ドリル `--finish id,... --victims ... --seeds 2,3`、練習 `--finish-practice id [--gens --lambda --parallel --out --log]` |
| 狩りのドリル（四足の galva が素手で抵抗しない人間を倒す・首を断つまで） | `... -- huntlab [--goal kill\|neck\|form] [--victims jin,marcus,daichi] [--seeds 1,2] [--seconds 30] [--set K=V,...] [--trace 1]`（死ぬ・首を断つまでの秒、最初のダウン、死への近さ・首の断ち具合）、`--tune 1 --knobs 名前:下限:上限,... [--gens --lambda --parallel]`（CMA-ES）、`--confirm 1 --set ...`（練習に使わなかった相手 ramon・konrad・sean で比べ直し、`confirm.jsonl` に残す）、`--video out.mp4 --victim id --seed n`。受け手は `PassiveControl`（立って腕を下ろすだけ） |
| 倒れた相手への攻め（立ったまま真下へ突く・踏みつけ） | `... -- groundlab [--moves hammer,thrust,stomp,heel] [--bouts N] [--seconds S] [--practice yes --candidates 40] [--video out.mp4]`（受け手を気絶させて倒したまま。技ごとの打ち始め・当たり・J・攻め手の転倒。`docs/BOT_PDCA.md`「倒れた相手への攻め」） |
| 姿勢のモデルを比べる・揺さぶりで崩す | `... -- balancelab [--only old,cop,soft-poly,soft-disk,softcop]`（立つ・引く・歩く・蹴る・投げ・試合で、倒れた回数・浮いた時間）/ `... -- swaylab [--hz 0,1,2] [--ki 0,3] [--cycles 3] [--push 0.25]`（周波数ごとに揺さぶってから支え釣り込み足。`docs/GRAPPLING.md` §7・§9） |
| フォームの忠実さ（型・蹴りが目標の経路をどれだけ追えているか） | `bash tools/remote-test.sh probe:FormFidelityProbe`（剣の手・刃の向き・刃筋・体幹・スタミナを体の候補ごとに、回し蹴りの練習の前 / 後、鎌の格子。`docs/plans/FORM_DECAY_STUDY.md`）/ 回し蹴りの練習の前後の動画 `... -- kickshin --video out.mp4 --compare yes --heights mid,high` |
| 動きの 1 枚（3 面の残像・軌跡・時系列の帯・動きの言葉・自然さ）と、不自然な瞬間の一覧（足の引っかかり・滑り・関節の隙間・震え・急な加速・自傷・支えの外の重心・浮き・目標を追えない・左右の取り違え） | `... -- motioncard --scene jab\|sword\|kick\|claw\|bout [--style champion] [--weapon katana --form kesa] [--kick roundhouse_mid] [--red-char a --blue-char b] [--from S --to S \| --hit N --hand Left] --out media_tmp/motion/<名前>` / `... -- motionaudit` に同じ引数（一覧 + 見つけた瞬間ごとの絵 `--audit-cards 6`）/ `... -- motionaudit --replay x.mrrep [--who blue]`。絵だけ描き直す: `py -3 tools/motioncard.py <名前>.motion.json [--from --to]`。言葉の辞書 `docs/MOTION_WORDS.md`、計画と定義 `docs/plans/MOTION_TOOLS_PLAN.md` |
| 並べ比べ（教科書 / 練習の後 / 手本を当たりの瞬間でそろえて 3 面に重ね、末端の速さと自然さの数の差・人の幅の中外を表に） | 名簿のキャラの剣: `... -- motioncard --scene sword --char jin --form shomen --tune textbook\|trained --card no --out media_tmp/motion/a`、手本の蹴り: `... -- motionhuman --export S03-T01_01`、重ねる: `py -3 tools/motioncard.py --compare a.motion.json,b.motion.json[,c] --labels 教科書,教育 [--before 0.5 --after 0.3] --out x.compare.png`（そろえる時刻 = 区間の最初の当たり、無ければ末端の最高速: `alignTime`） |
| 人の幅（mocap: 極真の蹴り 18 本を motioncard と同じ式で測る。躍度・運動連鎖の順・床に着いた足の速さ・骨の長さのぶれ） | `... -- motionhuman [--dir data/mocap/kyokushin] [--out data/mocap/kyokushin/human_ranges.json] [--export <名前>]`。カードの「人の幅」の行と並べ比べの ○ ↓ ↑ が読む |
| 練習の共通の枠（練習の条件で選ぶ → 使わなかった条件で基準と対で比べ直す → `docs/arena/confirm.jsonl` → 前後の motioncard） | `PracticeFrame<TCandidate, TCase>`（games/MiniRing.Arena/PracticeFrame.cs）。移し替え済み: `... -- clawlab [--natural yes] [--cards yes]`（`--natural` は点から自然さの代価 `FormCost.OfNaturalness` も引く） |
| 回避の形を体の動きで測る | `... -- formlab --punch 3 --slot Weave --style swarmer [--set "HeadY=-0.8,Step=0.8"] [--cue 0] [--bouts 3]`（頭の横・上下・前の軌跡、腰の沈み、拳と頭の距離。`docs/BOT_PDCA.md`） |
| 団体の計量・ランキング・王座（docs/ORGS.md） | `... -- weighin --org ivf` / `rankings --org ivf --seeds 1,2,3 --report docs/arena/ivf_rankings.md` / `title --org ivf --winner a --loser b` |
| IVF の新人リーグ（docs/plans/IVF_PROSPECTS_PLAN.md） | `... -- circuit season --workers 24`（種から新人を作り興行を回す。tower32 で）/ `circuit register` / `circuit challenge --workers 5` / `circuit profiles` / `circuit report` / `circuit verify --bout s1-c02-heavy` |
| ボクセルの書き出し・確かめ | `... -- voxexport --out boxer.vox` / `voxcheck --in boxer.vox [--install yes]`（武器・はめる物は `weaponexport --out weapons.vox` / `weaponcheck --in weapons.vox [--install yes]`） |
| CT から体のボクセルを作る | `py -3 -I tools/ct-body/fetch.py survey`・`fetch.py subject s1029`（TotalSegmentator を範囲指定で 1 人ぶん、リポジトリの外 `../ct-data/` へ）→ `build.py s1029 --out ../ct-data/out`（部位の .vox）→ `render.py <今.vox> <CT.vox> --out compare.png`（正面・横・上・断面）。計画は `docs/plans/CT_BODY_PLAN.md` |

撮った動画・記録は、世代を区切るとき（`PhysicsVersion` を上げたマージ・大きな機能が master に入った）にワークツリーの分も含めて `media/<世代>/` へ移し、`docs/MEDIA.md` に足す。

動画を縮めるときは**音を残す**: `ffmpeg -i in.mp4 -vcodec libx264 -crf 30 -preset slow -vf scale=960:-2 -c:a aac -b:a 96k out_small.mp4`（`-an` は音を消す。過去にこれで音が消えた）。

## 警告にとどめるテスト（`Soft.Check`）

蹴りのように未成熟な所のテストは、`Soft.Check(bool ok, string what)`（tests/MiniRing.Tests/Soft.cs）で書く。条件が成り立たなければ `⚠ WARNING: <what>` をコンソールに出し、`Assert.Inconclusive` でそのテストを「結果なし」にする（失敗に数えない）。数の目安は書いたまま残るので、蹴りを磨いて目安に戻れば自然に通る。いまは KickShinTests と KickContactTests の、当たった本数・当たり先・転倒・足の部位に使っている。
### ガルヴァの骨格モーション

`dotnet run --project games/MiniRing.Arena -c Release -- galva-motion` で、7 本の BVH と見本動画を `.visualcheck/galva-motion` に出す。BVH は +Y が上、+Z が前、単位は m。ルートの前進を含み、足・腕・鎌まで 29 本のボーンがある。

| クリップ | 動き |
|---|---|
| `stalk` | 四拍の忍び足 |
| `trot` | 対角の二脚を組にした速歩 |
| `sweep` | 肩・肘から遅れて鎌が走る右横薙ぎ |
| `kesa` | 右上から左下に振り抜く右鎌の袈裟斬り |
| `cleave` | 両鎌の振り下ろし |
| `claw-thrust` | 踏み込んで左鎌で突く |
| `foreleg-thrust` | 三脚で支えて右前脚を突き出す |

`--only stalk,sweep` でクリップを選び、`--bones on` で骨格表示、`--render off` で BVH だけを書き出す。試合から独立した確認用ツールで、名簿のモデルから骨と形を読み、キーフレームと幾何 IK で姿勢を直接計算する。攻撃キーは前後の区間で速度をつなぎ、鎌の刃元から刃先までの軸と刃の内側の向きを個別に指定する。骨格動画の橙色の線が刃の内側を示す。

可動域付きガルヴァの Blender ファイルから、肩を支える胴体を固定した右鎌の軌道を生成できる。

```powershell
blender --background Galva-Attacks-Stable-Edge.blend --python-exit-code 1 --python tools/galva_analytic_cut.py -- --output Galva-Analytic-Target-Cut.blend
```

`--point X Y Z` はアーマチュア座標の刃先目標 [m]、`--direction X Y Z` は目標での進行方向、`--normal X Y Z` は切断面の法線。`--radius` で円弧の半径、`--rake` で刃の弦と半径のなす角を指定する。各関節の角速度上限 [deg/s] は `--rates` に肩の X・Y・Z、前腕のねじり、肘の屈伸、手首の順で渡す。`--runup` と `--follow` は目標前後の弧長 [m]、`--acceleration` は円弧に沿った加減速の上限 [m/s²]。これらはアニメーション用の設定値。

刃先の位置と刃面から肘の候補を幾何式で列挙し、ヤコビアンから各候補の速度上限を求める。円弧の全区間で可動域に収まる候補のうち、指定の助走と減速で目標を最も速く通過するものを `06 Analytic Target Sweep` に保存する。

同じ目標で腹（`Thorax`）と胸（`Chest`）のヨーを加える比較も生成できる。

```powershell
blender --background Galva-Analytic-Target-Cut.blend --python-exit-code 1 --python tools/galva_analytic_trunk_cut.py -- --output Galva-Analytic-Trunk-Cut.blend
```

両関節のひねり方を探索し、各候補について円弧全体の腕の可動域、角速度、足先の接地を確認する。腹と胸のヨーの上限はそれぞれ ±24°・±35°、角速度上限は 160°/s・200°/s。元の右鎌アクションと比較用の `07 Analytic Trunk Sweep` を出力ファイルの同じリグに収録する。
