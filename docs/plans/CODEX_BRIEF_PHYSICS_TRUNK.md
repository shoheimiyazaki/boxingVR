# 指示書: 胴のひねりと足からの運動量の鎖（物理の統合 段 3 の 1b）

`docs/plans/CODEX_BRIEF_PHYSICS_FIST.md` と `CODEX_REPORT_PHYSICS_FIST.md`（前の調査。測定の道具 `FistScaleProbe`・`punchlab` の拡張は枝に入っている。物理の変更は外した）を先に読む。作業ツリー・軸（CLAUDE.md §1）・コミットしない・テストを緩めない、は同じ。

## 前の調査で分かったこと（種 1、右ストレート、当たる直前）
- 拳 4.4 m/s のうち、**肩自身の前進は 0.3〜0.4 m/s だけ**、肩に対する拳（腕の伸び）が約 3.9 m/s。
- 胸の鉛直軸まわりの角速度は **約 2 rad/s**。本物のストレートでは骨盤が 8〜12 rad/s・肩が 10〜15 rad/s（600〜900°/s）で回り、胸の回転と踏み込みで**肩が 3〜4 m/s** 進む。腕の伸びは 5〜6 m/s。合わせて約 9 m/s。
- 筋力（MaxHandForce）・腕の筋の最高速・体から拳への移送を変えても 9 m/s には届かない。**足りないのは「足 → 腰 → 胸 → 肩」の鎖が肩に渡す速さ**と見ている。

## 目標（`punchlab --paths standard --defenders still`、届く間合い、種 1〜3 と使っていない種 4〜6）
| 量 | 今 | 目標 |
|---|---|---|
| 当たる直前の胸の鉛直軸まわりの角速度 | 約 2 rad/s | 8〜12 rad/s |
| 肩自身の前方の速さ | 0.3〜0.4 m/s | 2〜3.5 m/s |
| 当たる拳の速さ | 約 4.4 m/s | 7〜10 m/s |
| 乗る質量 | 約 6.3 kg | 2〜5 kg（速さが上がれば運動量が保たれ質量は自然に下がるはず。質量の式は変えない）|
| エネルギー | 約 64 J | 60〜200 J |

## 見る所（体の力学として直す。倍率・種類の分岐・目標の書き換えはしない）
1. 胴のひねりは `Fighter.cs` の `ApplyUprightImpulse`（`UprightKp` 213・`UprightKd` 23.8・`MaxUprightAccel` 532 rad/s²）と、ひねりの目標 `PunchMechanics.TorsoTwist`（両手の位置の差から出る、上限 `MaxTorsoTwistRadians` 35°）、腰の分担 `HipTwistShare` 0.45。ひねりの速さを決めるのは何か（バネの固さ・最大の角加速度・目標の範囲・ボットが目標を出す時刻）を測って分ける。
2. 本物の値で比べる: 体幹の回旋のトルク（回旋筋 ピーク約 150〜250 N·m 級。体の断面から出す: `BodyBuild`・§1.7）・骨盤と胸の回旋の可動域（骨盤 40〜50°、胸が骨盤に対して追加で 30〜40°、合計 60〜90°）。今の `MaxTorsoTwistRadians` 35° が本物より狭ければ体の値として直す。
3. 姿勢を保つ PD（立つ・倒れない）とひねりを分ける必要があるか。ひねりの駆動を強くすると立つ・倒れる・ステップのテスト（`StepTests`・`FixedSimulationTests`・倒れ方のテスト）が崩れないか。崩れるなら、ひねりだけに別のバネと**筋力の上限（N·m、体から出す）**を持たせる。
4. 足からの鎖: 後ろ足の蹴りと腰の回転が胸・肩へ渡る運動量（`Fighter.Step.cs`・`TransferBodyMotion`・脚のバネ）。腰が先に回り、胸が遅れて追い、肩・拳が続く順（運動量の鎖）が物理から出ているか。体から拳へ渡るエネルギーの出どころは体の運動エネルギー＋バネだけ（§6「エネルギーの出どころ」）。

## 確かめ方・完了の条件
- 上の表の前後。種 1〜3 と 4〜6。ジャブ・フック（`attacklab`）・ステップ（`StepTests`）。
- 立ち姿・ステップ・ダウン・起き上がりが壊れていない（速いテスト、`dotnet test tests/MiniRing.Tests --filter "TestCategory!=slow&TestCategory!=visual&TestCategory!=Probe"`、ローカル）。**新しく落ちたテストを列挙**（`docs/plans/physics_consolidate_failing_baseline.txt` が master の 29 件）。
- 動画 40 秒（`video --red champion --blue swarmer`）と `py -3 tools/sheet.py` で、胴がねじれて打っているか・体が不自然に回っていないかを見て報告に書く。
- 変えたら `FighterTuning` にコメント（意味・根拠・測った値）、`PhysicsVersion` 67 → 68（`docs/VERSIONS.md` に 1 行）、`docs/BOXING.md`・`docs/DEBT.md`（「拳に乗る体の質量」(3)）を直す。
- 1 つずつ変えて比べる。片方の種に合わせ込まない。
- 全テストは統合役が tower32 で回す。**ローカルの Codex はリモート転送をしない**（承認されない）。

## 報告（`docs/plans/CODEX_REPORT_PHYSICS_TRUNK.md`）
測ったこと・変えたこと・前後の数字・新しく落ちたテスト・見送ったこと・残った近道。コミットはしない。
