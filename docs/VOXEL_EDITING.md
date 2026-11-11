# ボクサーのボクセルを編集ソフトで直す（Vengi・MagicaVoxel）

ボクサーの中身（皮膚・肉・骨・臓器・髪・目・トランクス）は `BoxerAnatomy.Build` が部位ごとに生成している。
これを .vox に書き出してボクセル編集ソフト（[Vengi](https://github.com/vengi-voxel/vengi)、MagicaVoxel など）で塗り直し、
ゲームへ組み込める。直した部位は生成の代わりに使われ、直していない部位は生成のまま。

## 流れ

1. 書き出す（組み込んだ直しがあればそれを当てたもの。`--from generated` で生成のまま）。

   ```bash
   dotnet run --project games/MiniRing.Arena -c Release -- voxexport --out boxer.vox
   ```

2. Vengi（voxedit）で `boxer.vox` を開いて塗る。保存は .vox のまま。
3. 確かめる（部位ごとに、生成から何がどれだけ変わったかが出る）。

   ```bash
   dotnet run --project games/MiniRing.Arena -c Release -- voxcheck --in boxer.vox
   ```

4. 問題なければ組み込む（`games/MiniRing/Voxels/boxer.vox` に置かれ、次のビルドからゲームに埋め込まれる）。

   ```bash
   dotnet run --project games/MiniRing.Arena -c Release -- voxcheck --in boxer.vox --install yes
   ```

## 決まりごと

| 項目 | 決まり |
|---|---|
| モデル | 部位の種類ごとに 1 つ。名前は `Head`・`Chest`・`Pelvis`・`UpperArm`・`Forearm`・`Thigh`・`Shin`・`Foot`・`Toe`。左右の腕・脚は同じ中身（1 つ直せば両方に効く） |
| 色 | 材質ごとに 1 色（パレットの 1〜15 番）。読み込みは**色**で材質に戻すので、パレットの並びが変わってもよいが、色そのものは変えない。材質に無い色（16 番以降の紫など）があると、どの部位のどの色が何個かをエラーで出す |
| トランクス | 赤（10 番）で塗る。ゲームではコーナーの色（赤・青）に塗り替える。身なり無しで作るときは服の所が皮膚になる |
| 大きさ | モデルの大きさ（セルの数）を変えない（切り詰め・拡大をしない）。設定（`HeadScale`・`VoxelSize`・体の大きさ）を変えるとセルの数が変わり、合わない部位は生成のまま使われる（警告が出る。テスト `ShippedModel_IfAny_FitsTheDefaultSettings` が落ちる）ので、書き出し直して直し直す |
| 座標 | .vox は Z が上。体の前は .vox の −Y（MagicaVoxel の正面）。本人の左は +X |
| 表面 | 皮膚の層（打たれたときの「元の表面」）は、空との境から自動で決まる。中を空にすると、そこも表面になる |

材質の番号と色（`voxexport` も出す）:

| 番号 | 材質 | 番号 | 材質 |
|---|---|---|---|
| 1 | 皮膚 | 9 | 目（白目） |
| 2 | 筋肉 | 10 | トランクス（赤） |
| 3 | 骨 | 11 | トランクス（青。読み込むと赤と同じ扱い） |
| 4 | 脳 | 12 | 腰のゴム |
| 5 | 心臓 | 13 | ブーツ |
| 6 | 肺 | 14 | 髪 |
| 7 | 肝臓 | 15 | 黒目 |
| 8 | 腸 | | |

臓器（脳・心臓・肺・肝臓・腸）は半分以上壊れると死ぬので、減らしすぎると打たれ弱くなる。骨は腕・脚が切れる所（骨が断たれる所）を決める。

## 仕組み

- `games/MiniRing/Voxels/MagicaVox.cs`: .vox（版 200）の読み書き。名前付きのモデル（nTRN の `_name`）とパレット。知らないチャンクは読み飛ばす。
- `games/MiniRing/Voxels/BoxerVoxelModel.cs`: 部位の格子と .vox の行き来（軸の入れ替え・色 ↔ 材質）、`BoxerVoxelOverrides`（組み込んだ直し `Shipped`）。
- `BoxerAnatomy.Build(..., overrides)`: その部位の直しがあり、セルの数が同じなら生成せずそれを使う。
- テスト: `tests/MiniRing.Tests/VoxelModelFileTests.cs`（書き出して読み戻すと同じ体になる・パレットの並びが変わっても色で戻る・直しが使われる・大きさ違いは生成に戻る・知らない色と名前はエラー）。
