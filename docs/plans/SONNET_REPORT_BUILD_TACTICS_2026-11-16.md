完了

# 作業報告: 体格に応じた戦い方（2026-11-16、Sonnet）

## マーカス・アキラが強かった理由（基準 `docs/arena/build_tactics_baseline.md`、種 1）
- マーカス（champion、腕比 1.22、重さ 1.86）: 自分だけ届く帯に 19 %、相手だけ届く帯に 0 %（相手の腕の外から打てる）。ただし勝率 41 %（9 位）、打撃は与 9.4/被 19.2、命中 33 %、スタミナ平均 0.22（重い体は先に尽きる）。勝ち点は軽い相手から。この表では「勝てている」より「距離の優位はあるが手数・スタミナで落とす」。
- アキラ（karate、腕比 0.89）: 相手だけ届く帯に 24 % いるのに勝率 52 %（6 位）。与量 161/被量 92、命中 56 %、スタミナ 0.86。善戦の理由は命中の質と省エネ（距離の優位ではない）。

## 入れた層と式（`Bots/BuildRead.cs`、LogicVersion 30、PhysicsVersion 据え置き）
`FighterSense` 末尾に `SelfBuild`/`OpponentBuild`、`Fighter.Build`、`BodyBuild.MassRatio = H³M²`。ρ = 自分の腕/相手の腕、η = 身長比、μ = 重さ比。全部 `BotStyle` の共通の値（`Validated()` で範囲検証）。キャラ名では分けず、体格が同じなら全倍率が 1。
- `BuildStandOff`: 立ち位置（普段・打ちに入る・休む・ストップジャブ・EngageRange・keepOut）× ρ^(−k)
- `BuildInside`: 打つ間合い × min(ρ,1)^k
- `BuildPush`: 攻めの間隔の縮む速さ × μ^k、打って離れる確率 × μ^(−k)
- `BuildDuck`: 潜る守り（Duck・Weave・Roll）の重み × η^(−k)、引く守り（Sway・StepBack）× η^k
- `BuildAimBody`: 胴の重みの合計に k × max(0, −ln η) を足す

## 係数の採否
| 係数 | 試した値（種 1、72 試合の A/B 勝点率） | 採否 |
|---|---|---|
| BuildStandOff | 0.5 → 38.9 %、1.0 → 59.0 % | 採る 0.8（距離が体格に沿って動く） |
| BuildInside | 0.5 → 54.2 %、1.0 → 49.3 % | 採る 0.5 |
| BuildPush | 0.3 → 46.5 %、0.6 → 50.7 %、組に足して種 7 で 51.7 % | 採らない（既定 0。スタミナが先に尽きる疑い。機構は残す） |
| BuildDuck | 0.5 → 47.9 %、1.0 → 48.6 % | 採る 0.5（点に出ない・害なし。効果は未確認） |
| BuildAimBody | 2 → 52.8 %、4 → 52.1 % | 採る 2（同上） |
使っていない種 7（144 試合）: StandOff+Inside 56.6 %、+Duck+AimBody 53.8 %、+Push 51.7 %。1 回の A/B の揺らぎは約 ±6 %で、単独の係数は点では区別できない（StandOff の 0.5 と 1.0 が非単調なのは揺らぎ）。

## 前 → 後（全員に入れた総当たり、種 1 と種 5、各 132 試合。表は docs/BOT_PDCA.md、生は docs/arena/build_tactics_{baseline,after}*.md）
- 種 1 の勝率 前 → 後: vikram 84→82、ramon 59→70、gaston 59→48、jin 59→57、sean 57→43、akira 52→30、konrad 50→34、dmitri 45→41、marcus 41→45、joao 36→59、yoandry 32→50、albert 25→41。
- 種 5: vikram 82→73、jin 82→64、yoandry 57→68、akira 55→34、konrad 55→50、ramon 52→52、joao 45→34、gaston 43→52、marcus 41→41、dmitri 36→27、sean 30→52、albert 23→52。
- 距離: アキラの相手だけ届く帯 24→15 %（種 5: 24→17）、エル・トロの内側 4→8 %（6→12）。両側が層を使うと長い腕の「自分だけ届く帯」は動かない（マーカス 19→18 %、種 5: 21→16 %）。層を片方だけに入れる A/B ではマーカス 22→35 %。
- 拳のキャラ 9 人 A/B（種 7）はどのキャラも崩れていない（各 7〜13 勝/16）。

## 崩れた所（基準 (c)）と未確認
- アキラが両方の種で 52→30 %、55→34 %（各 22 試合、揺らぎ約 ±10 %）。アキラだけに層を入れて入れない全員と戦うと 50.8→50.8 %（66 試合）で不変、アキラだけ入れず他全員に入れると他が 55.3 %。原因はアキラ自身の層でなく、層を使う腕の長い相手らしい（揺らぎ内で確定していない）。
- 基準 (a)（長い腕ほど自分だけ届く帯に）は、全員が層を使う総当たりでは満たさない（相殺）。短い腕側と A/B では満たす。
- 剣士・こんぼう（jin・konrad・sean）は対象外（表には出した）。続き: 空手の蹴り（脚の届く長さ）を層に入れる、剣士・こんぼうへの展開。
- 動画は 40 秒のコマ送り（4 秒ごとのコンタクトシート）で見ただけ。距離の違いを秒単位では測っていない。

## 教育（キャラごと）で詰めると良さそうな係数・キャラ（表から言える範囲）
- アキラ（短い腕・空手）: `BuildStandOff`・`BuildInside` を弱める、または蹴りの間合いを別に教える。
- マーカス（重い・長い腕）: `BuildPush` は逆効果の疑い（スタミナ 0.22）。`BuildStandOff` を強めて「自分だけ届く帯」を使い切る価値はある。
- エル・トロ（短い腕・軽い）: 内側 4→12 % と効いている。`BuildInside` を強める余地。
- ドミトリー（最重量）: 手数 11/試合で低い。スタミナと合わせて教育で調整。

## テスト
全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`: 643 件合格・0 失敗（README を 621→643 に）。新規 `BuildReadTests` 7 件（重さの式、同体格で倍率 1、長短の向き、重い/高い/低い、同体格でリプレイと同じ試合、同スタイルで長腕と短腕の距離が逆向き）。`ReplayTests` も通過。

## 動画（`media_tmp/`、git の外）
- `dmitri_ramon_before.mp4` / `dmitri_ramon_after.mp4`、`marcus_akira_before.mp4` / `marcus_akira_after.mp4`（種 4、40 秒、速さ 1。前 = `--set BuildStandOff=0,BuildInside=0,BuildPush=0,BuildDuck=0,BuildAimBody=0`）。
- 画面右上のカウントダウンで 38〜34 秒、26〜22 秒あたり。dmitri vs ramon は前はドミトリーがコーナーで押されてダウン、後はリング中央で近い打ち合いが増える（結果はどちらもエル・トロ勝ち）。marcus vs akira はどちらもマーカス勝ち（前 146–138、後 109–54）。差はコマでは小さい。
- 生の記録のコンタクトシート `media_tmp/sheet_*.png`。

## 道具・ファイル
`games/MiniRing.Arena/RosterLeague.cs`（`rosterleague --chars --bouts --seconds --seed --referee --parallel --set --ab on --layered id --plain id --out`、`video --set`）。基準表 `docs/arena/build_tactics_baseline.md`、A/B と後の表 `docs/arena/build_tactics_*.md`。文書: `docs/BOT_PDCA.md`・`docs/BOTS.md`・`docs/CHARACTERS.md`・`BoxerBot.LogicVersion` のコメント。コミットはしていない。
