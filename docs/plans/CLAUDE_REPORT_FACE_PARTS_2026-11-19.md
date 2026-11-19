# 報告: 顔を部品から組み立てる（F1 完了、F2 は未着手）

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

## F3（2026-11-19、枝 `claude/face-f3`）: 見た目の直しと名簿 18 人の顔

- 直した所: 目は参考図 D のまぶた 6 種（白目 + 黒目 3〜4 セル幅）に差し替え。瞳の既定は暗い茶（`FacePalette`、色は肌・髪・瞳の 3 色から一つの式）。髪は部品の `reach`（普通 1.5〜2 セル、アフロ・ポニーテール・長髪だけ大きい）と `frizz`（髪質）で頭の外へ出る量を決める。ひげは唇の左右にあれば隙間を空け、頬まで覆う全体のひげ（beard.7）を足し、無精ひげは `density` で間引く。鼻筋は隣の柱より前へ出る脇を影の皮膚にして正面から見えるようにし、鼻筋の高さ・眉の張り出しは横の輪郭の部位 `browridge` 4 種。四角い顔でも頭の上は丸い輪郭。
- 追加データ（`parts2/`）: まぶた 6・唇の厚み／人中 6（mouth.7〜12）・顔の形 5（jaw.7〜11）・髪質 6（hair.7〜12、`solidify` で縞を除いた）・鼻筋 4。場合分けは無く、重み付きの和（0.5 閾値）で混ぜる。
- 名簿: `tools/face-parts/roster_faces.py` が 18 人の `"face"`（部品の重み・値・肌髪瞳の hex）を書く。沖縄 3 人は二重・奥目・眉の張り出し・広い顔、本土 2 人と韓国は一重気味・平ら、ヨーロッパは鼻・髪・ひげ・瞳の色の組で区別。
- 画像: `docs/plans/face_refs/compare/fighter_<id>.png`（肖像 | 正面 | 横）、`roster_composed.png`、`parts_*.png`（browridge 含む）。再現は `FACE_DUMP_DIR` + `Probe_DumpFaces` + `render.py`。
- テスト: 新規 `FaceComposerLookTests` 14 件、既存 `FacePartLibraryTests` 修正。速い段 650 件中、最後の 1 件（目の幅の上限）を直して関連 24 件は合格（全体の再実行はしていない。直前の全体は 649 合格）。全テストは未実行。
- 残りの不一致: 20x28 では目の形の違い（一重／二重）は 1 セルの線でしか出ない。傷（ガストン・ディミトリ・マルクス・ナロン・ジョアン）と顔の傷は材質が無く未実装。ひげの色は髪と同じ（コンラッドの濃い金髪のひげなど）。髪は帽子のように平らで、肖像の毛先の細かさは出ない。口ひげは beard の形が大きく、唇の上が塗りつぶされる。横から見た輪郭の鼻・あごは粗い。

### F3 追補（レビュー指摘の修正）
- 無精ひげは `FaceMaterial.Stubble`（肌を髪へ 28% 寄せた一様な色。散らばった髪のセルは廃止）。そばかすは肌よりわずかに濃い色。毛先（frizz）は頭の外側のみ・半径 1。
- ひげは正面の図 × 顔の表面から 3 セルの殻（あごの形に依らず顎・頬に沿う）。beard.7 = もみあげ・頬・口ひげ・あごが 1 つにつながる領域（テストで連結を確認）。beard.2 = ハンドルバー、beard.8 = 細い口ひげ（鼻の下の行へ）。
- 長髪: hair.12 = 肩までの髪、hair.13 = 結んだ長髪（顔の横に垂れ、横から見て後ろへ尾）。
- 鼻の脇の影は目の高さ〜鼻先の 5 行だけ。眉は眉間（中央 2 セル）を空ける。
- 速い段の全体: 654 件すべて合格（失敗 0）。FaceMaterial に Stubble を末尾追加（F2 側は材質の対応表に足すこと）。
