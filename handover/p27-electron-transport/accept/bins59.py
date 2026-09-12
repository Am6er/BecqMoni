# -*- coding: utf-8 -*-
# П27: побинная раскладка 59.541 кэВ (бины 0..60) — G4, «до» (etr=0), «после» (etr=1), Δ %.
#   python bins59.py <сцена> [<суффикс плеча "после", умолчание etr1>]
import io, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
scene = sys.argv[1] if len(sys.argv) > 1 else 'RC103_bare_gap5'
arm = sys.argv[2] if len(sys.argv) > 2 else 'etr1'


def read_ours(path):
    h = {}
    for line in io.open(path, encoding='utf-8-sig'):
        p = line.strip().split(',')
        if len(p) != 2 or p[0] == 'keV':
            continue
        h[int(round(float(p[0])))] = float(p[1])
    return h


def read_g4(path):
    d = None
    h = {}
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
        if m:
            d = int(m.group(3))
        m = re.match(r'HIST (\d+) (\d+)', line)
        if m:
            h[int(m.group(1))] = int(m.group(2))
    return {k: v / d for k, v in h.items()}


o0 = read_ours(os.path.join(HERE, 'ours_%s_59.541_etr0.csv' % scene))
o1 = read_ours(os.path.join(HERE, 'ours_%s_59.541_%s.csv' % (scene, arm)))
g = read_g4(os.path.join(HERE, '..', 'g4', 'g4_%s_59.541.log' % scene))
print('%s: бин   G4         до(etr0)   Δ%%      после(%s) Δ%%' % (scene, arm))
for b in range(0, 61):
    G, A, B = g.get(b, 0.0), o0.get(b, 0.0), o1.get(b, 0.0)
    print('%3d  %.3E  %.3E %+7.1f   %.3E %+7.1f' % (b, G, A, 100 * (A / G - 1) if G else 0, B, 100 * (B / G - 1) if G else 0))
