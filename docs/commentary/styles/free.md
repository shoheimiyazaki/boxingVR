# 型: free（役の無い 2 人のフリートーク）

試合の動画を一緒に見ている 2 人（声 `a`: 男声・`b`: 女声。速さは同じ +15%）。何者か・何を話すかは決めない。

- 渡すもの: 話せる時間（動画の 0 秒から終わりまで）と、動画に映るものの記録（`bN.events.json`・`bN.digest.md`）だけ。
- 守ること: 記録と違えない・作中の言葉（README §2 の 2・3）・1 台詞 60 字。
- 骨組み: `useDraft: false`。声の配役（`speakers`）は `write-commentary.ps1` が付ける。
- 点検: 長さ・言わない言葉・ずれだけ。
- プロンプト: `tools/tvshow/prompts/free-talk.md`。
