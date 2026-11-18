# 極真空手の廻し蹴り（モーションキャプチャ、CC0）

回し蹴りの手本（`games/MiniRing/Bots/kicks/roundhouse_low.json`・`roundhouse_high.json`。中段 `roundhouse_mid.json` は両者を狙いの高さで混ぜた物）と、前蹴りの手本（`front_mid.json`）の元データ。

## 出典

Szczęsna A., Błaszczyszyn M., Pawlyta M.,
"Optical motion capture dataset of selected techniques in beginner and advanced Kyokushin karate athletes",
*Scientific Data* 8, 13 (2021). doi:10.1038/s41597-021-00801-5。
データは figshare（collection 4981073）、**ライセンス CC0**。ここでは 1 人分（`B0367.zip`、https://ndownloader.figshare.com/files/24601574）の一部だけを使う。

使うときは上の論文を引用すること。

## 何が入っているか

- 技 `S02` = 前蹴り（中段、ミット 1.04 m、右脚 6 本: 足首の速さの山 7.9〜9.6 m/s）、`S03` = 廻し蹴り下段（膝〜腰）、`S04` = 廻し蹴り上段（肩〜頭）。試し `E02` = コーチが持つミットを蹴る。`T01`・`T02` = 2 回の試し（各 3 本）。右脚の蹴り 18 本（前蹴り 6・下段 6・上段 6）。
- `kick_<技>-<試し>_<通し番号>.csv`: 蹴り 1 本。1 行目 `# source=… rate=200 peak_frame=… peak_speed=… first_frame=…`（原本のファイル名・撮影の速さ [Hz]・足首の速さの山のフレーム（この区間の中）・その速さ [m/s]・原本のどのフレームから切ったか）、2 行目が列名、あとは 1 フレーム 1 行。
- 列: `frame` と、ラベルごとの `_x _y _z`（**mm、Z が上**、実験室の座標。欠損は空欄）。ラベルは Vicon Plug-In Gait の名前: 股・膝・足首の関節の中心 `RHJC RKJC RAJC LHJC LKJC LAJC`、`RTOE RHEE LTOE LHEE`、骨盤の外側の点 `LASI RASI LPSI RPSI`、肩の中心 `LSJC RSJC`、`CLAV C7`、ミット `Tarcza1`〜`Tarcza6`。
- 1 本あたり 125〜280 フレーム（0.6〜1.4 秒）、全部で約 0.7 MB。

## 切り出し方

`tools/mocap/extract_kicks.py`（C3D の読みは `tools/mocap/c3dread.py`）。右足首（`RAJC`）の速さが 6 m/s を超える山（0.5 秒以上離れる）を 1 本とし、山から前へ速さが 1 m/s を下回るまで（構え）、後ろへ 1 m/s を下回るまで（戻り）、さらに前後 0.15 秒の余白を付けた区間を、使うラベルだけ mm の整数に丸めて書く。C3D の原本（1 本 6 MB 前後）はリポジトリに入れていない。

```
py -3 -I tools/mocap/extract_kicks.py <B0367 の展開先> <出力先>   # S02〜S05 全部が出る。S05 は使わないので置かない
```

## ゲームでの使い方

`games/MiniRing/Motion/KickCapture.cs` が読み、ミットの方向を基準に、腿の軸（方位・仰角）・すねの曲がる面のねじり・膝の曲げ・骨盤の回り・胸のひねりへ読み替える（ゲームの膝の制御と同じ定義。`LegKnee`）。`kickmocap`（`miniring-arena`）が表（`docs/arena/kick_mocap_analysis.md`）と手本の JSON を出す。
