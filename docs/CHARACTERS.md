# キャラクター（体格・顔・目）

キャラ 1 人は「スタイル JSON（ボットの性格）＋ 体格（`Build`）＋ 顔（`Face`）」で決まる。体格と顔は **見える寸法** で、強さはそこから出る（CLAUDE.md §1.7 見た目が脅威を伝える）。

```jsonc
{ "Name": "titan", "RingName": "タイタン",
  "Build": { "Height": 1.18, "Arms": 1.15, "Legs": 1.1, "Muscle": 1.35 },
  "Face":  { "EyeSpacing": 0.9, "Nose": 1.25, "Jaw": 1.28, "EyeTilt": 0.25, ... },
  ... ボットの性格（Aim・ComboWeights など）... }
```

例は `docs/characters/titan.json`（大きい）・`shrimp.json`（小さい）・`eyehunter.json`（目を狙う）。`--red docs/characters/titan.json` のようにそのまま `video`・`league` に渡せる。`Build`・`Face` を省くと、基準の体格と、リングネームから決まる顔になる。

## 1. 体格（`BodyBuild`、基準 = 1.0）

| パラメータ | 意味 | 効くところ |
|---|---|---|
| `Height` | 身長（全部位の長さ・太さ） | 体の寸法 → 質量・間合い |
| `Arms` | 腕の長さ（身長に掛ける） | 拳の届く距離（`Fighter.ArmReach`） → `FighterSense.OpponentArmReach` → `RangeSense` / `EngageRange` |
| `Legs` | 脚の長さ | 歩幅・脚の関節トルクのてこ |
| `Muscle` | 筋肉の太さ（手足の断面の辺、胴の幅と奥行き） | 断面積 → 筋力 |

指定できる範囲は 0.6〜1.5（`BodyBuild.Validate`）。

**ボクセルの数は変えない**。部位の箱の寸法（半径）だけを変え、同じ数のセルを引き伸ばす（`BoxerBody` が基準体格の中身を作って箱へ写す）。セルの大きさが変わるので、質量は部位の体積に比例する（セルあたりの質量は同じ材質の配置のまま）。

### 力の出どころ（一つの式）

| 量 | 式 | 備考 |
|---|---|---|
| 腕の力 | 状態 × 断面積比 `Height² × Muscle²` | 拳を目標へ引く PD 力（`Fighter.ArmStrength`）。筋力は断面積に比例（生理の近似） |
| 脚の力 | 状態 × 断面積比 × 脚の長さ比 `Height × Legs` | 関節のトルク = 筋力 × てこの腕（`BodyBuild.LegTorqueRatio`）。ホバー・歩く力・蹴りの足首の力にも同じ値を使う |
| 立てるか | 脚の**状態**（体格に依らない） | 痩せた脚が健康なまま立てなくならない（`Fighter.LegCondition`） |
| 質量 | 部位の体積に比例 | §8 の近道（材質の密度から直接は出していない） |

基準体格（`Muscle = Height = Arms = Legs = 1`）では比がちょうど 1 で、今までの物理と同じ（テストが比 = 1 と質量比 = 体積比を確かめる）。

### 筋肉が傷むと力が落ちる（残った断面）

`BoxerBody.Muscle(部位)`:

- 腕・脚（梁の軸のある部位）: 軸に沿った薄切りごとに、残った肉（皮膚と筋肉のセル）の数 / 元の数 を出し、**いちばん細い半分の薄切り（肉のセルが最大の 4 分の 1 未満の端は除く）の平均**を取る。筋肉は直列に力を伝えるので、どこか 1 か所が細くなればそこで力が決まる。一様に削れた部位では体積の割合と同じ。
- 頭・胴など軸のない部位: 体積の割合。

以前は全部「残った体積の割合」だった。1 か所だけ深くえぐれても（体積はほとんど減らない）力が落ちなかった。テスト `MuscleStrength_FollowsTheWeakestCrossSection_NotTheVolume`。

力 = `MinMuscleStrength + (1 - MinMuscleStrength) × 残った断面`（肉が無くなっても腱・骨で動かせる分が残る）。

## 2. 顔（`FaceParts`・`FaceSculptor`）

頭のボクセル（約 10 × 14 × 10 セル。1 セル 5 mm）に、部位ごとのテンプレートを巻き付ける（`Voxels/FaceTemplates.cs`）。すべて基準の顔に対する相対値で、`FaceParts.Default` が基準:

| 部位 | パラメータ | 作り方 |
|---|---|---|
| 目 | `EyeSpacing` `EyeSize` `EyeHeight` `EyeTilt` | 白目・黒目のセルを、基準の目の位置へ逆写し（傾き・寸法・間隔・高さ、表面からの深さを保つ）して作り直す。黒目は表の白目のうち目の中心にいちばん近い 1 セル。**白目の体積が視力の材料**なので、潰せる体積を持ったまま動く |
| 眉 | `BrowHeight` `BrowTilt` `BrowThickness` | 目の上の細い帯を傾け・太さを変えて、表面の皮膚を髪の色にする（`FaceLook.Brows` のとき） |
| 鼻 | `Nose` `NoseWidth` `NoseLength` | 鼻筋と鼻先の起伏（前へ盛る） |
| 口 | `MouthWidth` `MouthHeight` `Lips` | 唇の帯（材質 `lip`: 皮膚と同じ強さ・密度、色だけ肌から赤みへ） |
| 耳 | `EarSize` `EarHeight` | 頭の横の起伏（外へ盛る） |
| あご・頬骨 | `Jaw` `JawWidth` `Cheekbones` | あごの突き出し・下顎の幅・頬の盛り上がり |
| 髪 | `Hairline`（髪型は `FaceLook.Style`） | 生え際の高さ（`Full`・`Crop`）。髪型・色・ひげは従来どおりリングネームから |

起伏は「柱ごとに表面を前後（左右）へずらす」ワープで作る: 皮膚は 1 層のまま、その下の肉・骨が引き伸ばされる（空洞を作らない）。頭の中身（脳）は動かさない（テストが脳のセル数が変わらないことを確かめる）。

**解像度の限界**: 頭は 10 × 14 × 10 セルしかなく、目は 2〜3 セル、鼻は 1〜2 セルの盛り上がりにしかならない。パラメータの連続な差は、セルの丸めで階段状に現れる。見分けの主役は肌・髪の色と髪型・ひげ・目の位置と寸法・眉の傾き・口の太さ。頭だけ 2 倍の細かさ（2.5 mm）にする案は、頭の削れ（脳のダメージ・視力）を測り直すことになるので見送った（§8）。

`docs/images/gen13_faces.png`: 全キャラの顔（左 = 正面、右 = 斜め 40 度）。`miniring-arena faces [--names a,b,f.json] [--columns N]` で作る。

## 3. 目が潰れる（`VisionState`・`VisionPerception`）

目の白目・黒目のセル（材質 `eye`・`pupil`）の残り HP の割合が、左右の目それぞれの「見える度合い」（`BoxerBody.Vision`）。連続で、場合分けはない。

- **ボットへ渡す情報**（`BoxingGame.ArmedSense`）: 両目が無事なら今の状態そのまま。損傷があれば、`VisionPerception` の低域フィルタ（毎ステップ更新）を通した写しを渡す。見えない側の視野にある相手の拳・肩・腰・頭は、更新が遅れ（`VisionLagSeconds` × 失った割合²）、位置がぶれる（`VisionNoiseReach`）。ぶれは試合の種付きの乱数（§1.6）で、参照回数に依存しない。拳の速さも見える度合いで縮み、`TargetMap`（狙いの点）は実体への参照を切って写しにする（傷んだ目から現在位置を逆引きさせない）。
- **人（キーボード・意図操作）**: 意図の解釈も同じ `ArmedSense` を受け取るので、同じ遅れ・ぶれが入る。画面は、潰れた目の側の端が暗くなる（`BlindHud`、失った割合に連続）。
- **VR**: ホストの描画は別（計画: HMD の左右の目それぞれの画面の端を、その目の失った割合で暗くする。`PlayerSense.SelfVision` を読めばよい）。未実装。
- **ボット側の狙い**: `AimWeights.Eye`（目）・`BlindSide`（見えない側）。重みは相手の見えが悪いほど増える（`Targeting.Weight`: 目は `2 − 両目の平均`、見えない側は `1 − 悪い方の目`）。空手家の貫手・一本拳（`KarateHandChoice`）と結びつく。
- 測定は `miniring-arena visionlab` と `docs/BOT_PDCA.md` の gen13。

## 4. 確かめ

| テスト | 内容 |
|---|---|
| `CharacterTests` | 体格でセル数は変わらず質量比 = 体積比、腕の力・脚の力・断面の最も細い所、顔の各部位が自分のセルを動かす、脳は動かない、両目と黒目が残る、全キャラの顔が見分けられる、スタイル JSON → コーナー |
| `VisionTests` | 目は左右独立に連続で減り、再生すると戻る、見えない側の拳の証拠が遅れる、`BlindHud`、片目が潰れると守りが間に合わない率が上がる |

## 5. 名簿（13 人）とメニュー（2026-11-16）

試合に出られるキャラクターは、組み込みの名簿 `games/MiniRing/Characters/roster/<id>.json`（**1 人 1 ファイル**。読み込みと検証は `CharacterRoster`、型は `FighterCharacter`）で決める。国の旗の表だけは共通の `games/MiniRing/Characters/countries.json`。国籍・武器・肌の色・身長と体格・名前を **1 か所に合わせる**。スタイル JSON（`champion.json` など）には触らない（ボットの性格は `textbook` でスタイルの名前を指すだけ。教科書からの差分は `education`）。

### ファイルの形（E0、2026-11-16）
ファイルは **体 ＋ 教科書 ＋ 教育 ＋ 育ちの記録**（CLAUDE.md §1.8、計画は `docs/plans/CHARACTER_EDUCATION_PLAN.md`）。

```jsonc
{
  "id": "marcus",                         // ファイル名と同じ（marcus.json）。小文字・数字・-・_
  "order": 8,                             // 選択画面の並び（小さい順、同じなら id）
  "name": "マーカス・\"ザ・ハンマー\"・ブルックス", "reading": "...", "hudName": "ザ・ハンマー",
  "origin": "アメリカ", "country": "usa", "tagline": "ヘビー級の王者",
  "look": { "skin": [0.3, 0.19, 0.12], "hair": [...], "hairstyle": "Bald", "eyes": [...], "brows": true, "beard": false,
            "parts": { "jaw": 1.12, "jawWidth": 1.1, "cheekbones": 1.1 } },
  "body": { "build": { "height": 1.12, "arms": 1.1, "legs": 1.0, "muscle": 1.15 }, "wear": "glove", "weapon": null },
  "textbook": "champion",                 // BotCatalog のスタイルの名前（全キャラ共通の基本）
  "education": {                          // 教科書からの差分だけ。ここでは全員空
    "style": { "Aggression": 0.8, "Aim": { "Chin": 2 } },   // 書いた鍵だけ教科書を上書き（BotStyle.ToJson と同じ書き方。列挙体は数）
    "vs": { "akira": { "RangePress": 3 } },                 // その相手 id との試合だけ（style の上にさらに重ねる）
    "weaponBot": { "Rest": 0.4, "Defense": { "BlockShare": 0.5 } }   // 握る武器（刀・ロングソード・こんぼう）のボットの値。書いた鍵だけ武器の教科書を上書き（下）。武器が無いキャラは null か {}
  },
  "record": []                            // 育ちの記録（教育を変えた履歴。E2 以降が書き足す。読み込みは中身を解釈しない）
}
```

- **解決**（`FighterCharacter.Resolve(相手の id)` / `EducationResolver`）: 教科書の JSON（`BotStyle.ToJson`）→ `education.style` → `education.vs[相手]` の順に JSON の段で重ね（オブジェクトは鍵ごとに再帰で重ね、配列・値は置き換え）、`BotStyle` に読み直して `Validated()`。**教育が空（重なる層が無い）なら教科書そのもの**（同じ物を返す。今と同じ試合）。結果の `Name` は教科書の名前のまま（空手の判定 `IsKarate` など、名前で見ている所が壊れないように）。
- 教育で変えられないもの: `Name`・`RingName`・`Build`・`Face`（体と名前。強さは見えている体から出す、§1.7）。
- **解決したスタイルを全部に通す**（名前で `BotCatalog` を引き直さない）: 青の CPU（`BotCatalog.Corners(解決済み)` → `CpuStyleJson`）、観戦の赤（`RedBotStyle`）、人が操る赤・2P の青の意図の解釈（`BoxingCorners.PlayerStyle`・`CpuStyle` → `DesktopFighterControl`）、レフェリーなしの倒れた相手への攻め（`GroundStyle`）、Arena の `--red-char` / `rosterleague`（`--set` はその上にさらに重ねる）。
- **記録**: 見出しに解決済みのスタイルの全体が入る（青 = `CpuStyleJson`、ボットの赤 = `RedBotStyleJson`、人が操る赤 = `PlayerStyleJson`）。再生は名簿のファイルを引かないので、後でファイルの教育が変わっても同じ試合。リプレイ形式は版 9（バイナリの並びは版 7 と同じ。古い記録は `PlayerStyleJson` が無く、今までの決め方）。握る武器のボットの値（解決済みの `WeaponStyle` の JSON）は見出しの `PlayerWeaponStyleJson`・`CpuWeaponStyleJson`（版 9。無い記録は null = その武器の組み込みの教科書）。
- **武器のボットの教科書と教育**（`education.weaponBot`、2026-11-16）: 刀・ロングソード・こんぼうのボットの判断の値は `WeaponStyle`（`Bots/WeaponStyle.cs`）に全部まとまっていて、JSON に読み書きできる（`Validated()` が範囲を検査し、知らない鍵・範囲外は止める）。武器ごとの教科書は組み込みの `Bots/weapons/katana.json`・`longsword.json`・`club.json`（`WeaponCatalog.Textbook`）。値: 剣士の守り `Defense`（`SwordDefense`）・間合いの取り合い `HitAndAway`（null = 遠間で構えて 1 つずつ詰める）・狙い `Aim`/`AutoAim`・自分の体を避ける通り道 `Clearance`・技の時計 `Pacing`・`Rest`・`MinReach`・折れた武器を捨てる長さ `KeepReach`、こんぼうの `ClubRange`・`ClubRest`・`ClubStandOff`・`ClubExact`。使わない側の値は無視される（剣士はこんぼうの値を読まない）。`education.weaponBot` は書いた鍵だけが教科書を上書きする（`WeaponEducation.Resolve`、重ね方は `style` と同じ。最上位の鍵が教科書に無い・`Name` を書いた・値が範囲外・武器を握らない（拳・鉄の剣）キャラが空でない値を書いた、は読み込みで止める）。解決した値は `FighterCharacter.WeaponStyle` → `BoxingCorners.PlayerWeaponStyle`/`CpuWeaponStyle` → 武器のボット（`FormSwordsman.Bot(.., style)`・`ClubBot.FromStyle`）と記録の見出しへ。教育が空なら教科書そのもの（今までの試合とビット単位で同じ: `CharacterSamenessTests` の `w-*` の組）。**相手ごとの層（`vs`）は無い**（武器の値は相手を読んで変えるものではなく、教育全体で 1 組）。
- プレイヤーが作ったキャラ・新人: `CharacterRoster.LoadFile(path)` / `LoadDirectory(dir)`（アプリのデータの場所から読む入口。`vs` の相手は組み込みの id を認め、組み込みと同じ id は断る）。E5・E6 が使う。

### 方針
- **実在の人物はモデルにしない**。顔・名前の権利の問題と、体が削れ・切断され・死ぬゲームで実在の人を描く問題のため。名簿は全員が架空で、実在の特定の人を連想させる名前・顔にしない（顔は肌・髪・瞳の色と、基準の顔の小さな個人差だけ）。
- **国らしさは、その国の格闘の伝統（武器・戦い方）と、名前・装い・体格で出す**。人種の身体的な特徴を戯画化（誇張した顔の造作など）はしない。肌の色・髪・瞳は、その地域に普通にある色を選ぶ。顔の部位（`FaceParts`）は基準の ±15 % 以内の個人差だけ（あご・眉の太さ・頬骨・口の幅）。
- **強さは見えている体から出る**（CLAUDE.md §1.7）。大きい体格のキャラは実際に強い。能力値の数字は名簿に持たない。ボットの性格（`style`）は入力元の違いで、体の強さではない。
- **武器はキャラクターごとに固定**（メニューに武器を選ぶ行は無い）。握る武器（`weapon`: katana / longsword / ironSword / club）か、手にはめる物（`wear`: glove / none / knuckles / katar / gauntlet）。

### 名簿
| id | 名前 | 国 | 手・武器 | スタイル | 身長 / 腕 / 脚 / 筋肉 |
|---|---|---|---|---|---|
| jin | 鬼塚 迅 | 日本 | 日本刀 | champion | 1.0 / 1.0 / 1.0 / 0.95 |
| akira | 比嘉 明 | 日本（沖縄） | 素手（空手） | karate | 0.95 / 1.0 / 1.0 / 1.05 |
| konrad | コンラート・アイゼンハルト | ドイツ | ロングソード | boxer | 1.15 / 1.05 / 1.05 / 1.1 |
| sean | ショーン・オマリー | アイルランド | こんぼう | slugger | 1.05 / 1.0 / 1.0 / 1.15 |
| vikram | ヴィクラム・ラトール | インド | カタール | boxer | 1.0 / 1.0 / 1.0 / 1.0 |
| ramon | ラモン・"エル・トロ"・カスティージョ | メキシコ | グローブ | swarmer | 0.92 / 0.95 / 1.0 / 1.1 |
| yoandry | ヨアンドリ・モラレス | キューバ | グローブ | counter | 1.02 / 1.06 / 1.0 / 1.0 |
| marcus | マーカス・"ザ・ハンマー"・ブルックス | アメリカ | グローブ | champion | 1.12 / 1.1 / 1.0 / 1.15 |
| albert | アルバート・クロムウェル | イギリス | 素手（拳闘） | jabber | 1.0 / 1.04 / 1.0 / 1.0 |
| dmitri | ドミトリー・ヴォルコフ | ロシア | グローブ | slugger | 1.15 / 1.0 / 1.0 / 1.2 |
| joao | ジョアン・"ガロ"・シルバ | ブラジル | メリケンサック | swarmer | 0.98 / 1.0 / 1.0 / 1.05 |
| gaston | ガストン・ド・ヴァロワ | フランス | 籠手 | outboxer | 1.05 / 1.05 / 1.0 / 1.0 |

肌・髪・瞳・髪型・眉・ひげ・一言は各人のファイル（`roster/<id>.json`）。国旗は色の帯に丸と四角を重ねた簡略の図（`countries` 表の `flag`）で、メニューが図形で描く。

### 検証（壊れていれば止まる）
体格は 0.6〜1.5、色は 0〜1 の有限値、顔の部位は `FaceParts.Validate` の幅、教科書（`textbook`）は `BotCatalog.Styles` にあること、国・武器・はめる物の名前、id は小文字・数字・`-`・`_` でファイル名と同じ・重複なし、`order` があること、知らない項目はエラー。教育は読み込みのときに全部試しに解決する: 最上位の鍵が教科書の JSON に無い（綴りの誤り）・入れ子の知らない鍵・`Name`/`RingName`/`Build`/`Face` を書いた・知らないコンビネーション名・`vs` の相手が名簿に無い・`weaponBot` の鍵の綴り・値の範囲・武器を握らないのに書いた、はどれも止める（入れ子の鍵は読み直しの厳密な検査で、辞書（`ComboWeights` など）の新しい鍵は各自の検証で見る）。例外のメッセージにどの人（ファイル）のどの欄かを書く（`InvalidDataException`）。

### 試合・記録への渡し方
- `MatchSelection`（デスクトップ）は赤・青の 2 人のキャラクターとレフェリーの有無を持ち、`BoxingCorners` の体格・武器・はめる物・両手持ち・顔（`PlayerLook` / `CpuLook`）・画面の名前（`PlayerLabel` / `CpuLabel`）・`Referee` を埋める。空手の判定（`LocalVersusControl.IsKarate`）はスタイルの名前（教科書の名前。教育では変わらない）で見る（表示名ではない）。ボットが解決したスタイルを全部に通す道は上の「解決したスタイルを全部に通す」。
- 顔の `FaceLook`・画面の名前は記録の見出し（`ReplayMatchSettings` の末尾の欄）にも入るので、再生しても同じ顔・同じ名前。古い記録は欄が無く、今までどおりリングネームから決めた顔。
- Arena の動画: `video --red-char jin --blue-char marcus`（武器・はめる物・体格・顔・表示名が名簿の値）。

### 肖像（メニューの顔・全身の絵）
`CharacterPortraits` は、試合の体と **同じ関数**（`BoxerBody.BuildPartGrid`: 基準体格の格子 → 体格へ伸縮 → 顔を巻き付け）で部位の格子を作り、立った姿（`Fighter.Spawn`）に置いて、腕だけ肩を軸に脇へ下ろし（回すだけ）、正面から奥へ光線を通して **いちばん手前のセルの材質** を四角（ドット絵）に写す。別の絵を描き起こさない。明るさは手前のセルの奥行きの段差から出した左上からの光だけ（顔の起伏が見える）。顔は 1 ピクセル = ボクセル 1 つ（5 mm）、全身も同じ細かさで、背の高さ・太さは体格の寸法のまま（同じ尺度で並べる）。作るのは重いので `PortraitSet` が背景スレッドで 1 人ずつ作り、できた人から出す。

### メニュー（`games/MiniRing.Desktop`）
- 状態の遷移は `MenuState`（描画・ファイル・音を持たない純粋な部分。入力は `MenuInput`、結果は `MenuOutcome`）、描画は `MenuView`・`MenuIcons`（`UiShapeDraw` の組み合わせだけ。外部の画像・フォントなし）、`MatchMenu` は `InputSnapshot` → `MenuInput` への変換と、結果の実行（設定の保存・記録の一覧・試合の開始・合図の音）だけ。
- ページ: 最初の画面（シングル・対戦・観戦・リプレイ・設定・終了）→ キャラ選択（12 人の顔の格子、赤・青のカーソル、左右に全身の絵・名前・国旗・武器・体格・一言）/ 設定（レフェリー あり／なし）/ リプレイ。
- キャラ選択: シングル・観戦は赤 → 青の順。対戦は 2 つ目のパッドで青を同時に動かせる（キーボードは順）。決定で選びを固め、戻るで 1 段戻る（青を解く → 赤を解く → 最初の画面）。最初の画面で戻っても終了しない（「終了」のボタンへ移るだけ）。
- 設定は `settings.json`（作業フォルダー、記録の `replays/` と同じ所）に残し、読むときに検証する。壊れていれば既定（レフェリーあり）へ戻して標準エラーに出す。

| テスト | 内容 |
|---|---|
| `CharacterRosterTests` | 名簿が決めた表どおり、壊れたデータを項目名つきで拒む、武器が試合へ渡る、顔・体格・画面の名前が試合に出る、記録の往復で同じ顔（古い記録は名前から）、肖像が本物のボクセルから作られ体格に従う |
| `MenuStateTests` | 選ぶ・戻る・一番上で戻っても終わらない・対戦の 2 つ目のパッド・ポインタ・設定の切り替えと保存と壊れたファイル・リプレイの一覧 |
| `MenuPagesVisualTests`（visual） | 各ページ（最初・設定・キャラ選択（シングル・対戦）・リプレイ）と肖像の拡大を `.visualcheck/menu_*.png` へ |

## 6. 体格が戦い方に効く所（2026-11-16、`Bots/BuildRead.cs`）

ボットは相手と自分の見えている寸法（腕の長さ・身長・重さの見積もり `BodyBuild.MassRatio` = H³M²）の比から、立ち位置・打つ間合い・守りの選び方・胴を狙う重みを連続に変える（名簿のキャラ名や体格の種類で分けない）。同じスタイル（例: champion を使うジンとマーカス）でも体が違えば戦い方が変わる。腕の長い方は相手の届かない所の端へ、短い方は相手の腕の内側へ入り、頭の低い方は潜る守りを選びやすい。体格が同じ同士では今までと同じ。係数は `BotStyle`（`BuildStandOff` など、`docs/BOTS.md`）で、キャラごとの差は教育（スタイルの上書き）で入れる。強さそのものは体（寸法）から出るので、能力値は持たない（§1.7）。測定は `rosterleague`（`docs/arena/build_tactics_*.md`）。

## 7. テコンドー家ディエゴ（2026-11-16）

13 人目 `diego`（教科書 `taekwondo`、素手、身長 1.1・腕 1.0・脚 1.1・筋肉 0.92、メキシコ）。蹴りの経路と測定は [TAEKWONDO.md](TAEKWONDO.md)。
