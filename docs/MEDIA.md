# 動画・リプレイの保管（開発の世代ごと）

撮った動画・リプレイ（`.mrrep`）・聞き比べの音・確かめの静止画は、`media/<世代>/` に開発の世代ごとに保管する。`media/` は大きいので git に入れない（`.gitignore`）。この表が索引で、git に残る。

## 決まり

- **世代** = master の節目（体の物理の版 `Fighter.PhysicsVersion` が変わった所・大きな機能のマージ）。ファイルは「その作業が master に入った節目」の世代に置く（撮った時刻の次の節目。ブランチ・ワークツリーで撮ったものは、そのブランチが master に入った節目）。縮めた版（`_small`）・遅くした版（`_075x`）・実況の声と台本は元の動画と同じ世代。
- 中は `video/`（元の `docs/video/`・`videos/`・`.visualcheck/` の中の相対パスをそのまま）と `replays/`。
- 新しく撮る動画は今までどおり `docs/video/`・`videos/` へ、記録は `replays/` へ出る。世代を区切るとき（`PhysicsVersion` を上げてマージした・大きな機能が master に入った）に、ワークツリーの分も含めて `media/<次の世代>/` へ移し、この表に世代を足す。
- リプレイは記録した版の体でしか再生できない（`docs/REPLAY.md`）。古い世代の `.mrrep` は、その世代のコミットを checkout して再生する。

## 世代の一覧

未マージの物理19・音v7の作業動画はまだ `docs/video/` に置く。マージ時に次の世代へ移す対象と検証記録は [CODEX_REPORT_3_2026-11-13.md](plans/CODEX_REPORT_3_2026-11-13.md)。音の比較3本、種3の骨折試合、前腕の接続・切断診断、種2のカタール切断を含む（縮小版も音声を保持）。

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
| `gen10_1113_range-fatigue` | 2026-11-13 〜（今） | claude/a-3d0203 のマージ（疲れが拳の速さに出る・間合いの層・学ぶ守り）・折れた骨をつなぐ肉（PhysicsVersion 17–18） | 17 | 439 MiB |

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
