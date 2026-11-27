"""Generate a fixed-body analytic cut. Run inside Blender with an existing Galva rig open."""

import argparse
from math import acos, atan2, ceil, isfinite, pi, radians
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Euler, Matrix, Quaternion, Vector


X, Y, Z = (Vector(axis) for axis in ((1, 0, 0), (0, 1, 0), (0, 0, 1)))
NAMES = ("UpperArm.R", "Forearm.R", "Claw.R")
CHANNELS = (
    (NAMES[0], "Shoulder swing X"), (NAMES[0], "Shoulder twist Y"),
    (NAMES[0], "Shoulder lift Z"), (NAMES[1], "Forearm twist"),
    (NAMES[1], "Elbow bend"), (NAMES[2], "Wrist bend"),
)
LOW = np.radians([-90, -90, -90, -85, 0, -85])
HIGH = np.radians([90, 90, 90, 85, 155, 85])
SHOULDER_ZERO = np.radians([22, -15, 0])
TWIST_ZERO = radians(125)
ACTION = "06 Analytic Target Sweep"


def radians_wrap(x):
    return (x + pi) % (2 * pi) - pi


def signed_angle(a, b, axis):
    return atan2(axis.dot(a.cross(b)), a.dot(b))


def rotation(x, y, z):
    return Matrix((x, y, z)).transposed().to_quaternion()


class Arm:
    def __init__(self, rig):
        rest = {bone.name: bone.matrix_local for bone in rig.data.bones}
        upper, elbow, claw = NAMES
        self.shoulder_base = rig.pose.bones["Chest"].matrix.copy() @ rest["Chest"].inverted() @ rest[upper]
        self.origin = self.shoulder_base.translation
        self.elbow_base = rest[upper].inverted() @ rest[elbow]
        self.wrist_base = rest[elbow].inverted() @ rest[claw]
        self.a = self.elbow_base.translation.length
        self.b = self.wrist_base.translation.length
        self.length = rig.pose.bones[claw].bone.length
        if (min(self.a, self.b, self.length) < 1e-6
                or self.elbow_base.translation.normalized().dot(Y) < 0.99999
                or self.wrist_base.translation.normalized().dot(Y) < 0.99999
                or (self.elbow_base.to_quaternion() @ Y).dot(Y) < 0.99999
                or abs((self.wrist_base.to_quaternion() @ Z).dot(Y)) > 0.00001):
            raise ValueError("The rest axes do not match the Galva arm's two-link geometry.")

    def forward(self, q):
        sx, sy, sz = q[:3] + SHOULDER_ZERO
        twist, bend = -(q[3] + TWIST_ZERO), -q[4]
        shoulder = self.shoulder_base @ Euler((sx, sy, sz), "YXZ").to_matrix().to_4x4()
        elbow = shoulder @ self.elbow_base @ Euler((0, twist, bend), "YXZ").to_matrix().to_4x4()
        wrist = elbow @ self.wrist_base @ Quaternion(Z, q[5]).to_matrix().to_4x4()
        return shoulder, elbow, wrist

    def inverse(self, tip, blade):
        wrist = tip - (blade @ Y) * self.length
        normal = blade @ Z
        span = wrist - self.origin
        dist = span.length
        projected = span - normal * span.dot(normal)
        if projected.length < 1e-7 or dist > self.a + self.b or dist < abs(self.a - self.b):
            return
        # Forearm directions lie in the plane perpendicular to the wrist hinge.
        # Intersect this plane with the two arm-length spheres.
        axis = projected.normalized()
        across = normal.cross(axis)
        along = (dist * dist + self.b * self.b - self.a * self.a) / (2 * projected.length)
        depth_squared = self.b * self.b - along * along
        if depth_squared <= 1e-10:
            return
        for branch in (-1, 1):
            elbow = wrist - (axis * along + across * (branch * depth_squared ** 0.5))
            u, v = (elbow - self.origin).normalized(), (wrist - elbow).normalized()
            hinge = -u.cross(v)
            if hinge.length < 1e-7:
                continue
            hinge.normalize()
            bend = acos(max(-1, min(1, u.dot(v))))
            zero_elbow = rotation(u.cross(hinge), u, hinge)
            shoulder = zero_elbow @ self.elbow_base.to_quaternion().inverted()
            flexed = zero_elbow @ Quaternion(Z, -bend)
            wrist_rest = self.wrist_base.to_quaternion()
            raw_twist = signed_angle(flexed @ wrist_rest @ Z, normal, v)
            flange = flexed @ Quaternion(Y, raw_twist) @ wrist_rest
            fold = signed_angle(flange @ Y, blade @ Y, normal)
            twist = radians_wrap(-raw_twist - TWIST_ZERO)
            x, y, z = (self.shoulder_base.to_quaternion().inverted() @ shoulder).to_euler("YXZ")
            for chart, raw in enumerate(((x, y, z), (pi - x, y + pi, z + pi))):
                q = np.r_[radians_wrap(np.array(raw) - SHOULDER_ZERO), twist, bend, fold]
                if np.all(q >= LOW - 1e-6) and np.all(q <= HIGH + 1e-6):
                    yield (branch, chart), q

    def jacobian(self, q):
        shoulder, elbow, wrist = self.forward(q)
        sx, _, sz = q[:3] + SHOULDER_ZERO
        base = self.shoulder_base.to_quaternion()
        z_rotation = Quaternion(Z, sz)
        eb = (shoulder @ self.elbow_base).to_quaternion()
        axes = (
            base @ z_rotation @ X,
            base @ z_rotation @ Quaternion(X, sx) @ Y,
            base @ Z,
            -(eb @ Quaternion(Z, -q[4]) @ Y),
            -(eb @ Z),
            wrist.to_quaternion() @ Z,
        )
        tip = wrist @ Vector((0, self.length, 0))
        pivots = (self.origin,) * 3 + (elbow.translation,) * 2 + (wrist.translation,)
        return np.array([tuple(axis.cross(tip - at)) + tuple(axis) for axis, at in zip(axes, pivots)]).T


def solve_path(arm, target, velocity, normal, radius, rake, speed_limits, runup, follow):
    radial = velocity.cross(normal).normalized()
    center = target - radial * radius
    distances = np.r_[np.linspace(-runup, 0, 401), np.linspace(0, follow, 401)[1:]]
    def goal(distance):
        outward = Quaternion(normal, distance / radius) @ radial
        chord = Quaternion(normal, rake) @ outward
        blade_normal = -normal
        return center + outward * radius, rotation(chord.cross(blade_normal), chord, blade_normal)

    def candidates(distance):
        tip, blade = goal(distance)
        yield from arm.inverse(tip, blade)

    contact = []
    for branch, q in candidates(0):
        jacobian = arm.jacobian(q)
        if np.linalg.cond(jacobian) < 1e4:
            rate = np.linalg.solve(jacobian, np.r_[tuple(velocity), tuple(normal / radius)])
            contact.append((1 / max(abs(rate) / speed_limits), branch, q, rate))
    if not contact:
        raise ValueError("No nonsingular contact pose fits the target and joint ranges.")

    paths = []
    for local_max, branch, _, rate in contact:
        poses = [dict(candidates(s)).get(branch) for s in distances]
        if any(q is None for q in poses):
            start = next((i + 1 for i in range(399, -1, -1) if poses[i] is None), 0)
            end = next((i - 1 for i in range(401, len(poses)) if poses[i] is None), len(poses) - 1)
            print(f"Branch {branch}: available arc {distances[start]:.3f} to {distances[end]:.3f} m", flush=True)
            continue
        poses = np.array(poses)
        # Check the closed-form pose derivative against the independent geometric Jacobian.
        if np.max(abs((poses[401] - poses[399]) / (distances[401] - distances[399]) - rate)) > 0.025:
            raise ValueError("Analytic speed and inverse-pose derivative disagree.")
        caps = []
        for s, q in zip(distances, poses):
            outward = Quaternion(normal, s / radius) @ radial
            tangent = normal.cross(outward)
            j = arm.jacobian(q)
            if np.linalg.cond(j) > 1e4:
                break
            derivative = np.linalg.solve(j, np.r_[tuple(tangent), tuple(normal / radius)])
            caps.append(1 / max(abs(derivative) / speed_limits))
        if len(caps) == len(distances):
            paths.append((local_max, branch, rate, np.array(caps), goal, distances))
    if not paths:
        raise ValueError("A contact pose exists, but this arc leaves its continuous joint range. Try a shorter arc.")
    return paths


def time_path(distances, caps, acceleration):
    squared = (0.998 * caps) ** 2
    squared[0] = squared[-1] = 0
    for i, ds in enumerate(np.diff(distances), 1):
        squared[i] = min(squared[i], squared[i - 1] + 2 * acceleration * ds)
    for i in range(len(distances) - 2, -1, -1):
        squared[i] = min(squared[i], squared[i + 1] + 2 * acceleration * (distances[i + 1] - distances[i]))
    speed = np.sqrt(squared)
    intervals = 2 * np.diff(distances) / (speed[1:] + speed[:-1])
    return speed, np.r_[0, np.cumsum(intervals)]


def bake(scene, rig, args):
    source = bpy.data.actions["01 Whip Sweep"]
    rig.animation_data.action = source
    rig.animation_data.action_slot = source.slots[0]
    scene.frame_set(27)
    body = {bone.name: (bone.location.copy(), bone.rotation_quaternion.copy())
            for bone in rig.pose.bones if bone.name not in NAMES and bone.rotation_mode == "QUATERNION"}
    other_arm = {bone.name: {name: float(bone[name]) for name in bone.keys() if isinstance(bone[name], float)}
                 for bone in rig.pose.bones if bone.name in ("UpperArm.L", "Forearm.L", "Claw.L")}
    arm = Arm(rig)
    normal, direction = Vector(args.normal), Vector(args.direction)
    if normal.length < 1e-6 or direction.length < 1e-6:
        raise ValueError("Provide nonzero normal and direction vectors.")
    normal.normalize()
    direction.normalize()
    if abs(normal.dot(direction)) > 1e-5:
        raise ValueError("The direction must lie in the cutting plane.")
    target = Vector(args.point) if args.point else arm.origin + Vector((0.66, 1.19, 0))
    limits = np.radians(args.rates)
    if min(limits) <= 0 or min(args.radius, args.acceleration, args.runup, args.follow) <= 0:
        raise ValueError("Radius, distances, acceleration and rate bounds must be positive.")
    paths = solve_path(arm, target, direction, normal, args.radius, radians(args.rake), limits, args.runup, args.follow)
    profiled = []
    for local_max, branch, rate, caps, goal, distances in paths:
        speed, times = time_path(distances, caps, args.acceleration)
        profiled.append((speed[400], local_max, branch, rate, speed, times, goal, distances))
    attained, local_max, branch, rate, speed, times, goal, distances = max(profiled, key=lambda item: item[0])
    contact_frame = 1 + ceil(times[400] * 60)
    end = contact_frame + ceil((times[-1] - times[400]) * 60)
    old = bpy.data.actions.get(ACTION)
    if old:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(ACTION)
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots.new("OBJECT", rig.name)

    def distance_at(t):
        if t <= 0:
            return distances[0]
        if t >= times[-1]:
            return distances[-1]
        i = np.searchsorted(times, t) - 1
        width = times[i + 1] - times[i]
        dt = t - times[i]
        a = (speed[i + 1] - speed[i]) / width
        return distances[i] + speed[i] * dt + 0.5 * a * dt * dt

    last = None
    previous_frame = None
    worst_rate = 0
    for frame in np.linspace(1, end, (end - 1) * 4 + 1):
        s = distance_at(times[400] + (frame - contact_frame) / 60)
        tip, blade = goal(s)
        q = dict(arm.inverse(tip, blade))[branch]
        if last is not None:
            worst_rate = max(worst_rate, max(abs(q - last) / limits) * 60 / (frame - previous_frame))
        last, previous_frame = q, frame
        _, _, wrist = arm.forward(q)
        reconstructed = wrist @ Vector((0, arm.length, 0))
        if (reconstructed - tip).length > 0.00002 or abs(wrist.to_quaternion().dot(blade)) < 0.99999:
            raise ValueError("Inverse pose failed reconstruction.")
        for (name, prop), value in zip(CHANNELS, np.degrees(q)):
            bone = rig.pose.bones[name]
            bone[prop] = float(value)
            bone.keyframe_insert(f'["{prop}"]', frame=frame, group=name)
    if worst_rate > 1.0005:
        raise ValueError(f"Baked angle rate exceeds the bound: {worst_rate:.5f}")
    for frame in (1, end):
        for name, (location, quaternion) in body.items():
            bone = rig.pose.bones[name]
            bone.rotation_quaternion = quaternion
            bone.keyframe_insert("rotation_quaternion", frame=frame, group=name)
            if bone.parent is None:
                bone.location = location
                bone.keyframe_insert("location", frame=frame, group=name)
        for name, values in other_arm.items():
            for prop, value in values.items():
                bone = rig.pose.bones[name]
                bone[prop] = value
                bone.keyframe_insert(f'["{prop}"]', frame=frame, group=name)
    for curve in action.fcurves:
        for key in curve.keyframe_points:
            key.interpolation = "LINEAR"
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start, action.frame_end = 1, end
    action.pose_markers.new("Target").frame = contact_frame
    action["target_m"] = list(target)
    action["direction"] = list(direction)
    action["normal"] = list(normal)
    action["radius_m"] = args.radius
    action["rake_deg"] = args.rake
    action["path_acceleration_m_s2"] = args.acceleration
    action["runup_m"] = args.runup
    action["follow_m"] = args.follow
    action["local_max_m_s"] = float(local_max)
    action["contact_m_s"] = float(attained)
    action["rate_limits_deg_s"] = args.rates
    scene.frame_start, scene.frame_end = 1, end
    scene.frame_set(contact_frame)
    actual = rig.pose.bones[NAMES[-1]].tail
    if (actual - target).length > 0.00003:
        raise ValueError("Evaluated Blender pose misses target.")
    print("Target", list(target), "frame", contact_frame, "end", end,
          "contact speed", attained, "instantaneous limit", local_max,
          "joint rates deg/s", np.degrees(rate * attained), "max baked rate ratio", worst_rate, flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--point", nargs=3, type=float, help="Blade tip target in armature-local meters.")
    parser.add_argument("--direction", nargs=3, type=float, default=[-0.8660254, 0.5, 0.0])
    parser.add_argument("--normal", nargs=3, type=float, default=[0, 0, 1])
    parser.add_argument("--radius", type=float, default=1.25)
    parser.add_argument("--rake", type=float, default=15.0, help="Blade chord angle in the cutting plane, in degrees.")
    parser.add_argument("--rates", nargs=6, type=float, default=[345, 240, 280, 320, 380, 500])
    parser.add_argument("--runup", type=float, default=0.88)
    parser.add_argument("--follow", type=float, default=0.83)
    parser.add_argument("--acceleration", type=float, default=65.0, help="Path acceleration in m/s².")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    options = [*(args.point or []), *args.direction, *args.normal, *args.rates, args.radius,
               args.rake, args.runup, args.follow, args.acceleration]
    if not all(map(isfinite, options)):
        raise ValueError("All numeric inputs must be finite.")
    rig = bpy.data.objects["Galva"]
    scene = bpy.context.scene
    if scene.render.fps != 60 or scene.render.fps_base != 1:
        raise ValueError("The rig needs an exact 60 fps scene.")
    if args.output.resolve() == Path(bpy.data.filepath).resolve():
        raise ValueError("Choose an output separate from the input rig.")
    bake(scene, rig, args)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(args.output.resolve()))


if __name__ == "__main__":
    main()
