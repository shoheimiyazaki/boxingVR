# 動画・リプレイの保管（開発の世代ごと）

撮った動画・リプレイ（`.mrrep`）・聞き比べの音・確かめの静止画は、`media/<世代>/` に開発の世代ごとに保管する。`media/` は大きいので git に入れない（`.gitignore`）。この表が索引で、git に残る。

## 決まり

- **世代** = master の節目（体の物理の版 `Fighter.PhysicsVersion` が変わった所・大きな機能のマージ）。ファイルは「その作業が master に入った節目」の世代に置く（撮った時刻の次の節目。ブランチ・ワークツリーで撮ったものは、そのブランチが master に入った節目）。縮めた版（`_small`）・遅くした版（`_075x`）・実況の声と台本は元の動画と同じ世代。
- 中は `video/`（元の `docs/video/`・`videos/`・`.visualcheck/` の中の相対パスをそのまま）と `replays/`。
- 新しく撮る動画は今までどおり `docs/video/`・`videos/` へ、記録は `replays/` へ出る。世代を区切るとき（`PhysicsVersion` を上げてマージした・大きな機能が master に入った）に、ワークツリーの分も含めて `media/<次の世代>/` へ移し、この表に世代を足す。
- リプレイは記録した版の体でしか再生できない（`docs/REPLAY.md`）。古い世代の `.mrrep` は、その世代のコミットを checkout して再生する。

## 世代の一覧

ワークツリー `.claude/worktrees/*` と本体の `docs/video/`・`videos/`・`replays/`・`media_tmp/` から、2026-11-17 22:24 に gen11〜gen15 を回収した（撮った時刻の次の節目へ。テレビ中継などのフォルダは丸ごと同じ世代）。ワークツリーの `.visualcheck/`（テストのたびに作り直す静止画）と `media_tmp/binrun/`（ビルドの写し）は移していない。まだ master に入っていない作業の分（ムエタイの肘 `agent-a9d77545…`・蹴りの練習 `agent-a9402fb…`・投げと関節技 `throwing-grappling-mechanics-de662d`）と、回収の直前 30 分に書かれたファイルは元の場所に残した。マージしたときに次の世代へ移す。

| 世代 | master に入った節目（〜） | 中身 | ファイル | 大きさ |
|---|---|---|---:|---:|
| `gen01_1107_vr-poc` | 2026-11-07 23:59 | VR の試作（Aigis・OpenXR）・部位ダメージ・ラグドールの手触り・記録と再生 | 4 | 2 MiB |
| `gen02_1110_bots` | 2026-11-10 11:21 | ボクシングのボット・技・防御ドリル・ボット同士の対戦場 | 2 | 39 MiB |
| `gen03_1110_voxel-body` | 2026-11-10 21:29 | ボクセルの体（削れ・断裂・死）・破片と打撃音・照明・回避の磨き | 113 | 879 MiB |
| `gen04_1111_predict-footwork` | 2026-11-11 01:11 | 予測の回避・かかとつま先の足運び・熱い破片と傷 | 33 | 293 MiB |
| `gen05_1111_one-punch-ko` | 2026-11-11 10:26 | 非線形のダメージと一撃ダウン・意識の 2 段・硬い首・トランクス（PhysicsVersion 2–3） | 12 | 171 MiB |
| `gen06_1111_living-room` | 2026-11-11 16:46 | 実物大の部屋の卓上リング・連続のダウン・.vox の編集（PhysicsVersion 6） | 15 | 151 MiB |
| `gen07_1112_weapons-gloves` | 2026-11-12 08:49 | 顔の窓・起き上がりの試み・0.75 倍速 / ボクセルの武器・グローブ・versus（PhysicsVersion 7–8） | 70 | 1,014 MiB |
| `gen08_1112_stamina-tv` | 2026-11-12 19:27 | スタミナと臓器・テレビ中継・狙い / 武器リング・打って離れる・武器を選ばない守り（PhysicsVersion 9–13） | 120 | 769 MiB |
| `gen09_1113_pressure-fracture` | 2026-11-13 08:34 | 皮膚の下の圧力・擦り減り・材質の接触音・審判のダウン・骨折 / guardspar（PhysicsVersion 16） | 21 | 772 MiB |
| `gen10_1113_range-fatigue` | 2026-11-13 13:02 | claude/a-3d0203 のマージ（疲れが拳の速さに出る・間合いの層・学ぶ守り）・折れた骨をつなぐ肉（PhysicsVersion 17–18） | 17 | 439 MiB |
| `gen11_1114_intent-karate` | 2026-11-14 17:27 | 意図の操作（キーボード）・1 打 1 音・肉でつながる骨折と切断 / 空手（蹴り・指・パリー）・目と見え・顔・体格・剣の接触の掃き直し（PhysicsVersion 19–27） | 102 | 815 MiB |
| `gen12_1115_stamina-swords` | 2026-11-15 20:56 | デスクトップのゲーム選択・対戦・再生 / スタミナの予算・描画と物理の分離 / 刀とロングソードの型・頭の作り直し | 58 | 532 MiB |
| `gen13_1116_characters` | 2026-11-16 17:09 | キャラクターファイル・首と脊椎・指の手・武器の教科書・レフェリーなし / 実況の PDCA・テレビ中継の札（tvcard 1–4） | 3074 | 11,774 MiB |
| `gen14_1117_tvprogram-cmaes` | 2026-11-17 10:52 | 大会の番組・弱った相手の詰め・解説・CMA-ES（cmatune）・臨界パワーのスタミナ（PhysicsVersion 38）/ 棒術・トンファー・テコンドー・片手剣と盾・生配信の試し | 446 | 2,915 MiB |
| `gen15_1117_legs-woundcam` | 2026-11-17 22:09 | 脚を失っても戦う（PhysicsVersion 39）・棒の滑る握り・VR の武器と動きの記録・回し蹴り・傷のカメラ（wound-cam）・WVC 再戦の中継 | 4111 | 17,392 MiB |

## gen01_1107_vr-poc

VR の試作（Aigis・OpenXR）・部位ダメージ・ラグドールの手触り・記録と再生

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `replays/miniring_desktop_20261107_204939.mrrep` | replays | 11-07 20:49 |
| `replays/miniring_vr_20261107_211156.mrrep` | replays | 11-07 21:11 |
| `replays/miniring_vr_20261107_214820.mrrep` | replays | 11-07 21:52 |
| `replays/miniring_vr_20261107_223756.mrrep` | replays | 11-07 22:43 |

## gen02_1110_bots

ボクシングのボット・技・防御ドリル・ボット同士の対戦場

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/miniring_champion_vs_champion-v1.mp4` | videos | 11-10 10:36 |
| `video/miniring_champion_vs_jabber.mp4` | videos | 11-10 10:37 |

## gen03_1110_voxel-body

ボクセルの体（削れ・断裂・死）・破片と打撃音・照明・回避の磨き

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/arena_look_sheet.jpg` | ワークツリー voxel-damage の videos | 11-10 18:51 |
| `video/break_sounds/1_glass/`（7 ファイル） | ワークツリー voxel-damage の videos | 11-10 13:52 |
| `video/break_sounds/1_glass_all.mp3` | ワークツリー voxel-damage の videos | 11-10 14:09 |
| `video/break_sounds/2_crumble/`（7 ファイル） | ワークツリー voxel-damage の videos | 11-10 13:52 |
| `video/break_sounds/2_crumble_all.mp3` | ワークツリー voxel-damage の videos | 11-10 14:09 |
| `video/break_sounds/3_crunch/`（7 ファイル） | ワークツリー voxel-damage の videos | 11-10 13:52 |
| `video/break_sounds/3_crunch_all.mp3` | ワークツリー voxel-damage の videos | 11-10 14:09 |
| `video/break_sounds/4_circuit/`（7 ファイル） | ワークツリー voxel-damage の videos | 11-10 13:52 |
| `video/break_sounds/4_circuit_all.mp3` | ワークツリー voxel-damage の videos | 11-10 14:09 |
| `video/break_sounds/5_metal/`（7 ファイル） | ワークツリー voxel-damage の videos | 11-10 13:52 |
| `video/break_sounds/5_metal_all.mp3` | ワークツリー voxel-damage の videos | 11-10 14:09 |
| `video/break_sounds_v2/1_glass/`（7 ファイル） | ワークツリー voxel-damage の videos | 11-10 15:00 |
| `video/break_sounds_v2/2_crumble/`（7 ファイル） | ワークツリー voxel-damage の videos | 11-10 15:00 |
| `video/break_sounds_v2/3_crunch/`（7 ファイル） | ワークツリー voxel-damage の videos | 11-10 15:00 |
| `video/break_sounds_v2/4_circuit/`（7 ファイル） | ワークツリー voxel-damage の videos | 11-10 15:00 |
| `video/break_sounds_v2/5_metal/`（7 ファイル） | ワークツリー voxel-damage の videos | 11-10 15:00 |
| `video/colors_cutaway_closeup.jpg` | ワークツリー voxel-damage の videos | 11-10 14:14 |
| `video/glass_metal_organs_all.mp3` | ワークツリー voxel-damage の videos | 11-10 15:00 |
| `video/impact_arena.png` | ワークツリー voxel-damage の videos | 11-10 18:52 |
| `video/impact_closeup.jpg` | ワークツリー voxel-damage の videos | 11-10 17:21 |
| `video/impact_zoom2.png` | ワークツリー voxel-damage の videos | 11-10 19:19 |
| `video/look_test.mp4` | ワークツリー voxel-damage の videos | 11-10 18:40 |
| `video/look_test2.mp4` | ワークツリー voxel-damage の videos | 11-10 18:41 |
| `video/miniring_arena_look.mp4` | ワークツリー voxel-damage の videos | 11-10 18:51 |
| `video/miniring_arena_look2.mp4` | ワークツリー voxel-damage の videos | 11-10 19:18 |
| `video/miniring_arena_look2_small.mp4` | ワークツリー voxel-damage の videos | 11-10 19:19 |
| `video/miniring_arena_look_small.mp4` | ワークツリー voxel-damage の videos | 11-10 18:51 |
| `video/miniring_drill_right_straight_after.mp4` | videos | 11-10 17:44 |
| `video/miniring_drill_right_straight_before.mp4` | videos | 11-10 17:44 |
| `video/miniring_swarmer_vs_counter.mp4` | videos | 11-10 15:34 |
| `video/miniring_swarmer_vs_slugger.mp4` | videos | 11-10 15:33 |
| `video/miniring_swarmer_vs_slugger_peekaboo.mp4` | videos | 11-10 17:47 |
| `video/miniring_voxel_glass.mp4` | ワークツリー voxel-damage の videos | 11-10 14:12 |
| `video/miniring_voxel_glass140.mp4` | ワークツリー voxel-damage の videos | 11-10 14:17 |
| `video/miniring_voxel_glass140_small.mp4` | ワークツリー voxel-damage の videos | 11-10 14:18 |
| `video/miniring_voxel_glass_v2.mp4` | ワークツリー voxel-damage の videos | 11-10 15:00 |
| `video/miniring_voxel_glass_v2_small.mp4` | ワークツリー voxel-damage の videos | 11-10 15:00 |
| `video/miniring_voxel_glow.mp4` | ワークツリー voxel-damage の videos | 11-10 17:20 |
| `video/miniring_voxel_glow_small.mp4` | ワークツリー voxel-damage の videos | 11-10 17:21 |
| `video/miniring_voxel_jet.mp4` | ワークツリー voxel-damage の videos | 11-10 16:54 |
| `video/miniring_voxel_jet_small.mp4` | ワークツリー voxel-damage の videos | 11-10 16:54 |
| `video/miniring_voxel_only.mp4` | ワークツリー voxel-damage の videos | 11-10 15:39 |
| `video/miniring_voxel_only_small.mp4` | ワークツリー voxel-damage の videos | 11-10 15:40 |
| `video/miniring_voxel_shatter.mp4` | ワークツリー voxel-damage の videos | 11-10 13:13 |
| `video/miniring_voxel_shatter_small.mp4` | ワークツリー voxel-damage の videos | 11-10 13:14 |
| `video/miniring_voxel_swing.mp4` | ワークツリー voxel-damage の videos | 11-10 15:59 |
| `video/miniring_voxel_swing_small.mp4` | ワークツリー voxel-damage の videos | 11-10 16:00 |
| `video/sounds/`（6 ファイル） | ワークツリー voxel-damage の videos | 11-10 13:10 |

## gen04_1111_predict-footwork

予測の回避・かかとつま先の足運び・熱い破片と傷

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/chunk_zoom_f03.png` | ワークツリー voxel-damage の videos | 11-11 00:12 |
| `video/chunk_zoom_f08.png` | ワークツリー voxel-damage の videos | 11-11 00:12 |
| `video/chunk_zoom_f16.png` | ワークツリー voxel-damage の videos | 11-11 00:07 |
| `video/face_splash_zoom.png` | ワークツリー voxel-damage の videos | 11-10 23:46 |
| `video/footwork_swarmer_vs_champion.mp4` | docs/video | 11-11 00:20 |
| `video/footwork_weave_drill.mp4` | docs/video | 11-11 00:20 |
| `video/heat_f03.png` | ワークツリー voxel-damage の videos | 11-11 00:56 |
| `video/heat_f08.png` | ワークツリー voxel-damage の videos | 11-11 00:56 |
| `video/heat_f16.png` | ワークツリー voxel-damage の videos | 11-11 00:56 |
| `video/heat_ramp.png` | ワークツリー voxel-damage の videos | 11-11 00:56 |
| `video/joints_swarmer_vs_champion.mp4` | docs/video | 11-11 00:49 |
| `video/miniring_chunks.mp4` | ワークツリー voxel-damage の videos | 11-11 00:13 |
| `video/miniring_chunks_small.mp4` | ワークツリー voxel-damage の videos | 11-11 00:13 |
| `video/miniring_heat.mp4` | ワークツリー voxel-damage の videos | 11-11 00:57 |
| `video/miniring_heat_small.mp4` | ワークツリー voxel-damage の videos | 11-11 00:57 |
| `video/miniring_splash.mp4` | ワークツリー voxel-damage の videos | 11-10 23:48 |
| `video/miniring_splash_small.mp4` | ワークツリー voxel-damage の videos | 11-10 23:48 |
| `video/miniring_trails.mp4` | ワークツリー voxel-damage の videos | 11-11 00:39 |
| `video/miniring_trails_small.mp4` | ワークツリー voxel-damage の videos | 11-11 00:39 |
| `video/miniring_wounds.mp4` | ワークツリー voxel-damage の videos | 11-11 01:01 |
| `video/miniring_wounds_small.mp4` | ワークツリー voxel-damage の videos | 11-11 01:01 |
| `video/predict_outside_slip.mp4` | docs/video | 11-10 21:45 |
| `video/predict_swarmer_vs_slugger.mp4` | docs/video | 11-10 21:44 |
| `video/trail_zoom_f03.png` | ワークツリー voxel-damage の videos | 11-11 00:37 |
| `video/trail_zoom_f08.png` | ワークツリー voxel-damage の videos | 11-11 00:38 |
| `video/wound_f03.png` | ワークツリー voxel-damage の videos | 11-11 01:00 |
| `video/wound_f08.png` | ワークツリー voxel-damage の videos | 11-11 01:00 |
| `video/wound_f16.png` | ワークツリー voxel-damage の videos | 11-11 01:00 |
| `video/wound_front_ramp.png` | ワークツリー voxel-damage の videos | 11-11 01:00 |
| `video/wound_ramp.png` | ワークツリー voxel-damage の videos | 11-11 01:00 |
| `video/woundfront_f03.png` | ワークツリー voxel-damage の videos | 11-11 01:00 |
| `video/woundfront_f08.png` | ワークツリー voxel-damage の videos | 11-11 01:00 |
| `video/woundfront_f16.png` | ワークツリー voxel-damage の videos | 11-11 01:00 |

## gen05_1111_one-punch-ko

非線形のダメージと一撃ダウン・意識の 2 段・硬い首・トランクス（PhysicsVersion 2–3）

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/damage_one_punch_down.mp4` | docs/video | 11-11 01:45 |
| `video/damage_one_punch_down_small.mp4` | docs/video | 11-11 01:51 |
| `video/faint_champion_vs_swarmer.mp4` | docs/video | 11-11 09:22 |
| `video/faint_champion_vs_swarmer_small.mp4` | docs/video | 11-11 09:22 |
| `video/miniring_wounds2.mp4` | ワークツリー voxel-damage の videos | 11-11 07:51 |
| `video/miniring_wounds2_small.mp4` | ワークツリー voxel-damage の videos | 11-11 07:51 |
| `video/neck_champion_vs_swarmer.mp4` | docs/video | 11-11 08:10 |
| `video/neck_champion_vs_swarmer_small.mp4` | docs/video | 11-11 08:10 |
| `video/sway.mp4` | .visualcheck | 11-11 08:05 |
| `video/weave_drill.mp4` | docs/video | 11-11 09:19 |
| `video/weave_drill_small.mp4` | docs/video | 11-11 09:21 |
| `video/wound_front_ramp2.png` | ワークツリー voxel-damage の videos | 11-11 07:50 |

## gen06_1111_living-room

実物大の部屋の卓上リング・連続のダウン・.vox の編集（PhysicsVersion 6）

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/bat_sheet.png` | ワークツリー voxel-damage の videos | 11-11 15:10 |
| `video/bat_swing.mp4` | ワークツリー voxel-damage の videos | 11-11 15:10 |
| `video/club_sheet.png` | ワークツリー voxel-damage の videos | 11-11 12:19 |
| `video/club_swings.mp4` | ワークツリー voxel-damage の videos | 11-11 12:18 |
| `video/down_continuous_champion_vs_swarmer.mp4` | docs/video | 11-11 14:46 |
| `video/down_continuous_champion_vs_swarmer_small.mp4` | docs/video | 11-11 16:49 |
| `video/new_punch_champion_vs_swarmer.mp4` | docs/video | 11-11 12:09 |
| `video/new_punch_champion_vs_swarmer_small.mp4` | docs/video | 11-11 12:09 |
| `video/room_champion_vs_swarmer.mp4` | docs/video | 11-11 16:45 |
| `video/room_champion_vs_swarmer_small.mp4` | docs/video | 11-11 16:49 |
| `video/weaponlab_sheet.png` | ワークツリー voxel-damage の videos | 11-11 11:20 |
| `video/weaponlab_sheet2.png` | ワークツリー voxel-damage の videos | 11-11 11:21 |
| `video/weaponlab_sheet3.png` | ワークツリー voxel-damage の videos | 11-11 11:30 |
| `video/weave_counter_drill.mp4` | docs/video | 11-11 10:57 |
| `video/weave_counter_drill_small.mp4` | docs/video | 11-11 10:57 |

## gen07_1112_weapons-gloves

顔の窓・起き上がりの試み・0.75 倍速 / ボクセルの武器・グローブ・versus（PhysicsVersion 7–8）

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/basics_kendo_sheet.png` | ワークツリー voxel-damage の videos | 11-11 19:31 |
| `video/basics_long_sheet.png` | ワークツリー voxel-damage の videos | 11-11 19:31 |
| `video/clips/clip1_slugger_vs_swarmer.mp4` | docs/video | 11-11 22:34 |
| `video/clips/clip1_slugger_vs_swarmer_075x.mp4` | docs/video | 11-11 22:35 |
| `video/clips/clip2_champion_vs_counter.mp4` | docs/video | 11-11 22:35 |
| `video/clips/clip2_champion_vs_counter_075x.mp4` | docs/video | 11-11 22:35 |
| `video/clips/clip3_outboxer_vs_jabber.mp4` | docs/video | 11-11 22:36 |
| `video/clips/clip3_outboxer_vs_jabber_075x.mp4` | docs/video | 11-11 22:36 |
| `video/clips/clip4_champion_vs_slugger.mp4` | docs/video | 11-11 22:37 |
| `video/clips/clip4_champion_vs_slugger_075x.mp4` | docs/video | 11-11 22:37 |
| `video/clips/clip5_counter_vs_swarmer.mp4` | docs/video | 11-11 22:38 |
| `video/clips/clip5_counter_vs_swarmer_075x.mp4` | docs/video | 11-11 22:38 |
| `video/debris_clash.mp4` | ワークツリー voxel-damage の videos | 11-11 22:15 |
| `video/debris_katana.mp4` | ワークツリー voxel-damage の videos | 11-11 22:15 |
| `video/debris_sheet.png` | ワークツリー voxel-damage の videos | 11-11 22:15 |
| `video/debris_zoom.png` | ワークツリー voxel-damage の videos | 11-11 22:15 |
| `video/defense_sheet.png` | ワークツリー voxel-damage の videos | 11-11 23:10 |
| `video/down/champion_vs_counter_202.mp4` | docs/video | 11-11 23:35 |
| `video/down/champion_vs_counter_202_small.mp4` | docs/video | 11-11 23:36 |
| `video/down/champion_vs_slugger_404.mp4` | docs/video | 11-11 23:35 |
| `video/down/champion_vs_slugger_404_small.mp4` | docs/video | 11-11 23:37 |
| `video/down/counter_vs_swarmer_505.mp4` | docs/video | 11-11 23:36 |
| `video/down/counter_vs_swarmer_505_small.mp4` | docs/video | 11-11 23:37 |
| `video/down/slugger_vs_swarmer_101.mp4` | docs/video | 11-11 23:32 |
| `video/down/slugger_vs_swarmer_101_small.mp4` | docs/video | 11-11 23:37 |
| `video/facecam_champion_vs_swarmer.mp4` | docs/video | 11-11 20:24 |
| `video/facecam_champion_vs_swarmer_small.mp4` | docs/video | 11-11 20:25 |
| `video/imit_long.mp4` | ワークツリー voxel-damage の videos | 11-11 19:32 |
| `video/imit_test.mp4` | ワークツリー voxel-damage の videos | 11-11 19:28 |
| `video/imit_test_sheet.png` | ワークツリー voxel-damage の videos | 11-11 19:28 |
| `video/ko/ko1_slugger_vs_swarmer.mp4` | docs/video | 11-11 22:46 |
| `video/ko/ko1_slugger_vs_swarmer_075x.mp4` | docs/video | 11-11 22:46 |
| `video/ko/ko2_champion_vs_counter.mp4` | docs/video | 11-11 22:47 |
| `video/ko/ko2_champion_vs_counter_075x.mp4` | docs/video | 11-11 22:47 |
| `video/ko/ko3_outboxer_vs_jabber.mp4` | docs/video | 11-11 22:48 |
| `video/ko/ko3_outboxer_vs_jabber_075x.mp4` | docs/video | 11-11 22:48 |
| `video/ko/ko4_champion_vs_slugger.mp4` | docs/video | 11-11 22:49 |
| `video/ko/ko4_champion_vs_slugger_075x.mp4` | docs/video | 11-11 22:49 |
| `video/ko/ko5_counter_vs_swarmer.mp4` | docs/video | 11-11 22:50 |
| `video/ko/ko5_counter_vs_swarmer_075x.mp4` | docs/video | 11-11 22:50 |
| `video/mocap_basic.mp4` | ワークツリー voxel-damage の videos | 11-11 19:21 |
| `video/mocap_basic_sheet.png` | ワークツリー voxel-damage の videos | 11-11 19:21 |
| `video/slice_katana.mp4` | ワークツリー voxel-damage の videos | 11-11 23:38 |
| `video/slice_katana_sheet.png` | ワークツリー voxel-damage の videos | 11-11 23:38 |
| `video/slice_lab.mp4` | ワークツリー voxel-damage の videos | 11-11 23:38 |
| `video/slice_sheet.png` | ワークツリー voxel-damage の videos | 11-11 23:38 |
| `video/slice_zoom.png` | ワークツリー voxel-damage の videos | 11-11 23:39 |
| `video/spray_katana.mp4` | ワークツリー voxel-damage の videos | 11-11 22:26 |
| `video/spray_lab.mp4` | ワークツリー voxel-damage の videos | 11-11 22:25 |
| `video/spray_lab_sheet.png` | ワークツリー voxel-damage の videos | 11-11 22:26 |
| `video/spray_zoom.png` | ワークツリー voxel-damage の videos | 11-11 22:26 |
| `video/sword_basics.mp4` | ワークツリー voxel-damage の videos | 11-11 19:38 |
| `video/sword_basics_sheet.png` | ワークツリー voxel-damage の videos | 11-11 19:38 |
| `video/sword_sparring_drill.mp4` | ワークツリー voxel-damage の videos | 11-11 18:39 |
| `video/sword_sparring_sheet.png` | ワークツリー voxel-damage の videos | 11-11 18:39 |
| `video/swordsman_defense.mp4` | ワークツリー voxel-damage の videos | 11-11 23:10 |
| `video/swordsman_katana_katana.mp4` | ワークツリー voxel-damage の videos | 11-11 20:56 |
| `video/swordsman_katana_longsword.mp4` | ワークツリー voxel-damage の videos | 11-11 20:56 |
| `video/swordsman_kk_sheet.png` | ワークツリー voxel-damage の videos | 11-11 20:36 |
| `video/swordsman_longsword_longsword.mp4` | ワークツリー voxel-damage の videos | 11-11 20:56 |
| `video/versus_バット_ロングソード.mp4` | ワークツリー voxel-damage の videos | 11-12 00:27 |
| `video/versus_拳_バット.mp4` | ワークツリー voxel-damage の videos | 11-12 00:24 |
| `video/versus_拳_日本刀.mp4` | ワークツリー voxel-damage の videos | 11-12 00:25 |
| `video/versus_日本刀_棍棒.mp4` | ワークツリー voxel-damage の videos | 11-12 00:25 |
| `video/vs1.png` | ワークツリー voxel-damage の videos | 11-12 00:27 |
| `video/vs2.png` | ワークツリー voxel-damage の videos | 11-12 00:27 |
| `video/z_1.15.png` | ワークツリー voxel-damage の videos | 11-11 23:39 |
| `video/z_1.3.png` | ワークツリー voxel-damage の videos | 11-11 23:39 |
| `video/z_9.3.png` | ワークツリー voxel-damage の videos | 11-11 23:39 |
| `video/z_9.4.png` | ワークツリー voxel-damage の videos | 11-11 23:39 |

## gen08_1112_stamina-tv

スタミナと臓器・テレビ中継・狙い / 武器リング・打って離れる・武器を選ばない守り（PhysicsVersion 9–13）

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/guard_defense_drill.mp4` | ワークツリー voxel-damage の videos | 11-12 15:39 |
| `video/guard_defense_drill_small.mp4` | ワークツリー voxel-damage の videos | 11-12 15:40 |
| `video/hitaway_katana_bat.mp4` | ワークツリー voxel-damage の videos | 11-12 11:53 |
| `video/hitaway_katana_longsword.mp4` | ワークツリー voxel-damage の videos | 11-12 11:52 |
| `video/tv/tv1_champion_vs_swarmer.events.json` | docs/video | 11-12 09:20 |
| `video/tv/tv1_champion_vs_swarmer.mp4` | docs/video | 11-12 09:20 |
| `video/tv/tv2_champion_vs_slugger.events.json` | docs/video | 11-12 11:10 |
| `video/tv/tv2_champion_vs_slugger.mp4` | docs/video | 11-12 11:10 |
| `video/tv/tv2_champion_vs_slugger.script.json` | docs/video | 11-12 11:12 |
| `video/tv/tv2_champion_vs_slugger_live.mp4` | docs/video | 11-12 11:12 |
| `video/tv/tv2_champion_vs_slugger_live_small.mp4` | docs/video | 11-12 11:13 |
| `video/tv/tv2_champion_vs_slugger_live_voices/`（19 ファイル） | docs/video | 11-12 11:12 |
| `video/tv/tv3_cyanosis.events.json` | docs/video | 11-12 11:26 |
| `video/tv/tv3_cyanosis.mp4` | docs/video | 11-12 11:26 |
| `video/tv/tv3_cyanosis_small.mp4` | docs/video | 11-12 11:27 |
| `video/tv/tv4_winded.events.json` | docs/video | 11-12 14:06 |
| `video/tv/tv4_winded.mp4` | docs/video | 11-12 14:06 |
| `video/tv/tv4_winded_small.mp4` | docs/video | 11-12 14:07 |
| `video/tv/tv5_face_meter.events.json` | docs/video | 11-12 18:59 |
| `video/tv/tv5_face_meter.mp4` | docs/video | 11-12 18:59 |
| `video/tv/tv5_face_meter_small.mp4` | docs/video | 11-12 19:07 |
| `video/tv/tv_champion_vs_swarmer.events.json` | docs/video | 11-12 09:48 |
| `video/tv/tv_champion_vs_swarmer.mp4` | docs/video | 11-12 09:48 |
| `video/tv/tv_champion_vs_swarmer.script.json` | docs/video | 11-12 09:53 |
| `video/tv/tv_champion_vs_swarmer_live.mp4` | docs/video | 11-12 09:54 |
| `video/tv/tv_champion_vs_swarmer_live_small.mp4` | docs/video | 11-12 09:54 |
| `video/tv/tv_champion_vs_swarmer_live_voices/`（22 ファイル） | docs/video | 11-12 09:53 |
| `video/tv/tv_slugger_vs_counter.events.json` | docs/video | 11-12 09:51 |
| `video/tv/tv_slugger_vs_counter.mp4` | docs/video | 11-12 09:52 |
| `video/tv/tv_slugger_vs_counter.script.json` | docs/video | 11-12 09:53 |
| `video/tv/tv_slugger_vs_counter_live.mp4` | docs/video | 11-12 09:54 |
| `video/tv/tv_slugger_vs_counter_live_small.mp4` | docs/video | 11-12 09:54 |
| `video/tv/tv_slugger_vs_counter_live_voices/`（47 ファイル） | docs/video | 11-12 09:53 |
| `video/wear_katar_gauntlet_small.mp4` | docs/video | 11-12 11:18 |
| `video/wear_knuckles_none_small.mp4` | docs/video | 11-12 11:18 |

## gen09_1113_pressure-fracture

皮膚の下の圧力・擦り減り・材質の接触音・審判のダウン・骨折 / guardspar（PhysicsVersion 16）

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/breakdemo.mp4` | videos | 11-13 02:00 |
| `video/glove_contact.mp4` | docs/video | 11-12 23:01 |
| `video/sounds/gauntlet_vs_katar.mp4` | docs/video | 11-13 00:32 |
| `video/sounds/gauntlet_vs_katar_small.mp4` | docs/video | 11-13 00:36 |
| `video/sounds/glove_vs_glove.mp4` | docs/video | 11-13 00:27 |
| `video/sounds/glove_vs_glove_small.mp4` | docs/video | 11-13 00:30 |
| `video/sounds/knuckles_vs_glove.mp4` | docs/video | 11-13 00:29 |
| `video/sounds/knuckles_vs_glove_small.mp4` | docs/video | 11-13 00:30 |
| `video/sounds/none_vs_none.mp4` | docs/video | 11-13 00:36 |
| `video/sounds/none_vs_none_small.mp4` | docs/video | 11-13 00:37 |
| `video/sounds/v2_glove.mp4` | docs/video | 11-13 00:57 |
| `video/sounds/v3_down.mp4` | docs/video | 11-13 01:04 |
| `video/sounds/v3_down_small.mp4` | docs/video | 11-13 01:05 |
| `video/sounds/v4_rules.mp4` | docs/video | 11-13 01:27 |
| `video/sounds/v5_glove.mp4` | docs/video | 11-13 01:51 |
| `video/sounds/v5_glove_small.mp4` | docs/video | 11-13 01:54 |
| `video/sounds/v5_none.mp4` | docs/video | 11-13 01:53 |
| `video/sounds/v5_none_small.mp4` | docs/video | 11-13 01:54 |
| `video/tv/tv6_skin.events.json` | docs/video | 11-12 23:49 |
| `video/tv/tv6_skin.mp4` | docs/video | 11-12 23:49 |
| `video/tv/tv6_skin_small.mp4` | docs/video | 11-12 23:50 |

## gen10_1113_range-fatigue

claude/a-3d0203 のマージ（疲れが拳の速さに出る・間合いの層・学ぶ守り）・折れた骨をつなぐ肉（PhysicsVersion 17–18）

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/fracture_champion_slugger_s1.mp4` | videos | 11-13 08:51 |
| `video/fracture_champion_slugger_s2.mp4` | videos | 11-13 08:52 |
| `video/fracture_champion_slugger_s2_small.mp4` | videos | 11-13 08:53 |
| `video/r5_champion_vs_jabber.mp4` | ワークツリー a-3d0203 の docs/video | 11-13 02:08 |
| `video/r5_champion_vs_jabber_small.mp4` | ワークツリー a-3d0203 の docs/video | 11-13 02:08 |
| `video/r5_counter_vs_outboxer.mp4` | ワークツリー a-3d0203 の docs/video | 11-13 02:12 |
| `video/r5_counter_vs_outboxer_small.mp4` | ワークツリー a-3d0203 の docs/video | 11-13 02:12 |
| `video/r5_fist_vs_katana.mp4` | ワークツリー a-3d0203 の docs/video | 11-13 02:17 |
| `video/r5_fist_vs_katana_small.mp4` | ワークツリー a-3d0203 の docs/video | 11-13 02:17 |
| `video/r5_fist_vs_longsword.mp4` | ワークツリー a-3d0203 の docs/video | 11-13 02:19 |
| `video/r5_fist_vs_longsword_small.mp4` | ワークツリー a-3d0203 の docs/video | 11-13 02:19 |
| `video/r5_slugger_vs_swarmer.mp4` | ワークツリー a-3d0203 の docs/video | 11-13 02:10 |
| `video/r5_slugger_vs_swarmer_small.mp4` | ワークツリー a-3d0203 の docs/video | 11-13 02:10 |
| `video/range_keep_guard.mp4` | ワークツリー a-3d0203 の docs/video | 11-12 20:42 |
| `video/range_keep_guard_small.mp4` | ワークツリー a-3d0203 の docs/video | 11-12 20:42 |
| `video/range_press_guard.mp4` | ワークツリー a-3d0203 の docs/video | 11-12 22:40 |
| `video/range_press_guard_small.mp4` | ワークツリー a-3d0203 の docs/video | 11-12 22:40 |

## gen11_1114_intent-karate

意図の操作（キーボード）・1 打 1 音・肉でつながる骨折と切断 / 空手（蹴り・指・パリー）・目と見え・顔・体格・剣の接触の掃き直し（PhysicsVersion 19–27）

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `replays/miniring_desktop_20261114_003408.mrrep` | replays | 11-14 00:41 |
| `video/gen10_champion_vs_slugger_glove.mp4` | docs/video | 11-13 13:04 |
| `video/gen10_champion_vs_slugger_glove_small.mp4` | docs/video | 11-13 13:06 |
| `video/gen10_fracture_check.png` | docs/video | 11-13 14:49 |
| `video/gen10_fracture_detail.png` | docs/video | 11-13 14:49 |
| `video/gen10_fracture_final_check.png` | docs/video | 11-13 14:50 |
| `video/gen10_fracture_search1.mp4` | docs/video | 11-13 14:45 |
| `video/gen10_fracture_search2.mp4` | docs/video | 11-13 14:46 |
| `video/gen10_fracture_search3.mp4` | docs/video | 11-13 14:47 |
| `video/gen10_fracture_search4.mp4` | docs/video | 11-13 14:47 |
| `video/gen10_fracture_search5.mp4` | docs/video | 11-13 14:48 |
| `video/gen10_fracture_search6.mp4` | docs/video | 11-13 14:48 |
| `video/gen10_fracture_swarmer_champion_seed3.mp4` | docs/video | 11-13 14:50 |
| `video/gen10_fracture_swarmer_champion_seed3_small.mp4` | docs/video | 11-13 14:50 |
| `video/gen10_intent.mp4` | docs/video | 11-13 15:04 |
| `video/gen10_intent.mrrep` | docs/video | 11-13 15:04 |
| `video/gen10_intent_check.png` | docs/video | 11-13 15:04 |
| `video/gen10_intent_demonstration.mp4` | docs/video | 11-13 15:06 |
| `video/gen10_intent_demonstration.mrrep` | docs/video | 11-13 15:06 |
| `video/gen10_intent_demonstration_check.png` | docs/video | 11-13 15:07 |
| `video/gen10_intent_demonstration_small.mp4` | docs/video | 11-13 15:08 |
| `video/gen10_intent_small.mp4` | docs/video | 11-13 15:04 |
| `video/gen10_knuckles_champion_vs_slugger.mp4` | docs/video | 11-13 13:05 |
| `video/gen10_knuckles_champion_vs_slugger_small.mp4` | docs/video | 11-13 13:06 |
| `video/gen11_flesh_bridge_diagnostic.mp4` | docs/video | 11-13 20:14 |
| `video/gen11_flesh_bridge_diagnostic.t2.0.png` | docs/video | 11-13 20:14 |
| `video/gen11_flesh_bridge_diagnostic.t4.0.png` | docs/video | 11-13 20:14 |
| `video/gen11_flesh_bridge_diagnostic.t5.0.png` | docs/video | 11-13 20:14 |
| `video/gen11_flesh_bridge_diagnostic.t5.5.png` | docs/video | 11-13 20:14 |
| `video/gen11_flesh_bridge_diagnostic.t6.0.png` | docs/video | 11-13 20:14 |
| `video/gen11_fracture_check.png` | docs/video | 11-13 20:02 |
| `video/gen11_fracture_detail.png` | docs/video | 11-13 20:02 |
| `video/gen11_fracture_swarmer_champion_seed3.mp4` | docs/video | 11-13 20:01 |
| `video/gen11_fracture_swarmer_champion_seed3_small.mp4` | docs/video | 11-13 20:02 |
| `video/gen11_intent_feel.audio.csv` | ワークツリー agent-aa499444c2e84c5ae の docs/video | 11-14 13:08 |
| `video/gen11_intent_feel.mp4` | ワークツリー agent-aa499444c2e84c5ae の docs/video | 11-14 13:08 |
| `video/gen11_intent_feel.mrrep` | ワークツリー agent-aa499444c2e84c5ae の docs/video | 11-14 13:08 |
| `video/gen11_intent_feel_small.mp4` | ワークツリー agent-aa499444c2e84c5ae の docs/video | 11-14 13:15 |
| `video/gen11_katar_check.png` | docs/video | 11-13 20:12 |
| `video/gen11_katar_seed613_small.mp4` | docs/video | 11-13 20:14 |
| `video/gen11_katar_sever_check.png` | docs/video | 11-13 20:21 |
| `video/gen11_katar_sever_detail.png` | docs/video | 11-13 20:21 |
| `video/gen11_katar_sever_seed613.audio.csv` | docs/video | 11-13 20:11 |
| `video/gen11_katar_sever_seed613.mp4` | docs/video | 11-13 20:11 |
| `video/gen11_katar_sever_slugger_swarmer_seed2.audio.csv` | docs/video | 11-13 20:20 |
| `video/gen11_katar_sever_slugger_swarmer_seed2.mp4` | docs/video | 11-13 20:20 |
| `video/gen11_katar_sever_slugger_swarmer_seed2_small.mp4` | docs/video | 11-13 20:21 |
| `video/gen11_sword_check.png` | docs/video | 11-13 20:02 |
| `video/gen11_sword_sever_detail.png` | docs/video | 11-13 20:02 |
| `video/gen12_bare_fist.audio.csv` | docs/video | 11-13 21:12 |
| `video/gen12_bare_fist.mp4` | docs/video | 11-13 21:12 |
| `video/gen12_bare_fist_sheet.png` | docs/video | 11-13 21:13 |
| `video/gen12_bare_fist_small.mp4` | docs/video | 11-13 21:13 |
| `video/gen12_karate_vs_boxer.audio.csv` | docs/video | 11-14 12:50 |
| `video/gen12_karate_vs_boxer.mp4` | docs/video | 11-14 12:50 |
| `video/gen12_karate_vs_boxer_knee.png` | docs/video | 11-14 12:52 |
| `video/gen12_karate_vs_boxer_sheet.png` | docs/video | 11-14 12:52 |
| `video/gen12_karate_vs_boxer_small.mp4` | docs/video | 11-14 12:52 |
| `video/gen13_big_vs_small.mp4` | docs/video | 11-14 14:25 |
| `video/gen13_big_vs_small_small.mp4` | docs/video | 11-14 14:25 |
| `video/gen13_blinded.mp4` | docs/video | 11-14 14:21 |
| `video/gen13_blinded_small.mp4` | docs/video | 11-14 14:21 |
| `video/sound_v6_glove_after.mp4` | docs/video | 11-13 15:41 |
| `video/sound_v6_glove_before.mp4` | docs/video | 11-13 15:15 |
| `video/sound_v6_glove_compare.mp4` | docs/video | 11-13 15:44 |
| `video/sound_v6_knuckles_after.mp4` | docs/video | 11-13 15:42 |
| `video/sound_v6_knuckles_before.mp4` | docs/video | 11-13 15:15 |
| `video/sound_v6_knuckles_compare.mp4` | docs/video | 11-13 15:44 |
| `video/sound_v6_sword_after.mp4` | docs/video | 11-13 15:45 |
| `video/sound_v6_sword_after.wav` | docs/video | 11-13 15:45 |
| `video/sound_v6_sword_before.mp4` | docs/video | 11-13 15:46 |
| `video/sound_v6_sword_before.wav` | docs/video | 11-13 15:19 |
| `video/sound_v6_sword_before_aligned.mp4` | docs/video | 11-13 15:38 |
| `video/sound_v6_sword_compare.mp4` | docs/video | 11-13 15:46 |
| `video/sound_v7_final_labels_check.png` | docs/video | 11-13 20:14 |
| `video/sound_v7_glove_after.audio.csv` | docs/video | 11-13 20:16 |
| `video/sound_v7_glove_after.mp4` | docs/video | 11-13 20:16 |
| `video/sound_v7_glove_compare.mp4` | docs/video | 11-13 20:17 |
| `video/sound_v7_glove_contact_frame.png` | docs/video | 11-13 20:12 |
| `video/sound_v7_glove_onset_wave.png` | docs/video | 11-13 20:17 |
| `video/sound_v7_glove_v6_same_scene.mp4` | docs/video | 11-13 20:06 |
| `video/sound_v7_knuckles_after.audio.csv` | docs/video | 11-13 20:11 |
| `video/sound_v7_knuckles_after.mp4` | docs/video | 11-13 20:11 |
| `video/sound_v7_knuckles_compare.mp4` | docs/video | 11-13 20:17 |
| `video/sound_v7_knuckles_contact_frame.png` | docs/video | 11-13 20:12 |
| `video/sound_v7_knuckles_onset_wave.png` | docs/video | 11-13 20:12 |
| `video/sound_v7_knuckles_v6_same_scene.mp4` | docs/video | 11-13 20:06 |
| `video/sound_v7_labels_check.png` | docs/video | 11-13 20:02 |
| `video/sound_v7_sword_after.audio.csv` | docs/video | 11-13 20:12 |
| `video/sound_v7_sword_after.mp4` | docs/video | 11-13 20:12 |
| `video/sound_v7_sword_after.wav` | docs/video | 11-13 20:12 |
| `video/sound_v7_sword_compare.mp4` | docs/video | 11-13 20:17 |
| `video/sound_v7_sword_contact_frame.png` | docs/video | 11-13 20:19 |
| `video/sound_v7_sword_onset_wave.png` | docs/video | 11-13 20:19 |
| `video/sound_v7_sword_v6_same_scene.mp4` | docs/video | 11-13 20:10 |
| `video/sound_v7_sword_v6_same_scene.wav` | docs/video | 11-13 20:10 |
| `video/sword_seam_after_2026-11-14.audio.csv` | ワークツリー agent-a420dd56239c09f61 の docs/video | 11-14 14:53 |
| `video/sword_seam_after_2026-11-14.mp4` | ワークツリー agent-a420dd56239c09f61 の docs/video | 11-14 14:53 |
| `video/sword_seam_after_2026-11-14.wav` | ワークツリー agent-a420dd56239c09f61 の docs/video | 11-14 14:53 |
| `video/sword_seam_before_2026-11-14.audio.csv` | ワークツリー agent-a420dd56239c09f61 の docs/video | 11-14 12:47 |
| `video/sword_seam_before_2026-11-14.mp4` | ワークツリー agent-a420dd56239c09f61 の docs/video | 11-14 12:47 |
| `video/sword_seam_before_2026-11-14.wav` | ワークツリー agent-a420dd56239c09f61 の docs/video | 11-14 12:47 |

## gen12_1115_stamina-swords

デスクトップのゲーム選択・対戦・再生 / スタミナの予算・描画と物理の分離 / 刀とロングソードの型・頭の作り直し

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `replays/miniring_desktop_20261114_182956.mrrep` | replays | 11-14 18:37 |
| `replays/miniring_desktop_20261114_192820.mrrep` | replays | 11-14 19:30 |
| `replays/miniring_desktop_20261114_193107.mrrep` | replays | 11-14 19:32 |
| `replays/miniring_desktop_20261114_193220.mrrep` | replays | 11-14 19:32 |
| `replays/miniring_desktop_20261114_193238.mrrep` | replays | 11-14 19:44 |
| `replays/miniring_desktop_20261114_194431.mrrep` | replays | 11-14 19:49 |
| `replays/miniring_desktop_20261114_194941.mrrep` | replays | 11-14 19:57 |
| `replays/miniring_desktop_20261114_204507.mrrep` | replays | 11-14 20:49 |
| `replays/miniring_desktop_20261114_204945.mrrep` | replays | 11-14 20:51 |
| `replays/miniring_desktop_20261115_094108.mrrep` | replays | 11-15 09:41 |
| `replays/miniring_desktop_20261115_164735.mrrep` | replays | 11-15 16:48 |
| `replays/miniring_desktop_20261115_164907.mrrep` | replays | 11-15 16:50 |
| `replays/miniring_desktop_20261115_182205.mrrep` | replays | 11-15 18:23 |
| `replays/miniring_desktop_20261115_182314.mrrep` | replays | 11-15 18:25 |
| `video/cpu_perception_delay.audio.csv` | docs/video | 11-15 09:09 |
| `video/cpu_perception_delay.mp4` | docs/video | 11-15 09:09 |
| `video/cpu_perception_delay_small.mp4` | docs/video | 11-15 09:09 |
| `video/delay/`（35 ファイル） | docs/video | 11-15 10:11〜10:40 |
| `video/gen13_katana_forms.audio.csv` | docs/video | 11-15 20:09 |
| `video/gen13_katana_forms.mp4` | docs/video | 11-15 20:09 |
| `video/gen13_longsword_forms.audio.csv` | docs/video | 11-15 20:10 |
| `video/gen13_longsword_forms.mp4` | docs/video | 11-15 20:10 |
| `video/gen13_stamina.audio.csv` | docs/video | 11-15 11:51 |
| `video/gen13_stamina.mp4` | docs/video | 11-15 11:51 |

## gen13_1116_characters

キャラクターファイル・首と脊椎・指の手・武器の教科書・レフェリーなし / 実況の PDCA・テレビ中継の札（tvcard 1–4）

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `replays/miniring_desktop_20261115_210202.mrrep` | replays | 11-15 21:02 |
| `replays/miniring_desktop_20261115_210303.mrrep` | replays | 11-15 21:03 |
| `replays/miniring_desktop_20261115_210631.mrrep` | replays | 11-15 21:07 |
| `replays/miniring_desktop_20261115_210734.mrrep` | replays | 11-15 21:08 |
| `replays/miniring_desktop_20261115_211525.mrrep` | replays | 11-15 21:22 |
| `video/boot_snag_after.audio.csv` | ワークツリー boots の docs/video | 11-16 08:16 |
| `video/boot_snag_after.mp4` | ワークツリー boots の docs/video | 11-16 08:16 |
| `video/boot_snag_before.audio.csv` | ワークツリー boots の docs/video | 11-16 08:14 |
| `video/boot_snag_before.mp4` | ワークツリー boots の docs/video | 11-16 08:14 |
| `video/commentary-pdca/`（1774 ファイル） | docs/video | 11-16 12:25〜14:56 |
| `video/media_tmp_build-tactics/`（19 ファイル） | ワークツリー build-tactics の media_tmp | 11-16 11:58〜12:16 |
| `video/media_tmp_fingers/`（248 ファイル） | ワークツリー fingers の media_tmp | 11-16 15:09 |
| `video/media_tmp_neck/`（25 ファイル） | ワークツリー neck の media_tmp | 11-16 13:08〜13:28 |
| `video/media_tmp_referee/`（7 ファイル） | ワークツリー referee の media_tmp | 11-16 08:13〜08:41 |
| `video/media_tmp_weapons/`（16 ファイル） | ワークツリー weapons の media_tmp | 11-16 14:48〜15:49 |
| `video/tvcard/`（140 ファイル） | docs/video | 11-15 21:16〜21:27 |
| `video/tvcard2/`（152 ファイル） | ワークツリー tvcard2 の docs/video | 11-16 09:10〜09:23 |
| `video/tvcard3/`（265 ファイル） | ワークツリー tvcard2 の docs/video | 11-16 10:26〜10:45 |
| `video/tvcard4/`（394 ファイル） | ワークツリー tvcard2 の docs/video | 11-16 11:26〜11:46 |
| `video/tvshow-test/`（24 ファイル） | docs/video | 11-16 12:16〜12:18 |
| `video/tvshow/`（1 ファイル） | docs/video | 11-16 12:12 |

## gen14_1117_tvprogram-cmaes

大会の番組・弱った相手の詰め・解説・CMA-ES（cmatune）・臨界パワーのスタミナ（PhysicsVersion 38）/ 棒術・トンファー・テコンドー・片手剣と盾・生配信の試し

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/cma_punch2_Slip_after.mp4` | ワークツリー evo-p0b の docs/video | 11-16 19:18 |
| `video/cma_punch2_Slip_before.mp4` | ワークツリー evo-p0b の docs/video | 11-16 19:18 |
| `video/cma_punch3_Weave_after.mp4` | ワークツリー evo-p0b の docs/video | 11-16 20:22 |
| `video/cma_punch3_Weave_before.mp4` | ワークツリー evo-p0b の docs/video | 11-16 20:22 |
| `video/live-test/`（32 ファイル） | docs/video | 11-16 20:12〜23:00 |
| `video/media_tmp_bojutsu/`（27 ファイル） | ワークツリー bojutsu の media_tmp | 11-16 18:00〜19:35 |
| `video/media_tmp_master/`（60 ファイル） | media_tmp | 11-17 01:25〜01:46 |
| `video/media_tmp_swordshield/`（31 ファイル） | ワークツリー swordshield の media_tmp | 11-16 21:08〜22:08 |
| `video/media_tmp_taekwondo/`（17 ファイル） | ワークツリー taekwondo の media_tmp | 11-16 17:31〜17:42 |
| `video/media_tmp_tonfa/`（18 ファイル） | ワークツリー tonfa の media_tmp | 11-16 19:55〜20:33 |
| `video/wvc-boxing-tournament/`（257 ファイル） | ワークツリー tvprogram の docs/video | 11-16 20:50〜11-17 08:03 |

## gen15_1117_legs-woundcam

脚を失っても戦う（PhysicsVersion 39）・棒の滑る握り・VR の武器と動きの記録・回し蹴り・傷のカメラ（wound-cam）・WVC 再戦の中継

| ファイル | 元の場所 | 撮った時刻 |
|---|---|---|
| `video/f_01.png` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/f_02.png` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/f_03.png` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/f_04.png` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/f_05.png` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/f_06.png` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/f_07.png` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/f_08.png` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/gen38/`（16 ファイル） | ワークツリー tvprogram の docs/video | 11-17 15:59〜16:04 |
| `video/jo_tsuki.mp4` | ワークツリー staff-vr-motion-kick-d8bd9a の videos | 11-17 15:20 |
| `video/korean_check.audio.csv` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/korean_check.events.json` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/korean_check.mp4` | ワークツリー agent-ab89385668f2ae181 の docs/video | 11-17 21:05 |
| `video/legends_akira_diego_s5.audio.csv` | docs/video | 11-17 19:42 |
| `video/legends_akira_diego_s5.mp4` | docs/video | 11-17 19:42 |
| `video/legloss_akira_diego_s1.audio.csv` | docs/video | 11-17 17:00 |
| `video/legloss_akira_diego_s1.mp4` | docs/video | 11-17 17:00 |
| `video/legloss_akira_diego_s5.audio.csv` | docs/video | 11-17 16:59 |
| `video/legloss_akira_diego_s5.mp4` | docs/video | 11-17 16:59 |
| `video/legloss_akira_diego_s5_small.mp4` | docs/video | 11-17 17:05 |
| `video/legloss_breakdemo.audio.csv` | docs/video | 11-17 11:59 |
| `video/legloss_breakdemo.mp4` | docs/video | 11-17 11:59 |
| `video/legloss_breakdemo.wav` | docs/video | 11-17 11:59 |
| `video/legloss_jin_konrad_s23.audio.csv` | docs/video | 11-17 17:05 |
| `video/legloss_jin_konrad_s23.mp4` | docs/video | 11-17 17:05 |
| `video/legloss_jin_konrad_s23_small.mp4` | docs/video | 11-17 17:05 |
| `video/legloss_jin_konrad_s8.audio.csv` | docs/video | 11-17 16:58 |
| `video/legloss_jin_konrad_s8.mp4` | docs/video | 11-17 16:58 |
| `video/media_tmp_agent-a5637c51405f75878/`（16 ファイル） | ワークツリー agent-a5637c51405f75878 の media_tmp | 11-17 12:52〜18:09 |
| `video/staff_slide_tsuki.mp4` | ワークツリー agent-acff62314be37d49b の docs/video | 11-17 12:41 |
| `video/vrmotion_thrust.mp4` | ワークツリー agent-a260c86478850153a の videos | 11-17 11:58 |
| `video/wvc-oneshot/`（507 ファイル） | ワークツリー wound-cam の docs/video | 11-17 19:47〜20:52 |
| `video/wvc-random1/`（532 ファイル） | ワークツリー wound-cam の docs/video | 11-17 20:59〜21:49 |
| `video/wvc-rematch-free/`（465 ファイル） | docs/video | 11-17 12:31〜14:46 |
| `video/wvc-rematch-open/`（356 ファイル） | docs/video | 11-17 12:31〜12:53 |
| `video/wvc-rematch-prospective/`（427 ファイル） | docs/video | 11-17 13:36〜14:53 |
| `video/wvc-rematch-prospective2/`（478 ファイル） | docs/video | 11-17 14:33〜16:07 |
| `video/wvc-rematch-wound/`（899 ファイル） | ワークツリー wound-cam の docs/video | 11-17 15:19〜18:55 |
| `video/wvc-rematch/`（386 ファイル） | docs/video | 11-17 10:53〜11:22 |
