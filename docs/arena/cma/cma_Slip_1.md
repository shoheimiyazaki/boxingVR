# cmatune（CMA-ES でドリルの点を上げる）

設定: 世代 16、λ 12、初めの σ 0.15000000596046448、1 候補の標本 16、20 秒、種 1、並列 既定。受け手と基準は champion（champion-v1 ではない）。

## defense:Slip

1 標本 = 1 つの種で、守り Slip が受け持つ拳（Straight）を打たせたドリルの差の点（守らない受け手の被ダメージ − 被ダメージ + 0.25 × 打ち返し）の和。受け手は champion の守りの表。

遺伝子（14 次元）: HeadX [-0.6, 0.6]・HeadY [-0.7, 0.2]・HeadZ [-0.5, 0.4]・Duration [0.15, 0.7]・Move [-1, 1]・Step [-1, 1]・Rate [6, 60]・HandsUp [0, 1]・GloveWidth [0, 0.35]・GloveHeight [-0.3, 0.2]・GloveForward [0.05, 0.5]・BodyDrop [0.2, 0.9]・TempleWidth [0, 0.4]・GloveAnchor [0, 1]。

| 世代 | σ | 最良 | 平均 | 最悪 | 平均 m の値 |
|---:|---:|---:|---:|---:|---|
| 1 | 0.1500 | 4.354 | 3.349 | 0.739 | HeadX=0.589 HeadY=-0.624 HeadZ=-0.307 Duration=0.273 Move=-0.134 Step=0.982 Rate=58.192 HandsUp=0.257 GloveWidth=0.206 GloveHeight=-0.041 GloveForward=0.372 BodyDrop=0.595 TempleWidth=0.162 GloveAnchor=0.07 |
| 2 | 0.1394 | 4.586 | -0.624 | -5.189 | HeadX=0.571 HeadY=-0.664 HeadZ=-0.323 Duration=0.206 Move=-0.409 Step=0.959 Rate=45.981 HandsUp=0.399 GloveWidth=0.189 GloveHeight=-0.063 GloveForward=0.38 BodyDrop=0.636 TempleWidth=0.191 GloveAnchor=0.041 |
| 3 | 0.1441 | 4.718 | 1.775 | -2.592 | HeadX=0.536 HeadY=-0.648 HeadZ=-0.332 Duration=0.229 Move=-0.561 Step=0.866 Rate=39.635 HandsUp=0.408 GloveWidth=0.202 GloveHeight=-0.026 GloveForward=0.386 BodyDrop=0.624 TempleWidth=0.182 GloveAnchor=0.049 |
| 4 | 0.1484 | 2.854 | 0.272 | -2.002 | HeadX=0.467 HeadY=-0.664 HeadZ=-0.316 Duration=0.306 Move=-0.407 Step=0.885 Rate=49.516 HandsUp=0.364 GloveWidth=0.216 GloveHeight=-0.112 GloveForward=0.33 BodyDrop=0.589 TempleWidth=0.176 GloveAnchor=0.157 |
| 5 | 0.1423 | 1.957 | 1.114 | -0.333 | HeadX=0.384 HeadY=-0.689 HeadZ=-0.399 Duration=0.272 Move=-0.403 Step=0.835 Rate=45.658 HandsUp=0.377 GloveWidth=0.215 GloveHeight=-0.14 GloveForward=0.36 BodyDrop=0.563 TempleWidth=0.159 GloveAnchor=0.083 |
| 6 | 0.1361 | 0.786 | 0.275 | -0.543 | HeadX=0.477 HeadY=-0.669 HeadZ=-0.28 Duration=0.275 Move=-0.136 Step=0.983 Rate=42.96 HandsUp=0.277 GloveWidth=0.219 GloveHeight=-0.197 GloveForward=0.417 BodyDrop=0.598 TempleWidth=0.203 GloveAnchor=0.031 |
| 7 | 0.1335 | 6.048 | 1.491 | -2.380 | HeadX=0.463 HeadY=-0.699 HeadZ=-0.324 Duration=0.285 Move=-0.043 Step=0.946 Rate=42.004 HandsUp=0.305 GloveWidth=0.203 GloveHeight=-0.174 GloveForward=0.377 BodyDrop=0.6 TempleWidth=0.235 GloveAnchor=0.051 |
| 8 | 0.1264 | 11.739 | 4.979 | 0.781 | HeadX=0.559 HeadY=-0.696 HeadZ=-0.363 Duration=0.26 Move=0.22 Step=0.896 Rate=40.579 HandsUp=0.42 GloveWidth=0.213 GloveHeight=-0.116 GloveForward=0.351 BodyDrop=0.62 TempleWidth=0.218 GloveAnchor=0.172 |
| 9 | 0.1250 | 7.946 | 4.558 | -0.532 | HeadX=0.54 HeadY=-0.65 HeadZ=-0.303 Duration=0.308 Move=0.237 Step=0.996 Rate=32.021 HandsUp=0.499 GloveWidth=0.199 GloveHeight=-0.172 GloveForward=0.332 BodyDrop=0.615 TempleWidth=0.241 GloveAnchor=0.279 |
| 10 | 0.1307 | 8.014 | 5.123 | 2.289 | HeadX=0.562 HeadY=-0.659 HeadZ=-0.41 Duration=0.306 Move=0.516 Step=0.92 Rate=34.675 HandsUp=0.404 GloveWidth=0.204 GloveHeight=-0.231 GloveForward=0.368 BodyDrop=0.632 TempleWidth=0.248 GloveAnchor=0.212 |
| 11 | 0.1334 | 2.845 | 0.913 | -1.567 | HeadX=0.535 HeadY=-0.586 HeadZ=-0.417 Duration=0.335 Move=0.613 Step=0.974 Rate=28.998 HandsUp=0.447 GloveWidth=0.201 GloveHeight=-0.264 GloveForward=0.356 BodyDrop=0.593 TempleWidth=0.251 GloveAnchor=0.056 |
| 12 | 0.1375 | 2.417 | 1.742 | 0.170 | HeadX=0.446 HeadY=-0.622 HeadZ=-0.442 Duration=0.405 Move=0.792 Step=0.847 Rate=38.275 HandsUp=0.497 GloveWidth=0.204 GloveHeight=-0.229 GloveForward=0.395 BodyDrop=0.543 TempleWidth=0.248 GloveAnchor=0.184 |
| 13 | 0.1402 | 11.324 | 7.393 | 4.684 | HeadX=0.425 HeadY=-0.634 HeadZ=-0.362 Duration=0.379 Move=0.83 Step=0.936 Rate=44.42 HandsUp=0.553 GloveWidth=0.186 GloveHeight=-0.217 GloveForward=0.409 BodyDrop=0.554 TempleWidth=0.23 GloveAnchor=0.098 |
| 14 | 0.1380 | 1.579 | 0.459 | -0.857 | HeadX=0.52 HeadY=-0.679 HeadZ=-0.479 Duration=0.374 Move=0.751 Step=0.857 Rate=44.028 HandsUp=0.477 GloveWidth=0.236 GloveHeight=-0.258 GloveForward=0.369 BodyDrop=0.548 TempleWidth=0.223 GloveAnchor=0.021 |
| 15 | 0.1332 | 5.071 | 2.816 | 0.921 | HeadX=0.493 HeadY=-0.645 HeadZ=-0.492 Duration=0.406 Move=0.822 Step=0.93 Rate=50.069 HandsUp=0.398 GloveWidth=0.202 GloveHeight=-0.205 GloveForward=0.37 BodyDrop=0.605 TempleWidth=0.249 GloveAnchor=0.085 |
| 16 | 0.1258 | 12.915 | 7.928 | 0.964 | HeadX=0.479 HeadY=-0.695 HeadZ=-0.441 Duration=0.389 Move=0.899 Step=0.903 Rate=46.928 HandsUp=0.381 GloveWidth=0.195 GloveHeight=-0.165 GloveForward=0.406 BodyDrop=0.63 TempleWidth=0.261 GloveAnchor=0.009 |

**世代の点は「その世代の種での比べ」で、世代の間では比べられない**（世代ごとに種を変えるので、点の水準が世代で動く）。止めた理由: 世代の数に届いた。

### 最後の比べ直し（使わなかった種・固定の n）

始めの値（champion）と、分布の平均 m（最良の個体ではない）を同じ種で対にした差の点: n=192 平均の差 +3.156 ± 1.009（95% 区間 1.166〜5.145）符号検定 p=0.0000（+51/-9/=132）ρ=0.80 → A が上（A = m、B = 始めの値、95% 区間）。

→ **改善**（信頼区間の下限が 0 より上）。

始めの値: HeadX=0.6 HeadY=-0.6 HeadZ=-0.15 Duration=0.3 Move=-0.2 Step=1 Rate=60 HandsUp=0.09 GloveWidth=0.21 GloveHeight=-0.054 GloveForward=0.357 BodyDrop=0.547 TempleWidth=0.158 GloveAnchor=0

m の値: HeadX=0.479 HeadY=-0.695 HeadZ=-0.441 Duration=0.389 Move=0.899 Step=0.903 Rate=46.928 HandsUp=0.381 GloveWidth=0.195 GloveHeight=-0.165 GloveForward=0.406 BodyDrop=0.63 TempleWidth=0.261 GloveAnchor=0.009

### 試合でも悪くならないか（champion の技表のその技だけを替えたスタイル 対 元の champion）

（行わない: --match-n が 2 未満）


### 予算（`DefenseDrill.Run`・`DefenseDrill.Punch`・`MeasureBaseline` を呼んだ回数 × 秒数。キャッシュで呼ばなかった分は数えない）

4648 回、合計 92960 秒（シミュレーション上。最後の比べ直しのドリルの分を含み、試合の確認は含まない）。このプロセスの壁時計 19.4 分。

候補（分布の平均 m）の本: `docs\arena\cma\cma_Slip_1.json`、champion に重ねたスタイル: `docs\arena\cma\cma_Slip_1.style.json`。


全体の壁時計: 19.4 分（このプロセスの分。16 コア）。
