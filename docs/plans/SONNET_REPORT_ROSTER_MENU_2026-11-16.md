完了（任意の 5 まで入れた。全テスト 599 件が合格）

# 報告: キャラクターの名簿と、絵で見せるメニュー（2026-11-16、ワークツリー `roster`、ブランチ `claude/roster`）

指示書: `docs/plans/CLAUDE_BRIEF_ROSTER_MENU_2026-11-16.md`。コミット・プッシュはしていない。

## 入れたもの
1. **名簿はデータ**: `games/MiniRing/Characters/`（`FighterCharacter`・`CharacterRoster`（読み込みと検証）・組み込みの `characters.json`・`CharacterPortraits`・`PortraitSet`）。12 人は指示書の表どおり（値の変更なし）。国は `countries` 表（名前と国旗の帯・丸・四角）、`origin` は表示用（akira は「日本（沖縄）」で旗は日本）。顔の部位（`FaceParts`）は基準の ±15 % 以内の個人差だけ（あご・眉の太さ・頬骨・口の幅）。壊れていれば `InvalidDataException`（どの人のどの欄か）。
2. **試合へ渡す**: `MatchSelection(Mode, Red, Blue, Referee)`（キャラクター 2 人）→ `BoxingCorners` の体格・顔・はめる物・武器・両手持ち・`Referee`。`BoxingCorners` の末尾に `PlayerLook` / `CpuLook`（`FaceLook?`）・`PlayerLabel` / `CpuLabel`（画面の名前）を足し、`BoxingGame` が使う（名簿の顔 = 試合の顔、HUD・実況の `RedLabel` / `BlueLabel` がキャラクターの短い名前）。`ReplayMatchSettings` の末尾にも同じ 4 欄（古い記録は欄が無く今までどおり）。`FaceLook.Validate()` を足し、記録を読むときに検証する。空手の判定 `LocalVersusControl.IsKarate` はスタイルの名前で見る（`PlayerName` / `CpuName` にスタイル名を入れる）。
3. **メニュー**（`games/MiniRing.Desktop`）: `MenuState`（純粋な状態遷移）・`MenuView`・`MenuIcons`（図形だけのアイコン・国旗・武器）・`MenuSettings`（`settings.json`、読むとき検証・壊れたら既定へ戻して標準エラー）・`MatchMenu`（入力の変換と結果の実行だけの殻）。最初の画面は 2 × 3 の大きなボタン、設定はレフェリーあり／なし、キャラ選択は 12 人の顔の格子（赤・青のカーソル、左右に全身・名前・旗・武器・体格のバー・一言）、対戦は 2 つ目のパッドで青を同時に動かせる。最初の画面で戻っても終了しない決まりは保った。
4. **顔・全身の絵は本物のボクセルから**: `BoxerBody.BuildPartGrid`（試合の体の格子を作る手順を静的関数へ切り出しただけ。挙動は同じ）を肖像も通る。立った姿に置き、腕だけ肩を軸に脇へ下ろし、正面から光線を通して最初のセルの材質を写す。明るさは奥行きの段差から出した左上の光だけ。顔は 1 ピクセル = ボクセル 1 つ、全身も 5 mm。大きさは体格の寸法のまま（同じ尺度）。
5. **Arena**: `video --red-char <id> --blue-char <id>`（スタイル・武器・はめる物・体格・顔・表示名が名簿の値）。1 本 4 秒で動作確認し、フレームで名前（ジン・ザ・ハンマー）が出ることを見た。

## 見て直したこと
- 最初の肖像は 1 cm / ピクセルで全身の顔が潰れていた → 5 mm に。
- 設定ページの説明文が「戻る」の行に重なっていた、チップの文字が影で二重に見えた（影なしの引数を足した）。
- 体格のバーの列同士・肩書きが全身の頭に重なっていた → 列の幅を詰め、肩書きを枠の左隅へ。
- 再生アイコンの三角を段数を増やして滑らかに。

## 確かめ
- 単体テスト（今回足したもの）: `CharacterRosterTests`・`MenuStateTests`（`tests/MiniRing.Tests`）。内容は `docs/CHARACTERS.md` §5 の表。`MenuLoadingTests`・`SnapshotTests.GameSelect_RendersJapaneseMenu` は新しい殻に合わせて直した（`HitRow` は無くなり、`MenuLayout.HitCenters` を確かめる）。
- 全テスト: `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe&TestCategory!=visual"` が 599 件すべて合格（6 分 11 秒）。visual（GPU）の `MenuPagesVisualTests` 2 件と `GameSelect_RendersJapaneseMenu` は別に走らせて合格。
- 画面（`.visualcheck/`、git の外、ワークツリー内。絶対パスは最終報告）: `menu_01_top.png`・`menu_02_settings_referee_on.png`・`menu_03_settings_referee_off.png`・`menu_04_select_single_red.png`・`menu_05_select_single_blue.png`・`menu_06_select_versus.png`・`menu_07_replays.png`・肖像の拡大 `menu_portraits_large.png`（`MenuPagesVisualTests`）。全部自分で見た。
- 文書: `docs/CHARACTERS.md` §5（名簿・方針・肖像・メニュー）、`docs/GUIDE.html` のメニューの節、`docs/INTENT_CONTROL.md`（武器は固定・空手の判定）。README の件数。

## 未確認・注意
- 実際のデスクトップのウィンドウでの操作（キーボード・マウス・2 つ目のパッドの実機）は確かめていない。入力の変換（`MatchMenu.Translate`）は元のコードの写しで、遷移は `MenuState` のテストで確かめた。2 つ目のパッドは `InputSnapshot.GamepadCount > 1` で検出する。
- 肖像の背景生成（1 人ずつ約 0.3 秒、12 人で数秒）の間、顔は「…」の枠。起動直後にキャラ選択へ入ると一瞬見える。
- メニューの合図の音・Return 直後の B の扱いは元のまま（変えていない）。
- 記録の見出し（`ReplayMatchSettings`）の引数は末尾に足したので、referee 側が `Referee` を足すときは同じ行の末尾でぶつかる（取り込みで両方を残す）。`BoxingCorners` も同様（`Referee` の後に足した）。
- `BoxerBody` の構築を静的関数へ切り出した（体格が基準でないとき、基準体格のモデルを部位ごとに作り直していたのを 1 回にした。結果は同じ）。boots が `BoxerBody` を触っていれば取り込みで確かめる。
- `PhysicsVersion`・`LogicVersion` は上げていない（物理・ボットの判断は変えていない）。CLAUDE.md は触っていない（§3 の構成に `games/MiniRing/Characters/` を足すのは取り込む側で）。
