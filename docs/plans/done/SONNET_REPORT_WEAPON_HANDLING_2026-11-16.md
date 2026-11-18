完了

# 報告: ロングソード・日本刀・こんぼうの扱いを鍛える（教科書の側）（2026-11-16、ブランチ `claude/weapons`）

指示書: `docs/plans/done/CLAUDE_BRIEF_WEAPON_HANDLING_2026-11-16.md`。体の物理・材料は変えていない（`PhysicsVersion` 33 のまま）。`BoxerBot.LogicVersion` 30 → 31（履歴に 1 行）。リプレイ形式 8 → 9。プッシュなし。

## 1. 教育できる形

- 剣士（刀・ロングソード）とこんぼうの判断の値を `WeaponStyle`（`games/MiniRing/Bots/WeaponStyle.cs`）へまとめた。`ToJson`/`FromJson`、`Validated()`（範囲・知らない鍵を断る。`SwordDefense.Validated()` を足した）。
  武器ごとの教科書は組み込みの `Bots/weapons/katana.json`・`longsword.json`・`club.json`（`WeaponCatalog.Textbook`）。
- `CharacterEducation.WeaponBot` を本物の上書きにした（`WeaponEducation.Resolve`: 教科書の JSON → `education.weaponBot` を鍵ごとに重ね、`Validated`。`Name` は変えられない・最上位の鍵が教科書に無い・範囲外・武器を握らない（拳・鉄の剣）のに空でない、は読み込みで止める）。
  解決した値は `FighterCharacter.WeaponStyle` → `BoxingCorners.PlayerWeaponStyle`/`CpuWeaponStyle`（`MatchSelection`・Arena の `VideoMaker.WithCharacters`）→ `FormSwordsman.Bot(.., style)`・`ClubBot.FromStyle` と、リプレイの見出し（版 9 の `PlayerWeaponStyleJson`/`CpuWeaponStyleJson`）へ。教育が空なら教科書そのもの。
- JSON の例（キャラのファイル）:

```jsonc
"education": {
  "style": {},
  "vs": {},
  "weaponBot": { "Rest": 0.4, "Defense": { "BlockShare": 0.5 }, "TechniqueWeights": { "unterhau": 0.3 } }   // 書いた鍵だけが武器の教科書を上書き
}
```

`WeaponStyle` の鍵: `Defense`（`SwordDefense` の全部）・`HitAndAway`（null = 遠間で構えて 1 つずつ詰める）・`Aim`・`AutoAim`・`Clearance`・`Pacing`・`Rest`・`MinReach`・`TechniqueWeights`（技の名前 → 重み、null = 同じ割合）・`KeepReach`・`ClubRange`・`ClubRest`・`ClubStandOff`・`ClubExact`・`ClubSwingWeights`（Chop/Side/Thrust → 重み）。
重みを足した `SwordsmanBot.Weights`・`ClubBot` の `weights` は null のとき乱数の使い方も同じ（ビット単位）。

- 値を移しただけの段階のチェックサム: 移す前（`CharacterSamenessProbe` で取った）の値と一致（E0 の 8 組 + 武器 6 組）。コミット `20fac15`。鍛えたあとも、前の値を `CharacterSamenessCases.LegacyWeaponStyle` として残し、その値で同じチェックサムになることを毎回テストする。
- 新しいテスト: `WeaponStyleTests`（11 件: 教科書の値・JSON の往復・知らない鍵と範囲外・教育の重なりと検査・重みの検査と効き・名簿ファイルの教育の検査・教育が試合・記録 → 再生に通ること）、`CharacterSamenessTests` に鍛えた教科書の期待値（剣の組）。

## 2. 物差し（`handlelab`）と基準

`games/MiniRing.Arena/HandleLab.cs`（`handlelab`）。ゲームと同じ道で試合を回し、振り（`SwordsmanBot.Current`・`ClubBot.Current` が出ている間 + 0.15 秒）ごとに: 当たった割合・当たる直前の刃筋のずれ（刃の動く向きが刃の面内から外れた角）・峰が先（刀）・当たった刃の位置・刃先の速さ・1 振りで欠けたセル・切断/折れ・間合いのずれ、守り（受け・外し・相手の振りのうち当たった割合）、自傷（技ごと・部位ごと）、勝敗・得点・セル差。
`--style-katana/--style-longsword/--style-club` で候補の `WeaponStyle` を渡せる。`handlespar`（`HandleSpar.cs`）は練習試合で値を育てる（使っていない種・相手で比べ直す）。
`guardlab`・`selfcuts` は古い技の一覧（`sword_library.json`）の剣士を測る道なので、試合と同じ道の `handlelab` の守り・自傷の欄で代えた（回していない。ここは指示書からの逸脱）。`rosterleague` は 5 人（jin・konrad・sean・marcus・akira）で前後に回した。

基準（`docs/arena/weapon_handling_baseline.md`、種 1・9 組 × 6 試合 × 40 秒）の弱い所:
1. 自分の武器で自分を削る: ロングソード 1 試合 321 セル（unterhau は 1 振り 32 セル）、刀 75（nuki-do 12）、こんぼうの Chop は自分の頭を 173 回打つ。konrad は総当たりで勝率 19 %（最下位）。
2. 刃筋のずれ平均 23〜35°（刀の当たりの 3〜6 割は峰が先）。
3. こんぼうの Side は当たる割合 65 %・欠けるセル 22（Chop 37）。

## 3. 試したことと採否

詳しい表は `docs/arena/weapon_handling_train.md`、仮説 → 結果 → 採否は `docs/BOT_PDCA.md` に書いた。選別は種 31・32（konrad は sean・marcus・jin、各 4 試合 × 30 秒）、比べ直しは**使っていない種 71 と使っていない相手（dmitri・albert・vikram・gaston）を足した各 28 試合**。

| | 使っていない種・相手の 28 試合（前 → 後） |
|---|---|
| 刀 + `Pacing`（採る） | 自傷 58 → 6 セル/試合、セル差 +1088 → +1575、勝-分-負 4-18-6 → 11-16-1、得点 66/52 → 48/33 |
| ロングソード + `Pacing`（採る） | 自傷 330 → 140、セル差 −312 → −41、勝-分-負 2-22-4 → 4-22-2、得点 65/68 → 57/81（下がる） |
| こんぼう 振り始める間合い 0.36 → 0.30 m（採らない） | 当たる割合 64 → 74 %、得点 46/78 → 56/79、セル差 +230 → +97、勝-分-負 4-20-4 → 3-25-0（他の 2 組の測定を合わせてセル差・勝ち越しは ±0） |

- 採った: 刀・ロングソードの `Pacing`（既存の `SwingPacing` の既定値。技の時計を体が目標に付いてきた分だけ進める）。こんぼうは採れるものが無く前のまま。
- 採らなかった: `SelfClearance`（刀では一度も働かず、ロングソードでは自傷が 20.5 → 27.2 セル/振りに増えた）、unterhau を出さない（自傷は減るが当たる割合 74 → 64 %・得点 54 → 41）、`HitAndAway`（選別だけ。刀は得点が悪化）、こんぼうの値、`handlespar` の探索（ロングソード 5 世代 × 6 候補: 使っていない種 +1.20・使っていない相手 −1.49 で割れ、採らない。1 候補 6 試合では揺らぎが値の違いより大きかった）。

前後（`docs/arena/weapon_handling_after.md`、種 1・54 試合。前は前の値を明示して再測定し、最初の測定とすべて一致した）:

| 武器 | | 振り | 当たった | 自傷セル/試合 | セル差/試合 | 勝-分-負 | 与/受の得点 |
|---|---|---:|---:|---:|---:|---|---|
| 刀 | 前 → 後 | 657 → 378 | 85 → 81 % | 75 → 35 | +704 → +755 | 12-12-6 → 15-12-3 | 71/54 → 42/40 |
| ロングソード | 前 → 後 | 333 → 238 | 75 → 84 % | 321 → 137 | −377 → −323 | 7-18-5 → 8-14-8 | 77/65 → 69/65 |
| こんぼう（値は同じ） | 前 → 後 | 604 → 591 | 72 → 74 % | 19 → 21 | +73 → +57 | 6-15-9 → 6-11-13 | 73/78 → 72/68 |

総当たり（`rosterleague`、5 人・各組 4 試合）: konrad 19 → 38 %、jin 75 → 69 %、akira 53 → 66 %、sean 69 → 50 %、marcus 34 → 28 %（16 試合で ±10 %。sean の落ちは jin・konrad が強くなった映り込み）。
副次: 刃筋のずれはロングソード 32〜35° → 24〜25°。刀の当たった刃先の速さは 2.5〜2.7 → 1.7〜2.4 m/s に下がり（遅くなる）、切断・折れは 100 振りあたり 3〜8 → 12〜23 に増えた。

## 4. 動画（`media_tmp/`、git に入れていない。`video --seed 3 --seconds 40 --speed 1`、約 48.6 秒）

- `before_katana_vs_longsword.mp4`・`after_katana_vs_longsword.mp4`（jin 対 konrad）、`before_club_vs_katana.mp4`・`after_club_vs_katana.mp4`（sean 対 jin）。前 = LogicVersion 30（教科書の追加前に撮った）、後 = 今。
- 見たこと（コマを取り出して見た: `media_tmp/sheet_*.png`・`strip_*.png`）: 刀対ロングソードは、前は konrad が序盤にダウンして引き分け（10 カウントの後に立つ）、後は konrad が jin のこめかみを打って K.O.（40 秒の残り 1 秒、konrad の勝ち）と、試合の流れが違う。
  こんぼう対刀は、後は sean が序盤にこんぼうを落として跪き、jin が立って剣を振る（打撃の音は 3 回。終わり際にダウンのカウントが入り、結果は引き分け）。前は 40 秒打ち合って引き分け（打撃の音は 48 回）。
- **「良くなった振りが見える秒」は、動画からは言い切れない**: 1 本ずつの試合で、`Pacing` の効き（自傷の減少・刃筋）を目で選んで示せる場面は取れなかった（前の動画の 33.7〜34.4 秒の刀の低い斬り上げは、体の脇を通るが自分の体への食い込みまでは確認できなかった）。数字（上）のほうが確かで、動画は参考。

## 5. テスト

`dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`: **679 件 すべて合格**（README を 679 件に直した）。`GroundStrikeTests.NoReferee_WeaponBotWoundsADownedOpponent` は、刃が止まる（種 1 の刀: `started=1` のまま）ことが前の教科書でも種 3 で起きたので、3 つの種のうち 2 つ以上で見る形に直した（Pacing の副作用として、倒れた相手への打ち下ろしの平均の傷は種 1〜6 で刀 55 → 39・ロングソード 10 → 5 と下がる）。

## 6. 文書

`docs/CHARACTERS.md`（§5 の `weaponBot`）・`docs/WEAPON_DEFENSE.md`（§7）・`docs/VOXEL_WEAPONS.md`（§5.6）・`docs/BOT_PDCA.md`・`docs/plans/CHARACTER_EDUCATION_PLAN.md`・`CLAUDE.md` §8（技の時計が既定になったことの 1 文）・`README.md`（件数）。記録は `docs/arena/weapon_handling_{baseline,train,after}.md`・`_baseline_roster.md`・`_after_roster.md`・`_spar_longsword.md`。

## 7. 確かめていないところ・注意

- 選別・比べ直しとも 30 秒の試合で K.O. が少なく、勝敗は 12〜28 試合で ±3 揺らぐ。採否は自傷のセル・セル差（物理で測れる量）を主に、勝敗は参考にした。ロングソードの勝ち越しは「変わらない」で、得点は下がる（採った代わりに失うもの）。
- `Pacing` の効きを、刀とロングソードで同じ値のまま採った。値そのものの調整（`GripSlack` など）は試していない。
- `guardlab`・`selfcuts`（古い技の一覧の道）・`TechniqueWeights` の本格的な探索は回していない。`HitAndAway` は使っていない種・相手では確かめていない。
- 武器の種類で判断を分けている所: `WeaponStyle` は 3 つの武器とも同じ形の値で、武器ごとの違いは教科書 JSON の値だけ。ただし `ClubRange`・`ClubStandOff`・`MinReach` は m の定数で、武器の長さ・重さ・握りから連続には出していない（技の間合いは型の動きから `StrikeDrill.ReachOf` で出ているが、こんぼうの間合いは手で決めた値）。
- 動画の前は教科書を足す前のビルドで撮った（再撮影はしていない）。
