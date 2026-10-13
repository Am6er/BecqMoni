# П184 (30.09.2026), S197: окна вокруг линии, «когерентное ВКЛ/ВЫКЛ» у обеих сторон.
# Наша сторона — CSV G4RawProbe (последняя строка = взвешенный пик, остальное — аналоговый
# континуум с весом 1/N на историю), ошибка пика — из .txt («ошибка взвешенной ветки»),
# континуум — Пуассон по числу историй. Несколько прогонов одного плеча сливаются с весом N.
# G4 — гистограммы g4cf (отсчёты), логи одного плеча суммируются.
# python ana.py <E> <w1,w2,...> on_ours=a.csv,b.csv off_ours=... on_g4=x.log,y.log off_g4=...
import sys, re, math

ANALOG = '--analog' in sys.argv
sys.argv = [a for a in sys.argv if a != '--analog']
E = float(sys.argv[1])
ws = [float(x) for x in sys.argv[2].split(',')]
arms = {}
for a in sys.argv[3:]:
    k, v = a.split('=', 1)
    arms[k] = v.split(',')


def our_run(csv):
    rows = []
    for line in open(csv, encoding='utf-8-sig').read().splitlines()[1:]:
        k, v = line.split(',')[:2]
        rows.append((float(k), float(v)))
    txt = open(csv[:-4] + '.txt', encoding='utf-8', errors='replace').read()
    n = int(re.search(r'историй (\d+),', txt).group(1))
    perr = float(re.search(r'ошибка взвешенной ветки ([\d.]+) %', txt).group(1)) / 100.0
    code = re.findall(r'code=(\d+)', txt)
    assert code and code[-1] == '0', (csv, code)
    if ANALOG:  # пик — аналоговой ветви (все порядки рассеяния), ошибка — Пуассон по попаданиям
        m = re.search(r'пик аналоговой ветви ([\d.E+-]+) \((\d+) историй', txt)
        rows[-1] = (rows[-1][0], float(m.group(1)))
        perr = 1.0 / math.sqrt(int(m.group(2)))
    return rows, n, perr


def our_arm(csvs):
    runs = [our_run(c) for c in csvs]
    N = sum(r[1] for r in runs)
    out = {}
    for w in ws + [None]:
        val = var = 0.0
        for rows, n, perr in runs:
            peak = rows[-1][1]
            cont = [v for k, v in rows[:-1] if (w is None or E - w <= k < E)]
            c = sum(cont)
            f = n / N
            val += f * (peak + c)
            var += f * f * ((peak * perr) ** 2 + c / n)
        out[w] = (val, math.sqrt(var))
    return out, N


def g4_arm(logs):
    h = {}
    dec = 0
    bk = None
    for p in logs:
        for l in open(p, encoding='utf-8', errors='replace'):
            m = re.match(r'HISTBEGIN bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', l)
            if m:
                bk = float(m.group(2))
                dec += int(m.group(3))
                continue
            m = re.match(r'HIST\s+(\d+)\s+(\d+)\s*$', l)
            if m:
                h[int(m.group(1))] = h.get(int(m.group(1)), 0) + int(m.group(2))
    out = {}
    for w in ws + [None]:
        c = sum(v for i, v in h.items() if w is None or (E - w <= i * bk <= E + 0.5 * bk + 1e-9))
        out[w] = (c / dec, math.sqrt(c) / dec)
    return out, dec


res = {}
for arm in ('on', 'off'):
    if f'{arm}_ours' in arms and f'{arm}_g4' in arms:
        o, N = our_arm(arms[f'{arm}_ours'])
        g, D = g4_arm(arms[f'{arm}_g4'])
        res[arm] = {}
        print(f'плечо {arm}: наша {N:.3g} историй, G4 {D:.3g}')
        for w in ws + [None]:
            r = o[w][0] / g[w][0]
            s = r * math.hypot(o[w][1] / o[w][0], g[w][1] / g[w][0])
            res[arm][w] = (r, s, o[w], g[w])
            name = 'полная' if w is None else f'[E-{w:.3f}, E]'
            print(f'  {name:18s} наша {o[w][0]:.5e} ± {100 * o[w][1] / o[w][0]:.2f} %  '
                  f'G4 {g[w][0]:.5e} ± {100 * g[w][1] / g[w][0]:.2f} %  наша/G4 {r:.4f} ± {100 * s:.2f} %')
if 'on' in res and 'off' in res:
    print('ВЫКЛ − ВКЛ (разность отношений наша/G4) и вклад когерентного у каждой стороны (ВКЛ/ВЫКЛ − 1):')
    for w in ws + [None]:
        a, b = res['on'][w], res['off'][w]
        d = b[0] - a[0]
        sd = math.hypot(a[1], b[1])
        co = a[2][0] / b[2][0] - 1
        cg = a[3][0] / b[3][0] - 1
        name = 'полная' if w is None else f'[E-{w:.3f}, E]'
        print(f'  {name:18s} R_off − R_on = {100 * d:+.2f} ± {100 * sd:.2f} п.п.   '
              f'когерентное даёт: наша {100 * co:+.2f} %, G4 {100 * cg:+.2f} %')
