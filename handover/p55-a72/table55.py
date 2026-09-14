# -*- coding: utf-8 -*-
# П55: markdown-таблицы §3.2 журнала из логов арбитра и CSV нашей стороны (тот же разбор, что cmp55.py).
import io, os, re, sys, math
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cmp55 import read_ours, read_g4, measures

g4dir, oursdir = sys.argv[1], sys.argv[2]
sets = [('RC103_point0', '1460.82'), ('ASN16_lu_side', '1460.82'), ('AS80_point0', '1460.82'),
        ('AS80_th_disk', '2614.511'), ('RC103_point0', '2614.511'),
        ('RC103_point0', '661.657'), ('ASN16_lu_side', '661.657'), ('AS80_point0', '661.657'),
        ('RC103_point0', '59.541'), ('ASN16_lu_side', '59.541')]
variants = ['nofluct', 'nodelta', 'lowcut', 'killesc']


def fmt(v, b, nb, nv):
    if b <= 0:
        return '—'
    noise = 100.0 * math.sqrt((1.0 / nb if nb > 0 else 0) + (1.0 / nv if nv > 0 else 0))
    return '%+.2f ± %.1f' % (100.0 * (v / b - 1.0), noise)


for scene, e in sets:
    path = os.path.join(g4dir, 'g4_%s_%s_def.log' % (scene, e))
    if not os.path.exists(path):
        continue
    base, basec, based = read_g4(path)
    if base is None:
        continue
    p = max(base)
    mb = measures(base, p)
    if p < 100:
        cols = ['пик', '32–42', '43–54', '55–59', '0.5–3 кэВ уноса', 'континуум']
    elif p > 1022:
        cols = ['пик', '[ 0.. 25%E)', '[25.. 50%E)', '[50.. 75%E)', '[75..100%E)', 'E−300…E−4', 'вылет511', 'вылет1022']
    else:
        cols = ['пик', '[ 0.. 25%E)', '[25.. 50%E)', '[50.. 75%E)', '[75..100%E)', 'E−300…E−4', 'E−100…E−4']
    print('\n**%s, %s кэВ** — арбитр умолчанием: историй %d, пик %.3E (%d отсч., шум %.2f %%)\n' % (
        scene, e, based, mb['пик'], basec.get(p, 0), 100.0 / math.sqrt(max(1, basec.get(p, 0)))))
    print('| вариант | ' + ' | '.join('Δ ' + c.replace('[ ', '[').replace('..', '…').replace('%E)', ' %E') + ', %' for c in cols) + ' |')
    print('|---' * (len(cols) + 1) + '|')
    for v in variants:
        vp = os.path.join(g4dir, 'g4_%s_%s_%s.log' % (scene, e, v))
        if not os.path.exists(vp):
            continue
        h, hc, hd = read_g4(vp)
        if h is None:
            continue
        m = measures(h, p)
        cells = [fmt(m[c], mb[c], mb[c] * based, m[c] * hd) for c in cols]
        print('| G4 `%s` | %s |' % (v, ' | '.join(cells)))
    for tag, label in (('ref', 'наша HEAD (физика 18)'), ('varposend1', 'наша `--posend=1`'), ('var40posend0', 'наша 40 млн `--posend=0`'), ('var40posend1', 'наша 40 млн `--posend=1`')):
        op = os.path.join(oursdir, 'ours_%s_%s_%s.csv' % (scene, e, tag))
        if not os.path.exists(op):
            continue
        h = read_ours(op)
        m = measures(h, p)
        # шум нашей стороны — не биномиальный (взвешенная ветка); ставим только биномиальный по G4 def
        cells = ['%+.2f' % (100.0 * (m[c] / mb[c] - 1.0)) if mb[c] > 0 else '—' for c in cols]
        print('| %s / G4 def | %s |' % (label, ' | '.join(cells)))
