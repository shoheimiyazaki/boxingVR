---
name: tv-commentary
description: WVC・VFC の試合動画に、実況・解説・選手の心の声などの語り（bN.extras.json）を書く。tools/tvshow/tvshow.ps1 が「足す台詞がまだありません」で止まったとき、LLM（Haiku）に台本のテイクを書かせるとき、中継の実況・解説を直すとき、別の語りの型（一人実況・心の声・別の個性の組み合わせ）で作るときに使う。
---

# 実況・解説の台詞を書く

**まず [docs/commentary/README.md](../../../docs/commentary/README.md) §2 を読む**（共通の決まり: 自然な話し言葉・60 字・自由に語る・言わない言葉）。組み合わせが指定されていなければ tv-duo（実況 tachibana ＋ 解説 saeki）。決まりの本体は `docs/TV_BROADCAST.md` §4。

## 団体ごとの語り口（WVC と VFC、`docs/TV_BROADCAST.md` §1.1）

札の `"org"`（既定 wvc）で、競技・団体の紹介・語り口がプロンプトへ差し込まれる（団体のファイル `games/MiniRing.Arena/Orgs/<id>.json` の `commentary`・`rulesSheet`。events.json の `rules` と `org` にも入る）。手で書くときも、**`bN.digest.md` の「この試合の決まり」と団体の行を先に読む**。

- **WVC**: 品のある実況（落ち着いて伝え、要所で声を張る）と、なぜ効いたかを言う解説。ダウン・カウント・判定がある。
- **VFC**（Voxel Fighting Championship、競技は Voxel Fighting）: 熱く荒い実況（見えたことを叩きつけるように、倒れても止まらない攻防と決着で声を張る）。解説は「どこが壊れたか・あと何が残っているか」（折れた骨・削れた肉・失った手足と、残った体で次に何ができるか）。**ダウンもカウントも判定も無い**: 「ダウン」「10 カウント」「K.O.」「判定」と言わず、「倒れた、それでも止まらない」「決着（相手が死んだとき）」「5 ラウンドを終えて両者生還、引き分け」と言う。ラウンドの合図はゴングでなくホーン。レフェリーはいない。休みでも体は治らない（傷はそのまま）。死・傷・残った体を言ってよい。ゲームの裏側の言葉と数値（ダメージ＋数字・打撲・％・VGV・ボット・物理・セル）を言わないのは WVC と同じ。

## 既定: LLM のテイク（`docs/TV_BROADCAST.md` §4.4）

札に `"commentary": { "writer": "llm", "model": "haiku" }` があれば、台詞は LLM が全部書く。手順はリポジトリに固めてあり、テイクごとの味付け（空気・話題。種で選ぶ）だけが変わる。**プロンプトを自分で書き直さない**（方針を変えるなら `tools/tvshow/prompts/tv-duo-free.md` を直す）。

CLI（`claude -p`）のログインが生きていれば `tvshow.ps1 -Stage write`（全試合）か `write-commentary.ps1 -Bout bN` だけでよい。エージェントの中から（CLI が使えないとき）は、試合ごとに:

1. `powershell -ExecutionPolicy Bypass -File tools\tvshow\write-commentary.ps1 -Card <札> -Bout bN -Mode prepare` → `PROMPT: <path>` が出る。
2. Haiku のサブエージェント（`model: haiku`）に、作業場所（このワークツリーの根）と「`<path>` を読んで、その通りにする。日本語で」だけを渡す。中身を言い足さない（テイクの再現のため。言い足したいことは札の試合の `notes` に書く）。試合ごとのサブエージェントは並列でよい。
3. `... -Mode finish` → 点検 OK なら記録して終わり。`FIX: <path>` と終了コード 2 なら、同じサブエージェントに「`<path>` を読んで直す」と送り、もう一度 finish（最大 2 回、その後は NG のまま記録される）。
4. `tvshow.ps1 -Card <札> -Stage dub` → `-Stage package`。声入れ後の点検の「ずれ」は、前の台詞を短くして直す。

前のテイクへ戻す: `write-commentary.ps1 -Card <札> -Bout bN -Use <out>\takes\bN\<テイク>`。テイクの記録は `take.json`（種・味付け・台詞の数・点検）。

**見る所**（数字が OK でも聞いて確かめる）: 解説が全体の 4 割前後あるか、休みに見立て・プロフィールの話があるか、体言止め・辞書の形の伝聞が続いていないか、事実（勝敗・ダウン・骨折）が記録どおりか。良くなければ新しいテイク（ばらつきは味方）。

## 手で足す（下書きを使う型、別の型）

`<out>/bN.digest.md`（区間ごとの出来事・プロフィール）と `<out>/bN.draft.json`（道具の骨組み: 紹介・コール・カウント・リプレイの入り・決着）を読み、足りない所を `<out>/bN.extras.json` に足す。下書きは書き直さない（思い違いは `"drop"` で消す）。

```json
{ "drop": ["下書きの思い違いの台詞（そのままの文）"],
  "lines": [ { "at": 13.6, "speaker": "announcer", "text": "佐伯さん、アキラ選手の作戦は？", "excited": false },
             { "at": 16.4, "speaker": "commentator", "text": "蹴りで足を止めてから、腹を狙いたいところですね。", "excited": false } ] }
```

`at` は言ってほしい時刻 [s]。`tvmerge` が空きに入れ、入らない台詞は落とす（表示される）。

## 欠かせないもの（点検 `tvlint` が見る）

ラウンドの始まりから 6 秒以内に「第Nラウンド」／ダウン・決着は 1 秒以内に実況がコール／リプレイに変わったら 2 秒以内に「リプレイ」「スロー」とどの場面か／カウントの最中は解説に問いを振らない／休みごとに掛け合い 1 組以上／1 台詞 60 字まで／言わない言葉（ダメージ＋数字・打撲・％・VGV・ボット・物理・セル）。

```bash
games/MiniRing.Arena/bin/Release/net10.0/miniring-arena.exe tvmerge --draft <out>/bN.draft.json --extras <out>/bN.extras.json --out <out>/bN.script.json
games/MiniRing.Arena/bin/Release/net10.0/miniring-arena.exe tvlint --script <out>/bN.script.json --events <out>/bN.events.json
```
