完了

# 作業指示 16（P1a）報告: CMA-ES の本体と遺伝子への写し

## 作った物
- `games/MiniRing.Arena/Evolution/CmaEs.cs`: CMA-ES 本体（依存なし・double・ヤコビ法の固有分解・固有値昇順・固有ベクトルは絶対値最大の成分を正にそろえる）。
  - API: `new CmaEs(mean, seed, initialSigma=0.15, maximize=false, lambda=null, maxGenerations=1000)`、`Ask()`（折り返し済みの候補 λ 個。何度呼んでも同じ）、`AskRaw()`（折り返す前）、`Tell(scores)`（候補の番号順）、`Best`（平均を折り返した点）、`RawMean`・`Sigma`・`Covariance`・`ConditionNumber`・`StopReason`（σ < 初めの 1/50、世代の上限、条件数 > 1e14 の 3 つだけ）。
  - 状態: `SaveState()`（JSON: 種・世代・λ・平均・σ・C・p_σ・p_c など）/ `CmaEs.LoadState(json)`。再開は続けて回した場合と JSON まで一致。
  - 乱数: 世代ごとに `new Random(Hash(seed, generation))` の Box-Muller。鏡映は三角波 `CmaEs.Reflect`。更新は折り返す前の標本。
- `games/MiniRing.Arena/Evolution/Genomes.cs`: `Gene`、`PunchGenome(technique)`（9 次元、幅の表 `Widths` は 1 か所・根拠コメント付き、`Validated()` を通す）、`DefenseGenome(defense)`（範囲は `DefenseRanges`。Block は HandsUp 固定、Sweep でない技は EndX 無し。次元は Block 13・抜けない技 14・Weave/Roll 15）。`Encode`/`Decode`/`Initial` は往復できる。
- `tests/MiniRing.Tests/CmaEsTests.cs`（新規 18 テスト）。
- `tests/MiniRing.Tests/MiniRing.Tests.csproj`: テストが Arena の型を見られないので `MiniRing.Arena` への ProjectReference を 1 行足した（許可された変更）。

## テスト結果（実行したのは CmaEsTests の 18 本のみ。全 18 成功。全体のスイートは未実行、Claude が実行する）
`dotnet build tests/MiniRing.Tests`（警告をエラー扱い）も成功。評価回数は折り返した平均の f が 1e-8 以下になるまで（止める条件は使わない）。

| 問題 (n=10, 初め σ=0.15, 平均 0.5) | 種 1 | 種 2 | 種 3 | テストの上限 |
|---|---|---|---|---|
| 球 Σ(x−0.3)² | 1210 | 1080 | 1180 | 3500（assert） |
| 回転した楕円（条件数 1e3） | 2840 | 2930 | 2690 | 30000 |
| Rosenbrock（[0,1]→[−2,2]、最小は x=0.75） | 5630 | 6310 | 6240 | 60000 |

球は brief の「2,000 回前後」より少なく収束した（平均が最初から最適の近く 0.2 離れ、判定が平均の点のため。文献の桁の範囲内）。

その他のテスト: 同じ種で同じ軌跡と Ask の再現性、並列評価で同じ、保存→再開で平均・σ・C・JSON 一致、鏡映（折り返し・折り返す前の標本で平均が動く）、最大化、止める条件、非有限の点数、遺伝子の往復（全技・範囲の端）、拳の幅が既定を中心にしている、技の正体（Lead/ToBody/Extend）保持、Block の HandsUp 固定。

## 未実施・注意
- 全テスト（約 22 分）と README のテスト件数の更新は未実施（新規は 18 件）。
- Rosenbrock は [0,1] の端ではなく 0.75 に最小を置く写像でテストした（端に最小があると鏡映の検証にならないため）。
- コミット・プッシュはしていない。
