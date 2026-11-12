# 実況・解説の吹き替え（tools/commentary）

試合の動画に、テレビ中継ふうの日本語の実況（アナウンサー）と解説の声を重ねる道具。声は Windows に入っている音声合成（OneCore の日本語の声）で作り、ffmpeg で動画に混ぜる。声が出ている間はゲームの音を少し（既定 6 dB）下げる。映像はそのままコピーする（再エンコードしない）。

ゲームの試合の計算には一切触れない（見た目・音だけの後処理）。

## 使い方

リポジトリの根で（PowerShell 5.1 で動く。ffmpeg が PATH に要る。ffprobe は不要）:

```powershell
powershell -ExecutionPolicy Bypass -File tools\commentary\dub.ps1 -Video in.mp4 -Script script.json -Out out.mp4
```

- 声の WAV とマニフェストは `<Out の名前>_voices\`（例: `out_voices\`）にできる。`-WorkDir` で変えられる。
- `-DuckDb 6`（ゲームの音を下げる量）、`-Gap 0.15`（台詞と台詞の最小のすき間、秒）。

2 段に分けて使うこともできる:

```powershell
# 1. 合成: 1 行 1 WAV + manifest.json
powershell -ExecutionPolicy Bypass -File tools\commentary\Synthesize.ps1 -Script script.json -OutDir voices
# 2. 混ぜる
powershell -ExecutionPolicy Bypass -File tools\commentary\Mix.ps1 -Video in.mp4 -Manifest voices\manifest.json -Out out.mp4
```

| スクリプト | 主な引数 |
|---|---|
| `Synthesize.ps1` | `-Script` `-OutDir` `-AnnouncerVoice` `-CommentatorVoice`（声の名前の一部の配列。先に当たったもの）`-Gap` `-SilenceDb -50` `-TailSeconds 0.08` |
| `Mix.ps1` | `-Video` `-Manifest` `-Out` `-DuckDb 6` `-DuckAttack 0.15` `-DuckRelease 0.4` `-VoiceGainDb 0` `-LimitDb -1` `-AudioBitrate 192k` |

## 台本（script.json）の形

UTF-8 で書く。

```json
{ "lines": [
  { "at": 0.5,  "speaker": "announcer",   "text": "さあ第1ラウンド、ゴングが鳴りました！", "excited": true },
  { "at": 3.2,  "speaker": "commentator", "text": "両者慎重な立ち上がりですね。" },
  { "at": 12.0, "speaker": "announcer",   "text": "左フック！入った！ぐらついた！", "excited": true }
] }
```

- `at`: 動画の中で話し始めたい時刻（秒）。
- `speaker`: `announcer`（実況）か `commentator`（解説）。
- `text`: 台詞。
- `excited`（省略可、既定 false）: 盛り上がる場面。速く・高く・大きく話す。

## 声

| 役 | 既定の声 | 話し方（SSML の prosody） |
|---|---|---|
| 実況 | Microsoft Ichiro（`MSTTS_V110_jaJP_IchiroM`、男性） | 速さ +15%・高さ +5%。excited は +35%・+12%・音量 +20% |
| 解説 | Microsoft Ayumi（`MSTTS_V110_jaJP_AyumiM`、女性）。無ければ Haruka | 速さ -3%・高さ -2%（落ち着いた声）。excited は +10%・+4%・音量 +10% |

ほかに入っている声: Haruka・Sayaka（OneCore）。`-AnnouncerVoice Sayaka` のように名前の一部で選べる。話し方の値は `Synthesize.ps1` の先頭の `$Prosody` にある。

合成は WinRT の `Windows.Media.SpeechSynthesis.SpeechSynthesizer`（OneCore の声）を使う。デスクトップの SAPI の声（Microsoft Haruka Desktop）は使わない。OneCore の声は台詞の後ろに 0.6〜0.8 秒の無音を付けるので、-50 dB より小さい後ろの無音を切り、0.08 秒だけ残す（台詞が無駄に後ろへずれないように）。

## 時刻のずれ（重なりを避ける仕組み）

二つの声が同時に話すことはない。台詞を `at` の順に（同じ時刻なら台本の順に）並べ、

```
開始 = max(at, 前の台詞の終わり + Gap)
```

で決める。前の台詞（話し手を問わない）が長くて次の `at` に食い込むなら、次の台詞をその分だけ後ろへずらす。ずれは後ろへ伝わる（順番は変わらない）。

マニフェスト（`manifest.json`）には台詞ごとに `requestedAt`（台本の時刻）・`start`（実際の開始）・`duration`（実際の長さ）・`end`・`shift`（ずらした秒数）・`file` が入り、全体の `shiftedCount`・`totalShift` も入る。合成のときにも `(shifted +0.64s)` と表示する。最後の台詞が動画より後ろで終わるときは Mix が警告を出し、はみ出た分は切れる。ずれが大きいときは台詞を短くするか、`at` の間隔を空ける（OneCore の声は 1 文字あたり概ね 0.12〜0.2 秒）。

## 混ぜ方（Mix.ps1）

一つの ffmpeg のフィルタグラフ（`<voices>\mix.filtergraph.txt` に書き出して `-filter_complex_script` で渡す）:

1. 各 WAV を 48 kHz ステレオにそろえ、`adelay` で開始時刻まで遅らせる（`amovie` で読む。ffmpeg は voices のフォルダで動かすのでパスの escape が要らず、台詞が多くてもコマンド行が長くならない）。
2. 声を `amix`（normalize=0）で足す。
3. ゲームの音に音量の包絡を掛ける: 台詞ごとに「開始の 0.15 秒前から下がり始め、終わってから 0.4 秒で戻る」台形を足して 1 で頭打ちにし、`gain = 1 - (1 - 10^(-DuckDb/20)) × 包絡`。声の間はちょうど DuckDb だけ下がる（コンプレッサと違い、下げる量が声の大きさに左右されない）。
4. ゲームと声を足し、`alimiter`（-1 dBFS）でクリップを防ぐ。長さは元の動画の音に合わせる。
5. 映像は `-c:v copy`、音は AAC。元の動画に音が無ければ無音の上に声だけを載せる。

## 確かめた結果（20 秒のテスト動画、6 台詞）

- 合成は 1 台詞あたり 0.03〜0.13 秒（最初の 1 本だけ声の読み込みで少し遅い）。混ぜるのは 2 秒弱。
- 出力の長さは 20.0 秒、音は AAC 48 kHz ステレオ、最大 -2.9 dB（クリップなし）。
- 440 Hz の試験音を帯域で測ると、声の無い所 -24.5 dB、声の下 -30.5 dB（約 6 dB 下がっている）。
