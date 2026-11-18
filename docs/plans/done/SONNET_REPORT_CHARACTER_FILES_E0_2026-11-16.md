完了

# 報告: E0 キャラクターを 1 人 1 ファイルに（2026-11-16、ブランチ `claude/charfiles`、未コミット）

指示書: `docs/plans/done/CLAUDE_BRIEF_CHARACTER_FILES_E0_2026-11-16.md`。教育はすべて空で、試合は今とビット単位で同じ。

## ファイルの形（実例）
`games/MiniRing/Characters/roster/<id>.json`（組み込みのリソース。`characters.json` は消し、国旗の表は `Characters/countries.json` へ）。`docs/CHARACTERS.md` §5 に全体の説明。

```json
{
  "id": "akira", "order": 2,
  "name": "比嘉 明", "reading": "ヒガ アキラ", "hudName": "アキラ", "origin": "日本（沖縄）", "country": "japan", "tagline": "首里の空手家",
  "look": { "skin": [0.82, 0.62, 0.46], "hair": [0.05, 0.05, 0.05], "hairstyle": "Crop", "eyes": [0.12, 0.07, 0.03],
            "brows": true, "beard": false, "parts": { "browThickness": 1.2 } },
  "body": { "build": { "height": 0.95, "arms": 1, "legs": 1, "muscle": 1.05 }, "wear": "none", "weapon": null },
  "textbook": "karate",
  "education": { "style": {}, "vs": {}, "weaponBot": null },
  "record": []
}
```
- 12 人の値は旧 `characters.json`（git の HEAD）と全項目を機械で突き合わせて差 0（名前・読み・国・武器・はめる物・体格・顔・並び・国旗の表）。メニューの絵はデータが同じなので変わらない。
- 名簿の並びは `order`（小さい順、同じなら id）。`education.style` / `education.vs[相手 id]` は `BotStyle.ToJson` と同じ書き方（書いた鍵だけ教科書を上書き）。`record` は教育を変えた履歴（E2 以降が書く。読み込みは中身を解釈しない）。

## 読み込みと検証（`CharacterRoster`、新規 `CharacterEducation.cs`）
- `FromFiles(files, knownIds)` が核。id（文字種・ファイル名と一致・重複）、`order` の有無、教科書が `BotCatalog.Styles` にあること、体格・色・顔・武器・はめる物・国、知らない項目、`record` の null、**教育の全層を読み込み時に試しに解決**（最上位の鍵が教科書の JSON に無い＝綴りの誤り／`Name`・`RingName`・`Build`・`Face` を書いた／入れ子の知らない鍵（読み直しを厳密にして検査）／知らないコンビネーション名／`vs` の相手が名簿に無い／`weaponBot` が空でない）を、どの人（ファイル）のどの鍵かを書いた `InvalidDataException` で止める。
- ファイルのパスから読む入口: `CharacterRoster.LoadFile(path)` / `LoadDirectory(dir)`（E5・E6 用。`vs` の相手は組み込みの id を認め、組み込みと同じ id は断る）。
- 制限（正直に）: 辞書型の鍵（`ComboWeights` の新しい鍵など）は「教科書に無い鍵」では止めない（新しい鍵が正当なため）。各自の検証（`Combo.Named` など）で見る。教育の値の範囲外は `Validated()` の丸めに任せる（黙って丸まる。範囲外を止めるかは E2 で決める）。

## 解決（`FighterCharacter.Resolve(相手の id)` / `EducationResolver`）
教科書（`BotStyle.ToJson`）→ `education.style` → `education.vs[相手]` を JSON の段で重ね（オブジェクトは鍵ごとに再帰、配列・値は置き換え）、`BotStyle` に読み直して `Validated()`。**重なる層が無ければ教科書そのもの（同じ物）を返す**ので、教育が空のときは今までの経路と同じ。`Name` は教科書の名前のまま。`FighterCharacter.Style` は `Resolve(null)`。

## 名前引きを解決済みのスタイルに置き換えた所
| 場所 | 前 | 後 |
|---|---|---|
| `MatchSelection.Corners`（デスクトップ） | `BotCatalog.Corners(Blue.Style)`、`RedBotStyle = Red.Style` | `Red.Resolve(Blue.Id)` / `Blue.Resolve(Red.Id)` を、青（`CpuStyleJson`）・赤のボット（`RedBotStyle`）・人が操る赤（新しい `PlayerStyle`）に通す |
| `LocalVersusControl`（赤・2P の青の意図操作） | `BotCatalog.Styles.FirstOrDefault(Name == 名前)` | 赤は `corners.PlayerStyle`、青は `corners.CpuStyle`（`CpuStyleJson` を読んだもの） |
| デスクトップ `Program.cs` の `IntentKeyboardControl` | 同上（名前引き） | `corners.PlayerStyle` |
| `BoxingGame.GroundStyle`（レフェリーなしの倒れた相手への攻め） | コーナー名で `BotCatalog.Styles` を引く → 渡されたスタイル → ボクサー | 解決済み（青は `CpuStyle`、赤は `RedBotStyle ?? PlayerStyle`）→ ボクサー。名前では引かない |
| Arena `video --red-char/--blue-char`・`tvscout`・`rosterleague` | `Contender.Named(StyleName)`／`BotCatalog.Style(StyleName)` | `RosterLeague.ContenderOf(キャラ, 相手 id, set)`（解決済みの上に `--set` を重ねる）。`VideoMaker.WithCharacters` が `PlayerStyle` も入れる |
| 武器のボット | 名前を引かない（`FormSwordsman.Bot`・`ClubBot.Basic` はスタイルを取らない） | 変更なし |

`BoxingCorners` の末尾に `BotStyle? PlayerStyle` と、プロパティ `CpuStyle` を足した。名前で見ている残りは意図して残してある（教育では変わらない教科書の名前）: 空手の判定 `IsKarate`（`LocalVersusControl`・`FighterCharacter`）、メニューの既定の青 `MatchMenu`（`StyleName == DefaultCpu`）、`TvShowGraphics`（触らない指定）、`BotNames.RingNameOf`（リングネーム・顔の見た目だけ）。

## 武器のボット（`education.weaponBot`）
今すぐは乗らない。`SwordDefense`・`HitAndAway`・`AimTuning` は JSON に読み書きできる型ではなく、`ClubBot.Basic` も引数が無い。欄だけ予約し、空 `{}` / `null` 以外は「まだ使えない」と読み込みで断る（テストあり）。

## 記録（リプレイ）
- `ReplayMatchSettings` の末尾に `PlayerStyleJson`（人が操る赤の意図の解釈に使う、解決済みのスタイル）。青は従来の `CpuStyleJson`、ボットの赤は従来の `RedBotStyleJson`（どちらも解決済みの全体が入る）。再生は名簿のファイルを引かない。
- `ReplayFile.Version` 7 → 8（バイナリの並びは版 7 と同じ。見出しの JSON に欄が増えるだけ。無い記録は null で今までの決め方）。古い版（1〜7）は今までどおり読める。
- `LogicVersion`・`PhysicsVersion` は上げていない（教育が空なら振る舞いが変わらないため）。

## 同じ試合の確かめ方と結果
- **変える前**に Probe（`CharacterSamenessProbe`）で、名簿のキャラ同士 8 組のチェックサム（途中と最後）を取った: 拳同士（レフェリーあり/なし）・空手 vs 群れ・刀 vs ロングソード・こんぼう vs 素手・カタール vs 籠手・1P（人が待機）の青が CPU の 2 組（空手 vs 刀を含む）。ゲームと同じ道（`MatchSelection.Corners`）で 10〜20 秒。
- **変えた後**、`CharacterSamenessTests` が同じ 8 組で 16 個すべて一致（期待値を埋め込み）。
- Arena も、変える前に `rosterleague --chars marcus,akira,jin,sean --bouts 1 --seconds 15 --parallel 1 --referee off --seed 3`（`--set Aggression=0.5` つきも）の出力を保存し、変えた後の出力と **差 0**（2 通りとも）。
- 倒れた相手への攻めの好み（`GroundDrive/Load/Cut/Stomp/Rest`・`Southpaw`）が、解決して JSON を通しても全 9 スタイル・全 12 人で教科書と同じ値であることをテスト（`GroundPreferences_OfTheResolvedStyle_EqualTheTextbooks`）。名前引きをやめた `GroundStyle` が、教育が空のとき同じ値を読む保証。
- 教育を入れた試合の記録→再生: 解決済みのスタイルが見出しに入り（青・ボットの赤・人が操る赤）、再生で全チェックサムが一致（観戦・1P の 2 通り）。教育を入れると試合が変わること（チェックサムが違う）も確認。
- 検証の失敗 8 種＋ファイル名・重複・order・record の失敗、重ね方（再帰・置き換え・`vs` の優先・名前保持）、`LoadFile`/`LoadDirectory`、意図操作へ届くこと（`DesktopFighterControl.Style`）、古い記録（欄なし）が読めること。

## テスト件数
`dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`（visual を含む）: 661 件すべて合格（失敗 0・スキップ 0）。README の件数を 643 → 661 に直した（元の README の件数が現状とずれていたぶんも含む）。新規は `CharacterFileTests`（17）・`CharacterSamenessTests`（2）。`CharacterRosterTests` は 1 ファイル 1 人の形に合わせて直した（内容は同じ）。

## 確かめていない所
- デスクトップの `Program.cs` の `OpenMatch` から `IntentKeyboardControl` へ渡す配線は、ゲームを起動しての確認はしていない（`corners.PlayerStyle` を渡す 1 行。同じ値が `LocalVersusControl` 経由で意図操作に届くことはテスト済み）。VR ホストは名簿を使っていない。
- 教育を入れたキャラでレフェリーなし・武器のボット（刀・こんぼう）の倒れた相手への攻めが実戦で効くことは、値の経路（`CpuStyle`/`RedBotStyle`/`PlayerStyle` → `GroundStyle`）までの確認で、実戦のダウンを起こして確かめてはいない。
- メニューの visual テストは全テストの一部として通った（`.visualcheck` の絵そのものは見ていない。データが同じなので変わらない理由づけ）。
- 通常の実行で 1 回だけ、フィルターに 2 クラスを指定した最初の実行でテストホストが落ちた（再実行では 19/19 通る）。低メモリ機の一過性とみており、再現していない。
- Arena の `video`（ffmpeg）と `tvscout` は実行していない（`rosterleague` で同じ `ContenderOf` の経路を確認）。

## 触ったファイル
`games/MiniRing/Characters/`（`CharacterRoster.cs` 書き直し、`CharacterEducation.cs` 新規、`FighterCharacter.cs`、`countries.json`・`roster/*.json` 新規、`characters.json` 削除）、`MiniRing.csproj`、`BoxingGame.cs`、`Replay/ReplayFile.cs`・`ReplayMatchSettings.cs`、`MiniRing.Desktop/`（`MatchSelection.cs`・`Program.cs`・`DesktopFighterControl.cs`）、`MiniRing.Arena/`（`Program.cs`・`RosterLeague.cs`・`VideoMaker.cs`）、テスト 4 ファイル（上記）＋ `CharacterSamenessCases.cs`・`CharacterSamenessProbe.cs`、`README.md`・`CLAUDE.md` §3・`docs/CHARACTERS.md` §5・`docs/TV_BROADCAST.md`・`docs/plans/CHARACTER_EDUCATION_PLAN.md`（E0 を済に）。禁止指定のファイル（`TvShowGraphics.cs`・`TvScout.cs`・`PunchBook.cs`・`DefenseBook.cs`・`Technique.cs`・`CODEX_BRIEF_*`）とキャラの値は触っていない。
