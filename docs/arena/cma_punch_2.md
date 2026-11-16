# cmatune punch:2（作業指示 18、2026-11-16）

設定: 世代 36（最初の 4 世代は 18 世代の設定で回し、止めて 36 に直して `--resume` で続けた。再開は続けて回した場合と一致する: `CmaTuneTests`）、λ 12、標本 16、20 秒、種 1、守りの組 Block・Slip・Duck（固定）、ドリルの最後の比べ直し n = 96、試合 n = 48（45 秒）。壁時計: 52.6 分（再開後のプロセス。最初の約 6 分を足して約 59 分、推定）+ 試合 n = 48 の回し直し 6.5 分。予算 21312 回（最後の比べ直しを含む）。生の状態: `docs/arena/cma/cma_punch_2.*`。

## 本体の記録（`cmatune` の出力。試合の確認は n = 48 で回し直した版）

設定: 世代 36、λ 12、初めの σ 0.15000000596046448、1 候補の標本 16、20 秒、種 1、並列 既定。受け手と基準は champion（champion-v1 ではない）。

## punch:2

1 標本 = 1 つの種で、受け手の守り（Block・Slip・Duck）を全部回したもの。点 = Σ（頭と胴に与えたダメージ − 打ち返されたダメージ）/ 秒。受け手は champion の構えと守りの表、守りは固定、合図 0.04 s、打ち返しあり、間合い 1.15。

遺伝子（9 次元）: Lateral [0, 0.4]・Low [-0.3, 0.3]・Overshoot [0.05, 0.6]・Duration [0.28, 0.52]・HeadShiftX [0, 0.24]・HeadShiftY [-0.14, 0.1]・HeadShiftZ [0.02, 0.26]・StepIn [0.05, 0.65]・Sweep [0, 0.3]。

（docs\arena\cma\cma_punch_2.state.json の状態から世代 36 の続き）

| 世代 | σ | 最良 | 平均 | 最悪 | 平均 m の値 |
|---:|---:|---:|---:|---:|---|
| 1 | 0.1500 | 4.877 | 2.564 | 0.116 | Lateral=0.064 Low=0.012 Overshoot=0.288 Duration=0.356 HeadShiftX=0.161 HeadShiftY=-0.023 HeadShiftZ=0.15 StepIn=0.303 Sweep=0.03 |
| 2 | 0.1512 | 6.101 | 4.172 | 2.493 | Lateral=0.047 Low=0.059 Overshoot=0.251 Duration=0.351 HeadShiftX=0.162 HeadShiftY=-0.021 HeadShiftZ=0.164 StepIn=0.277 Sweep=0.044 |
| 3 | 0.1520 | 6.524 | 3.334 | 0.791 | Lateral=0.038 Low=0.084 Overshoot=0.23 Duration=0.35 HeadShiftX=0.181 HeadShiftY=0.006 HeadShiftZ=0.185 StepIn=0.303 Sweep=0.064 |
| 4 | 0.1563 | 10.231 | 4.931 | 0.914 | Lateral=0.036 Low=0.157 Overshoot=0.171 Duration=0.352 HeadShiftX=0.221 HeadShiftY=0.009 HeadShiftZ=0.228 StepIn=0.256 Sweep=0.074 |
| 5 | 0.1766 | 9.388 | 5.606 | 3.153 | Lateral=0.055 Low=0.247 Overshoot=0.117 Duration=0.321 HeadShiftX=0.236 HeadShiftY=0.012 HeadShiftZ=0.218 StepIn=0.257 Sweep=0.105 |
| 6 | 0.2116 | 9.609 | 6.473 | 1.765 | Lateral=0.059 Low=0.29 Overshoot=0.179 Duration=0.346 HeadShiftX=0.22 HeadShiftY=-0.014 HeadShiftZ=0.252 StepIn=0.246 Sweep=0.112 |
| 7 | 0.2148 | 11.878 | 7.632 | 5.027 | Lateral=0.077 Low=0.241 Overshoot=0.192 Duration=0.309 HeadShiftX=0.212 HeadShiftY=-0.018 HeadShiftZ=0.229 StepIn=0.206 Sweep=0.116 |
| 8 | 0.2246 | 12.004 | 7.322 | 2.771 | Lateral=0.052 Low=0.279 Overshoot=0.071 Duration=0.292 HeadShiftX=0.196 HeadShiftY=-0.029 HeadShiftZ=0.251 StepIn=0.241 Sweep=0.138 |
| 9 | 0.2238 | 13.589 | 7.108 | 3.830 | Lateral=0.037 Low=0.257 Overshoot=0.082 Duration=0.327 HeadShiftX=0.23 HeadShiftY=-0.05 HeadShiftZ=0.228 StepIn=0.322 Sweep=0.15 |
| 10 | 0.2140 | 10.994 | 7.616 | 3.796 | Lateral=0.025 Low=0.283 Overshoot=0.098 Duration=0.325 HeadShiftX=0.217 HeadShiftY=-0.061 HeadShiftZ=0.244 StepIn=0.414 Sweep=0.175 |
| 11 | 0.2153 | 12.059 | 7.727 | 2.241 | Lateral=0.091 Low=0.292 Overshoot=0.123 Duration=0.298 HeadShiftX=0.223 HeadShiftY=-0.076 HeadShiftZ=0.189 StepIn=0.549 Sweep=0.197 |
| 12 | 0.2370 | 11.665 | 9.074 | 4.584 | Lateral=0.074 Low=0.211 Overshoot=0.246 Duration=0.29 HeadShiftX=0.201 HeadShiftY=-0.085 HeadShiftZ=0.166 StepIn=0.544 Sweep=0.201 |
| 13 | 0.2570 | 12.801 | 8.297 | 4.586 | Lateral=0.011 Low=0.22 Overshoot=0.305 Duration=0.311 HeadShiftX=0.144 HeadShiftY=-0.035 HeadShiftZ=0.118 StepIn=0.618 Sweep=0.195 |
| 14 | 0.2832 | 11.961 | 7.388 | 3.286 | Lateral=0.01 Low=0.297 Overshoot=0.341 Duration=0.321 HeadShiftX=0.149 HeadShiftY=-0.049 HeadShiftZ=0.128 StepIn=0.524 Sweep=0.196 |
| 15 | 0.2794 | 11.938 | 7.786 | 2.242 | Lateral=0.024 Low=0.246 Overshoot=0.406 Duration=0.327 HeadShiftX=0.22 HeadShiftY=-0.077 HeadShiftZ=0.124 StepIn=0.559 Sweep=0.2 |
| 16 | 0.2723 | 12.618 | 9.137 | 2.876 | Lateral=0.048 Low=0.268 Overshoot=0.249 Duration=0.356 HeadShiftX=0.22 HeadShiftY=-0.056 HeadShiftZ=0.143 StepIn=0.551 Sweep=0.138 |
| 17 | 0.2404 | 10.288 | 7.725 | 3.672 | Lateral=0.08 Low=0.299 Overshoot=0.211 Duration=0.314 HeadShiftX=0.238 HeadShiftY=-0.036 HeadShiftZ=0.148 StepIn=0.601 Sweep=0.135 |
| 18 | 0.2170 | 12.302 | 7.298 | 4.389 | Lateral=0.051 Low=0.221 Overshoot=0.242 Duration=0.32 HeadShiftX=0.219 HeadShiftY=-0.039 HeadShiftZ=0.139 StepIn=0.644 Sweep=0.146 |
| 19 | 0.1985 | 12.353 | 8.053 | 5.046 | Lateral=0.052 Low=0.284 Overshoot=0.255 Duration=0.324 HeadShiftX=0.159 HeadShiftY=-0.028 HeadShiftZ=0.16 StepIn=0.633 Sweep=0.142 |
| 20 | 0.1986 | 12.074 | 9.148 | 6.771 | Lateral=0.085 Low=0.294 Overshoot=0.192 Duration=0.356 HeadShiftX=0.104 HeadShiftY=-0.038 HeadShiftZ=0.213 StepIn=0.621 Sweep=0.151 |
| 21 | 0.2128 | 10.969 | 9.716 | 4.678 | Lateral=0.093 Low=0.248 Overshoot=0.229 Duration=0.353 HeadShiftX=0.091 HeadShiftY=-0.05 HeadShiftZ=0.252 StepIn=0.615 Sweep=0.212 |
| 22 | 0.2271 | 12.560 | 8.529 | 3.467 | Lateral=0.089 Low=0.275 Overshoot=0.302 Duration=0.327 HeadShiftX=0.108 HeadShiftY=-0.039 HeadShiftZ=0.236 StepIn=0.599 Sweep=0.213 |
| 23 | 0.2299 | 13.340 | 9.822 | 6.866 | Lateral=0.059 Low=0.264 Overshoot=0.26 Duration=0.332 HeadShiftX=0.132 HeadShiftY=0.006 HeadShiftZ=0.231 StepIn=0.479 Sweep=0.188 |
| 24 | 0.2234 | 11.649 | 7.500 | 4.036 | Lateral=0.083 Low=0.291 Overshoot=0.356 Duration=0.317 HeadShiftX=0.16 HeadShiftY=-0.037 HeadShiftZ=0.249 StepIn=0.614 Sweep=0.186 |
| 25 | 0.2011 | 13.500 | 9.439 | 4.412 | Lateral=0.071 Low=0.238 Overshoot=0.381 Duration=0.345 HeadShiftX=0.169 HeadShiftY=-0.058 HeadShiftZ=0.254 StepIn=0.622 Sweep=0.21 |
| 26 | 0.1846 | 14.255 | 9.944 | 5.887 | Lateral=0.059 Low=0.299 Overshoot=0.285 Duration=0.337 HeadShiftX=0.158 HeadShiftY=-0.068 HeadShiftZ=0.229 StepIn=0.545 Sweep=0.207 |
| 27 | 0.1714 | 15.289 | 10.974 | 7.741 | Lateral=0.022 Low=0.266 Overshoot=0.295 Duration=0.352 HeadShiftX=0.145 HeadShiftY=-0.065 HeadShiftZ=0.251 StepIn=0.568 Sweep=0.2 |
| 28 | 0.1597 | 12.695 | 10.945 | 5.998 | Lateral=0.019 Low=0.282 Overshoot=0.246 Duration=0.38 HeadShiftX=0.088 HeadShiftY=-0.081 HeadShiftZ=0.209 StepIn=0.603 Sweep=0.186 |
| 29 | 0.1667 | 12.570 | 9.443 | 5.385 | Lateral=0.017 Low=0.288 Overshoot=0.224 Duration=0.367 HeadShiftX=0.092 HeadShiftY=-0.055 HeadShiftZ=0.2 StepIn=0.61 Sweep=0.188 |
| 30 | 0.1610 | 14.031 | 10.976 | 7.450 | Lateral=0.021 Low=0.281 Overshoot=0.237 Duration=0.367 HeadShiftX=0.1 HeadShiftY=-0.034 HeadShiftZ=0.212 StepIn=0.635 Sweep=0.183 |
| 31 | 0.1472 | 16.047 | 11.646 | 6.108 | Lateral=0.041 Low=0.271 Overshoot=0.207 Duration=0.364 HeadShiftX=0.102 HeadShiftY=-0.046 HeadShiftZ=0.197 StepIn=0.622 Sweep=0.185 |
| 32 | 0.1345 | 15.377 | 11.791 | 5.693 | Lateral=0.046 Low=0.289 Overshoot=0.253 Duration=0.367 HeadShiftX=0.129 HeadShiftY=-0.062 HeadShiftZ=0.18 StepIn=0.561 Sweep=0.192 |
| 33 | 0.1307 | 14.619 | 11.314 | 8.908 | Lateral=0.027 Low=0.29 Overshoot=0.291 Duration=0.35 HeadShiftX=0.136 HeadShiftY=-0.058 HeadShiftZ=0.216 StepIn=0.632 Sweep=0.193 |
| 34 | 0.1146 | 14.421 | 10.520 | 8.210 | Lateral=0.017 Low=0.288 Overshoot=0.261 Duration=0.367 HeadShiftX=0.105 HeadShiftY=-0.064 HeadShiftZ=0.211 StepIn=0.618 Sweep=0.204 |
| 35 | 0.1022 | 16.000 | 11.100 | 7.307 | Lateral=0.014 Low=0.286 Overshoot=0.238 Duration=0.365 HeadShiftX=0.089 HeadShiftY=-0.051 HeadShiftZ=0.197 StepIn=0.552 Sweep=0.187 |
| 36 | 0.1011 | 15.248 | 12.366 | 7.951 | Lateral=0.013 Low=0.299 Overshoot=0.217 Duration=0.37 HeadShiftX=0.084 HeadShiftY=-0.052 HeadShiftZ=0.212 StepIn=0.584 Sweep=0.179 |

**世代の点は「その世代の種での比べ」で、世代の間では比べられない**（世代ごとに種を変えるので、点の水準が世代で動く）。止めた理由: 世代の数に届いた。

### 最後の比べ直し（使わなかった種・固定の n）

始めの値（champion）と、分布の平均 m（最良の個体ではない）を同じ種で対にした差の点: n=96 平均の差 +14.499 ± 0.610（95% 区間 13.287〜15.710）符号検定 p=0.0000（+96/-0/=0）ρ=-0.08 → A が上（A = m、B = 始めの値、95% 区間）。

→ **改善**（信頼区間の下限が 0 より上）。

始めの値: Lateral=0.1 Low=0 Overshoot=0.35 Duration=0.4 HeadShiftX=0.12 HeadShiftY=-0.02 HeadShiftZ=0.14 StepIn=0.35 Sweep=0

m の値: Lateral=0.013 Low=0.299 Overshoot=0.217 Duration=0.37 HeadShiftX=0.084 HeadShiftY=-0.052 HeadShiftZ=0.212 StepIn=0.584 Sweep=0.179

### 試合でも悪くならないか（champion の技表のその技だけを替えたスタイル 対 元の champion）

相手 boxer・outboxer・swarmer・counter・slugger・jabber × 両コーナー、45 秒、n = 48（点は `MatchScore.Of` の 1 標本ぶん。A = 替えたスタイル、B = champion）: n=48 平均の差 +1.151 ± 0.567（95% 区間 0.011〜2.290）符号検定 p=0.1934（+29/-19/=0）ρ=0.15 → A が上

→ 試合でも上がった（区間が 0 より上）。


### 予算（`DefenseDrill.Run`・`DefenseDrill.Punch`・`MeasureBaseline` を呼んだ回数 × 秒数。キャッシュで呼ばなかった分は数えない）

21312 回、合計 426240 秒（シミュレーション上。最後の比べ直しのドリルの分を含み、試合の確認は含まない）。このプロセスの壁時計 6.4 分。

候補（分布の平均 m）の本: `docs\arena\cma\cma_punch_2_m48.json`、champion に重ねたスタイル: `docs\arena\cma\cma_punch_2_m48.style.json`。


全体の壁時計: 6.4 分（このプロセスの分。16 コア）。

## 試合の確認の n = 16 の最初の結果（同じ 36 世代の状態、同じ m。種 k = 0..15 は n = 48 の先頭と同じ）

相手 boxer・outboxer・swarmer・counter・slugger・jabber × 両コーナー、45 秒、n = 16（点は `MatchScore.Of` の 1 標本ぶん。A = 替えたスタイル、B = champion）: n=16 平均の差 -0.578 ± 0.882（95% 区間 -2.458〜1.302）符号検定 p=1.0000（+8/-8/=0）ρ=0.31 → 差なし

## 見た目（Claude が撮ったコマを見た所見。`docs/arena/cma_frames/punch2_Slip_{before,after}_{1..4}.png`、動画 `docs/video/cma_punch2_Slip_{before,after}.mp4`）

条件は同一: `video --drill 2 --defense Slip --style champion --counters true --cue 0.04 --range 1.15 --seed 4 --seconds 10`、before は `--attacker-style champion`、after は `cma_punch_2.style.json`。受け手に使えるのは Slip・Duck・StepBack 等（Block は動画に使えない）で、Slip を選んだ。コマは 4.9 / 5.1 / 5.2 / 5.3 秒（動画内の時刻。最初のストレートの構えから当たりまで）。
- どちらも構えからのまっすぐな右ストレートに見える。腕を振り回す・のけぞって打つ・体ごと突っ込む・フックの形になる、は最初のストレートでは見えなかった。
- 違い: before は拳が頭へ届く 4 コマ目で火花・ダメージ表示が無く（スリップが間に合い、その後「スリップからカウンター」が出て攻め手が倒れる）、after は同じ 4 コマ目で火花が出て頭に当たる（HEAD 24、受け手がダウン）。拳の出し始めの姿勢はほぼ同じで、当たるタイミング・足の出し方（`StepIn` 0.35 → 0.58）の差が結果を分ける。
- 限界: ドリルのカメラでは 2 人がほぼ密着して見え、肘・肩の形の細かな差は読めない。見たのは seed 4 の最初の 1〜2 発と、Slip の受け手だけ（Duck の受け手・他の種は見ていない）。`Low` が範囲の上端（0.3）に張り付いたのは、頭と胴の合計の点（`AttackPoint`）が低い狙いを好んだ可能性がある（動画の 1 発目は頭に当たっているので、頭への狙いが下がったようには見えない）。→ 見た目に問題は見つからなかったが、確定ではない。
