"""Codex の参考図の文字の格子（docs/plans/face_refs）を、ゲームの部品の表（games/MiniRing/Voxels/FaceParts/*.json）へ写す。

使い方（リポジトリの根で）:  py -3 tools/face-parts/build_face_parts.py
出どころの txt から何度でも同じ JSON を作り直せる（決定的）。部品の大きさは参考図の格子（1 セル = 2.5 mm、頭の箱 20 x 28 x 20）で書き、
組み立て（FaceComposer）が任意の解像度の格子へ標本化する。
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from parse import parse_base, parse_parts  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
REFS = ROOT / "docs/plans/face_refs"
OUT = ROOT / "games/MiniRing/Voxels/FaceParts"

# 文字 -> 材質の名前。e は耳の内側・まぶたの影、d は目の深い窪み（どちらも影になった皮膚）。
LEGEND = {"s": "skin", "h": "hair", "b": "brow", "w": "white", "i": "iris", "l": "lip", "e": "shadow", "d": "shadow", "n": "nostril", ".": "empty"}
# 位置の目印にする材質（その文字のセルの重心を留める点にする）。
MARKERS = {"eyes": "wi", "brows": "b", "mouth": "l"}


def centroid(rows, chars):
    pts = [(x + 0.5, y + 0.5) for y, r in enumerate(rows) for x, c in enumerate(r) if c in chars]
    if not pts:
        raise ValueError(f"目印 {chars!r} が無い格子")
    return [round(sum(p[0] for p in pts) / len(pts), 4), round(sum(p[1] for p in pts) / len(pts), 4)]


def nonempty_center(rows):
    pts = [(x + 0.5, y + 0.5) for y, r in enumerate(rows) for x, c in enumerate(r) if c != "."]
    return [round(sum(p[0] for p in pts) / len(pts), 4), round(sum(p[1] for p in pts) / len(pts), 4)]


def part_json(key, part):
    cat = part["category"]
    views = {k.lower(): {"width": len(v[0]), "height": len(v), "rows": v} for k, v in part["views"].items()}
    front = part["views"]["FRONT"]
    if cat in MARKERS:
        anchor = centroid(front, MARKERS[cat])
    elif cat == "nose":
        # 鼻筋の付け根の高さ（上の端）と、正面の左右の真ん中。横の格子の 0 列目が顔の面。
        anchor = [len(front[0]) / 2.0, 0.0]
        top = next(y for y, r in enumerate(front) if any(c != "." for c in r))
        anchor[1] = top + 0.5
    elif cat == "ears":
        anchor = nonempty_center(part["views"]["SIDE"])
    else:
        anchor = [0.0, 0.0]
    return {"id": key, "name": part["name"], "anchor": anchor, "views": views}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    parts = {**parse_parts(REFS / "parts/parts_a.txt"), **parse_parts(REFS / "parts/parts_b.txt")}
    cats = {}
    for key, part in sorted(parts.items(), key=lambda kv: (kv[1]["category"], kv[1]["index"])):
        cats.setdefault(part["category"], []).append(part_json(key, part))
    for cat, items in cats.items():
        (OUT / f"{cat}.json").write_text(json.dumps({"category": cat, "legend": LEGEND, "parts": items}, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")

    base = parse_base(REFS / "base/base.txt")
    front = base["FRONT"]
    left = [r[:10] for r in front]
    right = [r[10:] for r in front]
    side = base["SIDE"]
    layout = {
        # 基準の顔の目印（参考図の格子の座標、右がプラス・下がプラス）。部品はこの点へ留める。
        "eyeLeftImage": centroid(left, "wi"),
        "eyeRightImage": [round(centroid(right, "wi")[0] + 10, 4), centroid(right, "wi")[1]],
        "browLeftImage": centroid(left, "b"),
        "browRightImage": [round(centroid(right, "b")[0] + 10, 4), centroid(right, "b")[1]],
        "mouth": centroid(front, "l"),
        "earSide": centroid(side, "e"),
        "noseTopRow": 13.5,
        # 横の輪郭の鼻の出っ張り（全ての顎の型の横の輪郭に焼き込まれている）を均す: この行の範囲は、前へ出る端をこの列までに切る。
        "noseRows": [13, 17],
        "profileCap": 16,
        # 鼻の横の格子の 0 列目が置かれる、前後の位置（後ろから数えたセル。先端は 4 列先 = 頭の箱の前の端）。
        "noseSurface": 15,
    }
    base_json = {"layout": layout, "legend": LEGEND, "views": {k.lower(): {"width": len(v[0]), "height": len(v), "rows": v} for k, v in base.items() if k != "DEPTH"}}
    (OUT / "base.json").write_text(json.dumps(base_json, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print("wrote", sorted(p.name for p in OUT.glob("*.json")), layout)


if __name__ == "__main__":
    main()
