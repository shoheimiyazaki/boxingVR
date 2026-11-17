# 引き継ぎ: 技の進化（P0a〜P1c）から次へ（2026-11-17）

このセッション（作業指示 14〜18）でしたこと・決めたこと・次にすることを、別のセッションが続けられるようにまとめる。計画の表は `docs/LEARNING_PLAN.md` §9、測定と採否は `docs/BOT_PDCA.md`「CMA-ES で技の値を磨く（P1c）」、生の記録は `docs/arena/cma_summary.md` ほか。

## 1. ユーザーの目標

PPO と進化的アルゴリズムでボットを進化させる: 今の技の最適化・新しい技の習得・戦略と戦術の最適化 → 相手に合わせて色々できる強い AI（参考: EPO, arXiv 2503.19037）。方針（進化は技の値へ、PPO は選ぶ層へ、`FighterCommand` を網が直接出さない）は `LEARNING_PLAN.md` §9「方針」。

## 2. できたもの（すべて master に入っている）

| 段 | 中身 | 主なファイル |
|---|---|---|
| P0a | 拳の技の値をデータの表へ（`PunchBook`。既定のままなら 1 ビットも変わらない） | `games/MiniRing/Bots/PunchBook.cs` ほか |
| P0b | 対にした評価（t 分布の区間・符号検定・Bonferroni のレース）、試合の点（`MatchScore`）、試合の標本（`MatchSampler`）、雑音の測定 `noiselab` | `games/MiniRing.Arena/Evolution/PairedEvaluation.cs`・`MatchScore.cs`・`MatchSampler.cs`・`NoiseLab.cs` |
| P1a | CMA-ES（Hansen の既定値、ask/tell、状態の保存・再開、反射で範囲に収める）と、遺伝子への写し（拳 9 次元・守り 13〜15 次元） | `Evolution/CmaEs.cs`・`Genomes.cs` |
| P1b | `cmatune`: CMA-ES をドリルにつなぐ。拳のドリルの攻め手の差し替え（`DefenseDrill.Punch(attackerStyle:)`）、打ち返し・胴のダメージ、予算の数え方（`DrillMeter`） | `games/MiniRing.Arena/CmaTune.cs`、`games/MiniRing/Bots/DefenseDrill.cs` |
| P1c | 測定（CMA-ES 対 (1+λ)、拳 3 技、動画のコマ）と、`defconfirm`（`drilltune` の結果を cmatune と同じ手続きで比べ直す） | `games/MiniRing.Arena/DefConfirm.cs`、`docs/arena/cma_*.md`、`docs/arena/cma/` |

テスト: `CmaEsTests`（19）・`PairedEvaluationTests`（12）・`CmaTuneTests`（11）ほか。決定性（並列の数で結果が変わらない・再開が通しと JSON まで一致）を確かめている。

コマンドの使い方は `CLAUDE.md` §5 と `games/MiniRing.Arena/Program.cs` の先頭のコメント（`noiselab`・`cmatune`・`defconfirm`）。

## 3. 分かったこと（次の設計の前提）

1. **雑音が非常に大きい**（`docs/arena/evo_noise.md`）: 値を 0.001 変えただけで軌道が分かれ、同じ種でも点の相関は ρ≈0.1。差は「共通の種で対にして比べる」でしか言えない。推奨: 試合 45 秒・n=16・λ=12、ドリル 40 秒・n=48、採否は使わなかった種で n=40 以上（固定の n。レースの途中結果で採否を決めない）。
2. **勝者の呪い**: 世代の最良個体ではなく分布の平均 m を比べる（`CmaEs.Best` は反射した m）。
3. **CMA-ES は道具として採る**: 守り Slip・Weave で 4 本中 3 本が有意に改善、(1+λ) `drilltune` は 0 本。ただし目的の式が違う（CMA-ES は比べ直しと同じ「差」、drilltune は「比」）・予算の一部が推定なので、優劣は傾向として扱う（`docs/arena/cma_vs_hill.md`）。
4. **固定した 1 守りのドリルの点は、試合の強さを代表しない**: 拳 punch:2 はドリル +14.5 に対し試合（n=48）+1.15（下限 0.01）、punch:3 は +8.6 に対し +0.27（差なし）。n=16 では符号が逆だった。**拳の値は既定に採っていない**（`docs/arena/cma/cma_punch_{2,3}.style.json` に候補として残すだけ）。
5. 見た目: 磨いた拳もストレート・フックの形のまま（当たりが早く、踏み込みが少し大きいだけ）。遺伝子の幅（`PunchGenome.Widths`、既定 ± 隣の技との 1/3）で技の正体を保っている。
6. champion は今の物理では champion-v1 より弱い（試合の相手を選ぶときに注意）。

## 4. 次にすること（未着手・未依頼。ユーザーの OK を取ってから指示書を書く）

1. **P1d（P2 の前）: 目的の点を直す**。ドリルの受け手を「守りを 1 つに決めた受け手」から「守りを自分で選ぶ受け手（`BoxerBot` の読み・`DefenseBook` の表）」へ替えるか、試合の点（`MatchScore`）を直接目的にする。そのうえで punch:2・punch:3 を磨き直し、試合 n≥100（使わなかった種）で確かめてから採否を決める。試合を目的にすると 1 評価が重い（45 秒 × n=16）ので、raytrek の 16 コアで回す前提で予算を先に測る。
2. **P2**: 戦術の PPO（`BoxerBot` の攻め・守りの選択を方策へ、学習用の step API、C# の手書きの小さな網で推論、§1.6 の決定性）。
3. **P3**: EPO 風の潜在の値の集団と相手の集団。**P4**: MAP-Elites で新しい技の経路。

## 5. 道具と回し方の注意

- **重い測定は raytrek で**（`ssh raytrek`、16 コア）。`dotnet` は ssh から普通に動く（Codex は ssh から動かない: `CLAUDE.md` §10）。作業ツリーは `C:\Users\smiya\repos\myvrgame1`。P1c はここで約 8 時間。
- raytrek の cmd の `findstr` は UTF-8 を壊すので、ログの検索は PowerShell の `Select-String -Encoding UTF8`。
- このデスクトップは RAM が少なく、全テスト（約 14 分）が他のセッションと重なると終わらないことがある。そのときは raytrek で回す。
- `cmatune --resume` は種・λ・σ・始めの値が違う状態を拒む。種は `GenerationSeed = seed + 1000 × (gen × samples + k)`、比べ直しは 500000 × 1000 から、試合は 600000 からで重ならない（`Generations × Samples < 500000`）。
- 指示書は `docs/plans/CODEX_BRIEF_14〜18_2026-11-15.md`、報告は `CODEX_REPORT_14〜18_2026-11-15.md`（17・18 は Sonnet が実行）。

## 6. 残したもの（git の外・コミットしていない）

- 動画: `docs/video/cma_punch2_Slip_{before,after}.mp4`・`cma_punch3_Weave_{before,after}.mp4`（gitignore。世代を区切るときに `media/` へ）。
- コマの PNG（約 17 MB）: ワークツリー `.claude/worktrees/evo-p0b/docs/arena/cma_frames/`（重いのでコミットしていない）。
