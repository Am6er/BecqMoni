# -*- coding: utf-8 -*-
"""(`S64`, `A227`) ВЫБОР ОКНА СПИСКА КАНДИДАТОВ — на выгрузке плеча ДО.

    python o21_window.py <labels.csv> <rivals.csv>

Считает две цены по КАЖДОМУ окну: сколько ложных подписей получат рядом ВЕРНОЕ
имя (польза) и сколько истинных получат рядом ЛОЖНОЕ (засорение). Оба окна
меряются в ПШПВ пика; «гандикап» — насколько промах кандидата может быть хуже
промаха победителя (то есть спор НЕ РЕШАЕТСЯ положением), «абсолютное» — просто
промах от пика.

⛔ Кандидат обязан пройти ТЕ ЖЕ отборы, что и победитель, иначе он не мог бы
   быть подписью вовсе: порог по выходу и окно 1.5 ПШПВ. Оба взяты из
   приложения по смыслу, а не по памяти (см. `PeakDetector`).
"""
import csv, sys, collections, os
sys.path.insert(0, os.path.abspath('tools/CORPUS/scripts/c1'))
import truth
sys.stdout.reconfigure(encoding='utf-8')

MINY = 0.1          # PeakDetector.MinimumLabelYieldPercent
MAXMISS = 1.5       # PeakDetector.MaximumLabelMissInFwhm


def run(labels, rivals):
    scene = truth.load()
    lab = {}
    for r in csv.DictReader(open(labels, encoding='utf-8-sig', newline='')):
        lab[(r['spectrum'], r['peak_kev'])] = r
    riv = collections.defaultdict(list)
    for r in csv.DictReader(open(rivals, encoding='utf-8-sig', newline='')):
        riv[(r['spectrum'], r['peak_kev'])].append(r)

    def elig(c):
        I = float(c['cand_intensity_pct'])
        return (I == 0.0 or I >= MINY) and float(c['cand_miss_fwhm']) <= MAXMISS

    def names_for(k, w, hand=None, absw=None):
        cands = sorted((c for c in riv.get(k, []) if elig(c)),
                       key=lambda c: float(c['cand_miss_kev']))
        out = []
        if cands:
            base = float(cands[0]['cand_miss_fwhm'])
            for c in cands:
                m = float(c['cand_miss_fwhm'])
                if hand is not None and m - base > hand + 1e-9:
                    continue
                if absw is not None and m > absw + 1e-9:
                    continue
                if c['cand_name'] not in out:
                    out.append(c['cand_name'])
        if w in out:
            out.remove(w)
        return [w] + out

    def score(**kw):
        multi = saved = dirty = 0
        tot = n = 0
        mx = 0
        for k, r in lab.items():
            w = r['nuclide']
            if not w:
                continue
            n += 1
            sc = scene.get(k[0], set())
            v = truth.verdict(w, sc)
            nm = names_for(k, w, **kw)
            tot += len(nm)
            mx = max(mx, len(nm))
            if len(nm) > 1:
                multi += 1
            alt = nm[1:]
            if v == 'ЛОЖЬ' and any(truth.verdict(a, sc) == 'ИСТИНА' for a in alt):
                saved += 1
            if v == 'ИСТИНА' and any(truth.verdict(a, sc) == 'ЛОЖЬ' for a in alt):
                dirty += 1
        return multi, saved, dirty, tot / n, mx, n

    print('ГАНДИКАП (промах кандидата хуже промаха победителя не более чем на, ПШПВ)')
    print('%8s %9s %9s %9s %9s %7s' % ('окно', 'пиков>1', 'спасено', 'засорено', 'сред.имён', 'макс'))
    for h in (0.02, 0.05, 0.10, 0.15, 0.20, 0.30, 0.50, 1.50):
        m, s, d, a, mx, n = score(hand=h)
        print('%8.2f %9d %9d %9d %9.2f %7d' % (h, m, s, d, a, mx))
    print()
    print('АБСОЛЮТНОЕ ОКНО ОТ ПИКА (для сравнения; так меряла C1)')
    print('%8s %9s %9s %9s %9s %7s' % ('окно', 'пиков>1', 'спасено', 'засорено', 'сред.имён', 'макс'))
    for w in (0.3, 0.5, 0.75, 1.0, 1.5):
        m, s, d, a, mx, n = score(absw=w)
        print('%8.2f %9d %9d %9d %9.2f %7d' % (w, m, s, d, a, mx))
    print()
    print('подписей всего: %d' % n)


if __name__ == '__main__':
    run(sys.argv[1], sys.argv[2])
