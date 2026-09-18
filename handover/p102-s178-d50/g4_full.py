# -*- coding: utf-8 -*-
"""П102 (`S178`): арбитр по СВОДКЕ `TAGFULL` (`iontag` v3) — окно сумм против модели по множеству квантов,
поглощённых ЦЕЛИКОМ.

    python g4_full.py <g4 лог с TAGFULL/HIST> <g4_sums_eu152_off_60M.md П101> [<cf_*.log плеча>] [--half=0.5] [--side=1.5:6.5]

По каждому окну таблицы П101 (те же центры окон `ion`): брутто; «СУММА» — события, чьё множество целиком
поглощённых квантов РАВНО одному из наборов слагаемых окна (допуск 0.15 кэВ гаммы, 0.35 кэВ рентген; квант
поставки одной энергии двумя ключами — 443.96/443.97 — один); «сумма+» — то же плюс наборы, где целиком
поглощён ещё один квант (тройки, которых у модели нет либо они подпороговые); «прочее» — остальное окна;
континуум — медиана боковых полос гистограммы `ionhist` (метод П101). Модель — столбец Б таблицы П101 (HEAD
9cae3b18 = плечо А этой полосы) либо F25 из `cf_*.log`. Печатает таблицу и топ множеств «прочего».
"""
import io, re, sys, math
from collections import defaultdict
for s in (sys.stdout,):
    s.reconfigure(encoding='utf-8', errors='replace')

g4log, table = sys.argv[1], sys.argv[2]
cflog = None
half = 0.5; side0, side1 = 1.5, 6.5
for a in sys.argv[3:]:
    if a.startswith('--half='): half = float(a[7:])
    elif a.startswith('--side='): side0, side1 = [float(x) for x in a[7:].split(':')]
    elif not a.startswith('--'): cflog = a

hist = {}; binkev = None; decays = None; windows = {}
full = defaultdict(dict)
with io.open(g4log, encoding='utf-8', errors='replace') as f:
    for line in f:
        if line.startswith('HISTBEGIN'):
            m = re.search(r'bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
            binkev = float(m.group(2)); decays = int(m.group(3))
        elif line.startswith('HIST '):
            _, i, n = line.split(); hist[int(i)] = int(n)
        elif line.startswith('RESULT window='):
            m = re.search(r'window=([\d.]+) counts=(\d+)', line)
            windows[round(float(m.group(1)), 3)] = int(m.group(2))
        elif line.startswith('RESULT decays='):
            decays = int(line.split('=')[1])
        elif line.startswith('TAGFULL '):
            m = re.search(r'window=([\d.]+) n=(\d+) full=(\S+)', line)
            full[round(float(m.group(1)), 3)][m.group(3)] = int(m.group(2))
if decays is None or not full:
    print('нет RESULT decays или TAGFULL'); sys.exit(2)


def bins(lo, hi):
    k0 = int(math.floor(lo / binkev + 0.5)); k1 = int(math.floor(hi / binkev + 0.5))
    return [hist.get(k, 0) for k in range(k0, k1)]


def continuum(elo, ehi):
    if binkev is None:
        return float('nan'), 0
    lo, hi = elo - half, ehi + half
    inside = bins(lo, hi)
    side = sorted(bins(lo - side1, lo - side0) + bins(hi + side0, hi + side1))
    n = len(side)
    med = 0.5 * (side[n // 2 - 1] + side[n // 2]) if n % 2 == 0 and n > 0 else (side[n // 2] if n else 0.0)
    return med * len(inside), sum(inside)


rows = []
with io.open(table, encoding='utf-8') as f:
    for line in f:
        if not line.startswith('| ') or line.startswith('| окно'): continue
        cells = [c.strip() for c in line.strip().strip('|').split('|')]
        if len(cells) < 4: continue
        m = re.match(r'([\d.]+)(?:…([\d.]+))?', cells[0])
        if not m: continue
        elo = float(m.group(1)); ehi = float(m.group(2)) if m.group(2) else elo
        sets = []
        for part in re.findall(r'([\d.+]+)[·×]', cells[1]):
            sets.append(sorted(float(x) for x in part.split('+')))
        try:
            modelB = float(cells[3])
        except ValueError:
            continue
        if any(abs(r[0] - elo) < 1e-6 for r in rows):
            continue
        rows.append((elo, ehi, sets, modelB, cells[1]))

model_f25 = None
if cflog:
    model_f25 = defaultdict(float)
    with io.open(cflog, encoding='utf-8', errors='replace') as f:
        for line in f:
            m = re.match(r'\s*([\d.]+)\s*=\s*([\d.+]+)\s+Eu-152\s+площадь\s+([\d.E+-]+)', line)
            if m:
                key = tuple(sorted(float(x) for x in m.group(2).split('+')))
                model_f25[key] = max(model_f25[key], float(m.group(3)))


def same(es, st):
    """Множество целиком поглощённых es равно набору st (с допусками)."""
    if len(es) != len(st):
        return False
    used = [False] * len(es)
    for e in st:
        tol = 0.35 if e < 100 else 0.15
        hit = -1
        for k, v in enumerate(es):
            if not used[k] and abs(v - e) < tol:
                hit = k; break
        if hit < 0:
            return False
        used[hit] = True
    return True


def superset(es, st):
    if len(es) <= len(st):
        return False
    used = [False] * len(es)
    for e in st:
        tol = 0.35 if e < 100 else 0.15
        hit = -1
        for k, v in enumerate(es):
            if not used[k] and abs(v - e) < tol:
                hit = k; break
        if hit < 0:
            return False
        used[hit] = True
    return True


print('Geant4 `iontag` v3 (`TAGFULL`): распадов %d; окно ±%.1f кэВ; континуум боковыми полосами %.1f…%.1f кэВ (медиана); модель — %s'
      % (decays, half, side0, side1, ('F25 ' + cflog) if cflog else 'таблица П101, столбец Б (HEAD 9cae3b18)'))
print()
print('| окно, кэВ | слагаемые (П101) | брутто | СУММА (множество = набор) | сумма+ (ещё один целиком) | прочее | континуум полосами | Geant4 сумма, на распад ± σ | модель | Δ % (σ) | нетто полосами (метод П101) | Δ к нему % |')
print('|---|---|---|---|---|---|---|---|---|---|---|---|')
others = {}
sum_rows = []
for elo, ehi, sets, modelB, label in rows:
    centers = [w for w in windows if elo - 0.05 <= w <= ehi + 0.05]
    if not centers: continue
    gross = sum(windows[w] for w in centers)
    n_sum = 0; n_plus = 0; other = defaultdict(int)
    seen_keys = set()
    for w in centers:
        for key, n in full.get(w, {}).items():
            es = [float(x) for x in key.split('+')] if key != '-' else []
            # окна перекрываются (284.2/284.8): одно и то же событие у двух центров — множество+окно как ключ нельзя
            # различить, но истинные суммы лежат ровно в одном окне (|E_сум − центр| < 0.5 только у своего)
            if any(same(es, st) for st in sets):
                n_sum += n
            elif any(superset(es, st) for st in sets):
                n_plus += n
            else:
                other[key] += n
    cont, gross_h = continuum(elo, ehi)
    if model_f25 is not None:
        mB = sum(model_f25.get(tuple(st), 0.0) for st in sets)
    else:
        mB = modelB
    eps = n_sum / decays; sig_eps = math.sqrt(max(n_sum, 1)) / decays
    d = 100.0 * (mB / eps - 1.0) if eps > 0 else float('nan')
    dsig = (mB - eps) / sig_eps if sig_eps > 0 else float('nan')
    net = (gross_h - cont) / decays
    dnet = 100.0 * (mB / net - 1.0) if net > 0 else float('nan')
    print('| %s | %s | %d | **%d** | %d | %d | %.0f | %.3e ± %.1e | %.4e | %+.1f (%+.1f) | %.3e | %+.1f |'
          % (('%.1f' % elo) if ehi == elo else ('%.1f…%.1f' % (elo, ehi)), label, gross, n_sum, n_plus, gross - n_sum - n_plus, cont,
             eps, sig_eps, mB, d, dsig, net, dnet))
    others[(elo, ehi)] = sorted(other.items(), key=lambda kv: -kv[1])[:6]
    sum_rows.append((elo, ehi, n_sum, mB, eps, sig_eps, d, dsig))
print()
print('## Что сидит в окнах помимо суммы (топ множеств целиком поглощённых у «прочего», n на %d распадов; «-» — ни один квант целиком)' % decays)
for (elo, ehi), lst in others.items():
    if not lst: continue
    print('- %s: %s' % (('%.1f' % elo) if ehi == elo else ('%.1f…%.1f' % (elo, ehi)),
                        '; '.join('%s ×%d' % (s, n) for s, n in lst)))
