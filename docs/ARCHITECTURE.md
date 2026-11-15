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

## ライブのスレッドと状態の受け渡し

Desktop と VR は `FixedSimulation<TInput, TSnapshot>` の専用スレッドで、1 歩を常に **1/240 秒**として進めます。描画は完成した最新のスナップショットだけを読みます。物理が遅いときは実時間への追いつきを捨て、試合時間がゆっくり進みます。描画は物理の完了を待ちません。240 Hz は試合内の固定刻みであり、重い歩でも実時間で毎秒 240 歩を保証する意味ではありません。

```text
描画・入力スレッド（90 Hz 以上を目標）
  デバイスを読む ── Submit（最新の連続入力＋未処理の操作）──┐
  完成した写しを読む ←── Latest（完成後に参照を差し替え）──┤
    ├─ 姿勢・HUD・カメラ・パーティクル                  │
    ├─ ボクセルの差分を既存 GPU テクスチャへ書き込む      │
    └─ VR: 現在の HMD 姿勢で両目を描く                  │
                                                       │
物理専用スレッド（単一所有、1 歩 = 1/240 秒）             │
  入力を採用 ←─────────────────────────────────────────┘
    → 試合を 1 歩進め、採用した入力と dt を同じ歩番号の順に記録
    → 最大 90 Hz で完成した写しを公開
    ├─ 毎歩の音イベントをコピー → 信頼できる列 → 音専用スレッド
    └─ 毎歩の VR 振動をコピー → 信頼できる列 → XR の振動出力
```

`IPresentationGame` を実装するゲームでは、`GameHost` は同期の追いつきループを使わず、表示更新ごとに `SubmitPresentation` を呼びます。Desktop の `Presentation/SnapshotMatchGame` は、同じ写しをそのフレームのシーン・HUD・顔カメラに使います。VR の `VrSimulation` も同じ固定歩の実行器を使います。Arena の動画・ヘッドレス計算は同期のままです。

### 入力・記録・速度

連続入力は未処理の最新値を採用します。リセットの押下は未処理の間保持し、VR のキャリブレーションは押した瞬間の姿勢ごと列に残します。キャリブレーション・振動・音を、差し替えられる最新シーンだけに載せて取りこぼすことはしません。

`FixedSimulation` のコールバックは実際に入力を適用する 0 始まりの歩番号を受け取ります。記録は描画フレームではなく物理の歩の中で行い、採用された入力と `dt` を同じ順に保存します。再生はその順序と刻みを使い、チェックサムを照合します。壁時計は次に物理を実行する時刻と写しの公開間隔だけを決め、物理の計算へは入りません。

Desktop の `GameSpeed` は物理の固定刻みを変えず、歩を実行する実時間の間隔を変えます。0.75 倍なら、余裕があるとき実時間 1 秒で試合が 0.75 秒進みます。負荷が高いとさらに遅くなります。描画と頭の追跡は表示側の速さで続けます。

### ボクセルと一過性のイベント

`VoxelSnapshotProducer` は元の `VoxelVolume` の同一性と `Revision` で複製を共有し、変更された版の RGBA 配列だけをコピーします。写しは元の格子や変更される配列を保持しません。`VoxelSnapshotConsumer` は描画専用の同じ `VoxelVolume` を保ち、変更セルを反映します。GPU は変更範囲を既存の 3D テクスチャへ転送するため、損傷のたびにテクスチャを作り直しません。GPU の更新版を飛ばした場合は全面転送へ戻します。

シーンの写しは最大 90 Hz で作りますが、音と VR 振動は毎歩収集します。`ReliableEventWorker` が受け付けた音イベントを音専用スレッドで順に処理し、終了時にも残りを処理します。音の合成・再生が描画や物理の処理を占有しません。音・カメラ・HUD・GPU の状態は試合の計算へ戻しません。

### メニューからの開始と終了

`MiniRing.Desktop/MenuGame` は試合・再生の構築をバックグラウンドで開始し、完成するまで「試合を準備中…」を描きます。初期化の重い処理も描画フレームで待ちません。メニューへ戻る際の物理スレッド停止・音の残りの処理・リソース解放も別タスクで行い、失敗はログと画面へ通知します。アプリケーションの終了時には残った処理の完了を待ちます。

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
| `IntentControl.cs` / `IntentScript.cs` | ホスト非依存の意図選択と、試合の時刻で動かすキーの台本。デスクトップだけが実キーを読む |
| `Bots/TechniqueExecution.cs` | ボットと意図操作が共有する拳・ガード・頭・溜め・ひねりの目標経路。判断や乱数は持たない |
| `BoxingRenderer.cs` / `Ring.cs` | 描画データ（体力バーもワールド空間なので VR でも見える） |
| `FaceCam.cs` | 画面の両脇に出す両ボクサーの顔の窓（左が赤コーナー、右が青コーナー）（往年の FPS の HUD の顔のような）。カメラは頭の剛体（＝頭のボクセルの格子）の座標系に固定し、顔の正面（ローカル +Z）から頭を向く。同じ場面をもう 1 つのカメラで描くだけなので、グローブ・相手の拳・部屋も写り、頭が振られると窓の中で背景が流れる。デスクトップのホストは `GameHost.Options.DynamicViews` で、画面全体の上に重ねる窓（`CameraView.IsOverlay`）として描く。動画は `video --facecam on`（ヘッドレスでは窓の大きさで別に描いて書き写す）。描画だけで、試合・記録には響かない。VR はまだ（視野に固定した板として出すのが次の段） |
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
                    → VrSimulation.Submit(入力) → 最後に完成したシーンと振動の列を読む
                    → 各目: Acquire/Wait → 現在の HMD 姿勢で描画 → Release
  xrEndFrame（投影レイヤー 1 枚）
物理専用スレッド: 入力を採用 → game.Step(入力, 1/240 秒) → 写しを公開（表示スレッドを待たせない）
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
