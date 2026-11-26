"""Draw one motion card (PNG) from a *.motion.json written by `miniring-arena motioncard / motionaudit`.

usage: py -3 tools/motioncard.py <x.motion.json> [--out card.png] [--from S] [--to S] [--ghosts N] [--audit-cards N]

The card has three views (front = as the opponent sees, side, top) with time-coloured ghost stick figures and the
trajectories of head, pelvis, hands, toes, heels and the weapon tip, then time strips (speeds, joint angles, heights,
foot contacts, edge alignment / joint gap / CoM outside support) with 0.1 s ticks, hits (red lines) and audit findings
(shaded), and the captions (motion words) and naturalness numbers. A human can point at "0.4 s, head too high".

--audit-cards N also writes up to N extra cards, one per audit finding (window = finding +- 0.3 s), next to the main card.
Needs matplotlib and numpy. docs/plans/MOTION_TOOLS_PLAN.md.
"""
import json
import os
import sys

import matplotlib

matplotlib.use('Agg')
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402

plt.rcParams['font.family'] = ['Yu Gothic', 'Meiryo', 'MS Gothic', 'DejaVu Sans']
plt.rcParams['axes.unicode_minus'] = False

BONES = [('head', 'chest'), ('chest', 'pelvis'), ('chest', 'shoulderL'), ('shoulderL', 'elbowL'), ('elbowL', 'handL'),
         ('chest', 'shoulderR'), ('shoulderR', 'elbowR'), ('elbowR', 'handR'),
         ('pelvis', 'hipL'), ('hipL', 'kneeL'), ('kneeL', 'ankleL'), ('ankleL', 'heelL'), ('heelL', 'toeL'), ('ankleL', 'toeL'),
         ('pelvis', 'hipR'), ('hipR', 'kneeR'), ('kneeR', 'ankleR'), ('ankleR', 'heelR'), ('heelR', 'toeR'), ('ankleR', 'toeR')]
TRAILS = [('head', '#d62728'), ('pelvis', '#7f7f7f'), ('handL', '#1f77b4'), ('handR', '#ff7f0e'),
          ('toeL', '#17becf'), ('toeR', '#bcbd22'), ('heelL', '#9edae5'), ('heelR', '#dbdb8d'), ('tip', '#2ca02c')]
FINDING_COLORS = {'snag': '#e377c2', 'slide': '#ff9896', 'gap': '#8c564b', 'jitter': '#9467bd', 'spike': '#d62728',
                  'self': '#000000', 'balance': '#bcbd22', 'airborne': '#17becf', 'tracking': '#ffbb78', 'swap': '#e7298a',
                  'down': '#c7c7c7'}


def arg(argv, name, default):
    if name not in argv:
        return default
    at = argv.index(name) + 1
    if at >= len(argv):
        raise SystemExit(f'{name} needs a value')
    return argv[at]


def load(path):
    with open(path, encoding='utf-8') as f:
        data = json.load(f)
    names = data['points']
    t = np.array([fr['t'] for fr in data['frames']])
    p = np.array([np.array(fr['p']).reshape(-1, 3) for fr in data['frames']])
    tgt = np.array([np.array(fr['target']).reshape(2, 3) for fr in data['frames']])
    c = np.array([fr['c'] for fr in data['frames']], dtype=bool)
    gap = np.array([fr['gap'] for fr in data['frames']])
    edge = np.array([fr.get('edge', np.nan) for fr in data['frames']])
    fwd = np.array([fr['fwd'] for fr in data['frames']])
    opp = np.array([fr['opp'] for fr in data['frames']])
    hits = [(fr['t'], fr['hit']) for fr in data['frames'] if 'hit' in fr]
    down = np.array([fr['down'] for fr in data['frames']])
    return data, names, t, p, tgt, c, gap, edge, fwd, opp, hits, down


def angle_at(a, b, c):
    u, w = a - b, c - b
    d = np.linalg.norm(u, axis=-1) * np.linalg.norm(w, axis=-1)
    return np.degrees(np.arccos(np.clip(np.sum(u * w, axis=-1) / np.maximum(d, 1e-9), -1, 1)))


def support_distance(com, pts):
    """Same as MotionMetrics.SupportDistance (positive = outside the hull of the touching points)."""
    if len(pts) == 0:
        return np.nan
    pts = sorted(set(map(tuple, pts)))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    def seg(p, a, b):
        a, b, p = np.array(a), np.array(b), np.array(p)
        ab = b - a
        L = ab @ ab
        s = 0 if L < 1e-12 else np.clip((p - a) @ ab / L, 0, 1)
        return np.linalg.norm(p - (a + s * ab))

    if len(pts) == 1:
        return float(np.linalg.norm(np.array(com) - np.array(pts[0])))
    if len(pts) == 2:
        return seg(com, pts[0], pts[1])
    hull = []
    for seq in (pts, pts[::-1]):
        start = len(hull)
        for q in seq:
            while len(hull) >= start + 2 and cross(hull[-2], hull[-1], q) <= 0:
                hull.pop()
            hull.append(q)
        hull.pop()
    if len(hull) < 3:
        return seg(com, hull[0], hull[-1])
    inside = all(cross(hull[i], hull[(i + 1) % len(hull)], com) >= 0 for i in range(len(hull)))
    near = min(seg(com, hull[i], hull[(i + 1) % len(hull)]) for i in range(len(hull)))
    return -near if inside else near


def card(path, out, t0=None, t1=None, ghosts=6, title_extra=''):
    data, names, t, p, tgt, c, gap, edge, fwd, opp, hits, down = load(path)
    ix = {n: i for i, n in enumerate(names)}
    t0 = float(data.get('windowFrom', t[0])) if t0 is None else t0
    t1 = float(data.get('windowTo', t[-1])) if t1 is None else t1
    sel = (t >= t0 - 1e-6) & (t <= t1 + 1e-6)
    if sel.sum() < 3:
        raise SystemExit(f'window {t0}-{t1} has no frames')
    t, p, tgt, c, gap, edge, fwd, opp, down = t[sel], p[sel], tgt[sel], c[sel], gap[sel], edge[sel], fwd[sel], opp[sel], down[sel]
    hits = [h for h in hits if t0 <= h[0] <= t1]
    dt = float(data['dt'])

    # The body frame at the start of the window: forward = toward the opponent, lateral = the fighter's left, up = +Y.
    f = np.array(fwd[0], dtype=float)
    f[1] = 0
    f /= max(np.linalg.norm(f), 1e-9)
    up = np.array([0.0, 1.0, 0.0])
    left = np.cross(up, f)
    origin = p[0, ix['pelvis']].copy()
    origin[1] = min(p[:, ix['heelL'], 1].min(), p[:, ix['heelR'], 1].min(), p[:, ix['toeL'], 1].min(), p[:, ix['toeR'], 1].min())
    rel = p - origin
    L = rel @ left
    U = rel @ up
    F = rel @ f
    views = [('正面（相手から見る）', -L, U, '横 [m]（+ = 本人の右）', '高さ [m]'),
             ('横（前 = 相手の方）', F, U, '前 [m]', '高さ [m]'),
             ('上から', -L, F, '横 [m]（+ = 本人の右）', '前 [m]')]

    fig = plt.figure(figsize=(18, 15), dpi=90)
    gs = fig.add_gridspec(9, 3, height_ratios=[4.6, 1.2, 1.2, 1.0, 0.8, 1.0, 0.25, 2.2, 0.01], hspace=0.55, wspace=0.18)
    cmap = plt.get_cmap('plasma')
    norm = plt.Normalize(t[0], t[-1])
    ghost_idx = np.linspace(0, len(t) - 1, ghosts).round().astype(int)
    for k, (title, X, Y, xl, yl) in enumerate(views):
        ax = fig.add_subplot(gs[0, k])
        for gi in ghost_idx:
            col = cmap(norm(t[gi]))
            for a, b in BONES:
                ax.plot([X[gi, ix[a]], X[gi, ix[b]]], [Y[gi, ix[a]], Y[gi, ix[b]]], color=col, lw=1.1, alpha=0.45)
            if np.linalg.norm(p[gi, ix['tip']] - p[gi, ix['handR']]) > 0.05:
                hand = 'handR' if np.linalg.norm(p[gi, ix['tip']] - p[gi, ix['handR']]) < np.linalg.norm(p[gi, ix['tip']] - p[gi, ix['handL']]) else 'handL'
                ax.plot([X[gi, ix[hand]], X[gi, ix['tip']]], [Y[gi, ix[hand]], Y[gi, ix['tip']]], color=col, lw=2.2, alpha=0.6)
            ax.text(X[gi, ix['head']], Y[gi, ix['head']] + (0.05 if k < 2 else 0.0), f'{t[gi]:.2f}', fontsize=7, color=col, ha='center')
        for name, color in TRAILS:
            if name == 'tip' and np.allclose(p[:, ix['tip']], p[:, ix['handR']], atol=0.03):
                continue
            # Trail in the point's own colour; dots every 0.1 s in the time colour (so "where was the hand at 0.4 s" can be read).
            ax.plot(X[:, ix[name]], Y[:, ix[name]], color=color, lw=1.8, alpha=0.95)
            tick = np.unique(np.searchsorted(t, np.arange(np.ceil(t[0] * 10) / 10, t[-1] + 1e-6, 0.1)).clip(0, len(t) - 1))
            ax.scatter(X[tick, ix[name]], Y[tick, ix[name]], c=[cmap(norm(t[i])) for i in tick], s=14, zorder=3, edgecolors=color, linewidths=0.6)
            ax.annotate(name, (X[-1, ix[name]], Y[-1, ix[name]]), fontsize=7, color=color, xytext=(3, 0), textcoords='offset points')
        # Opponent's head (where the strike goes).
        o = opp[len(opp) // 2] - origin
        ov = {0: (-(o @ left), o @ up), 1: (o @ f, o @ up), 2: (-(o @ left), o @ f)}[k]
        ax.plot(*ov, marker='o', ms=14, mfc='none', mec='#444', mew=1.5)
        ax.annotate('相手の頭', ov, fontsize=7, color='#444', xytext=(6, 6), textcoords='offset points')
        if k < 2:
            ax.axhline(0, color='#888', lw=0.8)
        ax.set_aspect('equal', adjustable='datalim')
        ax.set_title(title, fontsize=11)
        ax.set_xlabel(xl, fontsize=8)
        ax.set_ylabel(yl, fontsize=8)
        ax.tick_params(labelsize=7)
        ax.grid(alpha=0.25)
    sm = plt.cm.ScalarMappable(cmap=cmap, norm=norm)
    cb = fig.colorbar(sm, ax=fig.axes[:3], fraction=0.012, pad=0.01)
    cb.set_label('時刻 [s]', fontsize=8)

    # Time strips.
    def strip(row, ylabel):
        ax = fig.add_subplot(gs[row, :])
        ax.set_xlim(t[0], t[-1])
        ticks = np.arange(np.ceil(t[0] * 10) / 10, t[-1] + 1e-6, 0.1)
        ax.set_xticks(ticks)
        ax.set_xticklabels([f'{x:.1f}' for x in ticks], fontsize=7)
        ax.grid(alpha=0.3)
        ax.set_ylabel(ylabel, fontsize=8)
        ax.tick_params(labelsize=7)
        for ht, label in hits:
            ax.axvline(ht, color='red', lw=1.2, alpha=0.7)
        for x in data.get('findings', []):
            if x['kind'] == 'down' or x['to'] < t[0] or x['from'] > t[-1]:
                continue
            ax.axvspan(max(x['from'], t[0]), min(x['to'] + dt, t[-1]), color=FINDING_COLORS.get(x['kind'], '#999'), alpha=0.12)
        return ax

    def speed(name):
        v = np.gradient(p[:, ix[name]], dt, axis=0)
        return np.linalg.norm(v, axis=1)

    ax = strip(1, '速さ [m/s]')
    for name, color in TRAILS:
        if name in ('heelL', 'heelR', 'head'):
            continue
        if name == 'tip' and np.allclose(p[:, ix['tip']], p[:, ix['handR']], atol=0.03):
            continue
        ax.plot(t, speed(name), color=color, lw=1.3, label=name)
    ax.legend(fontsize=7, ncol=7, loc='upper left')
    for ht, label in hits:
        ax.annotate(label, (ht, ax.get_ylim()[1]), fontsize=7, color='red', rotation=0, va='top', xytext=(2, -2), textcoords='offset points')

    ax = strip(2, '角 [°]')
    knees = [angle_at(p[:, ix['hip' + s]], p[:, ix['knee' + s]], p[:, ix['ankle' + s]]) for s in 'LR']
    elbows = [angle_at(p[:, ix['shoulder' + s]], p[:, ix['elbow' + s]], p[:, ix['hand' + s]]) for s in 'LR']
    trunk = p[:, ix['chest']] - p[:, ix['pelvis']]
    lean = np.degrees(np.arctan2(trunk @ f, trunk[:, 1]))
    sh = p[:, ix['shoulderL']] - p[:, ix['shoulderR']]
    hp = p[:, ix['hipL']] - p[:, ix['hipR']]
    twist = np.degrees(np.arctan2(sh[:, 2], sh[:, 0]) - np.arctan2(hp[:, 2], hp[:, 0]))
    twist = (twist + 180) % 360 - 180
    for series, label, color, ls in [(knees[0], '膝L', '#17becf', '-'), (knees[1], '膝R', '#bcbd22', '-'), (elbows[0], '肘L', '#1f77b4', '--'),
                                     (elbows[1], '肘R', '#ff7f0e', '--'), (lean, '腰の折れ（前+）', '#7f7f7f', '-'), (twist, '胴のひねり', '#9467bd', '-')]:
        ax.plot(t, series, label=label, color=color, ls=ls, lw=1.3)
    ax.legend(fontsize=7, ncol=6, loc='upper left')

    ax = strip(3, '高さ [cm]')
    for name, color in [('head', '#d62728'), ('pelvis', '#7f7f7f'), ('handL', '#1f77b4'), ('handR', '#ff7f0e')]:
        ax.plot(t, 100 * U[:, ix[name]], color=color, lw=1.3, label=name)
    ax.legend(fontsize=7, ncol=4, loc='upper left')

    ax = strip(4, '接地')
    for row, name in enumerate(data['contactNames']):
        on = c[:, row]
        ax.fill_between(t, row + 0.1, row + 0.9, where=on, step='mid', color=['#9edae5', '#17becf', '#dbdb8d', '#bcbd22'][row])
    ax.set_yticks([0.5, 1.5, 2.5, 3.5])
    ax.set_yticklabels(data['contactNames'], fontsize=7)
    ax.set_ylim(0, 4)

    ax = strip(5, '刃筋 [°] / 隙間・支え [cm]')
    ax.plot(t, edge, color='#2ca02c', lw=1.3, label='刃筋のずれ [°]')
    ax.plot(t, 100 * gap, color='#8c564b', lw=1.3, label='関節の最大の隙間 [cm]')
    com = p[:, ix['com']]
    sup = []
    for i in range(len(t)):
        pts = [(p[i, ix[n], 0], p[i, ix[n], 2]) for k, n in enumerate(data['contactNames']) if c[i, k]]
        sup.append(support_distance((com[i, 0], com[i, 2]), pts))
    ax.plot(t, 100 * np.array(sup), color='#bcbd22', lw=1.3, label='重心の支えの外 [cm]（+ が外）')
    ax.axhline(0, color='#888', lw=0.6)
    ax.legend(fontsize=7, ncol=3, loc='upper left')

    # Text: header, naturalness, findings, captions.
    ax = fig.add_subplot(gs[7, :])
    ax.axis('off')
    lines = [f"{data['scene']}  [{data['subject']}]  physics {data['physicsVersion']} logic {data['logicVersion']}  seed {data['seed']}  window {t[0]:.2f}-{t[-1]:.2f} s {title_extra}",
             f"command: {data.get('command', '')}",
             f"自然さ: {data.get('natural', '')}" if title_extra == '' else '']
    found = [x for x in data.get('findings', []) if x['to'] >= t[0] and x['from'] <= t[-1]]
    lines.append('見つけた瞬間: ' + ('なし' if not found else ''))
    lines += [f"  {x['from']:.2f}-{x['to']:.2f} s  {x['kind']}: {x['text']}" for x in found[:8]]
    if len(found) > 8:
        lines.append(f'  …ほか {len(found) - 8} 件')
    caps = [x for x in data.get('captions', []) if t[0] <= x['t'] <= t[-1]]
    lines.append('動きの言葉:')
    lines += [f"  {x['t']:.2f} s: {x['text']}" for x in caps[:14]]
    ax.text(0, 1, '\n'.join(lines), va='top', ha='left', fontsize=8.5, family=plt.rcParams['font.family'], transform=ax.transAxes)

    fig.suptitle('motion card — 動きの 1 枚（色 = 時刻、赤線 = 当たり、帯 = 見つけた瞬間）', fontsize=13, y=0.995)
    fig.savefig(out, bbox_inches='tight')
    plt.close(fig)
    print(out)


def main(argv):
    if len(argv) < 2 or argv[1].startswith('--'):
        raise SystemExit(__doc__)
    path = argv[1]
    base = path[:-len('.motion.json')] if path.endswith('.motion.json') else os.path.splitext(path)[0]
    out = arg(argv, '--out', base + '.card.png')
    t0 = arg(argv, '--from', None)
    t1 = arg(argv, '--to', None)
    ghosts = int(arg(argv, '--ghosts', '6'))
    card(path, out, None if t0 is None else float(t0), None if t1 is None else float(t1), ghosts)
    n = int(arg(argv, '--audit-cards', '0'))
    if n > 0:
        with open(path, encoding='utf-8') as f:
            data = json.load(f)
        t_end = data['frames'][-1]['t']
        for old in os.listdir(os.path.dirname(os.path.abspath(base)) or '.'):
            if old.startswith(os.path.basename(base) + '.audit') and old.endswith('.png'):
                os.remove(os.path.join(os.path.dirname(os.path.abspath(base)), old))
        # One card per kind first (the rarer, more telling kinds before the common ones), then the rest in time order.
        order = ['swap', 'self', 'snag', 'airborne', 'tracking', 'balance', 'spike', 'jitter', 'slide', 'gap']
        found = [x for x in data.get('findings', []) if x['kind'] != 'down']
        firsts = [next(x for x in found if x['kind'] == k) for k in order if any(x['kind'] == k for x in found)]
        picks = (firsts + [x for x in found if x not in firsts])[:n]
        for i, x in enumerate(picks, 1):
            card(path, f"{base}.audit{i}_{x['kind']}.png", max(0.0, x['from'] - 0.3), min(t_end, x['to'] + 0.3), ghosts,
                 title_extra=f"  audit: {x['kind']} {x['where']} {x['from']:.2f}-{x['to']:.2f} s")


if __name__ == '__main__':
    main(sys.argv)
