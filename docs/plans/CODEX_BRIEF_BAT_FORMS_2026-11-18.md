# 指示書: バット（木の棍棒を両手で）の技を増やす — 2026-11-18

作業者へ: 先に `CLAUDE.md`（§1・§4・§8）、`docs/plans/CODEX_REPORT_SWORD_TECHNIQUES_2026-11-18.md`（直前の作業: 刀・ロングソードの技を増やし、柄頭打ち・前蹴り・狙いの一般化を入れた）、`games/MiniRing/Bots/ClubBot.cs`・`games/MiniRing/Weapons/WeaponSwing.cs`・`FormSwordsman.cs`・`Motion/StaffForms.cs` を読むこと。
ブランチ: `claude/sword-combat-techniques-51f876` の上で作業し、終わったらコミット（プッシュしない。`feat: ...`、付記なし）。

## ユーザーの要望
バットの技を多くする。振る向き（右から・左から・上から・下から・斜め）、狙う所（手・顔・肩・胴・脚）、突き（先端で胴・顔）、間合いの内側の技（グリップエンドで殴る・前蹴りで離す）。

## 今の状態
- 木の棍棒（`WeaponKind.WoodenClub`）は `ClubBot` が片手で 3 つの振り（`WeaponSwing` の Chop・Side・Thrust）だけで戦う（名簿では `sean`、教科書 `weapons/club.json`）。
- 両手のバットの振り（`SwingFamily.Bat`）は 1 つ（右肩から横へ）だけで、ドリル（guardlab の確かめの組）にしか使っていない。
- 刀・ロングソード・棒は `FormSwordsman`（`SwordKey` の型 → `SwordsmanBot`。狙い `MotionAim`・守り `WeaponDefense`・技の時計 `SwingPacing`）で戦う。

## 決めごと（Claude が決めた）
1. **バットは `FormSwordsman` の型で動かす**（§1.5: 一つの組み方）。`Motion/BatForms.cs` を作り、`SwordKey` のキーで型を書く（両手持ち、`CanTwoHand` は既にバットを含む）。新しい教科書 `Bots/weapons/bat.json`。片手の `ClubBot` とこんぼうの教科書 `club.json`・`sean` の試合は変えない（チェックサムを保つ）。
2. 型（最低限）: 構え（右肩の上で立てる・中段に前へ向ける）、右からの水平振り（頭・胴・脚の高さは狙いでずらす）、左からの返しの水平振り（バックハンド）、真上からの振り下ろし、右上・左上からの斜めの振り下ろし、下からのすくい上げ（あご・手へ）、先端での突き（胴・顔）、グリップエンドで殴る（柄頭打ちと同じ「後ろの端」の扱い。直前の作業の一般化を使う）、前蹴り（直前の作業の剣と同じ作り）。小さく速い手打ち（相手の手・前腕を狙う短い振り）も 1 つ。
3. 選び方・狙い・守りは剣と同じ物を使い、バットのための分岐を書かない（§1.3）。棍棒は刃が無い（`IsBlunt`）ので、当たりの違いは材料から出る。
4. 名簿にバットのキャラを足すかは決めない（足さない）。Arena から `--weapon bat` 相当で試せるようにする（既存の引数の作りにそろえる）。

## 確かめ方（完了の条件）
- 型ごとの数値のテスト（振る向き・始めと終わりが構え・グリップエンドが前へ出る）と、`selfpath`・`selfcuts`（バットの自傷）、`aimlab`（狙いごとの当たる割合）、`handlelab`（バット × 刀・バット × こんぼう（片手）・バット × バット、使っていない種、各 4 試合以上）。
- 型の動画 `docs/video/bat_forms_*.mp4`（音を残して縮める）を撮り、コマを抜いて向きが名前どおりに見えるか自分で見て報告に書く。
- 全テスト `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName!~Probe"`。README の件数。
- 判断が変わる所があれば `BoxerBot.LogicVersion` を次の番号へ（直前の作業の番号の次）。既存の試合（こんぼう・刀・ロングソード）が変わらないなら、その旨をテストで確かめる。
- 文書: `docs/SWORD_MOTION.md`（またはバットの節を新設）、`docs/BOT_PDCA.md`、`docs/arena/bat_forms_*.md`、§8 に近道。

## 報告
`docs/plans/CODEX_REPORT_BAT_FORMS_2026-11-18.md`: 技の表・測った数字・採否・動画のコマの所見・残り。最後にコミット。
