# 報告: 顔を部品から組み立てる（F1 完了。F2 は末尾の節）

枝 `claude/face-parts`（`claude/hand-layers` の ff63a89 の上）。プッシュなし。

## やったこと（F1: 部品の表と組み立て。格子に依らない）

- `tools/face-parts/{parse.py,build_face_parts.py}`: 参考図の txt（`docs/plans/face_refs/parts/parts_{a,b}.txt`・`base/base.txt`）→ `games/MiniRing/Voxels/FaceParts/*.json`（部位 8 つ × 各 6 種 + `base.json`）。何度でも同じ JSON を作り直せる。組み込み資源（`MiniRing.csproj`）。
- `FacePartLibrary`（読み込みと検証: 壊れた JSON・知らない文字・欠けた部位・大きさの不一致・留める点が格子の外、で `InvalidDataException`）。
- `FaceSpec`（部位ごとの部品の重み + `FaceParts` の値 + 肌・髪・瞳の色）。`For(名前)`（決定的）・`FromLook`（既存の髪型・眉・ひげを部品へ）・`FromJson`・`Validate`。
- `FaceComposer.Compose(spec, nx, ny, nz)`: 任意の解像度の材質の格子。輪郭は「あごの型 × 基準の上からの輪郭」の視体積、髪・ひげは正面 × 横の積、眉・唇・目は表面から塗る（目は 2 セルの深さの白目）、鼻は正面 × 横の積、耳は頭の横の外に横 × 正面の厚み。混ぜは重み付きの和を閾値で切る（輪郭 0.5、細い線 0.4）。
- 追加の要望（人種・国の違い）: `FaceParts` に `FaceWidth`・`BrowRidge`・`Freckles` を追加（既存: `Cheekbones`・`Jaw`・`NoseWidth`・`Nose`・`Lips` など）。色（肌・髪・瞳）は `FaceSpec` が持つ。まぶた・髪質・眉の張り出し・唇・顔の形は、`FaceParts/<eyelids|hairtexture|browridge|lips|faceshape>.json` を置けば読み込まれる（`FacePartLibrary.Extra`）。**組み立ての対応はまだ無い**（読み込んで検証するだけ）。`parts2/` が届いたら変換の道具へ足し、`FaceComposer` に対応を足す。
- 名簿: 任意の `"face"`（`FighterCharacter.Face`）。無いキャラは `look` から決める。
- 文書: `docs/CHARACTERS.md` に §2.0 を足した。`docs/DEBT.md` にはまだ足していない（近道は CHARACTERS §2.0 に書いた）。

## 測った数字・テスト

- 新しいテスト `FacePartLibraryTests` 9 件（部品数・壊れたファイルで止まる・検証・`FromJson`・同じ名前は同じ顔・混ぜの端と中間・解像度（体積比 ±15%）・値が効く・全員の顔が組み立つ）全部合格。
- キャラ・顔・名簿に関わる既存 76 件合格。速い段の全体は 636 件すべて合格（失敗 0）。全テストは未実行（raytrek 未使用）。

## 画像

- `docs/plans/face_refs/compare/compare_{Taro,Ren,Gaston,Narong,Yoandry}.png`（左 = 参考図の土台の正面・横・上、右 = 組み立てた顔）。
- `docs/plans/face_refs/compare/parts_{eyes,brows,mouth,nose,ears,jaw,hair,beard}.png`（同じ土台に部品だけ替えた 6 種）。
- 再現: `FACE_DUMP_DIR=<dir> dotnet test ... --filter Probe_DumpFaces` → `py -3 tools/face-parts/render.py <dir> <out>`。

## 目標と違う所

- 参考図の土台と同じ顔にはならない: 組み立ての顔は名前から決めた部品の組み合わせで、目・唇は部品の大きさ（目は土台より大きい）。鼻は横の輪郭に焼き込まれた出っ張りを均して部品の鼻を足している。正面の鼻の穴・唇の線は 1 セルで、寸法を小さくした顔（`Lips` が小さい等）では細くなる。
- 髪は「正面 × 横」の積で、髪の参考図の生え際が斜めのところ（横の格子が前の端まで届かない）は、参考図の正面と少し違う。
- 影の皮膚（`e`・`d`）は 1 つの材質にまとめた。
- 参考図 `parts_b.txt` の記述（上の見え方は「上から下 = 後ろから鼻」）と実データ（ポニーテールが下 = 上の端が前）が食い違っていたので、実データに合わせた。

## 残り

- **F2（顔の層、2.5 mm、PhysicsVersion 57）は未着手**。見積もりの結果、次の理由で 1 回の作業に収まらないと判断した:
  `VoxelBody.AddCompanion` は武器用に作られていて、体の部位は `BoxerBody`（`_parts`・`_byBody`・`_originals`・治る順・あざ・チアノーゼの塗り直しのキャッシュ・器官・首の折れ・`Landmarks`）と `BoxingGame` の接触・削れの道が部位 1 格子を前提にしている。
  目（`BoxerAnatomy.Eye`・`Pupil`・`EyeGaze`・`Vision`・`DestroyEye`）に触る所が 19 ファイル（`BodyLandmarks`・`TargetMap`・`Targeting`・`BotArena`・`BlindHud`・`Broadcast`・`VoxelBreakSound`・Arena の複数）。
  半端に入れると 1100 件のテストと再生のチェックサムを壊すので、F1 までで止めた。
  進め方の案: (1) `VoxelPart` に `Face`（`VoxelBody?` と `VoxelVolume`）を足し、`BoxerBody` の構築で頭の 5 mm の格子から頭蓋骨・脳・首以外のセルを抜く、(2) `BoxingGame` の拳 × 部位の接触と `Wound` を、武器の `Grids` と同じ添字の回しで部位の格子すべてに、(3) `Vision`・`EyeGaze`・`DestroyEye` を顔の層の白目に、(4) `Draw` の塗り直しを格子ごとに、(5) `PhysicsVersion` 57・チェックサムの取り直し。
- F3（`fighters/faces.txt` と肖像から 18 人の `"face"` を決める）も未着手（F2 より前に入れても、肖像以外には効かない）。
- 全テスト（raytrek）、`docs/DEBT.md` への追記。

---

# F2: 頭の顔の層（2.5 mm）— 報告（2026-11-19、枝 `claude/face-f2`）

枝 `claude/face-f2`（`claude/face-parts` の 10b167c の上）。プッシュしていない。`FaceComposer` の公開 API と部品の JSON の形式は変えていない（F3 と重ならない）。

## やったこと

- **顔の層**（`games/MiniRing/Voxels/FaceLayer.cs`）: 頭の剛体に 2 つ目の格子を重ねた。頭の箱と同じ箱を 20 × 28 × 20（約 2.5 mm）で分け、`VoxelBody.AddCompanion`（手の層と同じ仕組み）で載せる。`FaceLayer.Split(作った 5 mm の頭, FaceSpec)` が次のように分ける。
  - 5 mm の頭に残すもの: 脳・脊髄・頭蓋骨、首の段の皮膚・筋肉・頸椎。頭蓋骨のうち、眼窩と、脳に面していない骨で組み立てた顔の外へ出る所は抜く。首の段の皮膚は、あご・ひげと重ならない所だけ残す。
  - 顔の層 = 組み立てた顔 ∪ 残したセルを包む 1 層の皮膚 − 残したセルと重なる所。外の 1 層が皮膚で、その下は筋肉。
  - 材質の対応: 白目 → 目、瞳 → 黒目、髪・眉 → 髪、唇 → 唇、鼻の穴 → 空。それ以外（影・そばかす、F3 の無精ひげ）は皮膚。
- **質量**: 体格から決まる頭の質量は変えていない。それを 2 つの格子の体積で分ける（顔の層の 1 セルは 5 mm のセルの 1/8。`VoxelBody` の新しい引数 `massPerCell`）。当たりの箱は 2 つの格子の和になる。
- **当たり**
  - 武器・グローブ・素手・蹴り・肘の接触は、`BoxerBody.Layers`（顔の層 → 頭の格子の順）を回す。
  - 腕・脚の後ろの質量は、同じ剛体のうち打撃を止めた層までが受ける。
  - `VoxelBody.PreferField`（新しく作った）で、顔の層はいつも打たれる側になる。粗いグローブ（4 mm）やすね（5 mm）に打たれても、面の下で潰れるのは顔。
  - 顔の層が受けた曲げは、その下の梁（首）へ渡す（`VoxelContact.BeamOf`）。
  - `BoxerBody.Impact` は顔の層を先に削り、使い残しで頭蓋骨を削る。削る量の物差しは 5 mm のセルなので、顔の層には 8 倍で渡す（新しく作った `VoxelBody.ImpactSpending` を使う）。
- **目・見え**
  - `Vision`・`DestroyEye`・`BodyLandmarks`（目と、顔の層の表面まで辿るこめかみ・あご）・`EyeGaze`（黒目・まぶた）は顔の層を読む。見えの意味（白目の体積に対する残り）は前と同じ。
  - `TargetMap` の「脳が出ている」は、頭の格子と顔の層の両方が空いた所だけ。
- **見た目**
  - 顔の層を含めたもの: 描画・影・あざ・チアノーゼ・顔色・チェックサム（顔の層のセル数と版）。再生は頭蓋骨と脳を先に、顔を後に治す。
  - 肖像（メニューの顔）は顔の層の細かさで写す（`MenuView.FacePixel` 3.7 → 1.85）。
  - `miniring-arena faces` は 2 つの格子を、正面・横・上・斜めの 4 方向で描く。
- **顔の形の渡し方**
  - `BoxingCorners.PlayerFaceSpec`・`CpuFaceSpec` を新しく作った。名簿のキャラは `FighterCharacter.Face` を渡す（`VideoMaker.WithCharacters`・`MatchSelection`）。
  - 記録 `ReplayMatchSettings` にも同じ値を書く。顔は当たりの形なので、再生も同じ顔で作る必要がある。
  - 渡されなければ `FaceSpec.FromLook(見た目, 画面の名前)` で決める。
- **版と文書**: `Fighter.PhysicsVersion` を 57 にした（`docs/VERSIONS.md`）。`docs/CHARACTERS.md` §2 を書き直し、`docs/DEBT.md` に「顔の層」を足し、CLAUDE.md §8 の項目にも入れた。
- **レビュー**（別のエージェント）の指摘のうち直したもの:
  - 顔の層への打撃が首を曲げなかった → 下の梁へ渡す。
  - 再生で顔が先に予算を使い切り、脳が戻らなかった → 頭蓋骨と脳を先に治す。
  - `TargetMap` の版の鍵 → 組（tuple）にした。
  - 影・TV の実況の数え・空手の測定・武器のドリル・空手の動画 → 顔の層を見る。
  - 直さなかった指摘は `docs/DEBT.md`「顔の層」に書いた。

## 測った数字

次の表は、首の皮膚を 5 mm に残す前（b89d2fc）の値。その後、首の皮膚の 20 セルほどが 5 mm 側へ戻った。

| キャラ | 5 mm の頭（残り） | 脳 | 顔の層のセル（5 mm 換算） | 白目+瞳 |
|---|---|---|---|---|
| gaston | 296 | 162 | 2484（310.5） | 32 |
| narong | 296 | 162 | 2461（307.6） | 32 |
| yoandry | 296 | 162 | 2552（319.0） | 32 |
| marcus | 296 | 162 | 2292（286.5） | 44 |
| ramon | 296 | 162 | 2431（303.9） | 44 |
| albert | 296 | 162 | 2552（319.0） | 32 |

- 作った 5 mm の頭は 633 セル（脳 162・骨 118・皮膚 231・髪 70・目 8・黒目 2・唇 8・首の筋肉 26・脊髄 8）。顔の層へ移した外側は 337 セル分。**顔の層の体積は、その 85〜95%**（`FaceLayerTests` が ±20% に入ることを確かめる）。
- 1 セルの質量を作った頭のままにすると、頭は 2.7〜8% 軽くなった（既定の顔 0.500 → 0.486 kg、marcus 0.700 → 0.644 kg）。体格から決まる頭の質量を変えないよう、2 つの格子の体積で分けることにした。今の頭の質量は前と同じで、そのおよそ半分が顔の層の分。
- 脳のセル数は全員 162 のまま。
- 白目の体積: 前の 5 mm の目は 10 セル（2.5 mm 換算で 80）。顔の層の目は 32〜44 セルで、部品の目の大きさで決まる（細い目は 1 段）。見えの値は割合なので意味は変わらないが、狙える目は小さくなった。

## テスト

- **新しいテスト**: `FaceLayerTests` 5 件が次を確かめる。
  - 脳・脊髄が変わらない。頭蓋骨の外の材質が 5 mm の格子に無い（皮膚は首の段だけ）。
  - 同じ所を 2 つの格子が持たない。顔の層の体積が、抜いた外側の ±20% に入る。
  - 剛体は 1 つで、顔の層が頭の格子より先に当たる。
  - 白目を潰すと見えが落ち、`DestroyEye` で 0 になる。
  - 軽い打撃は顔だけを削り、重い打撃は頭蓋骨まで届く。
  - `FaceSpec` が記録の見出しを通っても、同じ顔に組み立つ。
- **目で見るテスト**: `FaceLayerVisualTests`（`.visualcheck/face_layer_views.png` に書く）。
- **取り直したチェックサム**
  - `CharacterSamenessTests`: Expected ← NOPLAN-LEGACY、ExpectedWeapons ← LEGACY、ExpectedAdopted ← SAME、ExpectedGamePlan ← LEGACY。手元の Release で `CharacterSamenessProbe` を回して取り（3ab5591）、raytrek の全テストでも同じ値で合格した。
  - `StepCostRegressionTests`（刀の試合）: 0x9e62bb83e2275925。
- **直したテスト**
  - 目・顔は顔の層を読むように: `EyeLookTests`（丸い目 eyes.1 の顔で）・`EyeLookVisualTests`・`FaceColorVisualTests`・`LandmarkTests`・`VisionTests`・`KarateVitalsTests`・`CharacterTests.EveryRosterFace_IsDistinguishable`・`CharacterRosterTests`（肖像の大きさ）。
  - `VoxelDamageTests.DestroyingTheBrain`: 前・後ろ・上から打つようにした（下からの打撃は首を通るため）。
  - `ClubGripTests`・`BojutsuTests` の数え: 顔の層も数える。
- **速い段**（手元、01bca87）: 641 件中 640 件が合格。失敗の 1 件は `FootShapeTests.AKickingToe_StaysWithinItsRange_AndDoesNotBounce`（37.04°。上限 37° のぎりぎり）。
- **全テスト**（raytrek、01bca87）: 1203 件のうち実行 1198、**合格 1182・失敗 16**（実行しないもの 5）。
  - raytrek の `remote-test` は別のエージェントが使っていたので、別の複製 `C:\Users\smiya\repos\rt-face-f2` で回した。CPU は他の dotnet の処理 35〜45 個と分け合った。
  - master でも落ちる 4 件: `Punches_AreAimedWhenThrown`・`ABatBot_Swings…`・`Punch_CounterDamage…`・`Tonfa ScriptedStraightPunch…`。
  - **F2 で落ちるようになった 12 件**（土台の 10b167c では合格することを確かめた）:
    - `KickShinTests` 3 件: 当たる本数・転ぶ本数が、許す幅を 1 本外れる。
    - `KickContactTests.EveryKick…`: かかと落としの後、蹴った側が起き上がりの途中。
    - `ElbowContactTests.EveryElbow…`: 横の肘がガードにも当たらない。狙いのこめかみが顔の層の表面へ 1 cm 動き、始めの距離が 1.08 → 1.09 m になった。
    - `BojutsuTests` 2 件: 横打ちが相手を削らない。
    - `PredictionTests.PredictedInsideSlip…`: 反応だけの側で頭に届く割合が 0.36（下限 0.4）。
    - `DownCallTests.AMomentaryBlackout…`。
    - `DownRecoveryTests.PelvisHeight…`: 0.254（上限 0.225）。
    - `ShieldTests.TheShield_TakesTheSwordsSwing…`。
    - `FootShapeTests.AKickingToe…`: 37.04°。
  - 12 件はどれも、頭の形（顔の層の表面・あご・急所の点の位置）と頭の当たりの箱が変わり、ボットや台本の動き（狙い・間合い・体勢）が少し変わったもの。チェックサムのテストは全部合格したので、決定性は保たれている（手元と raytrek で同じ値）。許す幅の調整や技の練習し直しは、この作業ではしていない。

## 画像（自分で見た）

- `docs/plans/face_refs/compare/f2_face_layer_views.png`
  - 試合の描画（ボクセルの描画・照明）で、名簿の 3 人（gaston・narong・yoandry）の頭を正面・横・上・斜めから写したもの（`FaceLayerVisualTests`）。
  - 目・眉・鼻・口・ひげ・髪が、顔の層の細かさで見える。横から見ると鼻が出ている。上からは髪と頭の形が見える。
- `docs/plans/face_refs/compare/f2_faces_arena.png`
  - `miniring-arena faces --names champion,swarmer,karate` の出力（2 つの格子の正射影）。1 人 1 行で、正面・横・上・斜め。
- 見て気づいた所
  - 顔の段差に、細く明るい線が出る（2.5 mm の段の照り返し）。
  - あごの下の首が短く見える（あごが首に重なっている）。
  - 細い目の部品（eyes.2・eyes.6）は白目が 1 段しかなく、黒目の上下が描けない。

## 目標と違う所・近道（`docs/DEBT.md`「顔の層」）

- 5 mm の頭蓋骨は、作った頭のまま。顔に合わせて作り直していない。眼窩と顔の外へ出る骨を抜くだけなので、脳を包む殻が額・鼻筋で顔の表面に近い（1 層の皮膚で包んでいる）。
- 顔の肉は皮膚と筋肉だけ。影・そばかす・無精ひげは皮膚にしている。F3 の `FacePalette` の色は、マージの後に顔の層の色の表へ写す必要がある。
- 同じ 1 歩で顔と頭蓋骨の両方に触れると、2 つの接触がそれぞれ頭をねじり、触れた音も 2 つ鳴ることがある。
- 首が折れると、首の段のうち顔の層が持つ皮膚（あごに重なる所）は頭の側に残る。
- 削れたセルを数える道具のうち、`HandleLab`・`SelfCutLab`・`SwordLibraryLab`・`SparVideo`・`SwingVideo`・`KneeLab`・`KarateLab`・`TvDirector` は 5 mm の格子だけを数える。`StrikeDrill`・`SwordSparring` は顔の層も数える（細かいセルも 1 個と数える）。

## 残り

1. **F2 で落ちるようになった 12 件**: 頭の形が変わったあとの技の練習し直し（蹴り・肘・棒の横打ちの経路と狙い）か、許す幅の見直しが要る。土台との違いは「狙いの点が顔の層の表面へ動いた」「頭の当たりの箱が 2 つの格子の和になった」の 2 つ。どちらが効いているかは、まだ切り分けていない。
2. **頭蓋骨の作り直し**: 5 mm の頭蓋骨を、組み立てた顔の形から作り直す（脳のセル数は保つ）。
3. **F3 とのマージ**: `FaceMaterial.Stubble` は皮膚へ写る（`FaceLayer.MaterialOf` の既定）。`FacePalette` の色を顔の層へ写す。部品が増えても `FaceComposer` の API は同じなので、そのまま組み立つ。
