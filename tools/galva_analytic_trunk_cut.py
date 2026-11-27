"""Add abdomen and chest yaw to an existing Galva analytic-cut Blender action."""

import argparse
from itertools import combinations, product
from math import ceil, isfinite, radians, tanh
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from galva_analytic_cut import ACTION, Arm, CHANNELS, Y, Z, rotation, time_path


TRUNK_ACTION = "07 Analytic Trunk Sweep"
TRUNK_RANGE = np.radians([24.0, 35.0])
TRUNK_RATES = np.radians([160.0, 200.0])
LEG_SUFFIXES = ("Front.L", "Front.R", "Hind.L", "Hind.R")


def with_rotation(matrix, turn):
    result = (turn @ matrix.to_quaternion()).to_matrix().to_4x4()
    result.translation = matrix.translation
    return result


class Trial:
    def __init__(self, scene, rig, source, anchor=None):
        self.scene, self.rig = scene, rig
        reference, frame = anchor if anchor is not None else (source, source.pose_markers["Target"].frame)
        rig.animation_data.action = reference
        rig.animation_data.action_slot = reference.slots[0]
        scene.frame_set(frame)
        self.source = source
        self.center_yaw = np.zeros(2)
        self.rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
        self.held = {b.name: b.matrix.copy() for b in rig.pose.bones}
        self.other_arm = {b.name: {key: b[key] for key in b.keys() if isinstance(b[key], float)}
                          for b in rig.pose.bones if b.name in ("UpperArm.L", "Forearm.L", "Claw.L")}
        self.arm = Arm(rig)
        self.shoulder_rest = self.rest["Chest"].inverted() @ self.rest["UpperArm.R"]
        self.target, self.direction, self.normal = (Vector(source[key]) for key in ("target_m", "direction", "normal"))
        self.radius = source["radius_m"]
        self.rake = radians(source["rake_deg"])
        self.acceleration = source["path_acceleration_m_s2"]
        self.arm_rates = np.radians(source["rate_limits_deg_s"])
        self.radial = self.direction.cross(self.normal).normalized()
        self.center = self.target - self.radius * self.radial

    def goal(self, s):
        outward = Quaternion(self.normal, s / self.radius) @ self.radial
        chord = Quaternion(self.normal, self.rake) @ outward
        face = -self.normal
        return self.center + outward * self.radius, rotation(chord.cross(face), chord, face), self.normal.cross(outward)

    def trunk(self, angles):
        root = with_rotation(self.held["Thorax"], Quaternion(Z, angles[0]))
        delta = root @ self.held["Thorax"].inverted()
        chest = with_rotation(delta @ self.held["Chest"], Quaternion(Z, angles[1]))
        self.arm.shoulder_base = chest @ self.shoulder_rest
        self.arm.origin = self.arm.shoulder_base.translation
        return root, delta, chest

    def leg(self, suffix, delta):
        femur, shin, foot = ("Femur" + suffix, "Tibia" + suffix, "Tarsus" + suffix)
        a0, b0, c = (self.held[name].translation for name in (femur, shin, foot))
        a = delta @ a0
        upper, lower = (b0 - a0).length, (c - b0).length
        reach = c - a
        length = reach.length
        if not (abs(upper - lower) + 0.001 < length < upper + lower - 0.001):
            return None
        axis = reach.normalized()
        knee_hint = delta @ b0 - a
        pole = knee_hint - axis * knee_hint.dot(axis)
        if pole.length < 1e-5:
            return None
        along = (length * length + upper * upper - lower * lower) / (2 * length)
        knee = a + axis * along + pole.normalized() * (upper * upper - along * along) ** 0.5
        bones = {}
        for name, start, old_axis, new_axis in (
            (femur, a, b0 - a0, knee - a), (shin, knee, c - b0, c - knee),
        ):
            pose = with_rotation(self.held[name], old_axis.rotation_difference(new_axis))
            pose.translation = start
            bones[name] = pose
        return bones

    def at(self, s, slopes, width, branch, require_feet=True):
        phase = tanh(s / width)
        angles = self.center_yaw + width * phase * slopes
        deriv = (1 - phase * phase) * slopes
        if np.any(abs(angles) > TRUNK_RANGE):
            return None
        root, delta, chest = self.trunk(angles)
        if require_feet and any(self.leg(suffix, delta) is None for suffix in LEG_SUFFIXES):
            return None
        tip, blade, tangent = self.goal(s)
        q = dict(self.arm.inverse(tip, blade)).get(branch)
        if q is None:
            return None
        j = self.arm.jacobian(q)
        if np.linalg.cond(j) > 1e4:
            return None
        axis_rate = tuple(self.normal / self.radius - Z * sum(deriv))
        trunk_linear = sum((Z.cross(tip - body.translation) * rate for body, rate in zip((root, chest), deriv)), Vector())
        rates = np.linalg.solve(j, np.r_[tuple(tangent - trunk_linear), axis_rate])
        return q, angles, rates, deriv, root, delta, chest

    def pose(self, root, delta, chest):
        result = {name: matrix.copy() for name, matrix in self.held.items()}
        result["Thorax"], result["Chest"] = root, chest
        result["Head"] = chest @ self.held["Chest"].inverted() @ self.held["Head"]
        for suffix in LEG_SUFFIXES:
            result["Coxa" + suffix] = delta @ self.held["Coxa" + suffix]
            result.update(self.leg(suffix, delta))
        return result


def contact_limit(trial, branch):
    q, _, rates, _, root, _, chest = trial.at(0.0, np.zeros(2), 1.0, branch)
    jac = trial.arm.jacobian(q)
    torso = np.array([tuple(Z.cross(trial.target - b.translation)) + tuple(Z) for b in (root, chest)]).T
    offset = np.linalg.solve(jac, torso)
    rows = np.r_[np.column_stack((-offset, rates)), [[1, 0, 0], [0, 1, 0]]]
    bounds = np.r_[trial.arm_rates, TRUNK_RATES]
    matrix = np.r_[rows, -rows, [[0, 0, -1]]]
    bound = np.r_[bounds, bounds, 0]
    best = np.zeros(3)
    for ids in combinations(range(len(bound)), 3):
        a = matrix[list(ids)]
        if abs(np.linalg.det(a)) < 1e-8:
            continue
        x = np.linalg.solve(a, bound[list(ids)])
        if x[2] > best[2] and np.all(matrix @ x <= bound + 1e-7):
            best = x
    return best


def evaluate(trial, distances, slopes, width, branch, acceleration):
    caps = []
    for s in distances:
        sample = trial.at(s, slopes, width, branch)
        if sample is None:
            return None
        _, _, rates, deriv, *_ = sample
        caps.append(1 / max(np.max(abs(rates) / trial.arm_rates), np.max(abs(deriv) / TRUNK_RATES)))
    speeds, times = time_path(distances, np.array(caps), acceleration)
    index = len(distances) // 2
    return speeds, times, caps[index]


def optimize(trial, runup, follow, acceleration):
    def grid(samples):
        return np.r_[np.linspace(-runup, 0, samples + 1), np.linspace(0, follow, samples + 1)[1:]]

    coarse = grid(48)
    trial.trunk(trial.center_yaw)
    tip, blade, _ = trial.goal(0)
    branches = [key for key, _ in trial.arm.inverse(tip, blade)]
    results = []
    bounds = []
    for branch in branches:
        bounds.append(contact_limit(trial, branch))
        for a, b, width in product((0.07, 0.13, 0.19, 0.25, 0.31),
                                   (0.09, 0.15, 0.21, 0.27, 0.33), (0.55, 0.80, 1.15)):
            slopes = np.array([a, b])
            profile = evaluate(trial, coarse, slopes, width, branch, acceleration)
            if profile is not None:
                speeds, times, _ = profile
                results.append((speeds[48], -times[-1], -max(abs(slopes / TRUNK_RATES)), a, b, width, branch))
    if not results:
        raise ValueError("No tested abdomen/chest yaw path satisfies the arm and leg ranges.")

    fine = grid(400)
    finalists = []
    for _, _, _, a, b, width, branch in sorted(results, reverse=True)[:8]:
        slopes = np.array([a, b])
        profile = evaluate(trial, fine, slopes, width, branch, acceleration)
        if profile is not None:
            speed, times, cap = profile
            finalists.append((speed[400], -times[-1], fine, slopes, width, branch, speed, times, cap))
    if not finalists:
        raise ValueError("The shortlisted torso paths fail at the refined sampling rate.")
    return max(finalists, key=lambda item: item[:2]), max(bound[2] for bound in bounds)


def bake(trial, result, local_limit, acceleration):
    attained, _, distances, slopes, width, branch, speed, times, cap = result
    rig, scene = trial.rig, trial.scene
    zero = trial.at(0, slopes, width, branch)
    before = trial.at(-0.001, slopes, width, branch)
    after = trial.at(0.001, slopes, width, branch)
    if np.max(abs((after[0] - before[0]) / 0.002 - zero[2])) > 0.025:
        raise ValueError("The torso and arm Jacobians disagree with the analytic pose path.")
    old = bpy.data.actions.get(TRUNK_ACTION)
    if old:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(TRUNK_ACTION)
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots.new("OBJECT", rig.name)
    contact, end = 1 + ceil(times[400] * 60), 1 + ceil(times[400] * 60) + ceil((times[-1] - times[400]) * 60)
    previous = {}
    angles_baked = []
    last_angles = None
    max_rate_ratio = 0
    samples_per_frame = 16
    for frame in np.linspace(1, end, (end - 1) * samples_per_frame + 1):
        t = times[400] + (frame - contact) / 60
        if t <= 0 or t >= times[-1]:
            s = distances[0] if t <= 0 else distances[-1]
        else:
            i = np.searchsorted(times, t) - 1
            dt = t - times[i]
            gain = (speed[i + 1] - speed[i]) / (times[i + 1] - times[i])
            s = distances[i] + speed[i] * dt + 0.5 * gain * dt * dt
        q, angles, _, _, root, delta, chest = trial.at(s, slopes, width, branch)
        angles_baked.append(angles)
        combined = np.r_[q, angles]
        if last_angles is not None:
            max_rate_ratio = max(max_rate_ratio, max(abs(combined - last_angles) * 60 * samples_per_frame /
                                                     np.r_[trial.arm_rates, TRUNK_RATES]))
        last_angles = combined
        body = trial.pose(root, delta, chest)
        for bone in rig.pose.bones:
            if bone.rotation_mode != "QUATERNION":
                continue
            rest = trial.rest[bone.name]
            base = body[bone.parent.name] @ trial.rest[bone.parent.name].inverted() @ rest if bone.parent else rest
            local = base.inverted() @ body[bone.name]
            quat = local.to_quaternion()
            if bone.name in previous and quat.dot(previous[bone.name]) < 0:
                quat.negate()
            previous[bone.name] = quat.copy()
            bone.rotation_quaternion = quat
            bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
            if bone.parent is None:
                bone.location = local.translation
                bone.keyframe_insert("location", frame=frame, group=bone.name)
        for (name, key), angle in zip(CHANNELS, np.degrees(q)):
            bone = rig.pose.bones[name]
            bone[key] = float(angle)
            bone.keyframe_insert(f'["{key}"]', frame=frame, group=name)
    if max_rate_ratio > 1.0005:
        raise ValueError(f"Baked joint rate exceeds its cap by ratio {max_rate_ratio:.5f}.")
    for name, values in trial.other_arm.items():
        bone = rig.pose.bones[name]
        for key, value in values.items():
            bone[key] = value
            for frame in (1, end):
                bone.keyframe_insert(f'["{key}"]', frame=frame, group=name)
    for curve in action.fcurves:
        for key in curve.keyframe_points:
            key.interpolation = "LINEAR"
    action.use_fake_user = action.use_frame_range = True
    action.frame_start, action.frame_end = 1, end
    action.pose_markers.new("Target").frame = contact
    for key in ("target_m", "direction", "normal", "radius_m", "rake_deg", "rate_limits_deg_s"):
        value = trial.source[key]
        action[key] = list(value) if hasattr(value, "to_list") else value
    action["path_acceleration_m_s2"] = acceleration
    action["runup_m"] = float(-distances[0])
    action["follow_m"] = float(distances[-1])
    action["contact_m_s"] = float(attained)
    action["local_max_m_s"] = float(local_limit)
    action["chosen_path_cap_m_s"] = float(cap)
    action["trunk_rates_deg_s"] = list(np.degrees(TRUNK_RATES))
    action["target_trunk_yaw_deg"] = list(np.degrees(trial.center_yaw))
    scene.frame_start, scene.frame_end = 1, end
    scene.frame_set(contact)
    print("Contact", contact, "speed", attained, "instantaneous 8-DOF limit", local_limit,
          "selected path cap", cap, "max baked rate ratio", max_rate_ratio,
          "torso yaw range", np.ptp(np.degrees(angles_baked), axis=0), flush=True)


def main():
    args = argparse.ArgumentParser(description=__doc__)
    args.add_argument("--output", type=Path, required=True)
    args.add_argument("--runup", type=float)
    args.add_argument("--follow", type=float)
    args.add_argument("--acceleration", type=float)
    options = args.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if options.output.resolve() == Path(bpy.data.filepath).resolve():
        raise ValueError("Choose an output separate from the input rig.")
    scene, rig = bpy.context.scene, bpy.data.objects["Galva"]
    if scene.render.fps != 60 or scene.render.fps_base != 1:
        raise ValueError("The rig needs an exact 60 fps scene.")
    trial = Trial(scene, rig, bpy.data.actions[ACTION])
    acceleration = trial.acceleration if options.acceleration is None else options.acceleration
    runup = trial.source.get("runup_m", 0.88) if options.runup is None else options.runup
    follow = trial.source.get("follow_m", 0.83) if options.follow is None else options.follow
    if not all(isfinite(v) and v > 0 for v in (runup, follow, acceleration)):
        raise ValueError("Arc lengths and acceleration must be finite and positive.")
    result, local_limit = optimize(trial, runup, follow, acceleration)
    bake(trial, result, local_limit, acceleration)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(options.output.resolve()))


if __name__ == "__main__":
    main()
