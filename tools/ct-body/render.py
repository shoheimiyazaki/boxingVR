"""Draw a boxer .vox (game parts) assembled into a standing body: outside from the front, side and top, and cutaways
(front half removed, left half removed, top removed at the chest) so the bones and organs show.

The parts are placed like HumanoidRagdoll builds them (chest on the pelvis, head on the chest, arms out to the sides in a
T-pose at three quarters of the chest's height, legs under the hips), on one 5 mm lattice. Only for looking: the game's
own grids are stretched to each part's box. The arm and leg grids on the person's left are drawn mirrored (the game uses
one grid for both sides; see docs/plans/CT_BODY_PLAN.md about mirroring).

Usage:
  py -3 -I tools/ct-body/render.py <a.vox> [<b.vox> ...] --labels "今の体" "CT の体" --out compare.png
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parent))
import vox  # noqa: E402

SCALE = 5  # pixels per cell
FONT = "C:/Windows/Fonts/meiryo.ttc"


def colours(palette: list[int]) -> np.ndarray:
    c = np.array([[(p >> s) & 0xFF for s in (0, 8, 16)] for p in palette], np.float32)
    return c


def assemble(path: Path) -> np.ndarray:
    models, _ = vox.read(path)
    g = {m.name: vox.from_vox_axes(m.cells) for m in models}
    pel, che, hea = g["Pelvis"], g["Chest"], g["Head"]
    ua, fa, th, sh, fo, to = g["UpperArm"], g["Forearm"], g["Thigh"], g["Shin"], g["Foot"], g["Toe"]
    W = ua.shape[0] * 2 + fa.shape[0] * 2 + che.shape[0] + 8
    H = hea.shape[1] + che.shape[1] + pel.shape[1] + th.shape[1] + sh.shape[1] + fo.shape[1] + 8
    D = che.shape[2] + fo.shape[2] + to.shape[2] + 8
    body = np.zeros((W, H, D), np.uint8)
    cx, cz = W // 2, D // 2

    def put(grid, centre, mirror=False):
        if mirror:
            grid = grid[::-1, :, :]
        lo = [int(round(c - s / 2)) for c, s in zip(centre, grid.shape)]
        sl = tuple(slice(l, l + s) for l, s in zip(lo, grid.shape))
        region = body[sl]
        mask = grid != 0
        region[mask] = grid[mask]

    y = fo.shape[1] + sh.shape[1] + th.shape[1] + 2
    pel_c = (cx, y + pel.shape[1] / 2, cz)
    che_c = (cx, y + pel.shape[1] + che.shape[1] / 2, cz)
    hea_c = (cx, y + pel.shape[1] + che.shape[1] + hea.shape[1] / 2, cz)
    shoulder = che_c[1] + che.shape[1] / 4
    for side in (-1, 1):
        mirror = side > 0  # the grid's hand end is -X; mirror it for the arm on +X
        ua_c = (cx + side * (che.shape[0] / 2 + ua.shape[0] / 2), shoulder, cz)
        fa_c = (ua_c[0] + side * (ua.shape[0] / 2 + fa.shape[0] / 2), shoulder, cz)
        put(ua, ua_c, mirror)
        put(fa, fa_c, mirror)
        hip_x = cx + side * pel.shape[0] / 2 * 0.65
        th_c = (hip_x, y - th.shape[1] / 2, cz)
        sh_c = (hip_x, th_c[1] - th.shape[1] / 2 - sh.shape[1] / 2, cz)
        fo_c = (hip_x, sh_c[1] - sh.shape[1] / 2 - fo.shape[1] / 2, cz + fo.shape[2] / 2 - 3)
        to_c = (hip_x, fo_c[1] - fo.shape[1] / 2 + to.shape[1] / 2, fo_c[2] + fo.shape[2] / 2 + to.shape[2] / 2)
        for grid, c in ((th, th_c), (sh, sh_c), (fo, fo_c), (to, to_c)):
            put(grid, c, mirror)
    put(pel, pel_c)
    put(che, che_c)
    put(hea, hea_c)
    nz = np.nonzero(body)
    return body[nz[0].min():nz[0].max() + 1, nz[1].min():nz[1].max() + 1, nz[2].min():nz[2].max() + 1]


def project(body: np.ndarray, rgb: np.ndarray, view: str) -> np.ndarray:
    """Orthographic view with depth shading. front: from +Z looking back; side: from +X (the person's left); top: from +Y."""
    if view == "front":
        vol = body                                         # rays along -Z; image x = -X? keep viewer's right = person's left
        axis, flip_x = 2, False
        order = np.arange(vol.shape[2])[::-1]
        get = lambda k: vol[:, :, k]  # noqa: E731
        w, h = vol.shape[0], vol.shape[1]
    elif view == "side":
        order = np.arange(body.shape[0])[::-1]
        get = lambda k: body[k, :, ::-1].T  # (Z reversed, Y): front to the left  # noqa: E731
        w, h = body.shape[2], body.shape[1]
    else:  # top
        order = np.arange(body.shape[1])[::-1]
        get = lambda k: body[:, k, :]  # (X, Z): after the transpose the front is at the bottom, the person's left on the right  # noqa: E731
        w, h = body.shape[0], body.shape[2]
    img = np.full((w, h, 3), 245.0, np.float32)
    done = np.zeros((w, h), bool)
    n = len(order)
    for depth, k in enumerate(order):
        sl = get(k)
        hit = (sl != 0) & ~done
        if hit.any():
            shade = 1.0 - 0.45 * depth / n
            img[hit] = rgb[sl[hit]] * shade
            done |= hit
    out = np.transpose(img, (1, 0, 2))
    if view in ("front", "side"):
        out = out[::-1]  # Y up
    return out.clip(0, 255).astype(np.uint8)


def cut(body: np.ndarray, view: str) -> np.ndarray:
    b = body.copy()
    filled = np.nonzero(b)
    if view == "front":   # remove the front half: the frontal plane through the chest's middle depth
        mid = int(np.median(filled[2]))
        b[:, :, mid + 1:] = 0
    elif view == "side":  # remove the person's left half
        mid = b.shape[0] // 2
        b[mid + 1:, :, :] = 0
    else:                 # remove everything above the heart height (a little above the chest's middle)
        ys = filled[1]
        b[:, int(np.percentile(ys, 80)):, :] = 0
    return b


def panel(path: Path):
    _, palette = vox.read(path)
    rgb = colours(palette)  # each file's own palette (an older file may lack a newer material's colour)
    body = assemble(path)
    views = [("正面", project(body, rgb, "front")), ("横（左から）", project(body, rgb, "side")), ("上", project(body, rgb, "top")),
             ("断面: 前を外す", project(cut(body, "front"), rgb, "front")), ("断面: 左を外す", project(cut(body, "side"), rgb, "side")),
             ("断面: 胸の上を外す", project(cut(body, "top"), rgb, "top")),
             ("動脈（前から透かす）", see_through(body, rgb, ARTERY))]
    return views


ARTERY = 26  # BoxerAnatomy.Artery


def see_through(body: np.ndarray, rgb: np.ndarray, material: int) -> np.ndarray:
    """Front view of one material through the body: the material in its colour (nearer = brighter) over a pale silhouette."""
    silhouette = project(body, rgb, "front").astype(np.float32)
    pale = (255 - (255 - silhouette) * 0.25).astype(np.uint8)
    only = np.where(body == material, body, 0).astype(np.uint8)
    if not only.any():
        return pale
    drawn = project(only, rgb, "front")
    hit = (only != 0).any(axis=2).T[::-1]
    out = pale.copy()
    out[hit] = drawn[hit]
    return out


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("vox", nargs="+")
    parser.add_argument("--labels", nargs="*")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    labels = args.labels or [Path(v).stem for v in args.vox]
    rows = [panel(Path(v)) for v in args.vox]
    font = ImageFont.truetype(FONT, 22)
    small = ImageFont.truetype(FONT, 16)
    widths = [max(r[i][1].shape[1] for r in rows) * SCALE for i in range(len(rows[0]))]
    heights = [max(v[1].shape[0] for v in r) * SCALE for r in rows]
    margin, label_w = 12, 150
    canvas = Image.new("RGB", (label_w + sum(widths) + margin * (len(widths) + 1), sum(heights) + 40 * len(rows) + margin * 2), (255, 255, 255))
    draw = ImageDraw.Draw(canvas)
    y = margin
    for label, r, h in zip(labels, rows, heights):
        draw.text((margin, y + h // 2), label, fill=(0, 0, 0), font=font)
        x = label_w
        for (title, img), w in zip(r, widths):
            draw.text((x, y), title, fill=(60, 60, 60), font=small)
            im = Image.fromarray(img).resize((img.shape[1] * SCALE, img.shape[0] * SCALE), Image.NEAREST)
            canvas.paste(im, (x, y + 28 + (h - im.height)))
            x += w + margin
        y += h + 40
    canvas.save(args.out)
    print(args.out, canvas.size)


if __name__ == "__main__":
    main()


def slices_sheet(paths: list[Path], labels: list[str], out: str, scale: int = 12) -> None:
    """Middle slices of each CT-made part, larger: frontal (front half removed), sagittal (the person's left removed),
    axial (from above). One row per body, so the shipped and the CT parts sit under each other."""
    parts = ["Head", "Chest", "Pelvis", "UpperArm", "Forearm"]
    rows = []
    for path in paths:
        models, palette = vox.read(path)
        rgb = colours(palette).astype(np.uint8)
        g = {m.name: vox.from_vox_axes(m.cells) for m in models}
        row = []
        for name in parts:
            a = g[name]
            nx, ny, nz = a.shape
            frontal = rgb[a[:, :, nz // 2]].transpose(1, 0, 2)[::-1]       # rows = Y (up first), cols = X (person's right on the left)
            sagittal = rgb[a[nx // 2, :, ::-1]][::-1]                      # rows = Y (up first), cols = Z, front on the left
            axial = rgb[a[:, ny // 2, :]].transpose(1, 0, 2)               # rows = Z (front at the bottom), cols = X: seen from above
            row.append((name, [frontal, sagittal, axial]))
        rows.append(row)
    font = ImageFont.truetype(FONT, 20)
    small = ImageFont.truetype(FONT, 14)
    gap = 10
    col_w = [sum(img.shape[1] for img in rows[0][p][1]) * scale + 2 * gap for p in range(len(parts))]
    row_h = max(img.shape[0] for r in rows for _, imgs in r for img in imgs) * scale
    W = 120 + sum(col_w) + gap * len(parts)
    H = (row_h + 50) * len(rows) + 30
    canvas = Image.new("RGB", (W, H), (255, 255, 255))
    draw = ImageDraw.Draw(canvas)
    y = 10
    for label, r in zip(labels, rows):
        draw.text((10, y + row_h // 2), label, fill=(0, 0, 0), font=font)
        x = 120
        for (name, imgs), w in zip(r, col_w):
            draw.text((x, y), f"{name}（前・横・上）", fill=(60, 60, 60), font=small)
            xx = x
            for img in imgs:
                im = Image.fromarray(img).resize((img.shape[1] * scale, img.shape[0] * scale), Image.NEAREST)
                canvas.paste(im, (xx, y + 24))
                xx += im.width + gap
            x += w + gap
        y += row_h + 50
    canvas.save(out)
    print(out, canvas.size)
