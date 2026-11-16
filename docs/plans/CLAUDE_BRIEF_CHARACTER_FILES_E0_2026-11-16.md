# 作業指示（2026-11-16）: E0 キャラクターを 1 人 1 ファイルに

作業場所: ワークツリー `E:\repos\ClaudeProjects\myvrgame1\.claude\worktrees\charfiles`（ブランチ `claude/charfiles`）だけ。master の作業ツリー・他のワークツリーには触らない。コミットはワークツリーの中でだけ（区切りに使ってよい）。プッシュしない。日本語。

最初に読む: `CLAUDE.md`（§1 全部、特に **§1.8**、§4、§6）、`docs/plans/CHARACTER_EDUCATION_PLAN.md`（全体の計画とファイルの形の案）、`docs/CHARACTERS.md` §5、`games/MiniRing/Characters/`（`FighterCharacter`・`CharacterRoster`・`characters.json`）、`games/MiniRing/Bots/BotStyle.cs`（`ToJson`/`FromJson`/`Validated`）・`BotCatalog.cs`、`BoxingGame.cs` の `BoxingCorners` と制御の作り方（`RedBotStyle`・`CpuStyleJson`・`GroundStyle`・`WeaponBot`）、`Replay/ReplayFile.cs`・`ReplayMatchSettings.cs`、`games/MiniRing.Desktop/MatchSelection.cs`・`Program.cs`・`DesktopFighterControl.cs`・`IntentKeyboardControl.cs`、`games/MiniRing.Arena/Program.cs`（`--red-char`・`rosterleague`・`--set`）・`RosterLeague.cs`・`VideoMaker.cs`。

## 目的
ファイター = 体 ＋ 教科書（スタイル）＋ 教育（差分）＋ 育ちの記録、を 1 人 1 ファイルで持ち、それを読み込んで試合のファイターを作る。この段では**教育はすべて空**で、試合は今とビット単位で同じ。次の段（E2: 教育の道具）が書き込める形と、解決したスタイルが試合・記録の全部に通る道を作るのが仕事。

## 決めたこと
1. **ファイル**: `games/MiniRing/Characters/roster/<id>.json`（組み込みのリソース）。今の `characters.json` の 12 人をそのまま移し、`characters.json` は消す（国の旗の色など、キャラ以外の共通の表があれば `countries.json` などに分ける）。形は計画書の案（`id`・名前・`look`・`body`{build, wear, weapon}・`textbook`・`education`{style, vs}・`record`[]）。名簿の順（選択画面の並び）はファイルの中の値（例 `order`）で決める。
2. **読み込みと検証**（`CharacterRoster`・`CharacterFile` など）: id の重複・ファイル名と id の不一致・教科書の名前が `BotCatalog` に無い・体格や色の範囲外・**教育の上書きの鍵が教科書の JSON に無い**（綴りの誤りを黙って無視しない）・`vs` の相手 id が名簿に無い → 分かるメッセージで止める。ファイルのパスから読む入口も作る（将来、プレイヤーが作ったキャラ・新人をアプリのデータの場所から読むため。E5/E6）。
3. **解決**: 教科書の JSON → `education.style` → `education.vs[相手 id]` の順に JSON の段で重ね（オブジェクトは鍵ごとに再帰で重ね、配列・値は置き換え）、`BotStyle.FromJson` → `Validated()`。結果の `Name` は教科書の名前のまま（空手の判定など、名前で見ている所が壊れないように。名前で見ている所が他にあれば報告）。`FighterCharacter.Resolve(string? opponentId)` のような 1 つの入口。
4. **解決したスタイルを全部に通す**（名前で `BotCatalog` を引き直している所を無くす）:
   - CPU（青）: `BotCatalog.Corners(Blue.Style)` を、解決したスタイルから作る（`CpuStyleJson` に解決後の全体）。
   - 観戦の赤ボット（`RedBotStyle`）、レフェリーなしの倒れた相手への攻め（`GroundStyle` が名前から引いている所）、武器のボット。
   - プレイヤーの意図操作（`DesktopFighterControl`・`IntentKeyboardControl` の `style`、`LocalVersusControl.StyleNamed`、デスクトップの `Program.cs` の `BotCatalog.Styles.FirstOrDefault(...)`）: 解決したスタイルを渡す。足りなければ `BoxingCorners` の末尾に `PlayerStyle`（`BotStyle?`）を足す。
   - Arena の `--red-char`・`rosterleague`（`--set` はその上にさらに重ねる）。
5. **記録**: 記録の見出しに、両コーナーの解決後のスタイル（プレイヤーの意図の解釈に使うものも）を入れ、再生ではそれを使う（名簿のファイルが後で変わっても同じ試合）。引数は末尾に足す。古い記録は今までどおり読める（無い欄は今の決め方）。
6. **剣士・こんぼうのボット**（`SwordsmanBot` の `SwordDefense`・`HitAndAway` など）: JSON に読み書きできるなら `education.weaponBot` として同じ道に乗せる。今すぐ乗らないなら欄だけ予約し、空でない値は「まだ使えない」と分かるメッセージで止める。どちらにしたか報告。
7. 版: 教育が空なら振る舞いは変わらないので `LogicVersion`・`PhysicsVersion` は上げない。記録の形を変えたら `ReplayFile` の版を上げる（古い版も読む）。

## 確かめ（完了の条件）
- **同じ試合になること**: 変える前に、名簿のキャラ同士（拳同士・空手・剣・こんぼう・カタールを含む 4〜5 組、レフェリーあり/なし）のチェックサムを Probe で取って記録し、変えた後に一致するテスト。
- 単体テスト: 読み込みの検証（上の各失敗）、重ね方（再帰・置き換え・`vs` の優先）、教育を入れたキャラの解決結果、教育を入れたキャラの試合の記録 → 再生でチェックサム一致（ファイルを後で変えても記録から同じ試合）、プレイヤー側の解決スタイルが意図操作に届く。
- 全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`（visual を含めて）、README の件数。メニューの visual テスト（キャラ選択の絵）が変わらないこと。
- 文書: `docs/CHARACTERS.md` §5（ファイルの形・置き場・検証・解決の順）、`CLAUDE.md` §3 の構成の表（`Characters/roster/`）、`docs/plans/CHARACTER_EDUCATION_PLAN.md` の E0 の状態。

## 触らない
`games/MiniRing.Arena/TvShowGraphics.cs`・`TvScout.cs`（別のセッション）、`PunchBook.cs`・`DefenseBook.cs`・`Technique.cs`（技の進化の作業）、`docs/plans/CODEX_BRIEF_*`。キャラの値（体格・顔・名前）は変えない。

## 報告
`docs/plans/SONNET_REPORT_CHARACTER_FILES_E0_2026-11-16.md`（ワークツリーの中。先頭に「完了」か「未完了」）: ファイルの形の実例 1 つ、解決の道（どこで名前引きを置き換えたか）、記録の変更、同じ試合の確かめ方と結果、テスト件数、確かめていない所。最終メッセージは 1 回だけ。背景の処理を残さない。
