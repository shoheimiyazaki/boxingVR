WVC（Wonderful Voxing Council）の「{{event}}」の試合動画に、テレビ中継の 2 人の声を付けます。日本語で。

- 実況（`announcer`）: 立花。試合を伝える役。
- 解説（`commentator`）: 佐伯。試合を見て話す役。

何を、どの順で、どれだけ話すかは、2 人に任せます。

試合: **{{key}}**（{{bout}}）。作業場所はリポジトリの根。資料の置き場 OUT = `{{out}}`。

## 手元にある資料（使うかどうか・どこを使うかは自由）

- `OUT/{{key}}.events.json`: 動画の中で起きることと、その時刻（画面に出る表・リプレイ・勝敗も）
- `OUT/{{key}}.digest.md`: それを読みやすくまとめたものと、両選手の資料
- `OUT/{{key}}.draft.json`: 機械が作った下書き。時刻を見るのに使ってもよい（台詞を真似る必要はない）。下書きは声にならない: 動画に入る声は、あなたが書く台詞だけ
{{cardNotes}}

## 守ってほしいことはこれだけ

1. 記録に無いことを、起きた事実として言わない（勝敗・ダウン・誰が何を当てたかは記録どおり）。
2. ここは作中の世界: 競技は Voxing、主催は WVC。ゲームの裏側の言葉（VGV・ボット・物理・セル・ダメージの数値・打撲・%）は言わない。
3. 実況の役として、ラウンドが始まったら 6 秒以内に「第Nラウンド」、ダウン・決着は 1 秒以内にそれと分かる言葉、リプレイに切り替わったら 2 秒以内にリプレイだと言う。
4. 1 台詞は 60 字まで。声は読み上げで、次の台詞と重なると後ろが押し出される（1 台詞の時間 ≒ (字数 × 0.16 秒 ＋ 句読点ごと 0.48 秒) ÷ (1 ＋ 速さ)。速さは実況 0.3・解説 0.15）。

## 書くもの: `OUT/{{key}}.extras.json`（既にあれば上書き）

```json
{ "style": "open", "useDraft": false, "lines": [ { "at": 0.3, "speaker": "announcer", "text": "…", "excited": false } ] }
```

`at` は動画の秒。`excited: true` にすると声が高く速くなる。

## 確かめる

```bash
{{exe}} tvmerge --draft OUT/{{key}}.draft.json --extras OUT/{{key}}.extras.json --out OUT/{{key}}.script.json
{{exe}} tvlint --script OUT/{{key}}.script.json --events OUT/{{key}}.events.json
```

tvmerge が「入らなかった台詞」を出したら、時刻か長さを直す。tvlint が OK になったら終わり。最後に台詞の数（実況・解説）と点検の結果を 2 行で報告する。
