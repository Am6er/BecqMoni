# -*- coding: utf-8 -*-
# П106 (M13, 19.09.2026): разложение «возврата» электрона по НАСЕЛЕНИЯМ — арбитр против нас, доли на историю по полосам.
# Читает наши CSV (D:\BqMoni_Claude\p106\ours, + П100/П94 из handover), логи арбитра (p106\g4out + handover П94/П92/П55).
# Тот же разбор файлов и те же полосы, что у cmp100.py (П100).
#   python arms106.py <сцена> <E> [--md]
import io, os, re, sys, math
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'D:\BqMoni_Claude\p106'
REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
P100 = os.path.join(REPO, 'handover', 'p100-m13')
P94 = os.path.join(REPO, 'handover', 'p94-amber44')
P92 = os.path.join(REPO, 'handover', 'p92-m12')
P55 = os.path.join(REPO, 'handover', 'p55-a72')


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
    decays, h, esc = None, {}, None
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
        if m:
            decays = int(m.group(3))
        m = re.match(r'HIST (\d+) (\d+)', line)
        if m:
            h[int(m.group(1))] = int(m.group(2))
        if line.startswith('ESCPOP'):
            esc = line.strip()
    if not decays:
        return None, None, None
    return {k: v / decays for k, v in h.items()}, decays, esc


def band(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k < hi)


BANDS = ['[ 0.. 25%E)', '[25.. 50%E)', '[50.. 75%E)', '[75..100%E)', '0–50 кэВ', '50–100 кэВ', '0–100 кэВ', '100–200 кэВ', 'континуум', 'пик']


def measures(h, p, e):
    m = {}
    m['пик'] = h.get(p, 0.0)
    m['полная'] = sum(h.values())
    m['континуум'] = m['полная'] - sum(h.get(k, 0.0) for k in range(p - 3, p + 1))
    for lo, hi in ((0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.0)):
        lo_i, hi_i = int(lo * p), min(int(hi * p), p - 3)
        m['[%2.0f..%3.0f%%E)' % (100 * lo, 100 * hi)] = band(h, lo_i, hi_i)
    for lo, hi in ((0, 50), (50, 100), (0, 100), (100, 200)):
        m['%d–%d кэВ' % (lo, hi)] = band(h, lo, hi)
    return m


def load(scene, e):
    data, esc = {}, {}
    for d, suffix in ((os.path.join(ROOT, 'ours'), ''), (os.path.join(P100, 'ours'), '100'), (os.path.join(P94, 'ours'), '94')):
        if not os.path.isdir(d):
            continue
        for f in sorted(os.listdir(d)):
            m = re.match(r'ours_%s_%s_(\w+)\.csv$' % (re.escape(scene), re.escape(e)), f)
            if m:
                h, n = read_ours(os.path.join(d, f))
                data['наша ' + m.group(1) + suffix] = (h, n)
    p55scene = 'RC103_point0' if scene == 'RC103_point0_p55' else (None if scene == 'RC103_point0' else scene)
    sources = [(os.path.join(ROOT, 'g4out'), scene), (os.path.join(P94, 'g4'), scene), (os.path.join(P92, 'g4'), scene)]
    if p55scene:
        sources.append((os.path.join(P55, 'g4'), p55scene))
    for d, sc in sources:
        if not os.path.isdir(d):
            continue
        for f in sorted(os.listdir(d)):
            m = re.match(r'g4_%s_%s_(\w+)\.log$' % (re.escape(sc), re.escape(e)), f)
            if m:
                key = 'G4 ' + m.group(1)
                if key in data:
                    continue
                h, n, es = read_g4(os.path.join(d, f))
                if h is not None:
                    data[key] = (h, n)
                    if es:
                        esc[key] = es
    # Вторая редакция рычага killoutbrem (только eBrem; аннигиляция позитрона обвязки не гасится) — она и идёт в таблицы;
    # первая редакция (02:00, гасила и кванты аннигиляции) остаётся под именем *_v1 для сверки.
    for name in ('killoutbrem', 'photon'):
        k2 = 'G4 ' + name + '2'
        if k2 in data:
            if 'G4 ' + name in data:
                data['G4 ' + name + '_v1'] = data.pop('G4 ' + name)
                if 'G4 ' + name in esc:
                    esc['G4 ' + name + '_v1'] = esc.pop('G4 ' + name)
            data['G4 ' + name] = data.pop(k2)
            if k2 in esc:
                esc['G4 ' + name] = esc.pop(k2)
    return data, esc


def sig(v, n):
    # биномиальный шум доли на историю (абсолютный)
    return math.sqrt(max(v, 0.0) / n) if n else 0.0


def fmt(v, s=None):
    if v is None:
        return '—'
    if s is None:
        return '%+.3E' % v
    return '%+.3E ± %.1E' % (v, s)


def main():
    scene, e = sys.argv[1], sys.argv[2]
    md = '--md' in sys.argv
    data, esc = load(scene, e)
    if not data:
        print('нет данных для %s %s' % (scene, e))
        return
    p = max(max(h) for h, n in data.values())
    ms = {k: measures(h, p, float(e)) for k, (h, n) in data.items()}
    ns = {k: n for k, (h, n) in data.items()}
    print('=== %s, E = %s кэВ, бин пика %d ===' % (scene, e, p))
    print('прогоны: ' + ', '.join('%s (%s)' % (k, ns[k]) for k in sorted(data)))
    for k in sorted(esc):
        print('  %s: %s' % (k, esc[k]))

    # Абсолютные доли на историю по всем плечам.
    print()
    print('доли на историю:')
    print('%-24s' % 'плечо' + ''.join('%13s' % b for b in BANDS))
    for k in sorted(ms):
        print('%-24s' % k + ''.join('%13.4E' % ms[k][b] for b in BANDS))

    def arm(name, a, b0):
        if a not in ms or b0 not in ms:
            return None
        return name, [(ms[a][bb] - ms[b0][bb], math.sqrt(sig(ms[a][bb], ns[a]) ** 2 + sig(ms[b0][bb], ns[b0]) ** 2)) for bb in BANDS]

    g4def = 'G4 def' if 'G4 def' in ms else 'G4 escpop'
    rows = []
    # Население 1: свой (рождённый в кристалле) e- — весь эффект снаружи (возврат + тормозное + δ).
    rows.append(('=== 1. СВОЙ e-: весь эффект вылета (возврат e-, тормозное и δ снаружи)', None))
    rows.append(arm('G4 def − killescown', g4def, 'G4 killescown'))
    rows.append(arm('наша def − own0', 'наша def', 'наша own0'))
    rows.append(arm('наша detour0 − off_detour0(П94)', 'наша detour0', 'наша off_detour094'))
    rows.append(arm('П100: elmix1_d0 − off_d0(П94)', 'наша elmix1_detour0100', 'наша off_detour094'))
    rows.append(('=== 2. Только возврат самого своего e- (тормозное/δ снаружи остаются)', None))
    rows.append(arm('G4 def − killret', g4def, 'G4 killret'))
    rows.append(arm('наша def − ret0', 'наша def', 'наша ret0'))
    rows.append(arm('G4 def − killretsame', g4def, 'G4 killretsame'))
    rows.append(arm('наша def − same0', 'наша def', 'наша same0'))
    rows.append(arm('G4 def − killretother', g4def, 'G4 killretother'))
    rows.append(arm('наша def − other0', 'наша def', 'наша other0'))
    rows.append(('=== 3. Тормозное (и δ) вылетевшего своего e-, вернувшееся в кристалл', None))
    rows.append(arm('G4 def − killescbrem', g4def, 'G4 killescbrem'))
    rows.append(arm('наша def − brem0', 'наша def', 'наша brem0'))
    rows.append(arm('G4 def − killescdelta', g4def, 'G4 killescdelta'))
    rows.append(arm('G4 killret − killescown', 'G4 killret', 'G4 killescown'))
    rows.append(arm('наша ret0 − own0', 'наша ret0', 'наша own0'))
    rows.append(('=== 4. ЗАНЕСЁННЫЙ e-, покидающий кристалл: его возврат (пересечение плеч killesc/killcarry)', None))
    rows.append(arm('G4 def − killesccarry', g4def, 'G4 killesccarry'))
    rows.append(arm('наша def − carry0', 'наша def', 'наша carry0'))
    rows.append(('=== 5. Старые плечи П94/П100 (для сверки с §4.4 П100)', None))
    rows.append(arm('G4 def − killesc (все e-)', g4def, 'G4 killesc'))
    rows.append(arm('G4 def − killcarry (занос)', g4def, 'G4 killcarry'))
    rows.append(arm('наша def − detour0 (занос)', 'наша def', 'наша detour0'))
    rows.append(('=== 6. База «ни возврата своего, ни заноса»', None))
    if all(k in ms for k in ('G4 killescown', 'G4 killcarry', g4def)):
        rows.append(('G4 killescown + killcarry − def', [(ms['G4 killescown'][bb] + ms['G4 killcarry'][bb] - ms[g4def][bb], 0.0) for bb in BANDS]))
    if all(k in ms for k in ('G4 killesc', 'G4 killcarry', g4def)):
        rows.append(('G4 killesc + killcarry − def (П100)', [(ms['G4 killesc'][bb] + ms['G4 killcarry'][bb] - ms[g4def][bb], 0.0) for bb in BANDS]))
    if 'наша base0' in ms:
        rows.append(('наша base0 (detour0 + own0)', [(ms['наша base0'][bb], sig(ms['наша base0'][bb], ns['наша base0'])) for bb in BANDS]))
    if 'наша off_detour094' in ms:
        rows.append(('наша off_detour0 (П94)', [(ms['наша off_detour094'][bb], sig(ms['наша off_detour094'][bb], ns['наша off_detour094'])) for bb in BANDS]))
    for lbl, key in (('G4 base (прямой прогон)', 'G4 base'), ('G4 photon (и без тормозного обвязки)', 'G4 photon'),
                     ('наша photon0 (detour0 + own0 + outbrem0)', 'наша photon0'), ('наша lbrem1_base0 (ключ ВКЛ)', 'наша lbrem1_base0')):
        if key in ms:
            rows.append((lbl, [(ms[key][bb], sig(ms[key][bb], ns[key])) for bb in BANDS]))
    rows.append(('=== 7. Тормозное электронов ОБВЯЗКИ (рождённых вне кристалла): толстая мишень / ключ lbrem / арбитр', None))
    rows.append(arm('G4 def − killoutbrem', g4def, 'G4 killoutbrem'))
    rows.append(arm('наша def − outbrem0 (толстая мишень)', 'наша def', 'наша outbrem0'))
    rows.append(arm('наша lbrem1 − lbrem1_outbrem0 (ключ)', 'наша lbrem1', 'наша lbrem1_outbrem0'))
    rows.append(arm('G4 base − photon', 'G4 base', 'G4 photon'))
    rows.append(arm('наша base0 − photon0', 'наша base0', 'наша photon0'))
    rows.append(arm('G4 def − killoutbrem_v1 (с аннигиляцией)', g4def, 'G4 killoutbrem_v1'))
    print()
    print('ПЛЕЧИ (доля на историю; «умолчание − рычаг»: отрицательное = член ВЫНИМАЕТ события из полосы):')
    print('%-38s' % 'плечо' + ''.join('%22s' % b for b in BANDS))
    for r in rows:
        if r is None:
            continue
        name, vals = r
        if vals is None:
            print(name)
            continue
        print('%-38s' % name + ''.join('%22s' % fmt(v, s) for v, s in vals))

    # Отношения наша/G4 по одноимённым населениям.
    print()
    print('ОТНОШЕНИЕ наша/G4 по населениям (полоса):')
    pairs = [('свой: весь эффект', ('наша def', 'наша own0'), (g4def, 'G4 killescown')),
             ('свой: возврат e-', ('наша def', 'наша ret0'), (g4def, 'G4 killret')),
             ('свой: та же грань', ('наша def', 'наша same0'), (g4def, 'G4 killretsame')),
             ('свой: другая грань', ('наша def', 'наша other0'), (g4def, 'G4 killretother')),
             ('свой: тормозное', ('наша def', 'наша brem0'), (g4def, 'G4 killescbrem')),
             ('занесённый: возврат', ('наша def', 'наша carry0'), (g4def, 'G4 killesccarry')),
             ('П100 мерка: наш возврат/G4 killesc', ('наша detour0', 'наша off_detour094'), (g4def, 'G4 killesc')),
             ('тормозное обвязки: толстая/G4', ('наша def', 'наша outbrem0'), (g4def, 'G4 killoutbrem')),
             ('тормозное обвязки: ключ lbrem/G4', ('наша lbrem1', 'наша lbrem1_outbrem0'), (g4def, 'G4 killoutbrem')),
             ('тормозное обвязки в базе: толстая/G4', ('наша base0', 'наша photon0'), ('G4 base', 'G4 photon'))]
    print('%-38s' % 'население' + ''.join('%13s' % b for b in BANDS))
    for name, (a, b0), (c, d0) in pairs:
        if not all(k in ms for k in (a, b0, c, d0)):
            continue
        cells = []
        for bb in BANDS:
            o = ms[a][bb] - ms[b0][bb]
            g = ms[c][bb] - ms[d0][bb]
            cells.append('%13.2f' % (o / g) if abs(g) > 1e-12 else '%13s' % '—')
        print('%-38s' % name + ''.join(cells))


if __name__ == '__main__':
    main()
