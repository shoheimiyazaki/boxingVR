"""駆動 DOE の解析（docs/plans/DRIVE_DOE_ANALYSIS.md の数字を出す）。

使い方: py -3 tools/drive_doe_analysis.py [lhs.csv] [oat.csv]
- 要素ごとの寄与: 標準化した要素の 2 次 + 相互作用の回帰（リッジ）で、5 分割の交差検証の R² の落ち（その要素を外したとき）。
- 自由 / 固定の対の差（体の寄与）。
- 縮約モデルの当たり（相関・偏り・RMSE）と、残差の回帰。
- 1 要素ずつの格子（OAT）の応答曲線と、基準での傾き。
"""
import sys
import itertools
import numpy as np

DIR = "docs/arena/drive/"
lhs_path = sys.argv[1] if len(sys.argv) > 1 else DIR + "doe_lhs.csv"
oat_path = sys.argv[2] if len(sys.argv) > 2 else DIR + "doe_oat.csv"


def load(path):
    with open(path, encoding="utf-8") as f:
        head = f.readline().strip().split(",")
        rows = [l.strip().split(",") for l in f if l.strip()]
    cols = {h: [r[i] for r in rows] for i, h in enumerate(head)}
    num = {}
    for h, v in cols.items():
        try:
            num[h] = np.array([float(x) if x not in ("NaN", "") else np.nan for x in v])
        except ValueError:
            num[h] = np.array(v)
    return num


FACT = ["MaxHandForce", "ElbowMax", "ShoulderMax", "ActTime", "HandKp", "LooseDamp", "ArmServoHz", "BodyEff", "BodyTime", "ReactShare",
        "BorrowSpeed", "BodyDrive", "MaxTwistDeg", "HipShare", "UprightKpRatio", "TwistBias", "StepInScale", "Spring", "Range", "Telegraph",
        "Curvature", "MinLeverDeg", "LeadHz", "TrunkTorque", "TrunkFF"]


def design(X, quad=True, inter=True):
    n, p = X.shape
    cols = [np.ones(n)] + [X[:, j] for j in range(p)]
    names = ["1"] + [str(j) for j in range(p)]
    if quad:
        cols += [X[:, j] ** 2 for j in range(p)]
        names += [f"{j}^2" for j in range(p)]
    if inter:
        for a, b in itertools.combinations(range(p), 2):
            cols.append(X[:, a] * X[:, b])
            names.append(f"{a}*{b}")
    return np.column_stack(cols), names


def ridge_fit(A, y, lam):
    p = A.shape[1]
    reg = lam * np.eye(p)
    reg[0, 0] = 0
    return np.linalg.solve(A.T @ A + reg, A.T @ y)


def cv_r2(X, y, lam=1.0, folds=5, **kw):
    n = len(y)
    idx = np.arange(n) % folds
    rng = np.random.default_rng(0)
    rng.shuffle(idx)
    pred = np.zeros(n)
    for k in range(folds):
        tr, te = idx != k, idx == k
        A, _ = design(X[tr], **kw)
        w = ridge_fit(A, y[tr], lam)
        B, _ = design(X[te], **kw)
        pred[te] = B @ w
    ss = ((y - pred) ** 2).sum()
    st = ((y - y.mean()) ** 2).sum()
    return 1 - ss / st


def importance(X, y, names, lam=1.0):
    """2 次 + 相互作用のリッジ回帰を 1 回当てはめ、要素 j を含む項の寄与の分散（その項だけで予測を動かした分）を重要度とする。
    返り値の第 1 は 5 分割の交差検証の R²。"""
    base = cv_r2(X, y, lam)
    A, tnames = design(X)
    w = ridge_fit(A, y, lam)
    out = []
    for j in range(X.shape[1]):
        cols = [i for i, n in enumerate(tnames) if n != "1" and str(j) in n.replace("^2", "").split("*")]
        contrib = A[:, cols] @ w[cols]
        out.append((contrib.var() / max(1e-12, y.var()), names[j]))
    out.sort(reverse=True)
    return base, out


def main():
    d = load(lhs_path)
    FACT[:] = [f for f in FACT if f in d]
    frozen = d["frozen"] == 1
    hit = d["hit"] == 1
    X_all = np.column_stack([d[f] for f in FACT])
    print(f"LHS rows {len(frozen)}  frozen {frozen.sum()}  hits free {(hit & ~frozen).sum()} / {(~frozen).sum()}  hits frozen {(hit & frozen).sum()} / {frozen.sum()}")
    for mode, mask in (("free", ~frozen), ("frozen", frozen)):
        for yname in ("peakFist", "fistHit", "closing", "mass", "energy"):
            m = mask & (hit if yname in ("fistHit", "closing", "mass", "energy") else np.ones_like(hit))
            if m.sum() < 60:
                continue
            X = X_all[m]
            keep = X.std(axis=0) > 0
            mu, sd = X.mean(axis=0), X.std(axis=0)
            Z = (X - mu) / np.where(sd > 0, sd, 1)
            y = d[yname][m]
            names = [n for n, k in zip(FACT, keep) if k]
            base, imp = importance(Z[:, keep], y, names)
            print(f"\n== {mode} / {yname}: n={m.sum()}  mean={y.mean():.3f} sd={y.std():.3f}  CV R2(quadratic+interactions)={base:.3f}")
            print("   重要度（その要素を含む項の分散 / 全分散）:", ", ".join(f"{n} {v:+.3f}" for v, n in imp[:10]))
            # 標準化した線形の効き（符号つき、y の標準偏差あたり）
            A = np.column_stack([np.ones(m.sum()), Z[:, keep]])
            w = np.linalg.lstsq(A, y, rcond=None)[0]
            order = np.argsort(-np.abs(w[1:]))
            print("   線形の効き（±1 SD あたり、元の単位）:", ", ".join(f"{names[i]} {w[1 + i]:+.3f}" for i in order[:10]))

    # 自由 - 固定の対
    ids = d["id"]
    free_i = {s[:-1]: i for i, s in enumerate(ids) if str(s).endswith("f")}
    frz_i = {s[:-1]: i for i, s in enumerate(ids) if str(s).endswith("z")}
    common = sorted(set(free_i) & set(frz_i))
    if common:
        fi = np.array([free_i[c] for c in common])
        zi = np.array([frz_i[c] for c in common])
        for y in ("peakFist", "peakShoulderFwd", "peakChestYaw"):
            diff = d[y][fi] - d[y][zi]
            print(f"\n体の寄与（自由 − 固定） {y}: mean {np.nanmean(diff):+.3f}  sd {np.nanstd(diff):.3f}  range {np.nanmin(diff):+.2f}..{np.nanmax(diff):+.2f}")
        diff = d["peakFist"][fi] - d["peakFist"][zi]
        Z = (X_all[fi] - X_all[fi].mean(0)) / X_all[fi].std(0)
        A = np.column_stack([np.ones(len(fi)), Z])
        w = np.linalg.lstsq(A, diff, rcond=None)[0]
        order = np.argsort(-np.abs(w[1:]))
        print("   体の寄与を決める要素（線形、±1 SD あたり）:", ", ".join(f"{FACT[i]} {w[1 + i]:+.3f}" for i in order[:8]))

    # 縮約モデルの当たり
    print("\n== 縮約モデル（M33 = 質量 上腕+前腕 / M13 = 0.4 倍 / Ideal = Hill の速さの関係なし） vs 実測の最高速 peakFist")
    for mode, mask in (("free", ~frozen), ("frozen", frozen)):
        y = d["peakFist"][mask]
        for mname in ("m33PeakFist", "m13PeakFist", "mIdealPeakFist"):
            p = d[mname][mask]
            r = np.corrcoef(y, p)[0, 1]
            slope = np.polyfit(p, y, 1)
            print(f"   {mode:6s} {mname:15s} corr {r:+.3f}  bias(real-model) {np.mean(y - p):+.3f}  RMSE {np.sqrt(np.mean((y - p) ** 2)):.3f}  real≈{slope[0]:.2f}·model{slope[1]:+.2f}")
        # 残差の回帰: 実測 - M33 を要素で
        res = y - d["m33PeakFist"][mask]
        X = X_all[mask]
        Z = (X - X.mean(0)) / np.where(X.std(0) > 0, X.std(0), 1)
        A = np.column_stack([np.ones(len(res)), Z])
        w = np.linalg.lstsq(A, res, rcond=None)[0]
        order = np.argsort(-np.abs(w[1:]))
        print(f"   {mode} 残差（実測 − M33）の線形の効き:", ", ".join(f"{FACT[i]} {w[1 + i]:+.3f}" for i in order[:8]), f" (残差 mean {res.mean():+.3f} sd {res.std():.3f})")

    # OAT
    try:
        o = load(oat_path)
    except FileNotFoundError:
        return
    FACT[:] = [f for f in FACT if f in o]
    print("\n== 1 要素ずつの格子（peakFist 自由 / 固定、水準 0..8 は下限..上限）")
    ids = o["id"]
    baseline = {}
    for i, s in enumerate(ids):
        baseline.setdefault(s, i)
    for fac in FACT:
        row_f, row_z = [], []
        for k in range(9):
            for suffix, row in (("f", row_f), ("z", row_z)):
                key = f"{fac}@{k}{suffix}"
                row.append(o["peakFist"][baseline[key]] if key in baseline else np.nan)
        lo, hi = o[fac][baseline[f"{fac}@0f"]], o[fac][baseline[f"{fac}@8f"]]
        print(f"   {fac:15s} [{lo:.4g} .. {hi:.4g}] free  " + " ".join(f"{v:5.2f}" for v in row_f) + "   frozen " + " ".join(f"{v:5.2f}" for v in row_z))


main()
