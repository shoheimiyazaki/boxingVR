# 完了 — レフェリーの有無と倒れた相手への攻め（2026-11-16）

状態: 優先順 1・2・3 すべて入れた。全テスト 596 件成功（Probe 除く）。コミット・プッシュはしていない。

## 変えたファイル
- ルール: `BoxingGame.cs`（`MayStrike`・`Referee` プロパティ・`WatchRecoveries` はレフェリーありだけ・ボットを `GroundStriker` に包む・レフェリーなしだけ `MaxCrouch` を `GroundCrouch` へ）、`BoxingGame.Weapons.cs`（拳・はめる物・武器・蹴りの 5 か所の `State == Standing` を `MayStrike` へ。`RawArmedSense` が `MayStrikeDowned` を渡す）
- 情報: `FighterCommand.cs`（`FighterSense.OpponentDowned` / `MayStrikeDowned`）
- 再生: `Replay/ReplayMatchSettings.cs`（`Referee`、古い記録は既定 true）
- 設定: `FighterTuning.GroundCrouch`、`BotStyle`（`GroundDrive/Load/Cut/Stomp/Rest`）、`karate.json`（`GroundStomp` 0.6）、`BoxerBot.LogicVersion` 29
- 新規: `Bots/GroundStrike.cs`（`GroundMove`・`GroundStrike`・`GroundStriker`）
- プレイヤー: `Desktop/DesktopFighterControl.cs`（弱 = 振り下ろす系・強 = 突き下ろす系／空手は踏み下ろし。押して溜め、離して打つ）
- Arena: `--referee on|off`（`video`・`league`）、`BotArena.BoutSpec.Referee`
- 文書: `docs/BOXING.md`・`INTENT_CONTROL.md`・`BOTS.md`・`CLAUDE.md` §8・`README.md`（596 件）
- テスト: `GroundStrikeTests.cs`（新規）、`ReplayTests.cs`（2 本追加）

## 決まり
レフェリーなし: 数え・10 カウント K.O.・動かない K.O. は今のまま、倒れた相手への打撃も同じ `ApplyHit` で数え、立ち直り後の一呼吸なし。`PhysicsVersion` は上げていない（レフェリーありの物理・既存の再生テストは不変）。

## 技ごとの経路（目標の経路。姿勢は書かない）
共通: 倒れた体の軸（腰〜頭）の最寄りの点の横へ歩き、近づくほど頭を沈め（`HeadOffset`）、振りかぶりで体を持ち上げ打つ間に落とす（体のバネ）。打つ先は上から下へ。
- 鉄槌 `hammer` / 沈んで突き下ろし `drop-thrust`（素手・グローブ・メリケン・カタール・籠手）
- 踏み下ろし `stomp`（空手。足首の目標を相手の上から下へ）
- 日本刀・ロングソード: 突き下ろし `thrust-down`、斬り下ろし `cut`。こんぼう: `cut`

## テスト
- `dotnet test ... --filter "FullyQualifiedName!~Probe"`: 596 成功 / 0 失敗
- 新規: レフェリーなしで当たる・ありでは数えない・10 カウント K.O.・一呼吸の有無・boxer/champion/swarmer/karate が 10 秒以内に当てる・空手が踏む・刀/ロングソード/こんぼうが倒れた相手を傷つける（ありでは触れない）・プレイヤーの呼びかけ・再生往復・旧ヘッダ
- 注: ドリルの値は決定的だが、立つ位置・沈みは物理次第で、当たるまでの秒数は 1 秒前後（`firstHit`）。

## 動画（ワークツリー内 `media_tmp/`、git 管理外・未整理）
- 拳: `media_tmp/referee_off_fists.mp4`（champion vs swarmer、seed 4、0.75 倍速。動画の秒で）: 約 70 秒（HEART）・72〜74 秒・82〜83 秒（TEMPLE）に、倒れた赤へ青がかがんで打つ（ダメージが 169→257→295→319 と増える）。10 カウント K.O.。
- 武器: `media_tmp/referee_off_swords.mp4`（刀 vs ロングソード、seed 3）: 約 6〜10 秒・38〜58 秒に倒れた相手へ斬り・突き下ろし（ダメージ 40→63、FATAL・K.O.）。
- 自分でコマ（コンタクトシート）を見て確認した。ただし細部（打ち下ろしの形の良し悪し）は解像度が低く、踏み下ろし・鉄槌の見た目の評価はユーザーの目が必要。

## 確かめていない所・近道
- コードレビュー（別の目）は未実施。VR は未確認（何も足していない）。デスクトップの意図操作の実機操作は未確認（`GroundStrike` を呼ぶテストのみ）。
- 近道（CLAUDE.md §8 に記載）: `GroundCrouch`（レフェリーなしだけ沈み上限を拡大）、`GroundMove` の寸法は手で決めた値、「届かないとき 1 秒試したあと寄って打つ」。
- 立つ位置・沈みは試行錯誤の値で、深く沈んだ姿勢で腕の力が落ちる・足が倒れた体にさえぎられる、といった物理上の限界のため、拳の打撃は強くない（`wounded`/`damage` の値はドリルで 5〜40 程度）。
