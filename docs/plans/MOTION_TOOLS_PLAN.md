# 動きの道具（motioncard・motionaudit・動きの言葉）の計画と記録（2026-11-26）

ユーザーの依頼（2026-11-26）: ファイターを鍛える作業で、人と Claude が体の動きについて話しやすく、Claude が動きを直感的に掴み、人の手を借りずに直せ、
人らしい自然な動きを機械的に支える道具をそろえる。観測・評価の側の道具で、試合の物理・ボットの判断は変えない（`PhysicsVersion`・`LogicVersion` は上げない）。

## 0. 何が困っていたか（過去 114 セッションの調べ）と、どの道具が効くか

| # | 困りごと | 効く道具 |
|---|---|---|
| F1 | 数字は良いのに動きがおかしい（練習が当たりの量だけを点にし、形を見ない。FORM_DECAY_STUDY: 刃筋 17〜32°） | A1 motioncard・B4 motionaudit・B5 形の物差しの一本化・C6 自然さ・C7 代価の差し込み口 |
| F2 | 動きを言葉で伝えにくい（万歳・鉄棒をくぐる・U 字・軌道面） | A2 動きの言葉の辞書（`docs/MOTION_WORDS.md`）・A1 の時刻の目盛り |
| F3 | Claude が見られるのは `tools/sheet.py` の静止画だけ（軌跡・速さ・関節角・接地の時間変化が無い） | A1 motioncard（3 面の残像と軌跡 + 時系列の帯）・JSON の記録 |
| F4 | テストが見た目を固定しないので、形の退行（配線の取り違え・左右の入れ替わり・足の引っかかり・つま先とかかとの隙間・関節の隙間）を見つけるのがユーザー | B4 motionaudit（物理の定義で自動で一覧） |
| F5 | 練習が今の種・相手に合わせ込む | D8 練習の共通の枠（残し） |
| F6 | 動画に何を撮ったか（技・スタイル・種・版）が残らない | A1 の見出し（場面・版・種・コマンド）・A3（残し） |

## 1. 今ある道具との重なり（足すのでなく延ばす）

| 今ある物 | 重なり | どうしたか |
|---|---|---|
| `tools/sheet.py`（動画のコマを格子に） | 見た目を 1 枚で見る | 置き換えない。sheet は描画された見た目、motioncard は体の点の軌跡と量。両方を並べて使う |
| `StrikeDrill.Run(afterStep)`・`KickMocap.Doll(onStep)`・`ClawLab.Run` | 1 回の技の場面 | 場面はこれらをそのまま使う（`ClawLab.Run` に `afterStep` を足しただけ。試行の中身は同じ） |
| `FormFidelityProbe`・`HandleLab`・`SwordPractice` の刃筋の式 | 同じ式が 3 か所 | `MotionMetrics.EdgeOffPlaneDegrees` 1 か所へ寄せ、3 か所から呼ぶ（§1.5） |
| `FormCost` | 練習の形の代価 | 自然さの項 `FormCost.OfNaturalness` を足した（まだどの練習も使わない: §C7） |
| `formlab`（回避の頭の軌跡） | 頭・腰の軌跡 | 残す。motioncard は回避にも使えるが、formlab の「拳と頭の距離」は相手との関係の量で別 |
| replay（`.mrrep`）・`ReplayPlayer` | 試合の再現 | `--replay` の場面として今のビルドで再生しながら記録する |
| `imitate --vrmotion`・`tools/mocap` | 人の動きの手本 | A3 の並べ比べの手本の側に使う（残し） |

## 2. 作った物（2026-11-26）

コードは `games/MiniRing.Arena/MotionTools/`、絵は `tools/motioncard.py`、テストは `tests/MiniRing.Tests/MotionMetricsTests.cs`。

### A1 motioncard（困りごと F1・F2・F3・F6）

```
dotnet run --project games/MiniRing.Arena -c Release -- motioncard --scene jab [--style champion] [--seconds 6] --out media_tmp/motion/jab
... motioncard --scene sword --weapon katana --form kesa --out media_tmp/motion/katana_kesa
... motioncard --scene kick --kick roundhouse_mid [--rear no] [--body taeyun] [--victim joao] --out media_tmp/motion/kick
... motioncard --scene claw [--opponent jin] [--aim 0] [--hand right] --out media_tmp/motion/claw
... motioncard --scene bout --red champion --blue swarmer | --red-char jin --blue-char konrad [--who blue] [--seconds 20] --out ...
... motioncard --replay replays/x.mrrep [--who blue] [--seconds 30] --out ...
    共通: [--from S --to S] [--hit N] [--hand Left|Right] [--seed N] [--every 2] [--card no]
py -3 tools/motioncard.py x.motion.json [--from S --to S] [--ghosts 6] [--audit-cards N]   # JSON から絵だけ描き直す
```

- 記録（`MotionTrace`、既定で 120 Hz）: 頭・胸・腰・重心・両肩・両肘・両手・両股関節・両膝・両足首・両かかと・両つま先・刃先の位置、手の目標、
  かかと・つま先ごとの床の接地（`PhysicsWorld.Contacts` の床との接触、深さ ≥ 0）、関節のいちばん大きい隙間、刃筋のずれ、スタミナ、倒れた、当たり（手・部位）、
  自分を削ったセル、相手の向き・頭。見出しに場面・`PhysicsVersion`・`LogicVersion`・種・コマンドを残す（F6）。
- 絵（1 枚）: 正面（相手から見る）・横・上の 3 面に、時刻の色の残像（棒人間）と、点ごとの色の軌跡（0.1 s ごとの時刻の色の点）、相手の頭。
  下に 0.1 s 目盛りの時系列の帯: 速さ・関節角（膝・肘・腰の折れ・胴のひねり）・高さ・接地・刃筋 / 関節の隙間 / 重心の支えの外。
  当たりは赤い縦線、motionaudit の見つけた瞬間は色の帯。最後に自然さの数・見つけた瞬間・動きの言葉の一覧。「0.4 s で頭が高い」と指させる。
- 区間: `--from/--to`、無ければ `--hit N`（N 回目の当たり、`--hand` で手を絞る。jab は既定で左手）の前 0.7 s〜後 0.4 s、当たりが無ければ末端のいちばん速い時刻の前後。
- 近道: 股関節の点は腰の剛体から左右 9 cm（この体の両腿は腰の真ん中の 1 点につながっていて、関節の点からは腰の線が出ない）。四足（ガルヴァ）の後ろ脚は記録しない
  （前の 2 本だけ。ガルヴァの接地・支え・足首の隙間の数はそのため当てにならない）。鎌の刃先は記録せず手の点（`BladeTipWorld` が無い・`WeaponOf` が無い）。

### B4 motionaudit（F1・F4）

`motioncard` と同じ引数。記録の全体を調べて一覧を出し、`--audit-cards N`（既定 6）枚の絵を「種類ごとに 1 枚目を先に」描く（`<out>.audit<i>_<種類>.png`）。
定義（閾値は `MotionAudit` の定数。道具の感度で、試合には入らない）:

| 種類 | 物理の定義 |
|---|---|
| gap | 関節の両側の点（拘束の点。つま先は作ったときの母趾球の点）の距離 > 3 cm が 0.05 s 以上（`Fighter.JointGaps`、読むだけの関数を足した） |
| snag | 足（かかとかつま先）が床に着いたまま、股関節〜足首が脚の長さの 97 % 以上に伸び、腰がその足から 0.3 m/s 以上で離れていく、が 0.08 s 以上 |
| slide | 床に着いた点が床に沿って 1.5 m/s より速く動く、が 0.08 s 以上（すり足は設計どおり床を運ぶので、人のすり足 ~1 m/s を超えた分だけ） |
| jitter | 0.1 s の窓で加速度の向き（30 m/s² 以上）が 6 回以上入れ替わる（30 Hz 以上の振動）、頭・腰・両手・刃先・つま先 |
| spike | 当たりの前後 0.06 s でないのに加速度 > 1500 m/s² |
| self | 持った物（武器・はめた物）で自分の体のセルを削った |
| balance | 立っていて、重心の真下が接地の点の凸包の外へ 5 cm 以上、0.3 s 以上 |
| airborne | 立っているのに両足が床から 0.25 s 以上離れる |
| tracking | 手の目標と実際の手の距離 > 腕の長さの 4 割、0.25 s 以上 |
| swap | 左手の目標の近くに右手、右手の目標の近くに左手（10 cm 以上近い）、0.1 s 以上（配線の左右の取り違え） |
| down | 倒れている（記録だけ） |

### B5 形の物差しの一本化（F1、§1.5）

`MotionMetrics` に刃筋（`EdgeOffPlaneDegrees`）・目標を追えた量（`TrackingRms`）・関節角・腰の折れ・胴のひねり・軌道面（`PlaneFit`）・支えの距離などを集め、
`FormFidelityProbe`・`HandleLab`・`SwordPractice` の刃筋は同じ関数を呼ぶ（値は前と同じ式）。拳・蹴り・剣・鎌のどれにも同じ式を使う。
残し: `KickShin.Trial` の膝の追従・`ClawLab` の面からのずれを `MotionMetrics` の関数へ寄せる（値の意味は同じ。測る点が技ごとに違うだけ）。

### C6 自然さの物差し（F1）

`MotionWords.Natural`（motioncard の見出しの「自然さ」）: 区間でいちばん速い末端（手・刃先・つま先、jab は左手）を打つ所として、
- 無次元の躍度（Hogan と Flash、∫|jerk|² dt·T⁵/L²、L は経路の長さ。最小躍度の動きで 720 = log10 2.86。前後 2 点の移動平均の後、打ちの山の ±0.3 s）
- 運動連鎖の順（拳・刃: 腰 → 胸 → 肩 → 肘 → 手（→ 刃先）、蹴り: 腰 → 股関節 → 膝 → 足首 → つま先 の速さの山の時刻が近い所から順か、隣どうしの割合）
- 頭の揺れ（頭の加速度の RMS）、重心が支えの外にいた時間、胴のひねりの幅、末端の軌道面からのずれ、U 字の深さ
- 残し: 関節ごとの仕事の割り振り（関節のトルク × 角速度。トルクを記録に足す必要がある）、左右の釣り合い・逆の回し（腕の振りと腰）、人の幅（mocap からの値。`tools/mocap` の BVH を同じ点に写せば同じ関数で出せる）。

測った値（2026-11-26、物理 102・ロジック 73。人の値との比べはまだ）:

| 場面 | 末端 | 最高速 | 躍度 log10 | 連鎖の順 | 頭の揺れ | 支えの外 |
|---|---|---|---|---|---|---|
| champion のジャブ（左手） | handL | 3.50 m/s | 5.55 | 0.75（肘が肩より先） | 9 m/s² | 0.44 s |
| 刀の袈裟 | 刃先 | 16.0 m/s | 5.23 | 0.80（肘が先） | 15 m/s² | 0.08 s |
| 回し蹴り（中段・後ろ足） | toeR | 8.69 m/s | 5.12 | 0.75（膝が股関節より先） | 7 m/s² | 0.39 s |
| ガルヴァの鎌（右） | handR | 4.69 m/s | 5.08 | 0.75 | 13 m/s² | 0.81 s（後ろ脚を見ていない） |

躍度は最小躍度より 2〜3 桁大きい（1 本の打ちの中に何度も加速・減速がある）。

### C7 代価の差し込み口（F1）

`FormCost.OfNaturalness(chainOrder, normalizedJerk)`: 連鎖の乱れ 1 あたり 1.0、躍度が 720 の 10 倍ごとに 0.5。どの練習もまだ使わない。
採るときは練習ごとに、使わなかった種・相手で比べ直して（§1.8）、`docs/arena/confirm.jsonl` に残す（重みもそこで決める）。

### A2 動きの言葉の辞書（F2）

`docs/MOTION_WORDS.md`（たとえ → 量・式・単位）と `MotionWords.Captions`（0.1 s ごとに「頭が 7 cm 下がる、腰から 14° 前へ折る、胴を 50° 右へひねる、右手が 25 cm 前へ出る、
左足が着く（22 cm 前へ）、万歳、当たり: Left:SolarPlexus」）。motioncard の下と JSON の `captions` に入る。

## 3. 確かめたこと（2026-11-26）

例の絵（`media_tmp/motion/`、git の外）: `jab_champion.card.png`・`katana_kesa.card.png`・`kick_roundhouse_mid.card.png`・`claw_galva.card.png`・
`replay_champion.card.png`（`recordbout --red champion --blue swarmer --seconds 20` の記録）と、それぞれの `.audit<i>_<種類>.png`。自分で見て:

- 3 面の残像と軌跡で、袈裟の刃先の大きな弧・回し蹴りのつま先の弧・ジャブの左手の出入りが読める。時刻の目盛りで「何秒に何が」を言える。
- 知られた問題を拾う: **足の引っかかり**（ジャブで右足が床に残ったまま腰が離れる 0.42〜0.74 s ほか、袈裟・回し蹴りでも）、**つま先とかかとの隙間**（回し蹴りの蹴り足 RightToe が 7.9 cm 開く 0.47〜0.78 s）、
  **関節の隙間**（足首・肩が 3〜8 cm 開く瞬間が試合中に多い。生まれた時の隙間は 0.1 cm 以下なので、動きの中で開いている）、刃先の震え（袈裟の後 1.7〜2.4 s）。
  左右の取り違えは今の場面では出ない（合成のテストで見つけることを確かめた）。
- 見つけた量の多い順（20 s の試合、champion）: slide 60・gap 49・tracking 19・snag 11・balance 4。足が床を 2〜4 m/s で滑る瞬間が多い（すり足の運びの速さが人より大きい）。
  関節の隙間と足の滑りは、物理の統合の作業（DEBT の「関節のつなぎ」「体を支える力」）で見る値として渡す。

テスト: `MotionMetricsTests`（12 件、速い段）: 最小躍度 720・2 つの小動作の方が躍度が大きい・連鎖の順 1 / 0・支えの内外・40 Hz だけが震え・角 / ひねり / 軌道面 / U 字・
区間の抽出・自然さの代価・静かに立つ体は何も見つからない・つま先の隙間・置き去りの足（引っかかり）・左右の取り違え。

## 4. 作った物（2026-11-26 後半）と残したこと

### 作った物
- **A3 並べ比べ**: `tools/motioncard.py --compare a,b[,c] --labels …`。各記録の `alignTime`（区間の最初の当たり、無ければ末端の最高速: `MotionCommand.AlignTime`）でそろえ、3 面に太線（その瞬間）・薄線（−0.2/−0.1/+0.1 s）・末端と刃先の軌跡を重ね、末端の速さの帯と、自然さの数（最高速・躍度・連鎖の順・頭の揺れ・ひねりの幅・軌道面・U 字）の表と差・人の幅の ○↓↑ を出す。
  名簿の教育で撮る: `motioncard --scene sword --char jin --form shomen --tune textbook|trained`（`SwordPractice.Swing` と同じ 1 振り）。手本: `motionhuman --export <名前>` が mocap の蹴りを .motion.json にする（.vrmotion を記録にする口はまだ）。
  例: jin の shomen、教科書 vs 教育（Tempo 1.51・Twist 0.13）。教育の方は刃先が床近くまで落ちる（U 字 85 cm 対 26 cm、ひねり 29° 対 12°）。jin の教育に kesa の差分は無い（kesa の比べは同じ 2 本になる）。
- **動画の焼き込み**: `ShotLabel`。`video`（中継の体裁・顔の窓を除く）と `imitate` の左下に、コマンド・種・物理と判断の版の 1 行。
- **D8 練習の共通の枠**: `PracticeFrame<TCandidate, TCase>`（練習の条件で選ぶ → held-out で基準と対で比べる → 採るのは差 > 0 かつ 95% 区間の下端 ≥ 0 → `ConfirmLog.Append` → 前後の motioncard）。clawlab を移した（採る条件は前の「平均の差 > 0」より厳しくなった）。swordpractice（CMA-ES の世代・並列・相手の体の表）と kickshin（高さごとの JSON・世代）はまだ。点の式と held-out の作り方が道具ごとに違うので、`Score`・`Train`・`HeldOut` を渡す形のまま移せるが、世代の回し方（ES）は枠の外に置く。
- **四足の記録**: 後ろ脚の腿・すね・かかと・つま先（`rthighL` … `rtoeR`）と後ろ脚の接地 4 つ、鎌の刃先（両手 `tip`・`tipL`: 握り + 刃の弦、ClawLab と同じ点）。支えの凸包・浮き・滑りは後ろ脚も数える。`Fighter.RearLegBodies`（観測だけの読み出し、物理は変えない）。
  ガルヴァで見えたこと: 鎌の振りの間、後ろ脚はほとんど床から浮き（5〜8 cm）、重心が支えの外へ 20 cm 出たまま。前の左の足首の関節が始めから終わりまで 8.7 cm 開いている（動きでなく形の隙間。足の物理を直している別の作業へ渡す）。後ろ脚の股・膝の関節（`RearRig.Joints`）の隙間はまだ記録に無い。
- **閾値と人の幅**: `motionhuman`（極真の蹴り 18 本を同じ式で）: 躍度 log10 5.33〜6.09、連鎖の順 0.75〜1.00、床に着いた足の速さ p95 1.75・p99 2.6 m/s、骨の長さのぶれ最大 8.6 cm（計測のぶれ）。`data/mocap/kyokushin/human_ranges.json`。滑りの閾値を 1.5 → 1.75 m/s（人の p95）。隙間 3 cm は据え置き（人の関節は開かない、すねの 7%。計測のぶれは根拠にしない）。カードに「人の幅」の行。蹴りの計測なので拳・刃では目安（剣の mocap `data/mocap/sfu_kendo` の .bvh を同じ式に通すのが次）。
- **C7 自然さの代価を練習に入れる**: clawlab で比べた（下）。採らない。

### C7 の結果（clawlab、2026-11-26）
| 点 | 練習で選んだ振り | held-out（動く ramon・albert、6 試行）選んだ − 基準 | 決め |
|---|---|---|---|
| 形の代価だけ | 0.42 s・溜め 0.75 | +2.75（95% −0.98〜6.48、符号 +4/−2） | 基準のまま |
| ＋自然さの代価 | 0.42 s・溜め 0.75（同じ） | +2.82（95% −0.80〜6.44、符号 +4/−2） | 基準のまま |

自然さの項は点を一様に 1.5 ほど下げただけで、候補の順も選ぶ物も変わらなかった。鎌の振りでは今の重みの自然さの項は効かない。名簿には入れない。効くかを見るなら、躍度の幅が候補で大きく違う練習（kickshin の高さ・剣の Tempo）で。記録は docs/arena/confirm.jsonl（clawlab・clawlab-natural）。

### 残したこと（優先の順）
1. swordpractice・kickshin を `PracticeFrame` へ（上の理由で、ES の世代は外に置き、比べ直し・台帳・前後のカードだけ枠を使う）。
2. C7 を Tempo の幅がある剣の練習で比べ直す。
3. .vrmotion を記録にして並べ比べの手本に。剣の mocap（sfu_kendo）で人の幅を刃・腕へ広げる。
4. ガルヴァの後ろ脚の関節の隙間、関節ごとの仕事（トルク）。
5. **D9 場面の切り出し**（設計だけ、試合の計算に触るので別の作業）: .mrrep は入力だけを持つ。決定性を保ついちばん安い道は「試行ごとに始めから t まで記録の入力で回し直す」（`ReplayPlayer.PlayUntil(t)`、体・ボット・乱数の状態はビットで同じになる。費用は 1 試行あたり t 秒の物理: 240 Hz で実時間の数倍速なので t = 60 s で十数秒）。t から先を変えるには `BoxingGame` に「この歩から先は記録の外の入力・制御を使う」口が要る（ボットの差し替え）。世界の複製（Bepu・ボクセル・ボットの内部）は大きく、回し直しで足りるうちは作らない。確かめ方: 何も変えない試行が記録の残りのチェックサムと一致すること。
6. 閾値を「ユーザーがおかしいと言った瞬間」で合わせ直す（例の記録が溜まってから）。
