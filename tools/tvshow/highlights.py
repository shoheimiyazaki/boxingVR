"""ハイライト番組: 札（tools/tvshow/cards/*.json）の撮影・声入れ済みの試合（<out>/bN_live.mp4 と bN.events.json）から、
見どころの場面だけを切り出してつなぐ。

場面は中継の記録（events.json）の出来事から決める（映像を見て選ばない。同じ記録なら同じ番組）:
  - 番組の始め: 1 試合目の「本日のプログラム」の表（その日の全試合の顔ぶれ）
  - 試合ごと: 冒頭のタイトル（両者の名前）→ ダウンのたびに前 --before 秒・後 --after 秒 → 決着（KO の前 --before 秒から勝敗の画面の終わりまで）
    判定の試合は最後の --decision 秒と勝敗の画面
  - 近い場面（--gap 秒以内）は 1 つにまとめる。切れ目の音は短くフェードする

使い方:
  py -3 tools/tvshow/highlights.py tools/tvshow/cards/ivf-prospects-highlights-s1.json [--before 8] [--after 6] [--part-mib 29]
出力: <out>/highlights_full.mp4 と、送れる大きさ（--part-mib）ごとの highlights_partN.mp4、場面の表 highlights.segments.json
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile


def load_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def first(events, kind):
    return next((e for e in events if e["type"] == kind), None)


def segments_of(events, index, before, after, decision, gap):
    """1 試合の場面（[始め, 終わり, 理由]）。"""
    end = first(events, "end")["t"]
    segs = []
    if index == 0 and (program := first(events, "program")) and (opening := first(events, "opening")):
        segs.append([program["t"], opening["t"], "番組の始め（本日のプログラム）"])
    if opening := first(events, "opening"):
        segs.append([opening["t"], opening["t"] + 7.0, "冒頭のタイトル"])
    for e in events:
        if e["type"] == "knockdown":
            segs.append([e["t"] - before, e["t"] + after, f"ダウン: {e['text'][:30]}"])
    ko = first(events, "ko")
    result = first(events, "result")
    if ko:
        segs.append([ko["t"] - before, end, "決着と勝敗の画面"])
    elif result:
        segs.append([result["t"] - decision, end, "判定の終わりと勝敗の画面"])
    segs.sort(key=lambda s: s[0])
    merged = []
    for s in segs:
        s[0] = max(0.0, s[0])
        s[1] = min(end, s[1])
        if merged and s[0] <= merged[-1][1] + gap:
            merged[-1][1] = max(merged[-1][1], s[1])
            merged[-1][2] += " / " + s[2]
        else:
            merged.append(s)
    return merged


def run(args):
    subprocess.run(args, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def cut(video, start, stop, dest, fade=0.2):
    dur = stop - start
    run(["ffmpeg", "-y", "-ss", f"{start:.3f}", "-i", video, "-t", f"{dur:.3f}",
         "-vf", "scale=960:-2,fps=30", "-af", f"afade=in:d={fade},afade=out:st={max(0.0, dur - fade):.3f}:d={fade},aresample=48000",
         "-c:v", "libx264", "-preset", "medium", "-crf", "30", "-pix_fmt", "yuv420p",
         "-c:a", "aac", "-b:a", "96k", "-ac", "2", dest])


def concat(parts, dest):
    with tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, encoding="utf-8") as f:
        for p in parts:
            f.write(f"file '{os.path.abspath(p)}'\n")
        listing = f.name
    try:
        run(["ffmpeg", "-y", "-f", "concat", "-safe", "0", "-i", listing, "-c", "copy", "-movflags", "+faststart", dest])
    finally:
        os.remove(listing)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("card")
    ap.add_argument("--before", type=float, default=8.0, help="ダウン・KO の何秒前から")
    ap.add_argument("--after", type=float, default=6.0, help="ダウンの何秒後まで")
    ap.add_argument("--decision", type=float, default=15.0, help="判定の試合の最後の何秒")
    ap.add_argument("--gap", type=float, default=3.0, help="これより近い場面は 1 つに")
    ap.add_argument("--part-mib", type=float, default=None, help="部分の大きさの上限 [MiB]（既定は札の partLimitMiB か 29）")
    a = ap.parse_args()

    repo = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    card = load_json(a.card)
    out = os.path.join(repo, card.get("out", "media/highlights"))
    limit = (a.part_mib or card.get("partLimitMiB", 29)) * 1024 * 1024
    table = []
    clips = []
    work = os.path.join(out, "highlights_work")
    os.makedirs(work, exist_ok=True)
    for i, bout in enumerate(card["bouts"]):
        key = f"b{i + 1}"
        video = os.path.join(out, f"{key}_live.mp4")
        if not os.path.exists(video):
            sys.exit(f"{video} がありません（tvshow.ps1 -Stage dub を先に）。")
        events = load_json(os.path.join(out, f"{key}.events.json"))["events"]
        for j, (s, e, why) in enumerate(segments_of(events, i, a.before, a.after, a.decision, a.gap)):
            dest = os.path.join(work, f"{key}_{j:02d}.mp4")
            cut(video, s, e, dest)
            clips.append((key, dest))
            table.append({"bout": key, "label": bout.get("bout", key), "from": round(s, 2), "to": round(e, 2), "seconds": round(e - s, 2), "why": why})
            print(f"{key} {s:7.2f}–{e:7.2f}（{e - s:5.1f} s）{why}")

    full = os.path.join(out, "highlights_full.mp4")
    concat([c for _, c in clips], full)
    with open(os.path.join(out, "highlights.segments.json"), "w", encoding="utf-8") as f:
        json.dump(table, f, ensure_ascii=False, indent=2)

    # 送れる大きさごとの部分（場面の切れ目で分ける。1 場面が上限を超えることはない長さ）。
    parts, current, size = [], [], 0
    for _, c in clips:
        s = os.path.getsize(c)
        if current and size + s > limit:
            parts.append(current)
            current, size = [], 0
        current.append(c)
        size += s
    if current:
        parts.append(current)
    for n, p in enumerate(parts, 1):
        dest = os.path.join(out, f"highlights_part{n}.mp4")
        concat(p, dest)
        print(f"{dest}: {len(p)} 場面 {os.path.getsize(dest) / 1048576:.1f} MiB")
    total = sum(t["seconds"] for t in table)
    print(f"{full}: {len(table)} 場面 {total:.0f} s")


if __name__ == "__main__":
    main()
