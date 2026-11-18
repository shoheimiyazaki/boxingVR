"""手の格子（HandShapeDumpProbe の CSV）を、正面・横・上の 3 方向から写した 1 枚の絵にする。

使い方:
    set HAND_DUMP_DIR=<dir>
    dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName~HandShapeDumpProbe"
    py -3 tools/hand_views.py <dir> [out.png]

座標は拳の格子のローカル: +X は拳を突き出す向き、+Y は手の甲、+Z は小指の側。前腕（forearm.txt の箱）を
手首から後ろへ薄く描いて、大きさを比べられるようにする。目盛りは 1 cm。
"""
import csv
import sys
from pathlib import Path

from PIL import Image, ImageDraw

COLORS = {
    "hand skin": (219, 153, 107),
    "hand muscle": (204, 13, 18),
    "hand bone": (255, 250, 224),
    "skin": (219, 153, 107),
    "bone": (255, 250, 224),
    "handle": (128, 82, 41),  # 握っている柄（武器の側の格子。絵だけ）
}
SCALE = 4000  # px / m（5 mm のセル = 20 px）
PANEL = 420
WRIST = [0.0, 0.0, 0.0]  # HandSkeleton.Wrist（腕の先が手を握る点。forearm.txt から読む）

# 3 方向: (名前, 横の軸, 縦の軸（上が正）, 奥行きの軸, 見る側の符号)
VIEWS = [
    ("front (from +X)", 2, 1, 0, 1),
    ("side (from -Z, thumb side)", 0, 1, 2, -1),
    ("top (from +Y, back of hand)", 0, 2, 1, 1),
]


def load(path):
    with open(path, newline="", encoding="utf-8") as f:
        return [(float(r["x"]), float(r["y"]), float(r["z"]), r["material"], float(r["cell"])) for r in csv.DictReader(f)]


def panel(cells, forearm, view):
    name, h, v, d, sign = view
    img = Image.new("RGB", (PANEL, PANEL + 30), (32, 34, 40))
    draw = ImageDraw.Draw(img, "RGBA")
    cx, cy = PANEL / 2, PANEL / 2

    def to_px(a, b):
        return cx + a * SCALE, cy - b * SCALE

    # 前腕の箱（手首から後ろ）。
    length, thick_y, thick_z = forearm
    lo = [WRIST[0] - length, WRIST[1] - thick_y / 2, WRIST[2] - thick_z / 2]
    hi = [WRIST[0], WRIST[1] + thick_y / 2, WRIST[2] + thick_z / 2]
    x0, y0 = to_px(lo[h], hi[v])
    x1, y1 = to_px(hi[h], lo[v])
    draw.rectangle([min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)], fill=(120, 140, 170, 70), outline=(150, 170, 200, 200))

    depths = [c[d] * sign for c in cells]
    near, far = max(depths), min(depths)
    for c in sorted(cells, key=lambda c: c[d] * sign):
        size = c[4] * SCALE
        px, py = to_px(c[h], c[v])
        t = 1.0 if near == far else 0.55 + 0.45 * (c[d] * sign - far) / (near - far)
        r, g, b = COLORS.get(c[3], (255, 0, 255))
        draw.rectangle([px - size / 2, py - size / 2, px + size / 2 - 1, py + size / 2 - 1],
                       fill=(int(r * t), int(g * t), int(b * t)), outline=(0, 0, 0))

    # 1 cm の目盛り。
    draw.line([20, PANEL - 10, 20 + 0.01 * SCALE, PANEL - 10], fill=(255, 255, 255), width=3)
    draw.text((20, PANEL - 28), "1 cm", fill=(255, 255, 255))
    draw.text((10, PANEL + 8), name, fill=(255, 255, 255))
    return img


def extent(cells, axis):
    vals = [c[axis] for c in cells]
    return (max(vals) - min(vals) + cells[0][4]) * 1000


def main():
    src = Path(sys.argv[1])
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else src / "hand_views.png"
    length, ty, tz, wx, wy = (float(x) for x in (src / "forearm.txt").read_text().split(","))
    WRIST[0], WRIST[1] = wx, wy
    rows = []
    for shape in ("fist", "grip"):
        everything = load(src / f"{shape}.csv")
        cells = [c for c in everything if c[3] != "handle"]
        label = (f"{shape}: {len(cells)} cells, X {extent(cells, 0):.0f} x Y {extent(cells, 1):.0f} x Z {extent(cells, 2):.0f} mm"
                 f"  (forearm box {ty * 1000:.0f} mm thick)")
        row = Image.new("RGB", (PANEL * 3, PANEL + 60), (20, 22, 26))
        ImageDraw.Draw(row).text((10, 8), label, fill=(255, 255, 255))
        for i, view in enumerate(VIEWS):
            row.paste(panel(everything, (length, ty, tz), view), (i * PANEL, 30))
        rows.append(row)
    sheet = Image.new("RGB", (PANEL * 3, sum(r.height for r in rows)))
    y = 0
    for r in rows:
        sheet.paste(r, (0, y))
        y += r.height
    sheet.save(out)
    print(out)


if __name__ == "__main__":
    main()
