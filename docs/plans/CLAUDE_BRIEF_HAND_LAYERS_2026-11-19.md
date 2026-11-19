# 指示書: 手を別の格子で重ねる（はめる物・グローブ・握る手）2026-11-19

作業ツリー `E:\repos\ClaudeProjects\myvrgame1-layers`、枝 `claude/hand-layers`（master 8693fda から）。master を直接触らない。コミットは段ごとに 1 つ（`<type>: <説明>`、Co-Authored-By などの付記はしない）。プッシュしない。
最初に CLAUDE.md §1・§4 と docs/KARATE.md「手のボクセルの作り直し」、docs/DEBT.md「手の物性を木の値で代用」を読む。

## 背景（決まったこと）

- 素手の拳は `HandSkeleton`／`BareHandState`（5 mm のセル、幅 40 mm、材質 HandSkin/HandMuscle/HandBone = 見た目は皮膚・肉・骨、物性は木）。PhysicsVersion 51 で master に入った。
- ユーザーの決定: はめる物（メリケンサック・カタール・籠手・トンファー・盾・グローブ）の中の拳は、**同じ手のモデルの 5 mm の格子のまま**、はめる物の鉄・木・革・詰め物は **2 mm の別の格子**にして、**同じ剛体に重ねる**。剣・棒・バットを握る手も同じ手のモデル（`HandSkeleton.GripPose`・`HeldBar`）から作り、武器の剛体に重ねる。
- ユーザーの指示: ボクシンググローブは丸（今は半径 2.4 cm の球）ではなく、本物らしい高い質の形にする。

## 段 1: 1 つの剛体に複数の格子（エンジンと Weapon）

1. `src/Aigis.Physics/Voxels/VoxelBody.cs`: 同じ bodyId の VoxelBody を束ねる仕組み（例: `VoxelBodyGroup` か、VoxelBody に「仲間」を持たせる）。`UpdateMass` は束の全格子の質量（`materialMass` の密度 × 体積）の和と、境界の和集合で `ReshapeDynamicBox` を 1 回だけ呼ぶ。一つの格子だけが箱を書き換えて他を消すことが無いように。`SplitAt`/`Sever`/`SeverDisconnected` は物の格子だけに効けばよい（手の格子は切り離さない。切れた物の破片は手を持たない）。
2. `Weapon`: `Body`（物の格子。握る所の武器のローカル座標は今のまま）に加えて、手の格子のリスト（`Hands`: 各々 VoxelBody ＋ BareHandState ＋ 描画の Volume、`GridCenterLocal` = 手の格子の中心が武器のローカルのどこか）。素手（Fist）は物の格子を持たず手だけ、で一つのモデルにまとめてよい（Body が手の格子になる今の形を壊さない方法でも可。判断して理由を書く）。
3. `BoxingGame.Weapons.cs` ほか（調べた一覧: Resolve・MayTouch の武器対武器 :747-750、武器対部位 :813-815、`hit.A == weapon.Body` の照合、Wound・Shatter・chips :837-842 :1060、`_beforeStep` の写し :651 :733 :932 :984、描画 `BoxingRenderer` :52 :159、外接球 `BoxingGame.cs` :688-690、`StateChecksum` :1963）: 武器の全格子を回す。チェックサムは手の格子も含める。決定的であること（順は固定のリスト順）。
4. この段では見た目と試合を変えない形で入れる（手の格子のリストは空）→ 全テストがそのまま通ることを確かめてコミット。

## 段 2: はめる物の拳を手の格子に置き換える

- `Weapon.FistAt`（半径 13 mm の球）を物の格子から消し、正拳の手の格子（`BareHandState.InitialGrid`）を重ねる。物の形の定数（`DusterFront`・`KatarBarZ`・`TonfaHandleFrom`・`GauntletPadding`・`ShieldBossRadius`…）は拳の半径 13 mm を前提にしているので、新しい拳（幅 40 mm、奥行き 40 mm、`HandSkeleton.SeikenTip`）に合わせて作り直す（根拠のコメントを直す）。カタールの横木・トンファーの柄・盾の握り棒は手が握る棒なので、`GripPose` と `HeldBar`（棒の太さ）で手を巻く方が正しい。正拳で握れる物（メリケンサックの握りの棒）は正拳のまま。
- 握る所（`GripOf`）と腕の先の点（`HandSkeleton.Wrist`）を合わせる。`HasRigidFace`・`TipOf`・`CoverRadiusOf` が新しい形で正しく出ること。
- 手は腕の先の剛体ではなく武器の剛体の一部なので、質量は手の格子 ＋ 物の格子の和（段 1）。

## 段 3: ボクシンググローブを本物の形に

- 中に正拳の手の格子（5 mm）、外を 2 mm のグローブの格子で包む。形は球でなく、本物の 10〜16 oz のボクシンググローブの形を人形の縮尺（拳の幅 40 mm に合わせる）で:
  - 拳頭の前にふくらんだ打つ面（詰め物がいちばん厚い。人で 3〜4 cm → 拳の幅 9 cm に対する比で）、
  - 指先を巻き込んだ丸い先端と、掌の側のくぼみ（握り芯の棒）、
  - 親指の袋（拳の横・人さし指の側に沿い、先が拳頭の横へ付く。本体とつながる）、
  - 手首を包む長めのカフ（前腕の太さ 32 mm を少し越える筒。紐かマジックテープの帯）、
  - 表の革（コーナーの色）、詰め物（発泡材）、帯と縫い目は色違いの材質（物性は革のまま。色は `BoxerAnatomy` に足し、.vox の色が全部違うテスト `VoxelModelFileTests.MaterialColours_AreAllDifferent` を通す）。
- 形は解析の式（曲面の組み合わせ・滑らかな最小値）で書き、場合分けの寄せ集めにしない。詰め物の厚さは物性（打つ面の衝撃を広げる）の根拠をコメントに。
- 当たりの数値（グローブで削れにくい・`HandWearTests`）が変わったら、物理として説明できるかを確かめて直す。

## 段 4: 握る手を武器に付ける

- 剣・刀・ロングソード・棍棒・バット・棒・杖: 握る所（`GripOf`、両手なら `TwoHandGripsOf` の Top と Bottom）に `GripPose` の手の格子を重ね、柄の太さで `HeldBar` を取る（柄の軸は武器の X、手の格子の Z を柄に合わせる回転）。滑る握り（棒の `SlideSpanOf`）では手の格子の位置が握りの位置について動く（決定的に）。
- 手が当たる・削れる・握る手が傷めば握る力が落ちる、は今は求めない（当たりと見た目と質量だけ）。将来の項目として docs/DEBT.md に書く。

## 全段に共通

- 体の物理・当たりが変わる段（2・3・4）は `Fighter.PhysicsVersion` を上げ（bump-version スキル。master の 52 の次）、docs/VERSIONS.md の先頭に 1 行。チェックサムのテストは `CharacterSamenessProbe` 等で取り直し、理由を注釈に書く。
- 新しい振る舞いには数値で確かめるテスト（例: 武器の剛体の質量 = 手の格子 ＋ 物の格子の材質の質量の和、グローブの打つ面の詰め物は手の甲の側より厚い、握る手の格子は柄のセルと重ならない）。
- 見た目: `tests/MiniRing.Tests/HandShapeDumpProbe.cs` と `tools/hand_views.py`（正面・横・上の 3 方向）を、はめる物・グローブ・握る手にも広げ、画像を自分で見て評価する。
- 速いテスト（`run.cmd test`）を通し、段の終わりに raytrek で全テスト（`bash tools/remote-test.sh`。使用中なら待つ）。
- docs: docs/KARATE.md の手の節、docs/BOXING.md（グローブ）、docs/TONFA.md・docs/SWORD_SHIELD.md（握る手）、docs/DEBT.md の「手の物性を木の値で代用」から片付いた所を消す。

## 報告

段ごとに: コミットの id、変えたファイル、テストの結果（件数）、緩めたテストとその理由、画像のパス、残った問題。
