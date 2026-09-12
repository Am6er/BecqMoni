# -*- coding: utf-8 -*-
# П32 12.09.2026, замеры `F16` и `A64` против Geant4: сводные таблицы «наш отклик против арбитра»
# по сценам, узлам, плечам (--etr=0 / --etr=1) и мирам арбитра (air / vacuum).
# Чтение и правило бина — те же, что у handover/p27-electron-transport/accept/cmp_p27.py
# (ПОСЛЕДНИЙ бин = пик, bin = int(E/шаг + 0.5)).
#   python cmp_p32.py            — обе таблицы
# F16: полосы в долях E (0–25 / 25–50 / 50–75 / 75–100 %E, верх обрезан p−3 как у cmp_p27), пик,
#      полная; отдельно — бины 50…65 кэВ (K-линии Lu 52.97/54.07/61.28/62.97) по одному.
# A64: мерка 02.09.2026 (§4 handover-2026-09-02-transport-a55-a62.md): «низ 13…край» = бины
#      [13, floor(Ec)], «край…пик» = [floor(Ec)+1, p−2], «пик» = два верхних бина p−1, p;
#      Ec = E·(2E/511)/(1+2E/511): 477.34 (661.657) и 1118.1 (1332.5). Шум — пуассоновский по
#      отсчётам арбитра и по историям нашей (аналоговая ветвь: n·доля), Δ/σ — по сумме квадратов.
import io, math, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
G4DIR = os.path.join(HERE, 'g4')
P27 = os.path.join(HERE, '..', 'p27-electron-transport', 'accept')
P20 = os.path.join(HERE, '..', 'p20-response-measures', 'a72')
P26 = os.path.join(HERE, '..', 'p26-amber22', 'g4')
ME = 510.998950


def read_ours(path):
    h = {}
    n = None
    for line in io.open(path, encoding='utf-8-sig'):
        parts = line.strip().split(',')
        if len(parts) != 2 or parts[0] == 'keV':
            continue
        h[int(round(float(parts[0])))] = float(parts[1])
    txt = path[:-4] + '.txt'
    if os.path.isfile(txt):
        for line in io.open(txt, encoding='utf-8-sig', errors='replace'):
            m = re.search(r'историй (\d+),', line)
            if m:
                n = int(m.group(1))
    return h, n


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
    return {k: v / decays for k, v in h.items()}, h, decays


def band(h, lo, hi):
    """сумма бинов lo..hi ВКЛЮЧИТЕЛЬНО"""
    return sum(v for k, v in h.items() if lo <= k <= hi)


def pct(a, b):
    return '%+.2f' % (100.0 * (a / b - 1.0)) if b else '—'


def sig(a, na, b, nb):
    """относительный шум пары, %: sqrt(1/(na·a) + 1/(nb·b))"""
    if a <= 0 or b <= 0 or not na or not nb:
        return float('nan')
    return 100.0 * math.sqrt(1.0 / (na * a) + 1.0 / (nb * b))


def cell(a, na, b, nb):
    d = 100.0 * (a / b - 1.0) if b else float('nan')
    s = sig(a, na, b, nb)
    return '%+.2f (σ %.2f, %.1fσ)' % (d, s, abs(d) / s if s else 0.0)


def edge(e):
    k = 2.0 * e / ME
    return e * k / (1.0 + k)


# ---------------------------------------------------------------- F16
print('## F16 — ASN16_lu_side (CsI 15×18×60, банка Lu₂O₃ сбоку), 200 кэВ')
print()
print('| арбитр (мир) | плечо | пик наша | пик G4 (отсч., шум %) | **Δ пик %** | Δ полная | **0–25 %E (0–50 кэВ)** | **25–50 %E (50–100 кэВ)** | 50–75 %E | 75–100 %E |')
print('|---|---|---|---|---|---|---|---|---|---|')
f16 = {}
for world in ('air', 'vacuum'):
    g4p = os.path.join(G4DIR, 'g4_ASN16_lu_side_200_%s.log' % world)
    if not os.path.isfile(g4p):
        print('| %s | — | нет лога Geant4 |' % world)
        continue
    g4, g4c, decays = read_g4(g4p)
    for arm in ('etr0', 'etr1'):
        op = os.path.join(HERE, 'f16', 'ours_ASN16_lu_side_200_%s.csv' % arm)
        if not os.path.isfile(op):
            print('| %s | %s | нет нашего файла |' % (world, arm))
            continue
        ours, n = read_ours(op)
        p = max(ours)
        po, pg, cg = ours.get(p, 0.0), g4.get(p, 0.0), g4c.get(p, 0)
        to, tg = sum(ours.values()), sum(g4.values())
        cells = []
        for lo, hi in ((0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.0)):
            lo_i, hi_i = int(lo * p), min(int(hi * p), p - 3) - 1   # как cmp_p27: [lo_i, hi_i)
            a, b = band(ours, lo_i, hi_i), band(g4, lo_i, hi_i)
            cells.append(cell(a, n, b, decays))
            f16[(world, arm, lo)] = (a, b)
        print('| %s | %s | %.4E | %.4E (%d, %.2f) | **%s** | %s | %s |'
              % (world, 'etr=0' if arm == 'etr0' else 'etr=1', po, pg, cg, 100.0 / cg ** 0.5 if cg else 0.0,
                 pct(po, pg), pct(to, tg), ' | '.join(cells)))

print()
print('### F16 — бины 48…66 кэВ по одному (наша/G4, мир air, etr=0; K-линии Lu: Kα2 52.97, Kα1 54.07, Kβ3 60.96, Kβ1 61.28, Kβ2 62.97)')
print()
g4p = os.path.join(G4DIR, 'g4_ASN16_lu_side_200_air.log')
op = os.path.join(HERE, 'f16', 'ours_ASN16_lu_side_200_etr0.csv')
if os.path.isfile(g4p) and os.path.isfile(op):
    g4, g4c, decays = read_g4(g4p)
    ours, n = read_ours(op)
    print('| бин | наша | G4 | наша/G4 |')
    print('|---|---|---|---|')
    for k in range(48, 67):
        a, b = ours.get(k, 0.0), g4.get(k, 0.0)
        print('| %d | %.3E | %.3E | %s |' % (k, a, b, ('%.2f' % (a / b)) if b else '—'))
    a, b = band(ours, 50, 65), band(g4, 50, 65)
    print('| **50…65 вместе** | %.4E | %.4E | **%.3f** |' % (a, b, a / b))

# ---------------------------------------------------------------- A64
print()
print('## A64 — AS80_point0 (NaI Ø80×80 с обвязкой, точка вплотную): полосы мерки 02.09.2026')
print()
print('| узел, кэВ | арбитр (мир) | плечо | пик (2 бина) наша | пик G4 (отсч.) | **Δ пик** | Δ полная | **низ 13…край** | **край…пик** |')
print('|---|---|---|---|---|---|---|---|---|')


def a64_row(label_node, label_world, label_arm, ours, n, g4, g4c, decays, e):
    p = max(ours)
    ec = int(math.floor(edge(e)))
    po = ours.get(p, 0.0) + ours.get(p - 1, 0.0)
    pg = g4.get(p, 0.0) + g4.get(p - 1, 0.0)
    cg = g4c.get(p, 0) + g4c.get(p - 1, 0)
    to, tg = sum(ours.values()), sum(g4.values())
    lo_o, lo_g = band(ours, 13, ec), band(g4, 13, ec)
    hi_o, hi_g = band(ours, ec + 1, p - 2), band(g4, ec + 1, p - 2)
    print('| %s | %s | %s | %.4E | %.4E (%d) | **%s** | %s | %s | **%s** |'
          % (label_node, label_world, label_arm, po, pg, cg, cell(po, n, pg, decays), pct(to, tg),
             cell(lo_o, n, lo_g, decays), cell(hi_o, n, hi_g, decays)))


for node in ('661.657', '1332.5'):
    e = float(node)
    for world in ('air', 'vacuum'):
        g4p = os.path.join(G4DIR, 'g4_AS80_point0_%s_%s.log' % (node, world))
        if not os.path.isfile(g4p):
            print('| %s | %s | — | нет лога Geant4 |' % (node, world))
            continue
        g4, g4c, decays = read_g4(g4p)
        for arm in ('etr0', 'etr1'):
            op = os.path.join(HERE, 'a64', 'ours_AS80_point0_%s_%s.csv' % (node, arm))
            if not os.path.isfile(op):
                print('| %s | %s | %s | нет нашего файла |' % (node, world, arm))
                continue
            ours, n = read_ours(op)
            a64_row(node, world, 'etr=0' if arm == 'etr0' else 'etr=1', ours, n, g4, g4c, decays, e)

# Плечо БЕЗ обвязки — голый AS80 в 5 мм (П27 accept, etr=0/1) против G4 П20 (vacuum, 2 млн) и П26 (vacuum, 2 млн)
print()
print('### A64 — плечо БЕЗ обвязки: AS80_bare_gap5 661.657 (наши CSV П27 accept, арбитр П20/П26 vacuum), та же мерка')
print()
print('| узел, кэВ | арбитр (мир) | плечо | пик (2 бина) наша | пик G4 (отсч.) | **Δ пик** | Δ полная | **низ 13…край** | **край…пик** |')
print('|---|---|---|---|---|---|---|---|---|')
for g4p, lab in ((os.path.join(P20, 'g4_AS80_bare_gap5_661.657.log'), 'vacuum П20'),
                 (os.path.join(P26, 'g4_AS80_bare_gap5_661.657_vacuum.log'), 'vacuum П26')):
    if not os.path.isfile(g4p):
        continue
    g4, g4c, decays = read_g4(g4p)
    for arm in ('etr0', 'etr1'):
        op = os.path.join(P27, 'ours_AS80_bare_gap5_661.657_%s.csv' % arm)
        if not os.path.isfile(op):
            continue
        ours, n = read_ours(op)
        a64_row('661.657 голый', lab, 'etr=0' if arm == 'etr0' else 'etr=1', ours, n, g4, g4c, decays, 661.657)

# Плечо БЕЗ обвязки с шумом плеча с обвязкой: голый AS80 8 млн / 8 млн (bare/, g4/…_vacuum), оба узла
print()
print('### A64 — плечо БЕЗ обвязки, 8 млн / 8 млн: AS80_bare_gap5 (точка в 5 мм), арбитр vacuum, та же мерка')
print()
print('| узел, кэВ | арбитр (мир) | плечо | пик (2 бина) наша | пик G4 (отсч.) | **Δ пик** | Δ полная | **низ 13…край** | **край…пик** |')
print('|---|---|---|---|---|---|---|---|---|')
for node in ('661.657', '1332.5'):
    g4p = os.path.join(G4DIR, 'g4_AS80_bare_gap5_%s_vacuum.log' % node)
    if not os.path.isfile(g4p):
        continue
    g4, g4c, decays = read_g4(g4p)
    for arm in ('etr0', 'etr1'):
        op = os.path.join(HERE, 'bare', 'ours_AS80_bare_gap5_%s_%s.csv' % (node, arm))
        if not os.path.isfile(op):
            continue
        ours, n = read_ours(op)
        a64_row(node + ' голый', 'vacuum', 'etr=0' if arm == 'etr0' else 'etr=1', ours, n, g4, g4c, decays, float(node))

# Контроль нашей стороны: ctl/ours_AS80_bare_gap5_661.657_etr0.csv побайтно = П27 accept
print()
ctl = os.path.join(HERE, 'ctl', 'ours_AS80_bare_gap5_661.657_etr0.csv')
ref = os.path.join(P27, 'ours_AS80_bare_gap5_661.657_etr0.csv')
if os.path.isfile(ctl) and os.path.isfile(ref):
    same = io.open(ctl, 'rb').read() == io.open(ref, 'rb').read()
    print('Положительный контроль нашей стороны (build_p32 против build_p27, AS80_bare_gap5 661.657 etr=0, 4 млн): %s'
          % ('ПОБАЙТНО СОВПАЛ' if same else '⛔ РАЗОШЁЛСЯ'))
