# 指示書: master とのマージ（PhysicsVersion 53・LogicVersion 55）の後の調整し直し（2026-11-19）

先に [SONNET_BRIEF_SLIDE_RETUNE_2026-11-19.md](SONNET_BRIEF_SLIDE_RETUNE_2026-11-19.md) を読む。手順・決まり・リモートの使い方は同じ。

## 背景

- この枝（滑りの直し・握る・拾う・関節のつなぎ、49 / LogicVersion 52）に master（呼吸と循環のスタミナ・素手の拳・投げ技の「床から支える体」・体重を軸足へ移す蹴りの差分、52 / 54）を合わせた。コミットは 88f13c1。
- 衝突の扱い:
  - 蹴りの差分 `Bots/kicks/*.tune.json`: この枝の値（49 で練習した物）を仮に採った。master の値は `git show master:<path>` で読める。
  - SlideGripTests・ShieldTests・チェックサム: master の側を採った。この枝の `OnSettled` は消えた。

## tower32 の全テストで落ちた 17 件（1225 件中。trx は tower32 の `C:\Users\Miyazaki\repos\full53.trx`、ログは `full53.log`）

| 分類 | テスト |
|---|---|
| 蹴り | KickShin の後ろの足の回し蹴り・前の足の回し蹴り・前蹴り、KickContact の `TurningKicks_TurnTheBody_AndLandWithTheFootPartTheirPathPoints` |
| チェックサム | `AdoptedWeaponTextbooks_…`・`EmptyEducation_…`・`GamePlanEducation_…`・`KatanaMatch_KeepsItsChecksum_…`・`WeaponTextbooksInJson_WithTheOldValues_…` |
| 肘 | `AFasterElbow_CarriesMoreEnergy`・`EveryElbow_LandsAsAHit_ThroughTheFistImpactPath` |
| 拳 | `Punch_CounterDamage_IsPositiveWithCountersAndZeroWithout`・`Punches_AreAimedWhenThrown_SoAnEarlySlipBeatsThem` |
| ボット | `Infighter_DoesNotFightCrouchedTheWholeTime` |
| 棒・トンファー | `ScriptedStraightPunch_LandsWithTheFrontOfTheStick_NotTheBackOfTheFist`・`TheForwardForms_HitWithTheFrontEnd_AndGaeshiAlsoWithTheRear` |
| 滑り | `KatanaLookingAwayFromTheOpponentDoesNotSlide`（SlideTests。滑りの直しと「床から支える体」の重なりを最優先で見る） |

## やること

1. **滑り**: `KatanaLookingAway…` がなぜ落ちるかを調べる。滑りの直し（`Footwork` の置いた所の記憶）と master の `SupportFromGround`・`LegHeld` の重なりを見て、何も入力しない体が流れないように直す。物理を変えたら `PhysicsVersion` は 53 のまま上書きでよい（まだ外に出ていない番号）。直したら VERSIONS.md の 53 の行に書き足す。
2. **蹴り**: まず今の差分と master の差分を 53 の物理で比べる（`kickshin`、調整 6 条件と使っていない 6 条件）。良い方を出発点にして、要れば練習し直す。採否・表・文書は前の指示書と同じにする。LogicVersion は 55 のまま上書きでよい。
3. **そのほか**: 1 件ずつ、どちらの枝の変更（滑りの直しか、master の床から支える体・呼吸か）で変わったかを確かめる。物理として正しい結果なら期待値を直して注釈を書き、較正なら測り直す。説明の無い緩めはしない。
4. **チェックサム**: 最後に取り直す（1〜3 で変わるので）。
5. **確かめ**:
   - 蹴りの動画のコマを自分で見る。
   - 全テストを通す。
   - README の件数を合わせる。
   - 枝にコミットする。プッシュはしない。

## リモート

- **raytrek**: 別の作業のテストが走っていることがある（`remote-test.sh` が `test.log` の取り合いで失敗する）。空いていなければ tower32 を使う。
- **tower32**:
  - repo は `C:\Users\Miyazaki\repos\myvrgame1`（今は 88f13c1 に detach）。
  - 全テストは約 70 分かかる。長い実行はスケジュールタスクで起動し、終わったらタスクと作業ファイルを消す。tower32 には眠りを切った設定が残っている。
