# -*- coding: utf-8 -*-
"""Положительный контроль правок `A196`/`A197` по корпусу (полоса O1).

    python o1_controls.py <labels_before.csv> <labels_after.csv>

Три плеча:
  1. ЗАКОННЫЕ `Pa-234m` 1001.0 в сценах с объявленным ураном обязаны ОСТАТЬСЯ,
     а в сценах без урана — уйти (`A196`);
  2. ИСТИННЫЕ подписи поимённо по нуклидам: ни одна не должна убыть (`A197`);
  3. заведомо плохой вход: подпись, у чьего нуклида в спектре нет ни одной
     другой своей линии, — до правки стоит, после снята.
"""
import csv, sys, os, collections

_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.normpath(os.path.join(_HERE, '..', '..'))
sys.path.insert(0, os.path.join(_REPO, 'tools', 'CORPUS', 'scripts', 'c1'))
import truth


def load(p):
    with open(p, encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))


def main(pb, pa):
    scene = truth.load()
    before, after = load(pb), load(pa)
    bi = {(r['spectrum'], r['peak_kev']): r for r in before}
    ai = {(r['spectrum'], r['peak_kev']): r for r in after}

    print('=== ПЛЕЧО 1: `Pa-234m` 1001.0 — уран объявлен / не объявлен ===')
    kept = gone = wrong = 0
    for k in sorted(bi):
        b = bi[k]
        if b['nuclide'] != 'Pa-234m' or abs(float(b['line_kev'] or 0) - 1001.0) > 0.5:
            continue
        a = ai.get(k, {'nuclide': ''})
        uran = 'Pa-234m' in scene.get(k[0], set())
        ok = (a['nuclide'] == 'Pa-234m') == uran
        if uran and a['nuclide'] == 'Pa-234m':
            kept += 1
        if (not uran) and a['nuclide'] != 'Pa-234m':
            gone += 1
        if not ok:
            wrong += 1
        print('   %-26s пик %-9s уран %-3s -> %-14s %s'
              % (k[0], k[1], 'да' if uran else 'НЕТ',
                 a['nuclide'] or '(нет подписи)', 'ok' if ok else '⛔'))
    print('   законных оставлено %d, чужих снято %d, расхождений %d' % (kept, gone, wrong))

    print()
    print('=== ПЛЕЧО 2: ИСТИННЫЕ подписи поимённо ===')

    def truthful(rows):
        c = collections.Counter()
        for r in rows:
            if r['nuclide'] and truth.verdict(r['nuclide'], scene.get(r['spectrum'], set())) == 'ИСТИНА':
                c[r['nuclide']] += 1
        return c

    cb, ca = truthful(before), truthful(after)
    bad = 0
    for nm in sorted(set(cb) | set(ca)):
        mark = '' if ca[nm] >= cb[nm] else '  ⛔ УБЫЛО'
        if ca[nm] < cb[nm]:
            bad += 1
        print('   %-16s %4d -> %4d%s' % (nm, cb[nm], ca[nm], mark))
    print('   ИТОГО ИСТИНА %d -> %d ; нуклидов с убылью: %d' % (sum(cb.values()), sum(ca.values()), bad))

    print()
    print('=== ПЛЕЧО 3: заведомо плохой вход — подпись без единой другой своей линии ===')
    lost = collections.Counter()
    for k in sorted(bi):
        b, a = bi[k], ai.get(k, {'nuclide': ''})
        if b['nuclide'] and not a['nuclide']:
            lost[(truth.verdict(b['nuclide'], scene.get(k[0], set())),
                  b['nuclide'], b['line_kev'], b['intensity_pct'])] += 1
    for (v, nm, kev, i), n in sorted(lost.items(), key=lambda x: -x[1]):
        print('   %-10s %-14s %9s кэВ I=%-9s снято %3d' % (v, nm, kev, i, n))
    print('   всего снято подписей: %d' % sum(lost.values()))

    print()
    print('=== НОВЫЕ ПОДПИСИ ===')
    new = collections.Counter()
    for k in sorted(ai):
        a, b = ai[k], bi.get(k, {'nuclide': ''})
        if a['nuclide'] and a['nuclide'] != b['nuclide']:
            new[(a['nuclide'], a['line_kev'], k[0])] += 1
    for (nm, kev, spec), n in sorted(new.items()):
        print('   %-14s %9s кэВ  %s' % (nm, kev, spec))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
