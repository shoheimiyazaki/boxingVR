"""VR の刀の稽古の記録（.vrmotion）に、物理で説明できる量で点を付け、型ごとに良い振りを選ぶ。

使い方:
  py -3 tools/vrmotion_pick.py --forms katana_forms.json --out table.md [--json scores.json] [--trim-dir docs/vrmotion] clip_dir...

型の経路（katana_forms.json）は調査用のテスト KatanaFormDumpProbe が書き出す（環境変数 KATANA_FORMS_OUT）。
点の付け方は docs/arena/vrmotion_pick_2026-11-24.md。記録そのものは読むだけで、書き換えない（--trim-dir には切り出した写しを書く）。

量（どれも記録から連続に出す。型の種類で分けない: 突きか斬りかは、型の経路の「切っ先が刀の軸に沿って動く割合」から出す）:
  - vmax   : 切っ先の最高速度 [m/s]。人の体の寸法の生の入力（ワールド）で、右のコントローラから前へ BLADE_HUMAN の点。
  - align  : 刃筋。速い間（最高速の半分以上）の、速さの 2 乗の重みの平均。斬りは「刀に垂直な動き」と刃の向きの cos、
             突きは「刀の軸に沿う動き」の割合。型の軸に沿う割合 a で混ぜる（align = a・突き + (1−a)・斬り）。
  - smooth : 振りの滑らかさ。切っ先の速さの山が一つか（副の山の数）と、速さの無次元のジャーク（LDLJ-V、大きいほど滑らか）。
  - aim    : 型の狙い。最高速の瞬間の切っ先の位置と動く向き（体フレーム、腕の長さ = 1）を、型の経路の最高速の瞬間と比べる。
  - form   : 型の経路との近さ。振りの区間の「右手の位置 + 切っ先の位置」を型の振りの区間と DTW で合わせた平均のずれ（腕の長さ = 1）。
"""

from __future__ import annotations

import argparse
import glob
import json
import math
import os
import re
import shutil
import sys
from dataclasses import dataclass, asdict

import numpy as np
from scipy.ndimage import median_filter
from scipy.signal import find_peaks, savgol_filter

SHIELD = False  # --shield: 左手が盾を持つ記録（盾が前にある割合を点に掛ける）
BLADE_HUMAN = 0.85  # 右手（鍔元）から切っ先まで [m]: 刀身 70 cm の打刀を両手で持ったときの、右手から切っ先
BLADE_FORM = 1.7  # 型の経路で切っ先を測る長さ（腕の長さ = 1。SwordForms.Blade と同じ）
SEG_FLOOR = 0.2  # 振りの区間: 切っ先の速さが山の 20% より上の続き
FAST_SHARE = 0.5  # 刃筋を見る「速い間」: 山の 50% より上
MIN_DURATION = 1.0  # 記録の長さの下限 [s]（これより短いのは録り損ね）
CRASHED = -2  # なぞりの測定で人形の物理が例外で落ちた（vrmotion_sim.sh が書く）
MIN_SWING = 0.1  # 振りの区間の長さの下限 [s]: 打ちは 0.1 s より短くは加速しない（それより短い山は手首のはじき・トラッキングの揺れ）
MIN_SPEED = 3.0  # 振りとみなす切っ先の速さの下限 [m/s]
MIN_HOLD = 0.8  # 振りの間に刀を握っている割合の下限
MIN_HEIGHT = 1.3  # 身長の合わせの下限 [m]（それより低いのは合わせ損ね: 写像の尺度が狂う）
EDGE_PAD = 0.15  # 振りの区間が記録の端からこれ [s] 内なら、途中で切れている
SPIKE = 0.05  # トラッキングの飛び: 中央値（5 コマ）から 5 cm 以上離れたコマ
BODY_TOP, BODY_BOTTOM, BODY_RADIUS = 0.3, -2.6, 0.32  # 人形の体を体フレームの縦のカプセルで（腕の長さ = 1: 頭の上から足まで、胴の半分の幅）
SELF_WINDOW = (0.3, 0.6)  # 自分の体に刃が入るかを見る区間: 振りの前 0.3 s から後 0.6 s（振り抜き）
GROUND_LOW = 0.5  # 倒れた相手への攻撃の候補: 振りの間に切っ先がこの高さ [m]（床から。腰より下）より下へ来る
GROUND_HIGH = 0.9  # 低さの点の基準 [m]: ここで 0、0.2 m（倒れた体の首・胴の高さ）で満点
NOT_RECORDED = {"mae-geri": "蹴りは記録に入らない（ボタンの列が無い。手の動きだけ）"}


def qrot(q: np.ndarray, v: np.ndarray) -> np.ndarray:
    """四元数 q（x,y,z,w; 形 (n,4)）で v（(3,)）を回す。"""
    u = q[:, :3]
    w = q[:, 3:4]
    v = np.broadcast_to(v, u.shape)
    t = 2.0 * np.cross(u, v)
    return v + w * t + np.cross(u, t)


def unit(v: np.ndarray) -> np.ndarray:
    n = np.linalg.norm(v, axis=-1, keepdims=True)
    return v / np.maximum(n, 1e-9)


def smooth(x: np.ndarray, dt: float, deriv: int = 0, window_s: float = 0.09, order: int = 3) -> np.ndarray:
    w = max(order + 2, int(round(window_s / dt)) | 1)
    if w % 2 == 0:
        w += 1
    if len(x) <= w:
        return np.zeros_like(x) if deriv else x
    return savgol_filter(x, w, order, deriv=deriv, delta=dt, axis=0)


@dataclass
class Form:
    name: str
    dt: float
    right: np.ndarray
    tip: np.ndarray
    tip_vel: np.ndarray
    peak: int
    seg: tuple[int, int]
    axial: float  # 速い間に切っ先が刀の軸に沿って動く割合（符号付き: 柄頭が先なら負）


def load_forms(path: str) -> dict[str, Form]:
    d = json.load(open(path, encoding="utf-8"))
    out = {}
    for name, f in d["forms"].items():
        a = np.array(f["frames"], float)
        dt = f["dt"]
        right, blade = a[:, 0:3], a[:, 3:6]
        tip = right + BLADE_FORM * blade
        vel = np.gradient(tip, dt, axis=0)
        # 突き・柄頭打ちは右手そのものが動くので、柄頭の動きも見る: 軸の割合は切っ先の速度で測る
        speed = np.linalg.norm(vel, axis=1)
        peak = int(np.argmax(speed))
        seg = segment(speed, peak, dt)
        fast = speed > FAST_SHARE * speed[peak]
        wgt = speed[fast] ** 2
        ax = np.sum(wgt * np.einsum("ij,ij->i", vel[fast], blade[fast]) / np.maximum(speed[fast], 1e-9)) / np.sum(wgt)
        out[name] = Form(name, dt, right, tip, vel, peak, seg, float(ax))
    return out


def self_hit(right: np.ndarray, blade: np.ndarray, samples: int = 9) -> np.ndarray:
    """各コマで、刃（右手の少し先から切っ先まで）が人形の体のカプセルに入るか。体フレームの縦の線分からの水平の距離で見る。"""
    s = np.linspace(0.35, 1.0, samples)  # 鍔元の近く（握りの手の間）は除く
    pts = right[:, None, :] + BLADE_FORM * s[None, :, None] * blade[:, None, :]
    inside_y = (pts[:, :, 1] < BODY_TOP) & (pts[:, :, 1] > BODY_BOTTOM)
    r = np.hypot(pts[:, :, 0], pts[:, :, 2])
    return np.any(inside_y & (r < BODY_RADIUS), axis=1)


def despike(x: np.ndarray, kernel: int = 5) -> np.ndarray:
    """単発の飛び（1〜2 コマだけ跳んで戻る）を、各成分の中央値で消す。"""
    return median_filter(x, size=(kernel, 1), mode="nearest")  # 端は端の値を伸ばす（0 で埋めない）


def despike_quat(q: np.ndarray) -> np.ndarray:
    q = q.copy()
    for i in range(1, len(q)):  # 符号をそろえる（q と −q は同じ向き）
        if np.dot(q[i], q[i - 1]) < 0:
            q[i] = -q[i]
    return unit(despike(q))


def segment(speed: np.ndarray, peak: int, dt: float, max_s: float = 0.9) -> tuple[int, int]:
    floor = SEG_FLOOR * speed[peak]
    lo = peak
    span = int(max_s / dt)
    while lo > 0 and speed[lo - 1] > floor and peak - lo < span:
        lo -= 1
    hi = peak
    while hi < len(speed) - 1 and speed[hi + 1] > floor and hi - peak < span:
        hi += 1
    return lo, hi


def dtw_mean(a: np.ndarray, b: np.ndarray) -> float:
    n, m = len(a), len(b)
    cost = np.linalg.norm(a[:, None, :] - b[None, :, :], axis=2)
    acc = np.full((n + 1, m + 1), np.inf)
    steps = np.zeros((n + 1, m + 1))
    acc[0, 0] = 0.0
    for i in range(1, n + 1):
        for j in range(1, m + 1):
            choices = (acc[i - 1, j - 1], acc[i - 1, j], acc[i, j - 1])
            k = int(np.argmin(choices))
            prev = [(i - 1, j - 1), (i - 1, j), (i, j - 1)][k]
            acc[i, j] = cost[i - 1, j - 1] + choices[k]
            steps[i, j] = steps[prev] + 1
    return float(acc[n, m] / steps[n, m])


def resample(x: np.ndarray, count: int) -> np.ndarray:
    s = np.linspace(0, len(x) - 1, count)
    return np.stack([np.interp(s, np.arange(len(x)), x[:, k]) for k in range(x.shape[1])], axis=1)


@dataclass
class Swing:
    t_peak: float
    t_start: float
    t_end: float
    vmax: float
    align: float
    cut_align: float
    thrust_align: float
    sub_peaks: int
    ldlj: float
    aim_dist: float
    aim_cos: float
    form_dist: float
    hold: float
    tip_min_y: float = 9.0  # 振りの間と振り抜き（後 0.25 s）の、切っ先の最も低い高さ [m]（床 = 0）
    tip_peak_y: float = 9.0  # 最高速の瞬間（当たる瞬間）の切っ先の高さ [m]
    shield_front: float = 0.0  # 振りの前後（前 0.3 s・後 0.6 s）で、左手（盾）の目標が体の前（腕の長さ 0.4 より前）にある割合
    vdown: float = 0.0  # 最高速の瞬間の、切っ先の下向きの速さ [m/s]
    motion_axial: float = 0.0  # 速い間に切っ先が刀の軸に沿って動く割合（その振り自身の。突き下ろしなら 1 に近い）
    ground_align: float = 0.0  # その振り自身の軸の割合で混ぜた刃筋（型に依らない）
    ground: float = 0.0  # 倒れた相手への攻撃としての点
    hand: float = 0.0  # 最高速の瞬間の右手の速さ [m/s]（切っ先の速さのうち腕・体から来る分の目安。残りは手首の返し）
    self_share: float = 0.0  # 振りの前後で、人形の刃が自分の体に入るコマの割合（写像後の目標で。粗い: 人形は目標に届かないので self_cells を見る）
    track: float = -1.0  # なぞりの右手の目標からの平均のずれ（腕の長さ = 1。-1 は測っていない）
    self_cells: int = -1  # なぞった人形が自分の刃で失った体のセル（-1 は測っていない）
    score: float = 0.0


@dataclass
class ClipResult:
    file: str
    source: str
    form: str
    duration: float
    height: float
    gap: str
    reject: str
    swings: int
    best: Swing | None
    spikes: int
    gain: float = 2.0  # 写し方: 2 = 卓上の人形を外から（構えからのずれを 2 倍）、1 = 一人称（手の目標 = 見えているコントローラ）
    all_swings: list | None = None


def score_swing(sw: Swing) -> float:
    """0..100。重みは「速さ 30・刃筋 25・滑らかさ 15・狙い 15・型 15」。どれも物理の量を 0..1 へ写す（写し方の根拠は表の文書）。"""
    speed = min(1.0, sw.vmax / 12.0)  # 12 m/s ≈ 剣道の打突の切っ先の速さの下の方
    align = max(0.0, sw.align)
    smooth_n = min(1.0, max(0.0, (sw.ldlj + 14.0) / 8.0)) * (1.0 / (1 + sw.sub_peaks))
    aim = 0.5 * (1 + sw.aim_cos) * math.exp(-sw.aim_dist / 2.0)
    form = math.exp(-sw.form_dist / 2.0)
    base = 100 * (0.30 * speed + 0.25 * align + 0.15 * smooth_n + 0.15 * aim + 0.15 * form)
    selfcut = max(0.0, 2.0 * sw.self_share, sw.self_cells / 40.0)  # 自分を斬る振りは手本にならない: 40 セル（腕の肉の 1 か所ほど）で 0
    if sw.self_cells == CRASHED:
        return 0.0  # なぞりで落ちた振りは選ばない
    shield = (0.5 + 0.5 * sw.shield_front) if SHIELD else 1.0  # 盾を持つ型（--shield）: 振りの間も盾を前に保つ方が手本になる
    return base * (1.0 - min(1.0, selfcut)) * shield


def ground_score(sw: Swing) -> float:
    """倒れた相手への攻撃（振り下ろし・突き下ろし）としての点 0..100: 下向きの速さ 40・低さ 30・刃筋 30（型に依らない）× 自傷・握り。"""
    down = min(1.0, max(0.0, sw.vdown) / 8.0)  # 8 m/s ≈ 速い振り下ろしの切っ先の下向きの速さ
    low = min(1.0, max(0.0, (GROUND_HIGH - sw.tip_peak_y) / (GROUND_HIGH - 0.2)))  # 当たる瞬間の高さ（振り抜きで床を通るだけでは低くない）
    down = down * max(0.0, sw.vdown) / max(sw.vmax, 1e-6)  # 最高速のうち下向きの割合（横・上への振りは下へ通っても倒れた相手を打たない）
    align = max(0.0, sw.ground_align)
    selfcut = max(0.0, 2.0 * sw.self_share, sw.self_cells / 40.0)
    if sw.self_cells == CRASHED:
        return 0.0
    return 100 * (0.4 * down + 0.3 * low + 0.3 * align) * (1.0 - min(1.0, selfcut)) * (1.0 if sw.hold >= MIN_HOLD else 0.0)


def analyse(path: str, forms: dict[str, Form]) -> tuple[ClipResult, list[Swing], np.ndarray]:
    d = json.load(open(path, encoding="utf-8"))
    c = {n: i for i, n in enumerate(d["columns"])}
    F = np.array(d["frames"], float)
    name = os.path.basename(path)
    m = re.match(r"^\d{8}_\d{6}_([a-z]+)_(.+)\.vrmotion$", name)
    form_name = m.group(2) if m else name
    note = d.get("note", "")
    gap = note.split("gap ")[-1].split(" m")[0] if "gap" in note else ""
    t = F[:, 0]
    dur = float(t[-1] - t[0])
    height = float(d["playerHeight"])
    res = ClipResult(name, path, form_name, dur, height, gap, "", 0, None, 0, float(d.get("motionGain", 2.0)))
    if form_name not in forms:
        res.reject = "型の経路が無い"
        return res, [], F
    if dur < MIN_DURATION or len(t) < 20:
        res.reject = f"短すぎる（{dur:.1f} s）"
        return res, [], F
    dt = float(np.median(np.diff(t)))

    def col(prefix: str, k: int = 3) -> np.ndarray:
        i = c[prefix]
        return F[:, i:i + k]

    r_pos_raw = col("rPos.x")
    r_pos, r_q = despike(r_pos_raw), despike_quat(col("rQ.x", 4))
    res.spikes = int(np.sum(np.linalg.norm(r_pos_raw - r_pos, axis=1) > SPIKE))
    hand_speed = np.linalg.norm(smooth(r_pos, dt, 1), axis=1)
    fwd = unit(qrot(r_q, np.array([0.0, 0.0, -1.0])))
    edge = -unit(qrot(r_q, np.array([0.0, 1.0, 0.0])))
    tip = r_pos + BLADE_HUMAN * fwd
    tip_s = smooth(tip, dt)
    vel = smooth(tip, dt, 1)
    jerk = smooth(tip, dt, 3, window_s=0.15, order=4)
    speed = np.linalg.norm(vel, axis=1)

    # 写像後（体フレーム、腕の長さ = 1）: 型と比べる
    rt = smooth(despike(col("rTarget.x")), dt)
    left_t = smooth(despike(col("lTarget.x")), dt)
    wq = despike_quat(col("rWeaponQ.x", 4))
    blade_b = unit(qrot(wq, np.array([1.0, 0.0, 0.0])))
    tip_b = smooth(rt + BLADE_FORM * blade_b, dt)
    vel_b = smooth(rt + BLADE_FORM * blade_b, dt, 1)
    hold = F[:, c["rGrab"]] > 0.5
    tracked = F[:, c["rTracked"]] > 0.5
    selfhit = self_hit(rt, blade_b)

    form = forms[form_name]
    a = form.axial
    f_lo, f_hi = form.seg
    f_path = np.hstack([form.right[f_lo:f_hi + 1], form.tip[f_lo:f_hi + 1]])
    f_peak_tip = form.tip[form.peak]
    f_peak_dir = unit(form.tip_vel[form.peak])

    peaks, _ = find_peaks(speed, height=MIN_SPEED, prominence=1.0, distance=max(1, int(0.3 / dt)))
    swings: list[Swing] = []
    for p in peaks:
        lo, hi = segment(speed, p, dt)
        if not tracked[max(0, lo - 3):hi + 4].all():
            continue  # トラッキングが切れた所（位置が 0 へ飛ぶ）は振りではない
        if (hi - lo) * dt < MIN_SWING:
            continue
        fast = np.arange(lo, hi + 1)
        fast = fast[speed[fast] > FAST_SHARE * speed[p]]
        w = speed[fast] ** 2
        v = vel[fast]
        vn = unit(v)
        axial = np.einsum("ij,ij->i", vn, fwd[fast])
        perp = unit(v - np.einsum("ij,ij->i", v, fwd[fast])[:, None] * fwd[fast])
        cut = np.einsum("ij,ij->i", perp, edge[fast])
        cut_align = float(np.sum(w * cut) / np.sum(w))
        thrust_align = float(np.sum(w * axial * math.copysign(1.0, a)) / np.sum(w))
        align = abs(a) * thrust_align + (1 - abs(a)) * cut_align
        seg_speed = speed[lo:hi + 1]
        sub, _ = find_peaks(seg_speed, height=0.3 * speed[p], prominence=0.15 * speed[p])
        T = (hi - lo) * dt
        ldlj = -math.log(max(1e-12, (T ** 3 / speed[p] ** 2) * float(np.sum(np.linalg.norm(jerk[lo:hi + 1], axis=1) ** 2) * dt))) if T > 0 else -99
        aim_dist = float(np.linalg.norm(tip_b[p] - f_peak_tip))
        aim_cos = float(np.dot(unit(vel_b[p]), f_peak_dir))
        vr_path = np.hstack([rt[lo:hi + 1], tip_b[lo:hi + 1]])
        form_dist = dtw_mean(resample(vr_path, 40), resample(f_path, 40)) if hi > lo else 9.0
        sw = Swing(float(t[p] - t[0]), float(t[lo] - t[0]), float(t[hi] - t[0]), float(speed[p]), align, cut_align, thrust_align,
                   max(0, len(sub) - 1), ldlj, aim_dist, aim_cos, form_dist, float(hold[lo:hi + 1].mean()))
        sw.hand = float(hand_speed[p])
        g_hi = min(len(t), hi + int(0.25 / dt) + 1)
        sw.tip_min_y = float(tip_s[lo:g_hi, 1].min())
        sw.vdown = float(-vel[p, 1])
        sw.tip_peak_y = float(tip_s[p, 1])
        own_axial = float(np.sum(w * np.abs(axial)) / np.sum(w))  # その振り自身が軸に沿って動く割合（= 突きとしての刃筋）
        sw.motion_axial = own_axial
        sw.ground_align = own_axial * own_axial + (1 - own_axial) * cut_align
        sw.ground = ground_score(sw)
        w_lo, w_hi = max(0, lo - int(SELF_WINDOW[0] / dt)), min(len(t), hi + int(SELF_WINDOW[1] / dt) + 1)
        sw.self_share = float(selfhit[w_lo:w_hi].mean())
        sw.shield_front = float((left_t[w_lo:w_hi, 2] > 0.4).mean())
        sw.score = score_swing(sw)
        swings.append(sw)
    res.swings = len(swings)
    res.all_swings = swings
    if form_name in NOT_RECORDED:
        res.reject = NOT_RECORDED[form_name]
        return res, swings, F
    if not swings:
        res.reject = f"振りが無い（切っ先 {speed.max():.1f} m/s）"
        return res, swings, F

    def usable(s: Swing) -> str:
        if s.hold < MIN_HOLD:
            return f"刀を持っていない（握り {s.hold:.0%}）"
        if s.t_start < EDGE_PAD or s.t_end > dur - EDGE_PAD:
            return "振りの途中で切れている"
        return ""

    ok = [s for s in swings if not usable(s)]
    best = max(ok or swings, key=lambda s: s.score)
    res.best = best
    if not ok:
        res.reject = usable(best)
    elif res.gain > 1.0 and height < MIN_HEIGHT:  # 身長は 2 倍の写し方（肩の位置の見積もり）でだけ使う
        res.reject = f"身長の合わせ損ね（{height:.2f} m）"
    return res, swings, F


def trim(path: str, sw: Swing, out_path: str, pre: float = 1.0, post: float = 0.8) -> None:
    """振りの前後だけを切り出した写し（時刻は 0 から）。"""
    d = json.load(open(path, encoding="utf-8"))
    frames = d["frames"]
    t0 = frames[0][0]
    a, b = sw.t_start - pre + t0, sw.t_end + post + t0
    kept = [f for f in frames if a <= f[0] <= b]
    base = kept[0][0]
    for f in kept:
        f[0] = round(f[0] - base, 5)
    d["frames"] = kept
    d["note"] = d.get("note", "") + f" | picked swing {sw.t_start:.2f}-{sw.t_end:.2f} s of {os.path.basename(path)}"
    with open(out_path, "w", encoding="utf-8") as fh:
        json.dump(d, fh, separators=(",", ":"))


def swing_key(r: ClipResult, sw: Swing) -> str:
    """なぞりの測定の鍵: 記録と振りの始まりの時刻（同じ記録の別の振りを取り違えない）。"""
    return f"{r.file[:15]}_{r.form}_{int(round(sw.t_start * 100))}"


def best_key(r: ClipResult) -> str:
    return swing_key(r, r.best)


def ground_key(r: ClipResult, k: int) -> str:
    return swing_key(r, r.all_swings[k])


def ground_candidates(results: list[ClipResult]) -> list[tuple[ClipResult, int, Swing]]:
    """全部の記録の全部の振りのうち、切っ先が腰より下（GROUND_LOW）へ来て、刀を握り、下向きに動くもの（型の名前に依らない）。"""
    out = []
    for r in results:
        for k, sw in enumerate(r.all_swings or []):
            if sw.tip_peak_y < GROUND_LOW and sw.hold >= MIN_HOLD and sw.vdown > 0 and r.duration - EDGE_PAD > sw.t_end and sw.t_start > EDGE_PAD:
                out.append((r, k, sw))
    return sorted(out, key=lambda x: -x[2].ground)


def mapping_name(gain: float) -> str:
    return "一人称（等倍）" if gain <= 1.0 else "卓上（2 倍）"


def fmt(x: float, spec: str) -> str:
    if x == CRASHED:
        return "落ちる"
    return "" if x < 0 or x != x else format(x, spec)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("dirs", nargs="+")
    ap.add_argument("--forms", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--json")
    ap.add_argument("--pick-dir", help="型ごとの上位（除外でないもの）の振りを切り出して書く")
    ap.add_argument("--trim-all", help="除外でない記録の全部の一番の振りを切り出して書く（なぞりで測るため）")
    ap.add_argument("--sim", help="なぞりの測定（file,right_error,left_error,self_cells の CSV。tools/vrmotion_sim.sh が書く）")
    ap.add_argument("--ground-rows", type=int, default=25)
    ap.add_argument("--ground-sim", type=int, default=16, help="--trim-all で、倒れた相手への攻撃の上位をいくつ切り出すか（なぞりで測る）")
    ap.add_argument("--ground-picks", type=int, default=4, help="倒れた相手への攻撃として切り出す数")
    ap.add_argument("--weapon", default="katana", help="記録の名前の武器（katana・ironsword…）。この武器の記録だけを読む")
    ap.add_argument("--blade-scale", type=float, default=1.0, help="刀身の長さの刀に対する比（片手剣は 1.214: 型の書き出しの blade の比）")
    ap.add_argument("--shield", action="store_true", help="左手が盾を持つ記録")
    ap.add_argument("--close", type=float, default=5.0, help="2 本目も選ぶ点の差")
    args = ap.parse_args()
    global BLADE_HUMAN, BLADE_FORM, SHIELD
    BLADE_HUMAN *= args.blade_scale
    BLADE_FORM *= args.blade_scale
    SHIELD = args.shield
    forms = load_forms(args.forms)
    files = sorted({os.path.basename(p): p for d in args.dirs for p in glob.glob(os.path.join(d, f"*_{args.weapon}_*.vrmotion"))}.values(), key=os.path.basename)
    results = []
    sim = {}
    if args.sim and os.path.exists(args.sim):
        for line in open(args.sim, encoding="utf-8"):
            parts = line.strip().split(",")
            if len(parts) == 4 and parts[0] != "file":
                sim[parts[0]] = (float(parts[1]), float(parts[2]), int(parts[3]))  # NaN は測れなかった
    for p in files:
        r, _, _ = analyse(p, forms)
        if r.best and best_key(r) in sim:
            r.best.track, _, r.best.self_cells = sim[best_key(r)]
            r.best.score = score_swing(r.best)
            r.best.ground = ground_score(r.best)
        for k, sw in enumerate(r.all_swings or []):
            if ground_key(r, k) in sim:
                sw.track, _, sw.self_cells = sim[ground_key(r, k)]
                sw.ground = ground_score(sw)
        results.append(r)
    if args.trim_all:
        os.makedirs(args.trim_all, exist_ok=True)
        for r in results:
            if r.best and not r.reject:
                trim(r.source, r.best, os.path.join(args.trim_all, best_key(r) + ".vrmotion"))
        for r, k, sw in ground_candidates(results)[:args.ground_sim]:
            trim(r.source, sw, os.path.join(args.trim_all, ground_key(r, k) + ".vrmotion"))
    order = list(forms)
    lines = ["| 型 | 記録 | 写し方 | 長さ s | 飛び | 振り | 切っ先 m/s | 手 m/s | 刃筋 | 副の山 | LDLJ | 狙い（ずれ / cos） | 型のずれ | なぞりのずれ | 自傷セル | 盾が前 | 握り | 点 | 除外 |",
             "|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---|"]
    for r in sorted(results, key=lambda r: (order.index(r.form) if r.form in order else 99, -(r.best.score if r.best else -1))):
        b = r.best
        if b:
            lines.append(f"| {r.form} | {r.file[:15]} | {mapping_name(r.gain)} | {r.duration:.1f} | {r.spikes} | {r.swings} | {b.vmax:.1f} | {b.hand:.1f} | {b.align:+.2f} | {b.sub_peaks} | {b.ldlj:.1f} | "
                         f"{b.aim_dist:.2f} / {b.aim_cos:+.2f} | {b.form_dist:.2f} | {fmt(b.track, '.2f')} | {fmt(b.self_cells, 'd')} | {b.shield_front:.0%} | {b.hold:.0%} | {b.score:.0f} | {r.reject} |")
        else:
            lines.append(f"| {r.form} | {r.file[:15]} | {mapping_name(r.gain)} | {r.duration:.1f} | {r.spikes} | 0 | | | | | | | | | | | | | {r.reject} |")
    ground = ground_candidates(results)
    lines += ["", "### 倒れた相手への攻撃の候補（全部の記録の全部の振りから。点の順）", "",
              f"最高速の瞬間に切っ先が {GROUND_LOW} m より下にあり、刀を握り、下向きに動く振り {len(ground)} 本。",
              "",
              "| 点 | 記録（型） | 写し方 | 区間 s | 当たる瞬間の高さ m | 切っ先の最低 m | 下向き m/s | 切っ先 m/s | 軸の割合 | 刃筋 | なぞりのずれ | 自傷セル |",
              "|---:|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|"]
    for r, k, sw in ground[:args.ground_rows]:
        lines.append(f"| {sw.ground:.0f} | {r.file[:15]}（{r.form}） | {mapping_name(r.gain)} | {sw.t_start:.2f}–{sw.t_end:.2f} | {sw.tip_peak_y:.2f} | {sw.tip_min_y:.2f} | {sw.vdown:.1f} | "
                     f"{sw.vmax:.1f} | {sw.motion_axial:.2f} | {sw.ground_align:+.2f} | {fmt(sw.track, '.2f')} | {fmt(sw.self_cells, 'd')} |")
    with open(args.out, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")
    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump([{**asdict(r), "best": asdict(r.best) if r.best else None} for r in results], fh, ensure_ascii=False, indent=1)
    if args.pick_dir:
        os.makedirs(args.pick_dir, exist_ok=True)
        for form in order:
            ok = sorted((r for r in results if r.form == form and r.best and not r.reject and r.best.score > 0), key=lambda r: -r.best.score)
            picks = ok[:1] + [r for r in ok[1:2] if ok[0].best.score - r.best.score <= args.close]
            for rank, r in enumerate(picks, 1):
                out = os.path.join(args.pick_dir, f"{args.weapon}_{form}_{rank}_{r.file[:15]}.vrmotion")
                trim(r.source, r.best, out)
                print(f"pick {form} #{rank}: {r.file} swing {r.best.t_start:.2f}-{r.best.t_end:.2f} s score {r.best.score:.0f} -> {out}")
        for n, (r, k, sw) in enumerate([g for g in ground if g[2].ground > 0][:args.ground_picks], 1):
            out = os.path.join(args.pick_dir, f"{args.weapon}_ground_{n}_{r.file[:15]}_{r.form}.vrmotion")
            trim(r.source, sw, out)
            print(f"ground #{n}: {r.file} swing {sw.t_start:.2f}-{sw.t_end:.2f} s score {sw.ground:.0f} -> {out}")
    print(f"{len(results)} clips -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
