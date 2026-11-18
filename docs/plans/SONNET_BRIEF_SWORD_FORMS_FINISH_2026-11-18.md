# 指示書: 刀・ロングソードの技の仕上げ（Codex の途中から）— 2026-11-18

元の指示書: `docs/plans/CODEX_BRIEF_SWORD_TECHNIQUES_2026-11-18.md`（必ず読む）。Codex がクレジット切れで止まった所から続ける。
Codex は報告・文書を書く前に止まった。作ったもの（コミット `eec4de4`、マージ済み）:
- 型: `games/MiniRing/Motion/SwordForms.Additional.cs`（刀: shomen・migi-nagi・hidari-nagi・gyaku-kiriage・gyakufu・kote・kote-men・do-tsuki・katate-tsuki・tsuka-ate・oshi-giri・hiki-do・mae-geri、ロングソード: scheitelhau・schielhau・krumphau・zwerchhau-left・unterhau-left・stich-low・knaufschlag・schnitt・tritt など。実際の一覧はコードで確かめる）
- 狙い: `MotionAim`（手・肩などの狙い）、`SwordFormSkills.json`（aimlab の値）、柄頭を打つ端（`Weapon.StrikeEndsX`）、`StrikeDrill`
- テスト `SwordExpansionTests.cs`、Arena の `SwordFormLab.cs`・`AimLab`・`ImitationVideo` の変更
- 測定の生の記録 `docs/arena/sword_forms_*.md`、教科書の候補 `docs/arena/sword_forms_{katana,longsword}_adopt.json`
- 教科書 `Bots/weapons/katana.json`・`longsword.json` は重みと `MinReach: 0.1`（ロングソードは `AutoAim: true`）に**変えられている**が、確かめは終わっていない。

## 分かっている問題（Claude が読んだ数字。`docs/arena/sword_forms_handle_confirm_{before,after}.md`、種 171）
| | 自傷セル/試合 前 → 後 | 勝-分-負 前 → 後 | 当たった振り |
|---|---|---|---|
| 刀 | 28 → 78 | 5-1-6 → 2-6-4 | 81% → 69% |
| ロングソード | 70 → **472** | 4-3-1 → 5-3-0 | 79% → 44% |
- `sword_forms_selfcuts_after.md`: 自傷のほとんどは「狙いでずらした unterhau（胴狙い）」で体が目標から遅れて自分の胸・腿を斬る（1304 セル）と、構え・歩きの間の自傷。ロングソードの `AutoAim: true` と低い `MinReach` が原因の候補。

## やること（優先順）
1. **自傷を戻す**: 原因を 1 つずつ確かめる（AutoAim を切る／unterhau・unterhau-left の重みを下げる／狙いのずらしの上限 `AimTuning.MaxShift` を技ごとでなく教科書の値で下げる、など）。判断の式を種類で分けない（§1.3）。教科書の値で直すのを先に。
2. **最後の比べ**: 使っていない種（例 `--seed 271` と `--seed 391`）で、変更前（`c34577e` の教科書: 重み null・MinReach 0.33・AutoAim false）と変更後を `handlelab --pairs jin:konrad,jin:jin,konrad:sean --bouts 4 --seconds 30` で比べる。採る条件: 勝ち負け・セル差が悪くならず、自傷セル/試合が前の 1.5 倍以内。満たさない武器は、新しい技を残しつつ教科書を前の値へ戻す（技は意図操作・名簿の教育で使える）。採否を書く。
3. **見た目**: raytrek の `docs/video/sword_forms_katana_*.mp4` から各型 4〜6 コマを PNG に抜き（ffmpeg）、ローカルに持ってきて Read で見る。斬る向きが名前どおりか（右薙ぎは右から、逆風は真下から、柄頭打ちは柄頭が前へ）を報告に書く。ロングソードの型の動画が無ければ撮る（`imitate --weapon longsword --only <名前> --out ...`）。
4. **文書**: `docs/SWORD_MOTION.md` に技の表（名前・向き・狙い・間合い・aimlab の値）、`docs/BOT_PDCA.md` に仮説 → 結果 → 採否、`CLAUDE.md` §8 に近道（手で作ったキー・柄頭を打つ端の一覧・前蹴りの扱い）、`docs/GUIDE.html` の該当節、README のテスト件数。
5. **全テスト**（Probe を除く）が通ること。LiveStreamTests の 2 件は raytrek の ffmpeg が古く SRT/NVENC p4 が無いので Inconclusive になる（Codex が足した確かめ）— それは既知でよい。
6. 報告 `docs/plans/done/SONNET_REPORT_SWORD_FORMS_2026-11-18.md`（作った技の表・前後の数字・採否・コマの所見・残り）。

## 実行の場所（大事）
この PC は空きメモリが 1 GB も無い。**ビルド・テスト・Arena の測定はこの PC で走らせない**。raytrek で走らせる:
- `bash C:/Users/smiya/AppData/Local/Temp/claude/edeb7fa9e714dd6f/db419768-03ca-4cb0-99f5-fe8c5b7ff5c3/scratchpad/rt.sh sword-forms "<cmd.exe のコマンド>"` — 今のローカルの HEAD を raytrek のクローン `C:\Users\smiya\repos\sword-forms` へ送り、そこでコマンドを走らせる（先に `git commit` しておく。途中のコミットは `wip: ...` でよい）。
- 結果のファイルを持ってくる: `bash .../rt.sh sword-forms --get docs/arena/x.md docs/arena/x.md`
- 例: `rt.sh sword-forms "dotnet test tests\MiniRing.Tests -c Release --filter FullyQualifiedName!~Probe"`、`rt.sh sword-forms "dotnet run --project games\MiniRing.Arena -c Release -- handlelab ... --out docs\arena\x.md"`。
- 長い測定（10 分以上）は Bash の `run_in_background` で。raytrek は 1 本ずつ（`--parallel 4` まで）。
- 動画（docs/video）は git の外。持ってくるのは確かめる PNG だけ。

コミットはこのワークツリーのブランチへ（`feat:`/`docs:`/`test:` の形、付記なし）。プッシュしない。終わったら最後のコミットの SHA と報告の要約を返す。
