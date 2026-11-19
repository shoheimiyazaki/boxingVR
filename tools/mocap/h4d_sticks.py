import numpy as np, glob, os, sys
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
root, seq, t0, t1, out = sys.argv[1], sys.argv[2], float(sys.argv[3]), float(sys.argv[4]), sys.argv[5]
FPS = 20.0
files = sorted(glob.glob(os.path.join(root, "03_grappling2", seq, "processed_data", "poses3d", "*.npy")))
frames = [np.load(f, allow_pickle=True).item() for f in files]
ids = sorted(frames[0].keys())
P = {k: np.stack([fr[k][:, :3] for fr in frames]) for k in ids}
bones = [(5,7),(7,9),(6,8),(8,10),(5,6),(11,12),(5,11),(6,12),(11,13),(13,15),(12,14),(14,16),(0,5),(0,6)]
i0, i1 = int(t0 * FPS), min(int(t1 * FPS), len(frames) - 1)
idx = list(range(i0, i1 + 1, 2))
# view axis: perpendicular to the line between the two hips at the first frame
h = {k: 0.5 * (P[k][i0, 11] + P[k][i0, 12]) for k in ids}
line = h[ids[1]][:2] - h[ids[0]][:2]; line /= np.linalg.norm(line)
floor = np.percentile(np.concatenate([P[k][:, [15, 16], 2].ravel() for k in ids]), 2)
cols = 6; rows = (len(idx) + cols - 1) // cols
fig, axs = plt.subplots(rows, cols, figsize=(cols * 2.2, rows * 2.4))
mid = 0.5 * (h[ids[0]][:2] + h[ids[1]][:2])
for n, ax in enumerate(np.ravel(axs)):
    ax.set_xlim(-1.2, 1.2); ax.set_ylim(-0.05, 1.9); ax.set_aspect("equal"); ax.axis("off")
    if n >= len(idx): continue
    t = idx[n]
    ax.plot([-1.2, 1.2], [0, 0], color="0.7", lw=1)
    for k, c in zip(ids, ["tab:red", "tab:blue"]):
        p = P[k][t]
        x = (p[:, :2] - mid) @ line; z = p[:, 2] - floor
        for a, b in bones: ax.plot([x[a], x[b]], [z[a], z[b]], color=c, lw=2)
        ax.plot(x[0], z[0], "o", color=c, ms=4)
    ax.set_title(f"{t / FPS:.2f}s", fontsize=8)
plt.tight_layout(); plt.savefig(out, dpi=80)
print(out)
