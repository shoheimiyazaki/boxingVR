# VR で武器を使う・VR の動きを記録して手本にする（2026-11-17）

指示書は `docs/plans/CLAUDE_BRIEF_VR_MOTION_2026-11-17.md`。VR の実機が無い環境で作ったので、**実機で確かめていない所は末尾の「実機で試してほしいこと」**に分けて書く。

## 1. 武器を持つ（`--weapon`）

```
miniring-vr --weapon staff                       # 自分が棒、相手も棒（champion の教科書の型）
miniring-vr --weapon katana --cpu-weapon none    # 相手は素手
miniring-vr --weapon longsword --cpu jabber      # 相手のスタイルを選ぶ（BotCatalog の名前）
```

- `--weapon`: `staff` / `katana` / `longsword` / `ironsword` / `woodenclub` など（`WeaponKind` の名前、大文字小文字は問わない）。棒・刀・ロングソードは両手持ち（既存の `Support` の作り方）。
- `--cpu-weapon <kind|none>`: 相手の武器。既定は自分と同じ。`--weapon` が無いと使えない。
- `--cpu <style>`: 相手のスタイル（既定 `champion`）。キャラクター（roster）の選択は未対応。
- **グリップで持つ（体の側、2026-11-19）**: 握っていない手が `HandCommand.Grip = true` で、手が柄から `FighterTuning.WeaponGraspReach`（15.2 cm = 1.8 × 掌幅 84.5 mm。2026-11-24 の手の実寸化で 14.1 cm から。大きくなった手が柄のそばで止まる距離 14.2 cm を超えて握れなくなったため）以内にあれば握る。自分が握っている武器があれば、その手として添える（先に握った手から柄頭の側へ `FormSwordsman.Spacing` × 腕の長さ、柄の端まで）。なければ、落ちている武器を鍔に近い握り（`Weapon.GripOf`）で拾う（左手だけでも可）。武器は動かさず、今のずれのまま玉継手でつなぐ。素手の拳をはめていれば外す。`HandCommand.LetGo` は握っている手だけを放し、もう一方がまだ握っていればその手が片手で持つ（手首のバネは `OneHandWristShare`）。ボット・従来の操作は Grip を出さないので変わらない。テスト: `WeaponGraspTests`。小さい体の腕は床に届かないので、床の武器を拾うには写像の側でしゃがむ・体を折る必要がある（体の側では、手が柄の近くに来れば拾える）。
- `--weapon` を付けなければ今まで通りの素手の VR（`BoxingCorners.Default`、`Orientation` null・`Slide` false）。

### 入力の写像（`VrPlayerMapper`、ホスト非依存）

| 入力 | 指令 |
|---|---|
| コントローラの位置 | 体フレームの手の目標（今まで通り。構えからのずれを 2 倍） |
| **トリガー**（oculus touch / index: `trigger/value`、simple: `select/click`） | 引いている手は握る、離している手は `HandCommand.Slide = true`（握った棒が手の中を滑る）。ヒステリシスは握りと同じ（押した 0.55・離した 0.35） |
| **コントローラの向き** | `HandCommand.Orientation`（武器のローカル X = コントローラの前 = grip 姿勢の −Z、Y = 上、Z = 右）。体フレームへは位置と同じ肩のフレームで写す。両手に入れる（どちらを使うかは体の側が決める） |
| グリップ（squeeze） | 今まで通り掴む（クリンチ） |
| A / X | 身長キャリブレーション（今まで通り） |
| **B / Y** | VR の動きの記録の開始・停止 |

`Slide` を使うのは入力の側まで。体の側（棒が実際に手の中を滑る）は別の作業（棒の握り）で、それが入るまでは `Slide` は無視される。握れる武器を持たない手では元々意味が無い。

## 2. VR の動きの記録（`.vrmotion`）

- **開始・停止**: B/Y ボタン（1 回の稽古ごとに 1 ファイル）、または `--record-motion`（起動から終了まで。B/Y で区切ってもよい）。終了時に記録中なら保存する。
- **保存先**: `motions/<日時>_<武器>.vrmotion`（`replays/` と同じ流儀、git の外。素手は `fists`）。書けないときは理由を出して試合は続ける。
- **形式**: 文字の JSON。`format`/`version`（1）/`playerHeight`/`playerArmReach`/`puppetArmReach`/`motionGain`/`weapon`/`note`/`columns`/`frames`（1 フレーム = 数の並び、列の名前は `columns`）。版・列が合わなければ読み込みで `FormatException`。
- **1 フレーム**（約 60〜100 Hz。`VrMotionRecorder.MinInterval` 10 ms より短い刻みは捨てる。時刻は試合の刻みの和で、時計は使わない）:
  - 生の入力（人の体の寸法 [m]、ワールド）: 頭の位置・向き、両手の位置・向き・トラッキング、トリガー・グリップ、スティック。
  - 写像後（体フレーム、**腕の長さ = 1**）: 両手の目標、武器の向き、`Slide`、掴み、頭のずれ、歩く向き（視線のヨー基準）。
- **長さの単位**: 人の体の寸法のまま（生の入力）と、腕の長さで割った正規化の値（写像後）の両方。`VrMotionClip.ToHuman` / `ToNormalized` で行き来できる（`PlayerArmReach` = 身長 × 0.36）。`StaffForms` も腕の長さ = 1 なので、そのまま同じ尺度。
- コード: `games/MiniRing/Motion/VrMotionClip.cs`（形式・読み書き・補間）、`VrMotionRecorder.cs`、`VrPlayerMapper.cs`、`VrRawFrame.cs`。ホスト側は `games/MiniRing.Vr/VrMotionSession.cs`（ボタンで開始・停止）、`VrMatchOptions.cs`（引数）。

## 3. 手本として使う（なぞる）

`VrMotionImitation : IFighterControl`（`MotionImitation` と同じ考え方）。記録した正規化の両手の目標・武器の向き・`Slide`・頭のずれ・歩きを、その人形の腕の長さで当てはめて `FighterCommand` を出す。体はバネで追う（§1.1。追いつけなければ遅れる）。再生は渡された刻みの和だけで進む（§1.6）。

```
miniring-arena vrmotion-synth --out docs/vrmotion/staff_thrust_synthetic.vrmotion     # 合成の記録（棒の突き）を作る
miniring-arena imitate --vrmotion motions/xxx.vrmotion [--weapon staff] [--slow 2] [--rate 1] --out x.mp4
miniring-arena imitate --vrmotion x.vrmotion --measure yes                            # 動画を作らず、両手の目標からのずれだけ
```

動画は赤が武器を持って相手に背を向けてなぞる。金の線・十字は両手の目標（右 = 金、左 = 水色）、画面の隅に目標からのずれ（腕の長さ = 1）と、トリガーを離している手の表示。

`docs/vrmotion/staff_thrust_synthetic.vrmotion` は実機の代わりの合成（身長 1.7 m の人が棒を両手で持ち、少し引いて 14 cm 突く。突きの間は左手のトリガーを離す）。人の体の寸法の軌跡を作って実機と同じ `VrPlayerMapper` → `VrMotionRecorder` に通したもの。なぞり方（2026-11-17 の測定）: 右手の目標からのずれ 平均 0.05・左手 0.23（腕の長さ = 1。左手は両手持ちの棒の握りが決める側）。

## 4. 次の段（今回は作っていない）

- 記録から技の経路（`StaffForms` のキー: 右手の位置・棒の向き・ひねり）を取り出す。正規化（腕の長さ = 1）の両手の目標と武器の向きがそのまま材料になる。
- ボットの教育（`docs/CHARACTERS.md` §教育）の経路の種に使う。人が打った突き・払いを `PunchSpec` / 型のキーへ写し、CMA-ES（`cmatune`）で磨く。
- 体の側の滑る握り（`Slide` を実際に棒の上の握りの移動にする）が入ったら、記録の `Slide` と両手の間隔を持ち替えの手本にする。
- 向き（`Orientation`）の roll の流儀（刃筋）は、棒には効かないが刀・剣には効く。実機で振って確かめる。

## 5. お手本づくりの稽古（`run_vr_training_katana.cmd` = `run.cmd vr --training katana`、2026-11-19）

正眼（中段）に構えて動かない相手へ切り込む動きを、型ごとに記録する。記録はそのまま §3 の「なぞる」や、型のキーを取り出す材料になる。

| ボタン | 何が起きるか |
|---|---|
| 右スティック 左・右 | 前・次の型（刀は `FormSwordsman.Forms(Katana)` の 20 型の順: shomen・kesa・…）。変えるたびにワールドを作り直し、その型の間で向き合う。記録は型ごとに新しいファイルへ切り替わる。右手に短い振動 |
| 左スティック | 歩く（`--first-person` では VR の空間の原点をワールドで動かし、体がそれを追って歩く） |
| グリップ（左右） | 武器を持つ（§6）。両手持ちで両方を押している間は右のコントローラが両手を動かす（`--two-hand right`、既定）。離した手は武器を放す |
| トリガー | 使わない |
| **A 単独** | 身長の合わせ（まっすぐ立って押し、**離したとき**に働く。4 つ同時を押したあとは合わせない）。一人称では「VR の空間 → ワールド」の変換もこのとき決まる |
| **A + B + X + Y を同時に** | 同じ型のまま作り直す（相手を斬り崩したあと・立ち位置がずれたとき）。両手に短い振動。記録も新しいファイルになる。一人称では変換を作り直した体の頭へ合わせ直す |
| **X（左コントローラ）** | 左脚の蹴り。間合いで蹴りが決まる: 遠ければ前蹴り（`KickTechnique.Front`）、近ければ膝蹴り（`Knee`）。`KickRunner.Choose` が、今の胸と胸の距離（腕の長さの倍数）と各蹴りの届く間合い（`KickSpec.StandoffFor`）の近い方を選ぶ（閾値は持たない）。手は蹴りの間もコントローラを追う |
| **Y（左コントローラ）** | 右脚の蹴り（X と同じ） |
| B（右コントローラ） | 4 つ同時の一部としてだけ使う。A か B を押している間の X・Y は蹴りにならない（4 つ同時を押す途中で蹴らない） |

- **記録は常に取る**: 稽古を始めたときから続け、型を変える・作り直すたびに保存して新しい 1 ファイル（`motions/<日時>_<武器>_<型>.vrmotion`、注に `training <型> gap <間> m (seigan target)`）。終了時に今の分を保存。B/Y の開始・停止は稽古では使わない（試合の `--record-motion` と B/Y は今まで通り）。
- **ボタンの割り当て（OpenXR の提案）**: Oculus Touch は左 X・Y、右 A・B（`x/click`・`y/click`・`a/click`・`b/click`）。Valve Index は左右の A・B だけ（左の A・B が X・Y、右の A・B が A・B に当たる）なので、4 つ同時・蹴りも同じ形で使える。simple controller にはフェイスボタンが無い（メニューだけ）ので、稽古の操作は使えない。ヘッドセットのメニュー（Quest の Oculus ボタン）は別。
- **武器**: `run_vr_training_katana.cmd`（① 右手で両手）・`_katana_fpv.cmd`（② 一人称）に加え、`run_vr_training_longsword.cmd`・`jo`・`club`（こんぼう = 片手。型はバットの型を借りる。`docs/DEBT.md`）・`swordshield`（片手剣 + 左手の盾: `--shield`。左のコントローラが盾を動かす）と、それぞれの `_fpv.cmd`（`jo`・`longsword`・`swordshield`）。`--training` は `katana`/`longsword`/`staff`/`jo`/`ironsword`/`bat`/`woodenclub`。

- **始めの間**（`SwordTraining.Forms`）: その型の動き（踏み込みを含む）で物打ちが相手の体の面にちょうど届く距離（`StrikeDrill.ReachOf`）。試合の技の下限（0.33 m）では外さず、全部の型を並べる（柄頭打ち・押し斬りも）。下限 0.2 m・上限はリングの縁から 20 cm 内。コンソールに型の名前と間（ゲームの寸法と人の大きさ）を出す。
- **相手**（`GuardHold`）: 武器の型の構え（刀は中段 = `FormSwordsman.Guard`）を相手へ向いてとり続ける。歩かない・打たない・受けない。体はバネで構えを追うので、打たれれば持っていかれ、構えへ戻る。
- **ワールド**: 武器のドリルのリング（`RingLayout.DrillWeapons`、2.4 m）の中央で向き合う。審判・休みの動作・ラウンドは無し。試合の記録（replays）は取らない。型を変える・作り直す（A+B+X+Y）と `BoxingGame` を作り直す（§1.1: 位置は書き込まない）。身長の合わせ（写像）は作り直しても残る。
- コード: `games/MiniRing/Motion/SwordTraining.cs`（型の並びと間・試合の設定・`GuardHold`・`StickFlick`）、`games/MiniRing.Vr/VrTrainingSession.cs`（スティック・記録・振動）、`VrMotionSession` に記録の名前と注・`Stop`・`Restart`、`Motion/KickRunner.cs`（蹴りの実行と `FaceButtonTracker`）。テスト `SwordTrainingTests`。
- **確かめていないこと（実機が無い）**: ヘッドセットで、型を変えたときに立ち位置が正しく変わるか・振動が届くか・相手が正眼に見えるか。ヘッドセットの中では型の名前が見えない（コンソールだけ）ので、型の順と振動で数える。見える札（ワールドの中の文字）は未実装。

## 6. 刀の VR 操作: グリップで持つ・① 右手で両手・② 一人称（2026-11-19）

指示書は `docs/plans/SONNET_BRIEF_VR_KATANA_2026-11-19.md`（A）。VR の実機が無い環境で作ったので、確かめたことと確かめていないことを分けて書く。

### 6.1 グリップで持つ（武器の試合の写像）

- `VrPlayerMapper.GripHolds`（VR ホストの武器の試合で有効）: グリップ = `HandCommand.Grip`（素手の掴みと同じ欄）。**トリガーは使わない**（`Slide` は常に false）。握っていない手で、手が柄の近くにあれば持つ（拾う・添える）のは体の側（B の作業）。
- 武器を握っている手でグリップを**離した瞬間**だけ `HandCommand.LetGo = true` を 1 歩出す（離しっぱなしでは出さない: 始めに握っていた武器を、グリップを押したことのない人がいきなり落とさない。フォーカスを失った離しでも出さない）。握っていない（`SelfWeapon.Held` が false の）ときは出さない。
- 旧（トリガーで握る・離すと滑る）は `GripHolds = false` で残してある（合成の記録 `VrMotionSynth` と旧の単体テスト用）。

### 6.2 ① `--two-hand right`（既定）

両手持ちの武器（`FormSwordsman.Handles`: 刀・ロングソード・棒・杖・バット）で、**左右のグリップを両方押している間**、左手の目標は左のコントローラでなく、右のコントローラの姿勢から決めた柄の上の点: 右手の目標から武器の軸（右の向きの前 = 武器のローカル X）に沿って柄頭の側へ、`FormSwordsman.Spacing(weapon)` × 人形の腕の長さ。左手の武器の向きも右と同じ。左のグリップを離せば左手は左のコントローラを追う（片手持ち）。`--two-hand both` は左右のコントローラがそれぞれの手（今まで通り）。片手の武器（片手剣・こんぼう）では効かない（片手剣の左手は盾、こんぼうの左手は空き）。

### 6.3 ② `--first-person`

- カメラは HMD の姿勢のまま（人形の頭に固定しない）。`VrWorldPlacement`（VR の空間 → ワールドの相似変換）を身長の合わせ（A、最初のフレームでも自動）で決めて固定: 縮尺 = 人形の腕の長さ ÷ 人の腕の長さ（`PuppetMapping.Scale`。目の間隔も同じ縮尺で縮む）、位置 = そのときの HMD が人形の頭（`FighterSense.SelfHead` = 頭の中心。目はやや前・上）に重なる、向き = そのときの HMD のヨーが人形の向きに重なる。その後は人形が押されても動かない。描画は `Program.cs` の目ごとの視点に変換を掛ける（近い面も縮尺どおり）。
- 体の頭の目標 = HMD のワールドの位置。水平のずれは歩く指令 `VrPlayerMapper.WalkToward`: ずれが `WalkDeadZone`（0.15）× 腕の長さの間は 0（`HeadOffset` で傾けるだけ。傾きの水平は `LeanLimit` 0.3 まで）、そこから `WalkFullRange`（0.7）× 腕の長さまで比例して歩く速さへ。高さ（ダッキング）は `HeadOffset`。
- **両手（2026-11-24）**: 目の描画と同じ変換でコントローラをワールドへ写し、体の今のフレームへ戻したものが手の目標（`VrPlayerMapper.FirstPersonHandLocal`。増幅しない: `HandGain` = 1）。武器の向き（`HandCommand.Orientation`）もコントローラの向きを同じ変換で回したもの。これで手の目標の玉（描いている目標）は見えているコントローラ（実際の手）に重なる。それまでは三人称と同じ写し方（人の肩のフレーム・視線を緩く追うヨー・構えからのずれ 2 倍）で、目標がコントローラから大きくずれ、相手へロックオンした体の向きと視線の向きの差でも横へずれていた（ユーザーの指摘「コントローラと位置が違うので思った位置に振れません」）。
  - **外した増幅の影響**: 2 倍は 60 cm の卓上の人形を外から操るとき、人の小さな動きでも人形の腕が伸び切るためのもの（`HandTargetLocal` の注釈: 構えを中心に増幅し、届かない分は腕の IK が止める）。一人称では縮尺（人形の腕 ÷ 人の腕、0.85 前後）だけになるので、**届く所まで実際に腕を伸ばす**必要がある。拳・刀の速さも実際に振った速さ × 縮尺になる（2 倍の増幅で得ていた速さは無い。威力は振りの速さと体の質量から物理で出る）。体より遠い所へコントローラを出せば、手は届く所で止まり目標の玉だけがコントローラに付いていく（届かないのは正しい、CLAUDE.md §1.1）。体が押されて後ろへ下がると、手の目標は部屋の中の点のままなので腕が前へ伸びる。
  - 頭だけ動かしてコントローラをその場に置くと、手は部屋の中の元の点に残る（人が歩けば手も付いてくるので実機では起きない）。
  - 記録（`.vrmotion`）の見出しの `motionGain` は、一人称で撮れば 1。
- **自分の体は半透明（2026-11-24）**: `BoxingGame.PlayerBodyOpacity` = `BoxingRenderer.FirstPersonBodyOpacity`（0.35）。腕・胴越しに相手と刀が見える。手（グローブ・素手）・握った武器・切り落とされた部位は不透明。半透明の描き方はエンジンの `VoxelVolumePipeline`（深度だけ → 一番手前の面だけ混ぜる）。描画だけの設定で物理の版は変えない。
- 左スティックは変換の基準点をワールドで動かす（`StickWalkRatio` 0.8 × 歩く速さ）。基準点が体の頭から `MaxLead`（1.2）× 腕の長さより離れる向きへは動かさない（体がロープ・相手に止められてもカメラが置いて行かれない）。
- 自分の頭（頭の部位のボクセル。髪・顔・目はその格子の中）を目の描画から外す: `BoxingGame.HidePlayerHead`（描画だけの設定。物理・当たり・相手から見た姿・チェックサムは変わらない。切り落とされた頭の破片は描く）。
- 稽古で作り直す（型の変更・A+B+X+Y）と `ResetPlacement` で変換を作り直した体の頭へ合わせ直す。
- 起動: `run_vr_training_katana.cmd`（①）・`run_vr_training_katana_fpv.cmd`（② `--first-person --two-hand both`）。引数は組み合わせられる。

### 6.4 確かめたこと（実機なし。raytrek）

- 単体テスト `VrKatanaControlTests`（写像: グリップ・LetGo の 1 歩・① の左手の位置 = 右手 − 軸 × 間隔・左のグリップを離すと左のコントローラ・一人称の変換で HMD が頭に重なる・歩く指令がずれに比例・スティックで基準点が動き頭打ち・目の行列・頭を描かない・フェイスボタン・蹴りの種類・各武器の稽古の型と間・オプション）と `VrFirstPersonPhysicsTests`（物理: 止めた HMD で 8 秒、骨盤・頭が 2 cm 前後しか動かない（1.7 cm、腕の長さ 18.8 cm）／HMD を人の 0.6 m 前へ動かすと頭が約 4 cm（デッドゾーン 2.8 cm）まで近づいて止まる／グリップを押さなくても始めの刀は落とさない／左を離すと左だけ放す／右を離すと刀ごと放す）。
- `miniring-vr --first-person-selftest`（HMD なし）: `.visualcheck/vr_first_person.png`（一人称の目の視点: 相手が正面に見え、自分の頭は映らない）、`vr_first_person_behind_hidden.png` / `_visible.png`（後ろ斜め上から: 隠す設定で頭・髪が消え、首の切り口が見える）。目の視点では頭の中にカメラがあるので頭の有無で見た目が変わらない（比べは後ろからの 2 枚で）。`vr_first_person_down.png` / `_down_opaque.png`（目だけ 34° 下を見る: 半透明の右腕越しに刀の柄が見える / 不透明では隠れる。水平の視点では自分の体は視野の外）。
- 一人称の手（2026-11-24）: `VrFirstPersonHandTests`（手の目標のワールドの点とコントローラを写した点の差が 0（1e-5 m 以内）: 素手・刀、構え・ジャブ・フック、表示の空間でも同じ／コントローラを 40 cm 動かすと目標は 40 cm × 縮尺だけ動く（増幅なし）／武器の軸 = 見えているコントローラの前／一人称でなければ増幅 2 のまま）と `VrFirstPersonPhysicsTests.TheDrawnHandTargetSitsOnTheStillControllerAndTheHandComesToIt`（止めたコントローラに、描いている目標の玉が 0.2 cm、体の手が 1.3 cm まで来る）。

### 6.5 実機で試してほしいこと（確かめていない）

1. 一人称で、頭を動かしたとき景色が HMD に追従し（カメラは頭に固定されない）、体が遅れて付いてくるか。縮尺で世界が小さく（人形の目の高さ）見えるか。目の間隔の縮みで立体感が出るか（酔わないか）。
2. 左スティックの歩きで、カメラの原点が動いて体が付いてくるか。ロープ際・相手に当たったとき、カメラが体から離れすぎないか。
3. ① 両グリップを押して右手だけで振ったとき、左手が柄頭の側に付いて自然に見えるか（右の向き = 武器の向き。ひねりの向きが逆なら `WeaponOrientation`）。グリップを離したときの放す・拾う・添える（B の実装）の手触り。
4. A 単独で合わせ、4 つ同時で作り直し、X・Y の蹴り（前蹴り・膝蹴りの切り替わり）が押し間違いなく出るか。Quest Touch の X・Y・A・B の割り当て（Index は A・B）。
5. 稽古を終えたとき（Ctrl+C）に今の記録が保存されるか。`motions/` に型ごとのファイルができているか。
6. 一人称の「頭の位置のずれ」の許容（頭の目標と体の頭の差）が大きすぎて酔わないか。デッドゾーン・傾けの上限・基準点の頭打ち（`VrPlayerMapper` の定数）の調整。
7. （2026-11-24）一人称で、手の目標の玉（水色・握ると黄色）が実際の手（コントローラ）の所に見えるか。ゆっくり動かすと玉は手に付いてくるか、体の手（グローブ・素手）が遅れて玉を追うか。速く振ると玉と体の手の差が開くのは体の側（バネで追う）。思った所へ振れるか。半透明の自分の腕・胴が邪魔でないか・薄すぎて自分の体の向きが分からなくならないか（`BoxingRenderer.FirstPersonBodyOpacity`）。

## 実機で試してほしいこと（確かめていない、§1〜§3 の分）

1. `miniring-vr --weapon staff --record-motion` で起動し、A/X で身長を合わせてから、棒を両手で持つつもりでトリガーを引き、突く・払う。終了（Ctrl+C）後、`motions/*_staff.vrmotion` ができているか。`[vr] motion saved: ...` がコンソールに出る。
2. B/Y を押すたびに `[vr] motion recording started` / `motion saved` が出て、別のファイルになるか（Quest Touch は左 Y・右 B、Index は B、simple controller には無い）。
3. トリガーが引けているか（Quest のトリガー = 人差し指）。`select/click` に割り当たる simple controller ではグリップと同じボタンになる。
4. コントローラの向きが、武器の向きとして自然か（前を向けると武器が前を向くか、ひねりの向きが逆でないか）。逆なら `VrPlayerMapper.WeaponOrientation` の軸を直す。体の側の手首のバネが追う。
5. 記録したファイルを `miniring-arena imitate --vrmotion <file> --weapon staff --out x.mp4` で動画にし、動きがなぞれているか。
6. 既存の素手の VR（`--weapon` なし）が変わっていないか（Orientation null・Slide false のはずで、単体テストでも確かめてある）。

## 残った問題

- `Slide` は体の側が未実装のため、このブランチでは棒は実際には滑らない（入力と記録だけ）。
- トリガーの割り当て・B/Y は OpenXR のバインディングの提案で、ランタイム側で別の割り当てを使っていると効かない（simple controller に B/Y は無い）。
- 手の向きの記録は grip 姿勢（`grip/pose`）。コントローラの持ち方による個人差の補正は無い。
- `--cpu` はスタイル名のみ（キャラクターの roster は未対応）。
