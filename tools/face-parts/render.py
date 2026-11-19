"""組み立てた顔の格子（FacePartLibraryTests.Probe_DumpFaces の txt）を、正面・横・上の 3 方向の絵にして、参考図と並べる。

使い方（リポジトリの根で）:
    set FACE_DUMP_DIR=<dir>
    dotnet test tests/MiniRing.Tests --filter "FullyQualifiedName~Probe_DumpFaces"
    py -3 tools/face-parts/render.py <dir> <out_dir>

出力: compare_<名前>.png（参考図の土台の 3 方向 | 組み立てた顔の 3 方向）、parts_<部位>.png（同じ土台に部品だけ替えた 6 種）。
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

REFS = Path(__file__).resolve().parents[2] / "docs/plans/face_refs"
SYMBOLS = ".shbwilenf"
# 材質 -> 色（参考図の凡例に合わせる）。b 眉・h 髪・w 白目・i 黒目・l 唇・e 影・n 鼻の穴・f そばかす。
COLORS = {".": (32, 32, 32), "s": (200, 152, 120), "h": (42, 32, 24), "b": (60, 40, 30), "w": (240, 240, 235), "i": (30, 60, 110),
          "l": (168, 90, 80), "e": (160, 120, 88), "n": (106, 72, 56), "f": (150, 100, 70)}
CELL = 16


def load(path):
    head, body = path.read_text(encoding="utf-8").split("\n", 1)
    nx, ny, nz = map(int, head.split())
    return nx, ny, nz, body


def views(path):
    nx, ny, nz, body = load(path)
    at = lambda x, y, z: SYMBOLS[ord(body[x + nx * (y + ny * z)]) and SYMBOLS.index(body[x + nx * (y + ny * z)])]
    cell = lambda x, y, z: body[x + nx * (y + ny * z)]
    front = [[next((cell(x, y, z) for z in range(nz - 1, -1, -1) if cell(x, y, z) != "."), ".") for x in range(nx)] for y in range(ny - 1, -1, -1)]
    # 横: 本人の右（-X）から見る。左が後ろ、右が鼻。
    side = [[next((cell(x, y, z) for x in range(nx) if cell(x, y, z) != "."), ".") for z in range(nz)] for y in range(ny - 1, -1, -1)]
    # 上: 鼻が上。
    top = [[next((cell(x, y, z) for y in range(ny - 1, -1, -1) if cell(x, y, z) != "."), ".") for x in range(nx)] for z in range(nz - 1, -1, -1)]
    return front, side, top


def draw(grid):
    h, w = len(grid), len(grid[0])
    img = Image.new("RGB", (w * CELL, h * CELL))
    d = ImageDraw.Draw(img)
    for y, row in enumerate(grid):
        for x, c in enumerate(row):
            d.rectangle([x * CELL, y * CELL, (x + 1) * CELL - 1, (y + 1) * CELL - 1], fill=COLORS[c])
    return img


def strip(images, labels):
    pad = 8
    w = sum(i.width for i in images) + pad * (len(images) + 1)
    h = max(i.height for i in images) + 28
    out = Image.new("RGB", (w, h), (20, 20, 24))
    d = ImageDraw.Draw(out)
    x = pad
    for img, label in zip(images, labels):
        d.text((x, 6), label, fill=(230, 230, 230))
        out.paste(img, (x, 24))
        x += img.width + pad
    return out


def ref(name, size):
    img = Image.open(REFS / "base" / f"base_{name}.png").convert("RGB")
    return img.resize((size[0] * CELL, size[1] * CELL), Image.NEAREST)


def main():
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    ref_imgs = [ref("front", (20, 28)), ref("side", (20, 28)), ref("top", (20, 20))]
    for f in sorted(src.glob("*.txt")):
        if f.stem.startswith("part_"):
            continue
        fr, sd, tp = views(f)
        strip(ref_imgs + [draw(fr), draw(sd), draw(tp)], ["ref front", "ref side", "ref top", f"{f.stem} front", "side", "top"]).save(out / f"compare_{f.stem}.png")
    for cat in ["eyes", "brows", "mouth", "nose", "ears", "jaw", "hair", "beard"]:
        panels, labels = [], []
        for i in range(1, 7):
            f = src / f"part_{cat}_{i}.txt"
            if not f.exists():
                continue
            fr, sd, tp = views(f)
            panels += [draw(fr), draw(sd)]
            labels += [f"{cat}.{i} front", "side"]
        if panels:
            small = [p.resize((p.width // 2, p.height // 2), Image.NEAREST) for p in panels]
            strip(small, labels).save(out / f"parts_{cat}.png")


if __name__ == "__main__":
    main()
