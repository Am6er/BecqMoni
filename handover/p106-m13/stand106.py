# -*- coding: utf-8 -*-
# П106 (M13): стенд П92/П94 при ключе lbrem ВКЛ — наша сторона / арбитр def − 1, % по полосам (те же полосы, что cmp100.py):
# столбцы «П103» (= П100 elmix1, умолчания физики 20), «ВКЛ lbrem1» (П106), «± σ». Для 59.5 кэВ — полосы П55 (32–42, 43–54, 55–59).
#   python stand106.py <сцена> <E> [--md]
import sys, math
sys.path.insert(0, r'D:\BqMoni_Claude\p106')
import arms106 as a
sys.stdout.reconfigure(encoding='utf-8')


def measures(h, p, e):
    if e < 100.0:
        m = {}
        m['пик'] = h.get(p, 0.0)
        m['32–42'] = a.band(h, 32, 43)
        m['43–54'] = a.band(h, 43, 55)
        m['55–59'] = a.band(h, 55, 60)
        m['0–31'] = a.band(h, 0, 32)
        return m
    m = a.measures(h, p, e)
    for k in ('континуум',):
        m.pop(k, None)
    return m


def cell(v, base, nv, nb):
    if base <= 0:
        return '—'
    s = 100.0 * math.sqrt((1.0 / (v * nv) if v * nv > 0 else 0.0) + (1.0 / (base * nb) if base * nb > 0 else 0.0))
    return '%+.2f ± %.1f' % (100.0 * (v / base - 1.0), s)


def main():
    scene, e = sys.argv[1], sys.argv[2]
    md = '--md' in sys.argv
    data, esc = a.load(scene, e)
    if 'G4 def' not in data:
        print('нет G4 def для %s %s' % (scene, e)); return
    p = max(max(h) for h, n in data.values())
    ms = {k: measures(h, p, float(e)) for k, (h, n) in data.items()}
    ns = {k: n for k, (h, n) in data.items()}
    cols = [('П103 (elmix1)/def', 'наша elmix1100'), ('ВКЛ lbrem1/def', 'наша lbrem1')]
    cols = [c for c in cols if c[1] in ms]
    print('=== %s, E = %s кэВ (арбитр def n=%s; наши n=%s)' % (scene, e, ns['G4 def'], ', '.join('%s %s' % (c[1], ns[c[1]]) for c in cols)))
    if md:
        print('| полоса | ' + ' | '.join(c[0] for c in cols) + ' |')
        print('|---|' + '---|' * len(cols))
    for k in ms['G4 def']:
        row = [cell(ms[c[1]][k], ms['G4 def'][k], ns[c[1]], ns['G4 def']) for c in cols]
        if md:
            print('| %s | ' % k + ' | '.join(row) + ' |')
        else:
            print('%-16s' % k + ''.join('%22s' % r for r in row))


if __name__ == '__main__':
    main()
