"""Summarise an MSTest .trx: counts, wall time, failed tests (first line of the message) and the slowest classes.

usage: py -3 tools/trx-summary.py <file.trx> [--classes N] [--all-classes]
Short on purpose: an agent reads this instead of the trx or the console log.
"""
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from datetime import datetime

NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def seconds(text):
    h, m, s = text.split(':')
    return int(h) * 3600 + int(m) * 60 + float(s)


def stamp(text):
    # 2026-11-17T21:25:02.8559324+09:00: fromisoformat before 3.11 takes at most 6 fractional digits
    return datetime.fromisoformat(re.sub(r'(\.\d{6})\d+', r'\1', text)) if text else None


def first_line(text):
    lines = (text or '').strip().splitlines()
    return lines[0] if lines else ''


def main(argv):
    path = argv[1]
    limit = int(argv[argv.index('--classes') + 1]) if '--classes' in argv else 10
    if '--all-classes' in argv:
        limit = 10 ** 6
    root = ET.parse(path).getroot()
    class_of = {d.get('id'): d.find('t:TestMethod', NS).get('className').split('.')[-1]
                for d in root.iterfind('.//t:UnitTest', NS)}
    slow_classes = {class_of[d.get('id')] for d in root.iterfind('.//t:UnitTest', NS)
                    if any(c.get('TestCategory') == 'slow' for c in d.iterfind('.//t:TestCategoryItem', NS))}
    counters = root.find('.//t:Counters', NS)
    times = root.find('t:Times', NS)
    start = stamp(times.get('start')) if times is not None else None
    finish = stamp(times.get('finish')) if times is not None else None
    wall = (finish - start).total_seconds() if start and finish else 0.0
    total = defaultdict(float)
    count = defaultdict(int)
    failed = []
    for r in root.iterfind('.//t:UnitTestResult', NS):
        name = class_of.get(r.get('testId'), '?')
        total[name] += seconds(r.get('duration', '0:0:0'))
        count[name] += 1
        outcome = r.get('outcome')
        if outcome not in ('Passed', 'NotExecuted'):
            msg = r.find('.//t:ErrorInfo/t:Message', NS)
            first = first_line(msg.text if msg is not None else '')
            failed.append(f'[{outcome}] {name}.{r.get("testName")}: {first[:300]}')
    c = counters.attrib if counters is not None else {}
    not_run = int(c.get('total', 0)) - int(c.get('executed', 0))
    print(f'total {c.get("total")} executed {c.get("executed")} passed {c.get("passed")} failed {c.get("failed")} '
          f'not-run {not_run}; wall {wall / 60:.1f} min, '
          f'sum of test times {sum(total.values()) / 60:.1f} min')
    for f in failed:
        print('FAILED', f)
    untagged = sorted((n for n in total if total[n] >= 5.0 and n not in slow_classes and not n.endswith('Probe')), key=lambda n: -total[n])
    if untagged:
        print('classes over 5 s without [TestCategory("slow")] (they slow the fast tier; tag them): '
              + ', '.join(f'{n} {total[n]:.0f}s' for n in untagged))
    print(f'slowest classes (of {len(total)}):')
    for name in sorted(total, key=lambda n: -total[n])[:limit]:
        print(f'  {total[name]:7.1f}s {count[name]:4d} tests  {name}')
    return 1 if failed or not_run > 0 else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
