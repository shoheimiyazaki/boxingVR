"""bladecut の「境目」の表（武器 × 部位 × 段 = 最初に届いた速さ）を、既定の表と比べる（docs/PERFORMANCE.md §7）。

    py -3 tools/bladecut-compare.py docs/arena/simplify/bladecut_base.md docs/arena/simplify/bladecut_*.md

速さの段（2, 3, 4, 5, 6, 8, 10, 12, 15, 18, 22, 26, 30, 35, 40, 50 m/s）で何段ずれたかを数える。「—」（届かない）は最後の段の 1 つ上として数える。
"""
import re
import sys

STEPS = [2, 3, 4, 5, 6, 8, 10, 12, 15, 18, 22, 26, 30, 35, 40, 50]


def table(path):
    rows = {}
    inside = False
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("## 境目"):
                inside = True
                continue
            if inside and line.startswith("## "):
                break
            if not inside or not line.startswith("| ") or line.startswith("| 武器") or line.startswith("|---"):
                continue
            cells = [c.strip() for c in line.strip().strip("|").split("|")]
            key = (cells[0], cells[1])
            levels = []
            for c in cells[2:]:
                m = re.match(r"([\d.]+)", c)
                levels.append(STEPS.index(int(float(m.group(1)))) if m else len(STEPS))
            rows[key] = levels
    return rows


def main():
    base = table(sys.argv[1])
    print("| 案 | 比べた境目 | 同じ | 1 段ずれ | 2 段以上ずれ | 届かなくなった | 届くようになった | 平均のずれ [段]（+ は遅い = 斬れにくい） |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|")
    for path in sys.argv[2:]:
        other = table(path)
        diffs = []
        lost = gained = 0
        for key, levels in base.items():
            for a, b in zip(levels, other.get(key, [])):
                if a == len(STEPS) and b == len(STEPS):
                    diffs.append(0)
                    continue
                if b == len(STEPS):
                    lost += 1
                    continue
                if a == len(STEPS):
                    gained += 1
                    continue
                diffs.append(b - a)
        same = sum(1 for d in diffs if d == 0)
        one = sum(1 for d in diffs if abs(d) == 1)
        two = sum(1 for d in diffs if abs(d) >= 2)
        mean = sum(diffs) / len(diffs) if diffs else 0
        name = re.sub(r".*bladecut_(.*)\.md", r"\1", path.replace("\\", "/"))
        print(f"| {name} | {len(diffs) + lost + gained} | {same} | {one} | {two} | {lost} | {gained} | {mean:+.2f} |")


if __name__ == "__main__":
    main()
