"""胴のひねりの縮約モデルの当たりと、候補の予測（DRIVE_DOE_ANALYSIS.md §3 の数字）。py -3 tools/drive_doe_trunk.py [csv]"""
import sys
import numpy as np
argv_csv = sys.argv[1] if len(sys.argv) > 1 else "docs/arena/drive/doe_lhs_v3.csv"
sys.argv = [sys.argv[0], "x", "y"]
src = open("tools/drive_doe_analysis.py", encoding="utf-8").read().split("def main():")[0]
exec(src)
d = load(argv_csv)
fr = d["frozen"] == 0
real = d["realPeakChestRate"][fr]
print(f"n free = {fr.sum()}  real peak chest rate mean {real.mean():.2f} sd {real.std():.2f}")
ratios = [1, 2, 4, 6]
fits = {}
for r in ratios:
    p = d[f"tmP_base_r{r}"][fr]
    a, b = np.polyfit(p, real, 1)
    fits[r] = (a, b)
    print(f"model base ratio {r}: corr {np.corrcoef(real, p)[0,1]:+.3f}  bias(real-model) {np.mean(real-p):+.3f}  RMSE {np.sqrt(np.mean((real-p)**2)):.3f}  real≈{a:.2f}·model{b:+.2f}")
best = min(ratios, key=lambda r: np.sqrt(np.mean((real - d[f'tmP_base_r{r}'][fr]) ** 2)))
print(f"best effective yaw-inertia ratio of the model = {best}")
names = ["base", "ff", "rom50", "ff_rom50", "kp2", "kp4", "ff_kp2", "ff_rom50_kp2", "ff_rom50_kp4"]
print("\n候補の予測（自由、LHS 全点の平均、胸の鉛直軸まわりの最大角速度 [rad/s]。ratio = 胸の有効慣性の補正）")
print("   " + "name".ljust(16) + "".join(f"r{r}".rjust(9) for r in ratios))
for n in names:
    print("   " + n.ljust(16) + "".join(f"{d['tmP_'+n+'_r'+str(r)][fr].mean():9.2f}" for r in ratios))
