# -*- coding: utf-8 -*-
u"""Поячеечная сверка двух корпусных прогонов (`*_components.csv`, `*_runs.csv`).

Строки сличаются по КЛЮЧУ (спектр + компонент + вид), а не по номеру: сдвиг
номеров от одного изменившегося спектра иначе выдаёт за расхождение весь файл.
Графы времени (`ms`, `cpu_ms`) игнорируются — они шумят от прогона к прогону и
к содержанию не относятся; их разницу считаем и печатаем отдельной строкой,
чтобы «игнорируются» не превратилось в «не измерены».

    python handover/p29-recal/cmp_runs.py <каталог A> <каталог B> [ячеек на спектр]
"""
import csv, io, os, sys, collections

TIME = ('ms', 'cpu_ms')


def load(d):
    rows = {}
    for fn in sorted(os.listdir(d)):
        if fn.endswith('_components.csv'):
            kind = 'comp'
        elif fn.endswith('_runs.csv'):
            kind = 'runs'
        else:
            continue
        with io.open(os.path.join(d, fn), encoding='utf-8-sig', newline='') as f:
            for r in csv.DictReader(f):
                key = (r['spectrum'], kind, r.get('component', ''), r.get('kind', ''))
                rows.setdefault(key, []).append(r)
    return rows


def main():
    a, b = load(sys.argv[1]), load(sys.argv[2])
    lim = int(sys.argv[3]) if len(sys.argv) > 3 else 20
    keys = sorted(set(a) | set(b))
    specs = sorted({k[0] for k in keys})
    diff = collections.OrderedDict()
    timeonly = 0
    for k in keys:
        ra, rb = a.get(k, []), b.get(k, [])
        cells = []
        if len(ra) != len(rb):
            cells.append((u'ЧИСЛО СТРОК %s' % (k[2] or k[1]), len(ra), len(rb)))
        else:
            for da, db in zip(ra, rb):
                for col in sorted(set(da) | set(db)):
                    if da.get(col) == db.get(col):
                        continue
                    if col in TIME:
                        timeonly += 1
                        continue
                    cells.append((u'%s.%s %s' % (k[1], col, k[2]),
                                  da.get(col), db.get(col)))
        if cells:
            diff.setdefault(k[0], []).extend(cells)
    print(u'спектров: %d;  СОВПАЛИ ПОЯЧЕЕЧНО: %d;  РАЗОШЛИСЬ: %d'
          % (len(specs), len(specs) - len(diff), len(diff)))
    print(u'ячеек времени (ms/cpu_ms), сознательно не в счёт: %d' % timeonly)
    for spec, cells in diff.items():
        print(u'\n=== %s : ячеек %d' % (spec, len(cells)))
        for c in cells[:lim]:
            print(u'    %-46s %s  ->  %s' % c)
        if len(cells) > lim:
            print(u'    … ещё %d' % (len(cells) - lim))


main()
