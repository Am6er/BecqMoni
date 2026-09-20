# -*- coding: utf-8 -*-
"""Сводная таблица сайдкаров `.qk` (П83, AMBER42): сцена → Q2/Q4 на 60/662/1332/2614 кэВ
(интерполяция линейная по ln E, как в `AngularAttenuation.Q`), histories, наибольший dQ2
по узлам, клеймо физики матрицы. Печатает markdown; сцены с dQ2 max > порога помечает.

    python qk_table.py <каталог .qk> [ещё каталоги...] [--thr=0.0075] [--csv=<файл>]
"""
import io, os, sys, glob, math

def load(path):
    head, rows = {}, []
    with io.open(path, encoding='utf-8', newline='') as f:
        for line in f:
            line = line.rstrip('\r\n')
            if not line or line.startswith('#'):
                continue
            if '=' in line and ' ' not in line.split('=', 1)[0]:
                k, v = line.split('=', 1); head[k] = v; continue
            rows.append([float(x) for x in line.split()])
    return head, rows

def interp(rows, col, e):
    xs = [math.log(r[0]) for r in rows]
    x = math.log(e)
    if x <= xs[0]: return rows[0][col]
    if x >= xs[-1]: return rows[-1][col]
    for i in range(1, len(xs)):
        if x <= xs[i]:
            t = (x - xs[i-1]) / (xs[i] - xs[i-1])
            return rows[i-1][col] + t * (rows[i][col] - rows[i-1][col])
    return rows[-1][col]

def main():
    dirs = [a for a in sys.argv[1:] if not a.startswith('--')]
    thr = 0.0075
    csv = None
    for a in sys.argv[1:]:
        if a.startswith('--thr='): thr = float(a[6:])
        if a.startswith('--csv='): csv = a[6:]
    files = []
    for d in dirs:
        files += sorted(glob.glob(os.path.join(d, '*.qk')))
    E = [60.0, 661.7, 1332.5, 2614.5]
    out = []
    print('| сцена | hist | Q₂ 60 | Q₂ 662 | Q₂ 1332 | Q₂ 2614 | Q₄ 60 | Q₄ 662 | Q₄ 1332 | Q₄ 2614 | dQ₂ max | физика |')
    print('|---|---|---|---|---|---|---|---|---|---|---|---|')
    over = []
    for f in files:
        h, r = load(f)
        q2 = [interp(r, 1, e) for e in E]
        q4 = [interp(r, 2, e) for e in E]
        d2 = max(x[3] for x in r)
        phys = h.get('matrix', '').split(';')[0]
        name = os.path.basename(f)[:-3]
        flag = ' ⚠' if d2 > thr else ''
        print('| %s | %s | %s | %s | %s%s | %s |' % (name, h.get('histories'),
              ' | '.join('%.3f' % v for v in q2), ' | '.join('%.3f' % v for v in q4), '%.4f' % d2, flag, phys))
        out.append((name, h.get('histories'), q2, q4, d2, phys))
        if d2 > thr: over.append((name, d2))
    print()
    print('сцен %d; dQ₂ max > %.4f у %d: %s' % (len(files), thr, len(over),
          ', '.join('%s (%.4f)' % o for o in over) if over else 'нет'))
    if csv:
        with io.open(csv, 'w', encoding='utf-8', newline='') as w:
            w.write('scene,histories,Q2_60,Q2_662,Q2_1332,Q2_2614,Q4_60,Q4_662,Q4_1332,Q4_2614,dQ2max,phys\n')
            for name, hist, q2, q4, d2, phys in out:
                w.write('%s,%s,%s,%s,%.4f,%s\n' % (name, hist, ','.join('%.4f' % v for v in q2),
                                                  ','.join('%.4f' % v for v in q4), d2, phys))
    return 0 if not over else 1

if __name__ == '__main__':
    sys.exit(main())
