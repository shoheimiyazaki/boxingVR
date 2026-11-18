---
name: bump-version
description: Fighter.PhysicsVersion（体の物理・当たり・静的な形）か BoxerBot.LogicVersion（ボットの判断）を上げる。物理・判断を変えたとき、枝をマージして版がぶつかったとき、チェックサムのテストが変わったときに使う。
---

# 版を上げる

1. **上げるかを決める**: 同じ種・同じ入力で試合の結果が変わるなら上げる。体・当たり・リングと部屋の当たり判定 → `PhysicsVersion`（`games/MiniRing/Fighter.cs`）。ボットの判断・教科書の既定の判断 → `LogicVersion`（`games/MiniRing/Bots/BoxerBot.cs`）。描画・音・HUD だけなら上げない。
2. **今の値を見る**: `dotnet run --project games/MiniRing.Arena -c Release -- version`。master と並行の枝の値も見る（`git show master:games/MiniRing/Fighter.cs | grep "PhysicsVersion ="`）。ぶつかるなら両方より大きい番号にする。
3. **定数を書き換える**: 注釈は今の版の 1 行だけ（`// N: 何を変えたか。履歴は docs/VERSIONS.md`）。前の版の文はコードに残さない。
4. **履歴を足す**: `docs/VERSIONS.md` の該当の節の**先頭**に `- **N**: 何を変えたか（なぜ・測った結果・関わる docs）。` を 1 行。マージなら合わせた枝の名前と番号を書く。
5. **チェックサム**: 記録の再生・チェックサムを持つテスト（`ReplayTests`・`BotExecutionBaselineTests`・`PunchBookChecksumProbe` など）が変わるのは正しい。変わった値を取り直し、取り直した理由をテストの注釈かコミットに書く。黙って期待値だけ書き換えない（変わるはずの無いテストが変わったら原因を調べる）。
6. **確かめる**: `run.cmd test`（速い段）→ 完了前に `bash tools/remote-test.sh`（全部）。
