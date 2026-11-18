完了

# 作業報告: 試合の刀・ロングソードを、それぞれの型で動かす（2026-11-15）

- 新規 `games/MiniRing/Bots/FormSwordsman.cs`: 型（`SwordForms`）から技・構え・間合い（`StrikeDrill.ReachOf`、0.33 m 未満は除く）・剣士のボットを組む一つの組み方。プレイヤーの弱・強の型（刀: tsuki / kesa、ロングソード: stich / oberhau）もここ。
- `BoxingGame.WeaponBot`: 刀・ロングソードは `FormSwordsman.Bot`（`SwordBot.Basic` をやめた）。`LogicVersion` 26 → 27。`PhysicsVersion` は不変。
- `SwordLibraryLab.Bot`（Arena）も `FormSwordsman.Bot` を通る。
- `DesktopFighterControl`: 刀・ロングソードの弱・強は型を `MotionImitation.ToCommand` で出す（ボットと同じ経路）。鉄の剣・棍棒は従来どおり。
- `VideoMaker` / `Program.cs`: `video --red-weapon / --blue-weapon`（ゲームと同じ UseWeaponBots の道で撮る）。
- テスト `FormSwordsmanTests`（11 件）、全 577 件通過（Probe 除く）。README 566 → 577。
- 文書: `docs/VOXEL_WEAPONS.md` §4.2、`docs/INTENT_CONTROL.md`。
