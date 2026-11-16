# cmatune（CMA-ES でドリルの点を上げる）

設定: 世代 36、λ 12、初めの σ 0.15000000596046448、1 候補の標本 16、20 秒、種 1、並列 既定。受け手と基準は champion（champion-v1 ではない）。

## punch:3

1 標本 = 1 つの種で、受け手の守り（Block・Weave・Duck）を全部回したもの。点 = Σ（頭と胴に与えたダメージ − 打ち返されたダメージ）/ 秒。受け手は champion の構えと守りの表、守りは固定、合図 0.04 s、打ち返しあり、間合い 1.15。

遺伝子（9 次元）: Lateral [0.55, 1.15]・Low [-0.6, 0]・Overshoot [0, 0.35]・Duration [0.3, 0.54]・HeadShiftX [-0.22, 0.02]・HeadShiftY [-0.15, 0.09]・HeadShiftZ [-0.07, 0.17]・StepIn [0, 0.4]・Sweep [0, 0.6]。

| 世代 | σ | 最良 | 平均 | 最悪 | 平均 m の値 |
|---:|---:|---:|---:|---:|---|
| 1 | 0.1500 | -2.760 | -5.610 | -8.617 | Lateral=0.804 Low=-0.226 Overshoot=0.073 Duration=0.41 HeadShiftX=-0.095 HeadShiftY=-0.044 HeadShiftZ=0.064 StepIn=0.108 Sweep=0.267 |
| 2 | 0.1370 | -2.826 | -5.310 | -7.118 | Lateral=0.786 Low=-0.324 Overshoot=0.087 Duration=0.402 HeadShiftX=-0.131 HeadShiftY=-0.089 HeadShiftZ=0.089 StepIn=0.154 Sweep=0.338 |
| 3 | 0.1482 | -1.648 | -3.989 | -6.124 | Lateral=0.752 Low=-0.345 Overshoot=0.099 Duration=0.411 HeadShiftX=-0.135 HeadShiftY=-0.114 HeadShiftZ=0.092 StepIn=0.181 Sweep=0.377 |
| 4 | 0.1596 | -1.372 | -4.270 | -6.676 | Lateral=0.812 Low=-0.353 Overshoot=0.114 Duration=0.419 HeadShiftX=-0.15 HeadShiftY=-0.143 HeadShiftZ=0.111 StepIn=0.207 Sweep=0.371 |
| 5 | 0.1703 | -0.007 | -3.331 | -6.942 | Lateral=0.885 Low=-0.402 Overshoot=0.132 Duration=0.386 HeadShiftX=-0.166 HeadShiftY=-0.129 HeadShiftZ=0.138 StepIn=0.214 Sweep=0.452 |
| 6 | 0.1891 | -2.476 | -3.604 | -6.348 | Lateral=0.894 Low=-0.44 Overshoot=0.166 Duration=0.385 HeadShiftX=-0.187 HeadShiftY=-0.143 HeadShiftZ=0.119 StepIn=0.139 Sweep=0.478 |
| 7 | 0.1953 | -1.119 | -2.911 | -4.960 | Lateral=0.906 Low=-0.462 Overshoot=0.23 Duration=0.407 HeadShiftX=-0.209 HeadShiftY=-0.138 HeadShiftZ=0.125 StepIn=0.173 Sweep=0.476 |
| 8 | 0.1997 | -1.931 | -3.235 | -5.096 | Lateral=0.969 Low=-0.402 Overshoot=0.208 Duration=0.386 HeadShiftX=-0.194 HeadShiftY=-0.118 HeadShiftZ=0.128 StepIn=0.086 Sweep=0.505 |
| 9 | 0.2027 | -1.401 | -3.407 | -6.426 | Lateral=0.837 Low=-0.397 Overshoot=0.257 Duration=0.341 HeadShiftX=-0.209 HeadShiftY=-0.123 HeadShiftZ=0.128 StepIn=0.114 Sweep=0.524 |
| 10 | 0.2051 | -1.263 | -2.353 | -3.874 | Lateral=0.789 Low=-0.499 Overshoot=0.274 Duration=0.312 HeadShiftX=-0.204 HeadShiftY=-0.147 HeadShiftZ=0.152 StepIn=0.124 Sweep=0.529 |
| 11 | 0.2148 | -1.143 | -2.642 | -4.068 | Lateral=0.821 Low=-0.591 Overshoot=0.248 Duration=0.307 HeadShiftX=-0.197 HeadShiftY=-0.126 HeadShiftZ=0.151 StepIn=0.12 Sweep=0.584 |
| 12 | 0.2195 | 0.199 | -1.292 | -2.754 | Lateral=0.817 Low=-0.584 Overshoot=0.259 Duration=0.332 HeadShiftX=-0.173 HeadShiftY=-0.138 HeadShiftZ=0.155 StepIn=0.102 Sweep=0.582 |
| 13 | 0.2096 | -0.226 | -2.061 | -3.446 | Lateral=0.752 Low=-0.525 Overshoot=0.302 Duration=0.322 HeadShiftX=-0.184 HeadShiftY=-0.125 HeadShiftZ=0.165 StepIn=0.157 Sweep=0.564 |
| 14 | 0.2001 | -0.235 | -2.411 | -4.778 | Lateral=0.691 Low=-0.573 Overshoot=0.33 Duration=0.315 HeadShiftX=-0.185 HeadShiftY=-0.113 HeadShiftZ=0.169 StepIn=0.129 Sweep=0.587 |
| 15 | 0.1917 | 1.222 | -1.368 | -3.325 | Lateral=0.558 Low=-0.546 Overshoot=0.328 Duration=0.302 HeadShiftX=-0.19 HeadShiftY=-0.089 HeadShiftZ=0.159 StepIn=0.101 Sweep=0.599 |
| 16 | 0.1959 | 1.389 | -1.428 | -3.816 | Lateral=0.582 Low=-0.462 Overshoot=0.321 Duration=0.32 HeadShiftX=-0.183 HeadShiftY=-0.103 HeadShiftZ=0.164 StepIn=0.128 Sweep=0.55 |
| 17 | 0.1960 | 0.019 | -1.121 | -2.210 | Lateral=0.564 Low=-0.557 Overshoot=0.344 Duration=0.323 HeadShiftX=-0.176 HeadShiftY=-0.119 HeadShiftZ=0.162 StepIn=0.115 Sweep=0.497 |
| 18 | 0.1966 | 1.947 | -0.614 | -3.015 | Lateral=0.58 Low=-0.5 Overshoot=0.299 Duration=0.325 HeadShiftX=-0.189 HeadShiftY=-0.123 HeadShiftZ=0.164 StepIn=0.14 Sweep=0.452 |
| 19 | 0.1960 | 0.239 | -1.646 | -3.389 | Lateral=0.595 Low=-0.528 Overshoot=0.274 Duration=0.315 HeadShiftX=-0.202 HeadShiftY=-0.095 HeadShiftZ=0.114 StepIn=0.147 Sweep=0.528 |
| 20 | 0.1939 | 0.467 | -1.393 | -3.541 | Lateral=0.575 Low=-0.556 Overshoot=0.226 Duration=0.324 HeadShiftX=-0.203 HeadShiftY=-0.083 HeadShiftZ=0.066 StepIn=0.101 Sweep=0.528 |
| 21 | 0.2127 | -1.015 | -1.890 | -2.669 | Lateral=0.561 Low=-0.592 Overshoot=0.235 Duration=0.323 HeadShiftX=-0.213 HeadShiftY=-0.1 HeadShiftZ=0.065 StepIn=0.089 Sweep=0.51 |
| 22 | 0.2094 | -0.522 | -2.175 | -4.355 | Lateral=0.606 Low=-0.592 Overshoot=0.31 Duration=0.333 HeadShiftX=-0.195 HeadShiftY=-0.129 HeadShiftZ=0.128 StepIn=0.109 Sweep=0.496 |
| 23 | 0.1843 | 0.372 | -1.689 | -4.561 | Lateral=0.592 Low=-0.597 Overshoot=0.338 Duration=0.329 HeadShiftX=-0.195 HeadShiftY=-0.109 HeadShiftZ=0.153 StepIn=0.101 Sweep=0.411 |
| 24 | 0.1727 | 2.289 | -0.736 | -3.742 | Lateral=0.595 Low=-0.547 Overshoot=0.346 Duration=0.302 HeadShiftX=-0.19 HeadShiftY=-0.107 HeadShiftZ=0.167 StepIn=0.152 Sweep=0.429 |
| 25 | 0.1676 | 0.761 | -0.649 | -2.541 | Lateral=0.63 Low=-0.449 Overshoot=0.312 Duration=0.329 HeadShiftX=-0.186 HeadShiftY=-0.15 HeadShiftZ=0.162 StepIn=0.156 Sweep=0.411 |
| 26 | 0.1708 | 0.991 | -1.304 | -3.619 | Lateral=0.647 Low=-0.474 Overshoot=0.305 Duration=0.343 HeadShiftX=-0.183 HeadShiftY=-0.132 HeadShiftZ=0.155 StepIn=0.125 Sweep=0.451 |
| 27 | 0.1648 | 1.172 | -2.103 | -4.571 | Lateral=0.638 Low=-0.48 Overshoot=0.339 Duration=0.342 HeadShiftX=-0.18 HeadShiftY=-0.115 HeadShiftZ=0.129 StepIn=0.145 Sweep=0.435 |
| 28 | 0.1547 | 0.769 | -1.318 | -3.215 | Lateral=0.587 Low=-0.504 Overshoot=0.297 Duration=0.337 HeadShiftX=-0.188 HeadShiftY=-0.136 HeadShiftZ=0.142 StepIn=0.182 Sweep=0.394 |
| 29 | 0.1448 | 2.177 | -0.375 | -2.160 | Lateral=0.595 Low=-0.524 Overshoot=0.281 Duration=0.341 HeadShiftX=-0.178 HeadShiftY=-0.121 HeadShiftZ=0.169 StepIn=0.201 Sweep=0.371 |
| 30 | 0.1386 | 2.520 | -1.113 | -5.178 | Lateral=0.592 Low=-0.503 Overshoot=0.257 Duration=0.333 HeadShiftX=-0.174 HeadShiftY=-0.122 HeadShiftZ=0.166 StepIn=0.216 Sweep=0.355 |
| 31 | 0.1310 | 0.944 | -0.606 | -1.951 | Lateral=0.593 Low=-0.502 Overshoot=0.273 Duration=0.308 HeadShiftX=-0.171 HeadShiftY=-0.13 HeadShiftZ=0.161 StepIn=0.185 Sweep=0.364 |
| 32 | 0.1229 | 1.490 | 0.389 | -0.331 | Lateral=0.64 Low=-0.55 Overshoot=0.243 Duration=0.307 HeadShiftX=-0.177 HeadShiftY=-0.117 HeadShiftZ=0.159 StepIn=0.207 Sweep=0.359 |
| 33 | 0.1203 | 2.085 | -0.093 | -1.694 | Lateral=0.599 Low=-0.552 Overshoot=0.27 Duration=0.302 HeadShiftX=-0.192 HeadShiftY=-0.148 HeadShiftZ=0.155 StepIn=0.212 Sweep=0.349 |
| 34 | 0.1143 | 3.222 | 0.281 | -0.669 | Lateral=0.564 Low=-0.579 Overshoot=0.268 Duration=0.306 HeadShiftX=-0.215 HeadShiftY=-0.147 HeadShiftZ=0.164 StepIn=0.199 Sweep=0.331 |
| 35 | 0.1145 | 2.468 | 0.753 | -1.116 | Lateral=0.556 Low=-0.572 Overshoot=0.279 Duration=0.309 HeadShiftX=-0.217 HeadShiftY=-0.146 HeadShiftZ=0.167 StepIn=0.248 Sweep=0.326 |
| 36 | 0.1162 | 3.382 | 1.578 | 0.457 | Lateral=0.559 Low=-0.56 Overshoot=0.302 Duration=0.301 HeadShiftX=-0.204 HeadShiftY=-0.145 HeadShiftZ=0.154 StepIn=0.264 Sweep=0.292 |

**世代の点は「その世代の種での比べ」で、世代の間では比べられない**（世代ごとに種を変えるので、点の水準が世代で動く）。止めた理由: 世代の数に届いた。

### 最後の比べ直し（使わなかった種・固定の n）

始めの値（champion）と、分布の平均 m（最良の個体ではない）を同じ種で対にした差の点: n=96 平均の差 +8.601 ± 0.450（95% 区間 7.707〜9.495）符号検定 p=0.0000（+93/-3/=0）ρ=0.01 → A が上（A = m、B = 始めの値、95% 区間）。

→ **改善**（信頼区間の下限が 0 より上）。

始めの値: Lateral=0.85 Low=-0.3 Overshoot=0.05 Duration=0.42 HeadShiftX=-0.1 HeadShiftY=-0.03 HeadShiftZ=0.05 StepIn=0.1 Sweep=0.3

m の値: Lateral=0.559 Low=-0.56 Overshoot=0.302 Duration=0.301 HeadShiftX=-0.204 HeadShiftY=-0.145 HeadShiftZ=0.154 StepIn=0.264 Sweep=0.292

### 試合でも悪くならないか（champion の技表のその技だけを替えたスタイル 対 元の champion）

相手 boxer・outboxer・swarmer・counter・slugger・jabber × 両コーナー、45 秒、n = 16（点は `MatchScore.Of` の 1 標本ぶん。A = 替えたスタイル、B = champion）: n=16 平均の差 -0.875 ± 0.724（95% 区間 -2.419〜0.669）符号検定 p=0.2101（+5/-11/=0）ρ=0.59 → 差なし

→ 試合では差が言えない（区間が 0 を含む）。悪くなったとも言えない。


### 予算（`DefenseDrill.Run`・`DefenseDrill.Punch`・`MeasureBaseline` を呼んだ回数 × 秒数。キャッシュで呼ばなかった分は数えない）

21312 回、合計 426240 秒（シミュレーション上。最後の比べ直しのドリルの分を含み、試合の確認は含まない）。このプロセスの壁時計 63.2 分。

候補（分布の平均 m）の本: `docs\arena\cma\cma_punch_3.json`、champion に重ねたスタイル: `docs\arena\cma\cma_punch_3.style.json`。


全体の壁時計: 63.2 分（このプロセスの分。16 コア）。
