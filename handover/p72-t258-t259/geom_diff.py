# -*- coding: utf-8 -*-
"""П72 (T258): сверка каталога генератора с живыми сценами корпуса — побайтно, файл за файлом.

    python handover/p72-t258-t259/geom_diff.py <каталог генератора> [--live=<каталог корпуса/geometries>]

Печатает: у каждого живого `.in` — РАВЕН / РАЗОШЁЛСЯ (число разошедшихся строк и первые из них) /
НЕТ У ГЕНЕРАТОРА; лишние у генератора; `index.csv` — побайтно. Код 0 — всё равно, 1 — есть расхождения.
"""
import io, os, sys

def main():
    gen = sys.argv[1]
    live = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'tools', 'CORPUS', 'corpus', 'geometries')
    for a in sys.argv[2:]:
        if a.startswith('--live='):
            live = a[7:]
    live = os.path.abspath(live)
    names = sorted(f for f in os.listdir(live) if f.endswith('.in'))
    gen_names = sorted(f for f in os.listdir(gen) if f.endswith('.in'))
    equal, differ, missing = [], [], []
    for n in names:
        a = open(os.path.join(live, n), 'rb').read()
        p = os.path.join(gen, n)
        if not os.path.isfile(p):
            missing.append(n)
            print('НЕТ У ГЕНЕРАТОРА  %s' % n)
            continue
        b = open(p, 'rb').read()
        if a == b:
            equal.append(n)
            continue
        al, bl = a.split(b'\r\n'), b.split(b'\r\n')
        differ.append(n)
        print('РАЗОШЁЛСЯ        %s: живой %d строк / %d байт, генератор %d строк / %d байт' % (n, len(al), len(a), len(bl), len(b)))
        import difflib
        d = list(difflib.unified_diff([x.decode('cp1251', 'replace') for x in al], [x.decode('cp1251', 'replace') for x in bl],
                                      'live/' + n, 'gen/' + n, lineterm='', n=0))
        for line in d[:40]:
            print('      ' + line)
    extra = [n for n in gen_names if n not in names]
    for n in extra:
        print('ЛИШНИЙ У ГЕНЕРАТОРА %s' % n)
    ia = open(os.path.join(live, 'index.csv'), 'rb').read()
    ib = open(os.path.join(gen, 'index.csv'), 'rb').read() if os.path.isfile(os.path.join(gen, 'index.csv')) else None
    idx_ok = ia == ib
    if not idx_ok:
        print('index.csv РАЗОШЁЛСЯ: живой %d строк, генератор %s' % (ia.count(b'\n'), ib.count(b'\n') if ib is not None else 'нет'))
        if ib is not None:
            al = set(ia.decode('utf-8-sig').splitlines()); bl = set(ib.decode('utf-8-sig').splitlines())
            for x in sorted(al - bl)[:12]: print('      только живой:     ' + x[:120])
            for x in sorted(bl - al)[:12]: print('      только генератор: ' + x[:120])
    print()
    print('живых .in %d: РАВНЫ %d, РАЗОШЛИСЬ %d, НЕТ У ГЕНЕРАТОРА %d; лишних у генератора %d; index.csv %s'
          % (len(names), len(equal), len(differ), len(missing), len(extra), 'РАВЕН' if idx_ok else 'РАЗОШЁЛСЯ'))
    return 0 if (not differ and not missing and not extra and idx_ok) else 1

if __name__ == '__main__':
    for s in (sys.stdout, sys.stderr):
        try: s.reconfigure(encoding='utf-8', errors='replace')
        except Exception: pass
    sys.exit(main())
