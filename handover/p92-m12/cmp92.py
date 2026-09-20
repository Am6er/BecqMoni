# -*- coding: utf-8 -*-
# П92 (M12, 17.09.2026): сводка плеч абляции — наш сырой отклик (G4RawProbe --out=, keV,response)
# против арбитра Geant4 (g4cf hist: HIST <бин> <счёт>) по полосам.
#   python cmp92.py <сцена> <E> [--geo=<суффиксы через запятую>] [--md]
# Читает D:\BqMoni_Claude\p92\ours\ours_<сцена><суффикс>_<E>_<tag>.csv и g4out\g4_<…>_<tag>.log.
# Правило бина у обоих одно: bin = (int)(edep/шаг + 0.5), последний бин — пик (П20/П55).
# Полосы: пик (последний бин), четверти шкалы континуума [0..25/25..50/50..75/75..100 %E) без ±3 бинов
# пика (границы П55 cmp55.py), E−300…E−4, E−100…E−4, полосы низа 0–100/100–200/200–300 кэВ (П42 §5),
# полоса обратного рассеяния E_bs±20 (E_bs = E/(1+2E/511)), вылеты 511/1022 (E>1022).
# «±» — биномиальный шум разности √(1/N₁+1/N₂), где N — отсчёты полосы (наша сторона: доля × историй;
# зерно у наших плеч одно, поэтому для разностей «плечо − ref» это ВЕРХНЯЯ граница).
import io, os, re, sys, math
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'D:\BqMoni_Claude\p92'


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
    if not decays:
        return None, None
    return {k: v / decays for k, v in h.items()}, decays


def band(h, lo, hi):
    return sum(v for k, v in h.items() if lo <= k < hi)


def measures(h, p, e):
    m = {}
    m['пик'] = h.get(p, 0.0)
    m['полная'] = sum(h.values())
    m['континуум'] = m['полная'] - sum(h.get(k, 0.0) for k in range(p - 3, p + 1))
    for lo, hi in ((0.0, 0.25), (0.25, 0.5), (0.5, 0.75), (0.75, 1.0)):
        lo_i, hi_i = int(lo * p), min(int(hi * p), p - 3)
        m['[%2.0f..%3.0f%%E)' % (100 * lo, 100 * hi)] = band(h, lo_i, hi_i)
    m['E−300…E−4'] = band(h, max(0, p - 300), p - 3)
    m['E−100…E−4'] = band(h, max(0, p - 100), p - 3)
    for lo, hi in ((0, 50), (50, 100), (0, 100), (100, 200), (200, 300), (300, 400)):
        if hi < p - 3:
            m['%d–%d кэВ' % (lo, hi)] = band(h, lo, hi)
    ebs = e / (1.0 + 2.0 * e / 510.99895)
    m['обр.расс. %.0f±20' % ebs] = band(h, int(ebs) - 20, int(ebs) + 21)
    if p > 1022:
        for name, esc in (('вылет511', 511), ('вылет1022', 1022)):
            c = p - esc
            m[name] = sum(h.get(k, 0.0) for k in (c - 1, c, c + 1))
    return m


def load(scene, e):
    data, hist = {}, {}
    for f in sorted(os.listdir(os.path.join(ROOT, 'ours'))):
        m = re.match(r'ours_%s_%s_(\w+)\.csv$' % (re.escape(scene), re.escape(e)), f)
        if m:
            h, n = read_ours(os.path.join(ROOT, 'ours', f))
            data['наша ' + m.group(1)] = (h, n)
    for f in sorted(os.listdir(os.path.join(ROOT, 'g4out'))):
        m = re.match(r'g4_%s_%s_(\w+)\.log$' % (re.escape(scene), re.escape(e)), f)
        if m:
            h, n = read_g4(os.path.join(ROOT, 'g4out', f))
            if h is not None:
                data['G4 ' + m.group(1)] = (h, n)
    return data


def sigma(v1, n1, v2, n2):
    a = v1 * n1 if n1 else 0.0
    b = v2 * n2 if n2 else 0.0
    return 100.0 * math.sqrt((1.0 / a if a > 0 else 0.0) + (1.0 / b if b > 0 else 0.0))


def cell(v, base, n_v, n_b):
    if base <= 0 or v is None:
        return '%16s' % '—'
    return '%+7.2f ±%4.1f' % (100.0 * (v / base - 1.0), sigma(v, n_v, base, n_b)) + '   '


def table(title, keys, cols, ms, ns, md):
    print()
    print('**%s**' % title if md else title)
    print()
    if md:
        print('| полоса | ' + ' | '.join(c[0] for c in cols) + ' |')
        print('|---|' + '---|' * len(cols))
    else:
        print('%-22s' % 'полоса' + ''.join('%22s' % c[0] for c in cols))
    for k in keys:
        row = []
        for label, num, den in cols:
            if num not in ms or den not in ms:
                row.append('—')
                continue
            row.append(cell(ms[num][k], ms[den][k], ns[num], ns[den]).strip())
        if md:
            print('| %s | ' % k + ' | '.join(row) + ' |')
        else:
            print('%-22s' % k + ''.join('%22s' % r for r in row))


def main():
    scene, e = sys.argv[1], sys.argv[2]
    md = '--md' in sys.argv
    geos = ['']
    for a in sys.argv[3:]:
        if a.startswith('--geo='):
            geos += ['_' + g for g in a[6:].split(',') if g]
    data = {}
    for g in geos:
        for k, v in load(scene + g, e).items():
            data[k + (g if g else '')] = v
    if not data:
        print('нет данных для %s %s' % (scene, e))
        return
    p = max(max(h) for h, n in data.values())
    ms = {k: measures(h, p, float(e)) for k, (h, n) in data.items()}
    ns = {k: n for k, (h, n) in data.items()}
    print('=== %s, E = %s кэВ, бин пика %d ===' % (scene, e, p))
    print('прогоны: ' + ', '.join('%s (%s)' % (k, ns[k]) for k in data))
    keys = list(next(iter(ms.values())).keys())

    # 1. Наша сторона против арбитра в ТОМ ЖЕ приближении (плечо к плечу).
    cols = [('ref/def', 'наша ref', 'G4 def'), ('ref/killesc', 'наша ref', 'G4 killesc'),
            ('detour0/killcarry', 'наша detour0', 'G4 killcarry'), ('ref/fullcarry', 'наша ref', 'G4 fullcarry'),
            ('ecomp0/def', 'наша ecomp0', 'G4 def'), ('bpath0/def', 'наша bpath0', 'G4 def'),
            ('nobrem/def', 'наша nobrem', 'G4 def'), ('detour1/def', 'наша detour1', 'G4 def')]
    cols = [c for c in cols if c[1] in ms and c[2] in ms]
    table('наша сторона / арбитр − 1, % (столбец — пара плеч: наше плечо / плечо арбитра)', keys, cols, ms, ns, md)
    if len(geos) > 1:
        cols = [('ref/def', 'наша ref', 'G4 def')]
        for g in geos[1:]:
            cols.append(('ref/def %s' % g[1:], 'наша ref' + g, 'G4 def' + g))
        cols = [c for c in cols if c[1] in ms and c[2] in ms]
        table('наша сторона / арбитр − 1, % — по ГЕОМЕТРИЯМ (обвязка снята у обеих сторон)', keys, cols, ms, ns, md)

    # 2. Вклад члена по полосам у каждой стороны: (плечо − умолчание)/умолчание.
    cols = [('G4 killesc/def', 'G4 killesc', 'G4 def'), ('G4 killcarry/def', 'G4 killcarry', 'G4 def'),
            ('G4 fullcarry/def', 'G4 fullcarry', 'G4 def'),
            ('наша detour0/ref', 'наша detour0', 'наша ref'), ('наша ecomp0/ref', 'наша ecomp0', 'наша ref'),
            ('наша bpath0/ref', 'наша bpath0', 'наша ref'), ('наша nobrem/ref', 'наша nobrem', 'наша ref'),
            ('наша detour1/ref', 'наша detour1', 'наша ref')]
    for g in geos[1:]:
        cols.append(('G4 %s/def' % g[1:], 'G4 def' + g, 'G4 def'))
        cols.append(('наша %s/ref' % g[1:], 'наша ref' + g, 'наша ref'))
    cols = [c for c in cols if c[1] in ms and c[2] in ms]
    table('цена члена по полосам у каждой стороны: (плечо − умолчание)/умолчание, %', keys, cols, ms, ns, md)

    # 3. Абсолютные доли на историю — для масштаба.
    print()
    print('доли на историю (G4 def / наша ref):')
    for k in keys:
        a = ms['G4 def'][k] if 'G4 def' in ms else float('nan')
        b = ms['наша ref'][k] if 'наша ref' in ms else float('nan')
        print('  %-22s %11.4E %11.4E' % (k, a, b))


if __name__ == '__main__':
    main()
