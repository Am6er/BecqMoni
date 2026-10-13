# -*- coding: utf-8 -*-
# П111 (M13, 19.09.2026): плечо ТОРМОЗНОГО ЭЛЕКТРОНОВ ОБВЯЗКИ — арбитр (def − killoutbrem) против нас (def − outbrem0),
# доли на историю по полосам, с шумом. Тот же разбор файлов и те же полосы, что у arms106.py (П106).
#   python arms111.py <сцена> <E> <g4_def_tag> <g4_kill_tag> <наш_def_tag> <наш_outbrem0_tag> [ещё пары наш_def наш_outbrem0 ...]
import io, os, re, sys, math
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'D:\BqMoni_Claude\p111'


def read_ours(path):
    h, n = {}, None
    for line in io.open(path, encoding='utf-8-sig'):
        parts = line.strip().split(',')
        if len(parts) != 2 or parts[0] == 'keV':
            continue
        h[int(round(float(parts[0])))] = float(parts[1])
    txt = path[:-4] + '.txt'
    if os.path.exists(txt):
        for line in io.open(txt, encoding='utf-8', errors='replace'):
            m = re.search(r'историй (\d+)', line)
            if m:
                n = int(m.group(1))
                break
    return h, n


def read_g4(path):
    decays, h = None, {}
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
        if m:
            decays = int(m.group(3))
        m = re.match(r'HIST (\d+) (\d+)', line)
        if m:
            h[int(m.group(1))] = int(m.group(2))
    return {k: v / decays for k, v in h.items()}, decays


def band(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k < hi)


BANDS = ['[ 0.. 25%E)', '[25.. 50%E)', '[50.. 75%E)', '[75..100%E)', '0–50 кэВ', '50–100 кэВ', '0–100 кэВ', '100–200 кэВ', '200–400 кэВ', 'континуум', 'пик']


def measures(h, p):
    m = {}
    m['пик'] = h.get(p, 0.0)
    m['полная'] = sum(h.values())
    m['континуум'] = m['полная'] - sum(h.get(k, 0.0) for k in range(p - 3, p + 1))
    for lo, hi in ((0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.0)):
        lo_i, hi_i = int(lo * p), min(int(hi * p), p - 3)
        m['[%2.0f..%3.0f%%E)' % (100 * lo, 100 * hi)] = band(h, lo_i, hi_i)
    for lo, hi in ((0, 50), (50, 100), (0, 100), (100, 200), (200, 400)):
        m['%d–%d кэВ' % (lo, hi)] = band(h, lo, hi)
    return m


def arm(a, na, b, nb, p):
    ma, mb = measures(a, p), measures(b, p)
    out = {}
    for k in BANDS:
        d = ma[k] - mb[k]
        # шум: биномиальный по доле полосы на историю у каждой стороны
        s = math.sqrt(max(ma[k], 0.0) / na + max(mb[k], 0.0) / nb)
        out[k] = (d, s)
    return out


def main():
    scene, e = sys.argv[1], sys.argv[2]
    g4def, g4kill = sys.argv[3], sys.argv[4]
    pairs = list(zip(sys.argv[5::2], sys.argv[6::2]))
    p = int(round(float(e)))
    gd, nd = read_g4(os.path.join(ROOT, 'g4out', 'g4_%s_%s_%s.log' % (scene, e, g4def)))
    gk, nk = read_g4(os.path.join(ROOT, 'g4out', 'g4_%s_%s_%s.log' % (scene, e, g4kill)))
    g4arm = arm(gd, nd, gk, nk, p)
    print('=== %s, E = %s кэВ: плечо тормозного электронов обвязки (доля на историю; «умолчание − рычаг»)' % (scene, e))
    print('арбитр: %s (%d) − %s (%d)' % (g4def, nd, g4kill, nk))
    hdr = '%-34s' % 'плечо' + ''.join('%22s' % b for b in BANDS)
    print(hdr)
    print('%-34s' % 'G4 def − killoutbrem' + ''.join('%+12.3E ±%7.1E' % g4arm[b] for b in BANDS))
    for od, ok in pairs:
        hd, n1 = read_ours(os.path.join(ROOT, 'ours', 'ours_%s_%s_%s.csv' % (scene, e, od)))
        hk, n2 = read_ours(os.path.join(ROOT, 'ours', 'ours_%s_%s_%s.csv' % (scene, e, ok)))
        oa = arm(hd, n1, hk, n2, p)
        print('%-34s' % ('наша %s − %s' % (od, ok)) + ''.join('%+12.3E ±%7.1E' % oa[b] for b in BANDS))
        print('%-34s' % '   наша/G4' + ''.join('%22s' % ('%.3f ± %.3f' % (oa[b][0] / g4arm[b][0], abs(oa[b][0] / g4arm[b][0]) * math.sqrt((oa[b][1] / oa[b][0]) ** 2 + (g4arm[b][1] / g4arm[b][0]) ** 2)) if g4arm[b][0] else '—') for b in BANDS))
    print()
    print('уровни (доля на историю):')
    print('%-34s' % 'G4 def' + ''.join('%22.4E' % measures(gd, p)[b] for b in BANDS))
    for od, ok in pairs:
        hd, n1 = read_ours(os.path.join(ROOT, 'ours', 'ours_%s_%s_%s.csv' % (scene, e, od)))
        md = measures(hd, p)
        print('%-34s' % ('наша ' + od) + ''.join('%22.4E' % md[b] for b in BANDS))
        print('%-34s' % '   наша/G4 def' + ''.join('%22.3f' % (md[b] / measures(gd, p)[b]) for b in BANDS))


main()
