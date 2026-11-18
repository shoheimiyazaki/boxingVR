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

| 操作 | 何が起きるか |
|---|---|
| 右スティック 左・右 | 前・次の型（刀は `FormSwordsman.Forms(Katana)` の 20 型の順: shomen・kesa・…）。変えるたびにワールドを作り直し、その型の間で向き合う。記録中なら保存して止める。右手に短い振動 |
| 右スティック 下 | 同じ型のまま作り直す（相手を斬り崩したあと・立ち位置がずれたとき）。両手に短い振動 |
| B / Y | 記録の開始（右手に強め）・停止（両手に長め）。1 回 = 1 ファイル `motions/<日時>_katana_<型>.vrmotion`、注に `training <型> gap <間> m (seigan target)` |
| A / X・トリガー・左スティック | 今まで通り（身長・握る・歩く） |

- **始めの間**（`SwordTraining.Forms`）: その型の動き（踏み込みを含む）で物打ちが相手の体の面にちょうど届く距離（`StrikeDrill.ReachOf`）。試合の技の下限（0.33 m）では外さず、全部の型を並べる（柄頭打ち・押し斬りも）。下限 0.2 m・上限はリングの縁から 20 cm 内。コンソールに型の名前と間（ゲームの寸法と人の大きさ）を出す。
- **相手**（`GuardHold`）: 武器の型の構え（刀は中段 = `FormSwordsman.Guard`）を相手へ向いてとり続ける。歩かない・打たない・受けない。体はバネで構えを追うので、打たれれば持っていかれ、構えへ戻る。
- **ワールド**: 武器のドリルのリング（`RingLayout.DrillWeapons`、2.4 m）の中央で向き合う。審判・休みの動作・ラウンドは無し。試合の記録（replays）は取らない。型を変える・作り直すと `BoxingGame` を作り直す（§1.1: 位置は書き込まない）。身長の合わせ（写像）は作り直しても残る。
- コード: `games/MiniRing/Motion/SwordTraining.cs`（型の並びと間・試合の設定・`GuardHold`・`StickFlick`）、`games/MiniRing.Vr/VrTrainingSession.cs`（スティック・記録・振動）、`VrMotionSession` に記録の名前と注・`Stop`。テスト `SwordTrainingTests`。
- **確かめていないこと（実機が無い）**: ヘッドセットで、型を変えたときに立ち位置が正しく変わるか・振動が届くか・相手が正眼に見えるか。ヘッドセットの中では型の名前が見えない（コンソールだけ）ので、型の順と振動で数える。見える札（ワールドの中の文字）は未実装。

## 実機で試してほしいこと（確かめていない）

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
