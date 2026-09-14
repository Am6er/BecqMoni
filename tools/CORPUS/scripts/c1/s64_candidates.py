# -*- coding: utf-8 -*-
"""`S64`: ЧТО ДАЁТ КАЖДЫЙ ИЗ ТРЁХ КАНДИДАТОВ — счётом по корпусу.

    python s64_candidates.py <labels.csv> <rivals.csv>

Ничего не выбирает. Считает цену и выручку каждого способа, названного в
строке реестра, на всём корпусе и поимённо на семи спектрах, которые строка
называет.
"""
import csv, sys, os, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import truth

NAMED_AM = ['G1S16_Am241_P25', 'ASN16_Am241', 'ASN8_Am241', 'G1S24_Am241_P5']
NAMED_K = ['ASN16_BrazilNuts', 'ASN3_Tile', 'ASN8_UGlass']


def load(p):
    with open(p, encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))


def parent(name):
    """Родитель подписи: `Am-241/x-rays` -> `Am-241`, `Tl-208 SE` -> `Tl-208`."""
    n = name.split('/')[0].strip()
    for suf in (' SE', ' DE'):
        if n.endswith(suf):
            n = n[:-len(suf)]
    return n


def main(plab, priv):
    scene = truth.load()
    labels = load(plab)
    rivals = load(priv)
    lab = {(r['spectrum'], r['peak_kev']): r for r in labels}

    # соперники по пикам, с окном
    by_peak = collections.defaultdict(list)
    for r in rivals:
        by_peak[(r['spectrum'], r['peak_kev'])].append(r)

    def names_in(k, w):
        return {r['cand_name'] for r in by_peak.get(k, [])
                if float(r['cand_miss_fwhm']) <= w}

    print('=== КАНДИДАТ 1: подписывать НЕСКОЛЬКИМИ именами ===')
    print('сколько РАЗНЫХ имён попадает в один пик (окно — доля ПШПВ пика)')
    print('%-8s %8s %8s %8s %8s %8s' % ('окно', '1 имя', '2', '3', '4+', 'всего'))
    for w in (0.5, 1.0, 1.5, 2.0):
        c = collections.Counter()
        for k in lab:
            n = len(names_in(k, w))
            c[min(n, 4) if n < 4 else 4] += 1
        print('%-8s %8d %8d %8d %8d %8d'
              % (('%.1f ПШПВ' % w), c[1], c[2], c[3], c[4], sum(c.values())))

    print()
    print('=== КАНДИДАТ 2: у родителя должны быть ПРОЧИЕ его линии в спектре ===')
    print('для каждого СПОРНОГО пика (>=2 имён в 0.5 ПШПВ) — сколько ДРУГИХ пиков')
    print('того же спектра подписаны тем же родителем')
    helped = tie = 0
    rows = []
    for k in sorted(lab):
        cand = names_in(k, 0.5)
        if len(cand) < 2:
            continue
        sp = k[0]
        others = collections.Counter()
        for kk, rr in lab.items():
            if kk[0] != sp or kk == k or not rr['nuclide']:
                continue
            others[parent(rr['nuclide'])] += 1
        sup = {c: others.get(parent(c), 0) for c in cand}
        vals = sorted(sup.values(), reverse=True)
        if vals[0] > vals[1]:
            helped += 1
        else:
            tie += 1
        rows.append((sp, k[1], lab[k]['nuclide'], sup))
    print('  спорных пиков: %d ; родителя РАЗЛИЧАЕТ: %d ; НИЧЬЯ (все по нулю или поровну): %d'
          % (len(rows), helped, tie))

    print()
    print('=== КАНДИДАТ 3: штраф элементному рентгену без элемента в сцене ===')
    mats = {}
    with open(os.path.join(truth._CORPUS, 'materials.csv'),
              encoding='utf-8-sig', newline='') as fh:
        for r in csv.DictReader(fh):
            els = set()
            for col in ('crystal', 'sample', 'shield'):
                for e in (r.get(col) or '').split(';'):
                    if e.strip():
                        els.add(e.strip())
            mats[r['spectrum']] = els
    cnt = collections.Counter()
    for r in labels:
        nm = r['nuclide']
        if not nm or 'x-ray' not in nm.lower():
            continue
        el = nm.split()[0]
        cnt[(el, el in mats.get(r['spectrum'], set()))] += 1
    for (el, present), n in sorted(cnt.items()):
        print('  %-6s элемент В СЦЕНЕ: %-3s подписей %3d'
              % (el, 'да' if present else 'НЕТ', n))

    print()
    print('=== СПЕКТРЫ, НАЗВАННЫЕ В СТРОКЕ ===')
    for sp in NAMED_AM + NAMED_K:
        peaks = sorted([k for k in lab if k[0] == sp], key=lambda x: float(x[1]))
        print('--- %s (пиков %d) ---' % (sp, len(peaks)))
        for k in peaks:
            r = lab[k]
            e = float(k[1])
            if not (50 <= e <= 70 or 1380 <= e <= 1560):
                continue
            cand = sorted(by_peak.get(k, []), key=lambda x: float(x['cand_miss_kev']))
            print('    пик %9.3f ПШПВ %7.3f -> %s %s'
                  % (e, float(r['fwhm_kev']), r['nuclide'] or '(нет)', r['line_kev']))
            for c in cand:
                print('        кандидат %-14s %9s I=%-8s промах %7s кэВ = %s ПШПВ'
                      % (c['cand_name'], c['cand_kev'], c['cand_intensity_pct'],
                         c['cand_miss_kev'], c['cand_miss_fwhm']))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
