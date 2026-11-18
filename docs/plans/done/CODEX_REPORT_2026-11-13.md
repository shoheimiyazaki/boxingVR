作業 A → B の順で実装・検証を完了しました。コミット・プッシュはしていません。

- **A:** swarmer 対 champion、seed 3、両者グローブ、速度 1。試合 **2.0 秒に赤の前腕が骨折**（映像 5.5 秒）。映像を目視確認済み。[音声付き縮小動画](E:/repos/ClaudeProjects/myvrgame1/docs/video/gen10_fracture_swarmer_champion_seed3_small.mp4)
- **B:** 意図操作、溜め、先行入力、回避中断、ボットとの共通実行部、リプレイ v4 を実装。従来操作は `--control classic`。[実演動画](E:/repos/ClaudeProjects/myvrgame1/docs/video/gen10_intent_demonstration_small.mp4)／[設計・検証記録](E:/repos/ClaudeProjects/myvrgame1/docs/INTENT_CONTROL.md)
- **検証:** 全通常テスト **425 件成功**。最終対象テスト **28 件成功**。切り出し前の 2,400 歩と一致。主要追加部の行カバレッジ **89.9%**。

実キー試験は Computer Use の許可対象外で未実施です。並行して追加された音声変更は、今回の全件検証には含みません。