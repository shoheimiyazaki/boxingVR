"""極真空手の廻し蹴り（ミットを蹴る E02）から、右脚の蹴りの区間だけを小さな CSV に切り出す（data/mocap/kyokushin/）。

使い方: py -3 -I tools/mocap/extract_kicks.py <B0367 の展開先> <出力フォルダ>
各蹴り = 右足首の速さの山（6 m/s 超、0.5 秒以上離れる）の前後。速さが 1 m/s を下回るまでさかのぼった所（構え）から、
山のあと 1 m/s を下回る所（戻り）まで、前後に 0.15 秒の余白を付ける。長さは mm を整数に丸める。欠損は空欄。
"""
import glob
import math
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from c3dread import read  # noqa: E402

KEEP = ['RHJC', 'RKJC', 'RAJC', 'LHJC', 'LKJC', 'LAJC', 'RTOE', 'RHEE', 'LTOE', 'LHEE', 'LASI', 'RASI', 'LPSI', 'RPSI',
        'LSJC', 'RSJC', 'CLAV', 'C7', 'Tarcza1', 'Tarcza2', 'Tarcza3', 'Tarcza4', 'Tarcza5', 'Tarcza6']


def speed(frames, i, f, rate):
    p, q = frames[f - 1][i], frames[f][i]
    if not p or not q:
        return 0.0
    return math.dist(p, q) * rate / 1000.0


def main(src, out):
    os.makedirs(out, exist_ok=True)
    count = 0
    for path in sorted(glob.glob(os.path.join(src, '*', '*-E02-*.c3d'))):
        labels, rate, frames = read(path)
        idx = {l.split(':')[-1]: k for k, l in enumerate(labels)}
        a = idx['RAJC']
        sp = [0.0] + [speed(frames, a, f, rate) for f in range(1, len(frames))]
        peaks = []
        for f, s in enumerate(sp):
            if s > 6 and all(abs(f - p) > rate * 0.5 for p, _ in peaks):
                m = max(range(max(0, f - 20), min(len(sp), f + 20)), key=lambda x: sp[x])
                peaks.append((m, sp[m]))
        name = os.path.basename(path).replace('.c3d', '')
        for n, (f, s) in enumerate(peaks):
            lo = f
            while lo > 0 and sp[lo] > 1.0:
                lo -= 1
            hi = f
            while hi < len(sp) - 1 and sp[hi] > 1.0:
                hi += 1
            margin = int(0.15 * rate)
            lo = max(0, lo - margin)
            hi = min(len(sp) - 1, hi + margin)
            code = name.split('-')[-3] + '-' + name.split('-')[-1]  # S03-T01
            file = os.path.join(out, 'kick_%s_%02d.csv' % (code, n + 1))
            with open(file, 'w', newline='\n') as w:
                w.write('# source=%s rate=%d peak_frame=%d peak_speed=%.1f first_frame=%d\n' % (name, rate, f - lo, s, lo))
                w.write('frame,' + ','.join('%s_%s' % (l, c) for l in KEEP for c in 'xyz') + '\n')
                for g in range(lo, hi + 1):
                    row = [str(g - lo)]
                    for l in KEEP:
                        p = frames[g][idx[l]]
                        row += [str(int(round(v))) for v in p] if p else ['', '', '']
                    w.write(','.join(row) + '\n')
            count += 1
            print(file, hi - lo + 1, 'frames, peak %.1f m/s' % s)
    print(count, 'kicks')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
