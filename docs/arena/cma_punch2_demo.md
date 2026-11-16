# cmatune（CMA-ES でドリルの点を上げる）

設定: 世代 3、λ 6、初めの σ 0.15000000596046448、1 候補の標本 4、20 秒、種 1、並列 既定。受け手と基準は champion（champion-v1 ではない）。

## punch:2

1 標本 = 1 つの種で、受け手の守り（Block・Slip・Duck）を全部回したもの。点 = Σ（頭と胴に与えたダメージ − 打ち返されたダメージ）/ 秒。受け手は champion の構えと守りの表、守りは固定、合図 0.04 s、打ち返しあり、間合い 1.15。

遺伝子（9 次元）: Lateral [0, 0.4]・Low [-0.3, 0.3]・Overshoot [0.05, 0.6]・Duration [0.28, 0.52]・HeadShiftX [0, 0.24]・HeadShiftY [-0.14, 0.1]・HeadShiftZ [0.02, 0.26]・StepIn [0.05, 0.65]・Sweep [0, 0.3]。

| 世代 | σ | 最良 | 平均 | 最悪 | 平均 m の値 |
|---:|---:|---:|---:|---:|---|
| 1 | 0.1500 | 5.954 | 2.706 | 0.121 | Lateral=0.051 Low=0.043 Overshoot=0.317 Duration=0.351 HeadShiftX=0.187 HeadShiftY=-0.034 HeadShiftZ=0.152 StepIn=0.263 Sweep=0.037 |
| 2 | 0.1478 | 7.127 | 4.926 | 1.265 | Lateral=0.109 Low=-0.097 Overshoot=0.414 Duration=0.303 HeadShiftX=0.22 HeadShiftY=0.014 HeadShiftZ=0.156 StepIn=0.216 Sweep=0.031 |
| 3 | 0.1575 | 8.945 | 6.221 | 4.337 | Lateral=0.139 Low=-0.122 Overshoot=0.446 Duration=0.282 HeadShiftX=0.193 HeadShiftY=0.01 HeadShiftZ=0.152 StepIn=0.221 Sweep=0.014 |

**世代の点は「その世代の種での比べ」で、世代の間では比べられない**（世代ごとに種を変えるので、点の水準が世代で動く）。止めた理由: 世代の数に届いた。

### 最後の比べ直し（使わなかった種・固定の n）

始めの値（champion）と、分布の平均 m（最良の個体ではない）を同じ種で対にした差の点: n=8 平均の差 +0.271 ± 2.716（95% 区間 -6.152〜6.694）符号検定 p=0.7266（+3/-5/=0）ρ=-0.13 → 差なし（A = m、B = 始めの値、95% 区間）。

→ 改善とは言えない（信頼区間が 0 を含む）。

始めの値: Lateral=0.1 Low=0 Overshoot=0.35 Duration=0.4 HeadShiftX=0.12 HeadShiftY=-0.02 HeadShiftZ=0.14 StepIn=0.35 Sweep=0

m の値: Lateral=0.139 Low=-0.122 Overshoot=0.446 Duration=0.282 HeadShiftX=0.193 HeadShiftY=0.01 HeadShiftZ=0.152 StepIn=0.221 Sweep=0.014

### 試合でも悪くならないか（champion の技表のその技だけを替えたスタイル 対 元の champion）

相手 boxer・outboxer・swarmer・counter・slugger・jabber × 両コーナー、20 秒、n = 2（点は `MatchScore.Of` の 1 標本ぶん。A = 替えたスタイル、B = champion）: n=2 平均の差 +1.459 ± 0.955（95% 区間 -10.674〜13.592）符号検定 p=0.5000（+2/-0/=0）ρ=1.00 → 差なし

→ 試合では差が言えない（区間が 0 を含む）。悪くなったとも言えない。


### 予算（`DefenseDrill.Run`・`DefenseDrill.Punch`・`MeasureBaseline` を呼んだ回数 × 秒数。キャッシュで呼ばなかった分は数えない）

264 回、合計 5280 秒（シミュレーション上。最後の比べ直しのドリルの分を含み、試合の確認は含まない）。このプロセスの壁時計 2.2 分。

候補（分布の平均 m）の本: `docs/arena/cma/punch2_demo.json`、champion に重ねたスタイル: `docs/arena/cma/punch2_demo.style.json`。


全体の壁時計: 2.2 分（このプロセスの分。12 コア）。

