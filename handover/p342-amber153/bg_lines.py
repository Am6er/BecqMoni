# -*- coding: utf-8 -*-
u"""П342: доля вычитаемого фона в окне ±1.25 ПШПВ главных линий нуклида (сосуды G1S с паспортом). Пишет bg_lines.txt."""
import io, math, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import corpus_xml as cx  # noqa: E402
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')
S = os.path.join(HERE, '..', '..', 'tools', 'CORPUS', 'corpus', 'spectra')
LINES = {'Cs137': [661.66], 'K40': [1460.8], 'Ra226': [351.9, 609.3], 'Th232': [583.2, 911.2, 2614.5]}
for k in sorted(os.listdir(S)):
    key = k[:-4]
    if not key.startswith('G1S') or not any(s in key for s in ('_Mar', 'Denta', 'Petri')) or 'Coal' in key or 'Mix' in key:
        continue
    nuc = [n for n in LINES if n in key]
    if not nuc:
        continue
    d = cx.read(os.path.join(S, k))
    kk = d['fg']['live'] / d['bg']['live']
    out = []
    for e in LINES[nuc[0]]:
        w = 1.25 * 0.068 * 662 * math.sqrt(e / 662)
        n = cx.band(d['fg'], e - w, e + w)
        b = cx.band(d['bg'], e - w, e + w) * kk
        out.append('%.0f кэВ: %.3f' % (e, b / n))
    print('%-24s %s' % (key, '  '.join(out)))
