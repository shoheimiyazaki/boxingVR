"""Write a character's practised ground finishing (groundtime --practice <id> --out <id>.json) or finish combinations
(groundtime --finish-practice <id>; every value goes to education.style) into its roster file.

    py -3 tools/ground-education.py docs/arena/groundtime/practice-<label>/<id>.json [--force]

Writes only into "education" (CLAUDE.md §1.8: the education is the input side):
  - education.style: GroundDrive, GroundRest, GroundLoad, GroundCut, and for fist fighters GroundStomp, GroundKick and the
    paths ("Ground").
  - fighters holding a weapon with a bot: the paths go to education.weaponBot.Ground (over the weapon's textbook).
Skips a practice whose held-out comparison was not better (Better = false) unless --force.
The roster files are hand formatted, so the keys are written as one line each into the existing "style" / "weaponBot"
object (replacing an earlier line of the same key). Running it twice gives the same file.
"""
import json
import re
import sys

STYLE_KEYS = ["GroundDrive", "GroundRest", "GroundLoad", "GroundCut", "GroundStomp", "GroundKick"]


def compact(value):
    return json.dumps(round_floats(value), ensure_ascii=False, separators=(", ", ": "))


def round_floats(v):
    if isinstance(v, float):
        return round(v, 3)
    if isinstance(v, dict):
        return {k: round_floats(x) for k, x in v.items() if x is not None or k == "MaxSink"}
    if isinstance(v, list):
        return [round_floats(x) for x in v]
    return v


def set_line(text, obj_key, key, value, nl):
    """Put `"key": value,` as the first line inside the object `"obj_key": {` under education (create the object if null)."""
    edu = text.index('"education"')
    m = re.compile(r'"%s":\s*(\{|null)' % re.escape(obj_key)).search(text, edu)
    if m is None:
        raise SystemExit(f'no "{obj_key}" under education')
    if m.group(1) == "null":
        text = text[:m.start()] + f'"{obj_key}": {{{nl}    }}' + text[m.end():]
        m = re.compile(r'"%s":\s*\{' % re.escape(obj_key)).search(text, edu)
    body_start = m.end()
    # drop an earlier line for this key (one line, written by this tool)
    line_re = re.compile(r'%s[ ]*"%s": [^\r\n]*' % (re.escape(nl), re.escape(key)))
    close = matching_brace(text, body_start - 1)
    inner = text[body_start:close]
    inner = line_re.sub("", inner, count=1)
    if inner.strip() in ("", ","):
        inner = ""
    indent = "      "
    empty = inner.strip() == ""
    entry = f'{nl}{indent}"{key}": {compact(value)}' + ("" if empty else ",")
    return text[:body_start] + entry + inner + text[close:] if not empty else text[:body_start] + entry + nl + "    " + text[close:]


def matching_brace(text, open_at):
    depth = 0
    in_str = False
    i = open_at
    while i < len(text):
        c = text[i]
        if in_str:
            if c == "\\":
                i += 2
                continue
            if c == '"':
                in_str = False
        elif c == '"':
            in_str = True
        elif c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    raise SystemExit("unbalanced braces")


def main():
    path = sys.argv[1]
    force = "--force" in sys.argv
    practice = json.load(open(path, encoding="utf-8-sig"))
    if not practice["Better"] and not force:
        print(f'{practice["Id"]}: not better on the held-out runs ({practice["Before"]:.2f} -> {practice["After"]:.2f}), skipped')
        return
    edu = practice["Education"]
    roster = f'games/MiniRing/Characters/roster/{practice["Id"]}.json'
    text = open(roster, encoding="utf-8", newline="").read()
    nl = "\r\n" if "\r\n" in text else "\n"
    if "Armed" not in edu:
        # フィニッシュの練習（groundtime --finish-practice）: どの値も education.style へ。
        for key in reversed(list(edu.keys())):
            text = set_line(text, "style", key, edu[key], nl)
        json.loads(text)
        open(roster, "w", encoding="utf-8", newline="").write(text)
        print(f'{practice["Id"]}: wrote finish ({practice["Before"]:.2f} -> {practice["After"]:.2f})')
        return
    armed = edu["Armed"]
    for key in reversed(STYLE_KEYS):
        if armed and key in ("GroundStomp", "GroundKick"):
            continue
        text = set_line(text, "style", key, edu[key], nl)
    if armed:
        text = set_line(text, "weaponBot", "Ground", edu["Ground"], nl)
    else:
        text = set_line(text, "style", "Ground", edu["Ground"], nl)
    json.loads(text)  # still valid JSON
    open(roster, "w", encoding="utf-8", newline="").write(text)
    print(f'{practice["Id"]}: wrote ({practice["Before"]:.2f} -> {practice["After"]:.2f})')


if __name__ == "__main__":
    main()
