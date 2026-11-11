# アーキテクチャ

```
src/                      Aigis エンジン（engine@3aa7250 からコピー。Foundation/Physics/Render/Platform/Player/Audio）
games/MiniRing/           ゲーム本体（ホスト非依存・IGame<BoxingInput>）
games/MiniRing.Desktop/   デスクトップホスト（Aigis GameHost + キーボード/マウス）
games/MiniRing.Vr/        OpenXR ホスト（iTVision 3 から移植） + --selftest
tests/MiniRing.Tests/     純関数テスト + ヘッドレス物理テスト + visual（GPU スナップショット）
```

依存の向き: `MiniRing.Vr / MiniRing.Desktop → MiniRing → Aigis.(Physics, Render, Player) → Aigis.Foundation`。
Bepu の型は Aigis.Physics の外に出ない（Aigis の規約）。

## ゲーム本体（games/MiniRing）

| ファイル | 役割 |
|---|---|
| `FighterCommand.cs` | 入力元（VR / キーボード / AI）とラグドールの唯一の契約: 移動方向・向き・左右の手（体フレームのローカル目標・握り） |
| `BodyFrame.cs` | 位置 + ヨーだけの体座標系。プレイヤーと人形の間で手の位置を受け渡す |
| `Fighter.cs` | Aigis `HumanoidRagdoll` を 60cm に縮小して生成し、指令をバネ・ダンパで伝える |
| `ArmIk.cs` / `FighterMath.cs` | 2 ボーン IK → 関節サーボ目標、ホバー/移動/拳の PD、歩行（純関数） |
| `BoxingGame.cs` | リング・2 体・CPU・ヒット判定・ラウンド進行。物理は 240Hz サブステップ |
| `PuppetMapping.cs` | HMD・コントローラ → 人形の体フレーム（純関数） |
| `AiControl.cs` | CPU（間合い・パンチ・掴み）。seed 固定で決定的 |
| `BoxingRenderer.cs` / `Ring.cs` | 描画データ（体力バーもワールド空間なので VR でも見える） |
| `LowPoly/` | ローポリの体（`BoxerShapes`・`Lathe`・`LowPolyMesh`）、打撃のへこみ（`DentedMesh`・`BoxerSkin`）、変形した三角形の描き方（`RightTriangles`）。見た目だけで、当たり判定は箱のまま |

### ファイターの制御（1 サブステップ = 1/240 s）

```
Fighter.UpdateGrab   グリップの立ち上がりで、拳から 4cm 以内の相手部位へ BallSocket（breakable）
Fighter.ApplyControl
  Standing:  ホバー PD（骨盤高さ・重力は常に満額）＋ 骨盤/胸の直立 PD（相手へロックオン）＋ 水平速度 P   … 力積で付与
             脚: AddLegServos の股 AngularServo / 膝・足首 TwistServo に正弦歩行の目標（後退時は位相逆回し）
             首: AngularServo で直立へ
             腕: IK → 肩 AngularServo（上腕の回転）・肘 TwistServo（曲げ角）
                 ＋ 拳を目標へ PD 力（目標速度フィードフォワード・上限 35N・反作用の半分を胸へ）
  Down / GettingUp: 0.3 秒かけて力を抜いて崩れ、その後はいつも立とうとする。腰を上げられる高さ（StandReach）は意識で連続に決まり
             （床 → 膝立ち・座り込み → 中腰 → 立つ）、意識が戻るのに合わせて RiseTime の速さでゆっくり上げる（Rise）。
             腰が DownHeightRatio より下なら Down、上なら GettingUp（審判のカウントはどちらも続く）。
             腰が RecoverHeightRatio まで戻り、上体が起きた姿勢を RecoverHold 保てば Standing（脚を失った・死んだなら起きない）
PhysicsWorld.Step → Contacts() から「前腕 × 相手の部位」を拾い、衝突直前の拳速度のうち相手へ踏み込む成分でダメージ
```

位置や姿勢を直接書き換えるのは、生成直後に向きを合わせる処理（`Fighter.Spawn`）だけです。ラウンドのリセットではワールドごと作り直します。Aigis では剛体の削除がハンドル再利用の点で危険なためです。

## VR ホスト（games/MiniRing.Vr）

```
XrSession.Create
  xrCreateInstance(XR_KHR_vulkan_enable) → xrGetSystem(HMD)
  → ランタイムが要求する Vulkan 拡張で VulkanContext（インスタンス → ランタイム指名の GPU でデバイス）
  → xrCreateSession(GraphicsBindingVulkanKHR) → Stage 空間 → アクション → 目ごとのスワップチェーン(R8G8B8A8_SRGB)
毎フレーム RunFrame
  PollEvents（READY で BeginSession / STOPPING で EndSession）→ xrWaitFrame → xrBeginFrame
  shouldRender なら: LocateViews → 初回だけ再センタリング → SyncActions・手/スティック/グリップ/A・X
                    → game.Step(入力, 1/表示Hz) → 各目: Acquire/Wait → 描画 → Release
  xrEndFrame（投影レイヤー 1 枚）
```

### 描画の流れ

Aigis のレンダラーは自前のデバイスを作り、出力先は自分のウィンドウか CPU 読み戻しに限られます。そこで `HeadlessRenderer` に**外部デバイスモード**を追加しました。

- XR セッションに渡したデバイスの上で、普段どおり R8G8B8A8_UNORM のオフスクリーンへ描きます。
- 描いた結果を `vkCmdCopyImage` でスワップチェーン画像（SRGB）へビット単位でコピーします。
- Aigis の出力はガンマ済みの値なので、SRGB 画像に入れればコンポジタが正しく復号します。

コピーの前後で画像のレイアウトを遷移させ、OpenXR の約束である COLOR_ATTACHMENT_OPTIMAL に戻します。`--selftest` は、この経路が通常の描画とバイト単位で一致することを確かめます。

### 座標系

| 空間 | 前方 | 備考 |
|---|---|---|
| OpenXR | −Z | Stage 空間で、床が y=0 |
| ゲーム | +Z | プレイヤーは原点に立ち、+Z 方向約 1m にリング |

XR 空間からワールドへは Y 軸の 180° 回転で変換します（`XrSession.WorldFromXr`）。目・頭・手のすべてに同じ変換を適用しています。

**左右**: Aigis は右手系なので、+Z を向いた体の右手は −X 側です（右 = 前 × 上）。`Hand.LocalSide()` が体フレームでの X 符号（左 +1・右 −1）を返し、`BodyFrame.Right` も同じ規約です。Aigis の `HumanoidRagdoll` は −X 側の腕を "Left" と呼ぶので、`Fighter` は部位名を入れ替えて本人の左右に対応させています（テスト `Hand_ReachesStraightPunchTarget` で拳の側を確認）。
