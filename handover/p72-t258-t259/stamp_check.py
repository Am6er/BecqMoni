# -*- coding: utf-8 -*-
"""П72 (T258): приёмка генератора КЛЕЙМОМ. Для каждой живой сцены корпуса — три клейма через
`MatrixStampProbe`: живого `.in` (при умолчаниях), построенного генератором `.in` и матрицы `.rmx`
живого склада. Сцена принята, когда все три равны.

    python handover/p72-t258-t259/stamp_check.py <каталог генератора> <каталог сборки проб> [--live=<geometries>] [--out=<csv>]

Код 0 — все сцены сошлись; 1 — есть расхождения (перечислены).
"""
import io, os, re, subprocess, sys

def stamps(exe, geometry, matrix):
    p = subprocess.run([exe, '--geometry=' + geometry, '--matrix=' + matrix], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    text = p.stdout.decode('utf-8', 'replace')
    m1 = re.search(r'клеймо при умолчаниях\s*:\s*(phys=\S+)', text)
    m2 = re.search(r'клеймо в файле\s*:\s*(phys=\S+)', text)
    return (m1.group(1) if m1 else None), (m2.group(1) if m2 else None), text

def main():
    gen = sys.argv[1]; build = sys.argv[2]
    here = os.path.dirname(os.path.abspath(__file__))
    live = os.path.abspath(os.path.join(here, '..', '..', 'tools', 'CORPUS', 'corpus', 'geometries'))
    out = None
    for a in sys.argv[3:]:
        if a.startswith('--live='): live = a[7:]
        if a.startswith('--out='): out = a[6:]
    exe = os.path.join(build, 'MatrixStampProbe.exe')
    rows = []
    bad = 0
    for n in sorted(f for f in os.listdir(live) if f.endswith('.in')):
        key = n[:-3]
        rmx = os.path.join(live, key + '.rmx')
        s_live, s_rmx, _ = stamps(exe, os.path.join(live, n), rmx)
        g = os.path.join(gen, n)
        if os.path.isfile(g):
            s_gen, _, _ = stamps(exe, g, rmx)
        else:
            s_gen = None
        ok = s_live is not None and s_live == s_gen and (s_rmx is None or s_rmx == s_live)
        verdict = 'СОШЛОСЬ' if ok else ('НЕТ У ГЕНЕРАТОРА' if s_gen is None else 'РАЗОШЛОСЬ')
        if not ok: bad += 1
        rows.append((key, s_live, s_gen, s_rmx, verdict))
        print('%-30s живой %s  генератор %s  матрица %s  %s' % (key, (s_live or '?')[:24], (s_gen or '?')[:24], (s_rmx or 'нет .rmx')[:24], verdict))
    print()
    print('сцен %d: сошлось %d, не сошлось %d' % (len(rows), len(rows) - bad, bad))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write('scene,stamp_live_in,stamp_generated_in,stamp_rmx,verdict\n')
            for r in rows:
                fh.write(','.join(x or '' for x in r) + '\n')
    return 0 if bad == 0 else 1

if __name__ == '__main__':
    for s in (sys.stdout, sys.stderr):
        try: s.reconfigure(encoding='utf-8', errors='replace')
        except Exception: pass
    sys.exit(main())
