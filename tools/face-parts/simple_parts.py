"""目と眉の部品を簡単な形に描き替える（2026-11-20、ユーザーの指示）。

- 目: 白目は幅 3〜4・高さ 2〜3、黒目は 1 セル（まぶたの影は描かない）。一重・二重などの部品の id は残し、4 つの形に割り当てる。
- 眉: 途切れない（4 近傍でつながった）形。目から 1 マス以上離すのは FaceComposer が置くときに行う（眉の高さの値と目の大きさで変わるため）。

build_face_parts.py が参考図から作った表の、目（eyes.json）と眉（brows.json）の正面の絵だけを置き換える。部品の id・数・横の図は残す。
何度でも同じ JSON になる。使い方（リポジトリの根で）: py -3 tools/face-parts/build_face_parts.py && py -3 tools/face-parts/simple_parts.py
"""
import json
from pathlib import Path

PARTS = Path(__file__).resolve().parents[2] / "games/MiniRing/Voxels/FaceParts"

# 白目は幅 3〜4・高さ 2〜3（8 x 5 の絵、左の列 = 目じり、右 = 目頭）。黒目は 1 セル（2026-11-20、ユーザーの指示）: 幅 3 は真ん中の列、
# 幅 4 は真ん中 2 列のうち目頭側（相手を見る向き。左右の目で鏡写しになる）。高さ 2 は上の段（上まぶたに触れる黒目）、高さ 3 は真ん中の段。
W4H2 = ([4.0, 2.0], ["ssssssss", "sswwiwss", "sswwwwss", "ssssssss", "ssssssss"])
W4H3 = ([4.0, 2.5], ["ssssssss", "sswwwwss", "sswwiwss", "sswwwwss", "ssssssss"])
W3H2 = ([3.5, 2.0], ["ssssssss", "sswiwsss", "sswwwsss", "ssssssss", "ssssssss"])
W3H3 = ([3.5, 2.5], ["ssssssss", "sswwwsss", "sswiwsss", "sswwwsss", "ssssssss"])
EYES = {
    "eyes.1": W4H2,  # 一重: 横長
    "eyes.2": W3H2,  # 蒙古ひだ: 小さめ
    "eyes.3": W4H3,  # 二重
    "eyes.4": W4H3,  # 奥目
    "eyes.5": W3H3,  # 大きく開いた目（黒目が小さく見える）
    "eyes.6": W4H2,  # 眠そうな目
}

# 眉（8 x 3、左の列 = 眉じり）。どれも 4 近傍でつながる。留める点は元の部品のまま。
BROWS = {
    "brows.1": ["ssssssss", "sbbbbbbs", "ssssssss"],  # 細くまっすぐ
    "brows.2": ["sbbbbbbs", "sbbbbbbs", "ssssssss"],  # 太くまっすぐ
    "brows.3": ["ssbbbbss", "sbbssbbs", "ssssssss"],  # 弓なり
    "brows.4": ["sbbsssss", "ssbbbsss", "ssssbbbs"],  # 吊り上がり（眉頭が下）
    "brows.5": ["ssssbbbs", "ssbbbsss", "sbbsssss"],  # 下がり眉（眉じりが下）
    "brows.6": ["sbbbbbbs", "bbbbbbbs", "sbbbbbss"],  # 濃い眉
}


def rewrite(category, table, anchors):
    path = PARTS / f"{category}.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    for part in data["parts"]:
        entry = table[part["id"]]
        anchor, rows = entry if anchors else (None, entry)
        view = part["views"]["front"]
        assert len(rows) == view["height"] and all(len(r) == view["width"] for r in rows), part["id"]
        view["rows"] = rows
        if anchor is not None:
            part["anchor"] = anchor
    text = json.dumps(data, ensure_ascii=False, indent=1) + "\n"
    path.write_bytes(text.replace("\n", "\r\n").encode("utf-8"))  # 元のファイルと同じ CRLF・1 字の字下げ


if __name__ == "__main__":
    rewrite("eyes", EYES, anchors=True)
    rewrite("brows", BROWS, anchors=False)
    print("rewrote eyes, brows in", PARTS)
