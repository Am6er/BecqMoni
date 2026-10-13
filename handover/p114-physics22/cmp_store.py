# -*- coding: utf-8 -*-
r"""П114 — сверка ВСЕХ тел склада полосы (физика 22, lbang=1) со снимком живого склада физики 21 (rev31). Образец — П107.
Независимым читателем `rmx_body.py`: клеймо (phys=19, `eltr` в клеймо не читается — оно sha; берётся
из хвоста? нет — судим по `phys=` и по тому, что тело РАЗОШЛОСЬ), sha256 тела, блок Q_k (узлов),
и сдвиг B/A−1 по полосам у узлов ~662/1461/2614 кэВ: пик, нижняя четверть q0, 0–100 кэВ, сумма строки.
Ожидание физики 22: тела РАЗОШЛИСЬ у всех (ключ двигает поток случайных чисел), пик — в шуме,
континуум сцен с обвязкой сдвинут (нижняя четверть у мелких кристаллов).

  python cmp_store.py <склад полосы> <снимок физики 18> [--csv=<файл>] [--only=k1,k2]
Код 0 — все сцены снимка есть в складе полосы, у всех phys=22 и тело разошлось; 1 — иначе.
"""
import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rmx_body  # noqa: E402
import rmx_qk  # noqa: E402

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass


def main(argv):
    new, old = argv[0], argv[1]
    csv = None
    only = None
    for a in argv[2:]:
        if a.startswith('--csv='):
            csv = a[6:]
        elif a.startswith('--only='):
            only = set(a[7:].split(','))
    keys = sorted(f[:-4] for f in os.listdir(old) if f.lower().endswith('.rmx'))
    if only:
        keys = [k for k in keys if k in only]
    rows = []
    n_ok = 0
    hdr = ('сцена', 'phys', 'тело', 'Qk', 'пик662', 'q0_662', 'k100_662', 'Σ662', 'пик1461', 'q0_1461', 'k100_1461', 'Σ1461', 'пик2614', 'q0_2614', 'k100_2614', 'Σ2614')
    print('%-32s %5s %-10s %4s ' % hdr[:4] + ' '.join('%9s' % h for h in hdr[4:]))
    for k in keys:
        pn = os.path.join(new, k + '.rmx')
        po = os.path.join(old, k + '.rmx')
        if not os.path.isfile(pn):
            rows.append((k, '', 'НЕТ ФАЙЛА', 0) + (float('nan'),) * 12)
            print('%-32s %5s %-10s' % (k, '', 'НЕТ ФАЙЛА'))
            continue
        a = rmx_body.read(po)
        b = rmx_body.read(pn)
        qk = rmx_qk.read(pn)
        phys = b['stamp'].split(';')[0].replace('phys=', '')
        differs = a['body_sha'] != b['body_sha']
        nq = len(qk['qk']) if qk['qk'] else 0
        deltas = []
        for e, ra, rb, d in rmx_body.compare(a, b, [662.0, 1461.0, 2614.0]):
            deltas += [d['peak'], d['q0'], d['k0_100'], d['sum']]
        ok = phys == '22' and differs and nq == b['nodes'] and nq > 0
        n_ok += 1 if ok else 0
        rows.append((k, phys, 'РАЗОШЛОСЬ' if differs else 'ПОБИТОВО', nq) + tuple(deltas))
        print('%-32s %5s %-10s %4d ' % (k, phys, 'РАЗОШЛОСЬ' if differs else 'ПОБИТОВО', nq) + ' '.join('%+9.2f' % v for v in deltas))
    print('сцен %d, принято (phys=22, тело разошлось, блок Q_k полон) %d' % (len(keys), n_ok))
    # сводка по столбцам сдвига: медиана / мин / макс
    import statistics
    cols = list(zip(*[r[4:] for r in rows if r[2] == 'РАЗОШЛОСЬ']))
    if cols:
        print('сводка B/A−1, %% по %d сценам (медиана / мин / макс):' % len(cols[0]))
        for name, col in zip(hdr[4:], cols):
            print('  %-10s %+7.2f / %+7.2f / %+7.2f' % (name, statistics.median(col), min(col), max(col)))
    if csv:
        with io.open(csv, 'w', encoding='utf-8', newline='') as f:
            f.write(','.join(('scene', 'phys', 'body', 'qk_nodes', 'd_peak_662', 'd_q0_662', 'd_k100_662', 'd_sum_662',
                              'd_peak_1461', 'd_q0_1461', 'd_k100_1461', 'd_sum_1461',
                              'd_peak_2614', 'd_q0_2614', 'd_k100_2614', 'd_sum_2614')) + '\n')
            for r in rows:
                f.write(','.join([r[0], r[1], r[2], '%d' % r[3]] + ['%r' % v for v in r[4:]]) + '\n')
    return 0 if n_ok == len(keys) else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
