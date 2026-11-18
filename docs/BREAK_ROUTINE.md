# 休み（ラウンドの間）の選手の動作（2026-11-18、LogicVersion 43）

ラウンドの間の休み（`BoxingGame.InBreak`）に、選手が水を飲む・汗を拭く・スマホを見る・電話をする・深呼吸する…といった動作をする。
指示書は `docs/plans/CODEX_BRIEF_BREAK_ACTIONS.md`。軸（CLAUDE.md §1）との関係を先に書く。

- **入力は目標**（§1.1）: 動作は `FighterCommand`（両手の目標・頭のずれ・胴のひねり・体の向き）の時間変化（経路）だけで書く。休みの入力元（`IBreakSource`）が出すのは `FighterCommand` だけで、体がバネ・ダンパでそれを追った結果が見た目になる。
- **姿勢・アニメーションを書かない**（§1.2）: 「ボトルを口へ」は、頭を基準にした拳の目標の経路。頭を後ろへ反らせるのは頭のずれの目標。キーフレームの再生ではなく、点を滑らかにつないだ目標（と周期の揺れ）。
- **場合分けをしない**（§1.3）: 動作ごとに別の物理・強さの式を持たない。動作はデータ（`BreakAction`）で、同じ 1 つの評価（`BreakRoutine.Overlay`）を通る。選び方の重みは「基本 + 疲れ × (1 − スタミナ) + 傷 × 意識の薄れ」の連続の式。
- **決定的**（§1.6）: 並びは試合の種から作る種付きの乱数と、休みに入った時点の体の状態（見える・読める値）だけで決まる。記録 → 再生でビット単位に同じ（`BreakRoutineTests.ARecordedBreakReplaysBitForBit`）。

## 1. 構造

```
games/MiniRing/Break/
  BreakAction.cs    動作のデータ: 経路（Path3・Wave）・手の基準（体 / 頭）・選ばれやすさの重み（BreakWeights）
  BreakBook.cs      動作の一覧（All(tired)）と動作の名前
  BreakRoutine.cs   IBreakSource（差し替えできる入力元）と既定の BreakRoutine（くじ引き・並べる・経路を指令へ）
  BreakProp.cs      小物（ボトル・タオル・スマホ）のボクセル物と、手への付き方（PropHold）
games/MiniRing/BoxingGame.Break.cs   休みの間だけ入力元を差し替える（BreakCommand）・小物の管理
```

- `BoxingGame` の変更は最小: `Substep` の休みの枝（`Resting`）で、`RestCommand`（コーナーへ歩く・相手の方を向く・ガードを下げる）を直接渡す代わりに `BreakCommand` を通す。試合の後（`MatchOver`）と倒れている選手は今までどおり構えのまま。休みが終われば入力元と小物を捨て、元のボット（`_cpuControl` など。休みの間は読まれていないだけで、捨てていない）が操作する。`UpdateMatch`（休みの時計・再生・ラウンドの切り替え）は触っていない。
- `BoxingCorners` に `BreakActions`（既定 true。false なら休みは今までどおり構えを下げて立つだけ）と `BreakSource`（種と選手から入力元を作る差し替え口。null を返すとその選手は動作をしない）を足した。
- 好みはキャラのファイルの値: `BotStyle.BreakLikes`（動作の名前 → 倍率。無い名前は 1）。コードにキャラ名は書かない（§1.8）。

## 2. 動作の選び方（`BreakRoutine`）

1. コーナーへ着くまで（休みの歩きの指令が小さくなるか 2.5 秒経つまで）は構えのまま。着いた時点で計画を立てる。
2. 動作の数は 2〜4（種付き）。残りの休み（`BreakLeft` − 余白 0.6 秒）に、動作の長さの合計と動作の間の最短 0.5 秒 × (数 + 2)（新しい動作を足した後の数 + 1） が収まるまで、重み付きのくじ引き（同じ動作は 1 回まで）で選ぶ。入らない動作は飛ばす。
3. 動作の間の休みは、余りを (数 + 1) 等分。
4. 小物を持つ手は利き手（`BotStyle.Southpaw` でない側）が 75 %、ときどきもう一方（種付き）。
5. 重み = `Base + Tired × (1 − スタミナ) + Hurt × 意識の薄れ`（負にはならない）× `BreakLikes[名前]`。深呼吸は元の体のスタミナが少ないほど選ばれやすく、深く・速い（周期と振幅がスタミナの少なさに比例）。
6. 動作の最初と最後は、休みの構えから `Lead` 秒かけて目標へ移り、`Release` 秒かけて構えへ戻る（目標の混ぜ合わせ。smoothstep）。

## 3. 動作の一覧

値の単位は腕の長さ（約 19 cm）を 1 とした体フレームのローカル（X は動作の手の外側が +、頭の基準の目標は頭の中心からのずれ）。実際の値は `BreakBook.cs`。

| 名前 | 小物 | 経路 |
|---|---|---|
| `deepbreath` 深呼吸 | - | 両手を腰へ。頭が上・後ろへ上下する周期（吸う・吐く）。胴が小さく左右にひねる |
| `drink` 水を飲む | ボトル | 拳を頭基準で口の前へ運び、頭を少し後ろ・上へ反らし、口を離して下ろす |
| `wipe` 汗を拭く | タオル | 拳を額へ上げ、横になでながら頬・首へ下ろす（横の周期の揺れ）。頭は少し下を向く |
| `phone` スマホを見る | スマホ | 拳を顔の前下へ上げ、頭を前へ・下へ。反対の手が画面をなでるように小さく動く |
| `call` 電話をする | スマホ | 拳を耳へ当て、体の向きを相手の方から約 0.5 rad そらし、頭を耳の側へ傾ける |
| `shoulderroll` 肩を回す | - | 両手の目標が円を描く（肩が上・後ろ・下・前へ回る）。胴が小さくひねる |
| `neckroll` 首を回す | - | 頭のずれが円を描く。手は構えのまま |
| `slapface` グローブで顔を叩く | - | 両手を頬の外から頬へ 2 回打ちつける。頭は少し前へ |
| `ropes` コーナーにもたれる | - | 両腕を横・後ろへ広げてロープに掛け、頭・体を後ろへあずける |
| `glare` 相手のコーナーをにらむ | - | 頭を前へ・下げ、ゆっくり左右へ。手は構えを締めて下げたまま |
| `shakearms` 腕を振ってほぐす | - | 両手を下げたまま小刻みに上下へ振る（左右は逆位相） |

## 4. 小物（`BreakProp`）

- ボクセルの物（`VoxelGrid`、セルは 2 mm）で、見た目の色だけの専用の材質の表（`BreakPalette`。体の表とは別なので体の材質番号・.vox の色番号に響かない）。ボトル（水・キャップ）・タオル（赤い縞）・スマホ（画面が光る黒い板）。
- **剛体ではない**: 休みの間だけ、動作の間（`IBreakSource.Holding`）持っている手のグローブの中心へ付いて描かれる（`BoxingRenderer.FillVoxels`）。体と打ち合わず、体を削らない。休みの終わり・動作が終わるごとに手から消える（セコンドへ返す）。物理に入らないので `PhysicsVersion` は上げていない。
- 向きは動作ごとの持ち方（`PropHold`: 長い軸と面の向きを体フレームで書く）。**近道**: 前腕のひねり（手首の回り）は入力から制御できず、持った小物の向きが手の形で変わるのを、体フレームで書いた持ち方で代えている。ボトルは口へ向ける、スマホの画面は顔・耳へ向ける、タオルの面は顔へ向ける。
- 寸法は人形の縮尺の物を、グローブ（半径 2.4 cm）から両端がはみ出して見えるよう少し大きめにした見た目の値。

## 5. 見て確かめた（`BreakActionProbe`）

`BREAK_PROBE_DIR=<dir> [BREAK_PROBE_ONLY=drink:R,call:L] dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName~BreakActionProbe"` で、動作ごとの 6 コマを前斜めと真横から PNG にする（`drink:L` のように手を指定できる）。見て直したこと:

- 水を飲む: ボトルの口を向ける方向（前腕の向きから決めると真横になり、チューブを首にあてているように見えた）→ 口へ向ける持ち方へ。ボトルの底を上げた斜めの持ち方で飲んでいるように見える。
- 電話・スマホ: 前腕の向きに固定すると画面が横を向いた → 画面が顔・耳へ向く持ち方へ。スマホ・タオルは大きすぎて看板に見えたので縮めた。
- 動作の手の届き: 頭を後ろへ反らせすぎると拳（頭基準の目標）が口へ届かなかったので、反らせ方を小さくした。
- 肩を回す・腕を振るは、肩・手首を直接回せないので見た目の変化が小さい（目標が円を描き、体がバネで追う）。

## 6. 軸から外れている所（CLAUDE.md §8 にも書いた）

- 小物は物理の剛体ではなく、グローブの中心に付いて描くだけ。向きは持ち方（体フレームの軸）から決める。小物は体に当たらない・削らない。
- グローブの上から握る形は作っていない（グローブの中心に小物の握る点を置くだけ）。指の握りを小物に合わせない。
- 動作は手・頭・胴・向きの目標の経路で、肩・首の関節を直接回す目標は無い（肩を回す・首を回すは、手と頭の目標が円を描いた結果）。
- 休みの歩き（`RestCommand`）は変えていない。着いた判断（指令の大きさ 0.3 以下か 2.5 秒）は経験の値。
- 動作の長さ・経路の数値・重みの値は、見て決めた手の値（物理から出した値ではない）。
- 動作の選び方の重み・好み（`BreakLikes`）は入力の側の判断で、体の強さではない。

---

# 始め・休み・終わりのエモート（2026-11-18、LogicVersion 50、指示書 `docs/plans/CODEX_BRIEF_EMOTES.md`）

休みの動作と同じ作り（目標の経路のデータ `BreakAction`・入力元 `IBreakSource`・小物 `BreakProp`）を、**試合の始め・休み・試合の終わり**の 3 つの場面へ広げ、動作を 60 種（休みの 11 種 + 新規 49 種）にした。軸との関係は冒頭と同じ: 姿勢・アニメーションは書かない（手・頭・胴・向き・歩みの目標の経路だけ）、種類での場合分けは書かない（勝ち負けも好みも重みの式の中の連続の量）、決定的（種付き乱数と見える状態だけ）。

## 7. 場面

| 場面 | いつ | 構え（重ねる土台） | 長さ | 動作の数 |
|---|---|---|---|---|
| `Start` | 入場してから最初のゴングまで（`BoxingGame.InIntro`、`MatchRules.IntroSeconds`） | その場に立って相手を向き、ガードを休みと同じだけ下げる（`StandCommand`） | TV は 7 秒（0.75 倍速で約 9 秒） | 1〜3（収まる分） |
| `Break` | ラウンドの間の休み（`InBreak`） | コーナーへ歩く休みの構え（`RestCommand`） | 休みの長さ | 2〜4 |
| `End` | 決着（K.O.・判定）の後（`MatchOver`、`MatchRules.EndSeconds`） | `StandCommand` | TV は 7 秒（約 9 秒） | 1〜3 |

- 始め・終わりの間は `Resting`（打撃・ラウンドの時計が動かない）。始めの終わりにゴング（`RecentBell.Start`）。`IntroSeconds` / `EndSeconds` が 0（既定）なら従来どおり（最初の歩からゴング・決着の後はコーナーへ歩くだけ）。TV の撮影・下見（`TvShowRules.Rules`）だけが 7 秒ずつ入れるので、ゲーム・リーグ・既存のテストの試合は変わらない。
- 倒れている選手は動作をしない（入力元を作らない。K.O. された敗者は倒れたまま）。倒れた選手が試合の終わりの間に立ち上がれば、その時点から計画を立てる。
- 場面ごとの動作の表は、動作のデータ（`BreakAction.Scenes`）で引く（`BreakBook.For(scene, tired)`）。同じ動作が複数の場面で選べる（手を振る = 始め・休み・終わり、など）。**選び方の式は場面に依らず同じ**。
- 始め・終わりは動作が短い（2.4〜4 秒）ので、動作の間の最短 `EmoteGap` 0.15 秒・終わりの余白 `EmoteEndMargin` 0.3 秒（休みは 0.5・0.6）で並べる。
- 相手がいて成り立つ動作（握手）は `NeedsPartner`: 相手が立っているときだけ選べる（相手が倒れていれば選ばない）。

## 8. 重み: 勝ち負けと好み（連続の式）

```
重み = max(0, Base + Tired × (1 − スタミナ) + Hurt × 意識の薄れ + Joy × 気分) × BreakLikes[名前]
```

- **気分**は試合の終わりだけ入る: 勝者 +1・敗者 −1・引き分け・始め・休みは 0（`BoxingGame.EndMood`。勝ち数の差の符号）。`Joy` が正の動作（両手を挙げる・雄叫び…）は勝者に選ばれやすく敗者には 0 になり、負の動作（頭を抱える・天を仰ぐ…）は敗者に選ばれやすい。「勝ったらこの動作」と分けず、同じ式の係数だけが違う（§1.3）。
- 好み（`BotStyle.BreakLikes`）は名簿のキャラクターのファイル `education.style.BreakLikes`（名前 → 倍率。無い名前は 1、0 は選ばない）。コードにキャラ名は書かない（§1.8）。教育（技）の値であって体の強さではない（§1.7）。
- 試合の中では固定（学ぶのは試合と試合の間）。`ReplayMatchSettings` に `SettleSeconds` を足し、`MatchRules`（`IntroSeconds` / `EndSeconds` を含む JSON）と合わせて記録 → 再生でビット単位に同じ（`EmoteTests.AnIntroSettleBreakAndEndReplayBitForBit`）。

## 9. 動作の一覧（`BreakEmotes.cs` 新規 49 種 + `BreakBook.cs` 11 種）

場面: S = 始め、B = 休み、E = 終わり。小物は §10。

**観客へ（15）**: `wave` 手を振る（S B E）・`wavetwo` 両手を振る（S E）・`raisearms` 両手を挙げる（S E。勝者）・`fistpump` 拳を突き上げる（S E。勝者）・`blowkiss` 投げキス（S E）・`pointcrowd` 観客を指さす（S E）・`bow` お辞儀（S E。腰から折るヒップヒンジ = 頭の目標を前・下へ）・`chestbeat` 胸を叩く（S E）・`hypecrowd` 耳に手を当てて歓声をあおる（S E）・`gutspose` ガッツポーズ（S E。勝者）・`ropeperch` ロープに登る（届く範囲で。両腕を後ろ上へ掛けて反らす。S E）・`roar` 雄叫び（E。勝者）・`flex` 力こぶ（S E）・`pointup` 指を天へ（S E）・`lookcrowd` 客席を見回す（S）。

**体（12）**: `shadowbox` シャドーボクシング（S B）・`bounce` 軽く弾む（S B）・`neckcrack` 首を鳴らす（S B）・`gloveclap` 拳を打ち合わせる（S B）・`mouthguard` マウスピースを入れる（S。小物）・`stretcharms` 腕を上へ伸ばす（S B）・`sidestretch` 体側を伸ばす（S B）・`forwardbend` 前屈（S B）・`fixgloves` グローブを直す（S B）・`wipenose` 鼻をこする（S B）・`yawn` あくび（B）・`dance` ダンス（S E）。

**気持ち・作法（15）**: `selfie` 自撮り（S B E。小物）・`mirror` スマホの鏡で顔を見る（S B。小物）・`prayer` 祈る（S E）・`crossself` 十字を切る（S E）・`salute` 敬礼（S E）・`applause` 拍手（S E）・`handshake` 相手の方へ歩いて手を差し出す（E。`Stride`・`Standoff`・`NeedsPartner`）・`looksky` 天を仰ぐ（E。敗者）・`headinhands` 頭を抱える（E。敗者）・`handsonknees` 膝に手をつく（B E。息が上がっているほど）・`sitdown` 座り込む（E。届く範囲で深く沈む。敗者・傷）・`wipetears` 涙をぬぐう（E。敗者）・`shakehead` 首を振る（E。敗者）・`slamfist` 拳を打ちつける（E。敗者）・`towelhead` タオルを頭にかぶる（B E。小物）。

**飲み物（8）**: `drink` 水（既存。B S）・`sportsdrink` スポーツドリンク・`coffee` コーヒー（紙コップ。3 回すする）・`energydrink` エナジードリンク（缶）・`milk` 牛乳パック・`strawdrink` ストローのジュース・`jelly` ゼリー飲料・`spitwater` 口に含んで吐く（飛沫）。

**軽食（6）**: `banana`・`onigiri`・`sandwich`・`chocolate`・`apple`・`donut`（手の食べ物を口へ運び、かじるたびに手が小さく動き、頭が噛む）。

**嗜好・音楽（5）**: `cigarette` たばこ（くわえて吸い込み、手を離して煙を吐く）・`gum` ガムを噛む（頭が咀嚼の周期）・`headphones` ヘッドホンで首でリズム（頭に付く）・`earbud` イヤホン（耳に添える）・`hum` 口ずさむ（頭が左右・前後に揺れる）。

**休みの既存 11**: `deepbreath`（B E）・`wipe`（B E）・`phone`・`call`・`shoulderroll`・`neckroll`・`slapface`・`ropes`・`glare`・`shakearms`・`drink`。

## 10. 小物（`BreakProp.cs`、見た目だけ）

小物は 19 種（`PropKind`）: ボトル（水・スポーツドリンク）・紙コップ・缶・牛乳パック・ストロー付きパック・ゼリーのパウチ・バナナ・おにぎり・サンドイッチ・チョコバー・りんご・ドーナツ・たばこ・マウスピース・イヤホン・ヘッドホン・タオル・スマホ。ボクセルの物（セル 2 mm）で、見た目の色だけの専用の表（`BreakPalette`）。休みの小物と同じく**剛体ではない**: 持つ物はグローブの中心に付き、口へ運ぶ物は口の側の端が局所 +X。**ヘッドホンだけは頭に付く**（`PropAttach.Head`。頭の箱の寸法から作り、頭の向き `Fighter.HeadOrientation` に従う）。

**煙・飛沫**（`BreakVapor` → `BreakVaporLook`）: 動作が口から出す窓（経路の時刻 0..1）を持ち、その間の経過時間から吐く息の雲（`TiredLook`）と同じ作りの玉を口の位置へ置く。たばこの煙は灰色で昇って広がり薄れる（出し終えてから 1.6 秒漂う）、口に含んで吐く水は前・斜め下へ飛ぶ水色の粒。乱数は使わず、物理・記録に響かない。

## 11. キャラクターごとの好み（`education.style.BreakLikes`）

プロフィール（国・性格・趣味・家族・エピソード）に合わせた倍率（> 1 が 5〜10 個、< 1 は選びにくい）。たばこは葉巻の香りが趣味のヨアンドリだけ。

| キャラ | 好み（倍率）| 根拠（プロフィール） |
|---|---|---|
| akira 比嘉 明 | bow 3・handshake 2.5・hum 2.5・salute 2・applause 2・onigiri 2・deepbreath 1.5 / raisearms 0.3・roar 0.2・fistpump 0.4・dance 0.3・blowkiss 0 | 無口で礼儀正しく勝っても大げさに喜ばない、三線、沖縄そば作り |
| albert アルバート | coffee 3（紅茶）・handshake 3・applause 2.5・bow 2・salute 1.5・mirror 1.5・shadowbox 1.2 / roar 0.3・blowkiss 0.2・dance 0.4 | 紳士的で皮肉屋、紅茶の茶葉の食べ比べ、試合後は相手の健闘を認める |
| daichi 玉城 大地 | hum 3・bow 2.5・stretcharms 2・onigiri 2・banana 1.5・prayer 1.5・neckroll 1.5・handshake 1.5 / roar 0.3・selfie 0.3 | 島の民謡、農作業（体を伸ばす）、穏やかで頑固 |
| dmitri ドミトリー | bow 3.5・towelhead 3・earbud 2.5・deepbreath 2.5・headphones 2・handshake 2・sandwich 1.5・applause 1.5 / dance 0.3・blowkiss 0.2・wavetwo 0.4 | 試合後に深く頭を下げる律儀さ、サウナ（タオル・深呼吸）、ロシア民謡を聴く |
| edgar エドガー | coffee 3.5（紅茶）・handshake 3・bow 3・salute 2.5・applause 2・fixgloves 2・headinhands 1.5 / roar 0.2・blowkiss 0.2 | 試合の前後に盾を合わせて挨拶、紅茶と戦術書、木工で盾の枠を直す、負けた試合を見返す |
| gaston ガストン | bow 3・blowkiss 3・mirror 3・salute 2・selfie 2・hum 2・wave 2・pointcrowd 1.5・applause 1.5 / energydrink 0.3・jelly 0.4 | 礼儀正しく気取った態度、冗談好き、チェンバロの演奏 |
| jin 鬼塚 迅 | bow 3・coffee 2（茶）・deepbreath 2（居合の呼吸）・shakehead 2・slamfist 2・fixgloves 1.5（刀の手入れ）・onigiri 1.5・handshake 1.5 / roar 0.3・blowkiss 0・wavetwo 0.4 | 物静かで無駄口を叩かない、負けず嫌い、和菓子めぐり、刀の手入れ |
| joao ジョアン | dance 3.5・slamfist 3・hum 2・headphones 2.5・bounce 2.5・shakehead 2.5・chestbeat 2.5・roar 2.5・energydrink 2・gum 2 / handshake 0.4・bow 0.5・prayer 0.3 | サンバ、サッカー、短気でふてくされる、口が悪い |
| konrad コンラート | fixgloves 3.5・mouthguard 3・stretcharms 2・forwardbend 2・hum 2（ピアノ）・bow 2・handshake 2・neckcrack 1.5・coffee 1.5 / roar 0.3・blowkiss 0.2 | 真面目で几帳面、試合前の準備に誰より時間をかける |
| marcus マーカス | applause 3・handshake 3・sandwich 2.5（バーベキュー）・headphones 2.5（ジャズ）・glare 2・pointup 2・sportsdrink 2・bow 1.5・raisearms 1.5・hypecrowd 1.5 / blowkiss 0.3 | 穏やかで後輩の面倒見がいい、リングでだけ冷たい目、王者 |
| narong ナロン | prayer 4・bow 3・applause 3・handshake 3・banana 2.5（屋台）・sandwich 1.5・stretcharms 1.5 / roar 0.3・blowkiss 0.2 | リングに上がる前の祈り、試合後に相手の健闘を称える、屋台の食べ歩き |
| ramon ラモン | wipetears 3.5・crossself 3・blowkiss 2.5・hum 2.5（ギター）・chestbeat 2.5・dance 2・roar 2・bounce 2（サッカー）・headinhands 2・sandwich 1.5 | 陽気で情に厚い、試合後に涙を流す、母が毎回見に来る |
| sean ショーン | hum 4・dance 2.5・chestbeat 2.5・roar 2.5・energydrink 2（缶）・wavetwo 2・shadowbox 2・pointcrowd 2・handshake 1.5・gum 1.5 | 陽気で喧嘩っ早い、歌が好きで試合後に仲間と歌う、ギネスの飲み比べ |
| taeyun テユン | bow 3・dance 3・bounce 3・handshake 2.5・onigiri 2.5（屋台のトッポッキ）・hum 2・selfie 2・headphones 2・stretcharms 2・wave 1.5 | 礼儀正しく人懐っこい、ダンス・ギター・サッカー、跳ねて蹴る |
| tsuyoshi 新垣 剛 | hum 3（三線）・onigiri 2.5（母の料理）・wave 2.5・handshake 2.5・applause 2・bow 2・selfie 1.5・stretcharms 1.5 / roar 0.3 | 素直で人懐っこい、先輩・後輩に好かれる、三線と料理 |
| vikram ヴィクラム | salute 3・bow 2.5・handshake 2・prayer 2・pointup 2・hum 2（叙事詩の朗読）・glare 2・raisearms 1.5・chestbeat 1.5 / blowkiss 0.2・selfie 0.3 | 誇り高く寡黙、一度決めたことをやり通す、古い叙事詩の朗読 |
| yoandry ヨアンドリ | handshake 4・cigarette 4・dance 3・headphones 3（古いレコード）・hum 2.5・blowkiss 2・wave 2・wavetwo 2・applause 2・hypecrowd 2 / roar 0.5 | 試合後は必ず握手を求める、ダンス、葉巻の香りを嗅ぐこと、古いレコードを聴く |

## 12. 始めの T 字の姿勢をなくす（助走 `SettleSeconds`）

原因: 動画のタイトル・選手紹介の間、背景に描くのは**生成直後の世界**（`Fighter.Spawn` の後、まだ 1 歩も進めていない状態）で、腕は生成時の真横に広げた形（T 字）のまま。体は目標（構え）をバネで追うので、数歩進めれば構えへ追いつくが、その歩を進める前の絵を映していた。

直し方（姿勢は書かない）: `BoxingCorners.SettleSeconds`（既定 0）があれば、`BoxingGame` のコンストラクタが世界を作った直後に、その秒数だけ**両者を「一呼吸の構え」（`HoldCommand`: その場に立ち相手を向き、ガードの位置の目標）で回す**（`Settle`）。構えの目標を最初の歩から出し、体がバネで追いつくまで待つだけ。試合の時計・打撃・ボットの読み・乱数は進めない。コンストラクタの中で行うので、同じ種・同じ角から作り直した試合（リプレイ・影の試合・下見）も同じ助走をして一致する。`ReplayMatchSettings.SettleSeconds` で記録にも残る。TV の撮影・下見は `TvShowRules.Stage` が 1 秒を入れる（腕の真横への広がりが 1.0 超 → 0.8 未満: `EmoteTests.SettlingRemovesTheStraightOutTPoseBeforeTheFirstFrame`）。

## 13. 終わりのカット割り（TV モード）

決着の後（勝敗の画面 `TvShowGraphics.Result` の前）の映像は `TvDirector.PlanEnd`（決着の歩からの映像の秒。`EndSeconds` 7 秒 ÷ 速さ 0.75 = 約 9.3 秒）:

1. **勝者の手持ちのアップ**（`Handheld`、約 4 割 = 3.7 秒）
2. **敗者**（約 3 割 = 2.8 秒。倒れていれば `Ringside`（床の高さ）、立っていれば `Handheld`）
3. **リング全体の引き**（`Wide`、残り約 2.8 秒）

引き分けは両者を同じ長さ（3 割ずつ）で映してから引き。割合は決まった値で乱数なし。この間、立っている選手は終わりのエモートをする（勝者は喜び、敗者は悔しがる・うなだれる・相手を称える。倒れている敗者は倒れたまま）。K.O. のリプレイ（`決着のリプレイ`）は従来どおりこの映像の後に流れ、その後に勝敗の画面。試合の始めも `TvDirector.PlanIntro`（引き → 赤 → 青 → 引き）でカットを割る。

出来事（実況・解説の資料）: `intro_scene`（試合の始めの予定）・`end_scene`（決着の後の予定。勝者・敗者・倒れたまま・カットの並び）・各カットの `cut`（休みと同じ書き方）・動作の `start_action` / `break_action` / `end_action`（「X が敗れた後にうなだれている」のように、場面と勝ち負けが分かる言い方）。

## 14. 軸から外れている所（CLAUDE.md §8 にも書いた）

- 小物は物理の剛体ではなく描くだけ（§6 と同じ）。ヘッドホンは頭の箱に合わせた寸法で、動作の始めに現れ終わりに消える（付ける・外す動きは手の経路だけ）。口に入れる小物（マウスピース・たばこ・ガム）は口へ付けず、手に持って口の前へ運ぶだけ（たばこは手を離した後に口へ残らない）。
- 煙・飛沫は見た目だけの玉（物理・記録に響かない）。煙は決まった速さで昇るだけで風・熱に流されない。
- 動作の長さ・経路の数値・重み・好みの倍率は、見て決めた手の値（物理から出した値ではない）。勝ち負けの気分は勝ち数の差の符号（−1 / 0 / +1）で、K.O. か判定か・どれだけ圧勝かは使っていない。
- 跳ぶ・足踏みは入れていない（`FighterCommand` に跳ぶ・足踏みの指令が無い）。軽く弾む = 頭の上下の周期。座り込み・前屈・膝に手をつくは、頭の目標を下・前へ出して腰から折れた結果で、体を落とせる範囲（`GroundCrouch`）までしか沈まない（床に座らない）。
- 握手は歩み寄る力（`Stride`）と止まる距離（`Standoff` 0.4 m）の経験値で、相手が同時に動いていると手が合わないことがある。相手の手の位置を見て合わせてはいない。
- 助走（`SettleSeconds`）は動画（TV・撮影・下見）だけに入れた。ゲーム（デスクトップ・VR）の試合の始めの体は、最初の歩から構えへ追いつく（ゴングの前に着く）ままで、助走は入れていない。
