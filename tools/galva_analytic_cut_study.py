"""Compare arm-only and trunk-assisted analytic cuts across several Blender targets."""

import argparse
import csv
from dataclasses import dataclass
from math import acos, degrees, radians
from pathlib import Path
import sys
from types import SimpleNamespace

import bpy
import numpy as np
from mathutils import Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import galva_analytic_cut as arm
import galva_analytic_trunk_cut as trunk


@dataclass(frozen=True)
class Case:
    name: str
    x_tilt: float = 0
    y_tilt: float = 0
    yaw: float = 0
    reach: float = 1
    rake: float = 15
    runup: float = 0.58
    follow: float = 0.58
    acceleration: float = 65
    heading: float = 0


CASES = (
    Case("Horizontal", runup=0.88, follow=0.83),
    Case("High diagonal", x_tilt=26, runup=0.65, follow=0.67),
    Case("Low diagonal", x_tilt=-20, reach=0.96, runup=0.60, follow=0.60),
    Case("Left offset", x_tilt=8, yaw=23, rake=18, runup=0.63, follow=0.51),
    Case("Right offset", yaw=-25, rake=12, runup=0.55, follow=0.65),
    Case("Compound tilt", x_tilt=19, y_tilt=-18, yaw=12, reach=0.94, rake=27, runup=0.56, follow=0.62),
    Case("Steep diagonal", x_tilt=38, y_tilt=-14, yaw=10, rake=13, runup=0.42, follow=0.48),
    Case("Same point new heading", rake=17, heading=10, runup=0.55, follow=0.55),
    Case("Higher acceleration", acceleration=110, runup=0.88, follow=0.83),
    Case("Short joint margin", x_tilt=4, y_tilt=18, yaw=-19, reach=1.04, rake=8, runup=0.65, follow=0.58),
    Case("Beyond reach", reach=1.52, runup=0.55, follow=0.55),
    Case("New heading shorter runup", rake=17, heading=10, runup=0.43, follow=0.55),
)
RATES = [345, 240, 280, 320, 380, 500]


def activate(rig, action):
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots[0]


def measured(scene, rig, action):
    activate(rig, action)
    marker = action.pose_markers["Target"].frame
    tip = rig.pose.bones["Claw.R"]
    samples = []
    for frame, subframe in ((marker - 1, 15 / 16), (marker, 0), (marker, 1 / 16)):
        scene.frame_set(frame, subframe=subframe)
        samples.append(tip.tail.copy())
    speed = (samples[-1] - samples[0]) * 480
    error = (samples[1] - Vector(action["target_m"])).length
    assert error < 0.00002 and abs(speed.length - action["contact_m_s"]) < 0.045, (
        action.name, error, speed.length, action["contact_m_s"],
    )
    assert speed.normalized().dot(Vector(action["direction"])) > 0.999
    normal = Vector(action["normal"])
    toe_start = None
    drift = 0.0
    blade_error = 0.0
    for frame in range(1, int(action.frame_end) + 1):
        scene.frame_set(frame)
        blade_error = max(blade_error, (tip.matrix.to_quaternion() @ arm.Z + normal).length)
        toes = [b.tail.copy() for b in rig.pose.bones if b.name.startswith("Tip")]
        if toe_start is None:
            toe_start = toes
        drift = max(drift, *((a-b).length for a, b in zip(toes, toe_start)))
    assert len(toe_start) == 4 and drift < 0.0002 and blade_error < 0.0003, (action.name, drift, blade_error)
    return speed.length


def inputs(rig, scene, case):
    activate(rig, bpy.data.actions["01 Whip Sweep"])
    scene.frame_set(27)
    armature = arm.Arm(rig)
    tilt = Quaternion(arm.Z, radians(case.yaw)) @ Quaternion(arm.Y, radians(case.y_tilt)) @ Quaternion(arm.X, radians(case.x_tilt))
    normal = tilt @ arm.Z
    direction = Quaternion(normal, radians(case.heading)) @ (tilt @ Vector((-0.8660254, 0.5, 0)))
    point = armature.origin + tilt @ (case.reach * Vector((0.66, 1.19, 0)))
    return SimpleNamespace(point=point, direction=direction, normal=normal, rates=RATES,
                           radius=1.25, rake=case.rake, runup=case.runup, follow=case.follow,
                           acceleration=case.acceleration)


def probe_trunk_reach(scene, rig, args):
    source = {"target_m": list(args.point), "direction": list(args.direction.normalized()),
              "normal": list(args.normal.normalized()), "radius_m": args.radius, "rake_deg": args.rake,
              "path_acceleration_m_s2": args.acceleration, "rate_limits_deg_s": args.rates}
    reference = bpy.data.actions["01 Whip Sweep"]
    trial = trunk.Trial(scene, rig, source, anchor=(reference, 27))
    yaw_pairs = ((0, 0), (6, 8), (-6, -8), (12, 14), (-12, -14), (9, -8), (-9, 8))
    positions = np.r_[np.linspace(-args.runup, 0, 17), np.linspace(0, args.follow, 17)[1:]]
    contenders = []
    for a, b in yaw_pairs:
        angles = np.radians([a, b])
        trial.center_yaw = angles
        trial.trunk(angles)
        point, blade, _ = trial.goal(0)
        branches = [branch for branch, _ in trial.arm.inverse(point, blade)]
        for branch in branches:
            for slopes in (np.array([0.07, 0.09]), np.array([0.19, 0.21]), np.array([0.31, 0.33])):
                result = trunk.evaluate(trial, positions, slopes, 0.8, branch, args.acceleration)
                if result is not None:
                    contenders.append((result[0][16], a, b))
    ordered = sorted(contenders, key=lambda item: (item[0], -abs(item[1]) - abs(item[2])), reverse=True)
    for pair in dict.fromkeys((a, b) for _, a, b in ordered):
        trial.center_yaw = np.radians(pair)
        try:
            result, local_cap = trunk.optimize(trial, args.runup, args.follow, args.acceleration)
        except ValueError as error:
            if not str(error).startswith(("No tested abdomen/chest yaw path", "The shortlisted torso paths fail")):
                raise
            continue
        trunk.bake(trial, result, local_cap, args.acceleration)
        return bpy.data.actions[trunk.TRUNK_ACTION]
    return None


def record_trunk(action, row, scene, rig, index, case):
    row.update(trunk_status="OK", trunk_m_s=round(measured(scene, rig, action), 3))
    if row["arm_m_s"] != "":
        row["gain_percent"] = round(100 * (row["trunk_m_s"] / row["arm_m_s"] - 1), 2)
    row["torso_yaw_at_target_deg"] = "/".join(f"{v:g}" for v in action["target_trunk_yaw_deg"])
    action.name = f"{index:02} T {case.name}"
    action["study_case"] = case.name
    action["study_kind"] = "trunk"


def run(scene, rig):
    rows = []
    successful = []
    for index, case in enumerate(CASES, 1):
        args = inputs(rig, scene, case)
        row = {"case": case.name, **{key: round(float(v), 6) for keys, vector in (
            (("x_m", "y_m", "z_m"), args.point), (("direction_x", "direction_y", "direction_z"), args.direction),
            (("normal_x", "normal_y", "normal_z"), args.normal),
        ) for key, v in zip(keys, vector)},
            "plane_tilt_deg": round(degrees(acos(max(-1, min(1, args.normal.z)))), 2),
            "heading_offset_deg": case.heading, "blade_rake_deg": case.rake, "runup_m": case.runup,
            "follow_m": case.follow, "acceleration_m_s2": case.acceleration,
            "arm_m_s": "", "trunk_m_s": "", "gain_percent": "", "torso_yaw_at_target_deg": "",
            "arm_status": "", "trunk_status": ""}
        rows.append(row)
        try:
            arm.bake(scene, rig, args)
        except ValueError as error:
            message = str(error)
            if not message.startswith(("No nonsingular contact pose", "A contact pose exists")):
                raise
            row["arm_status"] = message
            moving = probe_trunk_reach(scene, rig, args)
            if moving is None:
                row["trunk_status"] = "No full path among the tested torso yaw offsets"
            else:
                record_trunk(moving, row, scene, rig, index, case)
                successful.append(moving)
            print(f"CASE {case.name}: {message} Trunk: {row['trunk_status']} {row['trunk_m_s']}", flush=True)
            continue
        fixed = bpy.data.actions[arm.ACTION]
        row.update(arm_status="OK", arm_m_s=round(measured(scene, rig, fixed), 3))
        experiment = trunk.Trial(scene, rig, fixed)
        try:
            result, local_cap = trunk.optimize(experiment, case.runup, case.follow, case.acceleration)
        except ValueError as error:
            if not str(error).startswith(("No tested abdomen/chest yaw path", "The shortlisted torso paths fail")):
                raise
            row["trunk_status"] = str(error)
        else:
            trunk.bake(experiment, result, local_cap, case.acceleration)
            moving = bpy.data.actions[trunk.TRUNK_ACTION]
            record_trunk(moving, row, scene, rig, index, case)
            successful.append(moving)
        fixed.name = f"{index:02} A {case.name}"
        fixed["study_case"] = case.name
        fixed["study_kind"] = "arm"
        successful.append(fixed)
        print(f"CASE {case.name}: {row['arm_m_s']} -> {row['trunk_m_s']} m/s", flush=True)
    if not successful:
        raise ValueError("The study did not generate any action.")
    default = max((a for a in successful if a["study_kind"] == "trunk"),
                  key=lambda a: a["contact_m_s"], default=successful[0])
    activate(rig, default)
    scene.frame_start, scene.frame_end = 1, max(int(a.frame_end) for a in successful)
    scene.frame_set(default.pose_markers["Target"].frame)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    output = args.output.resolve()
    if output == Path(bpy.data.filepath).resolve():
        raise ValueError("Choose an output separate from the input rig.")
    if bpy.context.scene.render.fps != 60 or bpy.context.scene.render.fps_base != 1:
        raise ValueError("The rig needs an exact 60 fps scene.")
    rows = run(bpy.context.scene, bpy.data.objects["Galva"])
    with output.with_suffix(".csv").open("w", encoding="utf-8-sig", newline="") as file:
        writer = csv.DictWriter(file, rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(output))


if __name__ == "__main__":
    main()
