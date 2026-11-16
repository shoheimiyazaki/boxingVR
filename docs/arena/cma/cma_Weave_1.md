# cmatune（CMA-ES でドリルの点を上げる）

設定: 世代 16、λ 12、初めの σ 0.15000000596046448、1 候補の標本 16、20 秒、種 1、並列 既定。受け手と基準は champion（champion-v1 ではない）。

## defense:Weave

1 標本 = 1 つの種で、守り Weave が受け持つ拳（Hook・Straight）を打たせたドリルの差の点（守らない受け手の被ダメージ − 被ダメージ + 0.25 × 打ち返し）の和。受け手は champion の守りの表。

遺伝子（15 次元）: HeadX [-0.6, 0.6]・HeadY [-0.7, 0.2]・HeadZ [-0.5, 0.4]・EndX [-0.6, 0.6]・Duration [0.15, 0.7]・Move [-1, 1]・Step [-1, 1]・Rate [6, 60]・HandsUp [0, 1]・GloveWidth [0, 0.35]・GloveHeight [-0.3, 0.2]・GloveForward [0.05, 0.5]・BodyDrop [0.2, 0.9]・TempleWidth [0, 0.4]・GloveAnchor [0, 1]。

| 世代 | σ | 最良 | 平均 | 最悪 | 平均 m の値 |
|---:|---:|---:|---:|---:|---|
| 1 | 0.1500 | 37.178 | 32.287 | 21.659 | HeadX=-0.449 HeadY=-0.675 HeadZ=0.363 EndX=0.452 Duration=0.352 Move=0.42 Step=0.931 Rate=58.589 HandsUp=0.069 GloveWidth=0.069 GloveHeight=0.033 GloveForward=0.386 BodyDrop=0.57 TempleWidth=0.371 GloveAnchor=0.252 |
| 2 | 0.1354 | 36.473 | 28.320 | 10.102 | HeadX=-0.489 HeadY=-0.671 HeadZ=0.396 EndX=0.453 Duration=0.342 Move=0.303 Step=0.982 Rate=58.124 HandsUp=0.124 GloveWidth=0.111 GloveHeight=0.059 GloveForward=0.365 BodyDrop=0.581 TempleWidth=0.358 GloveAnchor=0.253 |
| 3 | 0.1253 | 26.726 | 22.212 | 18.894 | HeadX=-0.412 HeadY=-0.634 HeadZ=0.388 EndX=0.393 Duration=0.34 Move=0.53 Step=0.961 Rate=59.346 HandsUp=0.157 GloveWidth=0.086 GloveHeight=0.1 GloveForward=0.356 BodyDrop=0.59 TempleWidth=0.323 GloveAnchor=0.202 |
| 4 | 0.1198 | 35.617 | 28.032 | 21.655 | HeadX=-0.266 HeadY=-0.607 HeadZ=0.283 EndX=0.212 Duration=0.391 Move=0.624 Step=0.964 Rate=57.595 HandsUp=0.172 GloveWidth=0.072 GloveHeight=0.053 GloveForward=0.352 BodyDrop=0.633 TempleWidth=0.322 GloveAnchor=0.082 |
| 5 | 0.1241 | 38.921 | 32.387 | 10.552 | HeadX=-0.24 HeadY=-0.642 HeadZ=0.227 EndX=0.225 Duration=0.334 Move=0.687 Step=0.81 Rate=59.808 HandsUp=0.096 GloveWidth=0.058 GloveHeight=0.043 GloveForward=0.324 BodyDrop=0.601 TempleWidth=0.312 GloveAnchor=0.055 |
| 6 | 0.1253 | 33.467 | 25.668 | 18.704 | HeadX=-0.15 HeadY=-0.661 HeadZ=0.318 EndX=0.274 Duration=0.265 Move=0.587 Step=0.963 Rate=55.983 HandsUp=0.049 GloveWidth=0.083 GloveHeight=0.024 GloveForward=0.308 BodyDrop=0.541 TempleWidth=0.336 GloveAnchor=0.026 |
| 7 | 0.1254 | 37.116 | 29.681 | 17.968 | HeadX=-0.037 HeadY=-0.676 HeadZ=0.256 EndX=0.213 Duration=0.238 Move=0.89 Step=0.984 Rate=55.907 HandsUp=0.031 GloveWidth=0.071 GloveHeight=-0.032 GloveForward=0.294 BodyDrop=0.55 TempleWidth=0.322 GloveAnchor=0.109 |
| 8 | 0.1297 | 41.672 | 34.014 | 20.412 | HeadX=-0.034 HeadY=-0.695 HeadZ=0.212 EndX=0.359 Duration=0.23 Move=0.774 Step=0.951 Rate=54.76 HandsUp=0.001 GloveWidth=0.104 GloveHeight=-0.039 GloveForward=0.281 BodyDrop=0.526 TempleWidth=0.275 GloveAnchor=0.012 |
| 9 | 0.1323 | 45.509 | 35.940 | 26.509 | HeadX=0.068 HeadY=-0.522 HeadZ=0.275 EndX=0.395 Duration=0.183 Move=0.97 Step=0.874 Rate=47.752 HandsUp=0.002 GloveWidth=0.121 GloveHeight=0.051 GloveForward=0.247 BodyDrop=0.531 TempleWidth=0.309 GloveAnchor=0.048 |
| 10 | 0.1410 | 47.300 | 37.923 | 31.178 | HeadX=-0.017 HeadY=-0.445 HeadZ=0.257 EndX=0.383 Duration=0.189 Move=0.85 Step=0.967 Rate=53.735 HandsUp=0.029 GloveWidth=0.156 GloveHeight=0.027 GloveForward=0.207 BodyDrop=0.479 TempleWidth=0.338 GloveAnchor=0.093 |
| 11 | 0.1451 | 32.359 | 24.495 | 12.804 | HeadX=0.007 HeadY=-0.424 HeadZ=0.12 EndX=0.314 Duration=0.191 Move=0.882 Step=0.993 Rate=54.614 HandsUp=0.105 GloveWidth=0.142 GloveHeight=0.065 GloveForward=0.194 BodyDrop=0.538 TempleWidth=0.385 GloveAnchor=0.211 |
| 12 | 0.1513 | 31.550 | 24.496 | 15.727 | HeadX=0.011 HeadY=-0.49 HeadZ=0.21 EndX=0.43 Duration=0.219 Move=0.812 Step=0.956 Rate=58.493 HandsUp=0.043 GloveWidth=0.093 GloveHeight=0.005 GloveForward=0.122 BodyDrop=0.585 TempleWidth=0.39 GloveAnchor=0.168 |
| 13 | 0.1534 | 48.297 | 35.161 | 27.290 | HeadX=-0.018 HeadY=-0.573 HeadZ=0.267 EndX=0.569 Duration=0.268 Move=0.807 Step=0.838 Rate=59.639 HandsUp=0.033 GloveWidth=0.065 GloveHeight=0.07 GloveForward=0.127 BodyDrop=0.633 TempleWidth=0.378 GloveAnchor=0.183 |
| 14 | 0.1536 | 31.348 | 28.028 | 25.066 | HeadX=0.022 HeadY=-0.48 HeadZ=0.176 EndX=0.543 Duration=0.247 Move=0.904 Step=0.721 Rate=55.651 HandsUp=0.016 GloveWidth=0.079 GloveHeight=0.153 GloveForward=0.133 BodyDrop=0.646 TempleWidth=0.368 GloveAnchor=0.341 |
| 15 | 0.1561 | 36.034 | 25.175 | 18.890 | HeadX=-0.057 HeadY=-0.585 HeadZ=0.171 EndX=0.573 Duration=0.306 Move=0.861 Step=0.988 Rate=56.937 HandsUp=0.017 GloveWidth=0.073 GloveHeight=0.16 GloveForward=0.091 BodyDrop=0.607 TempleWidth=0.398 GloveAnchor=0.268 |
| 16 | 0.1518 | 38.369 | 26.834 | 16.720 | HeadX=0.045 HeadY=-0.511 HeadZ=0.108 EndX=0.468 Duration=0.292 Move=0.337 Step=0.973 Rate=59.263 HandsUp=0.042 GloveWidth=0.084 GloveHeight=0.145 GloveForward=0.11 BodyDrop=0.615 TempleWidth=0.389 GloveAnchor=0.309 |

**世代の点は「その世代の種での比べ」で、世代の間では比べられない**（世代ごとに種を変えるので、点の水準が世代で動く）。止めた理由: 世代の数に届いた。

### 最後の比べ直し（使わなかった種・固定の n）

始めの値（champion）と、分布の平均 m（最良の個体ではない）を同じ種で対にした差の点: n=192 平均の差 -0.193 ± 1.893（95% 区間 -3.927〜3.542）符号検定 p=0.8287（+98/-94/=0）ρ=0.61 → 差なし（A = m、B = 始めの値、95% 区間）。

→ 改善とは言えない（信頼区間が 0 を含む）。

始めの値: HeadX=-0.5 HeadY=-0.7 HeadZ=0.4 EndX=0.6 Duration=0.4 Move=0.4 Step=0.8 Rate=60 HandsUp=0 GloveWidth=0.067 GloveHeight=0.022 GloveForward=0.373 BodyDrop=0.603 TempleWidth=0.4 GloveAnchor=0.271

m の値: HeadX=0.045 HeadY=-0.511 HeadZ=0.108 EndX=0.468 Duration=0.292 Move=0.337 Step=0.973 Rate=59.263 HandsUp=0.042 GloveWidth=0.084 GloveHeight=0.145 GloveForward=0.11 BodyDrop=0.615 TempleWidth=0.389 GloveAnchor=0.309

### 試合でも悪くならないか（champion の技表のその技だけを替えたスタイル 対 元の champion）

（行わない: --match-n が 2 未満）


### 予算（`DefenseDrill.Run`・`DefenseDrill.Punch`・`MeasureBaseline` を呼んだ回数 × 秒数。キャッシュで呼ばなかった分は数えない）

9134 回、合計 182680 秒（シミュレーション上。最後の比べ直しのドリルの分を含み、試合の確認は含まない）。このプロセスの壁時計 41.5 分。

候補（分布の平均 m）の本: `docs\arena\cma\cma_Weave_1.json`、champion に重ねたスタイル: `docs\arena\cma\cma_Weave_1.style.json`。


全体の壁時計: 41.5 分（このプロセスの分。16 コア）。
