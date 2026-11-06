# Mini Ring Boxing（VR POC）

目の前の卓上リングで、身長 60cm のアクティブラグドールを VR コントローラで操るボクシングゲームの POC。
自分の手の動きは位置として転写されず、**手の目標点へ向かうバネ・ダンパ（Bepu のサーボ＋PD 力）** として人形に伝わる。

- エンジン: **Aigis**（Dr. Tod's Very Good Clinic のエンジン。C# / .NET 10・Vulkan(Silk.NET)・BepuPhysics v2）を `src/` にコピー
- VR: **iTVision 3** の OpenXR 層（XR_KHR_vulkan_enable・Stage 空間・目ごとのスワップチェーン・アクション）を `games/MiniRing.Vr` に移植
- Unity は使わない

## 遊ぶ

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

環境変数 `XR_RUNTIME_JSON` が設定されていれば、そちらを優先します。Quest Link のリフレッシュレート（72/80/90/120Hz）は Meta Quest Link アプリのグラフィック設定で選んでください。

HMD なし（キーボード＋マウス）:

```bash
dotnet run --project games/MiniRing.Desktop -c Release
```

| 操作 | VR | デスクトップ |
|---|---|---|
| 歩く | 左スティック（視線基準） | WASD（カメラ基準） |
| 腕 | コントローラの位置（肩基準で腕の長さ比に縮小して人形の体へ写す） | 左クリック = 右ストレート / 右クリック = 左ジャブ / Shift+クリック = アッパー |
| 掴む | グリップ（握っている間） | Q = 左 / E = 右（押している間） |
| 投げる | 掴んだまま腕を振って離す | 同左 |
| 身長キャリブレーション | A / X（まっすぐ立って押す。初回は自動） | — |
| やり直し | KO・リングアウトで自動 | R |

- 向きは常に相手へロックオンします。
- 起動時に頭の向きを「リングの方向」として再センタリングします（iTVision と同じ）。リングは約 1m 前方、台の高さ 75cm です。
- 人形の手の目標点は小さな発光球で表示され、拳とは線でつながります。掴んでいる間はオレンジになります。
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

- テストは 25 件あります。純関数（IK・写像・ダメージなど）と、ヘッドレスで物理を回して「立つ・歩く・旋回・パンチが届く・構える・掴んで離す・ダウンから起き上がる・CPU 戦が発散しない」を確かめるものです。
- `--selftest` は HMD なしで VR 描画経路を検証します。自前の Vulkan デバイスと sRGB 画像（XR スワップチェーン画像の代役）に描いた結果が、通常の描画とバイト単位で一致するかを見ます。
- GPU の画面確認は `dotnet test tests/MiniRing.Tests --filter TestCategory=visual` で行い、出力先は `.visualcheck/` です。

**実機（HMD）では未検証です。** 最初に確かめる点は [docs/TUNING.md](docs/TUNING.md) にあります。

## ドキュメント

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — 構成・制御の仕組み・1 フレームの流れ
- [docs/REUSE.md](docs/REUSE.md) — iTVision 3 / Aigis(Dr. Tod) から何をどう持ってきたか、エンジンへの変更点
- [docs/TUNING.md](docs/TUNING.md) — 調整値と、実機で最初に見るべきこと
