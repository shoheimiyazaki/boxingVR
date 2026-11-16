# 作業指示（2026-11-16）: トンファー（両手にはめる木の棒）と、トンファー使い 1 人

作業場所: ワークツリー `.claude/worktrees/tonfa`（ブランチ `claude/tonfa`、master の今の先頭から）だけ。master・他のワークツリーには触らない。コミットはワークツリーの中でだけ。プッシュしない。日本語。

最初に読む: `CLAUDE.md`（§1 全部、特に §1.2・§1.3・§1.4・§1.7・§1.8、§4、§6、§8 の「はめる物の近道」「拳の打撃の削れ方」）、`docs/VOXEL_WEAPONS.md`（材質・接触・曲げ・はめる物）、`docs/WEAPON_DEFENSE.md`、`docs/CHARACTERS.md`、`docs/plans/CHARACTER_EDUCATION_PLAN.md`、`docs/BOJUTSU.md`・`docs/TAEKWONDO.md`（新しい武器・スタイル・キャラの足し方の例）、`games/MiniRing/Weapons/Weapon.cs`（`WeaponKind`・`IsWorn`・`CreateWorn`・`WornBoundsOf`・`Worn(...)`・カタール・籠手の作り）・`HandWear.cs`、`BoxingGame.Weapons.cs`（`Wear`・はめる物の接触）、`games/MiniRing/Bots/`（拳のボットがはめる物で戦う道: `BoxerBot`・スタイル JSON・`karate.json`）、`games/MiniRing/Characters/`（`roster/*.json` の `body.wear`）、Arena の `rosterleague`・`video`・`attacklab`。

## ユーザーの指示
「トンファー、片手剣と盾、テコンドー、棒術を加えて」。テコンドー・棒術は済み。次がトンファー（その後に片手剣と盾）。

## 決めたこと
- **はめる物 `HandWear.Tonfa` / `WeaponKind.Tonfa`**: 今のはめる物（メリケンサック・カタール・籠手）と同じ道で、**両手に 1 本ずつ**（`Wear` は空いている手すべてにはめる）。木（材質は棍棒・棒と同じ木の値。材料の値を変えない）。形は、拳で握る横の握り（短い柄）と、そこから直角に前腕に沿って後ろへ伸びる長い棒（肘を少し越える長さ）と、拳の前へ少し出る短い端。寸法は実物（全長 50 cm 前後・握りの柄 12 cm 前後・太さ 3 cm 前後）を人形の縮尺（`Weapon.cs` の他の物と同じ考え）で写し、根拠をコメントに。`.vox` の書き出し（`weaponexport`）に出ること。
- **打ち方は今の拳の道のまま**（§1.5）: 拳の打撃は、はめた物のボクセルと相手のボクセルの接触（`VoxelContact`、圧力のモデル）で起きる。トンファーの前の短い端・握った拳の前の木の面で突く。前腕に沿った長い棒は、腕で受けたとき相手の拳・武器を木で受ける（腕の肉の代わりに木が削れる・折れる）。種類ごとの倍率・当たり判定を書かない（§1.3）。
- **回す打ち（長い端を前へ振り出す）は今回は入れない**: 握りを軸に回るには、はめる物を手に固定せず握りの軸だけで回れる関節が要る（物理の新しい仕組み）。§8 に「トンファーは手に固定（回らない）」と書く。
- **ボット**: 拳のボット（`BoxerBot`）のまま。教科書は新しいスタイル JSON（`Bots/tonfa.json`。空手かボクサーのスタイルを基に、前腕で受ける守りの重みを高く、間合いはトンファーの見える長さから）。「トンファーなら」の分岐を判断の式に書かない（違いは JSON の値。前腕で受ける守りは今の `DefenseBook` の受けを使う。`DefenseBook.cs` は触らない）。
- **キャラクター 1 人**（`Characters/roster/<id>.json`、架空の人物・架空の名前。国は `countries.json` から。`body.wear` はトンファー、教育は空 `{}`）。メニュー（アイコン・肖像）・`rosterleague` に出ること。
- **版**: はめる物の形が 1 つ増える。既存の試合が変わらないなら `PhysicsVersion` は据え置き（変わるなら 36 に上げて理由を書く）。スタイルを足すだけで判断の式を変えないなら `LogicVersion` も据え置き。式を変えたら 36 に上げて履歴に 1 行。既存の試合のチェックサム（`CharacterSamenessTests`・`StepCostRegressionTests`）が通ること。

## 確かめ（完了の条件）
- テスト: トンファーのボクセル（長さ・質量・握り・前腕に沿う向き）、両手にはまる、構えて立つ相手へ台本の指令で突いたら前の端・拳の前の木で当たる（当たったセルの位置で）、前腕で受けた相手の拳・剣が木に当たる（腕の肉より先に木が削れる）、教科書・キャラのファイルの読み込みと検証。
- 測定: トンファー使い 対 空手家（akira）・ボクサー（marcus）・刀（jin）・こんぼう（sean）・棒術家（daichi）を各 4 試合以上（`rosterleague`）。使っていない種（種 7 など）でも 1 回。`docs/arena/tonfa_league.md`。比べもの: 同じキャラで `body.wear` をグローブ・素手にした場合。勝てなくてよい。弱い所を報告に。
- 動画: トンファー使い 対 刀 40 秒（`video --red-char <id> --blue-char jin --seconds 40 --speed 1`）と、突き・前腕の受けが見える 4 倍スロー（`media_tmp/`、git に入れない）。トンファーが前腕に沿って見えるか・どこで当たるか・受けで木に当たるかを、自分でコマを見て秒を報告に。Claude が見て確かめる。
- 全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"` を最後に通しで回す（一部だけの再実行で済ませない）。README の件数。
- 文書: `docs/VOXEL_WEAPONS.md`（トンファー）・`docs/WEAPON_DEFENSE.md`（前腕の受け）・`docs/CHARACTERS.md`・`CLAUDE.md` §3・§8（新しい近道）。新しい `docs/TONFA.md` を作ってもよい。

## 触らない
`PunchBook.cs`・`DefenseBook.cs`・`Technique.cs`、`Tv*.cs`（`TvShowGraphics.cs` の武器の名前の表に 1 行足すのだけはよい）、`VideoMaker.cs`（武器の名前の表に 1 行足すのだけはよい。master に他の作業の未コミットの変更がある）、`HeadlessRenderer.cs`、`docs/TV_BROADCAST.md`、`docs/plans/CODEX_BRIEF_*`、既存のキャラの体・顔の値、既存のスタイル・武器の教科書の値、体の物理（`Fighter*.cs` の体の部分・`Voxels/` の体・`Footwork.cs`）。

## 報告
`docs/plans/SONNET_REPORT_TONFA_2026-11-16.md`（先頭に「完了」か「未完了」）: トンファーの形、打ち方・受け方、教科書（JSON の要点）、表、試合の結果と弱点、動画の場所と秒、テスト件数、§8 に足した近道、確かめていない所。最終メッセージは 1 回だけ。背景の処理を残さない。
