"""Build the boxer's part grids (.vox) from one TotalSegmentator subject.

Pipeline (docs/plans/CT_BODY_PLAN.md):
  1. load CT + 117 masks, keep every 2nd voxel (1.5 mm -> 3 mm; a game cell is ~14 mm of the human, so ~100 samples per cell)
  2. one class per voxel: masks (labels.py) over HU windows inside the body outline (largest component: drops the table)
  3. split arms from trunk (nearest seed: humerus + forearm bone vs. every trunk structure), then cut trunk into
     head / chest / pelvis and the arm into upper arm / forearm at landmark planes
  4. per part: resample into the part's cell grid (cell counts fixed by the game; the human box is stretched to the
     game box, CLAUDE.md §1.7), assign one class per cell by a volume-preserving vote (each class keeps the volume it
     has in the CT, placed in the cells where its share is highest), outer layer -> skin / trunks
  5. head: keep the shipped (hand-edited) face shell, take only the inside from the CT
  6. parts the scan does not cover (thigh, shin, foot, toe) stay as shipped

Usage:
  py -3 -I tools/ct-body/build.py s1029 --out <dir>
Writes <dir>/ct_<id>.vox (game parts, same layout as games/MiniRing/Voxels/boxer.vox) and <dir>/ct_<id>_report.json.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

import numpy as np
from scipy import ndimage

sys.path.insert(0, str(Path(__file__).resolve().parent))
import labels as L  # noqa: E402
import nifti  # noqa: E402
import vox  # noqa: E402

REPO = Path(__file__).resolve().parents[2]
SHIPPED = REPO / "games/MiniRing/Voxels/boxer.vox"
STEP = 2  # keep every 2nd CT voxel (1.5 mm -> 3 mm)

# Game part order and cell counts are read from the shipped .vox (the game's own grids at the default build).
PART_ORDER = ["Head", "Chest", "Pelvis", "UpperArm", "Forearm", "Thigh", "Shin", "Foot", "Toe"]

# Outfit (BoxerAnatomy.Dress): pelvis surface is trunks, its top band the waistband.
RED_TRUNKS, WAISTBAND, HAIR, LIP, PUPIL = 10, 12, 14, 21, 15
WAISTBAND_FROM = 0.55  # normalised Y, BoxerAnatomy.WaistbandFrom

# Arteries: the narrowest vessel kept [mm, human]. The user asked for the arteries a 5 mm voxel can pick up; this is
# applied to the CT (1.5 mm), so it decides which branches are followed, and every kept branch is drawn in the game grid
# as a connected run of cells even where it is thinner than one cell (a game cell is 14-23 mm of the human).
MIN_ARTERY_MM = 5.0
# Below this the scan has no arterial contrast (blood without contrast is ~40 HU) and arteries are taken from the labels only.
ARTERY_MIN_HU = 150.0

# Colour of BoxerAnatomy.Artery (1, 0, 0.25) as .vox RGBA; voxcheck reads materials back by colour, so it must match.
ARTERY_RGBA = 255 | (0 << 8) | (64 << 16) | (255 << 24)

# Forearm length relative to the humerus when the scan does not show the wrist (adult radius ~0.80 of humerus length).
FOREARM_TO_HUMERUS = 0.8


# ---------------------------------------------------------------- 1-2: class volume

def load_subject(subject_dir: Path):
    """Class volume (uint8, 3 mm), body-frame coordinates of each index, and named masks needed for landmarks."""
    ct, header = nifti.load(subject_dir / "ct.nii.gz")
    affine = header.affine.copy()
    # Every later step assumes index order = increasing x, y, z (k up). A scan stored head-to-foot (or right-to-left) is
    # flipped here, with its affine, so "min k" is always the lowest point.
    flips = tuple(a for a in range(3) if affine[a, a] < 0)
    for a in flips:
        affine[:3, 3] += affine[:3, a] * (header.shape[a] - 1)
        affine[:3, a] *= -1
    ct_full = np.ascontiguousarray(np.flip(ct, flips))
    del ct
    ct = np.ascontiguousarray(ct_full[::STEP, ::STEP, ::STEP])
    affine[:3, :3] *= STEP
    labeled_full = np.zeros(ct_full.shape, bool)
    artery_full = np.zeros(ct_full.shape, bool)
    keep = {"humerus_left", "humerus_right", "skull", "hip_left", "hip_right", "vertebrae_L3", "femur_left", "femur_right",
            "clavicula_left", "clavicula_right", "scapula_left", "scapula_right"}
    cls = np.zeros(ct.shape, np.uint8)
    named: dict[str, np.ndarray] = {}
    masks_by_class: dict[int, list[np.ndarray]] = {}
    seg_dir = subject_dir / "segmentations"
    for f in sorted(os.listdir(seg_dir)):
        if not f.endswith(".nii.gz"):
            continue  # e.g. a .part left by an interrupted fetch
        label = f[:-7]
        m, _ = nifti.load(seg_dir / f)
        full = np.flip(m, flips) > 0
        del m
        if not full.any():
            continue
        labeled_full |= full
        if L.tool_class(label) == L.CLS_ARTERY:
            artery_full |= full
        m = np.ascontiguousarray(full[::STEP, ::STEP, ::STEP])
        del full
        masks_by_class.setdefault(L.tool_class(label), []).append(m)
        if label in keep:
            # Landmarks use the largest piece only: the automatic masks carry stray specks (s1029's skull has one in the pelvis).
            parts, count = ndimage.label(m)
            named[label] = parts == (1 + int(np.argmax(ndimage.sum_labels(m, parts, np.arange(1, count + 1))))) if count > 1 else m
    traced_full, artery_report = trace_arteries(ct_full, labeled_full, artery_full, header.zooms)
    del ct_full, labeled_full, artery_full
    traced = np.ascontiguousarray(traced_full[::STEP, ::STEP, ::STEP])
    artery_any = block_any(traced_full, ct.shape)  # for keeping the arteries connected (vote): any traced voxel in the 3 mm block
    del traced_full
    labeled = np.zeros(ct.shape, bool)
    for c in L.PAINT_ORDER:
        for m in masks_by_class.get(c, []):
            labeled |= m
    # Body outline: largest connected component above BODY_HU, holes filled per axial slice (lungs/airways are inside).
    body = ct > L.BODY_HU
    lab, n = ndimage.label(body)
    if n == 0:
        raise SystemExit("no body found in the CT")
    sizes = ndimage.sum_labels(body, lab, index=np.arange(1, n + 1))
    # The largest piece, plus any other piece that holds a labeled structure (an arm lying apart from the trunk).
    touching = np.unique(lab[labeled & (lab > 0)])
    body = np.isin(lab, np.union1d(touching, [1 + int(np.argmax(sizes))]))
    del lab
    for k in range(body.shape[2]):
        body[:, :, k] = ndimage.binary_fill_holes(body[:, :, k])
    body |= labeled
    # Unlabeled voxels inside the outline by HU: fat, bone, otherwise soft tissue.
    cls[body] = L.CLS_SOFT
    cls[body & (ct >= L.FAT_HU[0]) & (ct < L.FAT_HU[1])] = L.CLS_FAT
    unlabeled_bone = body & ~labeled & ~traced & (ct >= L.BONE_HU)  # contrast-filled arteries are as bright as bone
    # Keep only bone-sized lumps (drops isolated bright specks: calcified vessels, lines and electrodes on the skin).
    lab, n = ndimage.label(unlabeled_bone)
    if n:
        sizes = ndimage.sum_labels(unlabeled_bone, lab, index=np.arange(1, n + 1))
        big = np.zeros(n + 1, bool)
        big[1:] = sizes >= 40  # 40 voxels of 3 mm = ~1 cm³
        unlabeled_bone = big[lab]
    cls[unlabeled_bone] = L.CLS_BONE
    cls[body & traced] = L.CLS_ARTERY
    for c in L.PAINT_ORDER:
        for m in masks_by_class.get(c, []):
            cls[m] = c
    artery_any &= body
    for side in ("left", "right"):
        hum, cla = named.get(f"humerus_{side}"), named.get(f"clavicula_{side}")
        if hum is not None and cla is not None and centroid(hum)[2] > centroid(cla)[2]:
            raise SystemExit(f"the {side} arm is raised above the shoulder; pick a scan with the arms down")
    return cls, affine, named, unlabeled_bone, artery_any, artery_report


def trace_arteries(ct, labeled, arteries, zooms):
    """Follow the contrast-filled arteries beyond the labeled ones (aorta and its big branches), at the CT's own resolution.

    Lumen: voxels in the arterial HU window of this scan (from the labeled arteries: half the median .. 99.5th percentile,
    at least ARTERY_MIN_HU), not inside any mask. Size: only vessels at least MIN_ARTERY_MM across survive (an opening with a ball
    of that diameter: erode by its radius, dilate back inside the lumen). Connection: only lumen connected to a labeled
    artery is kept (geodesic reconstruction), so veins and bone that merely have the same brightness stay out unless
    they touch an artery. A scan without arterial contrast (labeled arteries below ARTERY_MIN_HU) gets the labels only."""
    report = {"labeled_voxels": int(arteries.sum())}
    if not arteries.any():
        return arteries, report | {"traced_voxels": 0, "reason": "no labeled arteries"}
    core = ndimage.binary_erosion(arteries, iterations=2)
    values = ct[core if core.any() else arteries]
    # Lower edge: half the lumen's median. A branch narrower than a few voxels is dimmer than the aorta (its edge voxels are
    # part wall), so the aorta's own lower percentile would lose the small branches the size rule is meant to keep.
    lo, hi = max(0.5 * float(np.median(values)), ARTERY_MIN_HU), float(np.percentile(values, 99.5))
    report |= {"hu_window": [round(lo), round(hi)], "median_hu": round(float(np.median(values))), "min_diameter_mm": MIN_ARTERY_MM}
    if np.median(values) < ARTERY_MIN_HU:
        return arteries, report | {"traced_voxels": 0, "reason": "no arterial contrast"}
    lumen = (ct >= lo) & (ct <= hi) & ~labeled
    radius = max(int(round((MIN_ARTERY_MM / 2) / min(zooms) - 0.5)), 1)  # voxels: a 5 mm tube at 1.5 mm keeps a 1-voxel core
    # 18-connected element: a 3 mm tube centred on a voxel has a "plus" cross-section that a 6-connected erosion would keep.
    ball = ndimage.generate_binary_structure(3, 2)
    core = ndimage.binary_erosion(lumen, structure=ball, iterations=radius)
    opened = ndimage.binary_dilation(core, structure=ball, iterations=radius) & lumen
    del core, lumen
    seeds = opened & ndimage.binary_dilation(arteries, structure=ball, iterations=2)
    grown = ndimage.binary_propagation(seeds, mask=opened, structure=ndimage.generate_binary_structure(3, 3))
    report["traced_voxels"] = int(grown.sum())
    return arteries | grown, report


def block_any(full: np.ndarray, shape) -> np.ndarray:
    """True where any voxel of the STEP^3 block is set (keeps thin vessels continuous when the volume is thinned)."""
    out = np.zeros(shape, bool)
    for dx in range(STEP):
        for dy in range(STEP):
            for dz in range(STEP):
                part = full[dx::STEP, dy::STEP, dz::STEP]
                out[:part.shape[0], :part.shape[1], :part.shape[2]] |= part
    return out


def body_frame(affine: np.ndarray, shape) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Per-axis coordinate vectors in the body frame (mm): X = person's left, Y = up, Z = front (game part axes).
    NIfTI world is RAS+ (x = right, y = anterior, z = superior), so X = -x, Y = z, Z = y. Requires an axis-aligned affine."""
    rot = affine[:3, :3]
    if np.count_nonzero(np.abs(rot) > 1e-6) != 3:
        raise SystemExit("oblique CT affines are not supported (resample to an axis-aligned grid first)")
    world = [affine[a, a] * np.arange(shape[a]) + affine[a, 3] for a in range(3)]
    if not all(np.abs(rot[a, a]) > 0 for a in range(3)):
        raise SystemExit("CT axes are permuted; only i=x, j=y, k=z grids are supported")
    return -world[0], world[2], world[1]


# ---------------------------------------------------------------- 3: parts

def centroid(mask: np.ndarray) -> np.ndarray:
    return np.array(ndimage.center_of_mass(mask))


def split_parts(cls: np.ndarray, named: dict, unlabeled_bone: np.ndarray, bx, by, bz):
    """Part id per voxel: 0 none, 1 head, 2 chest, 3 pelvis, 4 upper arm (person's right), 5 forearm (person's right),
    6/7 the left arm, 8 below the pelvis (thigh, not used)."""
    body = cls != L.CLS_EMPTY
    trunk_seed = body & (cls != L.CLS_SOFT) & (cls != L.CLS_FAT)
    arm_seed = {}
    for side in ("right", "left"):
        hum = named.get(f"humerus_{side}")
        if hum is None:
            raise SystemExit(f"humerus_{side} is missing: the scan does not show the arms")
        arm_seed[side] = hum.copy()
    # Forearm bones are not labeled: unlabeled bone lumps at or below the elbow, nearer this humerus than the other one
    # and within ~12 cm (40 voxels of 3 mm) of its axis sideways.
    lab, n = ndimage.label(unlabeled_bone)
    centres = np.array(ndimage.center_of_mass(unlabeled_bone, lab, np.arange(1, n + 1))) if n else np.zeros((0, 3))
    hum_x = {s: centroid(named[f"humerus_{s}"])[0] for s in ("right", "left")}
    for side, other in (("right", "left"), ("left", "right")):
        elbow_k = np.nonzero(named[f"humerus_{side}"].any(axis=(0, 1)))[0].min()
        picked = [i + 1 for i, c in enumerate(centres)
                  if c[2] <= elbow_k + 10 and abs(c[0] - hum_x[side]) < abs(c[0] - hum_x[other]) and abs(c[0] - hum_x[side]) < 40]
        if picked:
            arm_seed[side] |= np.isin(lab, picked)
    del lab
    for side in ("right", "left"):
        trunk_seed &= ~arm_seed[side]
    seeds = np.zeros(cls.shape, np.uint8)
    seeds[trunk_seed] = 1
    seeds[arm_seed["right"]] = 2
    seeds[arm_seed["left"]] = 3
    spacing = (abs(bx[1] - bx[0]), abs(bz[1] - bz[0]), abs(by[1] - by[0]))  # array axes i, j, k = x, y, z
    idx = ndimage.distance_transform_edt(seeds == 0, sampling=spacing, return_distances=False, return_indices=True).astype(np.int16)
    owner = seeds[idx[0], idx[1], idx[2]]
    del idx
    owner[~body] = 0

    y = by[None, None, :]
    part = np.zeros(cls.shape, np.uint8)
    skull = named["skull"]
    head_bottom = by[np.nonzero(skull.any(axis=(0, 1)))[0].min()]
    l3 = named.get("vertebrae_L3")
    if l3 is None:
        raise SystemExit("vertebrae_L3 is missing: cannot place the waist")
    waist = by[np.nonzero(l3.any(axis=(0, 1)))[0].max()]
    hips = named["hip_left"] | named["hip_right"]
    pelvis_bottom = by[np.nonzero(hips.any(axis=(0, 1)))[0].min()]
    trunk = owner == 1
    arm_axes = {}
    part[trunk & (y >= head_bottom)] = 1
    part[trunk & (y < head_bottom) & (y >= waist)] = 2
    part[trunk & (y < waist) & (y >= pelvis_bottom)] = 3
    part[trunk & (y < pelvis_bottom)] = 8
    landmarks = {"head_bottom": float(head_bottom), "waist_L3_top": float(waist), "pelvis_bottom": float(pelvis_bottom)}
    for side, (upper_id, fore_id) in (("right", (4, 5)), ("left", (6, 7))):
        hum = named[f"humerus_{side}"]
        ks = np.nonzero(hum.any(axis=(0, 1)))[0]
        shoulder, elbow = by[ks.max()], by[ks.min()]
        wrist = elbow - FOREARM_TO_HUMERUS * (shoulder - elbow)
        arm = owner == (2 if side == "right" else 3)
        part[arm & (y >= elbow)] = upper_id
        part[arm & (y < elbow) & (y >= wrist)] = fore_id
        fore_bone = (part == fore_id) & (cls == L.CLS_BONE)
        if fore_bone.sum() < 50:
            raise SystemExit(f"no forearm bone found on the {side} side")
        arm_axes[f"{side}_forearm_axis"] = principal_axis(fore_bone, bx, by, bz)
        top = hum[:, :, ks.max() - 3:ks.max() + 1]
        bottom = hum[:, :, ks.min():ks.min() + 4]
        arm_axes[side] = arm_axes.get(side, {}) | {"shoulder_y": float(shoulder), "elbow_y": float(elbow), "wrist_y": float(wrist),
                          "head_xyz": centroid_mm(top, bx, by, bz, ks.max() - 3), "elbow_xyz": centroid_mm(bottom, bx, by, bz, ks.min()),
                          "scan_bottom_y": float(by.min())}
        landmarks[f"{side}_shoulder"] = float(shoulder)
        landmarks[f"{side}_elbow"] = float(elbow)
        landmarks[f"{side}_wrist"] = float(wrist)
    # Each part keeps its largest connected piece only (drops cables, the table edge and specks the outline picked up).
    for value in range(1, 8):
        mask = part == value
        pieces, count = ndimage.label(mask)
        if count > 1:
            sizes = ndimage.sum_labels(mask, pieces, np.arange(1, count + 1))
            part[mask & (pieces != 1 + int(np.argmax(sizes)))] = 0
    return part, landmarks, arm_axes


def principal_axis(mask, bx, by, bz) -> list[float]:
    """Unit direction (body frame) of the longest extent of a mask, pointing up (towards the elbow for a forearm)."""
    i, j, k = np.nonzero(mask)
    pts = np.stack([bx[i], by[k], bz[j]], axis=1)
    pts = pts - pts.mean(0)
    _, _, vt = np.linalg.svd(pts, full_matrices=False)
    axis = vt[0]
    return (axis if axis[1] > 0 else -axis).tolist()


def centroid_mm(mask, bx, by, bz, k0):
    c = ndimage.center_of_mass(mask)
    return [float(np.interp(c[0], np.arange(len(bx)), bx)), float(np.interp(c[2] + k0, np.arange(len(by)), by)),
            float(np.interp(c[1], np.arange(len(bz)), bz))]


# ---------------------------------------------------------------- 4: resample + vote

def vote(local: np.ndarray, classes: np.ndarray, dims) -> tuple[np.ndarray, dict]:
    """local: (N, 3) coordinates already normalised to 0..1 inside the part box; classes: (N,) tool class.
    Returns the cell grid of tool classes (game axes [x, y, z]) and per-class volume shares.

    Outline: a cell is inside when at least half its samples are body. Inside cells are then filled class by class so
    each class keeps (rounded) the number of cells its samples add up to, taking the cells where its share is
    highest; this keeps thin, high-value structures (ribs, cord, vessels' walls of organs) that a plain majority would erase."""
    nx, ny, nz = dims
    cell = np.minimum((local * np.array(dims)).astype(int), np.array(dims) - 1)
    flat = cell[:, 0] + nx * (cell[:, 1] + ny * cell[:, 2])
    ncell = nx * ny * nz
    counts = np.zeros((ncell, L.COUNT), np.float64)
    np.add.at(counts, (flat, classes), 1.0)
    total = counts.sum(1)
    # The box spans the part's tissue extent, so empty samples are only the air around a rounded outline. A cell with no
    # sample (finer grid than the data) is filled from its neighbours by the nearest sampled cell.
    has = total > 0
    if not has.all():
        grid_has = has.reshape(dims, order="F")
        _, idx = ndimage.distance_transform_edt(~grid_has, return_indices=True)
        src = (idx[0] + nx * (idx[1] + ny * idx[2])).reshape(-1, order="F")
        counts = counts[src]
        total = counts.sum(1)
    share = counts / total[:, None]
    inside = share[:, L.CLS_EMPTY] < 0.5
    result = np.full(ncell, L.CLS_EMPTY, np.int64)
    free = inside.copy()
    stats = {}
    inner = [c for c in range(1, L.COUNT)]
    # Quota per class, scaled so the quotas fill exactly the inside cells.
    raw = share[inside][:, 1:].sum(0)
    want = raw / max(raw.sum(), 1e-9) * inside.sum()
    quota = np.floor(want).astype(int)
    for i in np.argsort(-(want - quota))[: int(inside.sum() - quota.sum())]:
        quota[i] += 1
    # Most precious first: small, high-share classes claim their best cells before the bulk tissue.
    order = sorted(inner, key=lambda c: quota[c - 1])
    for c in order:
        q = quota[c - 1]
        if q <= 0:
            continue
        candidates = np.nonzero(free & (share[:, c] > 0))[0]
        best = candidates[np.argsort(-share[candidates, c], kind="stable")][:q]
        result[best] = c
        free[best] = False
        stats[L.NAMES[c]] = {"cells": int(len(best)), "share_volume": round(float(want[c - 1]), 1)}
    # Anything left (rounding, classes that ran out of candidates) takes its own majority.
    left = np.nonzero(free)[0]
    if len(left):
        result[left] = 1 + np.argmax(share[left, 1:], axis=1)
    return result.reshape(dims, order="F"), stats


def part_samples(part_ids, part_value, cls, bx, by, bz, frame, dims, flag, pad_cells=0.0, straighten=False):
    """Samples of one part's box: coordinates normalised to 0..1 in the part's local frame, and classes. The box is the
    extent of the part's tissue (optionally padded by `pad_cells`); the outline's corners stay empty by the vote.
    Every CT voxel inside the box is a sample: the part's own voxels with their class, everything else (air, the table,
    the arm beside the chest) as empty.

    straighten: for a limb segment (length along local X). The game's segment is a straight box, the scanned one bends
    and leans, so its bounding box is much wider than any cross-section and the limb came out half as thick. Each point
    is shifted across the axis by the tissue centre of its cross-section (a smooth curve along X), which lays the limb
    straight before the box is taken. The box then spans the 0.5..99.5 percentiles (drops stray fringe voxels)."""
    sel = np.nonzero(part_ids == part_value)
    if len(sel[0]) == 0:
        raise SystemExit(f"part {part_value} has no voxels in this scan")
    local = frame(np.stack([bx[sel[0]], by[sel[2]], bz[sel[1]]], axis=1))
    shift = None
    if straighten:
        bins = np.linspace(local[:, 0].min(), local[:, 0].max(), 2 * dims[0] + 1)
        which = np.clip(np.digitize(local[:, 0], bins) - 1, 0, len(bins) - 2)
        centres = np.array([local[which == b, 1:].mean(0) if np.any(which == b) else [np.nan, np.nan] for b in range(len(bins) - 1)])
        mids = 0.5 * (bins[:-1] + bins[1:])
        ok = ~np.isnan(centres[:, 0])
        kernel = np.ones(3) / 3  # light smoothing: the axis follows the limb, not each bump of muscle
        cy = np.convolve(np.interp(mids, mids[ok], centres[ok, 0]), kernel, mode="same")
        cz = np.convolve(np.interp(mids, mids[ok], centres[ok, 1]), kernel, mode="same")
        cy[0], cy[-1], cz[0], cz[-1] = cy[1], cy[-2], cz[1], cz[-2]

        def shift(p):
            q = p.copy()
            q[:, 1] -= np.interp(p[:, 0], mids, cy)
            q[:, 2] -= np.interp(p[:, 0], mids, cz)
            return q
        local = shift(local)
        lo, hi = np.percentile(local, 0.5, axis=0), np.percentile(local, 99.5, axis=0)
        lo[0], hi[0] = local[:, 0].min(), local[:, 0].max()
    else:
        lo, hi = local.min(0), local.max(0)
    pad = (hi - lo) / np.array(dims) * pad_cells
    lo, hi = lo - pad, hi + pad
    # Candidate voxels: index bounding box of the part, grown by the padding in every direction (rotated frames: by the
    # longest padding), then kept if their local coordinates fall in the box.
    # Rotated (arm) boxes reach past the part's index box at their corners: grow by the cross-section's diagonal.
    reach = np.linalg.norm(hi[1:] - lo[1:]) / 2 if straighten else pad.max()
    grow = int(np.ceil(reach / abs(bx[1] - bx[0]))) + 1
    i0, i1 = max(sel[0].min() - grow, 0), min(sel[0].max() + grow + 1, len(bx))
    j0, j1 = max(sel[1].min() - grow, 0), min(sel[1].max() + grow + 1, len(bz))
    k0, k1 = max(sel[2].min() - grow, 0), min(sel[2].max() + grow + 1, len(by))
    ii, jj, kk = np.meshgrid(np.arange(i0, i1), np.arange(j0, j1), np.arange(k0, k1), indexing="ij")
    ii, jj, kk = ii.ravel(), jj.ravel(), kk.ravel()
    pts = frame(np.stack([bx[ii], by[kk], bz[jj]], axis=1))
    if shift is not None:
        pts = shift(pts)
    norm = (pts - lo) / (hi - lo)
    inside = np.all((norm >= 0) & (norm <= 1), axis=1)  # vote() clamps 1.0 into the last cell
    ii, jj, kk, norm = ii[inside], jj[inside], kk[inside], norm[inside]
    own = part_ids[ii, jj, kk] == part_value
    classes = np.where(own, cls[ii, jj, kk], L.CLS_EMPTY).astype(np.int64)
    return norm, classes, own & flag[ii, jj, kk], (lo, hi)


def trunk_frame(pts):
    return pts


def arm_frame(proximal_direction):
    """Arm segment local frame: X along the segment with the hand at -X (game: the far end of the arm grid is -X),
    Z = the body's front made orthogonal to X, Y = Z x X (right-handed like the game)."""
    x_axis = np.asarray(proximal_direction, float)
    x_axis = x_axis / np.linalg.norm(x_axis)
    front = np.array([0.0, 0.0, 1.0])
    z_axis = front - front.dot(x_axis) * x_axis
    z_axis /= np.linalg.norm(z_axis)
    y_axis = np.cross(z_axis, x_axis)
    m = np.stack([x_axis, y_axis, z_axis])

    def apply(pts):
        return pts @ m.T
    return apply


def connect_arteries(grid_cls: np.ndarray, norm: np.ndarray, flags: np.ndarray, dims, kind: str) -> tuple[np.ndarray, dict]:
    """Make the arteries of one part a connected tree of face-connected cells reaching every traced branch.

    The volume-preserving vote gives an artery only as many cells as its volume: the aorta keeps its cells, but a branch
    thinner than a cell (carotid, subclavian, the gut's arteries: 5-10 mm of a 14-23 mm cell) gets none. The cells the
    traced arteries pass through (any sample with the artery flag) form the corridor. Starting from the vote's artery
    cells (or, for a corridor piece without any, its cell with the most artery), the farthest corridor cell is joined to
    the tree by the cheapest path (cells with more artery cost less; leaving the corridor or crossing bone costs more),
    again and again until every corridor cell is next to the tree. Each branch becomes one run of cells through its
    middle, ending where the traced artery ends (where it gets narrower than MIN_ARTERY_MM, or the part ends)."""
    import heapq

    nx, ny, nz = dims
    cell = np.minimum((norm * np.array(dims)).astype(int), np.array(dims) - 1)
    flat = cell[:, 0] + nx * (cell[:, 1] + ny * cell[:, 2])
    ncell = nx * ny * nz
    hits = np.bincount(flat[flags], minlength=ncell).astype(float)
    total = np.bincount(flat, minlength=ncell).astype(float)
    share = np.divide(hits, total, out=np.zeros(ncell), where=total > 0).reshape(dims, order="F")
    grid = grid_cls.copy()
    # The skin layer (add_skin) will cover every outer cell except an artery on a joint face: arteries keep out of it.
    filled = grid != L.CLS_EMPTY
    inside = filled & ~(outer_layer(filled) & ~joint_faces(dims, kind))
    corridor = (share > 0) & inside
    grid[(grid == L.CLS_ARTERY) & ~inside] = L.CLS_SOFT
    artery = (grid == L.CLS_ARTERY) & inside
    report = {"vote_cells": int(artery.sum()), "corridor_cells": int(corridor.sum())}
    if not (artery | corridor).any():
        return grid, report | {"pieces": 0, "added_cells": 0}
    # A corridor piece with no voted artery cell starts from its cell with the most artery.
    regions, count = ndimage.label(corridor | artery, structure=ndimage.generate_binary_structure(3, 3))
    for r in range(1, count + 1):
        member = regions == r
        if not (artery & member).any():
            artery[np.unravel_index(np.argmax(np.where(member, share, -1.0)), dims)] = True
    # Bone and organs cost more to cross; the spinal cord and the brain are never crossed (a run there would cut them).
    cost = np.where(corridor, 1.0 + (1.0 - share), 6.0) + np.where(np.isin(grid, ORGAN_CLASSES), 4.0, 0.0)
    inside &= ~np.isin(grid, [L.CLS_CORD, L.CLS_BRAIN])
    corridor &= inside
    steps = ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1))
    face = ndimage.generate_binary_structure(3, 1)

    def search(sources):
        dist = np.full(dims, np.inf)
        hops = np.zeros(dims, int)
        prev = {}
        heap = [(0.0, c) for c in zip(*np.nonzero(sources))]
        for _, c in heap:
            dist[c] = 0.0
        heapq.heapify(heap)
        while heap:
            d, c = heapq.heappop(heap)
            if d > dist[c]:
                continue
            for dx, dy, dz in steps:
                n = (c[0] + dx, c[1] + dy, c[2] + dz)
                if 0 <= n[0] < nx and 0 <= n[1] < ny and 0 <= n[2] < nz and inside[n]:
                    nd = d + cost[n]
                    if nd < dist[n]:
                        dist[n], hops[n], prev[n] = nd, hops[c] + 1, c
                        heapq.heappush(heap, (nd, n))
        return dist, hops, prev

    def add_path(target, prev, stop):
        """Mark the path from target back to a source cell (where `stop` is true) as artery; returns the cells added."""
        added = 0
        c = target
        while c is not None and not stop[c]:
            if not artery[c]:
                artery[c] = True
                added += 1
            c = prev.get(c)
        return added

    added = 0
    # 1) join pieces of the same vessel (same corridor region) into one.
    # Pieces are labeled on all artery cells (a joining path may step outside the region's cells), and a region's pieces
    # are those meeting it. Each pass joins one piece to the main one or stops.
    for r in range(1, count + 1):
        while True:
            pieces, _ = ndimage.label(artery, structure=face)
            ids = np.unique(pieces[(regions == r) & artery])
            ids = ids[ids > 0]
            if len(ids) <= 1:
                break
            sizes = ndimage.sum_labels(artery, pieces, ids)
            main = pieces == ids[int(np.argmax(sizes))]
            dist, _, prev = search(main)
            others = np.isin(pieces, ids) & ~main
            target = np.unravel_index(np.argmin(np.where(others, dist, np.inf)), dims)
            if not np.isfinite(dist[target]) or target not in prev:
                break
            gained = add_path(prev[target], prev, main)
            added += gained
            if gained == 0:
                break  # the target already touches the main piece through cells labeled apart: nothing left to join
    # 2) reach out to the end of every branch: join the farthest corridor cell until all are next to the tree.
    while True:
        dist, hops, prev = search(artery)
        far = np.where(corridor & np.isfinite(dist), hops, -1)
        target = np.unravel_index(np.argmax(far), dims)
        if far[target] <= 1:
            break
        added += add_path(target, prev, artery.copy())
    grid[artery] = L.CLS_ARTERY
    _, pieces = ndimage.label(artery, structure=face)
    return grid, report | {"pieces": int(pieces), "added_cells": added, "cells": int(artery.sum())}


# Box faces through which a part meets its neighbours (axis of the faces): the head's neck end, the trunk's top and
# bottom (and the chest's shoulder sides), the arm's two ends.
JOINT_AXES = {"Head": (1,), "Chest": (0, 1), "Pelvis": (1,), "UpperArm": (0,), "Forearm": (0,)}
# Only the head's lower end meets the neck; the chest's sides meet the arms only in their upper part (the shoulders,
# HumanoidRagdoll puts the shoulder at three quarters of the chest's height).
SHOULDER_FROM = 0.6
ORGAN_CLASSES = [L.CLS_BONE, L.CLS_HEART, L.CLS_LUNG, L.CLS_LIVER, L.CLS_GUT]


def joint_faces(shape, kind: str) -> np.ndarray:
    joint = np.zeros(shape, bool)
    for axis in JOINT_AXES[kind]:
        index = [slice(None)] * 3
        for end in ((0,) if kind == "Head" else (0, -1)):
            index[axis] = end
            joint[tuple(index)] = True
    if kind == "Chest":
        joint[[0, -1], : int(shape[1] * SHOULDER_FROM), :] = False  # below the shoulders the chest's sides are skin
    return joint


def outer_layer(filled: np.ndarray) -> np.ndarray:
    """Filled cells touching empty or the box edge (the layer add_skin turns into skin)."""
    padded = np.pad(filled, 1, constant_values=False)
    return filled & ~ndimage.binary_erosion(padded, structure=ndimage.generate_binary_structure(3, 1))[1:-1, 1:-1, 1:-1]


def add_skin(grid: np.ndarray, kind: str) -> np.ndarray:
    """Outer layer -> skin; pelvis -> trunks/waistband (BoxerAnatomy.Dress). Surface = a filled cell touching empty or the box edge."""
    out = grid.copy()
    filled = grid != 0
    padded = np.pad(filled, 1, constant_values=False)
    interior = ndimage.binary_erosion(padded, structure=ndimage.generate_binary_structure(3, 1))[1:-1, 1:-1, 1:-1]
    # An artery may stay bare only where it leaves the part through a face that joins another part (it goes on there);
    # anywhere else the outer layer is skin over it.
    surface = filled & ~interior & ~((grid == L.ARTERY) & joint_faces(grid.shape, kind))
    if kind == "Pelvis":
        ys = np.nonzero(filled.any(axis=(0, 2)))[0]
        lo, hi = ys.min(), ys.max()
        n_y = (np.arange(grid.shape[1]) - (lo + hi) / 2) / max((hi - lo) / 2, 1)
        band = n_y[None, :, None] > WAISTBAND_FROM
        out[surface] = RED_TRUNKS
        out[surface & np.broadcast_to(band, grid.shape)] = WAISTBAND
    else:
        out[surface] = L.SKIN
    return out


def to_game(grid_cls: np.ndarray) -> np.ndarray:
    lut = np.zeros(L.COUNT, np.uint8)
    for c, m in L.GAME_MATERIAL.items():
        lut[c] = m
    return lut[grid_cls]


def merge_head(shipped: np.ndarray, ct_head: np.ndarray) -> np.ndarray:
    """Keep the hand-edited face (skin, hair, lips, eyes) and its outline; take the inside from the CT."""
    shell = np.isin(shipped, [L.SKIN, HAIR, LIP, L.EYE, PUPIL])
    out = shipped.copy()
    inside = (shipped != 0) & ~shell
    ct = ct_head.copy()
    ct[ct == 0] = L.MUSCLE  # the edited outline is a little larger than the CT head in places: fill with flesh
    out[inside] = ct[inside]
    return out


# ---------------------------------------------------------------- main

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("subject")
    parser.add_argument("--cache", default=os.environ.get("CTBODY_CACHE") or str(REPO.parent / "ct-data"))
    parser.add_argument("--set", default="small")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    subject_dir = Path(args.cache) / f"totalseg-{args.set}" / args.subject
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    shipped_models, palette = vox.read(SHIPPED)
    palette = list(palette)
    palette[L.ARTERY] = ARTERY_RGBA  # the shipped file predates the artery material
    shipped = {m.name: vox.from_vox_axes(m.cells) for m in shipped_models}
    missing = [p for p in PART_ORDER if p not in shipped]
    if missing:
        raise SystemExit(f"shipped boxer.vox lacks parts {missing}")

    cls, affine, named, unlabeled_bone, artery_any, artery_report = load_subject(subject_dir)
    bx, by, bz = body_frame(affine, cls.shape)
    part_ids, landmarks, arm_axes = split_parts(cls, named, unlabeled_bone, bx, by, bz)

    report = {"subject": args.subject, "source": "TotalSegmentator v2.0.1 (Zenodo 10047263), CC BY 4.0", "landmarks_mm": landmarks,
              "arm_axes": arm_axes, "arteries": artery_report, "parts": {}}
    grids = {}
    side = "right"  # the game's arm grid has the hand at -X, matching the person's right arm in this frame
    plan = {
        "Head": (1, trunk_frame), "Chest": (2, trunk_frame), "Pelvis": (3, trunk_frame),
        "UpperArm": (4, arm_frame(np.array(arm_axes[side]["head_xyz"]) - np.array(arm_axes[side]["elbow_xyz"]))),
        "Forearm": (5, arm_frame(arm_axes[f"{side}_forearm_axis"])),
    }
    for name, (value, frame) in plan.items():
        dims = shipped[name].shape
        norm, classes, flags, (lo, hi) = part_samples(part_ids, value, cls, bx, by, bz, frame, dims, artery_any,
                                                      straighten=name in ("UpperArm", "Forearm"))
        grid_cls, stats = vote(norm, classes, dims)
        grid_cls, artery_stats = connect_arteries(grid_cls, norm, flags, dims, name)
        # The head keeps the hand-edited face shell, so its CT cells go in without their own skin layer (the skull lies right
        # under the skin, in the CT's outermost cells).
        game = merge_head(shipped["Head"], to_game(grid_cls)) if name == "Head" else add_skin(to_game(grid_cls), name)
        grids[name] = game
        human_size = (hi - lo)
        report["parts"][name] = {"dims": list(dims), "human_box_mm": [round(float(v), 1) for v in human_size],
                                 "human_cell_mm": [round(float(s / d), 1) for s, d in zip(human_size, dims)],
                                 "samples": int(len(norm)), "classes": stats, "arteries": artery_stats,
                                 "materials": {int(k): int(v) for k, v in zip(*np.unique(game, return_counts=True))}}
    for name in PART_ORDER:
        if name not in grids:
            grids[name] = shipped[name]
            report["parts"][name] = {"from": "shipped boxer.vox (not covered by the scan)"}

    models = [vox.VoxModel(name, vox.to_vox_axes(grids[name])) for name in PART_ORDER]
    vox.write(out_dir / f"ct_{args.subject}.vox", models, palette)
    np.savez_compressed(out_dir / f"ct_{args.subject}_parts.npz", **grids)
    (out_dir / f"ct_{args.subject}_report.json").write_text(json.dumps(report, indent=1, ensure_ascii=False), encoding="utf-8")
    print(json.dumps({k: v.get("human_box_mm", v.get("from")) for k, v in report["parts"].items()}, ensure_ascii=False, indent=0))


if __name__ == "__main__":
    main()
