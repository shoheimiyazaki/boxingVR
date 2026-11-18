# 報告: バットの型（Sonnet）— 2026-11-18

## やったこと
- バットを新しい武器の種類 `WeaponKind.Bat`（木の棍棒と同じ形・材質を両手で握る）にした。片手のこんぼう（`sean`・`club.json`・`ClubBot`）は別の種類のまま触らず、既存のチェックサムのテストは全部通った。
- 型 14 本（`games/MiniRing/Motion/BatForms.cs`）を `FormSwordsman` の型の組み方に載せた（`SwordsmanBot`・狙い・守りは剣と同じ。`SwordsmanBot.cs`・刀・ロングソードの教科書は触っていない）。教科書 `Bots/weapons/bat.json`（重みなし）。
- `LogicVersion` 48（履歴の 1 行を `BoxerBot.cs` に追記）。
- Arena: `--red-weapon bat`（video）、`imitate --weapon bat`、`handlelab --pairs bat:jin`（`bat` は名簿に無い試験用: jin の体で持つ）、`--style-bat`、`aimlab`/`selfpath --forms yes` にバットを追加。

## 型の表
`docs/SWORD_MOTION.md` §10（間合い・狙いの測定つき）。右・左・胴・脚の水平 4、真上・右上・左上の振り下ろし 3、右下・左下のすくい上げ 2、胴・顔の突き 2、手打ち `snap`、グリップエンド `butt`、前蹴り `front-kick`。

## 測定
- 全テスト: 960 件成功・スキップ 5・失敗 0（新規 `BatFormsTests` 11 件。README の件数を 960 に）。
- aimlab: 頭 100%・胸 80%・腰 60%・上腕 80%・前腕 60%・腿 40%・すね 0%（`docs/arena/bat_forms_aim.md`）。技の集まり（間合い 0.33 以上）は 12 本、`butt`（31 cm）・`front-kick`（18 cm）は間合いの内側。
- selfpath: 目標の線が体を通るフレーム 0.0%（最初の `butt` は 30% だったので、棒の頭の端を右肩の上へ立てる形に直した）。
- handlelab（バット × 日本刀 jin・× こんぼう sean・× バット、4 種 × 12 試合、`docs/arena/bat_forms_handling.md`）: 自傷 0〜3 セル/試合、当たった振り 83〜89%、日本刀に -170 前後/試合で負け越し。
- 採否: 重みを変えた案 B は合計 -470（A）→ -500 で良くならず、使っていない種でも向きがそろわず不採用。教科書は重みなし。

## 見たコマ（raytrek で ffmpeg → jpg → こちらで開いた）
- `swing-right`: 棒を肩の上に立て、胴の高さで前へ水平に払い、構えへ戻る（カメラが回るので「右から」かまでは読み取れない。左右は `BatFormsTests` の数値）。
- `chop-right`: 右肩の上の斜めの振りかぶりから、体の前を斜め下へ落として水平近くまで振り抜く。
- `butt`: 棒を返して頭の端が右肩の上へ立ち、両手が前へ出る（握りの端が先）。
- 見ていない型: `swing-left`・`swing-mid`・`swing-low`・`chop`・`chop-left`・`scoop-*`・`poke-*`・`snap`・`front-kick`（数値テスト・aimlab・handlelab だけ）。動画 `docs/video/bat_forms_all.mp4`、コマ `docs/video/bat_forms_*_frames.jpg`。

## 残り・並行の作業への注意
- 技の選びが `swing-right`（一覧の先頭）に偏る（163 振り中 62）。同じ間合い（34 cm）の型から先頭が選ばれる作りで、`SwordsmanBot.cs` の側の話（触っていない）。
- `chop-right`・`chop-left` は imitate で棒の先が目標から最大 1.1 腕遅れる（自傷が 1 件出ることがある）。脚（腿・すね）はほぼ打てない。
- 間合いの内側の技（`butt`・`front-kick`）は型・間合い 0.33 の下限どおり用意した。「いまの距離で出せる技」の仕組みから使えるが、バットでは未確認。
- `docs/GUIDE.html` は更新していない。
- 近道は `CLAUDE.md` §8「バットの型」に書いた。
