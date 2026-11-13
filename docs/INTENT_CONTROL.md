# 意図で動かす操作

## 計画（2026-11-13）

作業 A の後に作業 B を進める。体の位置・姿勢には触らず、意図 → 技の選択 → `FighterCommand` の経路という三段を保つ。

| キー | 意図 |
|---|---|
| 矢印 / WASD | 相手基準の前後左右への移動 |
| J / 右クリック | 弱攻撃（前の手） |
| K / 左クリック | 強攻撃（後ろの手） |
| Space | 回避。方向なしはガード |
| R | ラウンドをやり直す |

既定は意図操作にし、`--control classic` で従来の操作を残す。

| 意図と状況 | 選択 |
|---|---|
| 弱 / 強 | ジャブ / ストレート |
| 横＋攻撃 | 同じ手のフック |
| 近距離＋攻撃 | ボディ、横方向ならボディフック |
| 後ろ＋攻撃 | アッパー |
| 前＋強 / 遠距離 | ストレートと踏み込み |
| 後ろ＋回避 | スウェー |
| 前＋回避 | `OpponentRead` と拳速度で読み、`DefenseBook` の効き目から選択 |
| 回避直後＋攻撃 | 回避の表のカウンターを優先 |

`TechniqueExecution` を共有の純粋な経路計算とし、拳の溜め・頭の移動・胴のひねり・バネ伝達・防御経路を `BoxerBot` から移す。判断と乱数の呼び順は変えない。リファクタ前の同種試合のチェックサム列を保存し、変更後と全件比較する。一致すれば LogicVersion 16 を維持する。

`IntentControl : IFighterControl` はキーに依存しない `FighterIntent` を受け取る。押下でその歩から予備動作を始め、保持中に深くし、離すと打つ。技途中の押下は一つだけ保持し、`Combo` の接続と経路進行から受付を決める。回避は攻撃と先行入力を中断する。調整量は `IntentTuning` に置く。

記録は解釈後の指令を保存する。形式を v4 にし、従来保存されなかった `Spring` と `TwistBias` も保存する。v3 の読み込みを維持する。台本の操作は Arena の `intentvideo` で同じ解釈部へ流す。

## 検証手順

- RED → GREEN → REFACTOR: 初歩の応答、移動、溜めと離し、前進強攻撃、回避の選択・中断、ガード、連打、境界値。
- 物理試験で溜めと踏み込みによる拳速を測る。
- 変更前後のボット試合のチェックサム列を比較し、既存ボット・ReplayTests を実行する。
- 意図操作の記録を再生し、全チェックサムを照合する。
- 台本動画と縮小版を作り、コマを抜き出して目視確認する。
- 別エージェントのレビュー、全通常テスト、README 件数と文書更新。

## 再利用の調査

GitHub の [Input-Combos](https://github.com/Skyway666/Input-Combos) と [fighting-game-motion-input](https://github.com/seung-cha/fighting-game-motion-input) を調査した。汎用の入力履歴認識は参考になるが、本作の物理による溜め・既存の `PunchSpec` / `DefenseBook` との共有を大半カバーしないため、追加依存は入れず既存の経路部品を再利用する。Context7 / Exa / ECC はこのセッションでは利用できない。

## 実装と使い方

- `IntentKeyboardControl`: キーとマウスを `FighterIntent` へ写す。ラウンドが変われば操作の状態を作り直す。
- `IntentControl`: 押下・保持・解放・一つの先行入力を管理する。攻撃と回避の選択はここで行う。保持時間はゲームの dt だけで進める。
- `TechniqueExecution` / `PunchAction`: ボットと共有する実行部。構え、拳、頭、逆ひねり、体のバネを拳へ渡す期間、防御の拳と頭を評価する。
- `CounterResponse`: ボットと共有する既定の返し。回避の表に候補があれば優先し、押した攻撃の手がない場合は通常の技へ戻る。
- `IntentScript`: 時刻順の押下状態を検証して読み込む。未知の JSON 項目、逆順・重複時刻、有限でない時刻、範囲外の方向は拒否する。

```powershell
dotnet run --project games/MiniRing.Desktop -c Release
dotnet run --project games/MiniRing.Desktop -c Release -- --control classic
dotnet run --project games/MiniRing.Arena -c Release -- intentvideo --script docs/arena/intent_demo.json --blue docs/arena/intent_opponent.json --out docs/video/gen10_intent_demonstration.mp4
```

台本は `at` 秒以降の状態の配列。`x` は右、`y` は前、行動は `light` / `heavy` / `evade`。省略した方向・行動は 0 / false（離す）。動画は速度 1、30fps、1/60 秒のホスト歩で意図を評価し、デスクトップと同じ解釈部を呼ぶ。`.mrrep` も出し、動画生成後に自動照合する。画面には押下状態と選ばれた技を表示する。

## 検証結果と制約

物理の版は 18、ボットの判断の版は 16 のまま。swarmer 対 champion（種 3/17）各 20 秒の全 2,400 ホスト歩で、切り出し前と同じチェックサムになる fixture を残した。意図操作の記録は別に全 480 歩のチェックサムを照合する。

固定値は同じ CPU／ランタイム環境での抽出検証専用。通常テストでは同じ環境で 2 回実行して比較する。今回の生成環境は Windows 10.0.26200、win-x64、.NET 10.0.11（SDK 10.0.400）、AMD64 Family 23 Model 96 Stepping 1。今回の固定値と明示比較する場合:

```powershell
$env:MINIRING_VERIFY_EXTRACTION = '1'
dotnet test tests/MiniRing.Tests -c Release --filter FullyQualifiedName~BotExecutionBaseline
```

18 歩保持した右手のピークは 2.015 m/s、1 歩の短押しは 1.897 m/s。前へ歩きながら保持すると 2.284 m/s（種 43、1 秒構えた後、解放後 18 歩で測定）。物理モデルへの加算倍率はない。

動画は通常 champion 相手と、操作を見るための低頻度ジャブ相手を作成。後者でワンツー・溜め・フック・スウェー・沈む回避・ガードを目視した。両動画の記録は各 14/14 チェックサム一致。検証記録は [arena/codex_2026-11-13.md](arena/codex_2026-11-13.md)。

Computer Use はデスクトップ版を許可対象外として起動を拒否したため、実キーによる操作感の評価は未実施。技の読みは予測と現在の拳の速度による推定なので、前＋回避が常に成功するとは限らない。先行入力は一つだけ保持し、寿命は前の技の時間まで。専用ゲームパッドの読取りと意図操作からの掴みは今回の範囲に含めず、従来操作を残した。

全通常テスト 425 件成功（失敗・スキップ 0）。最終差分の対象テスト 28 件も独立プロジェクトで成功。主要な新規制御・共有実行部の行カバレッジは合計 89.9%。並行作業で追加された音声変更はこの件数の検証対象外。コミット・プッシュは行わない。
