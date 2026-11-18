# 作業指示（2026-11-16）: 片手剣と盾、剣盾の剣士 1 人

作業場所: ワークツリー `.claude/worktrees/swordshield`（ブランチ `claude/swordshield`、master の今の先頭から）だけ。master・他のワークツリーには触らない。コミットはワークツリーの中でだけ。プッシュしない。日本語。

最初に読む:
- `CLAUDE.md`（§1 全部、特に §1.2・§1.3・§1.4・§1.5・§1.7・§1.8、§4、§6、§8 の武器・はめる物の近道）
- `docs/VOXEL_WEAPONS.md`（材質・接触・曲げ・§5.5 自傷・§5.6 技の時計）、`docs/WEAPON_DEFENSE.md`、`docs/CHARACTERS.md`、`docs/plans/CHARACTER_EDUCATION_PLAN.md`
- 直前に入った例: `docs/BOJUTSU.md`（両手の型を `FormSwordsman` に足した）、`docs/TONFA.md`（はめる物を足した・左手の鏡 `Weapon.MirroredOn`）
- コード: `games/MiniRing/Weapons/Weapon.cs`（`IronSword`・`IsWorn`・`CreateWorn`・`WornBoundsOf`・`MirroredOn`）・`HandWear.cs`、`BoxingGame.Weapons.cs`（`Arm`・`Wear`: 武器を握った後、空いている手にはめる物をはめる）、`BoxingGame.cs` の `WeaponBot`（今 `IronSword` には武器のボットが無い）、`games/MiniRing/Bots/`（`FormSwordsman.cs`・`SwordsmanBot.cs`・`WeaponStyle.cs`・`weapons/*.json`・`WeaponDefense.cs`）、`Motion/StaffForms.cs`、`games/MiniRing/Characters/`（`roster/*.json` の `body.weapon`・`body.wear`）、Arena の `handlelab`・`guardlab`・`selfcuts`・`rosterleague`・`video`

## ユーザーの指示
「トンファー、片手剣と盾、テコンドー、棒術を加えて」。これが最後の 1 つ。

## 決めたこと
- **盾 `HandWear.Shield` / `WeaponKind.Shield`**:
  - 左手にはめる物として、今のはめる物と同じ道を通す（`Arm` で右手に片手剣、`Wear` で空いた左手に盾）。
  - 形は丸い木の板（厚み）に鉄の縁と、中央の鉄の椀（拳を覆う）。手で握る形にする。
  - 寸法は実物（直径 60 cm 前後・板の厚み 1 cm 前後）を人形の縮尺で写し、根拠をコメントに書く。
  - 材料の値は今の木・鉄のまま。打たれれば木が削れ・割れ、縁の鉄は残る（材料から）。
  - `.vox` の書き出し（`weaponexport`）に出ること。
  - 盾は重く大きいので、腕の質量と腕の力（筋の断面、§1.7）で振り回しにくさが出るはず。盾の質量は格子の材質から出す。
- **片手剣**: 今の `IronSword` を使う（形・材料は変えない）。
- **型は目標の経路**（§1.2）: 右手の剣の型（片手の斬り下ろし・横の払い・突き）を `FormSwordsman` の表に足す（両手の間隔 0 の片手の握り）。
- **盾の構えと受けも左手の目標の経路として書く**: 相手の武器・拳の来る線と体の間へ盾を差し入れる。今の `WeaponDefense` の受けの考えを左手に使う。盾で押してから斬る（盾で打つ・押す）も経路で入れてよい。
- **§1.3 を守る**: 「盾なら」「片手剣なら」の分岐を判断の式に書かない。違いは武器の長さ・重さ・握り・はめる物の大きさ（見える物）と JSON の値から出す。今そうなっていない所は報告する。
- **ボット**: `BoxingGame.WeaponBot` で `IronSword` にも型の剣士（`FormSwordsman`）を使う。左手に盾がある時の守りは、盾の見える大きさから。
  - 判断の値は教科書 `Bots/weapons/swordshield.json`（`WeaponStyle`）として持ち、`education.weaponBot` で上書きできる形にする。
  - 盾を持たない片手剣の今の試合が変わる場合は、変わった理由を書く。
- **キャラクター 1 人**: `Characters/roster/<id>.json`。架空の人物・架空の名前、国は `countries.json` から。`body.weapon` は片手剣、`body.wear` は盾、教育は空 `{}`。メニュー（アイコン・肖像）・`rosterleague` に出ること。
- **版**:
  - 盾の形・当たりが増える。既存の試合が変わらないなら `PhysicsVersion` は据え置き（変わるなら 36 に上げて理由を書く）。
  - ボットの判断を足すので `BoxerBot.LogicVersion` を 35 → 36 に上げ、履歴に 1 行。
  - 既存の試合のチェックサム（`CharacterSamenessTests`・`StepCostRegressionTests`）が通ること。

## 確かめ（完了の条件）
- **テスト**: 次のことを確かめるテストを書く。
  - 盾のボクセル（直径・厚み・質量・木と鉄の配置）
  - 右手に剣・左手に盾がはまる
  - 片手剣の型が台本の指令で、構えて立つ相手に当たる
  - 盾の受けで相手の刀・こんぼうの振りが盾に当たり、体のセルが削られない（盾を下げた場合と比べて）
  - 自傷の数（`selfcuts` 相当）
  - 教科書・キャラのファイルの読み込みと検証
- **測定**:
  - `handlelab`・`guardlab` の表を剣盾も含めて取り、`docs/arena/swordshield_handle.md` に書く。
  - 試合: 剣盾の剣士 対 刀（jin）・ロングソード（konrad）・こんぼう（sean）・棒術家（daichi）・トンファー（tsuyoshi）・空手家（akira）・ボクサー（marcus）を各 4 試合以上（`rosterleague`）。使っていない種でも 1 回。結果は `docs/arena/swordshield_league.md` に書く。
  - 比べもの: 同じキャラで盾を外した場合（グローブ）も取る。勝てなくてよい。弱い所を報告に書く。
- **動画**（`media_tmp/` に置き、git に入れない）:
  - 剣盾 対 刀 40 秒（`video --red-char <id> --blue-char jin --seconds 40 --speed 1`）
  - 盾の受け・片手の斬りの 4 倍スロー
  - 盾が左手で構えて見えるか・受けで盾に当たるか・剣が片手で振られるかを自分でコマを見て確かめ、その秒を報告に書く。Claude が見て確かめる。
- **全テスト**: `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"` を最後に通しで回す（一部だけの再実行で済ませない）。README の件数を合わせる。
- **文書**: 次を直す。新しい `docs/SWORD_SHIELD.md` を作ってもよい。
  - `docs/VOXEL_WEAPONS.md`（盾・片手剣の型）
  - `docs/WEAPON_DEFENSE.md`（盾の受け）
  - `docs/CHARACTERS.md`
  - `CLAUDE.md` §3・§8（新しい近道）

## 触らない
- `PunchBook.cs`・`DefenseBook.cs`・`Technique.cs`
- `Tv*.cs`（`TvShowGraphics.cs` の武器の名前の表に 1 行足すのだけはよい）
- `VideoMaker.cs`（武器の名前の表に 1 行足すのだけはよい）
- 生配信の作業（`Live*.cs`・`Audio/Broadcast.cs`・`HeadlessRenderer.cs`・`tools/tvshow/`・`docs/TV_BROADCAST.md`）。master で別のセッションが変更中。
- `games/MiniRing.Arena/Program.cs` は足す行を最小に。生配信の作業が同じファイルを変えている。
- `docs/plans/CODEX_BRIEF_*`
- 既存のキャラの体・顔の値、既存のスタイル・武器の教科書の値
- 体の物理（`Fighter*.cs` の体の部分・`Voxels/` の体・`Footwork.cs`）

## 報告
`docs/plans/done/SONNET_REPORT_SWORDSHIELD_2026-11-16.md` に書く。先頭に「完了」か「未完了」。中身:
- 盾の形と質量
- 型と盾の受けの経路
- ボットと教科書（JSON の要点）
- 表
- 試合の結果と弱点
- 動画の場所と秒
- テスト件数
- §8 に足した近道
- 確かめていない所

最終メッセージは 1 回だけ。背景の処理を残さない。
