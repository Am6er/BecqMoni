# -*- coding: utf-8 -*-
# П20 12.09.2026, `A72`/`F15`: наш сырой отклик (G4RawProbe --out=, keV,response — доля на
# историю, ПОСЛЕДНИЙ бин = пик) против Geant4 (g4cf hist: HIST <бин> <счёт>, decays=N).
# Правило бина у обоих одно: bin = (int)(edep/шаг + 0.5), последний бин — пик.
#   python cmp_a72.py <ours.csv> <g4.log> [подпись]
# Печатает: пик (последний бин), пик окном ±3 бина (как 02.09.2026, §9в), полная,
# полосы в долях E (границы по бину пика), вылеты 511/1022 (для E > 1022).
import io, re, sys
sys.stdout.reconfigure(encoding='utf-8')


def read_ours(path):
    h = {}
    for line in io.open(path, encoding='utf-8-sig'):
        parts = line.strip().split(',')
        if len(parts) != 2 or parts[0] == 'keV':
            continue
        h[int(round(float(parts[0])))] = float(parts[1])
    return h


def read_g4(path):
    decays = None
    h = {}
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
        if m:
            decays = int(m.group(3))
        m = re.match(r'HIST (\d+) (\d+)', line)
        if m:
            h[int(m.group(1))] = int(m.group(2))
    if not decays:
        raise SystemExit('в логе Geant4 нет HISTBEGIN: ' + path)
    return {k: v / decays for k, v in h.items()}, {k: v for k, v in h.items()}, decays


def band(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k < hi)


def ratio(a, b):
    return '%.4f (%+.2f %%)' % (a / b, 100.0 * (a / b - 1.0)) if b else '—'


ours = read_ours(sys.argv[1])
g4, g4counts, decays = read_g4(sys.argv[2])
label = sys.argv[3] if len(sys.argv) > 3 else ''
n = max(ours) + 1
p = n - 1
print('=== %s: бинов %d, пик — бин %d; Geant4 историй %d' % (label, n, p, decays))
po, pg = ours.get(p, 0.0), g4.get(p, 0.0)
cg = g4counts.get(p, 0)
print('  пик (бин %d):      наша %.4E  G4 %.4E (%d отсч., шум %.2f %%)  наша/G4 %s'
      % (p, po, pg, cg, 100.0 / cg ** 0.5 if cg else 0.0, ratio(po, pg)))
wo = sum(ours.get(k, 0.0) for k in range(p - 3, p + 1))
wg = sum(g4.get(k, 0.0) for k in range(p - 3, p + 1))
cw = sum(g4counts.get(k, 0) for k in range(p - 3, p + 1))
print('  пик окном ±3:      наша %.4E  G4 %.4E (%d отсч.)  наша/G4 %s' % (wo, wg, cw, ratio(wo, wg)))
to, tg = sum(ours.values()), sum(g4.values())
print('  полная:            наша %.4E  G4 %.4E  наша/G4 %s' % (to, tg, ratio(to, tg)))
co, cgc = to - wo, tg - wg
print('  континуум (без окна ±3): наша %.4E  G4 %.4E  наша/G4 %s' % (co, cgc, ratio(co, cgc)))
for lo, hi in ((0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.0)):
    lo_i, hi_i = int(lo * p), min(int(hi * p), p - 3)
    a, b = band(ours, lo_i, hi_i), band(g4, lo_i, hi_i)
    print('  [%3.0f..%3.0f %%E) бины %4d..%4d: наша %.4E  G4 %.4E  наша/G4 %s'
          % (100 * lo, 100 * hi, lo_i, hi_i - 1, a, b, ratio(a, b)))
e = p
if e > 1022:
    for name, esc in (('вылет 511', 511), ('вылет 1022', 1022)):
        c = e - esc
        a = sum(ours.get(k, 0.0) for k in (c - 1, c, c + 1))
        b = sum(g4.get(k, 0.0) for k in (c - 1, c, c + 1))
        print('  %s (бин %d±1): наша %.4E  G4 %.4E  наша/G4 %s' % (name, c, a, b, ratio(a, b)))
