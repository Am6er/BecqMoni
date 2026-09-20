# -*- coding: utf-8 -*-
r"""П97 — ход единого счёта склада: сцены готово/всего ПО ТЕЛАМ `.rmx` (не по времени), недостающие ключи,
команда досчёта `--only=` без `--force`. Пишет D:\BqMoni_Claude\p97\status.md и печатает одну строку.
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

LANE = r'D:\BqMoni_Claude\p97'
STORE = os.path.join(LANE, 'store')


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
            times += [(m.group(1), float(m.group(2))) for m in re.finditer(r'^== (\S+).*?\n(?:.*\n)*?\s*время\s*:\s*([\d.]+) с', txt, re.M)]
    done_files = [f for f in ('count_done.txt', 'count2_done.txt', 'count3_done.txt') if os.path.isfile(os.path.join(LANE, f))]
    done = ''
    for f in done_files:
        done += f + ': ' + io.open(os.path.join(LANE, f), encoding='utf-8', errors='replace').read().strip() + '; '
    now = time.strftime('%Y-%m-%d %H:%M:%S')
    lines = [
        u'# П97 — ход единого счёта склада физики 19 (обновлено %s)' % now,
        u'',
        u'* сцен готово по телам `.rmx`: **%d из %d**; обрубков `.tmp`: %d; последняя в логе: `%s`' % (len(ready), len(keys), len(tmp), last or '—'),
        u'* завершения (`*_done.txt`): %s' % (done or 'нет — счёт идёт'),
        u'* недостающие (%d): %s' % (len(missing), ', '.join(missing) if missing else '—'),
        u'* досчёт после обрыва (без `--force`, готовые стоят): `python D:\\BqMoni_Claude\\p97\\make_cmd.py only %s` → `store_run2.cmd`' % (','.join(missing) if missing else '<нечего>'),
    ]
    if note:
        lines.append(u'* заметка: %s' % note)
    lines.append(u'')
    lines.append(u'| сцена | секунд |')
    lines.append(u'|---|---|')
    for k, s in times:
        lines.append(u'| `%s` | %.1f |' % (k, s))
    with io.open(os.path.join(LANE, 'status.md'), 'w', encoding='utf-8', newline='') as fh:
        fh.write(u'\n'.join(lines) + u'\n')
    print(u'%s готово %d/%d, tmp %d, последняя %s, done: %s' % (now, len(ready), len(keys), len(tmp), last or '—', done or 'нет'))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
