# -*- coding: utf-8 -*-
"""Сравнение двух каталогов сайдкаров `.qk` (П83, AMBER42).

    python cmp_qk.py <каталог A> <каталог B> [--brief]

Для каждой сцены, лежащей в обоих: совпадение шапки (geomsha, histories, seed),
совпадение клейма матрицы, побитовость тела (строки данных без учёта `built=`),
и, если тело разошлось, — наибольшее |ΔQ2|, |ΔQ4| в единицах напечатанного шума
dQ2/dQ4 файла A и наибольшее относительное расхождение eps_peak.
Код возврата 0 — все тела побитово; 1 — иначе.
"""
import io, os, sys, glob

def load(path):
    head, rows = {}, []
    with io.open(path, encoding='utf-8', newline='') as f:
        for line in f:
            line = line.rstrip('\r\n')
            if not line or line.startswith('#'):
                continue
            if '=' in line and ' ' not in line.split('=', 1)[0]:
                k, v = line.split('=', 1)
                head[k] = v
                continue
            rows.append(line)
    return head, rows

def main():
    a, b = sys.argv[1], sys.argv[2]
    brief = '--brief' in sys.argv
    names = sorted(set(os.path.basename(p) for p in glob.glob(os.path.join(a, '*.qk')))
                   & set(os.path.basename(p) for p in glob.glob(os.path.join(b, '*.qk'))))
    if not names:
        print('общих сцен нет'); return 2
    bad = 0
    print('%-32s %-6s %-6s %-6s %-8s %8s %8s %9s' % ('сцена', 'geom', 'hist', 'matrix', 'тело', 'dQ2/σ', 'dQ4/σ', 'dε_p max'))
    for n in names:
        ha, ra = load(os.path.join(a, n))
        hb, rb = load(os.path.join(b, n))
        geom = ha.get('geomsha') == hb.get('geomsha')
        hist = ha.get('histories') == hb.get('histories') and ha.get('seed') == hb.get('seed')
        mat = ha.get('matrix') == hb.get('matrix')
        same = ra == rb
        z2 = z4 = de = 0.0
        if not same:
            for la, lb in zip(ra, rb):
                fa = [float(x) for x in la.split()]
                fb = [float(x) for x in lb.split()]
                if fa[3] > 0: z2 = max(z2, abs(fa[1] - fb[1]) / fa[3])
                if fa[4] > 0: z4 = max(z4, abs(fa[2] - fb[2]) / fa[4])
                if fa[5] > 0: de = max(de, abs(fa[5] - fb[5]) / fa[5])
            bad += 1
        print('%-32s %-6s %-6s %-6s %-8s %8.2f %8.2f %8.3f%%' % (
            n[:-3], 'да' if geom else 'НЕТ', 'да' if hist else 'НЕТ', 'да' if mat else 'НЕТ',
            'ПОБИТОВО' if same else 'разошлось', z2, z4, de * 100))
        if not same and not brief:
            print('   A matrix=%s' % ha.get('matrix'))
            print('   B matrix=%s' % hb.get('matrix'))
    print('ИТОГО сцен %d, тел разошлось %d — %s' % (len(names), bad, 'ПОБИТОВО' if bad == 0 else 'РАСХОЖДЕНИЕ'))
    return 0 if bad == 0 else 1

if __name__ == '__main__':
    sys.exit(main())
