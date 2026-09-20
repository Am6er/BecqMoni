# -*- coding: utf-8 -*-
"""П101 (`S177`): арбитр Geant4 по сумм-пикам Eu-152 на `G1S_point5` — гистограмма `ionhist`
против перечня F25 обоих плеч (А — HEAD, Б — правка).

    python g4_sums.py <g4 лог с HIST> <cf_p5_a_ang0.log> <cf_p5_b_ang0.log> [--nuclide=Eu-152] [--half=0.5] [--side=1.5:6.5]

Наши суммы читаются из раздела F25 («E сумм, кэВ  слагаемые  нуклид  площадь»; и «не в образе»),
энергия окна у Geant4 — СУММА ИСТИННЫХ энергий слагаемых (у нас печатается видимая, по свету).
Суммы, чьи истинные энергии лежат ближе 1.0 кэВ, сливаются в одно окно (Geant4 их не различит:
1408+40.12 и 1408+39.52). Окно ±half кэВ; континуум — МЕДИАНА бинов боковых полос
[E−side1, E−side0] ∪ [E+side0, E+side1] на бин (соседний пик в полосе медиану не сдвигает; среднее
сдвигало: у окна 1126 в левой полосе стоял пик 1123.2), умноженная на число бинов окна, вычитается.
Окно ближе 1.2 кэВ к ЛИНИИ нуклида (из раскладки CF) помечается «в линии» и в сводку не идёт: у Geant4
сумма внутри окна линии от линии неотделима (у нас такая сумма стоит отдельно, потому что видимая
энергия по свету ушла из окна линии, либо учтена влётом). Печатает таблицу «сумма — А — Б — Geant4
нетто ± σ — Δ_A % — Δ_Б % (σ-ед.)» и сводку по окнам вне линий.
"""
import io, re, sys, math
for s in (sys.stdout,): s.reconfigure(encoding='utf-8', errors='replace')

g4log, alog, blog = sys.argv[1], sys.argv[2], sys.argv[3]
nuclide = 'Eu-152'; half = 0.5; side0, side1 = 1.5, 6.5
for a in sys.argv[4:]:
    if a.startswith('--nuclide='): nuclide = a[10:]
    elif a.startswith('--half='): half = float(a[7:])
    elif a.startswith('--side='): side0, side1 = [float(x) for x in a[7:].split(':')]

# --- Geant4 ---
hist = {}; binkev = None; decays = None; windows = {}
with io.open(g4log, encoding='utf-8', errors='replace') as f:
    for line in f:
        if line.startswith('HISTBEGIN'):
            m = re.search(r'bins=(\d+) bin_kev=([\d.]+) decays=(\d+)', line)
            binkev = float(m.group(2)); decays = int(m.group(3))
        elif line.startswith('HIST '):
            _, i, n = line.split(); hist[int(i)] = int(n)
        elif line.startswith('RESULT window='):
            m = re.search(r'window=([\d.]+) counts=(\d+) eps=([\d.e+-]+)', line)
            windows[float(m.group(1))] = (int(m.group(2)), float(m.group(3)))
        elif line.startswith('RESULT decays='):
            decays = int(line.split('=')[1])
if binkev is None:
    print('в логе нет HISTBEGIN — гистограмма не писалась'); sys.exit(2)


def bins(lo, hi):
    """Бины гистограммы на [lo, hi) кэВ: бин k покрывает [(k−0.5)·шаг, (k+0.5)·шаг)."""
    k0 = int(math.floor(lo / binkev + 0.5)); k1 = int(math.floor(hi / binkev + 0.5))
    return [hist.get(k, 0) for k in range(k0, k1)]


def window(elo, ehi):
    lo, hi = elo - half, ehi + half
    inside = bins(lo, hi)
    gross = sum(inside)
    side = sorted(bins(lo - side1, lo - side0) + bins(hi + side0, hi + side1))
    n = len(side)
    med = 0.5 * (side[n // 2 - 1] + side[n // 2]) if n % 2 == 0 and n > 0 else (side[n // 2] if n else 0.0)
    cont = med * len(inside)
    net = gross - cont
    # σ: пуассон брутто плюс погрешность медианы ≈ 1.25·sqrt(med/n) на бин
    sigma = math.sqrt(gross + (1.25 * math.sqrt(max(med, 1.0) / max(n, 1)) * len(inside)) ** 2)
    return gross, cont, net, sigma


# --- наши суммы (F25) ---
SUM_RE = re.compile(r'\s+([\d.]+)\s+([\d.+]+)\s+(\S+)\s+([\d.E+-]+)(\s+\(не в образе: (\S+)\))?')
CF_RE = re.compile(r'\s+([\d.]+)\s+(\S+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.E+-]+)\s*$')


def f25(path):
    out = {}; lines = []
    comp = None
    with io.open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            m = re.match(r'компонент: (\S+)', line.strip())
            if m: comp = m.group(1); continue
            m = CF_RE.match(line)
            if m and m.group(2) == nuclide:
                lines.append(float(m.group(1)))
                continue
            if comp != nuclide: continue
            m = SUM_RE.match(line)
            if m and m.group(3) == nuclide:
                parts = [float(x) for x in m.group(2).split('+')]
                key = m.group(2)
                out[key] = dict(apparent=float(m.group(1)), parts=parts, true=sum(parts),
                                area=float(m.group(4)), dropped=m.group(6))
    return out, lines


A, LINES = f25(alog); B, _ = f25(blog)


def near_line(elo, ehi, tol=1.2):
    return [e for e in LINES if elo - tol < e < ehi + tol]


keys = sorted(set(A) | set(B), key=lambda k: (A.get(k) or B.get(k))['true'])
# слить суммы ближе 1.0 кэВ по истинной энергии
groups = []
for k in keys:
    e = (A.get(k) or B.get(k))['true']
    if groups and e - groups[-1]['hi'] < 1.0:
        groups[-1]['keys'].append(k); groups[-1]['hi'] = e
    else:
        groups.append(dict(keys=[k], lo=e, hi=e))

print('Geant4: распадов %d, шаг гистограммы %.3f кэВ; окно ±%.1f кэВ, боковые полосы %.1f…%.1f кэВ (медиана); нуклид %s; линий в раскладке CF %d' % (decays, binkev, half, side0, side1, nuclide, len(LINES)))
print('суммы А %d, Б %d, окон %d (сливались суммы ближе 1.0 кэВ)\n' % (len(A), len(B), len(groups)))
print('| окно, кэВ | слагаемые (в образе Б: «·», срез/порог: «×») | А, на распад | Б, на распад | Geant4 брутто / континуум | Geant4 нетто ± σ | Δ_A % | Δ_Б % | Δ_Б, σ |')
print('|---|---|---|---|---|---|---|---|---|')
rows = []
for g in groups:
    ea = sum(A[k]['area'] for k in g['keys'] if k in A)
    eb = sum(B[k]['area'] for k in g['keys'] if k in B)
    gross, cont, net, sigma = window(g['lo'], g['hi'])
    eg = net / decays; sg = sigma / decays
    labels = []
    for k in g['keys']:
        d = B.get(k) or A.get(k)
        labels.append(k + ('×' if d.get('dropped') else '·'))
    da = (ea / eg - 1.0) * 100.0 if eg > 0 else float('nan')
    db = (eb / eg - 1.0) * 100.0 if eg > 0 else float('nan')
    ns = (eb - eg) / sg if sg > 0 else float('nan')
    inline = near_line(g['lo'], g['hi'])
    rows.append((g, labels, ea, eb, gross, cont, eg, sg, da, db, ns, inline))
    print('| %.1f%s | %s%s | %.4e | %.4e | %d / %.0f | %.3e ± %.1e | %+.1f | %+.1f | %+.1f |' % (
        g['lo'], ('…%.1f' % g['hi']) if g['hi'] - g['lo'] > 0.05 else '', ' '.join(labels),
        (' **в линии %s**' % '/'.join('%.1f' % e for e in inline)) if inline else '',
        ea, eb, gross, cont, eg, sg, da, db, ns))

print('\nСводка по окнам ВНЕ линий нуклида (±1.2 кэВ), Geant4 нетто > 5σ, площадь Б > 1e-6:')
tot_a = tot_b = tot_g = 0.0; n = 0; chi_a = chi_b = 0.0
print('| окно, кэВ | слагаемые | А | Б | Geant4 ± σ | Δ_A % | Δ_Б % | Δ_Б, σ |')
print('|---|---|---|---|---|---|---|---|')
unmodeled = []
for g, labels, ea, eb, gross, cont, eg, sg, da, db, ns, inline in rows:
    if not inline and sg > 0 and eg > 5 * sg and eb > 1e-6:
        if eb < 0.5 * eg:
            # Geant4 держит вдвое больше, чем модель: это не сумма, а квант, которого в
            # библиотеке нет (слабая линия схемы PhotonEvaporation вне decay_radiations).
            unmodeled.append((g, labels, eb, eg, sg))
            continue
        tot_a += ea; tot_b += eb; tot_g += eg; n += 1
        chi_a += ((ea - eg) / sg) ** 2; chi_b += ((eb - eg) / sg) ** 2
        print('| %.1f%s | %s | %.4e | %.4e | %.3e ± %.1e | %+.1f | %+.1f | %+.1f |' % (
            g['lo'], ('…%.1f' % g['hi']) if g['hi'] - g['lo'] > 0.05 else '', ' '.join(labels), ea, eb, eg, sg, da, db, ns))
for g, labels, eb, eg, sg in unmodeled:
    print('| %.1f%s | %s | — | %.4e | %.3e ± %.1e | | | **вне сводки: Geant4 держит вдвое больше — квант вне библиотеки** |' % (
        g['lo'], ('…%.1f' % g['hi']) if g['hi'] - g['lo'] > 0.05 else '', ' '.join(labels), eb, eg, sg))
print('\n  окон %d: Σ А %.4e, Σ Б %.4e, Σ Geant4 %.4e; Σ(Δ/σ)² А %.1f, Б %.1f; Σ_A/Σ_G4 %+.2f %%, Σ_Б/Σ_G4 %+.2f %%' % (
    n, tot_a, tot_b, tot_g, chi_a, chi_b, (tot_a / tot_g - 1) * 100 if tot_g else float('nan'), (tot_b / tot_g - 1) * 100 if tot_g else float('nan')))

if windows:
    print('\nОкна RESULT самого Geant4 (±0.5 кэВ, без вычитания континуума) — сверка с П85:')
    for e in sorted(windows):
        c, eps = windows[e]
        gross, cont, net, sigma = window(e, e)
        print('  %.1f: counts=%d eps=%.4e; по гистограмме брутто %d, континуум %.0f, нетто %.4e' % (e, c, eps, gross, cont, net / decays))
