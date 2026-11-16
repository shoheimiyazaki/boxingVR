# cmatune punch:1b（作業指示 18、2026-11-16）

設定: 世代 36、λ 12、標本 16、20 秒、種 1、守りの組 Block・StepBack（固定）、ドリルの最後の比べ直し n = 96、試合 n = 48。壁時計 46.8 分 + 試合 n = 48 の回し直し約 6 分。予算 14208 回。生の状態: `docs/arena/cma/cma_punch_1b.*`。

## 本体の記録（`cmatune` の出力。試合の確認は n = 48 で回し直した版）

設定: 世代 36、λ 12、初めの σ 0.15000000596046448、1 候補の標本 16、20 秒、種 1、並列 既定。受け手と基準は champion（champion-v1 ではない）。

## punch:1b

1 標本 = 1 つの種で、受け手の守り（Block・StepBack）を全部回したもの。点 = Σ（頭と胴に与えたダメージ − 打ち返されたダメージ）/ 秒。受け手は champion の構えと守りの表、守りは固定、合図 0.04 s、打ち返しあり、間合い 1.15。

遺伝子（9 次元）: Lateral [0, 0.3]・Low [0, 0.6]・Overshoot [0, 0.55]・Duration [0.2, 0.44]・HeadShiftX [-0.08, 0.16]・HeadShiftY [-0.4, -0.23]・HeadShiftZ [0, 0.24]・StepIn [0.4, 1]・Sweep [0, 0.3]。

（docs\arena\cma\cma_punch_1b.state.json の状態から世代 36 の続き）

| 世代 | σ | 最良 | 平均 | 最悪 | 平均 m の値 |
|---:|---:|---:|---:|---:|---|
| 1 | 0.1500 | 10.495 | 7.373 | 5.169 | Lateral=0.033 Low=0.311 Overshoot=0.299 Duration=0.329 HeadShiftX=0.023 HeadShiftY=-0.347 HeadShiftZ=0.106 StepIn=0.716 Sweep=0.021 |
| 2 | 0.1366 | 10.578 | 8.079 | 2.130 | Lateral=0.037 Low=0.268 Overshoot=0.305 Duration=0.321 HeadShiftX=0.04 HeadShiftY=-0.348 HeadShiftZ=0.107 StepIn=0.713 Sweep=0.021 |
| 3 | 0.1235 | 10.795 | 9.440 | 8.025 | Lateral=0.019 Low=0.241 Overshoot=0.238 Duration=0.325 HeadShiftX=0.037 HeadShiftY=-0.337 HeadShiftZ=0.102 StepIn=0.711 Sweep=0.022 |
| 4 | 0.1124 | 12.304 | 10.151 | 6.307 | Lateral=0.033 Low=0.25 Overshoot=0.24 Duration=0.343 HeadShiftX=0.017 HeadShiftY=-0.345 HeadShiftZ=0.094 StepIn=0.744 Sweep=0.023 |
| 5 | 0.1087 | 9.690 | 8.150 | 4.851 | Lateral=0.038 Low=0.215 Overshoot=0.251 Duration=0.342 HeadShiftX=0.033 HeadShiftY=-0.348 HeadShiftZ=0.105 StepIn=0.751 Sweep=0.009 |
| 6 | 0.0984 | 11.223 | 9.709 | 6.374 | Lateral=0.009 Low=0.24 Overshoot=0.238 Duration=0.338 HeadShiftX=0.024 HeadShiftY=-0.353 HeadShiftZ=0.113 StepIn=0.75 Sweep=0.018 |
| 7 | 0.0892 | 11.054 | 9.413 | 7.441 | Lateral=0.017 Low=0.226 Overshoot=0.245 Duration=0.339 HeadShiftX=0.039 HeadShiftY=-0.349 HeadShiftZ=0.113 StepIn=0.748 Sweep=0.022 |
| 8 | 0.0779 | 14.273 | 9.490 | 7.202 | Lateral=0.009 Low=0.249 Overshoot=0.253 Duration=0.35 HeadShiftX=0.038 HeadShiftY=-0.34 HeadShiftZ=0.092 StepIn=0.746 Sweep=0.033 |
| 9 | 0.0760 | 12.294 | 9.481 | 7.378 | Lateral=0.001 Low=0.245 Overshoot=0.266 Duration=0.345 HeadShiftX=0.035 HeadShiftY=-0.321 HeadShiftZ=0.097 StepIn=0.78 Sweep=0.024 |
| 10 | 0.0809 | 11.191 | 9.671 | 5.653 | Lateral=0.008 Low=0.219 Overshoot=0.288 Duration=0.339 HeadShiftX=0.039 HeadShiftY=-0.325 HeadShiftZ=0.097 StepIn=0.766 Sweep=0.039 |
| 11 | 0.0793 | 11.085 | 9.866 | 8.006 | Lateral=0.002 Low=0.243 Overshoot=0.243 Duration=0.333 HeadShiftX=0.022 HeadShiftY=-0.333 HeadShiftZ=0.084 StepIn=0.768 Sweep=0.021 |
| 12 | 0.0750 | 11.651 | 9.293 | 5.327 | Lateral=0.013 Low=0.251 Overshoot=0.244 Duration=0.345 HeadShiftX=0.007 HeadShiftY=-0.332 HeadShiftZ=0.102 StepIn=0.763 Sweep=0.039 |
| 13 | 0.0722 | 10.720 | 9.825 | 8.655 | Lateral=0.029 Low=0.209 Overshoot=0.225 Duration=0.349 HeadShiftX=0.001 HeadShiftY=-0.338 HeadShiftZ=0.096 StepIn=0.791 Sweep=0.026 |
| 14 | 0.0739 | 12.472 | 9.828 | 8.047 | Lateral=0.008 Low=0.25 Overshoot=0.237 Duration=0.34 HeadShiftX=0.014 HeadShiftY=-0.333 HeadShiftZ=0.098 StepIn=0.754 Sweep=0.037 |
| 15 | 0.0637 | 11.598 | 10.154 | 8.076 | Lateral=0.011 Low=0.234 Overshoot=0.234 Duration=0.345 HeadShiftX=0.019 HeadShiftY=-0.333 HeadShiftZ=0.101 StepIn=0.727 Sweep=0.045 |
| 16 | 0.0596 | 11.770 | 10.436 | 8.649 | Lateral=0.009 Low=0.234 Overshoot=0.235 Duration=0.343 HeadShiftX=0.012 HeadShiftY=-0.339 HeadShiftZ=0.113 StepIn=0.73 Sweep=0.034 |
| 17 | 0.0558 | 11.590 | 10.133 | 8.292 | Lateral=0.009 Low=0.229 Overshoot=0.25 Duration=0.342 HeadShiftX=0.015 HeadShiftY=-0.345 HeadShiftZ=0.109 StepIn=0.733 Sweep=0.046 |
| 18 | 0.0522 | 12.140 | 10.084 | 9.066 | Lateral=0.001 Low=0.221 Overshoot=0.25 Duration=0.338 HeadShiftX=0.014 HeadShiftY=-0.341 HeadShiftZ=0.107 StepIn=0.752 Sweep=0.051 |
| 19 | 0.0496 | 12.243 | 10.672 | 9.517 | Lateral=0.003 Low=0.238 Overshoot=0.269 Duration=0.337 HeadShiftX=0.02 HeadShiftY=-0.34 HeadShiftZ=0.1 StepIn=0.746 Sweep=0.049 |
| 20 | 0.0467 | 12.721 | 10.213 | 8.292 | Lateral=0.012 Low=0.238 Overshoot=0.271 Duration=0.346 HeadShiftX=0.029 HeadShiftY=-0.334 HeadShiftZ=0.101 StepIn=0.752 Sweep=0.043 |
| 21 | 0.0474 | 13.001 | 10.051 | 8.804 | Lateral=0.017 Low=0.237 Overshoot=0.263 Duration=0.345 HeadShiftX=0.023 HeadShiftY=-0.339 HeadShiftZ=0.101 StepIn=0.73 Sweep=0.038 |
| 22 | 0.0460 | 11.685 | 10.145 | 8.873 | Lateral=0.008 Low=0.244 Overshoot=0.261 Duration=0.34 HeadShiftX=0.014 HeadShiftY=-0.342 HeadShiftZ=0.102 StepIn=0.744 Sweep=0.033 |
| 23 | 0.0420 | 14.243 | 10.798 | 9.192 | Lateral=0.013 Low=0.241 Overshoot=0.291 Duration=0.338 HeadShiftX=0.012 HeadShiftY=-0.341 HeadShiftZ=0.114 StepIn=0.744 Sweep=0.04 |
| 24 | 0.0450 | 12.130 | 10.501 | 8.470 | Lateral=0.016 Low=0.224 Overshoot=0.3 Duration=0.34 HeadShiftX=0.018 HeadShiftY=-0.339 HeadShiftZ=0.112 StepIn=0.754 Sweep=0.028 |
| 25 | 0.0461 | 11.490 | 10.141 | 8.747 | Lateral=0.02 Low=0.209 Overshoot=0.306 Duration=0.34 HeadShiftX=0.013 HeadShiftY=-0.343 HeadShiftZ=0.118 StepIn=0.728 Sweep=0.028 |
| 26 | 0.0500 | 12.084 | 9.974 | 7.586 | Lateral=0.036 Low=0.217 Overshoot=0.304 Duration=0.334 HeadShiftX=0.016 HeadShiftY=-0.339 HeadShiftZ=0.121 StepIn=0.728 Sweep=0.027 |
| 27 | 0.0522 | 12.732 | 10.713 | 8.545 | Lateral=0.025 Low=0.205 Overshoot=0.31 Duration=0.333 HeadShiftX=0.025 HeadShiftY=-0.336 HeadShiftZ=0.115 StepIn=0.72 Sweep=0.028 |
| 28 | 0.0507 | 11.865 | 10.970 | 9.508 | Lateral=0.013 Low=0.186 Overshoot=0.309 Duration=0.33 HeadShiftX=0.021 HeadShiftY=-0.338 HeadShiftZ=0.116 StepIn=0.728 Sweep=0.036 |
| 29 | 0.0499 | 11.921 | 10.447 | 8.650 | Lateral=0.012 Low=0.201 Overshoot=0.293 Duration=0.325 HeadShiftX=0.019 HeadShiftY=-0.337 HeadShiftZ=0.112 StepIn=0.725 Sweep=0.042 |
| 30 | 0.0473 | 11.653 | 10.238 | 8.750 | Lateral=0.009 Low=0.208 Overshoot=0.285 Duration=0.324 HeadShiftX=0.02 HeadShiftY=-0.334 HeadShiftZ=0.116 StepIn=0.741 Sweep=0.055 |
| 31 | 0.0475 | 12.811 | 10.992 | 8.931 | Lateral=0.016 Low=0.207 Overshoot=0.277 Duration=0.331 HeadShiftX=0.014 HeadShiftY=-0.333 HeadShiftZ=0.118 StepIn=0.763 Sweep=0.047 |
| 32 | 0.0463 | 11.825 | 10.631 | 8.318 | Lateral=0.01 Low=0.198 Overshoot=0.265 Duration=0.329 HeadShiftX=0.012 HeadShiftY=-0.338 HeadShiftZ=0.117 StepIn=0.756 Sweep=0.049 |
| 33 | 0.0428 | 11.304 | 9.558 | 7.910 | Lateral=0.012 Low=0.187 Overshoot=0.248 Duration=0.326 HeadShiftX=0.014 HeadShiftY=-0.332 HeadShiftZ=0.116 StepIn=0.771 Sweep=0.057 |
| 34 | 0.0444 | 11.800 | 10.312 | 8.589 | Lateral=0.02 Low=0.196 Overshoot=0.244 Duration=0.337 HeadShiftX=0.01 HeadShiftY=-0.329 HeadShiftZ=0.114 StepIn=0.778 Sweep=0.053 |
| 35 | 0.0458 | 11.424 | 9.905 | 8.785 | Lateral=0.014 Low=0.186 Overshoot=0.245 Duration=0.331 HeadShiftX=0.019 HeadShiftY=-0.331 HeadShiftZ=0.114 StepIn=0.759 Sweep=0.035 |
| 36 | 0.0449 | 11.919 | 10.337 | 9.076 | Lateral=0.007 Low=0.189 Overshoot=0.264 Duration=0.333 HeadShiftX=0.014 HeadShiftY=-0.329 HeadShiftZ=0.11 StepIn=0.75 Sweep=0.018 |

**世代の点は「その世代の種での比べ」で、世代の間では比べられない**（世代ごとに種を変えるので、点の水準が世代で動く）。止めた理由: 世代の数に届いた。

### 最後の比べ直し（使わなかった種・固定の n）

始めの値（champion）と、分布の平均 m（最良の個体ではない）を同じ種で対にした差の点: n=96 平均の差 +0.126 ± 0.638（95% 区間 -1.140〜1.392）符号検定 p=0.7596（+46/-50/=0）ρ=0.04 → 差なし（A = m、B = 始めの値、95% 区間）。

→ 改善とは言えない（信頼区間が 0 を含む）。

始めの値: Lateral=0 Low=0.3 Overshoot=0.25 Duration=0.32 HeadShiftX=0.04 HeadShiftY=-0.35 HeadShiftZ=0.12 StepIn=0.7 Sweep=0

m の値: Lateral=0.007 Low=0.189 Overshoot=0.264 Duration=0.333 HeadShiftX=0.014 HeadShiftY=-0.329 HeadShiftZ=0.11 StepIn=0.75 Sweep=0.018

### 試合でも悪くならないか（champion の技表のその技だけを替えたスタイル 対 元の champion）

相手 boxer・outboxer・swarmer・counter・slugger・jabber × 両コーナー、45 秒、n = 48（点は `MatchScore.Of` の 1 標本ぶん。A = 替えたスタイル、B = champion）: n=48 平均の差 -0.438 ± 0.540（95% 区間 -1.523〜0.648）符号検定 p=0.6655（+22/-26/=0）ρ=0.28 → 差なし

→ 試合では差が言えない（区間が 0 を含む）。悪くなったとも言えない。


### 予算（`DefenseDrill.Run`・`DefenseDrill.Punch`・`MeasureBaseline` を呼んだ回数 × 秒数。キャッシュで呼ばなかった分は数えない）

14208 回、合計 284160 秒（シミュレーション上。最後の比べ直しのドリルの分を含み、試合の確認は含まない）。このプロセスの壁時計 6.1 分。

候補（分布の平均 m）の本: `docs\arena\cma\cma_punch_1b_m48.json`、champion に重ねたスタイル: `docs\arena\cma\cma_punch_1b_m48.style.json`。


全体の壁時計: 6.1 分（このプロセスの分。16 コア）。

## 試合の確認の n = 16 の最初の結果（同じ 36 世代の状態、同じ m。種 k = 0..15 は n = 48 の先頭と同じ）

相手 boxer・outboxer・swarmer・counter・slugger・jabber × 両コーナー、45 秒、n = 16（点は `MatchScore.Of` の 1 標本ぶん。A = 替えたスタイル、B = champion）: n=16 平均の差 -1.500 ± 1.009（95% 区間 -3.652〜0.652）符号検定 p=0.8036（+7/-9/=0）ρ=0.02 → 差なし

## 見た目

ドリルで改善と出なかったので撮っていない（動画・コマ無し）。分布は元の値のまま（`StepIn` 0.7 → 0.75 など、σ が 0.045 まで縮んで収束した）。
