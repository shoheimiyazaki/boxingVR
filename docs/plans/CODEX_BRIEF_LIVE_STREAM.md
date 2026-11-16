# 指示書: WVC のテレビ中継を生で配信する（OBS へ流す）

2026-11-16。判断は済み（ユーザーが「B: ffmpeg から OBS へ流す」を選んだ）。この指示書どおりに実装・測定する。

## 0. 背景（測定済み）

- `HeadlessRenderer` の読み戻しを HostCached にして、テレビ中継（剣・顔の窓・リプレイ）は 720p で実時間の 1.25 倍、1080p で 0.96 倍で撮れる（`docs/TV_BROADCAST.md` §7）。
- ffmpeg（PATH にある）は `h264_nvenc`・`srt`・`rtmp`・`tcp` を持つ。GPU は RTX 3060 Ti。OBS はこの PC に入っていない（受け側の確認は ffmpeg で行う）。
- 実況の声は前もって作る: 試合は種で決まる（決定的）ので、**描かずに試合を先に回して出来事（events.json）を作り → 実況の台本 → 声（Synthesize.ps1 の manifest.json）→ 本番を実時間で描きながら、声を混ぜて流す**。これが「配信の遅延」にあたる。

## 1. 作るもの（優先順）

### 1.1 `video --render off`（描かずに出来事だけ作る下回し）

- `games/MiniRing.Arena/VideoMaker.cs`: 描画しないモード。`HeadlessRenderer`（本体・顔の窓）を作らず、ffmpeg も起動せず、`Emit` はコマを数える（`frame++`）だけ。試合・演出（`TvDirector`・リプレイの影の試合・`Broadcast`）は今と同じに回し、`*.events.json` だけを書く（mp4・wav・タイムラインは書かない）。
- **完了の条件**: 同じ引数で `--render off` と普通の撮影の `events.json` が**バイト単位で同じ**（下の 3 で確かめる）。描かない分、物理の速さで終わる。
- `Program.cs` の `video` の引数に `render` を足す（`on`/`off`、既定 on）。

### 1.2 `video --live <url>`（実時間で描いて流す）

`VideoMaker` に生配信の出口を足す。ファイルへの書き出しの道（今の mp4）は変えない。

- **出口**: ffmpeg を 1 つ起動し、入力は 2 つ: 映像 = 標準入力の rawvideo RGBA（今と同じ）、音 = TCP（`-f s16le -ar 44100 -ac 1 -i tcp://127.0.0.1:<空いたポート>?listen=1`。ffmpeg が聞き、C# が `TcpClient` で繋ぐ。ffmpeg が聞き始めるまで数回やり直す）。
  - 映像: `-c:v h264_nvenc -preset p4 -tune ll -rc cbr -b:v <bitrate> -g <2×fps> -bf 0 -pix_fmt yuv420p`。`--encoder x264` なら `libx264 -preset veryfast -tune zerolatency`（NVENC の無い PC 向け）。
  - 音: `-c:a aac -b:a 160k -ac 2`。
  - 形式は URL で決める: `srt://`・`udp://` → `-f mpegts`（srt は URL に `pkt_size=1316` が無ければ足す）、`rtmp://`・`rtmps://` → `-f flv`、それ以外はファイルのパスとして拡張子（`.ts` → mpegts、`.flv` → flv、`.mkv` → matroska）。この決め方は純関数（`LiveOutput.Arguments(...)` など）にしてテストする。
  - 既定の URL（`--live` に値が無い・`on`）: `srt://127.0.0.1:9000?mode=listener&pkt_size=1316`（こちらが待ち、OBS の「メディアソース」が `srt://127.0.0.1:9000` へ繋ぐ）。
  - 配信の鍵（YouTube・Twitch の stream key）をコードや札に書かない。RTMP へ直接送るときは URL ごと引数で渡す（使う人が入れる）。
- **実時間の歩調**: コマ f を書く前に、時計が `t0 + f / fps` に達するまで待つ。遅れが 1 秒を超えたら（受け手が繋ぐまで ffmpeg が止まっていた・描画が詰まった）、`t0` を今に合わせ直して「止まっていた」と数える（遅れを取り戻すために速送りしない）。終わりに「遅れたコマの数・最大の遅れ・止まった回数」を出す。
- **音を流しながら作る**: 今は音を全部 `audio`（float の配列）に置いてから最後に wav にしている。生配信では同じ配列に同じように置き、**コマを書くたびに、`f / fps − 0.5 秒`（音の遅らせ）までの標本を s16 にして TCP へ書く**（0.5 秒あれば、そのコマまでに置かれる音はすべて入っている。ffmpeg は時刻で映像と音を合わせる）。最後に残りを `frame / fps` まで書く。
  - 会場の歓声（`Broadcast.MixInto`）は今は最後に一括で混ぜている。分ける: 地の歓声（`CrowdSound.Ambience`、長さは `audio` 全体）は始めに混ぜ、反応（`_cues`）は増えた分だけ書き出す前に混ぜる（`MixInto` は両方を呼ぶ形にして、ファイルの道の結果を変えない）。
  - 実況の声 `--voices <manifest.json>`（Synthesize.ps1 の出力。`lines[].file`・`start`・`end`。wav は 16 kHz mono s16 だが、標本の速さはヘッダから読む）: 始めに読み、44.1 kHz へ直線補間で変換して声の線に置く。ゲームの音は声の間だけ下げる（`tools/commentary/Mix.ps1` と同じ値: −6 dB、声の 0.15 秒前から下げ始め、終わって 0.4 秒で戻す。滑らかな傾き）。出力 = ゲームの音 × 下げ ＋ 声、最後に今と同じ `tanh` の柔らかい頭打ち。この混ぜ方はクラス（例 `LiveAudioMixer`）に分け、ファイルの道でも使えるなら使ってよい（使わなくてよい）。
  - 声入れの後の数値は、ファイルの道（`dub.ps1` → `Mix.ps1`）と完全一致でなくてよい（リミッターの違いなど）。違いは `docs/TV_BROADCAST.md` に一言。
- 生配信でも `*.events.json` を書く（`--out` のパスから。下回しと比べるため。既定の `--out` は `videos/live.ts` などでよいが、書くのは events.json だけ）。
- `Program.cs`: `video` に `live`・`voices`・`encoder`・`bitrate` を足す。`--live` と `--render off` は一緒に使えない（エラー）。

### 1.3 `tools/tvshow/tvshow.ps1 -Stage live`（札 1 枚で生の中継）

- 札に `"live": { "url": "...", "width": 1280, "height": 720, "encoder": "nvenc", "bitrate": "6M" }`（どれも省略可。既定は上）。
- 流れ（全試合の準備 → 順に流す）:
  1. 種を決める（今の scout と同じ）。
  2. 各試合を下回し（`video --render off ... --out <out>\bN.mp4` 相当のパスで `bN.events.json` を作る。撮影済みの `bN.events.json` があれば使う。同じ内容なので）。
  3. digest・draft（今と同じ）。台本 = extras があれば `tvmerge`、無ければ draft（生配信では `autoCommentary` を問わず、足す台詞の無い試合は下書きだけで流す）。
  4. `tools/commentary/Synthesize.ps1 -Script bN.script.json -OutDir <out>\bN_live_voices` で声だけ作る（manifest.json）。`tvlint` を今と同じに回す（警告だけ）。
  5. 試合ごとに `video --tv on --facecam on ... --live <url> --voices <manifest>`。試合と試合の間で接続は切れる（OBS のメディアソースは「再接続」の設定で繋ぎ直す。`docs/TV_BROADCAST.md` に書く）。
- 準備（1〜4）だけの段 `-Stage liveprep` も作る（流す前に確かめられるように）。

## 2. 守ること

- 試合の計算（物理・ボット・演出の選び方）を変えない。`PhysicsVersion`・`LogicVersion` は上げない。生配信と下回しとファイルの撮影で、試合は同じ（events.json が同じ）。
- 時計（`Stopwatch`）は歩調にだけ使い、試合の計算に入れない（CLAUDE.md §1.6）。
- 既存のファイルの道（`video` の mp4・`tvshow.ps1` の他の段）の出力を変えない。
- 調整値（音の遅らせ 0.5 秒・下げの量・歩調の 1 秒）は名前のある定数にして、何の量か・なぜその値かをコメントに。
- 例外は握りつぶさない。ffmpeg が落ちたら標準エラーの終わりを添えて止める。受け手が繋がらないまま待つ間は「受け手を待っています: <url>」と出す。
- 日本語のコメント・文書（このリポジトリの書き方）。コミットはしない。

## 3. 確かめ方（全部やって、数字を報告に書く）

1. ビルド: `dotnet build games/MiniRing.Arena -c Release`。
2. 単体テスト（`tests/MiniRing.Tests` に足す。Arena のファイルを使うなら csproj の `Compile Include` に足す。TvDraft.cs と同じやり方）:
   - URL → ffmpeg の形式・`pkt_size` の付け足し・エンコーダの引数。
   - 声の混ぜ方: 声の間はゲームの音が −6 dB（0.501 倍）、声の外は 1 倍、傾きの途中は間の値。16 kHz → 44.1 kHz の長さ。
   - 歓声の分割: `MixInto` の結果が、地の歓声 ＋ 反応を少しずつ混ぜた結果と同じ。
3. 下回しの一致: `video --red-char konrad --blue-char jin --tv on --facecam on --rounds 1 --seconds 20 --seed 3 --render off --out A.mp4` と、同じ引数の普通の撮影（`--out B.mp4`）の events.json を比べて同じ。かかった時間も。
4. 生配信を ffmpeg で受ける: 受け手 `ffmpeg -y -i "srt://127.0.0.1:9000?mode=caller" -c copy recv.ts` を先に（または後に）起動し、`video ... --tv on --facecam on --rounds 1 --live` を流す。
   - 受けた `recv.ts` の長さが撮影の長さと ±1 秒、映像と音の両方がある、遅れたコマの数・止まった回数（720p で遅れはほぼ 0 のはず）。
   - `--voices` 付き: `tvshow.ps1 -Stage liveprep` を、1 試合だけの札（例: marcus 対 akira、種 1、`"rounds": 1`、`"break": 20`、`"out": "docs/video/live-test"`）で回して声を作り、`-Stage live` で流す。比べもの（ファイルの道）は同じ札の `-Stage dub` で作る `b1_live.mp4`（撮影が要る）。`docs/video/commentary-pdca/final/` は物理の版が古い（physics 34 で撮った）ので使わない。ファイルの道の `b1_live.mp4` と、同じ時刻の音の大きさ（`ffmpeg -ss <t> -t 2 -af volumedetect`）が近いこと、声のある所で声が聞こえる大きさであることを数値で。
   - 受けた映像から 3 コマを PNG に書き出す（試合中・リプレイ・勝敗の画面）。
5. 全テスト: `dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName!~Probe"`（30 分ほど。最後に 1 回）。README のテスト件数を合わせる。
6. 文書: `docs/TV_BROADCAST.md` に「生配信」の節（使い方・OBS の受け方: メディアソース、ローカルファイルのチェックを外し、入力 `srt://127.0.0.1:9000`、入力形式 `mpegts`、再接続・遅延の考え方、配信の鍵は OBS に入れる）、`tools/tvshow/tvshow.ps1` の説明、`CLAUDE.md` §5 の表に 1 行。

## 4. 報告

変えたファイル、テストの件数と結果、3・4 の数字（一致したか・かかった時間・受けた長さ・遅れたコマ・音の大きさ）、書き出した PNG のパス、やらなかったこととその理由。
