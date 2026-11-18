---
name: verify-visual
description: 見た目・動きに関わる変更（技の経路・体の力学・回避の形・描画・HUD・動画の演出）を、動画を撮って自分の目で確かめる。CLAUDE.md §4.7「目で見る」を満たすとき、ユーザーに「見た目がおかしい」と言われたとき、動画やドリルの結果を報告する前に使う。
---

# 動画を撮って自分で見る

PNG・動画が「できた」ことは正しさの証明ではない。中身を読んで、ユーザーの言葉（「スウェーに見える」「ワンツーが無い」）で説明できるかを確かめる。

1. **撮る**（短く。必要な場面だけ）。Arena は `dotnet run --project games/MiniRing.Arena -c Release -- <コマンド>`。
   - 試合: `video --red <A> --blue <B> --seconds 20 --out docs/video/<名前>.mp4`（キャラは `--red-char`、K.O. まで `--until-ko yes`）
   - 技・型: `kickvideo`・`imitate --weapon staff --only tsuki`・`guardlab --video`・`intentvideo --script <台本>`（引数は docs/TOOLS.md）
   - 撮る前に、相手の技表・カウンターの有無など「撮りたい場面が本当に起きる設定か」を確かめる（受け手が既定の技表のまま撮られていた失敗がある）。
2. **1 枚にする**: `py -3 tools/sheet.py docs/video/<名前>.mp4 --count 12`（全体）→ 気になる所を `--from 4 --to 6 --count 8` で細かく。打撃の瞬間は `*.events.json` の時刻を `--times` に渡す。
3. **読む**: `Read` で PNG を見る。次を言葉にする: 何が起きたか（誰が何を打ち、どう避け、どう崩れたか）・期待と違う所・物理として不自然な所（姿勢が飛ぶ・貫通・浮く）。
4. **直す**: 違えば §1.2 に従い、姿勢を描き足さず、目標の経路か体の力学を直す。直したらもう一度 1〜3。
5. **伝える**: 確かめた画像（sheet）と動画を `SendUserFile` で送る（30 MiB を超える動画は音を残して縮める: CLAUDE.md §5）。言葉で見たことを添える。見ていない物は「見ていない」と書く。
