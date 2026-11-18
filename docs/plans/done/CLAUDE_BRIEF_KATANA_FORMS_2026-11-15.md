# 作業指示 14（2026-11-15）: 試合の刀・ロングソードを、それぞれの型で動かす

最初に `CLAUDE.md`（§1.1・§1.2・§1.5・§2・§4）、`docs/VOXEL_WEAPONS.md`、`docs/INTENT_CONTROL.md` を読む。日本語。コミット・プッシュはしない。作業ツリーには未コミットの頭の作業（`boxer.vox`・`PhysicsVersion` 31）があるが正しい現状なので戻さない。

## ユーザーの指摘
デスクトップ版で刀を選ぶと、動きがロングソード（刀らしくない）。

## 原因（Claude の調べ）
- CPU: `BoxingGame.WeaponBot`（games/MiniRing/BoxingGame.cs 1265 行あたり）が刀もロングソードも `SwordBot.Basic`（`SwordTechnique.Basics` の片手剣の汎用の振り）。
- プレイヤー: `games/MiniRing.Desktop/DesktopFighterControl.cs` の弱・強が武器を問わず `SwordTechnique.Basics` の kote・men。
- 武器ごとの型 `SwordForms.Katana()`（両手・`KatanaSpacing`）・`SwordForms.Longsword()`（`LongswordSpacing`、[0] は構えの型）と、それで戦う `SwordsmanBot` はあるが、Arena（`SwordLibraryLab.Bot`・`VersusLab` の日本刀）でしか使っていない。

## やること
1. **CPU**: 刀・ロングソードは、その武器の型（`SwordForms` の手で作った型だけ。BVH のモーションキャプチャは使わない＝ゲームは外部ファイルに頼らない）から `MotionTechnique` を作り、`SwordsmanBot` で戦わせる。構え（guard）・間合い・刃の長さ・握りの間隔・`SwordStance` の設定・`DisarmedFallback.WithFists` は Arena の `SwordLibraryLab.Bot` と同じ組み方にし、共通部分はゲーム本体（games/MiniRing）へ移して Arena もそれを使う（§1.5、一つの組み方）。刀の構えは刀の型の始まり（中段）から取る。技の間合い（Range）は Arena の決め方（物打ちが届く距離）を使うか、型から求める。
2. **プレイヤー（デスクトップの意図操作）**: 弱・強を、持っている武器の型から選ぶ（例: 刀 弱 = 突き・強 = 袈裟、ロングソード 弱 = Stich・強 = Oberhau/Zornhau）。実行部はボットと共有（§2: `SwordsmanBot` の技の実行、または同じ `MotionTechnique` の経路）。片手剣（IronSword）・棍棒は今のまま。VR はプレイヤーの手がそのままなので変えない。
3. `BoxerBot.LogicVersion` を上げ、履歴に 1 行。体の物理は変えない（`PhysicsVersion` は上げない）。
4. 文書: `docs/VOXEL_WEAPONS.md`・`docs/INTENT_CONTROL.md` の該当節。

## 確かめ
- テスト: 刀の試合と ロングソードの試合で CPU とプレイヤーの意図操作が出す技の名前が、それぞれの型の名前であること（刀に Longsword の名前、ロングソードに Katana の名前が出ない）。刀の試合で両手が握りの間隔を保って振ること（数値）。記録の再生（`ReplayTests`）全通過。
- 動画: `dotnet run --project games/MiniRing.Arena -c Release -- video` 相当で、刀同士とロングソード同士を各 20 秒（武器の指定の仕方は Arena の既存オプションを調べる。無ければ最小限で足す）。`docs/video/gen13_katana_forms.mp4`・`gen13_longsword_forms.mp4`。中のコマを数枚 PNG に抜いて、刀が両手の刀の型で振っていることを自分で見る。
- 全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`。README の件数。
- 最終報告 `docs/plans/SONNET_REPORT_14_2026-11-15.md`（先頭に「完了」か「未完了」）。最終メッセージは 1 回だけ。背景の処理を残さない。
