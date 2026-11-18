"""Lay frames of a video out as one labelled PNG, so an agent can look at a whole clip with one image read.

usage: py -3 tools/sheet.py <video.mp4> [out.png] [--count N] [--cols C] [--width W] [--from S] [--to S] [--times t1,t2,...]

  --count N    frames spread evenly over the clip (default 12)
  --cols C     frames per row (default 4)
  --width W    width of one frame in the sheet [px] (default 480; 4 columns -> 1920 px)
  --from/--to  only this part of the clip [s]
  --times      exact times [s] instead of --count (e.g. the moments of the hits from *.events.json)

Each frame is labelled with its time in the clip (mm:ss.s). Needs ffmpeg on PATH and Pillow.
Writes <video>.sheet.png next to the video when no output is given, and prints its path.
"""
import os
import re
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFont


def arg(argv, name, default):
    if name not in argv:
        return default
    at = argv.index(name) + 1
    if at >= len(argv) or argv[at].startswith('--'):
        raise SystemExit(f'{name} needs a value')
    return argv[at]


def duration(path):
    # ffmpeg only (ffprobe is not always installed): "Duration: 00:00:40.03," on stderr.
    err = subprocess.run(['ffmpeg', '-hide_banner', '-i', path], capture_output=True, text=True,
                         encoding='utf-8', errors='replace').stderr
    match = re.search(r'Duration: (\d+):(\d+):(\d+(?:\.\d+)?)', err)
    if not match:
        raise SystemExit(f'cannot read the duration of {path}: {err.strip()[-300:]}')
    h, m, s = match.groups()
    return int(h) * 3600 + int(m) * 60 + float(s)


def frame(path, t, width, folder, index):
    target = os.path.join(folder, f'{index:03d}.png')
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-ss', f'{t:.3f}', '-i', path, '-frames:v', '1',
                    '-vf', f'scale={width}:-2', target], check=True)
    return Image.open(target).convert('RGB') if os.path.exists(target) else None


def label(t):
    return f'{int(t // 60):02d}:{t % 60:04.1f}'


def main(argv):
    positional = [a for i, a in enumerate(argv[1:], 1) if not a.startswith('--') and not argv[i - 1].startswith('--')]
    if not positional:
        print(__doc__)
        return 2
    video = positional[0]
    out = positional[1] if len(positional) > 1 else os.path.splitext(video)[0] + '.sheet.png'
    cols = int(arg(argv, '--cols', '4'))
    width = int(arg(argv, '--width', '480'))
    length = duration(video)
    start = float(arg(argv, '--from', '0'))
    end = min(float(arg(argv, '--to', str(length))), length - 0.05)
    if end <= start:
        raise SystemExit(f'empty range: --from {start} --to {end:.2f} (the clip is {length:.2f} s)')
    if '--times' in argv:
        asked = [float(x) for x in arg(argv, '--times', '').split(',') if x]
        times = [min(max(t, 0.0), length - 0.05) for t in asked]
        for a, t in zip(asked, times):
            if a != t:
                print(f'warning: {a} s is outside the clip (0..{length:.2f} s); using {t:.2f} s', file=sys.stderr)
    else:
        count = int(arg(argv, '--count', '12'))
        step = (end - start) / count
        times = [start + step * (i + 0.5) for i in range(count)]
    if not times:
        print('no frames asked for', file=sys.stderr)
        return 2
    with tempfile.TemporaryDirectory() as folder:
        frames = [(i + 1, t, frame(video, t, width, folder, i)) for i, t in enumerate(times)]
    for n, t, f in frames:
        if f is None:
            print(f'warning: no frame at {t:.2f} s (frame {n} left out)', file=sys.stderr)
    frames = [(n, t, f) for n, t, f in frames if f is not None]
    if not frames:
        print('ffmpeg returned no frames', file=sys.stderr)
        return 1
    cell_w, cell_h = frames[0][2].size
    rows = (len(frames) + cols - 1) // cols
    sheet = Image.new('RGB', (cell_w * min(cols, len(frames)), cell_h * rows), 'black')
    draw = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype('arial.ttf', max(14, cell_h // 14))
    except OSError:
        font = ImageFont.load_default()
    for i, (n, t, f) in enumerate(frames):
        x, y = (i % cols) * cell_w, (i // cols) * cell_h
        sheet.paste(f, (x, y))
        text = f'{n}  {label(t)}'
        box = draw.textbbox((x + 6, y + 4), text, font=font)
        draw.rectangle((box[0] - 4, box[1] - 2, box[2] + 4, box[3] + 2), fill='black')
        draw.text((x + 6, y + 4), text, fill='yellow', font=font)
    sheet.save(out)
    print(out)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
