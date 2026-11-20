"""試合の興奮の曲線と見込み p_red を 1 枚の PNG にする（docs/EXCITEMENT.md）。

使い方: py -3 tools/excitement_curve.py <曲線.csv> [<曲線.csv> ...] --out <out.png>
曲線.csv は `excitement --curve <接頭辞>` が書く（t,arousal,p と、#秒,種類,強さ の出来事の行）。
"""
import argparse
import csv
import os

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

MARKS = {
    "Knockdown": ("v", "#d62728"), "KO": ("X", "#7f0000"), "OnePunch": ("*", "#ff7f0e"),
    "Rise": ("^", "#2ca02c"), "Counter": ("D", "#9467bd"), "NearMiss": ("o", "#17becf"),
    "BoneBreak": ("P", "#8c564b"), "LimbLoss": ("P", "#000000"), "HeadSnap": ("s", "#e377c2"),
}


def read(path):
    t, a, p, events = [], [], [], []
    with open(path, encoding="utf-8") as f:
        for row in csv.reader(f):
            if not row or row[0] == "t":
                continue
            if row[0].startswith("#"):
                events.append((float(row[0][1:]), row[1], float(row[2])))
                continue
            t.append(float(row[0]))
            a.append(float(row[1]))
            p.append(float(row[2]))
    return t, a, p, events


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("curves", nargs="+")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    fig, axes = plt.subplots(len(args.curves), 1, figsize=(9, 2.6 * len(args.curves)), squeeze=False)
    for ax, path in zip(axes[:, 0], args.curves):
        t, a, p, events = read(path)
        ax.plot(t, a, color="#1f77b4", lw=1.4, label="arousal")
        ax.set_ylabel("arousal")
        ax.set_title(os.path.basename(path), fontsize=9)
        top = max(a + [1.0])
        for et, kind, _ in events:
            if kind in MARKS:
                m, c = MARKS[kind]
                ax.plot([et], [top * 1.05], marker=m, color=c, ms=7, ls="none")
        ax2 = ax.twinx()
        ax2.plot(t, p, color="#d62728", lw=1.2, ls="--", label="p_red")
        ax2.axhline(0.5, color="#999999", lw=0.6)
        ax2.set_ylim(0, 1)
        ax2.set_ylabel("p_red")
        ax.set_xlabel("s")
    handles = [plt.Line2D([], [], marker=m, color=c, ls="none", label=k) for k, (m, c) in MARKS.items()]
    handles += [plt.Line2D([], [], color="#1f77b4", label="arousal"), plt.Line2D([], [], color="#d62728", ls="--", label="p_red")]
    fig.legend(handles=handles, loc="lower center", ncol=6, fontsize=7)
    fig.tight_layout(rect=(0, 0.06, 1, 1))
    fig.savefig(args.out, dpi=90)
    print(args.out)


if __name__ == "__main__":
    main()
