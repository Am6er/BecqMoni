# -*- coding: utf-8 -*-
"""П69 (S174, серый слой ниже порога доверия; невязка от Min_Range; из П68/П65/П63): сравнение двух csv `--rates=` (FsaStackShot).

    python handover/p69-s174/compare.py <a.csv> <b.csv> [--tol=1e-9]

Фит обязан быть ПОБИТОВО тем же: все столбцы, кроме `share_pct`, сравниваются дословно, строка к
строке (`meta` — χ²/ndf, невязка ε, дрейф; `component` — скорость счёта, z, пороги, пиковые отсчёты;
`limit` — пределы; `untied_tail` — хвосты S173, они у обоих плеч и они часть фита). `share_pct` —
ДОЛЯ СЛОЯ, и у спектра с матрицей она обязана сдвинуться (решение Amber 14.09.2026 — сплайн ниже
порога уходит в серый слой): печатается таблицей «компонент: А → Б», а расхождением считается только
если задан `--tol=` и |А − Б| > tol (плечо «подсадка» против HEAD). Строки раздела `grey` (есть только
у Б) печатаются и в сверку не входят.
Код 0 — расхождений вне доли нет; 1 — есть (печатаются первые двадцать).
"""
import csv
import io
import sys


def load(path):
    with io.open(path, encoding='utf-8', newline='') as fh:
        return [row for row in csv.reader(fh)]


def main():
    a, b = load(sys.argv[1]), load(sys.argv[2])
    tol = None
    for arg in sys.argv[3:]:
        if arg.startswith('--tol='):
            tol = float(arg[6:])
    head = a[0]
    if b[0] != head:
        print('  шапки различаются: %s | %s' % (a[0], b[0]))
        return 1
    share = head.index('share_pct')
    name = head.index('name')
    section = head.index('section')
    rate = head.index('count_rate')
    counts = head.index('peak_counts')
    greys = [r for r in b[1:] if r[section] == 'grey']
    b_core = [r for r in b[1:] if r[section] != 'grey']
    a_core = [r for r in a[1:] if r[section] != 'grey']
    n = max(len(a_core), len(b_core))
    diff = 0
    shares = []
    for i in range(n):
        ra = a_core[i] if i < len(a_core) else None
        rb = b_core[i] if i < len(b_core) else None
        if ra is None or rb is None:
            diff += 1
            print('  строк не поровну: %d' % (i + 2))
            continue
        xa = [v for j, v in enumerate(ra) if j != share]
        xb = [v for j, v in enumerate(rb) if j != share]
        if xa != xb:
            diff += 1
            if diff <= 20:
                print('  строка %d: %s | %s' % (i + 2, ra[:6], rb[:6]))
        if ra[section] == 'component' and (ra[share] != rb[share]):
            try:
                da, db = float(ra[share]), float(rb[share])
            except ValueError:
                da = db = float('nan')
            shares.append((ra[name], ra[share], rb[share], db - da))
            if tol is not None and abs(db - da) > tol:
                diff += 1
                print('  доля %s: %s | %s (|Δ| > %g)' % (ra[name], ra[share], rb[share], tol))
    for row in greys:
        if row[name] in ('below_floor', 'above_lines'):
            print('  серый Б: %-12s %10s отсч. %8s %%' % (row[name], row[counts], row[share]))
        elif row[name] in ('spread_floor', 'residual_floor'):
            print('  пол Б:   %-15s %6s кэВ канал %s' % (row[name], row[rate], row[counts]))
        else:
            print('  невязка Б: %-16s %s' % (row[name], row[rate]))
    for s in shares:
        print('  доля %-12s %10.4f -> %10.4f  (%+.4f)' % (s[0], float(s[1]), float(s[2]), s[3]))
    print('%s vs %s: строк %d, расхождений вне доли %d, долей сдвинулось %d' % (
        sys.argv[1], sys.argv[2], n, diff, len(shares)))
    return 0 if diff == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
