# 作業指示（2026-11-16）: 空手の手形で急所を壊せているかを確かめる

日本語で書く。コミット・プッシュはしない。作業は今のリポジトリ（ブランチ `codex/karate-vitals`）の中だけ。

最初に読む: `CLAUDE.md`（§1、特に §1.3・§1.4、§4、§8 の「空手」）、`docs/KARATE.md`、`docs/arena/gen12_karatelab_shapes.md`、`games/MiniRing.Arena/KarateLab.cs`、`games/MiniRing/Voxels/`（`BoxerAnatomy`・`BoxerBody`・`VoxelContact`・手形 `BareHandState`/`KarateHandChoice` まわり・目と視界 `VisionState`）、`BoxingGame.cs` の `ApplyHit`・`Fighter.TwistHead`（意識）、`Fighter.Stamina.cs`（臓器の打撲）。

## ユーザーの問い
空手家は指の形（手形）で急所を破壊できているか。

## 今の物差しの足りない所
`karatelab --only shapes` は「相手の欠けたセルの数」と「自分の指の骨の欠け」しか数えていない。急所が**壊れたか**（その急所の中身が削れたか・働きが落ちたか）は測っていない。

## やること
1. `KarateLab.Shapes` の表に、狙った急所ごとの「壊れ」を足す（打 1 回あたりと、試合の終わりの値）:
   - 目: 目のセルの欠け、見えの度合い（`VisionState`）の低下。
   - こめかみ・あご: 意識の低下（打 1 回の最大の落ち・終わりの値）、頭のねじれの角速度、脳のセルの欠け。
   - みぞおち・肝臓: 臓器のセル（どの臓器か）の欠け・臓器の打撲の量、スタミナの蓄え・供給の低下。
   - 狙った急所の周り（その急所の箱・半径）で欠けたセルと、それ以外で欠けたセルを分ける（当たった所が急所か）。
   - 比べの基準として、同じ急所を**握った拳**（素手の正拳）で打った行を必ず入れる。手形の種類は今の `KarateLab` が回しているもの全部。
2. 実行: `dotnet run --project games/MiniRing.Arena -c Release -- karatelab --only shapes --bouts 3 --seconds 30 --log docs/arena/karate_vitals.md`（時間が長すぎれば本数を減らし、その旨を書く）。
3. 読み取り: 形ごとに、拳と比べて急所の壊れが大きいか。物理として期待されること（圧力のモデル §1.4: 細い指先・一本拳は狭い面に力を集めるので、柔らかい所（目・腹の臓器の上）では深く入る。骨の上（こめかみ・あご）では指の骨が負けて折れうる）と合っているか。
4. 期待と違う所があれば原因を調べる（当たっていない・狙いが外れている・接触の面の大きさが手形を反映していない・臓器まで力が伝わらない、など）。**明らかなバグ**（例: 手形の指のセルが接触に使われていない）なら直して再発防止のテストを書く。物理の値の調整や近道で数字を合わせることはしない（直し方の案を報告に書く）。物理を変えたら `Fighter.PhysicsVersion` を上げ、チェックサムの期待値を理由つきで更新する。
5. 見た目: 代表的な 3 つ（貫手 → 目、一本拳 → こめかみかみぞおち、拳 → 同じ急所）を `video` かドリルの撮影で短く撮り（スロー可）、当たった瞬間の前後のコマを PNG に書き出す（`media_tmp/` に。git に入れない）。動画の作り方は `CLAUDE.md` §5。
6. 全テスト `dotnet test tests/MiniRing.Tests -c Release --filter "FullyQualifiedName!~Probe"`（コードを変えた場合）。

## 報告
`docs/plans/done/CODEX_REPORT_KARATE_VITALS_2026-11-16.md`（先頭に「完了」か「未完了」）: 表（形 × 急所 × 壊れ、拳との比）、期待どおりの所・違う所とその原因、直したこと（あれば）、PNG・動画の場所、確かめていない所。
