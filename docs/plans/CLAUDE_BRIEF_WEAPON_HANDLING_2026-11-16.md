# 作業指示（2026-11-16）: ロングソード・日本刀・こんぼうの扱いを鍛える（教科書の側）

作業場所: ワークツリー `E:\repos\ClaudeProjects\myvrgame1\.claude\worktrees\weapons`（ブランチ `claude/weapons`）だけ。master・他のワークツリーには触らない。コミットはワークツリーの中でだけ（区切りに使ってよい）。プッシュしない。日本語。

最初に読む: `CLAUDE.md`（§1 全部、特に **§1.8**、§4、§6、§8 の武器まわり「剣士の自分の体を避ける通り道・技の時計」「武器の接触の掃き直し」）、`docs/plans/CHARACTER_EDUCATION_PLAN.md`、`docs/WEAPON_DEFENSE.md`、`docs/VOXEL_WEAPONS.md`（§5.5 自傷）、`docs/BOT_PDCA.md`、`games/MiniRing/Bots/SwordsmanBot.cs`・`FormSwordsman.cs`・`SwordBot.cs`・`ClubBot.cs`・`WeaponDefense.cs`・`DisarmedFallback.cs`、`games/MiniRing/Characters/`（`CharacterEducation.cs` の `weaponBot` は予約だけ）、Arena の `guardlab`・`guardspar`・`awaylab`・`selfcuts`・`rosterleague`。

## ユーザーの指示
ロングソード、刀、こんぼうなどの扱いを鍛える。

## 決めたこと
- **鍛えるのは教科書**（§1.8）: 剣士（刀・ロングソード）とこんぼうのボットの「基本の扱い」を良くする。キャラごとの教育は次の段（E2）。体の物理・武器の材料は変えない（`PhysicsVersion` は上げない）。判断・技の経路の値を変えたら `BoxerBot.LogicVersion` を上げ、履歴に 1 行。
- **1. まず、教育できる形にする**: 剣士・こんぼうの判断の値（`SwordDefense`・`HitAndAway`・`AimTuning`・形（`FormSwordsman`）の値・`ClubBot` の値）を、`BotStyle` と同じく JSON に読み書きでき、`Validated()` で範囲を検証できる形（init プロパティの class/record）にまとめる（例: `WeaponStyle`）。武器ごとの教科書を JSON の組み込みリソース（例: `Bots/weapons/katana.json`・`longsword.json`・`club.json`）として持つ。値を移すだけの段階で試合がビット単位で同じことをチェックサムで確かめる（変える前に Probe で取る）。そのうえで `CharacterEducation.WeaponBot` を本物の上書きにする（キャラの解決で武器の教科書 → `education.weaponBot` を重ね、試合と記録の見出しへ。教育が空なら同じ試合）。
- **2. 物差し**: 武器の「扱い」を数で見る。少なくとも次を、刀・ロングソード・こんぼうそれぞれ、同じ武器・別の武器・拳の相手に対して:
  - 振りの質: 当たった振りの割合、当たったときの刃筋（刃の向きと動きの向きのずれ）、当たったときの刃先の速さ、1 振りで相手から欠けたセル、切断・骨折に至った数。
  - 守り: `guardlab` の表（受けた・外した・食らった）。
  - 自傷: `selfcuts`（自分の武器が自分を削った回数）。
  - 試合: 勝ち・K.O.・与えた/受けた量（`rosterleague --chars jin,konrad,sean,...`）。
  基準の表を `docs/arena/weapon_handling_baseline.md` に。
- **3. 鍛える**: 表から一番弱い所を 2〜3 個選び（例: 刃筋が立っていない・間合いの外で振る・こんぼうの当てどころ）、仮説 → 値または経路を変える → **使っていない種・使っていない相手で比べ直して**良いときだけ採る。探索は `guardspar` と同じ考えでよい（世代ごとに種を変える）。1 回のリーグ・探索は 2 時間以内（この PC はメモリが少なく、ほかの作業も動いている。並列に何本も走らせない）。
- §1.2・§1.3: 技は目標の経路として書き、体が物理で追う。「この技ならこの姿勢」を書かない。武器の種類で判断を分けず、武器の長さ・重さ・握り（片手/両手）など見える量から連続に出す（今そうなっていない所があれば報告）。

## 確かめ（完了の条件）
- 値を移した段: チェックサムが一致するテスト。`weaponBot` の教育を入れたキャラの解決・記録 → 再生の一致テスト。読み込みの検証（知らない鍵・範囲外）。
- 鍛えた段: 前後の表（使っていない種）を `docs/arena/weapon_handling_*.md`、仮説 → 結果 → 採否を `docs/BOT_PDCA.md`。
- 動画: 刀 対 ロングソード、こんぼう 対 刀を前後 1 本ずつ（`video --red-char jin --blue-char konrad --seconds 40 --speed 1` など）、`media_tmp/`（git に入れない）。良くなった振りが見える秒を報告に。Claude が見て確かめる。
- 全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`、README の件数。
- 文書: `docs/WEAPON_DEFENSE.md`・`docs/VOXEL_WEAPONS.md`・`docs/CHARACTERS.md` §5（`weaponBot`）・`docs/plans/CHARACTER_EDUCATION_PLAN.md`。

## 触らない
体の物理・ボクセル（`Fighter*.cs`・`Voxels/`・`Footwork.cs`。別の作業が首・背骨を入れている）、`PunchBook.cs`・`DefenseBook.cs`・`Technique.cs`（技の進化の作業）、`KarateLab.cs`、`TvShowGraphics.cs`・`TvScout.cs`、`docs/plans/CODEX_BRIEF_*`、名簿のキャラの体・顔の値。

## 報告
`docs/plans/SONNET_REPORT_WEAPON_HANDLING_2026-11-16.md`（先頭に「完了」か「未完了」）: 教育できる形（JSON の例）、基準の表と弱点、試したことと採否、前後の表、動画の場所と秒、テスト件数、確かめていない所。最終メッセージは 1 回だけ。背景の処理を残さない。
