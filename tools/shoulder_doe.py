"""Analyze v68 shoulder traces and sweep a reduced shoulder-girdle model."""
import csv
import itertools
import math
import random
import statistics as stats
from collections import defaultdict
from pathlib import Path

DATA = Path(__file__).resolve().parents[1] / "docs" / "arena" / "drive"
DT = 1 / 240
LEVELS = ((0.04, 0.10, 0.15), (4, 8, 12), (0.3, 0.8, 1.5), (25, 75, 150))


def load():
    trials = defaultdict(list)
    with (DATA / "shoulder_series.csv").open(newline="") as stream:
        for row in csv.DictReader(stream):
            group = row.pop("group")
            key = (group, int(row.pop("id")), int(row["yawLocked"]))
            trials[key].append({name: float(value) for name, value in row.items()})
    return trials


def swing(rows):
    return [r for r in rows if 0 <= r["t"] <= (r["hitTime"] - DT / 2 if r["hitTime"] >= 0 else 0.4)]


def contact(rows):
    return next((r for r in rows if r["contact"]), None)


def peak(rows, key):
    return max(rows, key=lambda r: abs(r[key]))


def mean(rows, key):
    return stats.mean(r[key] for r in rows)


def is_baseline(row):
    return all(math.isclose(row[key], value, abs_tol=1e-5) for key, value in
               {"torque": 70, "rom": 35, "hip": 0.45, "ff": 1}.items())


def shoulder_response(rows, travel, hz, mass, force_limit):
    """Incremental one-axis model driven by the unmodified hand target.

    The shoulder starts in equilibrium with the first command. The output
    includes a rigid carriage case and a hand PD cancellation case.
    """
    omega = 2 * math.pi * hz
    displacement = velocity = hand_offset = hand_speed = 0.0
    former_world_offset = initial_world_offset = 0.0
    result = []
    for i, row in enumerate(rows):
        demand = min(1.0, max(0.0, row["reachDemand"]))
        target = travel * demand * demand
        if i == 0:
            displacement = target
            former_world_offset = initial_world_offset = displacement * row["gripProjection"]
        world_offset = displacement * row["gripProjection"]
        carriage_speed = (world_offset - former_world_offset) / DT
        former_world_offset = world_offset
        result.append((displacement, carriage_speed, hand_speed + carriage_speed))

        acceleration = omega**2 * (target - displacement) - 1.6 * omega * velocity
        acceleration = max(-force_limit / mass, min(force_limit / mass, acceleration))
        velocity += DT * acceleration
        displacement += DT * velocity
        if displacement < 0.0 or displacement > travel:
            displacement = min(travel, max(0.0, displacement))
            velocity = 0.0

        # Linearized hand PD counteracts the displaced grip at fixed world target.
        # No arm collision, Hill force saturation, or reaction on the chest.
        hand_speed += DT * (-390.5 * (hand_offset + world_offset - initial_world_offset)
                            - 31.6 * (hand_speed + carriage_speed))
        hand_offset += DT * hand_speed
    return result


def design():
    for point in itertools.product(*LEVELS):
        yield "grid", point
    rng = random.Random(61122)
    n = 60
    axes = []
    for lo, _, hi in LEVELS:
        bins = list(range(n))
        rng.shuffle(bins)
        axes.append([lo + (hi - lo) * (i + rng.random()) / n for i in bins])
    for point in zip(*axes):
        yield "lhs", point


def run():
    trials = load()
    if len(trials) != 456:
        raise ValueError(f"Expected 456 traces, found {len(trials)}")
    baseline = [rows for (group, _, locked), rows in trials.items()
                if group == "grid" and not locked and is_baseline(rows[0])]
    print("Baseline (per seed, pre-contact peak and contact samples)")
    for rows in baseline:
        hit = contact(rows)
        print({key: round(hit[key], 5) for key in
               ("seed", "hitTime", "closing", "fistSpeed", "mass", "energy", "shoulder",
                "chest", "yaw", "tilt", "lever", "depth", "chestRate", "pelvisRate")})
        samples = swing(rows)
        print("peaks", {key: (round(peak(samples, key)[key], 4), round(peak(samples, key)["t"], 4))
                        for key in ("fistSpeed", "shoulder", "chestRate", "pelvisRate")})
        print("wider lever (+0.04 m) direct contact delta", 0.04 * abs(hit["chestRate"]))

    residual = max(abs(r["shoulder"] - r["chest"] - r["yaw"] - r["tilt"])
                   for rows in trials.values() for r in rows)
    if residual > 1e-6:
        raise ValueError(f"Shoulder decomposition failed: {residual}")
    print("Maximum shoulder decomposition error:", residual)
    for group in ("grid", "lhs", "holdout"):
        pairs = [(rows, trials[g, i, 1]) for (g, i, locked), rows in trials.items() if g == group and not locked]
        print(group, "pairs", len(pairs), "hits free/locked", [sum(contact(p[j]) is not None for p in pairs) for j in (0, 1)])
        for key in ("fistSpeed", "shoulder", "chest", "yaw"):
            effect = [max(r[key] for r in swing(a)) - max(r[key] for r in swing(b)) for a, b in pairs]
            print(key, "mean free-minus-locked peak", round(stats.mean(effect), 4))
        actual = [abs(peak(swing(a), "chestRate")["chestRate"]) for a, _ in pairs]
        predicted = [a[0]["trunkModelPeak"] for a, _ in pairs]
        errors = [p - a for p, a in zip(predicted, actual)]
        print("yaw model bias and RMSE", round(stats.mean(errors), 4), round(math.sqrt(stats.mean(e*e for e in errors)), 4))

    for key in ("torque", "rom", "hip", "ff"):
        groups = defaultdict(list)
        for (group, _, locked), rows in trials.items():
            if group == "grid" and not locked:
                groups[rows[0][key]].append(max(r["fistSpeed"] for r in swing(rows)))
        print("grid marginal fist peak", key, {k: round(stats.mean(v), 4) for k, v in sorted(groups.items())})

    for (group, _, locked), rows in trials.items():
        r = rows[0]
        if r["seed"] not in (1, 4) or (group == "grid" and locked):
            continue
        base_geometry = math.isclose(r["rom"], 35, abs_tol=1e-5) and math.isclose(r["hip"], 0.45, abs_tol=1e-5)
        if group == "holdout" or (group == "grid" and base_geometry and r["ff"] == 1):
            hit = contact(rows)
            print("candidate/contact", group, r["torque"], r["range"], locked,
                  {name: round(hit[name], 4) for name in ("fistSpeed", "fistForward", "shoulder", "chest",
                                                        "yaw", "tilt", "chestRate", "mass", "energy")})

    lhs = [rows for (group, _, locked), rows in trials.items() if group == "lhs" and not locked]
    print("LHS step/translation Pearson r:",
          round(stats.correlation([rows[0]["step"] for rows in lhs], [contact(rows)["chest"] for rows in lhs]), 4))

    path = DATA / "shoulder_girdle.csv"
    with path.open("w", newline="") as stream:
        writer = csv.writer(stream)
        writer.writerow(("design", "group", "trial", "yawLocked", "seed", "range",
                         "travel", "hz", "mass", "forceLimit", "peakTravel",
                         "shoulderPeakDelta", "fistPeakCarriageDelta", "fistPeakPdDelta",
                         "carriageAtContact", "pdAtContact"))
        for (group, i, locked), rows in trials.items():
            if group != "holdout" and not (group == "grid" and is_baseline(rows[0])):
                continue
            samples = swing(rows)
            included = {r["t"] for r in samples}
            hit = contact(rows)
            hit_index = rows.index(hit) if hit else None
            for design_name, point in itertools.chain([("fixed", (0., 8., 0.8, 75.))], design()):
                response = shoulder_response(rows, *point)
                active = [(r, d, v, pd) for r, (d, v, pd) in zip(rows, response) if r["t"] in included]
                change = max(r["shoulder"] + v for r, _, v, _ in active) - max(r["shoulder"] for r in samples)
                fist_peak = max(r["fistForward"] for r in samples)
                carriage_peak = max(r["fistForward"] + v for r, _, v, _ in active)
                pd_peak = max(r["fistForward"] + pd for r, _, _, pd in active)
                writer.writerow((design_name, group, i, locked, rows[0]["seed"], rows[0]["range"], *point,
                                 max(d for _, d, _, _ in active), change,
                                 carriage_peak - fist_peak, pd_peak - fist_peak,
                                 *(response[hit_index][1:] if hit_index is not None else ("", ""))))

    with path.open(newline="") as stream:
        sweep = list(csv.DictReader(stream))
    response_pairs = defaultdict(dict)
    for r in sweep:
        key = tuple(r[name] for name in ("group", "trial", "design", "travel", "hz", "mass", "forceLimit"))
        response_pairs[key][r["yawLocked"]] = float(r["shoulderPeakDelta"])
    for group in ("grid", "holdout"):
        interactions = [pair["0"] - pair["1"] for key, pair in response_pairs.items()
                        if key[0] == group and key[2] != "fixed"]
        print(group, "A/B shoulder-peak interaction (min/mean/max)",
              *(round(v, 4) for v in (min(interactions), stats.mean(interactions), max(interactions))))
        rows = [r for r in sweep if r["group"] == group and r["yawLocked"] == "0"
                and r["design"] != "fixed" and r["pdAtContact"]]
        if rows:
            print(group, "A model free contact sensitivity (min/mean/max)")
            for name in ("shoulderPeakDelta", "fistPeakCarriageDelta", "fistPeakPdDelta",
                         "carriageAtContact", "pdAtContact"):
                values = [float(r[name]) for r in rows]
                print(name, *(round(v, 4) for v in (min(values), stats.mean(values), max(values))))


if __name__ == "__main__":
    run()
