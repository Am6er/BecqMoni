# -*- coding: utf-8 -*-
"""П102 (`S178`): читатель подписей `iontag` арбитра Geant4 — ЧЕМ заполнено окно сумм.

    python g4_tags.py <g4 лог с TAG/HIST> <g4_sums_eu152_off_60M.md П101> [<cf_*.log плеча Б>] [--half=0.5] [--side=1.5:6.5]

По каждому окну `ion` (те же центры, что в таблице П101): брутто по `RESULT window=`, «истинная сумма» —
события, у которых МНОЖЕСТВО квантов, поглощённых целиком (признак `*` подписи `iontag` v2: вклад
потомков кванта = E ± 0.5 кэВ), совпадает с одним из наборов слагаемых этого окна из таблицы П101
(допуск 0.15 кэВ гаммы, 0.35 кэВ рентген; кванты без `*` — ушли целиком, раз полный вклад в окне;
у подписей БЕЗ признаков (v1) — старое правило «содержит все слагаемые»),
«прочее» — остальные события окна (континуум и пики вылета чужих сумм), континуум по боковым полосам
гистограммы `ionhist` того же прогона (медиана бинов [E−6.5, E−1.5] ∪ [E+1.5, E+6.5] на бин × бинов
окна — метод `g4_sums.py` П101), и модель Б (столбец «Б, на распад» таблицы П101 = HEAD 9cae3b18; при
`cf_*.log` — площади F25 этого лога, суммированные по тем же наборам). Печатает таблицу и топ подписей
«прочего» по окнам — они и называют, что сидит под пиком помимо суммы.
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

# --- Geant4: RESULT, HIST, TAG ---
hist = {}; binkev = None; decays = None; windows = {}
tags = defaultdict(dict)   # окно -> подпись -> n
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
        elif line.startswith('TAGSIG '):
            m = re.search(r'window=([\d.]+) n=(\d+) sig=(\S+)', line)
            tags[round(float(m.group(1)), 3)][m.group(3)] = int(m.group(2))
if decays is None:
    print('нет RESULT decays'); sys.exit(2)


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


# --- таблица П101: окно -> наборы слагаемых и модель Б ---
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
            sets.append([float(x) for x in part.split('+')])
        try:
            modelB = float(cells[3])
        except ValueError:
            continue
        if any(abs(r[0] - elo) < 1e-6 for r in rows):
            continue                      # сводка таблицы П101 повторяет строки — первая встреча
        rows.append((elo, ehi, sets, modelB, cells[1]))

# --- модель из cf-лога плеча (F25), если дан ---
model_f25 = None
if cflog:
    model_f25 = defaultdict(float)
    with io.open(cflog, encoding='utf-8', errors='replace') as f:
        for line in f:
            m = re.match(r'\s*([\d.]+)\s*=\s*([\d.+]+)\s+Eu-152\s+площадь\s+([\d.E+-]+)', line)
            if m:
                key = tuple(sorted(float(x) for x in m.group(2).split('+')))
                model_f25[key] = max(model_f25[key], float(m.group(3)))


def matches(sig, sets):
    if sig == '-':
        return False
    parts = sig.split('+')
    v2 = any(p.endswith('*') for p in parts)
    if v2:
        es = [float(p[:-1]) for p in parts if p.endswith('*')]   # только поглощённые целиком
    else:
        es = [float(p) for p in parts]
    for st in sets:
        used = [False] * len(es)
        ok = True
        for e in st:
            tol = 0.35 if e < 100 else 0.15
            hit = -1
            for k, v in enumerate(es):
                if not used[k] and abs(v - e) < tol:
                    hit = k; break
            if hit < 0:
                ok = False; break
            used[hit] = True
        if ok and (not v2 or all(used)):      # v2: лишних целиком поглощённых быть не должно
            return True
    return False


print('Geant4 `iontag`: распадов %d; окно ±%.1f кэВ; континуум боковыми полосами %.1f…%.1f кэВ (медиана); модель Б — %s'
      % (decays, half, side0, side1, ('F25 ' + cflog) if cflog else 'таблица П101 (HEAD 9cae3b18)'))
print()
print('| окно, кэВ | слагаемые (П101) | брутто | СУММА по подписи | прочее | континуум полосами | Geant4 сумма, на распад ± σ | модель Б | Δ_Б % (σ) | «нетто полосами» П101-методом | Δ_Б к нему % |')
print('|---|---|---|---|---|---|---|---|---|---|---|')
others = {}
for elo, ehi, sets, modelB, label in rows:
    centers = [w for w in windows if elo - 0.05 <= w <= ehi + 0.05]
    if not centers: continue
    gross = sum(windows[w] for w in centers)
    n_sum = 0; other = defaultdict(int)
    for w in centers:
        for sig, n in tags.get(w, {}).items():
            if matches(sig, sets): n_sum += n
            else: other[sig] += n
    cont, gross_h = continuum(elo, ehi)
    if model_f25 is not None:
        mB = sum(model_f25.get(tuple(sorted(st)), 0.0) for st in sets)
    else:
        mB = modelB
    eps = n_sum / decays; sig_eps = math.sqrt(max(n_sum, 1)) / decays
    d = 100.0 * (mB / eps - 1.0) if eps > 0 else float('nan')
    dsig = (mB - eps) / sig_eps if sig_eps > 0 else float('nan')
    net = (gross_h - cont) / decays
    dnet = 100.0 * (mB / net - 1.0) if net > 0 else float('nan')
    print('| %s | %s | %d | **%d** | %d | %.0f | %.3e ± %.1e | %.4e | %+.1f (%+.1f) | %.3e | %+.1f |'
          % (('%.1f' % elo) if ehi == elo else ('%.1f…%.1f' % (elo, ehi)), label, gross, n_sum, gross - n_sum, cont,
             eps, sig_eps, mB, d, dsig, net, dnet))
    others[(elo, ehi)] = sorted(other.items(), key=lambda kv: -kv[1])[:6]
print()
print('## Что сидит в окнах помимо суммы (топ подписей «прочего», n на %d распадов)' % decays)
for (elo, ehi), lst in others.items():
    if not lst: continue
    print('- %s: %s' % (('%.1f' % elo) if ehi == elo else ('%.1f…%.1f' % (elo, ehi)),
                        '; '.join('%s ×%d' % (s, n) for s, n in lst)))
