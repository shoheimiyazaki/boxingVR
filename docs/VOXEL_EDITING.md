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

## 組み込んだ直し（記録）

- 2026-11-15（PhysicsVersion 31）: 頭（`Head`）の外観をユーザーが MagicaVoxel で直した（鼻・眉・唇・髪。頭の中身は 1 セル後ろへ）。
  組み込む前に中身を外観に合わせて整えた: 皮膚は表の 1 セルだけにし、その下に埋もれた皮膚・皮膚の下の髪は近くの骨・筋肉を伸ばして埋めた
  （脳に接する所は骨。鼻・眉・頬の骨になる）、唇をトランクスの赤（10）から唇（21）へ。外から見える面は変えていない。
  効き: 顔の真ん中への重い 1 発で脳が削れる量が 21% → 4%（顔の骨で止まる）、溜めた右ストレートが反応だけの守りの頭に当たる割合が 0.57 → 0.48。
  キャラごとの顔のテンプレート（`FaceSculptor`: 髪型・起伏・眉・ひげ・色）は、直した頭の上に今までどおり掛かる。

## 決まりごと

| 項目 | 決まり |
|---|---|
| モデル | 部位の種類ごとに 1 つ。名前は `Head`・`Chest`・`Pelvis`・`UpperArm`・`Forearm`・`Thigh`・`Shin`・`Foot`・`Toe`。左右の腕・脚は同じ中身（1 つ直せば両方に効く） |
| まわり（無くてよい） | `<部位>Halo`（例 `ChestHalo`）: その部位の格子を各面 6 セル（`BoxerVoxelOverrides.HaloMargin`）広げた大きさで、切れ目の向こうにつながる組織。関節のつなぎ（`JointBridge`）が隙間の材質に使う。CT の体の道具（`tools/ct-body`）が書く。`voxcheck` が中身を数える |
| 色 | 材質ごとに 1 色（パレットの 1〜15 番）。読み込みは**色**で材質に戻すので、パレットの並びが変わってもよいが、色そのものは変えない。材質に無い色（23 番以降や、使わない番号の紫など）があると、どの部位のどの色が何個かをエラーで出す |
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
| 8 | 腸 | 22 | 脊髄（首・背骨の中。ゲームが組み立てるときに置く（`BoxerSpine`）ので、`voxexport` の書き出しには入らない。.vox に塗れば、その塗ったとおりに使われる） |
| 26 | 動脈（CT から作る体だけ。`tools/ct-body` が置く。生成の体には無い） | | |

臓器（脳・心臓・肺・肝臓・腸）は半分以上壊れると死ぬので、減らしすぎると打たれ弱くなる。骨は腕・脚が切れる所（骨が断たれる所）を決める。

## 仕組み

- `games/MiniRing/Voxels/MagicaVox.cs`: .vox（版 200）の読み書き。名前付きのモデル（nTRN の `_name`）とパレット。知らないチャンクは読み飛ばす。
- `games/MiniRing/Voxels/BoxerVoxelModel.cs`: 部位の格子と .vox の行き来（軸の入れ替え・色 ↔ 材質）、`BoxerVoxelOverrides`（組み込んだ直し `Shipped`）。
- `BoxerAnatomy.Build(..., overrides)`: その部位の直しがあり、セルの数が同じなら生成せずそれを使う。
- テスト: `tests/MiniRing.Tests/VoxelModelFileTests.cs`（書き出して読み戻すと同じ体になる・パレットの並びが変わっても色で戻る・直しが使われる・大きさ違いは生成に戻る・知らない色と名前はエラー）。

## 武器・はめる物

ボクサーと同じ流れで、武器・はめる物（`Weapon.Build`）も .vox で直せる。`WeaponVoxelModel`（`games/MiniRing/Voxels/WeaponVoxelModel.cs`）が行き来を受け持ち、
パレット・色 ↔ 材質・軸の入れ替えは体（`BoxerVoxelModel`）と同じ仕組みを使う。

```bash
dotnet run --project games/MiniRing.Arena -c Release -- weaponexport --out weapons.vox            # --from generated で生成のまま
dotnet run --project games/MiniRing.Arena -c Release -- weaponcheck --in weapons.vox              # 種類ごとの大きさ・塊の数・材質ごとの変化
dotnet run --project games/MiniRing.Arena -c Release -- weaponcheck --in weapons.vox --install yes # games/MiniRing/Voxels/weapons.vox へ
```

| 項目 | 決まり |
|---|---|
| 対象 | `IronSword`・`WoodenClub`・`Katana`・`Longsword`・`Glove`・`KnuckleDuster`・`Katar`・`Gauntlet` |
| `Fist`（素手の拳）は対象外 | `Weapon` は拳を `BareHandState` に置き換え、`BareHandState` は生成した拳（正拳）の幾何から指・節のセルを振り分ける（手形もセルの幾何変換）。直した格子ではその振り分けが合わなくなる。書き出さず、読み込みで `Fist` という名前があればエラー。`KarateFist` の「外側を皮膚にする」後処理も拳だけにある（ほかの種類には生成の後処理は無いので、直しは生成と同じ結果になる） |
| 座標 | .vox は Z が上。体と同じ変換（武器のローカルの Y が .vox の Z、Z が .vox の −Y）。長さの X（+X が切っ先）はそのまま |
| 塊 | .vox の 1 モデルは各軸 256 セルまで。超える軸（剣 264・ロングソード 304 セルの長さ）は均等に分け、`IronSword#0`・`IronSword#1`… とする。シーンの上では nTRN の位置で 1 本につながって並ぶ。読み込みは番号順に 1 本へ戻し、番号の欠け・重複・隙間・重なりはエラー。直すときは塊をまたいで塗ってよいが、**塊の大きさ・位置を変えない**。256 以下の武器は `#` を付けず種類名だけ |
| 革 | `Glove`・`Gauntlet` は赤い革（18）で書き出す。読み込みでは青い革（19）も 18 に戻し、`Weapon.Build(kind, leather)` が 18 を持ち主のコーナーの革に置き換える（体のトランクスと同じ扱い） |
| 大きさ | セルの数を変えない。違うと使われず生成のまま（警告が出る。テスト `ShippedModel_IfAny_FitsTheGeneratedWeapons` が落ちる） |
| 版 | **直した `weapons.vox` を組み込むと武器の質量・接触が変わるので、組み込むときに `Fighter.PhysicsVersion` を上げる**（道具を足しただけでは上げない。組み込み先は `games/MiniRing/Voxels/weapons.vox`、無ければ生成のまま） |

材質の番号表は体と武器で共通（上の表に、武器・はめる物が使う 16〜21 を足す）:

| 番号 | 材質 | 番号 | 材質 |
|---|---|---|---|
| 16 | 鉄 | 19 | 青い革（読み込むと赤い革と同じ扱い） |
| 17 | 木 | 20 | 詰め物 |
| 18 | 赤い革 | 21 | 唇 |

仕組み: `WeaponVoxelModel.ToVox` / `FromVox` / `Parts`、`WeaponVoxelOverrides`（`Shipped`・`Load`・`For`）、`Weapon.Build(kind, leather, overrides)`（直しがあり大きさが同じならそれを使う）、
`MagicaVox` の `VoxModel.Position`（nTRN の `_t`）。テスト: `tests/MiniRing.Tests/WeaponVoxelModelFileTests.cs`。
