完了（採否は Claude。全テストは回していない: コードの変更は `defconfirm` の追加だけで、`dotnet build` は通った）

# 作業指示 18（P1c）報告: CMA-ES で磨いて測る（実装者: Sonnet、測定は raytrek）

詳細は `docs/arena/cma_summary.md`（まとめ）、`cma_vs_hill.md`、`cma_punch_{2,3,1b}.md`。生の出力は `docs/arena/cma/`。

## 設定・壁時計（実測）
- 節 1（守り 8 本）: 20 秒・標本 16・λ = pop = 12・16 世代、確認 n = 192。13:48〜18:16 = 4 時間 28 分。
- 節 2（拳 3 技）: 20 秒・標本 16・λ 12・36 世代、ドリルの確認 n = 96、試合 n = 48。18:16〜21:04 = 2 時間 48 分（1 度 18 → 36 世代へ直して `--resume`）+ 試合 n = 48 の回し直し 19 分。
- 動画 2 組 約 7 分。合計約 8 時間。`evo_noise.md` の推奨（40 秒・n = 48）より小さい設定（予算のため）。

## 問い 1（CMA-ES 対 (1+λ)、始めの値との差、n = 192、95 % 区間）
| | (1+λ) | CMA-ES |
|---|---|---|
| Slip・種 1 | 0.000（出発点に戻した） | +3.156（1.166〜5.145）改善 |
| Slip・種 2 | −0.950（−2.835〜0.935） | +1.973（0.285〜3.660）改善 |
| Weave・種 1 | −2.022（−5.983〜1.939） | −0.193（−3.927〜3.542） |
| Weave・種 2 | 0.000（出発点に戻した） | +5.569（2.492〜8.647）改善 |
CMA-ES の探索の予算は (1+λ) の 89〜96 %（`drilltune` の前後の表 1344 回を引いた推定）。ただし最適化する点が違い（(1+λ) は比、CMA-ES は確認と同じ差）CMA-ES に有利。8 本・20 秒・標本 16。

## 問い 2（拳）
| 技 | ドリル n = 96 | 試合 n = 16 | 試合 n = 48 |
|---|---|---|---|
| punch:2 | +14.499（13.287〜15.710）改善 | −0.578（−2.458〜1.302） | +1.151（0.011〜2.290） |
| punch:3 | +8.601（7.707〜9.495）改善 | −0.875（−2.419〜0.669） | +0.268（−0.647〜1.184） |
| punch:1b | +0.126（−1.140〜1.392）差なし | −1.500（−3.652〜0.652） | −0.438（−1.523〜0.648） |
試合で悪化は見えないが、利点も小さい（+1 点を言うには n ≈ 111）。n = 16 は符号が入れ替わるほど揺れた。

## コマ・動画（§4.7、Claude が見た）
- `docs/arena/cma_frames/punch2_Slip_{before,after}_{1..4}.png`（4.9 / 5.1 / 5.2 / 5.3 秒）、`punch3_Weave_{before,after}_{1..4}.png`（5.0 / 5.15 / 5.25 / 5.35 秒）。
- 動画: `docs/video/cma_punch2_Slip_{before,after}.mp4`、`cma_punch3_Weave_{before,after}.mp4`（13〜16 MB。縮めていない）。条件は before/after で同一（`--drill <型> --defense <Slip|Weave> --style champion --counters true --cue 0.04 --range 1.15 --seed 4 --seconds 10`、before は `--attacker-style champion`）。Block は動画に使えないので Slip（punch:2）・Weave（punch:3）。punch:1b は改善なしで撮っていない。
- 見た目の所見: punch:2 は構えからのストレートのまま（振り回し・のけぞり・突っ込み・フック化なし）、当たりが早く `StepIn` が増え、after は頭に当たり受け手がダウン、before は Slip が間に合う。punch:3 は弧のフックのまま `Duration` 0.42 → 0.30 で約 0.1 秒早い。限界: 画角が密着で細部は読めず、seed 4 の最初の 1〜2 発と 1 つの守りだけ。`Low` が範囲の端に張り付いた（punch:2 上端 0.3、punch:3 下端寄り −0.56）。

## ツールの変更（コード）
- 新規 `games/MiniRing.Arena/DefConfirm.cs` と `Program.cs` の 1 行: `defconfirm --defense Slip --book <drilltune の --out> --seed S --seconds 20 --confirm-n N`（`drilltune` の出力を `cmatune` と同じ手続き・種・点で champion と比べる）、`--export-champion f.json`（champion の守りの本。`drilltune --book` の出発点を champion にするため）。理由: `drilltune` に出力の確認が無く、問い 1 に要った。ほかのコードは変えていない。テストは足していない（測定用の小さなコマンド）。
- `drilltune` の既定の本は `DefenseBook.Default` で champion ではない。今回は champion の本を `--book` で渡した。

## 注意・次
- 全テストは未実行。コミット・プッシュはしていない。raytrek に背景の処理は残っていない（`tasklist` で確認）。
- 次の提案（`cma_summary.md`）: P2 の前にドリルの点（固定した 1 守りへの勝ち）を、守りを選ぶ受け手・試合の点へ直す。採るなら punch:2・3 の m を試合 n ≥ 100 で確認。Slip の CMA-ES の 2 本はどちらも `Move` を −0.2 から +0.6〜+0.9 へ動かしたので、スリップの動画を見るべき（今回は撮っていない）。
