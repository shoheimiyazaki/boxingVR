# 引き継ぎ: WVC のテレビ中継の生配信（OBS へ）（2026-11-17）

このセッションでしたこと・決めたこと・確かめたこと・次にすることを、別のセッションが続けられるようにまとめる。使い方と仕組みの本文は `docs/TV_BROADCAST.md` §7（速さの測定と読み戻しの直し）・§8（生配信）。元の指示書は `docs/plans/CODEX_BRIEF_LIVE_STREAM.md`。

## 1. ユーザーの目標と決めたこと

- 目標: 実況付きのテレビ中継を**実時間で作って配信する**。
- ユーザーが選んだ形（案 B）: **ゲームが ffmpeg（GPU の NVENC）でエンコードして SRT で同じ PC の OBS へ流し、OBS が YouTube・Twitch へ配信する**。配信の鍵は OBS にだけ入れる（コード・札・ログに書かない）。
  - 退けた案: A = ゲームが RTMP へ直接（鍵を道具が持つ・シーン合成が無い）、C = OBS のプラグイン・仮想カメラ（作るものが大きい）、D = 録画してから流す（生ではない）。
- 実況の声は前もって作る: 試合は種で決まる（CLAUDE.md §1.6）ので、**描かずに先に回して（`video --render off`）出来事 → 台本 → 声（manifest.json）を作り、本番を実時間で描きながら声を混ぜる**。

## 2. できたもの（すべて master に入っている。コミット `6d3c89b` ほか）

| 中身 | 主なファイル |
|---|---|
| 読み戻しの速さの直し（HostCached のメモリを選ぶ。非キャッシュのメモリからの CPU コピーが 140 MB/s で律速だった。結果はビット単位で同じ） | `src/Aigis.Render/HeadlessRenderer.cs` |
| 描かない下回し `video --render off`（events.json だけ。撮影とバイト単位で同じ） | `games/MiniRing.Arena/VideoMaker.cs`・`Program.cs` |
| 生配信の出口 `video --live`: エンコード A（rawvideo＋音の TCP → mpegts）→ 緩衝（64 MB、落とさない）→ 送り B（`-re -c copy` → srt/flv/ファイル）。遅延の緩衝（既定 15 秒）、受け手の待ち（`--wait` 120 秒）、URL の伏せ、統計（min lead・underruns） | `games/MiniRing.Arena/LiveOutput.cs` |
| 声の混ぜ（16→44.1 kHz、声の間 −6 dB、tanh）と、歓声の分割（`MixAmbience`＋`MixCues`。ファイルの道は同じ） | `games/MiniRing.Arena/LiveAudioMixer.cs`、`games/MiniRing/Audio/Broadcast.cs` |
| 札 1 枚の段 `-Stage liveprep`（種 → 下回し（毎回作り直す）→ 台本 → 声）・`-Stage live`（試合ごとに流す。下回しと本番の events.json の一致を確かめる） | `tools/tvshow/tvshow.ps1`、`tools/tvshow/cards/live-test.json` |
| テスト 30 件（URL → 形式、声の混ぜ、歓声の分割、短い配信、720p の終わりの詰まりの再発防止 ほか） | `tests/MiniRing.Tests/LiveStreamTests.cs` |

## 3. 確かめたこと（GrandeDesktop、RTX 3060 Ti、他のセッションが CPU を 60〜85 % 使っている間）

- 720p、konrad 対 jin、種 3、1 ラウンド、遅延 15 秒: 受けた長さ 61.39 秒（試合 61.5 秒）、緩衝の残りの最小 15.5 秒、途切れ 0。
- `tvshow.ps1 -Stage liveprep` → `-Stage live`（`live-test.json`: marcus 対 akira、種 1）: 受けた長さ 51.26 秒、途切れ 0、本番の events.json は下回しと同じ。声は manifest の時刻にある（声のある所 −21.6 dB、無い所 −38.9 dB）。ファイルの道（dub 済みの mp4）と同じ時刻の音量は 0.8 dB 以内。
- 受けた映像から試合中・リプレイ・勝敗のコマを PNG にして目で見た。全テスト 789 件合格。
- 受け手は ffmpeg（`ffmpeg -y -i "srt://127.0.0.1:9000?mode=caller" -c copy recv.ts`）。**OBS 実機ではない**。

## 4. まだ確かめていないこと（次にすること、優先順）

1. **OBS 実機で受ける**（この PC に OBS は入っていない。入れるのはユーザー）。メディアソースの設定は `TV_BROADCAST.md` §8「OBS の受け方」。試合と試合の間で接続が切れ、OBS の再接続で戻ることも確かめる。
2. **YouTube・Twitch へ実際に流す**（OBS 経由。鍵はユーザーが OBS に入れる。エージェントは鍵を扱わない）。
3. **1080p の生配信**（§7 の測定では 1080p の撮影は実時間の 0.96 倍。遅延の緩衝で持つかを min lead で見る。足りなければ `--delay` を延ばすか 720p）。
4. 静かな PC（他の作業が動いていない）での測定。重い作業は raytrek へ回すとこちらが空く。
5. 試合の間の切れ目を無くす（1 回の `video` で複数の試合を流す、または B を試合をまたいで生かす）。今は試合ごとに接続が切れる。

## 5. はまった所（同じことをしない）

- **受け手を先に繋ぐと失敗する**: SRT はこちらが待ち側。「受け手を待っています」が出てから繋ぐ（出る前に繋ぐと I/O error。ffmpeg で受けるなら繰り返し起動する）。
- **コマごとの実時間の歩調は途切れる**: リプレイの追いつき（影の試合を 1 コマで数百歩）で 1 コマが 4〜5 秒かかった。遅延の緩衝に変えた。遅延 8 秒では CPU 80 % 前後の負荷で残りが 0.5 秒まで減ったので既定を 15 秒にした。
- **終わりの詰まり（720p・NVENC）**: 溜めた映像を全部 A へ渡してから音を書くと、A が音を待って止まる。音を 1 コマずつ挟んで渡す（`LiveOutput.Finish`）。x264・短い配信では再現しない（テストは NVENC・150 コマ・720p）。
- **古い下回しの使い回し**: 物理・ボットの版が変わると本番と台本がずれる。`liveprep` は毎回作り直す。
- **ログの文字コード**: ffmpeg・dotnet のログは cp932 で出ることがある。grep で見つからないときは `iconv -f cp932` を通す。
- **プロセスの止め方**: 他のセッションが同じ PC で動いている。自分が起動したプロセスだけを PID で止める（コマンド行の文字列で一致させて止めると、自分のシェルまで止めた）。
- **Python**: `python` は Windows ストアの入口が起動する。`py` を使う。
