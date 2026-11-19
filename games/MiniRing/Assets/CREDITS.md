# 部屋の素材の出典

どれも CC0（パブリックドメイン）。表記の義務は無いが、出典を残す。

| 素材 | 出典 | ライセンス | 使い方 |
|---|---|---|---|
| `Furniture/*.obj`・`*.mtl` | [Kenney Furniture Kit 2.0](https://kenney.nl/assets/furniture-kit)（Kenney） | CC0（`Furniture/License.txt`） | そのまま（OBJ format から 11 点） |
| `Textures/WoodFloor041.png` | [ambientCG WoodFloor041](https://ambientcg.com/view?id=WoodFloor041) | CC0 | 1K の Color を 512 px に縮小 |
| `Textures/Plaster001.png` | [ambientCG Plaster001](https://ambientcg.com/view?id=Plaster001) | CC0 | 同上 |
| `Textures/Fabric026.png` | [ambientCG Fabric026](https://ambientcg.com/view?id=Fabric026) | CC0 | 同上 |
| `Textures/Wood049.png` | [ambientCG Wood049](https://ambientcg.com/view?id=Wood049) | CC0 | 同上 |

元のファイル（zip）はリポジトリに入れない（`assets-src/`、.gitignore）。

# 体の中身（骨・臓器・筋肉・動脈・気管）の出典

`games/MiniRing/Voxels/boxer.vox` の胴・腕・脚・頭の中身は、人の CT の分割から作った（`tools/ct-body/`、`docs/plans/CT_BODY_PLAN.md`）。外形（ボクサーの形）と顔は手で作ったもの。

| 使ったもの | 出典 | ライセンス | 使い方 |
|---|---|---|---|
| 胴・腕・頭（症例 s1029） | Wasserthal J. et al., *TotalSegmentator dataset v2.0.1*（部分集合）, University Hospital Basel, Zenodo, [DOI 10.5281/zenodo.10047263](https://doi.org/10.5281/zenodo.10047263) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | 分割（117 のラベル）と CT の値を、5 mm のボクセルへ多数決で落とし、材質に置き換えた（改変あり） |
| 腿・すね・足（症例 s0774） | Wasserthal J. et al., *TotalSegmentator dataset v2.0.1*, University Hospital Basel, Zenodo, [DOI 10.5281/zenodo.10047292](https://doi.org/10.5281/zenodo.10047292) | CC BY 4.0 | 同上 |

元の CT はリポジトリに入れない（`tools/ct-body/fetch.py` が取り方を持つ）。
