# 指示書: 顔を部品から組み立てる（F1・F2）

計画は `docs/plans/FACE_PARTS_PLAN.md`。先に読むこと: `CLAUDE.md` §1・§4、`docs/CHARACTERS.md` §2・§2.5・§3、`games/MiniRing/Voxels/FaceTemplates.cs`・`FaceLook.cs`・`EyeLook.cs`、
手の層の段 1（`git show 181b431 --stat`、`git show f98fd33`: 1 つの剛体に複数の格子）と段 2（`5812cdc`: はめる物が手の格子を持つ）。

作業場所: ワークツリー `E:\repos\ClaudeProjects\myvrgame1-face`、枝 `claude/face-parts`（`claude/hand-layers` の ff63a89 の上）。master・他のワークツリーは触らない。

## 目標の絵

`docs/plans/face_refs/`:
- `base/face_concept.png`（お手本の全体像: 正面・横・上・3/4）、`base/base_{front,side,top}.png` と `base/base.txt`（20 × 28 × 20、2.5 mm、文字の格子と正面の深さ）。
- `parts/parts_*.png` と `parts/parts_a.txt`（目・眉・口）・`parts/parts_b.txt`（鼻・耳・あご・髪・ひげ）。各 6 種、文字の格子と留める点。

## F1: 部品の表と組み立て（格子に依らない）

1. 文字の格子 → ゲームのデータ: `games/MiniRing/Voxels/FaceParts/`（json）へ写す道具（`tools/` の node か C# の Arena コマンド）。出どころの txt から作り直せること。
2. 部品の形式: 正規化した顔の座標（頭の外形で −1..1）での 2D の型（正面）＋ 横の輪郭・深さ ＋ 留める点 ＋ 材質（皮膚・唇・目・髪…）。
3. 組み立て `FaceComposer`（名前は任せる）: 土台の頭 ＋ 部位ごとの部品の混ぜ（重み付きの和を閾値で切る。0/1 で元の部品、0.5 で中間）＋ 今の `FaceParts` の位置・寸法・傾きの値。出力は任意の解像度の格子（材質のセル）。
4. キャラのファイル: `roster/<id>.json` に任意の `"face"`（部位ごとの部品の重みと値）。無ければ名前のハッシュで部品と値を散らす（今の `FaceParts.For` と同じく決定的）。読み込みは検証して壊れていれば止まる（§1 の決まり）。
5. 単体テスト: 混ぜの端と中間・読み込みの検証・同じ名前は同じ顔。

## F2: 頭の顔の層（2.5 mm）

1. 頭の剛体に 2 つ目の格子（2.5 mm、20 × 28 × 20、頭の箱に合わせる）を、手の層の仕組みで持たせる。
2. 5 mm の頭の格子 = 頭蓋骨と脳（その内側）。頭蓋骨の外（頭皮・顔の肉・目・鼻・唇・耳・髪・ひげ）は顔の層だけに置き、5 mm の格子から抜く（同じものを 2 回持たない、§1.5）。
3. 目は顔の層だけ。視力（`BoxerBody.Vision`・`VisionState`）と目の描画（`EyeLook`: 黒目・まぶた）は顔の層の白目を読む。元の量（白目の体積に対する残り）の意味を保つ。
4. 打撃・削れ・砕ける見た目・あざ・チアノーゼの塗り直しが顔の層にも効くこと（手の層で、はめる物の格子が当たり・削れを受ける道と同じ）。
5. `PhysicsVersion` を **57** に上げる（56 は手の層の段 3 のグローブが使う）。bump-version スキルの手順。チェックサムを取り直し、取り直した理由をテストの注釈に書く。
6. テスト: 脳のセル数が変わらない・頭蓋骨の外が 2 回数えられていない・顔の層の目が潰れると見えが落ちる・顔の層の質量が前の頭の外側の質量と近い（大きく変わるなら数字を報告）。

## 確かめ（自分の目で）

- 3 キャラ以上の頭を正面・横・上から写し（`miniring-arena faces` を顔の層に対応させる）、`face_refs/base/base_*.png` と横に並べた比較の画像を作る（`docs/plans/face_refs/compare_*.png`）。
- 部品ごとに 6 種を並べた画像（同じ土台に部品だけ替えた顔）を作る。
- 速いテスト（`run.cmd test`）を通す。全テストは raytrek（`bash tools/remote-test.sh`）か tower32 で。落ちたものは master でも落ちる 4 件（Punches_AreAimed・ABatBot・Punch_CounterDamage・Tonfa ScriptedStraightPunch）以外を直す。

## 近道と文書

- 近道は `docs/DEBT.md` に足す。`docs/CHARACTERS.md` §2 を書き直し、`docs/VERSIONS.md` の先頭に 1 行。
- コミットは段ごとに `claude/face-parts` へ（`feat: ...` の形、付記なし）。プッシュしない。

## 報告

`docs/plans/CLAUDE_REPORT_FACE_PARTS_2026-11-19.md`: やったこと・測った数字・比較の画像のパス・目標と違う所・テストの結果（件数）・残り。
