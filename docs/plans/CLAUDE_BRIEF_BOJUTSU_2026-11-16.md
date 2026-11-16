# 作業指示（2026-11-16）: 棒術（木の六尺棒）と、棒術家 1 人

作業場所: ワークツリー `.claude/worktrees/bojutsu`（ブランチ `claude/bojutsu`、master の今の先頭から）だけ。master・他のワークツリーには触らない。コミットはワークツリーの中でだけ。プッシュしない。日本語。

最初に読む: `CLAUDE.md`（§1 全部、特に §1.3・§1.4・§1.7・§1.8、§4、§6、§8 の武器まわり）、`docs/VOXEL_WEAPONS.md`（材質・接触・曲げ・§4・§5.5 自傷・§5.6 技の時計）、`docs/WEAPON_DEFENSE.md`、`docs/CHARACTERS.md`、`docs/plans/CHARACTER_EDUCATION_PLAN.md`、`games/MiniRing/Weapons/Weapon.cs`（`WeaponKind`・`Build`・`GripOf`・`TwoHandGripsOf`・`TipOf`・`CanTwoHand`・`IsBlunt`）、`WeaponSwing.cs`・`SwordTechnique.cs`、`games/MiniRing/Bots/`（`ClubBot.cs`・`FormSwordsman.cs`・`SwordsmanBot.cs`・`WeaponStyle.cs`・`weapons/*.json`・`WeaponDefense.cs`・`DisarmedFallback.cs`）、`Fighter.Wield`、`games/MiniRing/Characters/`、Arena の `handlelab`・`guardlab`・`selfcuts`・`rosterleague`・`video`、テコンドーの作業で足した `docs/TAEKWONDO.md`（新しいスタイル・キャラの足し方の例）。

## ユーザーの指示
「トンファー、片手剣と盾、テコンドー、棒術を加えて」。テコンドーの次に棒術。

## 決めたこと
- **武器 `WeaponKind.Staff`**: 木の長い棒（材質は棍棒と同じ木。断面は丸くほぼ一定の太さで、両端が打つ所）。長さは体の寸法から決める（基準体格の身長ほど。値の根拠をコメントに）。ボクセルで作り、曲げ（`VoxelBending`）・削れ・折れは今の木の値のまま（材料の値を変えない）。.vox の書き出し（`weaponexport`）に出ること。
- **握り**: 両手を肩幅ほど離して棒の中ほどを握る（棍棒の隣り合った両手と違う）。`TwoHandGripsOf` に棒の握りを足し、`CanTwoHand`・`IsBlunt` に入れる（種類の一覧に足すのは今の近道のまま。§8 に既にある）。両手の間隔が広いので、棒の向きは両手の位置の差で決まる（てこ）。握りの位置を持ち替える動きは今回は入れない（§8 に書く）。
- **技は目標の経路**（§1.2）: 両手の目標の時間変化として書く。少なくとも、突き（前の端で直線に）、打ち（上から振り下ろす・横から払う。両手のてこで端を速く回す）、足払い（低く横に）、返し（打った端と逆の端で続けて打つ）、受け（棒の中ほどで相手の武器・拳を受ける。`WeaponDefense` の受けの考え）。どちらの端で打つかは、端の位置と相手との位置関係から連続に（§1.3。「突きなら前の端」の分岐を判断に書かない）。
- **ボット**: 既存の武器のボット（`FormSwordsman`/`SwordsmanBot`・`ClubBot`）の仕組みに合う方を選んで棒を動かす。判断の値は `WeaponStyle` の教科書 `Bots/weapons/staff.json` として持ち、`education.weaponBot` で上書きできる形に（剣・こんぼうと同じ）。武器の種類で判断を分けず、長さ・重さ・握りの間隔から出す（今そうなっていない所は報告）。
- **キャラクター 1 人**（`Characters/roster/<id>.json`、架空の人物・架空の名前。国は `countries.json` から。武器は棒、教育は空 `{}`）。メニュー・`rosterleague` に出ること。
- 版: 新しい武器を足すので形・当たりの物理が増える。既存の試合が変わらないなら `PhysicsVersion` は据え置き（変わるなら 36 に上げて理由を書く）。ボットの判断を足すので `BoxerBot.LogicVersion` を 35 に上げ、履歴に 1 行。既存の試合のチェックサム（`CharacterSamenessTests`・`StepCostRegressionTests`）が通ること。

## 確かめ（完了の条件）
- テスト: 棒のボクセル（長さ・質量・両端）、両手の握りの位置、各技が台本の指令で構えて立つ相手に当たる、どちらの端が当たったか、受けで相手の振りを止める（剣・こんぼうの振りに対して）、自傷の数（`selfcuts` 相当）、教科書・キャラのファイルの読み込みと検証。
- 測定: `handlelab` の表を棒も含めて（振りの数・当たった割合・欠けたセル・自傷・守り）、`docs/arena/bojutsu_handle.md`。試合: 棒術家 対 刀（jin）・ロングソード（konrad）・こんぼう（sean）・空手家・ボクサーを各 4 試合以上、`docs/arena/bojutsu_league.md`。勝てなくてよい。弱い所を報告に。
- 動画: 棒術家 対 刀 40 秒（`video --red-char <id> --blue-char jin --seconds 40 --speed 1`）と、突き・打ち・足払いの 4 倍スロー（`media_tmp/`、git に入れない）。棒が両手で握られて見えるか・両端を使うか・当たる所を、自分でコマを見て秒を報告に。Claude が見て確かめる。
- 全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"` を最後に通して回す（一部だけの再実行で済ませない）。README の件数。
- 文書: `docs/VOXEL_WEAPONS.md`（棒）・`docs/WEAPON_DEFENSE.md`（受け）・`docs/CHARACTERS.md`・`CLAUDE.md` §3・§8（新しい近道）。新しい `docs/BOJUTSU.md` を作ってもよい。

## 触らない
`PunchBook.cs`・`DefenseBook.cs`・`Technique.cs`、`Tv*.cs`、`docs/plans/CODEX_BRIEF_*`、既存のキャラの体・顔の値、既存の武器の教科書の値（`katana.json`・`longsword.json`・`club.json`）、体の物理（`Fighter*.cs` の体の部分・`Voxels/`・`Footwork.cs`。握りのために `Fighter.Wield` の両手の扱いを広げるのはよい）。

## 報告
`docs/plans/SONNET_REPORT_BOJUTSU_2026-11-16.md`（先頭に「完了」か「未完了」）: 棒の形と握り、技の経路、ボットと教科書（JSON の要点）、表、試合の結果と弱点、動画の場所と秒、テスト件数、§8 に足した近道、確かめていない所。最終メッセージは 1 回だけ。背景の処理を残さない。
