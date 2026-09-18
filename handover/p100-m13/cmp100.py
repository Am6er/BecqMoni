# -*- coding: utf-8 -*-
# П100 (M13, 18.09.2026; наследник cmp94.py П94): сводка приёмки — наш сырой отклик (G4RawProbe --out=, keV,response)
# против арбитра Geant4 (g4cf hist: HIST <бин> <счёт>) по полосам П92/П55. Наследник cmp92.py (П92): тот же
# разбор файлов, те же полосы; добавлены полосы 59.5 кэВ из cmp55.py (32–42, 43–54, 55–59, бины E−3…E−1) и
# чтение логов арбитра из артефактов П92 (handover/p92-m12/g4) и П55 (handover/p55-a72/g4; там сцена П55
# RC103_point0 зовётся RC103_point0, у П92/П94 — RC103_point0_p55) и наших CSV П92 (ref/detour0 → «наша ref92»).
#   python cmp94.py <сцена> <E> [--md]
# Правило бина у обоих одно: bin = (int)(edep/шаг + 0.5), последний бин — пик (П20/П55).
# «±» — биномиальный шум разности √(1/N₁+1/N₂), где N — отсчёты полосы (наша сторона: доля × историй).
import io, os, re, sys, math
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'D:\BqMoni_Claude\p100'
P94 = os.path.join(r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8', 'handover', 'p94-amber44')
REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
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
    if e < 100.0:
        # полосы П55 для 59.5 кэВ (cmp55.py)
        m['32–42'] = band(h, 32, 43)
        m['43–54'] = band(h, 43, 55)
        m['55–59'] = band(h, 55, 60)
        m['бины E−3…E−1'] = sum(h.get(k, 0.0) for k in (p - 3, p - 2, p - 1))
        m['0–31'] = band(h, 0, 32)
        return m
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
    data = {}
    # наши CSV этой полосы
    d = os.path.join(ROOT, 'ours')
    for f in sorted(os.listdir(d)):
        m = re.match(r'ours_%s_%s_(\w+)\.csv$' % (re.escape(scene), re.escape(e)), f)
        if m:
            h, n = read_ours(os.path.join(d, f))
            data['наша ' + m.group(1)] = (h, n)
    # наши CSV П94 (eltr1, off, eltr1_detour0 …) — метка «94»: «ПОСЛЕ» П94 = «ВЫКЛ» П100 (физика 19)
    d = os.path.join(P94, 'ours')
    if os.path.isdir(d):
        for f in sorted(os.listdir(d)):
            m = re.match(r'ours_%s_%s_(\w+)\.csv$' % (re.escape(scene), re.escape(e)), f)
            if m:
                h, n = read_ours(os.path.join(d, f))
                data['наша ' + m.group(1) + '94'] = (h, n)
    # наши CSV П92 (ref, detour0 …) — метка «92»
    d = os.path.join(P92, 'ours')
    if os.path.isdir(d):
        for f in sorted(os.listdir(d)):
            m = re.match(r'ours_%s_%s_(\w+)\.csv$' % (re.escape(scene), re.escape(e)), f)
            if m:
                h, n = read_ours(os.path.join(d, f))
                data['наша ' + m.group(1) + '92'] = (h, n)
    # арбитр: своя полоса, потом П92, потом П55 (там сцена П55 без суффикса)
    sources = [(os.path.join(ROOT, 'g4out'), scene), (os.path.join(P94, 'g4'), scene), (os.path.join(P92, 'g4'), scene)]
    p55scene = 'RC103_point0' if scene == 'RC103_point0_p55' else (None if scene == 'RC103_point0' else scene)
    # наши CSV П55 (ref = HEAD физики 18 на 14.09.2026) — метка «55»; сцена П55 там без суффикса
    d = os.path.join(P55, 'ours')
    if p55scene and os.path.isdir(d):
        for f in sorted(os.listdir(d)):
            m = re.match(r'ours_%s_%s_(ref)\.csv$' % (re.escape(p55scene), re.escape(e)), f)
            if m:
                h, n = read_ours(os.path.join(d, f))
                data['наша ' + m.group(1) + '55'] = (h, n)
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
                h, n = read_g4(os.path.join(d, f))
                if h is not None:
                    data[key] = (h, n)
    return data


def sigma(v1, n1, v2, n2):
    a = v1 * n1 if n1 else 0.0
    b = v2 * n2 if n2 else 0.0
    return 100.0 * math.sqrt((1.0 / a if a > 0 else 0.0) + (1.0 / b if b > 0 else 0.0))


def cell(v, base, n_v, n_b):
    if base <= 0 or v is None:
        return '—'
    return '%+.2f ± %.1f' % (100.0 * (v / base - 1.0), sigma(v, n_v, base, n_b))


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
            row.append(cell(ms[num][k], ms[den][k], ns[num], ns[den]))
        if md:
            print('| %s | ' % k + ' | '.join(row) + ' |')
        else:
            print('%-22s' % k + ''.join('%22s' % r for r in row))


def main():
    scene, e = sys.argv[1], sys.argv[2]
    md = '--md' in sys.argv
    data = load(scene, e)
    if not data:
        print('нет данных для %s %s' % (scene, e))
        return
    p = max(max(h) for h, n in data.values())
    ms = {k: measures(h, p, float(e)) for k, (h, n) in data.items()}
    ns = {k: n for k, (h, n) in data.items()}
    print('=== %s, E = %s кэВ, бин пика %d ===' % (scene, e, p))
    print('прогоны: ' + ', '.join('%s (%s)' % (k, ns[k]) for k in data))
    keys = list(next(iter(ms.values())).keys())

    # 1. Приёмка: до (ключ ВЫКЛ = П92 ref) и после (ключ ВКЛ) против арбитра умолчанием (с возвратом e-).
    cols = [('физика 18 (П92 ref)/def', 'наша ref92', 'G4 def'), ('физика 18 (П55 ref)/def', 'наша ref55', 'G4 def'),
            ('П94 = физика 19 (eltr1)/def', 'наша eltr194', 'G4 def'),
            ('П100 ВЫКЛ (off)/def', 'наша off', 'G4 def'),
            ('П100 ВКЛ: elmix1/def', 'наша elmix1', 'G4 def'),
            ('ВКЛ без заноса: elmix1_detour0/killcarry', 'наша elmix1_detour0', 'G4 killcarry'),
            ('П94 без заноса: eltr1_detour0/killcarry', 'наша eltr1_detour094', 'G4 killcarry')]
    cols = [c for c in cols if c[1] in ms and c[2] in ms]
    table('наша сторона / арбитр − 1, % (П94 — ключ eltr=1 = физика 19 = ВЫКЛ П100; ВКЛ — elmix=1; арбитр def — с возвратом e⁻)', keys, cols, ms, ns, md)

    # 2. Что сдвинул ключ у нас и что стоит возврат у арбитра.
    cols = [('П100: elmix1/eltr1(П94)', 'наша elmix1', 'наша eltr194'), ('elmix1/off', 'наша elmix1', 'наша off'),
            ('П100 возврат: elmix1_detour0/off_detour0(П94)', 'наша elmix1_detour0', 'наша off_detour094'),
            ('П94 возврат: eltr1_detour0/off_detour0', 'наша eltr1_detour094', 'наша off_detour094'),
            ('G4 возврат: def/killesc', 'G4 def', 'G4 killesc'),
            ('G4 killesc/def', 'G4 killesc', 'G4 def'), ('G4 killcarry/def', 'G4 killcarry', 'G4 def')]
    cols = [c for c in cols if c[1] in ms and c[2] in ms]
    table('цена члена по полосам: (плечо − умолчание)/умолчание, %', keys, cols, ms, ns, md)

    # 3. Абсолютные доли на историю — для масштаба.
    print()
    print('доли на историю (G4 def / наша elmix1 / наша П94 eltr1|off):')
    ours_off = 'наша eltr194' if 'наша eltr194' in ms else ('наша off' if 'наша off' in ms else 'наша ref92')
    for k in keys:
        a = ms['G4 def'][k] if 'G4 def' in ms else float('nan')
        b = ms['наша elmix1'][k] if 'наша elmix1' in ms else float('nan')
        c = ms[ours_off][k] if ours_off in ms else float('nan')
        print('  %-22s %11.4E %11.4E %11.4E' % (k, a, b, c))


if __name__ == '__main__':
    main()
