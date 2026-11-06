# 参照元から持ってきたもの

## iTVision 3（`Cardio-Flow-Design/iTVision` → `E:\repos\iTVision3`）

自作の C#/Vulkan/OpenXR エンジンで動く VR アプリです。VR のコードは `VulkanRenderer` の partial クラスに直結しており、独立した層になっていません。そのため**ファイルはコピーせず、OpenXR の手順を移植しました**。

| iTVision 3 | 移植先 / 扱い |
|---|---|
| `VR/VulkanRenderer.Vr.cs` CreateXrInstance / GetXrSystem / CreateXrSession / CreateXrSpace / RecenterAppSpace / EnumerateViewConfig / CreateEyeSwapchains / RenderVrFrame / PollXrEvents | `games/MiniRing.Vr/XrSession.cs`。手順・順序・Stage→Local フォールバック・初回再センタリングはそのまま |
| `VR/VulkanRenderer.VrAttach.cs` の XR_KHR_vulkan_enable（v1）手順 | `XrSession.Create` と `VulkanContext.cs`。iTVision は既存デバイスへ後付けするため拡張を先回りで有効化していたが、こちらは起動時に VR 専用で作るので、**ランタイムが要求する拡張をそのまま使う** |
| `VR/VulkanRenderer.VrInput.cs` アクションセット・Touch/Index/simple のバインド・SyncActions 失敗＝フォーカス喪失・グリップのヒステリシス | `XrSession.CreateActions` / `ReadInput`、`VrPlayerControl.cs`。iTVision に無かった **A/X ボタン（キャリブレーション）と振動出力を追加** |
| 目ごとに 2 パスで描画（マルチビューなし）、要求解像度 | 同じ方式。解像度倍率は `--scale` で指定 |
| 落とし穴（`SESSION_NOTES.md` など）: 拡張の不一致、SteamVR が HMD 無しで instance 作成に失敗する、IDLE のまま止まる、フォーカス喪失、レイアウト | エラーメッセージとコメントに反映。SteamVR の同梱や `XR_RUNTIME_JSON` での固定（`Shared/SteamVrRuntime.cs`）、PICO Connect の起動支援は**未移植**（POC ではシステムの既定ランタイムを使う） |
| GDI+ で描く VR HUD | 未移植。体力バーはワールド空間の箱で代用 |

## Dr. Tod's Very Good Clinic（Aigis / Triage、`E:\repos\ClaudeProjects\engine`、`shoheimiyazaki/aigis`）

Aigis の CLAUDE.md の規約「別のゲームを作るならリポジトリをコピーする（このリポジトリを広げない）」に従いました。

| 流用元 | 扱い |
|---|---|
| `src/Aigis.Foundation, Physics, Render, Platform, Player, Audio` | **丸ごとコピー**（engine@3aa7250）。`Directory.Build.props`（警告をエラー扱い・アナライザ）と `.editorconfig` も |
| `Aigis.Physics/HumanoidRagdoll`（13 剛体・BallSocket + Swing/Twist Limit + 減衰モーター・自己衝突なし） | そのまま使用。`HalfExtents` と `MassDistribution` を 0.355 倍・0.1 倍で渡して 60cm 体格にする |
| `HumanoidRagdoll.AddLegServos`、`PhysicsWorld.AddAngularServo / AddTwistServo`（Hz・減衰比・力上限＝バネ・ダンパ） | 脚・首・肩・肘の駆動に使用 |
| `games/Game.RagdollPlayground` のマウス掴み（BallSocket スプリング） | 掴み（`Fighter.TryGrab`）の参考にした。こちらは拳と相手部位を直接、壊れる関節でつなぐ |
| Triage Writhe の「目標は静止姿勢からの相対で」 | 全サーボ目標を T ポーズ（単位回転）からの相対で与える |
| `Aigis.Player.GameHost / IGame / OrbitCamera` | デスクトップホストに使用 |
| `Aigis.Render.HeadlessRenderer` | GPU スナップショットと **VR 描画**に使用（下の変更を加えた） |
| Unity 版 Triage（`Game4`）の ActiveRagdollMotor 方式（ホバー + 直立 + 水平 P、こらえて崩れる）と BrainDamageCalc の式 | 制御方式とダメージ式を移植 |

### コピーしたエンジンへの変更（上流 Aigis には無い）

| ファイル | 変更 |
|---|---|
| `src/Aigis.Render/HeadlessRenderer.External.cs`（新規） | 外部の Vulkan デバイスで動くコンストラクタと、`RenderPrimitiveSceneToImage`（描いた結果を外部画像へ GPU コピー） |
| `src/Aigis.Render/HeadlessRenderer.cs` | 外部デバイスのときはデバイスとインスタンスを破棄しない（`_ownsDevice`） |
| `src/Aigis.Render/HeadlessRenderer.Primitives.cs` | 出力先が外部画像なら、読み戻さずにコピーして終える |
| `src/Aigis.Render/Camera.cs` | `OffCenterViewProjection(Matrix4x4 view, …)` オーバーロードを追加（トラッキング姿勢から投影を作る）。既存のオーバーロードはこれに委譲 |
| 一部の doc コメント | オーバーロード追加で曖昧になった `cref` を明示 |

既存の呼び出しの挙動は変わりません（既定の経路は従来どおり）。

## 前回の Unity 版について

最初の試作では、参照先を `Nathon-rtgo/iTVision`（旧 Unity 版）と `Game4/Triage`（Unity 版の旧プロトタイプ）と取り違えていました。このとき作った Unity プロジェクトは削除済みです。ただし、制御の考え方（バネ・ダンパでの駆動、体フレームでの写像、ダメージ式）と、その時に受けたレビューの指摘は、このバージョンに引き継いでいます。具体的には、重力補償を常に満額にする、捻りの基準を曲げ平面の法線にする、衝突直前の速度でダメージを判定する、目標速度に上限を設ける、の 4 点です。
