import numpy as np, glob, os, sys, json
root = sys.argv[1]; FPS = 20.0
seqs = sorted(glob.glob(os.path.join(root, "03_grappling2", "*")))
report = []
for sd in seqs:
    files = sorted(glob.glob(os.path.join(sd, "processed_data", "poses3d", "*.npy")))
    if not files: continue
    frames = [np.load(f, allow_pickle=True).item() for f in files]
    ids = sorted(frames[0].keys())
    P = {k: np.stack([fr[k][:, :3] for fr in frames]) for k in ids}  # T x 17 x 3
    # up axis: head (0) minus ankles (15,16) over standing frames
    allp = np.concatenate([P[k] for k in ids])
    up_vec = np.median(allp[:, 0] - 0.5 * (allp[:, 15] + allp[:, 16]), axis=0)
    up = int(np.argmax(np.abs(up_vec))); sgn = np.sign(up_vec[up])
    floor = np.percentile(np.concatenate([sgn * P[k][:, [15, 16], up].ravel() for k in ids]), 2)
    out = {"seq": os.path.basename(sd), "frames": len(files), "up": up, "people": {}}
    for k in ids:
        hip = 0.5 * (P[k][:, 11] + P[k][:, 12]); hh = sgn * hip[:, up] - floor
        head = sgn * P[k][:, 0, up] - floor
        out["people"][k] = {"hipMax": float(np.percentile(hh, 95)), "hipMin": float(hh.min()), "headMax": float(np.percentile(head, 95))}
    # events: a person's hip drops from >= 75% of its standing hip height to <= 35% within 2 s
    ev = []
    for k in ids:
        hip = 0.5 * (P[k][:, 11] + P[k][:, 12]); hh = sgn * hip[:, up] - floor
        stand = np.percentile(hh, 95)
        for t in range(len(hh)):
            if hh[t] <= 0.35 * stand:
                w = hh[max(0, t - 40):t]
                if len(w) and w.max() >= 0.75 * stand and (not ev or ev[-1][0] != k or t - ev[-1][1] > 40):
                    t0 = max(0, t - 40) + int(np.argmax(w))
                    ev.append((k, t, t0))
    out["events"] = [{"down": k, "t_down": t / FPS, "t_start": t0 / FPS, "fall_s": (t - t0) / FPS} for k, t, t0 in ev]
    report.append(out)
for r in report:
    print(r["seq"], r["frames"], "up", r["up"], {k: (round(v["hipMax"], 2), round(v["hipMin"], 2), round(v["headMax"], 2)) for k, v in r["people"].items()})
    for e in r["events"]: print("   ", e)
json.dump(report, open(os.path.join(root, "..", "takedowns.json"), "w"), indent=1)
