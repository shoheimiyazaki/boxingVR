# 報告: テコンドー（2026-11-16）

**完了**（確かめていない所あり、下）

## 作ったもの
- 教科書 `games/MiniRing/Bots/taekwondo.json`: KickRate 0.75・KickDuration 0.95・KickRangeFit 1.5、蹴りの重み 回し 3・横 2・前/ロー/ミドル/ハイ/後ろ/かかと落とし 1・膝 0.3、拳（ComboWeights 1:3, 2:1, 1-2:2）は補助、パリー 0。`BotCatalog.Taekwondo`（Styles に追加）。
- キャラ `Characters/roster/diego.json`: ディエゴ・ソリス、メキシコ、素手、身長 1.1・腕 1.0・脚 1.1・筋肉 0.92、教育は空、順 13。
- 蹴り 4 種（`KickTechnique` の末尾: Roundhouse・Side・Back・Axe）。`KickSpec` の目標の経路 + 体の向き（FaceDirection）・胴のひねり（TwistBias）の経路。種類ごとの倍率・当たり判定なし。詳細・表は `docs/TAEKWONDO.md`。
- 選び方: `BotStyle.KickRangeFit`（0 = 従来と同じ重みと足し順。正なら今の間合いと蹴りの `Standoff` の合い具合で連続に重みを変える）。
- 当たった足の部位: `HitEvent.KickerPart`（Thigh/Shin/Foot/Toe）、`Fighter.KickPartOf`。
- `LogicVersion` 33→34（履歴 1 行）。`PhysicsVersion` は据え置き（体の物理・当たりは変えていない）。`CharacterSamenessTests` は全部通った。
- `VideoMaker` に環境変数 `MINIRING_KICKLOG=1` で蹴りの当たりを stderr に出す診断を追加。`karatelab` の蹴りの表に「当たった足の部位」列を追加。

## 測定
- `docs/arena/taekwondo_kicks.md`: 9 種すべて当たり（回し 22・横 24・後ろ 11・かかと落とし 20 / 2 本×20 秒）。蹴った側が倒れたのはミドルとかかと落としの各 1 本。足の速さ 1.5〜3.3 m/s。
- `KickContactTests`: 回し蹴り = すね、横蹴り = つま先・足の箱、後ろ蹴り = つま先、かかと落とし = すね→足の箱。体の向きは 1.1/1.5/2.6 rad 回る。
- 試合 `docs/arena/taekwondo_league.md`（種 1・各 4 試合×45 秒）: ディエゴ 2 勝 10 敗（空手家 1/4・swarmer の ramon 1/4・剣士 jin 0/4）、どちらも届かない間合い 56 %。間合いを詰めた案は種 1 で 5 勝・種 7 で 2.5 勝（元は 4.5 勝）と食い違い、採らず（`taekwondo_league_v2_seed*.md`、`v1_seed7.md`）。

## 動画（`media_tmp/`、git に入れていない）
- `taekwondo_vs_karate.mp4`（ディエゴ 対 アキラ、40 秒、速さ 1、種 3、全体 48.6 秒。結果は アキラ勝ち）。近接の乱戦で、蹴りが蹴りに見える場面は少ない（コマの確認は粗い）。
- `kick_rh.mp4`・`kick_back.mp4`（動かない相手への単独の蹴り）と 4 倍スロー `slow4x_roundhouse.mp4`（元動画 6.9 s〜）・`slow4x_back.mp4`（9.6 s〜）。コマで見た: 回し蹴りは脚が横へ高く上がり、体が回って相手の胴に当たり倒した（蹴りに見える）。後ろ蹴りは背を向ける回転と膝の引き上げは見えるが、後ろへ伸びる脚ははっきり見えない（弱い）。かかと落としはコマで見ていない。Claude の目で確認してほしい。

## テスト
- `dotnet test ... --filter "FullyQualifiedName!~Probe"`: 726 件（新規 `TaekwondoTests` 4 件・`KickContactTests` に 1 件追加）。最初に `EveryMenuPage_Renders`（肖像の数 12→13）が落ち、期待値を直して合格。他は全部合格（全体を 1 回通した後の修正は該当テストだけ再実行）。README の件数を 726 に。
- 名簿 12→13 に合わせて `CharacterFileTests`・`CharacterRosterTests` の数と表を更新。

## 文書・§8
`docs/TAEKWONDO.md`（新規）・`docs/CHARACTERS.md` §7・`CLAUDE.md` §3・§8 に「テコンドーの蹴り」の近道（回転は体の向きの目標を変える近道・胴のひねり上限 35°・足の速さは `KickFootForceGain` のまま・足の形は足首の曲げだけ）。

## 弱い所・確かめていない所
- 蹴りの足は 0.6〜3.5 m/s と遅く、試合では守られやすい。試合は 2 勝 10 敗。
- かかと落とし: すねが先に当たる。Standoff 0.8 だと蹴った側が倒れた（0.85 に決めたが揺れやすい）。
- 後ろ蹴りの見た目が弱い。かかと落としの動画コマは未確認。
- 蹴りの選び方は間合いだけ（頭の高さ・相手の拳の読みは使っていない）。`KickRangeFit` の効果自体を直接確かめるテストは無い（クランプの確認のみ）。
- `PhysicsVersion` 据え置きの判断は `CharacterSamenessTests` 通過で確認。KARATE.md の蹴りの表（旧）の数値は更新していない（現測定は taekwondo_kicks.md）。
- ディエゴが VR・デスクトップのメニューで出ることはテスト（`MenuPagesVisualTests`）で確認、画面の目視はしていない。
