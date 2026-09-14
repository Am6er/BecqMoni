# -*- coding: utf-8 -*-
# П55 (A72 оценка, 14.09.2026): сводка сырых откликов — Geant4 (g4cf hist: HIST <бин> <счёт>) в
# вариантах def / nofluct / nodelta / lowcut / killesc и наша сторона (G4RawProbe --out=, keV,response).
# Правило бина у обоих одно: bin = (int)(edep/шаг + 0.5), последний бин — пик (П20 cmp_a72.py).
#   python cmp55.py <каталог g4out> <каталог ours> <сцена> <E>
# Печатает по каждому варианту: пик, окно ±3, полная, континуум, полосы в долях E, полоса
# «под пиком» (E−300…E−4), вылеты 511/1022 (E>1022), полосы низа (для 59.5: 32–42, 43–54, 55–59)
# — и относительно варианта def: Δ % и шум (биномиальный, √N, независимо; для коррелированных
# прогонов с одним зерном это ВЕРХНЯЯ граница шума разности).
import io, os, re, sys, math
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
        return None, None, None
    return {k: v / decays for k, v in h.items()}, h, decays


def band(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k < hi)


def measures(h, p):
    m = {}
    m['пик'] = h.get(p, 0.0)
    m['пик±3'] = sum(h.get(k, 0.0) for k in range(p - 3, p + 1))
    m['полная'] = sum(h.values())
    m['континуум'] = m['полная'] - m['пик±3']
    for lo, hi in ((0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.0)):
        lo_i, hi_i = int(lo * p), min(int(hi * p), p - 3)
        m['[%2.0f..%3.0f%%E)' % (100 * lo, 100 * hi)] = band(h, lo_i, hi_i)
    m['E−300…E−4'] = band(h, max(0, p - 300), p - 3)
    m['E−100…E−4'] = band(h, max(0, p - 100), p - 3)
    if p > 1022:
        for name, esc in (('вылет511', 511), ('вылет1022', 1022)):
            c = p - esc
            m[name] = sum(h.get(k, 0.0) for k in (c - 1, c, c + 1))
    if p < 100:
        m['32–42'] = band(h, 32, 43)
        m['43–54'] = band(h, 43, 55)
        m['55–59'] = band(h, 55, 60)
        m['0.5–3 кэВ уноса'] = sum(h.get(k, 0.0) for k in range(p - 3, p))
    return m


def main():
    g4dir, oursdir, scene, e = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
    variants = ['def', 'nofluct', 'nodelta', 'lowcut', 'killesc']
    data = {}
    counts = {}
    decays = {}
    for v in variants:
        path = os.path.join(g4dir, 'g4_%s_%s_%s.log' % (scene, e, v))
        if os.path.exists(path):
            h, c, d = read_g4(path)
            if h is not None:
                data['G4 ' + v] = h
                counts['G4 ' + v] = c
                decays['G4 ' + v] = d
    for tag in ('ref', 'refposend0', 'refposend1', 'varposend0', 'varposend1', 'varposend1'):
        path = os.path.join(oursdir, 'ours_%s_%s_%s.csv' % (scene, e, tag))
        if os.path.exists(path) and ('наша ' + tag) not in data:
            data['наша ' + tag] = read_ours(path)
    if not data:
        print('нет данных для %s %s' % (scene, e))
        return
    p = max(max(h) for h in data.values())
    print('=== %s, E = %s кэВ, бин пика %d ===' % (scene, e, p))
    ms = {k: measures(h, p) for k, h in data.items()}
    base = ms.get('G4 def')
    keys = list(next(iter(ms.values())).keys())
    cols = list(ms.keys())
    print('%-18s' % 'мера' + ''.join('%22s' % c for c in cols))
    for k in keys:
        line = '%-18s' % k
        for c in cols:
            v = ms[c][k]
            if base is not None and c != 'G4 def' and base[k] > 0:
                # шум: √N по G4 def (нижняя оценка N — по доле и числу историй базы)
                nb = base[k] * decays['G4 def']
                nv = v * decays.get(c, decays['G4 def']) if c.startswith('G4') else None
                noise = 100.0 * math.sqrt((1.0 / nb if nb > 0 else 0) + (1.0 / nv if nv else 0))
                line += '%11.3E %+6.2f%%±%.1f' % (v, 100.0 * (v / base[k] - 1.0), noise)
            else:
                line += '%22.3E' % v
        print(line)
    if 'G4 def' in counts:
        print('  G4 def: историй %d, отсчётов в пике %d (шум %.2f %%)' % (
            decays['G4 def'], counts['G4 def'].get(p, 0), 100.0 / math.sqrt(max(1, counts['G4 def'].get(p, 0)))))


if __name__ == '__main__':
    main()
