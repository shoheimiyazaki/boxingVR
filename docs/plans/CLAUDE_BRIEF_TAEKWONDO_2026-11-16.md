# 作業指示（2026-11-16）: テコンドーの教科書と、テコンドー家 1 人

作業場所: ワークツリー `.claude/worktrees/taekwondo`（ブランチ `claude/taekwondo`、master `b9aee70` から）だけ。master・他のワークツリーには触らない。コミットはワークツリーの中でだけ。プッシュしない。日本語。

最初に読む: `CLAUDE.md`（§1 全部、特に §1.2・§1.3・§1.7・§1.8、§4、§6、§8 の「空手」(2)）、`docs/KARATE.md`（蹴り: `KickSpec`・`ResolveKicks`・`KickFootForceGain`・軸足・`karatelab` の蹴りの表）、`docs/CHARACTERS.md`（体格・キャラのファイル）、`docs/plans/CHARACTER_EDUCATION_PLAN.md`、`games/MiniRing/Bots/karate.json`・`KickSpec.cs`・`BoxerBot.cs`（蹴りの選び方）・`Footwork.cs`・`Fighter.cs`（脚の目標）・`games/MiniRing/Characters/`（`roster/*.json`・`countries.json`・`EducationResolver`）、`games/MiniRing.Arena/KarateLab.cs`。

## ユーザーの指示
「トンファー、片手剣と盾、テコンドー、棒術を加えて」。最初はテコンドー（素手・足技が主）。残りは後の作業。

## 決めたこと
- **教科書 `taekwondo`**（`Bots/taekwondo.json`、`karate.json` と同じ形）と、**キャラクター 1 人**（`roster/<id>.json`、架空の人物・架空の名前。国は `countries.json` にある国から。体は脚が長く細身・身長は高め。教育は空 `{}` から始める）。メニュー・`rosterleague` に出ること。
- **蹴りは目標の経路**（§1.2）: 今ある 5 種（前蹴り・ロー・ミドル・ハイ・膝）に加え、テコンドーの中心の蹴りを `KickSpec` の経路として足す: 回し蹴り（腰を回して足の甲・すねで横から。今のミドル/ハイとの違いを、骨盤のひねりと膝のたたみ→伸ばしの経路で）、横蹴り（体を横に向けて足刀で押し出す）、後ろ蹴り（背を向ける回転の経路）、かかと落とし（脚を上げて振り下ろす）。足のどこで当たるかは脚の骨のボクセルの接触で決まる（種類ごとの倍率・当たり判定を書かない、§1.3）。足の形（足の甲・足刀・かかと）は足首の角度の目標で出す。
- **構えと足の運び**: 半身で跳ねるように間合いを出入りする（`Footwork`・`RangeSense` の値で。姿勢を書き込まない）。距離は脚の長さから決まる（見える体の寸法、§1.7）。拳は補助（ジャブ・ストレートの重みを低く）。
- **選び方**（§1.3）: 蹴りの選択は、相手との距離・相手の頭/胴の高さ・相手の拳の読み（`OpponentRead`）から連続な重みで。「テコンドーなら」の分岐を判断の式に書かない（違いは JSON の値）。
- **近道の扱い**: 蹴りの足の速さは足首 1 点の引く力の近道（`KickFootForceGain`、§8 空手 (2)）で 2〜3.6 m/s しか出ていない。この作業ではその力の上限を上げない（物理を変えるなら別の作業）。回し蹴り・後ろ蹴りの回転は、骨盤・胴の目標（ひねり）の経路で作る。回りすぎて倒れるなら、それも結果として表に出す。
- 版: 判断・技の経路を足すので `BoxerBot.LogicVersion` を 34 に上げ、履歴に 1 行。体の物理・当たりを変えたら `Fighter.PhysicsVersion` を 36 に上げてチェックサムの期待値を理由つきで更新する（変えないで済むならそのまま）。既存のスタイルの試合が変わらないこと（`CharacterSamenessTests` が通ること）。

## 確かめ（完了の条件）
- テスト: 新しい 4 種の蹴りが、それぞれ台本の指令で構えて立つ相手に当たる（`KickContactTests` と同じ形）、蹴った側が立っている（骨盤の高さ）、当たる足の部位が経路どおり（回し蹴り = 足の甲かすね、横蹴り = 足の外側・かかと、かかと落とし = かかと）。教科書・キャラのファイルの読み込みと検証。
- 測定: `karatelab --only kicks` 相当の表を新しい蹴りも含めて（当たった数・欠けたセル・足の速さ・倒れた本数）、`docs/arena/taekwondo_kicks.md`。試合: テコンドー家 対 空手家・ボクサー（swarmer）・剣士（jin）を各 4 試合以上（`rosterleague` か `league`）、`docs/arena/taekwondo_league.md`。勝てなくてよい。弱い所を報告に。
- 動画: テコンドー家 対 空手家 40 秒（`video --red-char <id> --blue-char <空手家> --seconds 40 --speed 1`）と、回し蹴り・後ろ蹴りの 4 倍スロー（`media_tmp/`、git に入れない）。蹴りが蹴りに見えるか（脚が上がる・腰が回る・当たる所）を自分でコマで見て、秒を報告に。Claude が見て確かめる。
- 全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`、README の件数。
- 文書: `docs/KARATE.md`（または新しい `docs/TAEKWONDO.md`）・`docs/CHARACTERS.md`・`CLAUDE.md` §3（スタイル）・§8（新しい近道があれば）。

## 触らない
`PunchBook.cs`・`DefenseBook.cs`・`Technique.cs`（技の進化の作業）、`TvShowGraphics.cs`・`TvScout.cs`・`Tv*.cs`、`docs/plans/CODEX_BRIEF_*`、既存のキャラの体・顔の値、武器の教科書（`Bots/weapons/`）。

## 報告
`docs/plans/SONNET_REPORT_TAEKWONDO_2026-11-16.md`（先頭に「完了」か「未完了」）: 教科書とキャラのファイル（JSON の要点）、蹴りの経路、表、試合の結果と弱点、動画の場所と秒、テスト件数、§8 に足した近道、確かめていない所。最終メッセージは 1 回だけ。背景の処理を残さない。
