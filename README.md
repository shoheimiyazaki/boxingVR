# Mini Ring Boxing（VR POC）

目の前の卓上リングで、身長 60cm のアクティブラグドールを VR コントローラで操るボクシングゲームの POC。
自分の手の動きは位置として転写されず、**手の目標点へ向かうバネ・ダンパ（Bepu のサーボ＋PD 力）** として人形に伝わる。

- エンジン: **Aigis**（Dr. Tod's Very Good Clinic のエンジン。C# / .NET 10・Vulkan(Silk.NET)・BepuPhysics v2）を `src/` にコピー
- VR: **iTVision 3** の OpenXR 層（XR_KHR_vulkan_enable・Stage 空間・目ごとのスワップチェーン・アクション）を `games/MiniRing.Vr` に移植
- Unity は使わない

## 遊ぶ

いちばん簡単なのは `run.cmd` です（毎回 Release で差分ビルドしてから起動します）。PowerShell では `.\run.cmd vr` のように `.\` を付けてください。付けないと「認識されません」と出て実行されません。ビルド自体が落ちたとき（メモリ不足の後の MSB4166 など）は、.NET のビルドサーバーを再起動して 1 回だけやり直します。

| コマンド | 内容 |
|---|---|
| `run.cmd` | デスクトップで遊ぶ（キーボード＋マウス） |
| `run.cmd vr [--runtime ...] [--scale ...]` | VR で遊ぶ（既定は SteamVR なしで Quest に直接つなぐ） |
| `run.cmd replay [ファイル]` | 記録した試合を実時間で再生する（省略時は最新の記録） |
| `run.cmd verify [ファイル]` | 記録を最速で再シミュレーションし、記録どおりに再現できるか照合する |
| `run.cmd selftest` | HMD なしで VR の描画経路を検証する |
| `run.cmd test` | 自動テストを実行する（GPU スナップショットを除く） |
| `run.cmd arena league [--bouts N --seconds S]` | ボット同士の総当たり（順位表・技ごとの命中率・守りの成功率） |
| `run.cmd arena tune --base NAME` | ボットのスタイルを自動調整する（世代ごとに PDCA を記録） |

**試合は自動で記録されます**（デスクトップ版・VR 版とも `replays/` に `miniring_<host>_<日時>.mrrep`）。詳しくは [docs/REPLAY.md](docs/REPLAY.md)。

VR（**Quest を SteamVR なしで**つなぐのが既定）:

1. PC に **Meta Quest Link** アプリ（旧 Oculus アプリ）を入れ、Quest を USB ケーブルの Link か Air Link で接続する
2. 次のコマンドで起動する。Meta の OpenXR ランタイムを自分で探し、このプロセスだけ固定するので、SteamVR がシステム既定のままでも経由しない

```bash
dotnet run --project games/MiniRing.Vr -c Release
```

| `--runtime` | 使うランタイム |
|---|---|
| `auto`（既定） | Meta Quest Link → Virtual Desktop（VDXR）の順。どちらも無ければ案内を出して終了し、SteamVR には黙って落ちない |
| `meta` / `vd` / `steamvr` | そのランタイムを指定する |
| `system` | システム既定（レジストリの ActiveRuntime）を使う |
| `path.json` | マニフェストを直接指定する |

**ヘッドセットに映らないとき**は、表示に応じて次を確認してください。

| 表示 | 確認すること |
|---|---|
| `ErrorFormFactorUnavailable` で終了する | Link がつながっていない。Quest で Link を開始し、Meta Quest Link アプリでヘッドセットが「接続済み」になってから再実行する |
| `session state -> Idle` のまま進まない（8 秒後に案内が出る） | 1. Quest が Link の画面（スタンドアロンのホームではない）になっている<br>2. Meta Quest Link アプリの「設定 > 一般 > 提供元不明」がオンになっている（ストア外のアプリには必須）<br>3. ヘッドセットをかぶっている（近接センサーで起きる） |

環境変数 `XR_RUNTIME_JSON` が設定されていれば、そちらを優先します。Quest Link のリフレッシュレート（72/80/90/120Hz）は Meta Quest Link アプリのグラフィック設定で選んでください。

HMD なし（キーボード＋マウス）:

```bash
dotnet run --project games/MiniRing.Desktop -c Release
```

| 操作 | VR | デスクトップ |
|---|---|---|
| 歩く | 左スティック（視線基準） | WASD（カメラ基準） |
| 腕 | コントローラの位置（肩基準で腕の長さ比に縮小して人形の体へ写す） | 左クリック = 右手 / 右クリック = 左手（相手の頭へ）。Ctrl = 横から回す経路 / Shift = 下から突き上げる経路 / Alt = ボディを狙う |
| 頭（体の軸） | 頭を動かす（スリップ・ダッキング・前傾がそのまま人形の体の傾き・沈み込みになる） | 矢印キー（←→ スリップ / ↑ 前傾 / ↓ ダッキング） |
| 掴む | グリップ（握っている間） | Q = 左 / E = 右（押している間） |
| 投げる | 掴んだまま腕を振って離す | 同左 |
| 身長キャリブレーション | A / X（まっすぐ立って押す。初回は自動） | — |
| やり直し | 10 カウント・リングアウトで自動 | R |

- 向きは常に相手へロックオンします。
- 起動時に頭の向きを「リングの方向」として再センタリングします（iTVision と同じ）。リングは約 1m 前方、台の高さ 75cm です。
- 人形の手の目標点は常に光る玉で表示され、拳とは線でつながります（体やグローブの陰でも隠れません）。掴んでいる間はオレンジになります。
- VR ではコントローラの動きを **2 倍**にして人形へ渡します（構えの位置を中心に増幅するので、構えたままなら腕は伸び切りません）。
- 体は部位ごとの箱をボクセル（皮膚・肉・骨・臓器）で埋めたもので、パンチで削れて破片と血が飛び散ります。肉が削られた部位は力が落ち、腕・脚の骨が断たれるとそこから先が落ち、脳や心臓などの臓器を壊すと死んで K.O. です（[docs/VOXEL_DAMAGE.md](docs/VOXEL_DAMAGE.md)）。打たれると頭が首のバネで揺れ、当たりの重さと芯の食い方は**打撃音**で分かります（[docs/BOXING.md](docs/BOXING.md)）。
- CPU はボクシングの技を使うボットです。ジャブ・ストレート・フック・アッパー・ボディ・コンビネーションを打ち、拳が向かってくればブロック・ダッキング・スリップ・スウェー・バックステップで守り、守り切ればカウンターを返します。ボット同士を戦わせて改善した `champion` が相手をします（[docs/BOTS.md](docs/BOTS.md)、改善の記録は [docs/BOT_PDCA.md](docs/BOT_PDCA.md)）。
- VR 版にはデスクトップのミラーウィンドウがありません。状態はコンソールに出ます。

## 検証

```bash
dotnet build MiniRing.slnx
```

```bash
dotnet test tests/MiniRing.Tests --filter "TestCategory!=visual"
```

```bash
dotnet run --project games/MiniRing.Vr -- --selftest
```

- テストは 121 件あります（うち GPU 描画の visual 2 件）。記録→再生が全チェックサムまでビット単位で一致することも確かめています。純関数（IK・写像・力学モデル・部位判定・体の状態など）と、ヘッドレスで物理を回して「立つ・歩く・旋回・パンチが届く・掴んで離す・ダウンから起き上がる」「打たれた頭が首のバネで揺れて戻る」「手の位置で胴がひねれ、頭の位置で傾く」「ストレートは手打ちより重い」「打撃でボクセルが削れ、骨が断たれた腕・脚が落ち、脳を壊すと死ぬ」を確かめるものです。
- `--selftest` は HMD なしで VR 描画経路を検証します。自前の Vulkan デバイスと sRGB 画像（XR スワップチェーン画像の代役）に描いた結果が、通常の描画とバイト単位で一致するかを見ます。
- GPU の画面確認は `dotnet test tests/MiniRing.Tests --filter TestCategory=visual` で行い、出力先は `.visualcheck/` です。

**実機（HMD）では未検証です。** 最初に確かめる点は [docs/TUNING.md](docs/TUNING.md) にあります。

## ドキュメント

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — 構成・制御の仕組み・1 フレームの流れ
- [docs/BOXING.md](docs/BOXING.md) — パンチの力学（体重が乗る／手打ち）、部位ダメージ、打撃音
- [docs/VOXEL_DAMAGE.md](docs/VOXEL_DAMAGE.md) — ボクセルの体（削れる・弱る・骨が断たれて落ちる・臓器が壊れて死ぬ・飛び散る）とエンジンの部品
- [docs/REPLAY.md](docs/REPLAY.md) — 自動記録と完全再現（ファイル形式・再生・照合）
- [docs/BOTS.md](docs/BOTS.md) — ボクシングボットの技・守り・スタイルと、ボット同士の対戦場
- [docs/BOT_PDCA.md](docs/BOT_PDCA.md) — ボット同士を戦わせて改善した記録（データと判断）
- [docs/REUSE.md](docs/REUSE.md) — iTVision 3 / Aigis(Dr. Tod) から何をどう持ってきたか、エンジンへの変更点
- [docs/TUNING.md](docs/TUNING.md) — 調整値と、実機で最初に見るべきこと
