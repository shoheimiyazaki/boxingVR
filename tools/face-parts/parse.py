"""Shared parser for the Codex face reference text files (docs/plans/face_refs). Used by build_face_parts.py and for inspection."""
import re

def _grid_block(lines, i, w, h):
    rows = [lines[i + k].rstrip("\n") for k in range(h)]
    for r in rows:
        if len(r) != w:
            raise ValueError(f"row width {len(r)} != {w}: {r!r}")
    return rows, i + h

def parse_parts(path):
    """Parts file -> {'category.n': {name, anchor, views: {FRONT: rows, SIDE: rows, TOP: rows}}}. Handles both parts_a and parts_b layouts."""
    lines = open(path, encoding="utf-8").read().split("\n")
    parts = {}
    cur = None
    i = 0
    while i < len(lines):
        ln = lines[i]
        m = re.match(r"^\[(\w+)\.(\d+)\]\s*(.*)$", ln) or re.match(r"^(\w+) (\d+): ([^;]*?)(?: (\d+)x(\d+))?; anchor", ln)
        if re.match(r"^\[(\w+)\.(\d+)\]", ln):
            m = re.match(r"^\[(\w+)\.(\d+)\]\s*(.*)$", ln)
            cur = {"category": m.group(1), "index": int(m.group(2)), "name": m.group(3), "views": {}, "anchorText": ""}
            parts[f"{m.group(1)}.{m.group(2)}"] = cur
        elif re.match(r"^(\w+) (\d+): .*; anchor", ln):
            m = re.match(r"^(\w+) (\d+): (.*?) (\d+)x(\d+); anchor \((\d+),(\d+)\)", ln)
            w, h = int(m.group(4)), int(m.group(5))
            rows, i = _grid_block(lines, i + 1, w, h)
            cur = {"category": m.group(1), "index": int(m.group(2)), "name": m.group(3), "views": {"FRONT": rows}, "anchorText": f"({m.group(6)},{m.group(7)})"}
            parts[f"{m.group(1)}.{m.group(2)}"] = cur
            continue
        elif cur is not None and ln.startswith("anchor"):
            cur["anchorText"] = ln
        elif cur is not None:
            m = re.match(r"^(FRONT|SIDE|TOP) (\d+) (\d+)$", ln)
            if m:
                w, h = int(m.group(2)), int(m.group(3))
                rows, i = _grid_block(lines, i + 1, w, h)
                cur["views"][m.group(1)] = rows
                continue
        i += 1
    return parts

def parse_base(path):
    lines = open(path, encoding="utf-8").read().split("\n")
    views = {}
    i = 0
    while i < len(lines):
        m = re.match(r"^(FRONT|SIDE|TOP|DEPTH) (\d+)x(\d+)", lines[i])
        if m and m.group(1) == "DEPTH":
            rows, i = _grid_block(lines, i + 2, int(m.group(2)), int(m.group(3)))
            views["DEPTH"] = rows
            continue
        if m:
            w, h = int(m.group(2)), int(m.group(3))
            rows, i = _grid_block(lines, i + 1, w, h)
            views[m.group(1)] = rows
            continue
        i += 1
    return views


def parse_d(path):
    """parts2/parts_d.txt -> {name: {view: 'FRONT'|'SIDE'|'TOP', w, h, rows}}。形式: `[name] 説明 | FRONT 8x5 | anchor ...` の次の行から空行まで。"""
    result = {}
    cur = None
    for ln in open(path, encoding="utf-8").read().split("\n"):
        m = re.match(r"^\[(\w+)\]\s*(.*?)\s*\|\s*(FRONT|SIDE|TOP) (\d+)x(\d+)", ln)
        if m:
            cur = {"desc": m.group(2), "view": m.group(3), "w": int(m.group(4)), "h": int(m.group(5)), "rows": []}
            result[m.group(1)] = cur
        elif cur is not None and ln.strip():
            cur["rows"].append(ln.strip())
        elif cur is not None and cur["rows"]:
            if len(cur["rows"]) != cur["h"] or any(len(r) != cur["w"] for r in cur["rows"]):
                raise ValueError(f"grid size mismatch in {path}: {cur}")
            cur = None
    return result
