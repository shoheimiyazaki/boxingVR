---
name: tv-commentary
description: WVC の試合動画に、実況・解説・選手の心の声などの語り（bN.extras.json）を書く。tools/tvshow/tvshow.ps1 が「足す台詞がまだありません」で止まったとき、中継の実況・解説を直すとき、別の語りの型（一人実況・心の声・別の個性の組み合わせ）で作るときに使う。
---

# 実況・解説の足す台詞を書く

**まず [docs/commentary/README.md](../../../docs/commentary/README.md) を読む**（LLM 向けの説明書: 型 style・役 role・個性 persona の層と組み合わせ）。組み合わせが指定されていなければ tv-duo（実況 tachibana ＋ 解説 saeki）。下はその既定の型の要約。決まりの本体は `docs/TV_BROADCAST.md` §4。PDCA の記録は `docs/TV_COMMENTARY_PDCA.md`。

## 読むもの（試合ごと、`<out>` は札の `out`）

- `<out>/bN.digest.md`: 区間（冒頭・ラウンド・休み・決着の後・勝敗の画面）ごとの出来事。`実況:` は画面で見えること、`解説:` は体の中・狙い。
- `<out>/bN.draft.json`: 道具が置いた骨組み（選手紹介・ダウンのコール・カウント・立った・骨折と切断・ラウンドの区切り・リプレイの入り・決着・勝者）。**これは書き直さない**。足りない所に足す。

## 書くもの: `<out>/bN.extras.json`

```json
{ "drop": ["下書きの思い違いの台詞（そのままの文）"],
  "lines": [ { "at": 13.6, "speaker": "announcer", "text": "サエキさん、アキラ選手の作戦は？", "excited": false },
             { "at": 16.4, "speaker": "commentator", "text": "蹴りで足を止めて、それから腹です。", "excited": false } ] }
```

`at` は言ってほしい時刻 [s]（digest の時刻）。`tvmerge` が下書きの空きに入れ、入らない台詞は落とす（落ちた台詞は表示される。大事なら時刻か長さを変える）。

## 書き方（ラジオで聞いても分かること）

1. **主語**: 動き・体の状態の台詞には必ず選手の名前（「ハンマーの左、アキラの顔に入った！」）。「左が入った！」は誰か分からない。
2. **実況**（`announcer`）: 打ち合いの最中に見えることを短く。`excited: true` は打ち合いの声だけ。
3. **解説**（`commentator`、サエキ）: 間（休み・ダウンの後・リプレイ・打ち合いが途切れた所）に、理由と見通し。「〜が効いた」だけでなく「肝臓が効いて足が止まった」。
4. **掛け合い**: 休みごとに 1 組以上。実況が「サエキさん、〇〇選手の××は？」と**名前を入れて**問い、すぐ後に解説が答える（組は続けて書く）。**ダウンのカウント中は問いを振らない**（解説が自分から理由を言うのは良い）。
5. **短く**: 1 台詞 28 字まで。長い説明は 2 台詞に分ける。
6. **言わない**: ダメージの数値・打撲・％・VGV・ボット・物理・セル。割合は「3割」「半分」。ゲームを知らない人が一言で分かる言葉（「心臓の3割が壊れています」）。
7. **型を変える**: 「〜の…が、〜の…に」を続けない。「打ち返す！」「もう一発！」を混ぜる。
8. 下書きと同じことを言わない（コール・カウント・立った・勝者は下書きが言う）。同じ台詞を 2 度書かない。

## 確かめる

```bash
dotnet run --project games/MiniRing.Arena -c Release -- tvmerge --draft <out>/bN.draft.json --extras <out>/bN.extras.json --out <out>/bN.script.json
dotnet run --project games/MiniRing.Arena -c Release -- tvlint --script <out>/bN.script.json --events <out>/bN.events.json
```

点検が OK になったら、`tools/tvshow/tvshow.ps1 -Card <札> -Stage dub` のあと `-Stage package` で声入れと結合。声入れ後の点検で「ずれ」が出たら、前の台詞を短くする。
