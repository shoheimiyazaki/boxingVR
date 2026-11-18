# キャラクターの教育の計画（2026-11-16〜）

CLAUDE.md §1.8 の実装計画。タスクと記録を兼ねる（段ごとに状態を書き足す）。

## 考え方
- ファイター = **体**（体格・武器・はめる物・顔）＋ **教科書**（スタイル）＋ **教育**（教科書からの差分）。1 キャラ 1 ファイル。
- 教科書は全キャラ共通の「基本」: スタイル JSON（`champion.json` など。技の進化の作業で `PunchBook`・`DefenseBook` もスタイルが持つ形になる）と、体格を読む層（`claude/build-tactics`。見えている寸法から連続に間合い・前に出る強さ・回避の選び方を変える）。
- 教育は差分だけを持つ: 空なら教科書そのもの（今と同じ試合）。教育で変えるのは入力の側（判断の係数・技の経路・選ぶ重み・相手ごとの備え）だけ。体の強さは変えない。
- 試合の中では固定。学ぶのは試合の間だけ。記録は試合開始時の教育（解決したスタイルの全体）を持つ。

## キャラクターファイルの形（案）
`games/MiniRing/Characters/roster/<id>.json`（組み込み）／ プレイヤーが作ったキャラ・新人はアプリのデータの場所。

```jsonc
{
  "id": "marcus", "name": "マーカス・\"ザ・ハンマー\"・ブルックス", "hudName": "ザ・ハンマー",
  "origin": "アメリカ", "country": "usa", "line": "ヘビー級の王者",
  "look":  { /* FaceLook: 肌・髪・瞳・髪型・眉・ひげ・顔の寸法 */ },
  "body":  { "build": { "height": 1.12, "arms": 1.1, "legs": 1.0, "muscle": 1.15 }, "wear": "Glove", "weapon": null },
  "textbook": "champion",
  "education": {
    "style": { /* BotStyle の一部（書いた値だけ教科書を上書き。技の本 Punches/Defenses も） */ },
    "weaponBot": { /* 剣士・こんぼうの判断の値の一部 */ },
    "vs": { "akira": { /* その相手に限る上書き */ } }
  },
  "record": [ { "date": "2026-11-17", "method": "cma-es", "gens": 12, "trainOpponents": ["..."], "heldOut": ["..."],
                "seeds": "...", "before": { "score": 0.0 }, "after": { "score": 0.0 }, "adopted": true } ]
}
```
- 読み込みで検証（範囲・色・教科書の名前・上書きの鍵が実在すること）。壊れていれば分かるメッセージで止める（組み込み）／ 既定へ戻してログ（ユーザーのファイル）。
- 解決: 教科書 → `education.style` → `vs[相手]` の順に重ね、`BotStyle.Validate` を通した全体を試合へ渡す。記録の見出しには解決後の全体を入れる（両者とも。プレイヤーの側の技の経路も）。

## 段
| 段 | 中身 | 前提 | 状態 |
|---|---|---|---|
| E0 | `characters.json` を 1 人 1 ファイルへ分け、教育（空）・育ちの記録の欄を持つ形にする。解決したスタイルを両コーナー・プレイヤーの意図操作へ渡し、記録の見出しに入れる。教育が空なら今とビット単位で同じ（チェックサムのテスト） | build-tactics の取り込み | 済（2026-11-16、`Characters/roster/<id>.json`・`EducationResolver`・`BoxingCorners.PlayerStyle`・リプレイ版 8。`weaponBot` は欄だけ予約。仕様は `docs/CHARACTERS.md` §5、報告は `docs/plans/done/SONNET_REPORT_CHARACTER_FILES_E0_2026-11-16.md`） |
| E1 | 体格を読む層を教科書の一部に（係数は教科書の値、教育で上書き可） | `claude/build-tactics` | 済（2026-11-16、`BuildRead`・LogicVersion 30。アキラは全員が層を使うと下がる → E3 の教育で） |
| E2 | 教育の道具: Arena `educate --char <id> --gens N [--vs roster]`。CMA-ES（技の進化の作業のもの）で教育の値を探す。鍛える相手・種と、使わない相手・種に分け、使わない方で良いときだけ採る。最初は判断の係数と選ぶ重み（小さいゲノム）、次に技の経路 | 技の進化（`claude/evo-*`）の取り込み | 未 |
| E3 | 全キャラの教育を回す（1 人ずつ、体と武器に合わせて）。前後の総当たりの表を `docs/arena/education_*.md` に | E2 | 未 |
| E4 | 相手ごとの備え（`vs`）: その相手に対して、一般の教育より良いときだけ採る | E3 | 未 |
| E5 | プレイヤーのキャラ: 自分のキャラファイルの教育で意図の解釈・技の経路が決まる（ボットと同じ実行部）。キャラメイク（体格の範囲・武器・はめる物・顔・教科書を選ぶ。教育は空から） | E0 | 未 |
| E6 | 新人を育てるモード: 決まっていない新人を作り、試合の合間に教育を少しずつ進める（1 試合ごとに数世代、時間の上限つき）。成長の記録をメニューに見せる | E2・E5 | 未 |

## 守ること
- 教育で体の強さ（筋力・質量・材料・威力の倍率）を変えない（§1.7）。
- キャラ名・相手の名前でコードを分けない。違いはファイルの値。
- 鍛えた相手・種だけに合わせ込んだ教育を採らない（使わない相手・種で比べ直す）。
- 教育を変えたら、そのキャラの育ちの記録に 1 行（何を・どれだけ・前後の数値・採否）。

## 武器のボットの教科書と教育（2026-11-16、`claude/weapons`）
- `education.weaponBot` を本物の上書きにした（`WeaponStyle`・`Bots/weapons/*.json`・`WeaponEducation`。解決した値は `BoxingCorners.PlayerWeaponStyle`/`CpuWeaponStyle`・記録の見出し（リプレイ版 9）へ）。教育が空なら教科書そのもの（試合はビット単位で同じ）。仕様は `docs/CHARACTERS.md` §5。
- 教科書の側を鍛えた（LogicVersion 33。ブランチでは 31）: 刀・ロングソードに技の時計を入れた。E2 の道具としては、`handlelab`（物差し）・`handlespar`（練習試合での探索。使っていない種・相手で比べ直す）が使える。探索の教訓: 1 候補あたり 6 試合では揺らぎが値の違いより大きいので、自傷のセルのように物理で測れる量を主にして、試合を増やす。
- 武器の教育で書ける値: `Defense`・`HitAndAway`・`Clearance`・`Pacing`・`Rest`・`MinReach`・`TechniqueWeights`（技の名前 → 重み）・`KeepReach`・`ClubRange`・`ClubRest`・`ClubStandOff`・`ClubExact`・`ClubSwingWeights`。キャラごとの教育（E3）で、体と武器に合わせて使う。記録は `docs/BOT_PDCA.md`・`docs/arena/weapon_handling_*.md`。
