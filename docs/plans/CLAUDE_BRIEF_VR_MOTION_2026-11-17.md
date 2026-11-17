# 指示書: VR で武器を使う・VR の動きを記録して手本にする（2026-11-17）

先に `CLAUDE.md`（§1・§2・§4）、`docs/ARCHITECTURE.md`（VR ホスト・座標系）、`docs/REPLAY.md`、`games/MiniRing/Motion/`（`SwordMotion`・`MotionImitation`・`BvhClip`）を読むこと。日本語でコメント・文書を書く。

## ユーザーの指定（そのまま）

- 棒術やその他においても、VR に対応させる。
- VR で人間の動きを手本として取り込む仕組みも作りたい。VR の動きを保存するようにする。
- （棒術の作業から）VR でトリガーを引いている間は棒を保持、引いていないときは棒が滑る。

## いまの仕組み（調べた所）

- `games/MiniRing.Vr/XrSession.cs` の `XrInputFrame` は頭の位置・向き、両手の位置（向き無し）、スティック、squeeze（グリップ）、A/X だけ。トリガーと手の向きを読んでいない。
- `VrPlayerControl` は手の位置 → `PuppetMapping` → `HandCommand(target, Grip)`。武器の向き（`HandCommand.Orientation`）を出さない。
- VR のホスト（`Program.cs`）は試合の記録（`ReplayRecorder`、入力の `FighterCommand`）を `replays/` に書く。プレイヤーの武器を選ぶ口は無い（`SelfTest` だけ刀）。
- `HandCommand.Slide`（握った棒を滑らせる）は追加済み。体の側の物理は別の作業（棒の握り）が作る。この作業では入力の側だけ。

## 決めたこと（設計）

1. **XR の入力を足す**: 両手の grip 姿勢の向き（`Quaternion`）、トリガーの値（oculus touch / index: `/input/trigger/value`、simple: `/input/select/click`）、B/Y ボタン（記録の開始・停止に使う）。ワールドへの変換は位置と同じ `WorldFromXr`。
2. **VR で武器を持つ**: VR ホストに `--weapon <kind>`（と必要なら `--cpu-weapon`、`--cpu <キャラ id>`）を足し、`BoxingGame` の既存の設定（`PlayerWeapon` など）で試合を作る。棒・刀・ロングソードは両手持ち（既存の `Support` の作り方）。
   - トリガーを引いている手は握る、離している手は `Slide = true`（ヒステリシスはグリップと同じ流儀）。握れる武器を持っていない手では `Slide` は意味が無い（体の側が無視する）。
   - 武器の向き: 握っている手のコントローラの向きを、体フレームの `HandCommand.Orientation` にする（武器のローカル X = コントローラの前、grip 姿勢の −Z）。両手持ちのときもそれぞれの手に入れてよい（どちらを使うかは体の側が決める）。
   - squeeze（グリップ）は今まで通り掴む（クリンチ）。
   - 既存の素手の VR の挙動は変えない（`--weapon` 無しなら今まで通り）。
3. **VR の動きの記録**（手本として取り込むため）: 新しい形式 `.vrmotion`（`games/MiniRing/Motion/VrMotionClip.cs` などゲーム本体の側に、ホスト非依存で）。
   - 1 フレーム = 時刻、頭の位置・向き、両手の位置・向き・トラッキング有無、トリガー・グリップの値、スティック。加えて `VrPlayerControl` が出した写像後の値（体フレームの両手の目標・向き・`Slide`・頭のずれ・移動）。
   - ヘッダ: 形式の版、身長のキャリブレーション（`PlayerHeight`）、写像の定数（`MotionGain`）、プレイヤーの腕の長さの見積もり、選んだ武器、メモ（名前）。
   - 長さは人の体の寸法のまま（m）と、腕の長さで割った正規化の値の両方が取り出せること（人形の大きさに依らず当てはめるため。`StaffForms` も腕の長さ = 1）。
   - 書き方: B/Y で開始・停止（1 回の稽古ごとに 1 ファイル）、または `--record-motion` で起動から終了まで。保存先 `motions/<日時>_<武器>.vrmotion`（`replays/` と同じ流儀。git の外）。文字の JSON でも binary でもよいが、読み書きのテストを書く。
4. **手本として使う**: `VrMotionImitation : IFighterControl`（`MotionImitation` と同じ考え方）— 記録した正規化の両手の目標・向き・`Slide`・頭のずれ・移動を、その人形の腕の長さで当てはめて `FighterCommand` を出す。体はバネで追う（§1.1）。Arena に `imitate --vrmotion <file> [--weapon staff] [--slow N] --out x.mp4` を足し、記録した動きを人形がなぞる動画が撮れること。
   - 試合の計算に時計を入れない（§1.6）。再生は記録のフレームの時刻だけで進む。
   - 将来: 記録から技の経路（`StaffForms` のキー）を取り出す・ボットの教育に使う。今回は「取り込んで、なぞれる」まで。文書に次の段として書く。
5. デスクトップのホスト・ボットの判断は変えない。

## 完了の条件（優先順）

1. テスト（`tests/MiniRing.Tests`、VR の実機なしで）:
   - `.vrmotion` の書き出し → 読み込みで値が一致する。
   - 作った `XrInputFrame` の列を `VrPlayerControl`（テストから使えるようにするか、写像の部分をゲーム本体の純関数へ移す）に通すと、トリガーを離した手が `Slide = true`、握った手の `Orientation` がコントローラの向きの写像になる。
   - `VrMotionImitation` が、記録の写像後の値と同じ目標（腕の長さの比で縮めたもの）を出す。腕の長さの違う人形で比が保たれる。
2. `games/MiniRing.Vr` がビルドでき、`SelfTest`（あれば VR なしで動く部分）が通る。VR の実機の確認はできないので、ユーザーに試してもらう手順（起動の引数・ボタン）を `docs/ARCHITECTURE.md` の VR の節か新しい `docs/VR_MOTION.md` に書く。
3. 作ったテスト用の `.vrmotion`（合成の動き: たとえば両手で棒を前へ突く）で `imitate --vrmotion` の動画を撮り、コマを PNG に抜いて自分で見る。
4. 全テスト `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName!~Probe"` が通る。README のテスト件数を合わせる。

## 注意

- 別の作業が並行で `Fighter.cs`（腕・武器と脚）・`Footwork`・`KickSpec`・`Weapon.cs` を触る。そこは触らない（`HandCommand.Slide` を使うだけ）。`ReplayFile` も棒の作業が版を上げるので触らない。
- コミットは自分のブランチ（ワークツリー）に、`feat: ...` の形式で。プッシュしない。
- 最後の報告: 変えたファイル・形式の中身・テスト結果（件数）・動画のパスと見た内容・VR の実機で確かめてほしいこと・残った問題。確かめていない数字は書かない。
