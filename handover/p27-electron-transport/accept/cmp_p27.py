# -*- coding: utf-8 -*-
# П27 12.09.2026, приёмка `A72`: сводная таблица «наш отклик против Geant4» по сценам, узлам
# и плечам (--etr=0 «до» / --etr=1 «после»). Чтение и правило бина — те же, что у
# handover/p20-response-measures/a72/cmp_a72.py (ПОСЛЕДНИЙ бин = пик, bin = int(E/шаг + 0.5)).
#   python cmp_p27.py            — таблица по всем найденным парам
# Печатает markdown: пик наша/G4 (Δ %), полная, полосы в долях E, вылеты; для 59.541 —
# полосы по бинам 13–25 / 26–31 / 32–42 / 43–54 / 55–59 (потолок формы A72, ~~A63~~/~~A70~~).
import io, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
P20 = os.path.join(HERE, '..', '..', 'p20-response-measures', 'a72')
G4DIR = os.path.join(HERE, '..', 'g4')


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
    return {k: v / decays for k, v in h.items()}, h, decays


def band(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k < hi)


def pct(a, b):
    return '%+.2f' % (100.0 * (a / b - 1.0)) if b else '—'


def g4_log(scene, e):
    for d, tag in ((P20, e), (G4DIR, e)):
        for name in ('g4_%s_%s.log' % (scene, tag), 'g4_%s_%g.log' % (scene, float(tag))):
            p = os.path.join(d, name)
            if os.path.isfile(p):
                return p
    return None


SCENES = ('RC103_bare_gap5', 'OBS_bare_gap5', 'AS80_bare_gap5')
NODES = ('59.541', '661.657', '1461', '2614.511')

rows = []
print('| сцена | узел, кэВ | плечо | пик наша | пик G4 (отсч., шум %) | **Δ пик %** | Δ полная | 0–25 %E | 25–50 | 50–75 | 75–100 | вылет 511 | вылет 1022 |')
print('|---|---|---|---|---|---|---|---|---|---|---|---|---|')
for scene in SCENES:
    for node in NODES:
        g4p = g4_log(scene, node)
        if g4p is None:
            print('| %s | %s | — | нет лога Geant4 |' % (scene, node))
            continue
        g4, g4c, decays = read_g4(g4p)
        for arm in ('etr0', 'etr1'):
            op = os.path.join(HERE, 'ours_%s_%s_%s.csv' % (scene, node, arm))
            if not os.path.isfile(op):
                print('| %s | %s | %s | нет нашего файла |' % (scene, node, arm))
                continue
            ours = read_ours(op)
            n = max(ours) + 1
            p = n - 1
            po, pg, cg = ours.get(p, 0.0), g4.get(p, 0.0), g4c.get(p, 0)
            to, tg = sum(ours.values()), sum(g4.values())
            bands = []
            for lo, hi in ((0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.0)):
                lo_i, hi_i = int(lo * p), min(int(hi * p), p - 3)
                bands.append(pct(band(ours, lo_i, hi_i), band(g4, lo_i, hi_i)))
            esc = ['—', '—']
            if p > 1022:
                for i, e in enumerate((511, 1022)):
                    c = p - e
                    a = sum(ours.get(k, 0.0) for k in (c - 1, c, c + 1))
                    b = sum(g4.get(k, 0.0) for k in (c - 1, c, c + 1))
                    esc[i] = pct(a, b)
            print('| %s | %s | %s | %.4E | %.4E (%d, %.2f) | **%s** | %s | %s | %s | %s | %s | %s | %s |'
                  % (scene, node, 'до (etr=0)' if arm == 'etr0' else 'ПОСЛЕ (etr=1)', po, pg, cg,
                     100.0 / cg ** 0.5 if cg else 0.0, pct(po, pg), pct(to, tg),
                     bands[0], bands[1], bands[2], bands[3], esc[0], esc[1]))

# 59.541 — потолок формы A72: полосы по бинам, как в ~~A63~~/~~A70~~ (13–25 крупные потери,
# 32–42 и 43–54 — шельф мелких потерь и K-хвост, 55–59 — L-вылет, не предмет строки).
print()
print('### 59.541 кэВ — полосы по бинам (Δ % к Geant4; в скобках доля от полной у G4)')
print()
print('| сцена | плечо | пик (бин 60) | 13–25 | 26–31 | 32–42 | 43–54 | 55–59 |')
print('|---|---|---|---|---|---|---|---|')
for scene in SCENES:
    g4p = g4_log(scene, '59.541')
    if g4p is None:
        continue
    g4, g4c, decays = read_g4(g4p)
    tg = sum(g4.values())
    for arm in ('etr0', 'etr1'):
        op = os.path.join(HERE, 'ours_%s_59.541_%s.csv' % (scene, arm))
        if not os.path.isfile(op):
            continue
        ours = read_ours(op)
        p = max(ours)
        cells = []
        for lo, hi in ((13, 26), (26, 32), (32, 43), (43, 55), (55, 60)):
            a, b = band(ours, lo, hi), band(g4, lo, hi)
            cells.append('%s (%.2f %%)' % (pct(a, b), 100.0 * b / tg))
        print('| %s | %s | %s | %s |' % (scene, 'до' if arm == 'etr0' else 'ПОСЛЕ',
                                          pct(ours.get(p, 0.0), g4.get(p, 0.0)), ' | '.join(cells)))

# Сходимость по шагу — RC103 1461.
print()
print('### Сходимость по шагу переноса (RC103 1461, --etr-step=)')
print()
print('| шаг | пик Δ % | полная | 0–25 %E | 25–50 | 50–75 | 75–100 |')
print('|---|---|---|---|---|---|---|')
g4p = g4_log('RC103_bare_gap5', '1461')
if g4p:
    g4, g4c, decays = read_g4(g4p)
    for step, name in (('0.05', 'ours_RC103_bare_gap5_1461_etr1_step0.05.csv'),
                       ('0.1', 'ours_RC103_bare_gap5_1461_etr1.csv'),
                       ('0.2', 'ours_RC103_bare_gap5_1461_etr1_step0.2.csv')):
        op = os.path.join(HERE, name)
        if not os.path.isfile(op):
            continue
        ours = read_ours(op)
        p = max(ours)
        bands = []
        for lo, hi in ((0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.0)):
            lo_i, hi_i = int(lo * p), min(int(hi * p), p - 3)
            bands.append(pct(band(ours, lo_i, hi_i), band(g4, lo_i, hi_i)))
        print('| %s | %s | %s | %s |' % (step, pct(ours.get(p, 0.0), g4.get(p, 0.0)),
                                         pct(sum(ours.values()), sum(g4.values())), ' | '.join(bands)))
