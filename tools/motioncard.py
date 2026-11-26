"""Draw one motion card (PNG) from a *.motion.json written by `miniring-arena motioncard / motionaudit`.

usage: py -3 tools/motioncard.py <x.motion.json> [--out card.png] [--from S] [--to S] [--ghosts N] [--audit-cards N]
       py -3 tools/motioncard.py --compare a.motion.json,b.motion.json[,c] [--labels A,B,C] [--before 0.5] [--after 0.3] [--out x.png]

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
REAR_BONES = [('pelvis', 'rthighL'), ('rthighL', 'rshinL'), ('rshinL', 'rheelL'), ('rheelL', 'rtoeL'),
              ('pelvis', 'rthighR'), ('rthighR', 'rshinR'), ('rshinR', 'rheelR'), ('rheelR', 'rtoeR')]
TRAILS = [('head', '#d62728'), ('pelvis', '#7f7f7f'), ('handL', '#1f77b4'), ('handR', '#ff7f0e'),
          ('toeL', '#17becf'), ('toeR', '#bcbd22'), ('heelL', '#9edae5'), ('heelR', '#dbdb8d'), ('tip', '#2ca02c'), ('tipL', '#98df8a')]
REAR_TRAILS = [('rtoeL', '#8c564b'), ('rtoeR', '#c49c94')]
CONTACT_COLORS = ['#9edae5', '#17becf', '#dbdb8d', '#bcbd22', '#c5b0d5', '#9467bd', '#f7b6d2', '#e377c2']
HUMAN = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'data', 'mocap', 'kyokushin', 'human_ranges.json')


def human_ranges():
    """Human ranges written by `miniring-arena motionhuman` (same formulas as the game's motion tools), or None."""
    try:
        with open(HUMAN, encoding='utf-8') as f:
            return json.load(f)
    except OSError:
        return None


def human_line(data):
    """'jerk log10 x (human kicks a-b: inside)' for the card, from the numbers motioncard wrote."""
    h = human_ranges()
    if h is None or 'jerkLog10' not in data:
        return ''
    out = []
    for key, label in (('jerkLog10', '躍度 log10'), ('chainOrder', '連鎖の順')):
        v = float(data[key])
        r = h[key]
        where = '中' if r['Min'] <= v <= r['Max'] else ('下' if v < r['Min'] else '上')
        out.append(f"{label} {v:.2f}（人の蹴り {r['Count']} 本の幅 {r['Min']:.2f}〜{r['Max']:.2f} の{where}）")
    return '人の幅: ' + ' / '.join(out)


def drawn_trails(data, p, ix):
    """Trails to draw: the common ones, the left blade tip when it is not the left hand, the rear feet of a four-legged body."""
    out = []
    for name, color in TRAILS:
        if name == 'tip' and np.allclose(p[:, ix['tip']], p[:, ix['handR']], atol=0.03):
            continue
        if name == 'tipL' and ('tipL' not in ix or np.allclose(p[:, ix['tipL']], p[:, ix['handL']], atol=0.03)):
            continue
        out.append((name, color))
    if data.get('quadruped'):
        out += REAR_TRAILS
    return out
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
        bones = BONES + (REAR_BONES if data.get('quadruped') else [])
        for gi in ghost_idx:
            col = cmap(norm(t[gi]))
            for a, b in bones:
                ax.plot([X[gi, ix[a]], X[gi, ix[b]]], [Y[gi, ix[a]], Y[gi, ix[b]]], color=col, lw=1.1, alpha=0.45)
            if np.linalg.norm(p[gi, ix['tip']] - p[gi, ix['handR']]) > 0.05:
                hand = 'handR' if np.linalg.norm(p[gi, ix['tip']] - p[gi, ix['handR']]) < np.linalg.norm(p[gi, ix['tip']] - p[gi, ix['handL']]) else 'handL'
                ax.plot([X[gi, ix[hand]], X[gi, ix['tip']]], [Y[gi, ix[hand]], Y[gi, ix['tip']]], color=col, lw=2.2, alpha=0.6)
            ax.text(X[gi, ix['head']], Y[gi, ix['head']] + (0.05 if k < 2 else 0.0), f'{t[gi]:.2f}', fontsize=7, color=col, ha='center')
        for name, color in drawn_trails(data, p, ix):
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
    for name, color in drawn_trails(data, p, ix):
        if name in ('heelL', 'heelR', 'head'):
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
    rows = data['contactNames'] if data.get('quadruped') else data['contactNames'][:4]
    for row, name in enumerate(rows):
        on = c[:, row]
        ax.fill_between(t, row + 0.1, row + 0.9, where=on, step='mid', color=CONTACT_COLORS[row % len(CONTACT_COLORS)])
    ax.set_yticks([r + 0.5 for r in range(len(rows))])
    ax.set_yticklabels(rows, fontsize=6 if len(rows) > 4 else 7)
    ax.set_ylim(0, len(rows))

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
             f"自然さ: {data.get('natural', '')}" if title_extra == '' else '',
             human_line(data) if title_extra == '' else '']
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


COMPARE_COLORS = ['#1f77b4', '#ff7f0e', '#2ca02c']
COMPARE_NUMBERS = [('peakSpeed', '末端の最高速 [m/s]', '{:.2f}'), ('jerkLog10', '躍度 log10（最小躍度 2.86）', '{:.2f}'),
                   ('chainOrder', '運動連鎖の順（1 = 近い所から）', '{:.2f}'), ('headShake', '頭の揺れ [m/s²]', '{:.0f}'),
                   ('twistRange', '胴のひねりの幅 [°]', '{:.0f}'), ('planeRms', '軌道面からのずれ [cm]', 'cm'), ('dip', 'U 字の深さ [cm]', 'cm')]


def body_frame(fwd, p, ix, i0):
    """(origin, forward, left) of a trace: forward = toward the opponent at frame i0, origin = pelvis over the lowest foot point."""
    f = np.array(fwd[i0], dtype=float)
    f[1] = 0
    f /= max(np.linalg.norm(f), 1e-9)
    left = np.cross([0.0, 1.0, 0.0], f)
    origin = p[i0, ix['pelvis']].copy()
    origin[1] = min(p[:, ix[n], 1].min() for n in ('heelL', 'heelR', 'toeL', 'toeR'))
    return origin, f, left


def compare(paths, out, before=0.5, after=0.3, labels=None):
    """Side-by-side card (§A3): 2-3 traces aligned at their alignTime (first hit, else effector peak), drawn over each other in the 3 views."""
    runs = []
    for k, path in enumerate(paths):
        data, names, t, p, tgt, c, gap, edge, fwd, opp, hits, down = load(path)
        ix = {n: i for i, n in enumerate(names)}
        align = float(data.get('alignTime', data.get('peakTime', t[len(t) // 2])))
        sel = (t >= align - before - 1e-6) & (t <= align + after + 1e-6)
        if sel.sum() < 3:
            raise SystemExit(f'{path}: nothing around {align:.2f} s')
        i0 = int(np.argmax(sel))
        origin, f, left = body_frame(fwd, p, ix, i0)
        rel = p[sel] - origin
        runs.append(dict(data=data, ix=ix, t=t[sel] - align, L=-(rel @ left), U=rel[..., 1], F=rel @ f, eff=data.get('effector', 'handR'),
                         label=(labels[k] if labels and k < len(labels) else data['scene']), color=COMPARE_COLORS[k % 3], dt=float(data['dt'])))

    fig = plt.figure(figsize=(18, 13), dpi=90)
    gs = fig.add_gridspec(3, 3, height_ratios=[4.6, 1.6, 2.4], hspace=0.35, wspace=0.18)
    views = [('正面（相手から見る）', 'L', 'U', '横 [m]（+ = 本人の右）', '高さ [m]'), ('横（前 = 相手の方）', 'F', 'U', '前 [m]', '高さ [m]'),
             ('上から', 'L', 'F', '横 [m]（+ = 本人の右）', '前 [m]')]
    for k, (title, xa, ya, xl, yl) in enumerate(views):
        ax = fig.add_subplot(gs[0, k])
        for r in runs:
            X, Y, ix, t = r[xa], r[ya], r['ix'], r['t']
            bones = BONES + (REAR_BONES if r['data'].get('quadruped') else [])
            for dt_off, alpha, lw in ((-0.2, 0.18, 1.0), (-0.1, 0.3, 1.0), (0.0, 0.95, 2.0), (0.1, 0.3, 1.0)):
                gi = int(np.argmin(np.abs(t - dt_off)))
                for a, b in bones:
                    ax.plot([X[gi, ix[a]], X[gi, ix[b]]], [Y[gi, ix[a]], Y[gi, ix[b]]], color=r['color'], lw=lw, alpha=alpha)
            for name in {r['eff'], 'tip'} if 'tip' in ix and not np.allclose(X[:, ix['tip']], X[:, ix['handR']], atol=0.03) else {r['eff']}:
                ax.plot(X[:, ix[name]], Y[:, ix[name]], color=r['color'], lw=1.6, ls='-' if name == r['eff'] else '--')
                tick = np.unique(np.searchsorted(t, np.arange(np.ceil(t[0] * 10) / 10, t[-1] + 1e-6, 0.1)).clip(0, len(t) - 1))
                ax.scatter(X[tick, ix[name]], Y[tick, ix[name]], color=r['color'], s=10, zorder=3)
        if k < 2:
            ax.axhline(0, color='#888', lw=0.8)
        ax.set_aspect('equal', adjustable='datalim')
        ax.set_title(title, fontsize=11)
        ax.set_xlabel(xl, fontsize=8)
        ax.set_ylabel(yl, fontsize=8)
        ax.tick_params(labelsize=7)
        ax.grid(alpha=0.25)
    handles = [plt.Line2D([], [], color=r['color'], lw=2, label=r['label']) for r in runs]
    fig.axes[0].legend(handles=handles, fontsize=8, loc='upper left')

    ax = fig.add_subplot(gs[1, :])
    for r in runs:
        name = 'tip' if 'tip' in r['ix'] and r['eff'].startswith('tip') else r['eff']
        pts = np.stack([r['L'][:, r['ix'][name]], r['U'][:, r['ix'][name]], r['F'][:, r['ix'][name]]], axis=1)
        ax.plot(r['t'], np.linalg.norm(np.gradient(pts, r['dt'], axis=0), axis=1), color=r['color'], lw=1.5, label=f"{r['label']}: {r['eff']}")
    ax.axvline(0, color='red', lw=1.2, alpha=0.7)
    ax.set_xlabel('そろえた瞬間からの時刻 [s]（0 = 当たり、当たりが無ければ末端の最高速）', fontsize=8)
    ax.set_ylabel('末端の速さ [m/s]', fontsize=8)
    ax.grid(alpha=0.3)
    ax.legend(fontsize=7, loc='upper left')
    ax.tick_params(labelsize=7)

    ax = fig.add_subplot(gs[2, :])
    ax.axis('off')
    h = human_ranges()
    head = ['量'] + [r['label'][:38] for r in runs] + ([f'差（{runs[k]["label"][:10]} − {runs[0]["label"][:10]}）' for k in range(1, len(runs))])
    rows = []
    for key, label, fmt in COMPARE_NUMBERS:
        vals = [float(r['data'][key]) if key in r['data'] else np.nan for r in runs]
        scale = 100 if fmt == 'cm' else 1
        f = '{:.1f}' if fmt == 'cm' else fmt
        cells = [f.format(v * scale) for v in vals]
        if h is not None and key in h:
            cells = [x + (' ○' if h[key]['Min'] <= v <= h[key]['Max'] else ' ↓' if v < h[key]['Min'] else ' ↑') for x, v in zip(cells, vals)]
        rows.append([label] + cells + [('{:+.2f}' if fmt != 'cm' else '{:+.1f}').format((vals[k] - vals[0]) * scale) for k in range(1, len(vals))])
    table = ax.table(cellText=rows, colLabels=head, loc='upper left', cellLoc='left', colLoc='left')
    table.auto_set_font_size(False)
    table.set_fontsize(8)
    table.scale(1, 1.3)
    notes = [f"{r['label']}: {r['data']['scene']}  physics {r['data']['physicsVersion']} logic {r['data']['logicVersion']} seed {r['data']['seed']}  aligned at {float(r['data'].get('alignTime', 0)):.2f} s  |  {r['data'].get('command', '')}" for r in runs]
    if h is not None:
        notes.append(f"○ = 人の幅（{h['source']}）の中、↓ / ↑ = 幅より小さい / 大きい。蹴りの計測なので、拳・刃の値は目安。")
    ax.text(0, 0.02, '\n'.join(notes), va='bottom', ha='left', fontsize=7.5, transform=ax.transAxes)
    fig.suptitle('motion compare — 並べ比べ（色 = 記録、太線 = そろえた瞬間、薄線 = −0.2 / −0.1 / +0.1 s、点 = 0.1 s ごと、破線 = 刃先）', fontsize=12, y=0.995)
    fig.savefig(out, bbox_inches='tight')
    plt.close(fig)
    print(out)


def main(argv):
    if '--compare' in argv:
        paths = arg(argv, '--compare', '').split(',')
        labels = arg(argv, '--labels', None)
        compare(paths, arg(argv, '--out', os.path.splitext(paths[0])[0].replace('.motion', '') + '.compare.png'),
                float(arg(argv, '--before', '0.5')), float(arg(argv, '--after', '0.3')), None if labels is None else labels.split(','))
        return
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
