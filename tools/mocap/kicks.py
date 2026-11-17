import sys, math, glob, os
sys.path.insert(0, os.path.dirname(__file__))
from c3dread import read

def sub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def norm(a): return math.sqrt(a[0]**2+a[1]**2+a[2]**2)
def ang(a, b, c):
    u = sub(a, b); v = sub(c, b)
    return math.degrees(math.acos(max(-1, min(1, (u[0]*v[0]+u[1]*v[1]+u[2]*v[2])/(norm(u)*norm(v))))))

for path in sorted(glob.glob(os.path.join(os.path.dirname(__file__), 'B0367', '*', '*-E02-*.c3d'))):
    labels, rate, frames = read(path)
    idx = {l.split(':')[-1]: i for i, l in enumerate(labels)}
    print(os.path.basename(path), rate, len(frames))
    for side in 'LR':
        a = idx[side + 'AJC']; k = idx[side + 'KJC']; h = idx[side + 'HJC']
        speeds = []
        for f in range(1, len(frames)):
            p, q = frames[f-1][a], frames[f][a]
            speeds.append(norm(sub(q, p)) * rate / 1000 if p and q else 0)
        # peaks above 6 m/s, separated by 0.5 s
        peaks = []
        for f, s in enumerate(speeds):
            if s > 6 and all(abs(f - p) > rate * 0.5 for p, _ in peaks):
                w = range(max(0, f - 20), min(len(speeds), f + 20))
                m = max(w, key=lambda x: speeds[x])
                peaks.append((m, speeds[m]))
        for f, s in peaks:
            fr = frames[f]
            hip, knee, ank = fr[h], fr[k], fr[a]
            sh = [fr[idx['Tarcza%d' % i]] for i in range(1, 7) if fr[idx['Tarcza%d' % i]]]
            shc = tuple(sum(c[j] for c in sh)/len(sh) for j in range(3)) if sh else None
            print('  %s peak f=%d t=%.2fs ankle %.1f m/s  knee angle %.0f  hipZ %.0f kneeZ %.0f ankZ %.0f  shieldZ %s' % (
                side, f, f/rate, s, ang(hip, knee, ank) if hip and knee and ank else -1, hip[2] if hip else -1, knee[2] if knee else -1, ank[2] if ank else -1,
                '%.0f' % shc[2] if shc else '-'))
