# -*- coding: utf-8 -*-
"""Сравнение двух каталогов прогона по ЧЕТЫРЁМ таблицам (runs/components/limits/anchors)
без граф ms/cpu_ms — по ключу строки, столбец за столбцом (П83, AMBER42).

    python cmp4.py <A> <B> [--tables=runs,components,limits,anchors] [--max=N]

Печатает по таблице: строк, разошлось; по столбцам — сколько строк разошлось в каждом;
по спектрам — список (спектр: столбцы). Код 0 — побитово, 1 — нет.
"""
import io, os, sys, glob, csv
from collections import OrderedDict, Counter, defaultdict

TIME = {'ms', 'cpu_ms'}

def load(p):
    with io.open(p, encoding='utf-8-sig', newline='') as f:
        rows = list(csv.reader(f))
    if not rows:
        return [], []
    return rows[0], rows[1:]

def keyof(head, row, table):
    # ключ строки: spectrum + component (если есть) + line/anchor
    d = dict(zip(head, row))
    parts = [d.get('spectrum', '')]
    for k in ('component', 'line', 'energy', 'anchor', 'E', 'kev'):
        if k in d:
            parts.append(d[k])
    return '|'.join(parts)

def main():
    a, b = sys.argv[1], sys.argv[2]
    tables = ['runs', 'components', 'limits', 'anchors']
    mx = 60
    for arg in sys.argv[3:]:
        if arg.startswith('--tables='): tables = arg[9:].split(',')
        if arg.startswith('--max='): mx = int(arg[6:])
    bad_total = 0
    for t in tables:
        n_rows = 0; n_bad = 0
        col_bad = Counter(); spec_bad = defaultdict(set); missing = []
        for fa in sorted(glob.glob(os.path.join(a, '*_spline_%s.csv' % t))):
            name = os.path.basename(fa)
            fb = os.path.join(b, name)
            if not os.path.exists(fb):
                missing.append(name); continue
            ha, ra = load(fa); hb, rb = load(fb)
            if ha != hb:
                print('  %s: ШАПКИ РАЗНЫЕ: %s | %s' % (name, [c for c in ha if c not in hb], [c for c in hb if c not in ha]))
            keep = [c for c in ha if c not in TIME and c in hb]
            ia = {c: ha.index(c) for c in keep}; ib = {c: hb.index(c) for c in keep}
            da = OrderedDict(); db = OrderedDict()
            for r in ra:
                k = keyof(ha, r, t)
                while k in da: k += '+'
                da[k] = r
            for r in rb:
                k = keyof(hb, r, t)
                while k in db: k += '+'
                db[k] = r
            for k in set(da) | set(db):
                n_rows += 1
                if k not in da or k not in db:
                    n_bad += 1; spec_bad[k.split('|')[0]].add('<строка только в %s>' % ('A' if k in da else 'B')); continue
                x, y = da[k], db[k]
                diff = [c for c in keep if (x[ia[c]] if ia[c] < len(x) else '') != (y[ib[c]] if ib[c] < len(y) else '')]
                if diff:
                    n_bad += 1
                    for c in diff: col_bad[c] += 1
                    spec_bad[k.split('|')[0]].update(diff)
        bad_total += n_bad + len(missing)
        print('%-11s строк %5d  разошлось %4d%s' % (t, n_rows, n_bad, ('  НЕТ во втором: %s' % missing) if missing else ''))
        if col_bad:
            print('   по столбцам: ' + ', '.join('%s×%d' % kv for kv in col_bad.most_common()))
        if spec_bad:
            items = sorted(spec_bad.items())
            print('   по спектрам (%d): ' % len(items))
            for s, cols in items[:mx]:
                print('      %-28s %s' % (s, ', '.join(sorted(cols))))
            if len(items) > mx:
                print('      … ещё %d' % (len(items) - mx))
    print('ИТОГО разошлось %d — %s' % (bad_total, 'ПОБИТОВО' if bad_total == 0 else 'РАСХОЖДЕНИЕ'))
    return 0 if bad_total == 0 else 1

if __name__ == '__main__':
    sys.exit(main())
