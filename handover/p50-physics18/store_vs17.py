# -*- coding: utf-8 -*-
r"""Физика 18 против физики 17 по всем сценам склада — ТОЛЬКО ЧТЕНИЕ (П50, 14.09.2026; образец — П37 §5.2).

На каждую сцену, у которой в складе worktree лежит `.rmx`: `MatrixDiffProbe --a=<живой физики 17>
--b=<worktree физики 18>` (медианы пика / суммы / формы L1 по узлам) в `<out>/diff_<сцена>.txt`,
`rmx_nodes.py` (узлы 662 / 1461 / 2614) в `<out>/nodes_<сцена>.txt`, сводная таблица — в
`<out>.txt` и на печать. Разделитель дробной части — точка.

  python store_vs17.py [--out=D:\BqMoni_Claude\p50\art\store_vs17]
"""
import io
import os
import re
import subprocess
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
WT = r'D:\BqMoni_Claude\p50\wt'
BIN = os.path.join(WT, 'tools', 'effmaker', 'probes', 'build_p50')
LIVE = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'geometries')
NEW = os.path.join(WT, 'tools', 'CORPUS', 'corpus', 'geometries')
NODES = os.path.join(ROOT, 'handover', 'p50-physics18', 'rmx_nodes.py')


def median_line(text, label):
    m = re.search(u'^\\s*' + re.escape(label) + u'\\s*:\\s*([-+0-9.]+)\\s*/\\s*([-+0-9.]+)', text, re.M)
    return (float(m.group(1)), float(m.group(2))) if m else (float('nan'), float('nan'))


def node_row(text, kev):
    for line in text.splitlines():
        parts = line.split()
        if len(parts) >= 10:
            try:
                e = float(parts[0])
            except ValueError:
                continue
            if abs(e - kev) < 40:
                return float(parts[4]), float(parts[7])
    return float('nan'), float('nan')


def main(argv):
    out = r'D:\BqMoni_Claude\p50\art\store_vs17'
    for a in argv[1:]:
        if a.startswith('--out='):
            out = a[6:]
    os.makedirs(out, exist_ok=True)
    env = dict(os.environ, PYTHONIOENCODING='utf-8')
    scenes = sorted(f[:-4] for f in os.listdir(NEW) if f.endswith('.rmx'))
    rows = []
    hdr = u'%-30s %8s %8s %7s %9s %9s %9s %9s %9s %9s' % (
        u'сцена', u'пик мед', u'сум мед', u'L1 мед', u'Δпик662', u'Δсум662', u'Δпик1461', u'Δсум1461', u'Δпик2614', u'Δсум2614')
    lines = [hdr]
    for s in scenes:
        a = os.path.join(LIVE, s + '.rmx')
        b = os.path.join(NEW, s + '.rmx')
        if not os.path.exists(a):
            lines.append(u'%-30s  (в живом складе нет)' % s)
            continue
        d = subprocess.run([os.path.join(BIN, 'MatrixDiffProbe.exe'), '--a=' + a, '--b=' + b],
                           capture_output=True, text=True, encoding='utf-8', errors='replace')
        io.open(os.path.join(out, 'diff_%s.txt' % s), 'w', encoding='utf-8', newline='').write(d.stdout)
        n = subprocess.run([sys.executable, NODES, a, b, '662,1461,2614'],
                           capture_output=True, text=True, encoding='utf-8', errors='replace', env=env)
        io.open(os.path.join(out, 'nodes_%s.txt' % s), 'w', encoding='utf-8', newline='').write(n.stdout)
        pk = median_line(d.stdout, u'пик')[0]
        sm = median_line(d.stdout, u'сумма')[0]
        l1 = median_line(d.stdout, u'форма, L1')[0]
        p662, s662 = node_row(n.stdout, 662)
        p1461, s1461 = node_row(n.stdout, 1461)
        p2614, s2614 = node_row(n.stdout, 2614)
        lines.append(u'%-30s %8.3f %8.3f %7.2f %+9.3f %+9.3f %+9.3f %+9.3f %+9.3f %+9.3f' % (
            s, pk, sm, l1, p662, s662, p1461, s1461, p2614, s2614))
        rows.append((s, pk, sm, l1, p662, s662, p1461, s1461, p2614, s2614))
    text = u'\n'.join(lines) + u'\n'
    io.open(out + '.txt', 'w', encoding='utf-8', newline='').write(text)
    print(text)
    print(u'сцен сравнено: %d из %d в складе worktree' % (len(rows), len(scenes)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
