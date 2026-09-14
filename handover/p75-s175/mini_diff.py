# -*- coding: utf-8 -*-
"""П75 (S175; копия из П69): побитовое сравнение двух прогонов малой базы (все csv каталога), вне столбцов времени.

    python mini_diff.py <out_a> <out_b>

Сравниваются одноимённые *.csv строка к строке; столбцы `ms`/`cpu_ms` (время) из
сравнения исключаются и считаются отдельно. Код 0 — расхождений вне времени нет.
"""
import csv, io, os, sys

TIME = ('ms', 'cpu_ms', 'elapsed_ms', 'time_ms')

def rows(path):
    with io.open(path, encoding='utf-8', newline='') as fh:
        return [r for r in csv.reader(fh)]

def main():
    a, b = sys.argv[1], sys.argv[2]
    names = sorted(n for n in os.listdir(a) if n.lower().endswith('.csv'))
    files = lines = diff = tdiff = 0
    for n in names:
        pb = os.path.join(b, n)
        if not os.path.exists(pb):
            print('  нет в Б:', n); diff += 1; continue
        ra, rb = rows(os.path.join(a, n)), rows(pb)
        files += 1
        if not ra or not rb:
            if ra != rb: diff += 1; print('  пустой/непустой:', n)
            continue
        head = ra[0]
        tcols = [i for i, h in enumerate(head) if h in TIME]
        if ra[0] != rb[0]:
            diff += 1; print('  шапка:', n); continue
        for i in range(max(len(ra), len(rb))):
            lines += 1
            xa = ra[i] if i < len(ra) else None
            xb = rb[i] if i < len(rb) else None
            if xa is None or xb is None:
                diff += 1; print('  строк не поровну:', n, i + 1); continue
            ya = [v for j, v in enumerate(xa) if j not in tcols]
            yb = [v for j, v in enumerate(xb) if j not in tcols]
            if ya != yb:
                diff += 1
                if diff <= 15:
                    print('  %s строка %d: %s | %s' % (n, i + 1, xa[:4], xb[:4]))
            elif xa != xb:
                tdiff += 1
    print('csv %d, строк %d, расхождений вне времени %d, только во времени %d' % (files, lines, diff, tdiff))
    return 0 if diff == 0 else 1

if __name__ == '__main__':
    sys.exit(main())
