# 報告: 片手剣と盾（2026-11-16）

**完了**（確かめていない所は末尾）

## 入れたもの
- 盾 `HandWear.Shield` / `WeaponKind.Shield`: 直径 16 cm の木の板（厚さ 4 mm）、縁の鉄の輪、拳の前を覆う鉄の椀、鉄の握り棒。質量 179 g（鉄 120・木 47・拳 12。鉄の剣 85 g・グローブ 45 g）。`weaponexport` に出る。寸法は人形の肩幅比 0.27 倍の写し。
- 片手の型 `Motion/SwordShieldForms.cs`（tsuki・kiri-oroshi・yoko-barai）。左手は盾の位置としてキーに別に書く（`SwordKey.Left`、`MotionFrame.LeftFacing`）。`FormSwordsman.HasForms` で片手剣も型で動く（`Handles` は両手持ちのまま）。
- 盾の受け `WeaponDefense.Cover` + `SwordDefense.Cover`（既定 false）+ `SwordsmanBot.OffhandRadius`（見える盾の半径）。盾かグローブかで式は分けない。
- 教科書 `Bots/weapons/swordshield.json`（`education.weaponBot` で上書き可）、キャラ `edgar`（エドガー・ソーンヒル、イギリス）、メニュー・TV 見出しの絵と名前、`handlelab`（剣盾・`--style-swordshield`）、`guardlab --defender swordshield`。
- `LogicVersion` 36。`PhysicsVersion` は 35 のまま（`CharacterSamenessTests`・`StepCostRegressionTests` が通る）。

## 表の数字（詳細 `docs/SWORD_SHIELD.md`、生の記録 `docs/arena/swordshield_*.md`）
- 試合（8 人、各組 4 試合 × 40 秒）: edgar 種 1 で 21-1-6・77 %（2 位、jin 84 %）、使っていない種 7 で 18-1-9・66 %（3 位）、盾だけグローブ 16-0-12・57 %。
- handlelab（84 試合）: 剣盾は当たった振り 77 %、自傷 4 セル/試合（刀 70・ロングソード 125・棒 107）、勝-分-負 16-16-4。
- guardlab（剣盾の受け手）: 守らない 1.62 → 盾を構えの所に置く 0.92 → 覆う動き 0.84（確かめの組は 0.19 → 0.12）→ 打って離れる + 覆う 0.84・打たれた本数 16/38。**効きの大半は盾が体の前にあること。覆う動きの上乗せは小さい。**
- テスト `ShieldTests`: 刀を 1 度振る相手に、盾を下げた構えは体のセル 2982 失い、盾を構えの所に置くと 246、覆うと 216。

## 弱点
- 刀の jin に勝てない（1.5/4・1/4）。右の上腕が早く折れる（動画で 4.5 秒）。
- 剣と自分の盾が打ち合う（既存の規則のまま。広げるとこんぼう × 素手のチェックサムが変わるので入れなかった）。8 試合で接触 0〜130 歩。
- 盾の向きは前向きのまま。盾で押す・打つ型は未実装。剣の受け流しとの併用は自傷が増える。
- デスクトップの意図操作は片手剣と盾に未対応。

## 動画（`media_tmp/`、git の外）
- `swordshield_vs_jin.mp4`（縮めた `swordshield_vs_jin_small.mp4`）: 40 秒、盾が左手で構えて見える。右腕の骨折と剣の落下は動画 8.0 秒。
- `sw_vs_konrad.mp4` の 4 倍スロー `swordshield_slow4x.mp4`（元の 5.2〜8.0 秒）。コマで確認: 5.45 秒 右手だけで振る剣、5.7 秒 相手の腕に当たる、6.95〜7.95 秒 盾の面に相手の剣先が当たる。

## テスト
全テスト 759 件（Probe 除く）。最後の通しで 759 件すべて成功（9.7 分）。README は 759 件に合わせた。

## §8 に足した近道
`CLAUDE.md` §8「片手剣と盾」: 型の左手の経路は固定の値、盾の向きは前向き、`Cover` の定数（0.95・0.285・0.6）は手で決めた値、剣と盾は打ち合う、盾の寸法は肩幅比。

## 確かめていない所
- デスクトップ・VR のホストで実際に遊んでいない（メニューの絵は描画テストの件数のみ確認、目では見ていない）。
- 盾の向き（`LeftFacing`）が手首のサーボで目標どおりに回っているかは、コマで見た範囲だけ。
- 教科書は 1 回で決め、盾の半径・Cover の定数・型のキーを振って比べていない。`--style-swordshield` の別案は未測定。
- コードレビュー（別のエージェント）の指摘のうち、Cover の定数を教科書の値へ移すこと、`Blocks` の数え方、`GuardLab.Shielded` の静的な状態は直していない。
