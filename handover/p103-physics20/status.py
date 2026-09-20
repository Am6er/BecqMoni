# -*- coding: utf-8 -*-
r"""П103 — ход единого счёта склада: сцены готово/всего ПО ТЕЛАМ `.rmx` (не по времени), недостающие ключи,
команды досчёта `--only=` без `--force` (дальние — отдельно, --n=6000000). Пишет D:\BqMoni_Claude\p103\count_status.md
и печатает одну строку.
  python status.py [--note=текст]
"""
import io
import os
import re
import sys
import time

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

LANE = r'D:\BqMoni_Claude\p103'
STORE = os.path.join(LANE, 'store')
FAR = ['RC103_point50', 'ASN16_point10_house', 'G1S_point25']


def main(argv):
    note = ''
    for a in argv:
        if a.startswith('--note='):
            note = a[7:]
    keys = sorted(f[:-3] for f in os.listdir(STORE) if f.lower().endswith('.in'))
    ready = sorted(f[:-4] for f in os.listdir(STORE) if f.lower().endswith('.rmx') and os.path.getsize(os.path.join(STORE, f)) > 0)
    tmp = [f for f in os.listdir(STORE) if f.lower().endswith('.tmp')]
    missing = [k for k in keys if k not in ready]
    last = ''
    times = []
    for log in ('count.log', 'count2.log', 'count3.log'):
        p = os.path.join(LANE, log)
        if os.path.isfile(p):
            txt = io.open(p, encoding='utf-8', errors='replace').read()
            for m in re.finditer(r'^== (\S+)', txt, re.M):
                last = m.group(1)
            times += [(m.group(1), float(m.group(2)), float(m.group(3)), m.group(4))
                      for m in re.finditer(r'^== (\S+) ==\n\s*клеймо\s*:\s*\S+\n\s*время\s*:\s*([\d.]+) с на часах, ядер ([\d.]+).*\n\s*счёт\s*:.*\n(?:.*\n)*?\s*шум конт\.:\s*взвешенная ([\d.]+ %)', txt, re.M)]
    done = ''
    for f in ('count_far_done.txt', 'count_done.txt', 'count2_done.txt', 'count3_done.txt'):
        if os.path.isfile(os.path.join(LANE, f)):
            done += f + ': ' + io.open(os.path.join(LANE, f), encoding='utf-8', errors='replace').read().strip() + '; '
    now = time.strftime('%Y-%m-%d %H:%M:%S')
    miss_far = [k for k in missing if k in FAR]
    miss_std = [k for k in missing if k not in FAR]
    lines = [
        u'# П103 — ход единого счёта склада физики 20 (обновлено %s)' % now,
        u'',
        u'* сцен готово по телам `.rmx`: **%d из %d**; обрубков `.tmp`: %d; последняя в логе: `%s`' % (len(ready), len(keys), len(tmp), last or '—'),
        u'* завершения (`*_done.txt`): %s' % (done or 'нет — счёт идёт'),
        u'* недостающие (%d): %s' % (len(missing), ', '.join(missing) if missing else '—'),
        u'* досчёт после обрыва (без `--force`, готовые стоят): обычные — `python D:\\BqMoni_Claude\\p103\\scripts\\make_cmd.py only %s` → `store_run2.cmd`; дальние ×2 — `make_cmd.py far %s` → `store_run3.cmd`'
        % (','.join(miss_std) if miss_std else '<нечего>', ','.join(miss_far) if miss_far else '<нечего>'),
    ]
    if note:
        lines.append(u'* заметка: %s' % note)
    lines.append(u'')
    lines.append(u'| сцена | секунд | ядер | шум конт. |')
    lines.append(u'|---|---|---|---|')
    for k, s, c, nz in times:
        lines.append(u'| `%s` | %.1f | %.1f | %s |' % (k, s, c, nz))
    with io.open(os.path.join(LANE, 'count_status.md'), 'w', encoding='utf-8', newline='') as fh:
        fh.write(u'\n'.join(lines) + u'\n')
    print(u'%s: готово %d из %d, последняя %s, обрубков %d, done: %s' % (now, len(ready), len(keys), last or '—', len(tmp), done or 'нет'))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
