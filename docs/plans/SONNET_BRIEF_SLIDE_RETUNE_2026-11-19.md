# 指示書: 滑りの直し（PhysicsVersion 49）の後の調整し直し（2026-11-19）

## 背景

- `Footwork.Update` が着いている足の「置いた所」を毎歩実際の足へ合わせ直していたので、足首で留める力が 0 になり、何も入力しなくても体が 30 秒で 40〜80 cm 流れていた。
- 直し（`FootworkTuning.HoldRelax` 1 s・`HoldSettleSeconds` 1 s、tests `SlideTests`）を入れて `Fighter.PhysicsVersion` = 49 にした（docs/VERSIONS.md の 49）。
- 直す前は、止まっている間に 2 人が約 11 cm ずつ離れていった。そのため、いくつかの較正は「流れる立ち方」の上で合わされていた。
  - 例: 蹴りの稽古は 1.112 の間合いを頼んで、実際は胸と胸 1.70 で蹴っていた。直した後は 1.35 になり、すねの当たる点が 0.72〜0.80 から 0.35〜0.45 へずれた。

## 直す前に落ちたテスト（15 件、全部 slow）

1. **蹴り（5 件）**: CMA-ES の練習のやり直しが要る。
   - KickShinTests: `TheRearFootRoundhouses_AtEachHeight_LandOnTheirTarget_WithTheLowerShin`・`TheFrontFootRoundhouses_Land_WithoutFalling_AndTheLowOneUsesTheShin`・`TheHumanFrontKick_LandsOnTheBody_WithBothLegs_AndTheKickerStaysStanding`
   - KickContactTests: `TurningKicks_TurnTheBody_AndLandWithTheFootPartTheirPathPoints`
   - そのほか `Roundhouse_AtItsStandoff_LandsWithTheLowerShinOrAnkle_AcrossSeedsAndBothLegs`・`TheFrontFootKick_MovesTheHipAwayFromTheTarget_AndTheRearFootKick_MovesItCloser` も落ちる候補。
2. **試合のチェックサム（4 件）**: 取り直すだけ。
   - `AdoptedWeaponTextbooks_PlayTheRecordedMatches_AndDifferFromTheOldOnes`・`EmptyEducation_PlaysTheSameMatchesAsBeforeTheFilesWereSplit`・`GamePlanEducation_ChangesOnlyTheMatchesWithFistBots`・`KatanaMatch_KeepsItsChecksum_AndAllocatesLittlePerStep`
   - ほかにもチェックサムのテストが落ちれば同じ扱い。
3. **ふるまい（6 件）**: 1 つずつ原因を見る。
   - `ABatBot_SwingsAtAStandingOpponent_AndHitsWithTheWood`（バットの型の数）
   - `TheShield_TakesTheSwordsSwing_AndTheBodyBehindItLosesLessThanWithTheShieldDown`
   - 棒の持ち替え 3 件: `SwappingHands_TheStaffIsNotDropped_AndTheReleasedHandCanTakeHoldAgain`・`AHandSlidesToANewPlace_AndTakesHoldThere` ほか
   - `AlternatingRush_FullForTenSeconds_ThenFadesOverTheNextTen`（拳の速さ 2.49 対 下限 2.8）
   - `TheEndScenePutsWinnersAndLosersThroughTheEndTableForEndSeconds` が落ちればこれも

**まず今の落ちる一覧を測り直す**: 上は作業 C の時点の一覧で、48 のマージの後なので変わっているかもしれない。

## やること（優先順）

### 1. 今の落ちる一覧

- この枝の HEAD で raytrek の全テストを回す。
  - `bash tools/remote-test.sh all`（バックグラウンドで。約 10 分）
  - `--parallel` は使わない。

### 2. 蹴りの練習のやり直し

- 道具: Arena の `kickshin --practice yes`（docs/TAEKWONDO.md「回し蹴りのロー・ミドル・ハイ…」の節、docs/TOOLS.md）。差分のファイルは `games/MiniRing/Bots/kicks/*.tune.json`（後ろの足 `Rear` と前の足 `Front` は別）。
- **点の決まりは前と同じ**: 狙った所に、すねの 0.55〜0.9 の点で当たり、蹴った側が立ったまま。その中でダメージが主、運動量が従。前蹴りは胴に当たって立ったまま。
- 調整の条件と使っていない条件も前と同じ。
  - 調整: 受ける人 standard・joao・yoandry × 蹴る人 taeyun・standard
  - 比べ直し: 受ける人 akira・gaston・edgar × 蹴る人 2 体
- **採るかどうか**: 使っていない条件で、49 の今の差分より良いこと（◎ の数・ダメージ）を表にして確かめてから採る。
- 実行の場所:
  - 長いので raytrek で回す。手順は docs/DELEGATION.md と、下の「リモートの使い方」。
  - tower32（`ssh tower32`、64 GB、16 論理 CPU）も使える。後ろの足と前の足、または高さごとに分けて 2 台で並べてよい。
  - tower32 は眠ることがある。長い実行の前に、電源の設定（powercfg）で眠りを切るか、切れなかったら raytrek だけで回す。
- 記録:
  - 表を `docs/arena/kick_shin_lmh_v49_*.md` に残す。
  - docs/TAEKWONDO.md の結果の段落を新しい値で書き直す（古い値は「49 の前」として 1 行残す）。
  - docs/BOT_PDCA.md に仮説 → 結果 → 採否を書く。
- `.tune.json` はボットの判断なので、変えたら `BoxerBot.LogicVersion` を 1 上げる（skill `bump-version` の手順、docs/VERSIONS.md）。
- **テストの帯（すね 0.55〜0.9 など）は緩めない**。練習で戻らない条件があれば、その条件と測った値を報告する。

### 3. チェックサムの取り直し

- 落ちたチェックサムのテストの期待値を、49（と、2 で上げた LogicVersion）の値に取り直す。
- 各テストの注釈に「49: 滑りの直し」と書く。

### 4. ふるまいの 6 件

それぞれ、直す前の期待値が流れる立ち方の産物だったかを確かめる。例えば次の量を、直す前（48 = コミット `04748db` の後の HEAD の 1 つ前の親）と後で比べる。

- 足の位置
- 2 人の間
- 当たる時刻の拳の速さ
- 盾の角度

扱いは次のとおり。

- **物理として正しい結果のとき**（例: 着いた足が滑らないぶん拳が遅い）: 期待値を直し、注釈に理由と測った値を書く（CLAUDE.md §4.5）。ただし、数字を合わせるためだけに緩めない。
- **型・較正の値が古い立ち方に合わされていたとき**（バットの型・盾の構え・棒の持ち替えの間合い）: その値を測り直す。
- **直しの側の不具合のとき**（例: 持ち替えで歩いている扱いにならず足が留まる）: 原因と案を報告する。直しが軸（§1）に沿うなら直してよい。

### 5. 目で見る（CLAUDE.md §4.7）

- 練習した蹴りの動画を撮る。docs/TAEKWONDO.md と同じ並びでよく、ロー・ミドル・ハイ・前蹴り × 後ろの足と前の足。
- `py -3 tools/sheet.py` でコマを並べて**自分で見る**。すねで狙いに当たっているか、倒れていないかを確かめる。
- 動画は `media_tmp/` に置く（git の外）。

### 6. 仕上げ

- raytrek で全テストを通す（`bash tools/remote-test.sh all`）。
- README のテスト件数を合わせる。
- 変更をこの枝にコミットする。プッシュはしない。

## リモートの使い方

- `bash tools/remote-test.sh <all|fast|filter:...>` は、今の作業ツリーを raytrek へ送ってテストを回す。
- Arena のような任意のコマンドは、コミットしてから次の関数で送る。ローカルの HEAD を raytrek の clone `C:\Users\smiya\repos\<名前>` へ送って、そこで cmd.exe のコマンドを走らせる。
  ```bash
  rt() { clone="$1"; shift; remote="C:\\Users\\smiya\\repos\\$clone"; base=$(ssh raytrek "cd /d $remote && git rev-parse HEAD" | tr -d '\r'); head=$(git rev-parse HEAD); if [ "$base" != "$head" ]; then b="${TMP:-/tmp}/rt_$clone.bundle"; git bundle create -q "$b" "$(git merge-base $base $head 2>/dev/null || echo)..HEAD" 2>/dev/null || git bundle create -q "$b" HEAD; scp -q "$b" "raytrek:C:/Users/smiya/repos/rt_$clone.bundle"; ssh raytrek "cd /d $remote && git fetch -q C:\\Users\\smiya\\repos\\rt_$clone.bundle HEAD && git reset -q --hard FETCH_HEAD"; fi; ssh raytrek "cd /d $remote && $*"; }
  ```
  - clone `slide-fix` は既にある（中身は作業 C の枝。上の関数で今の HEAD に合わせられる）。
  - 新しい clone が要るときは、raytrek で既存の clone から `git clone` する。
  - ssh の出力は文字化けするので、`set DOTNET_CLI_UI_LANGUAGE=en` を付けてログへ書き、`scp` で取り戻す。
- tower32 の場合: ユーザー名は Miyazaki、repo は `C:\Users\Miyazaki\repos\myvrgame1`、GitHub の資格は無い。更新は bundle + scp + fetch で行う。
- 数時間かかるコマンドはバックグラウンドで走らせる。ポーリングでなく完了の通知で待つ。

## 報告

次を短く書く。

- 落ちていた一覧（1 の結果）
- 蹴りの前後の表（調整 / 使っていない、◎ の数・すねの点・ダメージ）と採否
- チェックサムで取り直した件数
- 6 件それぞれの原因と扱い
- 動画のコマで見たこと
- 全テストの件数
- 緩めたテストと、その理由
