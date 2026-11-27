"""Bake different starting poses and consecutive swings onto Galva's analytic cuts."""

import argparse
import csv
from dataclasses import dataclass
from math import acos, ceil, degrees, radians, sqrt
from pathlib import Path
import sys
from types import SimpleNamespace

import bpy
import numpy as np
from mathutils import Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import galva_analytic_cut as arm
import galva_analytic_trunk_cut as trunk
from galva_analytic_cut_study import measured


RATES = (345, 240, 280, 320, 380, 500)
ARMS = tuple((name, prop) for name, prop in arm.CHANNELS) + tuple(
    (name.replace(".R", ".L"), prop) for name, prop in arm.CHANNELS
)
POSE_ACCEL = 1500
ARM_NAMES = {name for name, _ in ARMS}
POSE_NAMES = ("Low guard", "Drawn claw", "After horizontal", "After vertical", "After diagonal")
CUT_NAMES = ("Horizontal", "Vertical", "Diagonal")


@dataclass
class Pose:
    matrix: dict
    local: dict
    controls: dict
    root: Vector


def activate(rig, action):
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots[0]


def read_pose(scene, rig, action, frame):
    activate(rig, action)
    scene.frame_set(frame)
    return Pose({b.name: b.matrix.copy() for b in rig.pose.bones},
                {b.name: b.rotation_quaternion.copy() for b in rig.pose.bones if b.name not in ARM_NAMES},
                {(name, prop): float(rig.pose.bones[name][prop]) for name, prop in ARMS},
                rig.pose.bones["Thorax"].location.copy())


def cut_targets(scene, rig):
    horizontal = bpy.data.actions["01 T Horizontal"]
    results = {"Horizontal": horizontal}
    for label, tilt, reach, rake in (("Vertical", 90, 1.24, 24), ("Diagonal", 45, 1.24, 12)):
        activate(rig, bpy.data.actions["01 Whip Sweep"])
        scene.frame_set(27)
        origin = arm.Arm(rig).origin
        axis = Quaternion(arm.Z, radians(150)) @ arm.X
        normal = Quaternion(arm.Z.cross(axis), radians(tilt)) @ arm.Z
        radial = normal.cross(arm.Z).normalized()
        up = radial.cross(normal).normalized()
        outward = Quaternion(normal, radians(-8)) @ radial
        target = origin + radial * reach + up * 0.13 + normal * 0.06
        options = SimpleNamespace(point=target, direction=normal.cross(outward), normal=normal,
                                  rates=list(RATES), radius=1.25, rake=rake, runup=0.62,
                                  follow=0.62, acceleration=65)
        arm.bake(scene, rig, options)
        fixed = bpy.data.actions[arm.ACTION]
        trial = trunk.Trial(scene, rig, fixed)
        result, limit = trunk.optimize(trial, options.runup, options.follow, options.acceleration)
        trunk.bake(trial, result, limit, options.acceleration)
        assisted = bpy.data.actions[trunk.TRUNK_ACTION]
        fixed.name, assisted.name = f"Cut {label} A", f"Cut {label} T"
        results[label] = max((fixed, assisted), key=lambda action: action["contact_m_s"])
    return results


def smooth(t):
    return t**3 * (10 + t * (-15 + 6 * t))


def interval(a, b):
    changes = np.array([abs(a.controls[key] - b.controls[key]) for key in ARMS])
    rates = np.r_[RATES, RATES]
    turn = np.array([degrees(a.local[n].rotation_difference(b.local[n]).angle)
                     for n in ("Thorax", "Chest")])
    turn = np.minimum(turn, 360 - turn)
    changes, rates = np.r_[changes, turn], np.r_[rates, 160, 200]
    seconds = max(np.max(1.875 * changes / rates),
                  sqrt((10 / sqrt(3)) * np.max(changes) / POSE_ACCEL),
                  1.875 * (b.root - a.root).length / 0.65)
    return ceil(60 * seconds)


def matrix_at(q, p):
    m = q.to_matrix().to_4x4()
    m.translation = p
    return m


class PoseBlend:
    def __init__(self, rig):
        self.rig = rig
        self.rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
        self.parent = {b.name: b.parent.name if b.parent else None for b in rig.data.bones}
        self.bind = {n: self.rest[p].inverted() @ self.rest[n] if p else self.rest[n]
                     for n, p in self.parent.items()}

    def knees(self, suffix, world, start, end, blend):
        femur, shin, foot, toe = (part + suffix for part in ("Femur", "Tibia", "Tarsus", "Tip"))
        root = (world[self.parent[femur]] @ self.bind[femur]).translation
        ank = start.matrix[foot].translation.lerp(end.matrix[foot].translation, blend)
        length_a, length_b = (self.bind[name].translation.length for name in (shin, foot))
        axis = ank - root
        dist = axis.length
        if not abs(length_a - length_b) + 1e-6 < dist < length_a + length_b - 1e-6:
            raise ValueError("A planted leg cannot reach during the preparation.")
        axis /= dist
        hint = start.matrix[shin].translation.lerp(end.matrix[shin].translation, blend) - root
        side = hint - axis * hint.dot(axis)
        if side.length < 1e-6:
            raise ValueError("A planted leg cannot reach during the preparation.")
        along = (dist * dist + length_a * length_a - length_b * length_b) / (2 * dist)
        knee = root + axis * along + side.normalized() * sqrt(max(0, length_a * length_a - along * along))
        for bone, child, p, q in ((femur, shin, root, knee), (shin, foot, knee, ank)):
            aim = start.matrix[bone].to_quaternion().slerp(end.matrix[bone].to_quaternion(), blend)
            turn = (aim @ self.bind[child].translation).rotation_difference(q - p)
            world[bone] = matrix_at(turn @ aim, p)
        foot_rotation = start.matrix[foot].to_quaternion().slerp(end.matrix[foot].to_quaternion(), blend)
        world[foot] = matrix_at(foot_rotation, ank)
        toe_at = world[foot] @ self.bind[toe]
        toe_rotation = start.matrix[toe].to_quaternion().slerp(end.matrix[toe].to_quaternion(), blend)
        world[toe] = matrix_at(toe_rotation, toe_at.translation)

    def key(self, start, end, blend, frame, previous):
        world = {}
        for name in ("Thorax", "Chest", "Head", *("Coxa" + suffix for suffix in trunk.LEG_SUFFIXES)):
            p = self.parent[name]
            rotation = start.local[name].slerp(end.local[name], blend)
            relative = matrix_at(rotation, start.root.lerp(end.root, blend) if p is None else Vector())
            world[name] = world[p] @ self.bind[name] @ relative if p else self.bind[name] @ relative
        for suffix in trunk.LEG_SUFFIXES:
            self.knees(suffix, world, start, end, blend)
        if blend == 0 or blend == 1:
            endpoint = start if blend == 0 else end
            world = {name: endpoint.matrix[name] for name in world}
        for name, matrix in world.items():
            p = self.parent[name]
            basis = world[p] @ self.bind[name] if p else self.bind[name]
            local = basis.inverted() @ matrix
            q = local.to_quaternion()
            if name in previous and q.dot(previous[name]) < 0:
                q.negate()
            previous[name] = q.copy()
            bone = self.rig.pose.bones[name]
            bone.rotation_quaternion = q
            bone.keyframe_insert("rotation_quaternion", frame=frame, group=name)
            if p is None:
                bone.location = local.translation
                bone.keyframe_insert("location", frame=frame, group=name)
        for (name, prop), value in start.controls.items():
            bone = self.rig.pose.bones[name]
            bone[prop] = value + blend * (end.controls[name, prop] - value)
            bone.keyframe_insert(f'["{prop}"]', frame=frame, group=name)


def shift(action, offset):
    for curve in action.fcurves:
        for key in curve.keyframe_points:
            key.co.x += offset
            key.handle_left.x += offset
            key.handle_right.x += offset
        curve.update()
    for marker in action.pose_markers:
        marker.frame += offset
    action.frame_end += offset


def align_rotations(action, at, before, before_at):
    paths = {curve.data_path for curve in action.fcurves if curve.data_path.endswith(".rotation_quaternion")}
    for path in paths:
        after = [action.fcurves.find(path, index=i) for i in range(4)]
        preceding = [before.fcurves.find(path, index=i) for i in range(4)]
        if sum(a.evaluate(at) * b.evaluate(before_at) for a, b in zip(after, preceding)) >= 0:
            continue
        for curve in after:
            for key in curve.keyframe_points:
                if key.co.x >= at:
                    key.co.y *= -1
                    key.handle_left.y *= -1
                    key.handle_right.y *= -1
            curve.update()


def prep_clear(scene, rig, prep):
    minimum = float("inf")
    toes = None
    slide = 0.0
    for i in range(prep * 4 + 1):
        scene.frame_set(i // 4 + 1, subframe=(i % 4) / 4)
        b = rig.pose.bones
        minimum = min(minimum, b["Claw.R"].tail.z, b["Claw.L"].tail.z)
        pose_toes = [item.tail.copy() for item in b if item.name.startswith("Tip")]
        toes = pose_toes if toes is None else toes
        slide = max(slide, *((a-b).length for a, b in zip(pose_toes, toes)))
    return minimum, slide


def create_transition(scene, rig, blend, start, end, cut, name, waypoint):
    for poses in ((start, end), (start, waypoint, end)):
        segments = [interval(a, b) for a, b in zip(poses, poses[1:])]
        prep = sum(segments)
        action = cut.copy()
        action.name = name
        shift(action, prep)
        action.frame_start = 1
        action.pose_markers.new("Cut starts").frame = 1 + prep
        action["prep_frames"] = prep
        activate(rig, action)
        previous = {}
        offset = 0
        try:
            for a, b, frames in zip(poses, poses[1:], segments):
                for index in range(frames * 4):
                    blend.key(a, b, smooth(index / (frames * 4)), 1 + offset + index / 4, previous)
                offset += frames
        except ValueError as error:
            bpy.data.actions.remove(action)
            if "planted leg cannot reach" not in str(error):
                raise
            continue
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
        if prep:
            align_rotations(action, 1 + prep, action, 0.75 + prep)
        minimum, slide = prep_clear(scene, rig, prep)
        if minimum >= 0.013 and slide < 0.003:
            action.use_fake_user = True
            return action, prep, slide, len(segments) > 1
        bpy.data.actions.remove(action)
    raise ValueError(f"{name} has no planted preparation with both claws clear of the floor.")


def chain(first, transition, name):
    action = transition.copy()
    action.name = name
    duration = int(first.frame_end) - 1
    shift(action, duration)
    align_rotations(action, 1 + duration, first, first.frame_end)
    for original in first.fcurves:
        curve = action.fcurves.find(original.data_path, index=original.array_index)
        for key in original.keyframe_points:
            if key.co.x < first.frame_end:
                created = curve.keyframe_points.insert(*key.co, options={"FAST"})
                created.interpolation = "LINEAR"
        curve.update()
    action.pose_markers.new("First target").frame = first.pose_markers["Target"].frame
    action.pose_markers.new("Previous swing ends").frame = int(first.frame_end)
    action.frame_start = 1
    action.use_fake_user = True
    return action


def run(scene, rig):
    cuts = cut_targets(scene, rig)
    blend = PoseBlend(rig)
    starts = {
        "Low guard": read_pose(scene, rig, bpy.data.actions["03 Overhead Cleave"], 1),
        "Drawn claw": read_pose(scene, rig, bpy.data.actions["01 Whip Sweep"], 1),
        **{f"After {name.lower()}": read_pose(scene, rig, action, int(action.frame_end))
           for name, action in cuts.items()},
    }
    ends = {name: read_pose(scene, rig, action, 1) for name, action in cuts.items()}
    prepared, rows = {}, []
    for i, start_name in enumerate(POSE_NAMES, 1):
        for j, cut_name in enumerate(CUT_NAMES, 1):
            name = f"S{i}{j} {start_name} to {cut_name}"
            a, prep, drift, waypoint = create_transition(scene, rig, blend, starts[start_name], ends[cut_name],
                                                          cuts[cut_name], name, starts["Low guard"])
            speed = measured(scene, rig, a)
            scene.frame_set(1)
            assert max((bone.matrix.translation - starts[start_name].matrix[bone.name].translation).length
                       for bone in rig.pose.bones) < 0.00003
            assert all(abs(bone.matrix.to_quaternion().dot(starts[start_name].matrix[bone.name].to_quaternion())) > 0.99999
                       for bone in rig.pose.bones)
            a["start_label"], a["cut_label"], a["sequence_kind"] = start_name, cut_name, "transition"
            a["measured_contact_m_s"] = speed
            prepared[start_name, cut_name] = a
            row = dict(start=start_name, target=cut_name, preparation_s=round(prep / 60, 3),
                       time_to_target_s=round((a.pose_markers["Target"].frame - 1) / 60, 3),
                       contact_m_s=round(speed, 3), toe_drift_mm=round(drift * 1000, 3),
                       guard_waypoint=waypoint,
                       start_wrist_x_m=round(starts[start_name].matrix["Claw.R"].translation.x, 3),
                       start_wrist_y_m=round(starts[start_name].matrix["Claw.R"].translation.y, 3),
                       start_wrist_z_m=round(starts[start_name].matrix["Claw.R"].translation.z, 3),
                       target_x_m=round(a["target_m"][0], 3), target_y_m=round(a["target_m"][1], 3),
                       target_z_m=round(a["target_m"][2], 3),
                       plane_tilt_deg=round(degrees(acos(max(-1, min(1, a["normal"][2])))), 2),
                       target_direction_z=round(a["direction"][2], 3))
            rows.append(row)
            print(row, flush=True)
    for i, (first, second) in enumerate((("Horizontal", "Vertical"), ("Vertical", "Diagonal"),
                                        ("Diagonal", "Horizontal")), 1):
        a = chain(cuts[first], prepared[f"After {first.lower()}", second], f"C{i} {first} then {second}")
        a["sequence_kind"], a["previous_cut"] = "chain", first
        measured(scene, rig, a)
    pick = prepared["After horizontal", "Vertical"]
    activate(rig, pick)
    scene.frame_start, scene.frame_end = 1, int(pick.frame_end)
    scene.frame_set(1)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    output = args.output.resolve()
    if output == Path(bpy.data.filepath).resolve():
        raise ValueError("Choose an output separate from the input rig.")
    scene = bpy.context.scene
    if scene.render.fps != 60 or scene.render.fps_base != 1:
        raise ValueError("The rig needs an exact 60 fps scene.")
    rows = run(scene, bpy.data.objects["Galva"])
    with output.with_suffix(".csv").open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(output), compress=True)


if __name__ == "__main__":
    main()
