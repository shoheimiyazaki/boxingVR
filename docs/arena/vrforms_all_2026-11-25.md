## 教科書の型 対 VR の振りの型（刀の剣士どうし、種 1〜8 × 色を入れ替えて 16 試合 × 60 秒、死んだら終わり）

差し替えた型（`docs/vrmotion/` の順 1）: tsuki ← katana_tsuki_1_20261124_230124.vrmotion・kesa ← katana_kesa_1_20261124_225935.vrmotion・gyaku-kesa ← katana_gyaku-kesa_1_20261124_225410.vrmotion・nuki-do ← katana_nuki-do_1_20261124_222815.vrmotion・kiriage ← katana_kiriage_1_20261124_222041.vrmotion・harai-men ← katana_harai-men_1_20261124_225429.vrmotion・hiki-men ← katana_hiki-men_1_20261124_230200.vrmotion・shomen ← katana_shomen_1_20261124_230322.vrmotion・migi-nagi ← katana_migi-nagi_1_20261124_230410.vrmotion・hidari-nagi ← katana_hidari-nagi_1_20261124_225506.vrmotion・gyaku-kiriage ← katana_gyaku-kiriage_1_20261124_224032.vrmotion・gyakufu ← katana_gyakufu_1_20261124_224104.vrmotion・kote ← katana_kote_1_20261124_230024.vrmotion・kote-men ← katana_kote-men_1_20261124_223103.vrmotion・do-tsuki ← katana_do-tsuki_1_20261124_222208.vrmotion・katate-tsuki ← katana_katate-tsuki_1_20261124_224145.vrmotion・tsuka-ate ← katana_tsuka-ate_1_20261124_225730.vrmotion・oshi-giri ← katana_oshi-giri_1_20261124_223210.vrmotion・hiki-do ← katana_hiki-do_1_20261124_230108.vrmotion

| | VR の振り | 教科書 |
|---|---:|---:|
| 失ったセル（平均） | 690 | 528 |
| 死んだ | 4 | 2 |
| 相手より失ったセルが少なかった試合 | 7 | 9 |
| 技を出した数（平均） | 3.3 | 9.4 |

自分を斬る量（構えて動かない相手 `GuardHold` と 60 秒。相手は打たないので、剣士が失ったセルは自分の刃による）:

| | VR の振り | 教科書 |
|---|---:|---:|
| 自分が失ったセル（平均） | 61 | 205 |
| 自分を斬った試合 | 5 / 8 | 8 / 8 |
| 動かない相手が失ったセル（平均） | 834 | 664 |

技ごとの出した数（全試合の合計）:

| 型 | VR の振り | 教科書 | VR の間合い [m] | 教科書の間合い [m] |
|---|---:|---:|---:|---:|
| tsuki | 4 | 2 | 1.30 | 1.36 |
| kesa | 0 | 10 | 0.88 | 1.11 |
| gyaku-kesa | 2 | 4 | 1.01 | 1.11 |
| nuki-do | 4 | 8 | 1.07 | 1.36 |
| kiriage | 6 | 13 | 1.42 | 1.03 |
| harai-men | 8 | 9 | 1.06 | 1.29 |
| hiki-men | 0 | 0 | 0.88 | 0.88 |
| shomen | 2 | 3 | 1.01 | 1.33 |
| migi-nagi | 0 | 3 | 0.93 | 1.29 |
| hidari-nagi | 0 | 0 | 1.12 | 1.29 |
| gyaku-kiriage | 0 | 0 | 1.42 | 1.28 |
| gyakufu | 4 | 5 | 1.10 | 1.24 |
| kote | 3 | 8 | 1.37 | 1.18 |
| kote-men | 1 | 3 | 1.30 | 1.39 |
| do-tsuki | 10 | 6 | 1.69 | 1.33 |
| katate-tsuki | 2 | 1 | 1.75 | 1.44 |
| tsuka-ate | 0 | 5 | 1.11 | 0.40 |
| oshi-giri | 2 | 27 | 1.08 | 0.74 |
| hiki-do | 4 | 30 | 0.78 | 0.74 |
| mae-geri | 1 | 13 | 0.87 | 0.87 |

| 種 | VR の色 | VR が失ったセル | 教科書が失ったセル | 死 |
|---:|---|---:|---:|---|
| 1 | 赤 | 1374 | 0 | VR |
| 1 | 青 | 61 | 1451 | 教科書 |
| 2 | 赤 | 93 | 1204 |  |
| 2 | 青 | 328 | 6 | VR |
| 3 | 赤 | 456 | 42 | VR |
| 3 | 青 | 1570 | 43 |  |
| 4 | 赤 | 1018 | 1433 |  |
| 4 | 青 | 251 | 211 |  |
| 5 | 赤 | 1479 | 1353 |  |
| 5 | 青 | 1978 | 510 |  |
| 6 | 赤 | 606 | 576 | VR |
| 6 | 青 | 238 | 326 |  |
| 7 | 赤 | 1470 | 46 |  |
| 7 | 青 | 34 | 568 | 教科書 |
| 8 | 赤 | 1 | 3 |  |
| 8 | 青 | 82 | 677 |  |
