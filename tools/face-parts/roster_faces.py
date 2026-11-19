"""名簿 18 人の "face"（部品の重み・値・肌髪瞳の色）を roster/<id>.json に書く。

出どころ: docs/plans/face_refs/fighters/faces.txt（Codex の肖像の説明）と <id>.png の肖像。
使い方（リポジトリの根で）:  py -3 tools/face-parts/roster_faces.py
何度でも実行できる（"face" があれば置き換える）。部品の id は games/MiniRing/Voxels/FaceParts/*.json、値は FaceParts（FaceTemplates.cs）。
人種・国の見た目の違いは、種類の分岐ではなく、このファイルの連続の値（まぶた・眉の張り出し・唇・髪質・顔の形の部品の重み、寸法の値、色）。
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ROSTER = ROOT / "games/MiniRing/Characters/roster"


def hexc(h):
    h = h.lstrip("#")
    return [round(int(h[i:i + 2], 16) / 255.0, 3) for i in (0, 2, 4)]


# 瞳（参考図 D の凡例: 暗い茶・茶・ヘーゼル・緑・灰・青）。
IRIS = {"dark": "#302017", "brown": "#654127", "hazel": "#88733E", "green": "#60734E", "grey": "#7B8588", "blue": "#597D9B",
        "greygreen": "#6E7C6B", "bluegrey": "#6C8191", "amber": "#7A5230"}

# id -> (肌, 髪, 瞳, 部品の重み, 値)。肌・髪は faces.txt の hex。
FIGHTERS = {
    # 沖縄（首里）の空手家: 二重で奥目気味・眉の張り出し・広めの顔と高い頬。
    "akira": ("#C49370", "#191A19", "dark",
              {"hair": {"hair.2": 0.5, "hair.8": 0.5}, "brows": {"brows.2": 0.5, "brows.4": 0.5}, "eyes": {"eyes.3": 0.5, "eyes.4": 0.5},
               "nose": {"nose.1": 0.6, "nose.2": 0.4}, "mouth": {"mouth.8": 1.0}, "jaw": {"jaw.9": 0.6, "jaw.3": 0.4},
               "ears": {"ears.1": 0.5, "ears.2": 0.5}, "beard": {}, "browridge": {"browridge.2": 0.5, "browridge.4": 0.5}},
              {"cheekbones": 1.35, "faceWidth": 1.05, "noseWidth": 1.1, "browThickness": 1.25, "browTilt": 0.12}),
    # 英国（シェフィールド）: 長くまっすぐな高い鼻・深い目・長い角ばったあご・大きなカールのひげ。
    "albert": ("#D9AD94", "#493025", "hazel",
               {"hair": {"hair.8": 0.7, "hair.1": 0.3}, "brows": {"brows.3": 0.6, "brows.2": 0.4}, "eyes": {"eyes.4": 1.0},
                "nose": {"nose.5": 0.6, "nose.1": 0.4}, "mouth": {"mouth.7": 1.0}, "jaw": {"jaw.8": 0.5, "jaw.9": 0.5},
                "ears": {"ears.2": 0.5, "ears.3": 0.5}, "beard": {"beard.2": 1.0}, "browridge": {"browridge.3": 1.0}},
               {"cheekbones": 1.5, "noseLength": 1.25, "nose": 1.2, "jaw": 1.1, "browThickness": 1.2, "faceWidth": 0.97, "mouthWidth": 0.9}),
    # 沖縄（名護）の棒術家: 細長い目・高い頬骨・細い顔・長い鼻・髪を後ろで結ぶ。
    "daichi": ("#BD906B", "#20201D", "dark",
               {"hair": {"hair.7": 0.5, "hair.5": 0.5}, "brows": {"brows.1": 0.6, "brows.4": 0.4}, "eyes": {"eyes.3": 0.5, "eyes.2": 0.5},
                "nose": {"nose.1": 0.5, "nose.5": 0.5}, "mouth": {"mouth.8": 0.6, "mouth.7": 0.4}, "jaw": {"jaw.8": 0.5, "jaw.6": 0.5},
                "ears": {"ears.2": 1.0}, "beard": {}, "browridge": {"browridge.2": 0.6, "browridge.4": 0.4}},
               {"cheekbones": 1.5, "noseLength": 1.15, "jaw": 1.1, "browTilt": 0.15, "eyeSize": 0.95, "faceWidth": 0.97}),
    # ロシア（北）: 青白い肌・短い髪・奥目で小さい灰色の目・つぶれた太い鼻・大きな耳・大きな四角いあご。
    "dmitri": ("#D3B3A4", "#79614A", "grey",
               {"hair": {"hair.1": 1.0}, "brows": {"brows.2": 0.5, "brows.6": 0.5}, "eyes": {"eyes.4": 1.0},
                "nose": {"nose.6": 0.6, "nose.2": 0.4}, "mouth": {"mouth.7": 1.0}, "jaw": {"jaw.9": 0.5, "jaw.2": 0.5},
                "ears": {"ears.4": 1.0}, "beard": {"beard.1": 1.0}, "browridge": {"browridge.4": 1.0}},
               {"cheekbones": 1.3, "eyeSize": 0.85, "noseWidth": 1.3, "jawWidth": 1.2, "faceWidth": 1.14, "earSize": 1.3, "browThickness": 1.4, "mouthWidth": 1.1}),
    # 英国（ヨーク）: 波打つ栗色の髪・太い眉・まぶたの重い灰緑の目・強い鼻・短いあごひげ。
    "edgar": ("#C69F85", "#51382A", "greygreen",
              {"hair": {"hair.8": 0.5, "hair.9": 0.5}, "brows": {"brows.6": 1.0}, "eyes": {"eyes.6": 0.6, "eyes.4": 0.4},
               "nose": {"nose.3": 0.4, "nose.1": 0.6}, "mouth": {"mouth.8": 0.7, "mouth.9": 0.3}, "jaw": {"jaw.2": 0.5, "jaw.9": 0.5},
               "ears": {"ears.1": 0.5, "ears.2": 0.5}, "beard": {"beard.7": 1.0}, "browridge": {"browridge.2": 0.5, "browridge.3": 0.5}},
              {"cheekbones": 1.4, "noseLength": 1.15, "jawWidth": 1.1, "browThickness": 1.4}),
    # フランス（カルカソンヌ）: オリーブの肌・後退した額・鷲鼻・細長い耳・とがった口ひげとあごひげ。
    "gaston": ("#C59F81", "#342821", "brown",
               {"hair": {"hair.7": 0.6, "hair.2": 0.4}, "brows": {"brows.4": 1.0}, "eyes": {"eyes.6": 0.6, "eyes.3": 0.4},
                "nose": {"nose.3": 1.0}, "mouth": {"mouth.8": 0.6, "mouth.7": 0.4}, "jaw": {"jaw.8": 0.5, "jaw.6": 0.5},
                "ears": {"ears.5": 1.0}, "beard": {"beard.8": 0.5, "beard.3": 0.5}, "browridge": {"browridge.3": 1.0}},
               {"cheekbones": 1.5, "noseLength": 1.2, "nose": 1.15, "hairline": 0.07, "faceWidth": 0.95, "browTilt": 0.2, "mouthWidth": 0.9}),
    # 日本（高知）の剣士: 細い一重の目・細い鼻・卵形の細面・髪を後ろで結ぶ。
    "jin": ("#D5B69A", "#171C22", "dark",
            {"hair": {"hair.13": 1.0}, "brows": {"brows.1": 0.5, "brows.2": 0.5}, "eyes": {"eyes.1": 0.7, "eyes.6": 0.3},
             "nose": {"nose.1": 0.5, "nose.4": 0.5}, "mouth": {"mouth.7": 0.6, "mouth.8": 0.4}, "jaw": {"jaw.10": 0.5, "jaw.1": 0.5},
             "ears": {"ears.1": 1.0}, "beard": {}, "browridge": {"browridge.1": 1.0}},
            {"cheekbones": 1.2, "noseWidth": 0.85, "faceWidth": 0.95, "browTilt": 0.08, "eyeSize": 0.95}),
    # ブラジル（サンパウロ）: 暖かい褐色の肌・縮れ髪のクレスト・とがったあごと高い頬・ふっくらした唇・小さなあごひげ。
    "joao": ("#AD7953", "#201B17", "amber",
             {"hair": {"hair.4": 0.5, "hair.9": 0.5}, "brows": {"brows.4": 1.0}, "eyes": {"eyes.3": 0.6, "eyes.5": 0.4},
              "nose": {"nose.2": 0.5, "nose.4": 0.5}, "mouth": {"mouth.9": 1.0}, "jaw": {"jaw.1": 0.5, "jaw.6": 0.5},
              "ears": {"ears.1": 1.0}, "beard": {"beard.3": 1.0}, "browridge": {"browridge.2": 1.0}},
             {"cheekbones": 1.55, "noseWidth": 1.15, "faceWidth": 0.98, "browTilt": 0.2, "mouthWidth": 1.05}),
    # ドイツ（ニュルンベルク）: 淡い肌・肩までの金髪・奥目の青灰の目・長くまっすぐな鼻・長い四角いあご・短いひげ。
    "konrad": ("#D2AD92", "#887044", "bluegrey",
               {"hair": {"hair.12": 1.0}, "brows": {"brows.4": 0.5, "brows.6": 0.5}, "eyes": {"eyes.4": 1.0},
                "nose": {"nose.5": 0.5, "nose.1": 0.5}, "mouth": {"mouth.7": 1.0}, "jaw": {"jaw.8": 0.5, "jaw.9": 0.5},
                "ears": {"ears.5": 1.0}, "beard": {"beard.7": 1.0}, "browridge": {"browridge.3": 1.0}},
               {"cheekbones": 1.45, "noseLength": 1.2, "jaw": 1.1, "browThickness": 1.3, "faceWidth": 1.0}),
    # アメリカ（デトロイト）: 濃い褐色の肌・短い縮れ毛・奥目・幅広い鼻・広い口・大きな四角いあご・ひげ。
    "marcus": ("#704A35", "#171514", "dark",
               {"hair": {"hair.10": 1.0}, "brows": {"brows.2": 0.6, "brows.6": 0.4}, "eyes": {"eyes.4": 0.6, "eyes.6": 0.4},
                "nose": {"nose.2": 0.7, "nose.6": 0.3}, "mouth": {"mouth.9": 0.6, "mouth.3": 0.4}, "jaw": {"jaw.9": 0.4, "jaw.2": 0.4, "jaw.11": 0.2},
                "ears": {"ears.2": 1.0}, "beard": {"beard.7": 1.0}, "browridge": {"browridge.2": 1.0}},
               {"cheekbones": 1.3, "noseWidth": 1.3, "mouthWidth": 1.15, "jawWidth": 1.2, "faceWidth": 1.14, "browHeight": -0.03, "browThickness": 1.4, "lips": 1.1}),
    # タイ（ブリーラム）: 青銅色の肌・短い刈り上げ・細い一重気味の目・低い鼻筋と丸い鼻先・高く広い頬。
    "narong": ("#B78053", "#191917", "dark",
               {"hair": {"hair.2": 1.0}, "brows": {"brows.1": 1.0}, "eyes": {"eyes.1": 0.5, "eyes.2": 0.5},
                "nose": {"nose.4": 0.5, "nose.2": 0.5}, "mouth": {"mouth.8": 0.7, "mouth.9": 0.3}, "jaw": {"jaw.9": 0.4, "jaw.3": 0.6},
                "ears": {"ears.1": 1.0}, "beard": {}, "browridge": {"browridge.1": 1.0}},
               {"cheekbones": 1.45, "noseWidth": 1.2, "faceWidth": 1.06, "nose": 0.9, "eyeSize": 0.95}),
    # メキシコ（モンテレイ）: 銅色の肌・平らな黒髪・小さな奥目・つぶれて曲がった鼻・口ひげ・短く太いあご。
    "ramon": ("#AE7854", "#211B17", "brown",
              {"hair": {"hair.2": 1.0}, "brows": {"brows.2": 1.0}, "eyes": {"eyes.4": 1.0},
               "nose": {"nose.2": 0.5, "nose.6": 0.5}, "mouth": {"mouth.3": 0.5, "mouth.8": 0.5}, "jaw": {"jaw.2": 0.6, "jaw.9": 0.4},
               "ears": {"ears.4": 0.5, "ears.2": 0.5}, "beard": {"beard.2": 1.0}, "browridge": {"browridge.2": 0.5, "browridge.3": 0.5}},
              {"cheekbones": 1.4, "eyeSize": 0.88, "noseWidth": 1.25, "jawWidth": 1.2, "faceWidth": 1.12, "browThickness": 1.4, "nose": 0.95}),
    # 日本（熊本）の柔術家: 二重のまぶたと丸みのある目・太い眉・つぶれた耳・四角く広いあご・薄いひげ。
    "ren": ("#C7A282", "#1E1B19", "dark",
            {"hair": {"hair.2": 0.5, "hair.8": 0.5}, "brows": {"brows.2": 0.7, "brows.4": 0.3}, "eyes": {"eyes.6": 0.5, "eyes.3": 0.5},
             "nose": {"nose.1": 1.0}, "mouth": {"mouth.8": 1.0}, "jaw": {"jaw.9": 0.5, "jaw.7": 0.5},
             "ears": {"ears.4": 1.0}, "beard": {"beard.1": 1.0}, "browridge": {"browridge.2": 1.0}},
            {"cheekbones": 1.25, "faceWidth": 1.1, "browThickness": 1.3, "jawWidth": 1.1, "earSize": 1.15}),
    # アイルランド（コーク）: 淡い肌とそばかす・赤い巻き毛・奥目の緑の目・曲がった鼻・突き出た耳・赤いひげ。
    "sean": ("#D6AC94", "#914928", "green",
             {"hair": {"hair.9": 0.5, "hair.8": 0.5}, "brows": {"brows.6": 1.0}, "eyes": {"eyes.4": 1.0},
              "nose": {"nose.6": 0.5, "nose.3": 0.5}, "mouth": {"mouth.7": 1.0}, "jaw": {"jaw.9": 0.5, "jaw.5": 0.5},
              "ears": {"ears.3": 1.0}, "beard": {"beard.7": 1.0}, "browridge": {"browridge.2": 0.5, "browridge.3": 0.5}},
             {"cheekbones": 1.35, "freckles": 0.9, "faceWidth": 1.04, "earSize": 1.3, "browThickness": 1.35, "nose": 1.1}),
    # 韓国（釜山）: 淡い黄みの肌・一重の細い目・平らな鼻筋・小さな鼻・Vラインの細いあご・高い頬骨・きっちりした髪。
    "taeyun": ("#D7B698", "#1B1B1D", "dark",
               {"hair": {"hair.7": 0.8, "hair.2": 0.2}, "brows": {"brows.1": 0.5, "brows.2": 0.5}, "eyes": {"eyes.1": 0.8, "eyes.2": 0.2},
                "nose": {"nose.1": 0.5, "nose.4": 0.5}, "mouth": {"mouth.8": 1.0}, "jaw": {"jaw.8": 0.4, "jaw.1": 0.6},
                "ears": {"ears.1": 1.0}, "beard": {}, "browridge": {"browridge.1": 1.0}},
               {"cheekbones": 1.25, "noseWidth": 0.9, "faceWidth": 1.0, "eyeSize": 0.95, "nose": 0.9}),
    # 沖縄（那覇）のトンファー使い: 日焼けした肌・離れた目・短く広い鼻・太い低い眉・広い顔・逆立つ硬い髪。
    "tsuyoshi": ("#BD8B62", "#20201B", "dark",
                 {"hair": {"hair.2": 0.5, "hair.10": 0.5}, "brows": {"brows.2": 1.0}, "eyes": {"eyes.3": 0.5, "eyes.4": 0.5},
                  "nose": {"nose.4": 0.4, "nose.2": 0.6}, "mouth": {"mouth.3": 0.5, "mouth.7": 0.5}, "jaw": {"jaw.7": 0.5, "jaw.9": 0.5},
                  "ears": {"ears.3": 1.0}, "beard": {}, "browridge": {"browridge.4": 1.0}},
                 {"cheekbones": 1.5, "eyeSpacing": 1.15, "noseWidth": 1.3, "noseLength": 0.9, "faceWidth": 1.14, "browHeight": -0.03, "browThickness": 1.4}),
    # インド（ジョードプル）: 暖かい褐色の肌・波打つ黒髪・深い目と太いアーチ形の眉・鷲鼻・上へ巻いた口ひげと短いひげ。
    "vikram": ("#A36E48", "#1C1714", "dark",
               {"hair": {"hair.8": 0.7, "hair.7": 0.3}, "brows": {"brows.3": 0.5, "brows.6": 0.5}, "eyes": {"eyes.4": 1.0},
                "nose": {"nose.3": 0.8, "nose.5": 0.2}, "mouth": {"mouth.9": 1.0}, "jaw": {"jaw.9": 0.5, "jaw.4": 0.5},
                "ears": {"ears.5": 1.0}, "beard": {"beard.2": 0.5, "beard.7": 0.5}, "browridge": {"browridge.3": 1.0}},
               {"cheekbones": 1.55, "noseLength": 1.2, "nose": 1.1, "browThickness": 1.4, "faceWidth": 1.0}),
    # キューバ（ハバナ）: 濃い褐色の肌・短い縮れ毛のフェード・まぶたの厚い目・丸い鼻先・ふっくらした唇。
    "yoandry": ("#8B5C3F", "#181512", "brown",
                {"hair": {"hair.10": 1.0}, "brows": {"brows.3": 1.0}, "eyes": {"eyes.6": 0.5, "eyes.3": 0.5},
                 "nose": {"nose.4": 0.4, "nose.2": 0.6}, "mouth": {"mouth.9": 0.7, "mouth.10": 0.3}, "jaw": {"jaw.3": 0.5, "jaw.9": 0.5},
                 "ears": {"ears.1": 1.0}, "beard": {}, "browridge": {"browridge.2": 1.0}},
                {"cheekbones": 1.4, "noseWidth": 1.15, "faceWidth": 1.02, "lips": 1.1}),
}


def fmt_weights(w):
    return "{ " + ", ".join(f'"{k}": {v}' for k, v in w.items()) + " }"


def block(skin, hair, iris, parts, values):
    lines = ['  "face": {', '    "parts": {']
    items = list(parts.items())
    for i, (cat, w) in enumerate(items):
        lines.append(f'      "{cat}": {fmt_weights(w)}' + ("," if i < len(items) - 1 else ""))
    lines.append("    },")
    lines.append('    "values": { ' + ", ".join(f'"{k}": {v}' for k, v in values.items()) + " },")
    lines.append(f'    "skin": {json.dumps(hexc(skin))}, "hair": {json.dumps(hexc(hair))}, "iris": {json.dumps(hexc(IRIS[iris]))}')
    lines.append("  },")
    return "\n".join(lines) + "\n"


def main():
    for fid, (skin, hair, iris, parts, values) in FIGHTERS.items():
        path = ROSTER / f"{fid}.json"
        text = path.read_text(encoding="utf-8")
        crlf = "\r\n" in text
        text = text.replace("\r\n", "\n")
        text = re.sub(r'  "face": \{\n.*?\n  \},\n', "", text, flags=re.S)
        marker = '  "body": {'
        assert text.count(marker) == 1, fid
        text = text.replace(marker, block(skin, hair, iris, parts, values) + marker)
        json.loads(text)
        path.write_text(text.replace("\n", "\r\n") if crlf else text, encoding="utf-8", newline="")
    print("wrote", len(FIGHTERS))


if __name__ == "__main__":
    main()
