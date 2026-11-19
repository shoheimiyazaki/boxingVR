"""Codex の参考図の文字の格子（docs/plans/face_refs）を、ゲームの部品の表（games/MiniRing/Voxels/FaceParts/*.json）へ写す。

使い方（リポジトリの根で）:  py -3 tools/face-parts/build_face_parts.py
出どころの txt から何度でも同じ JSON を作り直せる（決定的）。部品の大きさは参考図の格子（1 セル = 2.5 mm、頭の箱 20 x 28 x 20）で書き、
組み立て（FaceComposer）が任意の解像度の格子へ標本化する。
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from parse import parse_base, parse_parts, parse_d  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
REFS = ROOT / "docs/plans/face_refs"
OUT = ROOT / "games/MiniRing/Voxels/FaceParts"

# 文字 -> 材質の名前。e は耳の内側・まぶたの影、d は目の深い窪み（どちらも影になった皮膚）。
LEGEND = {"s": "skin", "h": "hair", "b": "brow", "w": "white", "i": "iris", "l": "lip", "e": "shadow", "d": "shadow", "k": "shadow", "f": "freckle", "n": "nostril", ".": "empty"}
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


def view_json(rows):
    return {"width": len(rows[0]), "height": len(rows), "rows": rows}


def swap(rows, table):
    return ["".join(table.get(c, c) for c in r) for r in rows]


# 髪が頭の輪郭の外へ足してよいセル数（参考図のセル。組み立ての髪の「届く範囲」）。普通の髪は 1〜2 セル、量のある髪（アフロ・ポニーテール・長髪）だけ大きい。
HAIR_REACH = {"hair.1": 1.5, "hair.2": 1.5, "hair.3": 0.0, "hair.4": 2.0, "hair.5": 4.0, "hair.6": 4.5,
              "hair.7": 1.5, "hair.8": 2.0, "hair.9": 2.5, "hair.10": 2.0, "hair.11": 2.0, "hair.12": 6.0, "hair.13": 6.0}


# 髪質のばらつき（0 = まっすぐ、1 = 縮れ）。頭の輪郭の外に足す毛先の割合（FaceComposer）。
HAIR_FRIZZ = {"hair.1": 0.0, "hair.2": 0.0, "hair.3": 0.0, "hair.4": 0.2, "hair.5": 0.0, "hair.6": 0.7,
              "hair.7": 0.0, "hair.8": 0.3, "hair.9": 0.6, "hair.10": 0.85, "hair.11": 0.8, "hair.12": 0.0, "hair.13": 0.0}


def eye_part(index, name, key, d):
    """parts_d の目（8x5）。眉（b）は眉の部位が描くので皮膚へ、目の行の両端の影と目の下の影は消す（白目・黒目は 3〜4 セル幅、まぶたの線は目の上だけ）。"""
    front = d[key]["rows"]
    eye_rows = [y for y, r in enumerate(front) if any(c in "wi" for c in r)]
    out = []
    for y, r in enumerate(front):
        r = r.replace("b", "s")
        if y >= min(eye_rows):
            r = r.replace("k", "s")
        out.append(r)
    # まぶたの線（k）は、目（白目・黒目）の幅の内側だけ（横へはみ出すと厚いアイシャドウに見える）。
    cols = [x for r in front for x, c in enumerate(r) if c in "wi"]
    lo, hi = min(cols), max(cols)
    out = ["".join(c if c != "k" or lo <= x <= hi else "s" for x, c in enumerate(r)) for r in out]
    side = swap(d[key + "_side"]["rows"], {"b": "s"})
    return {"id": f"eyes.{index}", "name": name, "anchor": centroid(out, "wi"),
            "views": {"front": view_json(out), "side": view_json(side)}}


def solidify(rows, pad=(1, 1)):
    """参考図 D の髪の行は、1 行おきに幅が違う・頭の外に 1 セルだけの毛（髪質のばらつき）が付く。頭の輪郭の格子と重ねると縞になるので、
    髪のかたまりの行（4 セル以上つながった h がある行）は、前後 1 行を含めた幅の範囲を埋め、ばらばらの毛を消す。生え際の毛先（ばらばらの h だけの行）はそのまま。"""
    spans = []
    for r in rows:
        best = None
        i = 0
        while i < len(r):
            if r[i] == "h":
                j = i
                while j < len(r) and r[j] == "h":
                    j += 1
                if j - i >= 4 and (best is None or j - i > best[1] - best[0]):
                    best = (i, j - 1)
                i = j
            else:
                i += 1
        spans.append(best)
    out = []
    for y, r in enumerate(rows):
        if spans[y] is None:
            # 生え際の毛先（額の中のばらばらの毛）は消す。頭の縁（両端 3 セル）の毛（長髪の垂れ）は残す。
            out.append("".join("s" if c == "h" and 3 <= x < len(r) - 3 else c for x, c in enumerate(r)))
            continue
        window = [spans[k] for k in (y - 1, y, y + 1) if 0 <= k < len(rows) and spans[k] is not None]
        lo, hi = max(1, min(w[0] for w in window) - pad[0]), min(len(r) - 2, max(w[1] for w in window) + pad[1])
        out.append("." * lo + "h" * (hi - lo + 1) + "." * (len(r) - hi - 1))
    return out


def build_extra(cats):
    d = parse_d(REFS / "parts2/parts_d.txt")
    # 目: 参考図 D のまぶたの違い（一重・内側に覆いかぶさる・二重・奥目・大きい・厚いまぶた）。顔の寸法は 3〜4 セルの白目 + 黒目（基準の顔と同じ大きさ）。
    eyes = [("monolid", "eye_monolid"), ("epicanthic fold", "eye_epicanthic_low"), ("double eyelid", "eye_double_high"),
            ("deep-set", "eye_deep_set"), ("wide open", "eye_wide_open"), ("heavy-lidded", "eye_heavy_lidded")]
    cats["eyes"] = [eye_part(i + 1, name, key, d) for i, (name, key) in enumerate(eyes)]
    # 口: 唇の厚み・人中の長さ（mouth.7〜12）。留める点は基準の口の行（唇の 1 行目 = 基準の口の行）。人中の長さが唇の位置の違いとして残る。
    lips = [("thin lips", "mouth_thin"), ("medium lips", "mouth_medium"), ("full lips", "mouth_full"), ("very full lips", "mouth_very_full"),
            ("short philtrum", "mouth_philtrum_short"), ("long philtrum", "mouth_philtrum_long")]
    for i, (name, key) in enumerate(lips):
        front = swap(d[key]["rows"], {"n": "s"})
        side = swap(d[key + "_side"]["rows"], {"n": "s"})
        cats["mouth"].append({"id": f"mouth.{7 + i}", "name": name, "anchor": [5.0, 1.5], "views": {"front": view_json(front), "side": view_json(side)}})
    # あご: 顔の形（広い・長い・四角・卵形・下顎が前）。横の輪郭は広い・長い・四角・卵形では「round soft jaw」（jaw.3）を借り、上からの輪郭は参考図 D のもの（参考図は鼻が下。基準は鼻が上なので上下を返す）。
    side3 = next(p for p in cats["jaw"] if p["id"] == "jaw.3")["views"]["side"]
    shapes = [("wide face", "face_wide"), ("long face", "face_long"), ("square face", "face_square"), ("oval face", "face_oval"), ("prognathic lower face", "face_prognathic")]
    for i, (name, key) in enumerate(shapes):
        views = {"front": view_json(d[key + "_front"]["rows"]), "top": view_json(list(reversed(d[key + "_top"]["rows"])))}
        views["side"] = view_json(d[key + "_side"]["rows"]) if key + "_side" in d else side3
        cats["jaw"].append({"id": f"jaw.{7 + i}", "name": name, "anchor": [0.0, 0.0], "views": views})
    # 髪: 髪質（まっすぐ・うねり・ゆるい巻き・細かい縮れ・縮れのハイトップ・後ろへ流した長髪）。頭の外へ出る量は reach（データ）。
    hair = [("straight", "hair_straight"), ("wavy", "hair_wavy"), ("loose curls", "hair_loose_curls"), ("tight coils", "hair_tight_coils"),
            ("coily high top", "hair_high_top"), ("swept-back long", "hair_swept_long")]
    for i, (name, key) in enumerate(hair):
        cats["hair"].append({"id": f"hair.{7 + i}", "name": name, "anchor": [0.0, 0.0],
                             "views": {"front": view_json(solidify(d[key + "_front"]["rows"])), "side": view_json(solidify(d[key + "_side"]["rows"], pad=(2, 1)))}})
    # 眉の張り出しと鼻筋の高さ（横の輪郭 8x12。留める点 = 頭の横の格子の (15, 13)）。
    bridges = [("flat low bridge", "bridge_flat_low"), ("medium bridge", "bridge_medium"), ("high narrow bridge", "bridge_high_narrow"), ("prominent brow ridge", "bridge_prominent")]
    cats["browridge"] = [{"id": f"browridge.{i + 1}", "name": name, "anchor": [4.0, 6.0], "views": {"side": view_json(d[key]["rows"])}} for i, (name, key) in enumerate(bridges)]


def blank(w=20, h=28):
    return [["."] * w for _ in range(h)]


def rows_of(grid):
    return ["".join(r) for r in grid]


def build_beards(cats):
    """ひげ。組み立ては正面の図だけを使い、顔の表面から 3 セルの殻に置く（横の図は検証のために残す）。
    beard.7 = 全体のひげ: もみあげ（行 12〜16 の頭の縁）・頬の下（行 17）・口ひげから下（行 18 以降は頭の幅いっぱい）が 1 つにつながる。唇の所だけはコンポーザが空ける。
    beard.2 = 口ひげ（ハンドルバー、行 19・20 と下がった端）、beard.8 = 細くとがった口ひげ。beard.1 = beard.7 と同じ形の無精ひげ（density < 0.5 = 髪ではなく肌の色を寄せる）。"""
    by_id = {p["id"]: p for p in cats["beard"]}
    base_front = by_id["beard.4"]["views"]["front"]["rows"]

    def head_mask():
        return [[c != "." for c in r] for r in base_front]

    mask = head_mask()

    def paint(cells):
        g = [["s" if mask[y][x] else "." for x in range(20)] for y in range(28)]
        for y, x in cells:
            if mask[y][x]:
                g[y][x] = "h"
        return g

    full = []
    for y in range(12, 17):
        full += [(y, x) for x in (2, 3, 16, 17)]
    full += [(17, x) for x in (2, 3, 4, 5, 14, 15, 16, 17)]
    for y in range(18, 28):
        full += [(y, x) for x in range(2, 18)]
    side = by_id["beard.4"]["views"]["side"]
    cats["beard"] = [p for p in cats["beard"] if p["id"] not in ("beard.2", "beard.7", "beard.8")]
    handlebar = [(19, x) for x in range(5, 15)] + [(20, x) for x in range(5, 15)] + [(21, 4), (21, 15), (22, 4), (22, 15)]
    thin = [(20, x) for x in range(4, 16)] + [(21, 5), (21, 14), (22, 4), (22, 15)]
    cats["beard"].append({"id": "beard.2", "name": "moustache (handlebar)", "anchor": [0.0, 0.0], "views": {"front": view_json(rows_of(paint(handlebar))), "side": side}})
    cats["beard"].append({"id": "beard.7", "name": "full beard with cheeks", "anchor": [0.0, 0.0], "views": {"front": view_json(rows_of(paint(full))), "side": side}})
    cats["beard"].append({"id": "beard.8", "name": "thin pointed moustache", "anchor": [0.0, 0.0], "views": {"front": view_json(rows_of(paint(thin))), "side": side}})
    stubble = next(p for p in cats["beard"] if p["id"] == "beard.1")
    stubble["views"] = {"front": view_json(rows_of(paint([(y, x) for y in range(16, 28) for x in range(2, 18)]))), "side": side}
    stubble["density"] = 0.45
    cats["beard"].sort(key=lambda p: int(p["id"].split(".")[1]))


def build_long_hair(cats):
    """長い髪（髪質の参考図 D の長髪を置き換える）。頭の外（顔の横）へ垂れる髪を、前の図（頭の縁の外の列）と横の図（耳の後ろ・首）で書く。
    hair.12 = 肩までの髪（耳の後ろへ流す）、hair.13 = 後ろで結んだ長髪（顎まで垂れる横髪と、後ろへ下がる尾）。頭の外へ出る量は reach（6）。"""
    d = parse_d(REFS / "parts2/parts_d.txt")
    cap_front = solidify(d["hair_swept_long_front"]["rows"])[:9]
    cap_side = solidify(d["hair_swept_long_side"]["rows"], pad=(2, 1))[:9]

    def make(curtain_x, curtain_to, side_rows, behind=()):
        front = blank()
        side = blank()
        for y in range(9):
            for x, c in enumerate(cap_front[y]):
                front[y][x] = c
            for z, c in enumerate(cap_side[y]):
                side[y][z] = c
        for y in range(9, curtain_to):
            for x in curtain_x:
                front[y][x] = "h"
        for y, zs in side_rows.items():
            for z in zs:
                side[y][z] = "h"
        for y in behind:
            for x in range(6, 14):
                front[y][x] = "h"  # 顔の真後ろ（前から見ると頭に隠れる。尾の幅）
        return {"front": view_json(rows_of(front)), "side": view_json(rows_of(side))}

    shoulder = make((0, 1, 2, 17, 18, 19), 27, {**{y: range(1, 13) for y in range(9, 12)}, **{y: range(1, 6) for y in range(12, 17)}, **{y: range(1, 10) for y in range(17, 28)}})
    tied = make((0, 1, 18, 19), 20, {**{y: range(1, 13) for y in range(9, 13)}, **{y: range(0, 4) for y in range(13, 27)}}, behind=range(13, 27))
    parts = [p for p in cats["hair"] if p["id"] not in ("hair.12", "hair.13")]
    parts.append({"id": "hair.12", "name": "shoulder-length, behind the ears", "anchor": [0.0, 0.0], "views": shoulder})
    parts.append({"id": "hair.13", "name": "long, tied back", "anchor": [0.0, 0.0], "views": tied})
    cats["hair"] = parts


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    parts = {**parse_parts(REFS / "parts/parts_a.txt"), **parse_parts(REFS / "parts/parts_b.txt")}
    cats = {}
    for key, part in sorted(parts.items(), key=lambda kv: (kv[1]["category"], kv[1]["index"])):
        cats.setdefault(part["category"], []).append(part_json(key, part))
    build_extra(cats)
    build_beards(cats)
    build_long_hair(cats)
    for part in cats["hair"]:
        part["reach"] = HAIR_REACH[part["id"]]
        part["frizz"] = HAIR_FRIZZ[part["id"]]
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
        # 鼻筋・眉の張り出しの横の輪郭（browridge）の留める点が置かれる、頭の横の格子の (前後, 高さ)。
        "bridgeAnchor": [15, 13],
    }
    base_json = {"layout": layout, "legend": LEGEND, "views": {k.lower(): {"width": len(v[0]), "height": len(v), "rows": v} for k, v in base.items() if k != "DEPTH"}}
    (OUT / "base.json").write_text(json.dumps(base_json, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print("wrote", sorted(p.name for p in OUT.glob("*.json")), layout)


if __name__ == "__main__":
    main()
