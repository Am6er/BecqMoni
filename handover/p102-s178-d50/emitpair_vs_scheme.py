# -*- coding: utf-8 -*-
"""П102 (`S178`): условные вероятности пар P(B | A) — ход по схеме уровней (`ref_pairs.csv` П90 = `CascadePairProbe`
= `FsaCascadeSummer.RectifyBySchemes`) против ПРЯМОГО счёта по эмиссии Geant4 (`g4cf emitpair`: доля событий с
квантом A, у которых есть и квант B; допуск ±0.35 кэВ, детектор не участвует).

    python emitpair_vs_scheme.py <ref_pairs.csv> <g4 лог с EMITPAIR> <out.md>

⚠ Две оговорки, обе названы в таблице: (1) у Geant4 линии 443.961 (1529.8→1085.8) и 443.974 (810.45→366.5)
в один допуск — их условные СМЕШАНЫ с весами 2.825/0.292; схемная смесь печатается рядом; (2) для B-рентгена
Geant4 считает ПРИСУТСТВИЕ (≥ 1 кванта), модель — ОЖИДАЕМОЕ ЧИСЛО; при двух источниках вакансии (захват +
конверсия партнёра) присутствие меньше ожидания на произведение — такие строки помечены.
"""
import io, sys, csv, re, math
for s in (sys.stdout,):
    s.reconfigure(encoding='utf-8', errors='replace')
ref, g4log, out = sys.argv[1], sys.argv[2], sys.argv[3]
G4E = {39.522: 39.56, 40.117: 40.17, 45.523: 45.40, 46.575: 46.65}


def g4e(e):
    for k, v in G4E.items():
        if abs(e - k) < 0.05:
            return v
    return e


scheme = {}
lines = {}
for r in csv.reader(io.open(ref, encoding='utf-8'), delimiter=';'):
    if r[0] != '152EU':
        continue
    scheme[(float(r[1]), float(r[2]))] = float(r[3])
g4 = {}
for line in io.open(g4log, encoding='utf-8', errors='replace'):
    m = re.match(r'EMITPAIR ([\d.]+) ([\d.]+) n1=(\d+) n12=(\d+) P=([\d.]+)', line)
    if m:
        g4[(round(float(m.group(1)), 3), round(float(m.group(2)), 3))] = (int(m.group(3)), int(m.group(4)), float(m.group(5)))
    m = re.match(r'RESULT decays=(\d+)', line)
    if m:
        decays = int(m.group(1))

# смесь двух линий 444 у Geant4: I(443.961)=2.825 %, I(443.974)=0.292 % (эмиссия Geant4 20 млн)
W1, W2 = 2.825, 0.292
rows = []
for (a, b), p in sorted(scheme.items()):
    key = (round(g4e(a), 3), round(g4e(b), 3))
    if key not in g4:
        continue
    n1, n12, pg = g4[key]
    sig = math.sqrt(max(n12, 1)) / n1 if n1 else float('nan')
    note = ''
    pm = p
    if abs(a - 443.96) < 0.02 or abs(a - 443.965) < 0.02:
        p1 = scheme.get((443.965, b), 0.0); p2 = scheme.get((443.960, b), 0.0)
        pm = (W1 * p1 + W2 * p2) / (W1 + W2)
        note = 'A — смесь 443.961/443.974 у Geant4; схема взвешена 2.825/0.292'
    if b < 100:
        note = (note + '; ' if note else '') + 'B — рентген: у Geant4 присутствие, у схемы ожидание'
    if p < 0:
        note = (note + '; ' if note else '') + 'доля осталась поставочной'
        pm = float('nan')
    d = 100.0 * (pm / pg - 1.0) if pg > 0 and pm == pm else float('nan')
    dsig = (pm - pg) / sig if sig > 0 and pm == pm else float('nan')
    rows.append((a, b, pm, pg, sig, n1, n12, d, dsig, note))

md = ['# `S178` — условные пар P(B | A): схема уровней против эмиссии Geant4 (П102, 18.09.2026)', '',
      'Geant4 `emitpair`, распадов %d, зерно 7; схема — `handover/p90-s176/ref_pairs.csv` (П90, = `CascadePairProbe`).' % decays, '',
      '| A, кэВ | B, кэВ | P схемы | P Geant4 ± σ | n(A) | n(A∧B) | Δ схема/Geant4, % | Δ/σ | оговорка |',
      '|---|---|---|---|---|---|---|---|---|']
big = 0
for a, b, pm, pg, sig, n1, n12, d, dsig, note in rows:
    md.append('| %.3f | %.3f | %s | %.4f ± %.4f | %d | %d | %s | %s | %s |' % (
        a, b, ('%.4f' % pm) if pm == pm else '—', pg, sig, n1, n12,
        ('%+.1f' % d) if d == d else '—', ('%+.1f' % dsig) if dsig == dsig else '—', note))
    if dsig == dsig and abs(dsig) > 3 and not note:
        big += 1
md.append('')
md.append('Пар в таблице %d; расхождений > 3σ без оговорок: %d.' % (len(rows), big))
io.open(out, 'w', encoding='utf-8').write('\n'.join(md) + '\n')
print('\n'.join(md))
