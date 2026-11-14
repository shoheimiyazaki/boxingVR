# 空手の継続作業: 同時編集検出時点の引継ぎ

作業1・2は未完了。コミット・プッシュなし。

## 同時編集

開始時には存在しなかった LegCommand.cs / KickSpec.cs / BareHandState.cs と Fighter.cs の脚制御が、本セッションの担当者が production を編集する前に出現した。KarateFistTests.cs にも、本セッションが書いていない期待値修正が入った。共有ファイルの編集を停止し、ユーザーへ別セッションの編集有無を質問した。

こちらの担当者が追加したテストは HandShapeTests.cs / KickTests.cs / KarateBotTests.cs / FistParryTests.cs。BotStyle / BoxerBot / BotCatalog / DefenseBook / Technique / karate.json は担当者の途中変更と外部変更が混在し、未統合。途中変更を一括で戻すと外部の変更も失うため、リセットしていない。

## ビルド・テスト

- 初回 restore は実行ユーザーの違いでパッケージキャッシュを見失い NU1301。
- `dotnet restore tests/MiniRing.Tests --packages C:/Users/smiya/.nuget/packages --ignore-failed-sources -p:NuGetAudit=false` で既存キャッシュから復旧。以後 `--no-restore` を用いる。依存関係・追跡対象の NuGet 設定は変更していない。
- 現ソースのビルドは KickKind と KickTechnique の不整合、LegReach 重複などで失敗。454件成功という前回報告を今回の変更の検証結果として使わない。
- 未ビルドの既存 Release バイナリによる全テストも別途開始したが、これは最新ソースの検証にならない。

## 作業1の判断材料

既存 VoxelContact は p=E/(give*A)、塑性仕事=強度*A*進行距離。有効質量は接触位置・慣性にも依存する。同じ初期質量と速度でも、形によって回転へ逃げる成分と壊す面積が異なる。狭い拳頭で圧力が上がることから、総欠損体積が必ず増すとは導けない。前回の7/8m/sの測定はその反例。総削れ量を強制する威力倍率の追加は不適切。期待値を変更する場合も、最新実装で数値テストを再実行して記録する必要がある。

## レビューで見つかった要修正点（編集中のスナップショット）

1. BareHandState の形別 Geometry が異なるセル数・組織量を生成し、形切替で質量が変わる。16節のうち親指に相当する12..14が未使用。
2. Capture は残存HPを比率にし、Build はセル数へ切り捨てて投影。部分摩耗・染み・へこみの情報を失い、切替で累積損傷が発生し得る。canonicalな組織セル状態の全単射が必要。
3. Seiken は WornBounds の中心シフト済みだが Segment は別原点を前提とするため、形間の指節対応が不正。
4. ReplaceGrid は物理の形と質量も整合させる必要がある。剛体ID・関節・速度を保ちつつ、親の残存組織から質量を一意に決める。
5. ReplayFile は Version=4で新手形・脚指令の保存なし。親損傷・手形の checksum 反映も必要。
6. 蹴り中も Footwork.Update が両脚を進め、軸足が遊脚になる可能性。両脚指令中の hover 支持も検証が必要。
7. KneeTargetLocal は曲げ平面方向にしか効かず、狙った膝位置を保証しない。各蹴りで実際の部位接触・支持を測る必要がある。
8. 脚 Orientation の有限値・正規化検証が不足。Footwork.Enabled=false では脚指令が無視される。Spring は未使用。
9. 脚打撃を拳と同じ圧力・有効質量・運動量の経路へ統合し、PhysicsVersionを更新する。
10. ParryGlove に存在しない BodyFrame.ToLocal 呼出し。パリーの調整値が未使用、防御方向から脅威手を推測するため逆手を参照し得る。実際の脅威手を渡し、接触による偏向を測る。

再開時はまず同時編集の担当を一本化し、API不整合を解消する。その後 RED→GREEN、段階ごとの全テスト、ドリル・PDCA、動画のコマ確認、文書を完了する。
