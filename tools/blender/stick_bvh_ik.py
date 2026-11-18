"""棒人形の BVH（stick --forms/--swing --bvh）を Blender に読み込み、両手で握る武器のもう一方の手に IK を掛けて .blend に保存する。

使い方（Blender 4.x）:
    blender -b --factory-startup --python tools/blender/stick_bvh_ik.py -- <out.blend> <a.bvh> [b.bvh ...] [--bake]

- 最初の BVH でアーマチュアを作り、残りの BVH は動き（Action）だけを取り込む（名前 = ファイル名から先頭の「キャラ_」を除いた物、偽のユーザーを付けて残す）。
  Action Editor で動きを切り替えられる。シーンの fps は BVH のコマの間隔（棒人形は 120 fps）に合わせる。
- 握りのボーン grip.L（または grip.R）があれば: 同じ側の前腕（forearm.L）に IK（目標 = grip.L の先、つなぐ長さ 2 = 前腕と上腕）を掛ける。
  BVH の腕の角を出発点に解くので、肘の向きは元の動きのまま（Pole Target は置かない）。剣を動かしても手が柄から離れない。
- --bake: IK の結果を回転へ焼き込む（制約を外しても同じ姿勢になる。ゲームや別の道具へ渡すとき）。
- 終わりに、動きごとの「手首と握りの点の離れ」を IK なし・ありで出す（確かめ）。
"""
import os
import sys

import bpy


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    bake = "--bake" in args
    args = [a for a in args if a != "--bake"]
    if len(args) < 2:
        raise SystemExit("usage: -- <out.blend> <a.bvh> [b.bvh ...] [--bake]")
    out, paths = args[0], args[1:]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm = None
    actions = []
    for path in paths:
        before = set(bpy.data.objects)
        first = arm is None
        bpy.ops.import_anim.bvh(filepath=path, axis_forward='-Z', axis_up='Y',
                                update_scene_fps=first, update_scene_duration=first)
        imported = [o for o in bpy.data.objects if o not in before and o.type == 'ARMATURE'][0]
        action = imported.animation_data.action
        action.name = motion_name(path)
        action.use_fake_user = True
        actions.append(action)
        if first:
            arm = imported
            arm.name = os.path.splitext(os.path.basename(out))[0]
        else:
            bpy.data.objects.remove(imported, do_unlink=True)

    grip = next((b.name for b in arm.data.bones if b.name.startswith("grip.")), None)
    hand = None
    if grip is not None:
        hand = "forearm" + grip[len("grip"):]
        ik = arm.pose.bones[hand].constraints.new('IK')
        ik.name = "Grip IK"
        ik.target = arm
        ik.subtarget = grip
        ik.head_tail = 1.0  # 握りのボーンの先 = もう一方の手が柄を握る点
        ik.use_tail = True
        ik.chain_count = 2

    scene = bpy.context.scene
    for action in actions:
        use(arm, action)
        start, end = (int(x) for x in action.frame_range)
        if grip is not None:
            report(arm, hand, grip, action.name, start, end)
            if bake:
                bake_action(arm, action, start, end)

    use(arm, actions[0])
    scene.frame_start, scene.frame_end = (int(x) for x in actions[0].frame_range)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out))
    print("saved", os.path.abspath(out), "bones", len(arm.data.bones), "actions", len(actions), "grip", grip)


def motion_name(path):
    name = os.path.splitext(os.path.basename(path))[0]
    return name.split("_", 1)[1] if "_" in name else name


def use(arm, action):
    arm.animation_data.action = action
    data = arm.animation_data
    if hasattr(data, "action_slot") and data.action_slot is None and len(action.slots) > 0:
        data.action_slot = action.slots[0]


def gap(arm, hand, grip):
    wrist = arm.matrix_world @ arm.pose.bones[hand].tail
    target = arm.matrix_world @ arm.pose.bones[grip].tail
    return (wrist - target).length


def report(arm, hand, grip, name, start, end):
    scene = bpy.context.scene
    ik = arm.pose.bones[hand].constraints["Grip IK"]
    frames = range(start, end + 1, max(1, (end - start) // 20))
    worst = {}
    for on in (False, True):
        ik.mute = not on
        values = []
        for f in frames:
            scene.frame_set(f)
            values.append(gap(arm, hand, grip))
        worst[on] = (sum(values) / len(values), max(values))
    ik.mute = False
    print("check %-16s frames %d-%d  wrist-to-grip without IK: mean %.3f max %.3f m  with IK: mean %.4f max %.4f m"
          % (name, start, end, worst[False][0], worst[False][1], worst[True][0], worst[True][1]))


def bake_action(arm, action, start, end):
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.select_all(action='SELECT')
    bpy.ops.nla.bake(frame_start=start, frame_end=end, only_selected=True, visual_keying=True,
                     clear_constraints=False, use_current_action=True, bake_types={'POSE'})
    bpy.ops.object.mode_set(mode='OBJECT')


main()
