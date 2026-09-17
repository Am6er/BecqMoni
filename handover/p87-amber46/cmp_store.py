# -*- coding: utf-8 -*-
r"""Сверка ВСЕХ тел склада полосы (формат 9) с живым складом / снимком (формат 8) — шаг 12 П87.
Независимым читателем `rmx_qk.py` (не кодом приложения): клеймо, sha256 ТЕЛА (сетка + строки по
каналам, между блоком настроек и `ANGK`/`NOIS`), число узлов блока Q_k, худший шум Q₂, Q₂ у 662 кэВ.
Печатает таблицу и итог «тел побитово N из M»; код 0 — все M побитово и у всех блок Q_k, 1 — иначе.

  python cmp_store.py <склад формата 9> <снимок формата 8> [--csv=<файл>]
"""
import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rmx_qk  # noqa: E402

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass


def main(argv):
    new, old = argv[0], argv[1]
    csv = None
    for a in argv[2:]:
        if a.startswith('--csv='):
            csv = a[6:]
    keys = sorted(f[:-4] for f in os.listdir(old) if f.lower().endswith('.rmx'))
    rows = []
    same = 0
    withqk = 0
    for k in keys:
        pn = os.path.join(new, k + '.rmx')
        po = os.path.join(old, k + '.rmx')
        if not os.path.isfile(pn):
            rows.append((k, 'НЕТ ФАЙЛА', '', '', 0, 0.0, 0.0, 0, 0))
            continue
        a = rmx_qk.read(po)
        b = rmx_qk.read(pn)
        body = a['body_sha'] == b['body_sha']
        stamp = a['stamp'] == b['stamp']
        qk = b['qk']
        n = len(qk) if qk else 0
        worst = max((max(r[2], r[3]) for r in qk), default=0.0) if qk else 0.0
        q662 = rmx_qk.interp(b['energies'], [r[0] for r in qk], 661.7) if qk else 0.0
        hist = qk[len(qk) // 2][10] if qk else 0
        if body:
            same += 1
        if n == b['nodes'] and n > 0:
            withqk += 1
        rows.append((k, 'ПОБИТОВО' if body else 'РАЗОШЛОСЬ', 'клеймо =' if stamp else 'КЛЕЙМО ≠', b['body_sha'][:16], n, worst, q662, hist, b['format']))
    print('%-32s %-10s %-9s %-17s %5s %8s %8s %9s %s' % ('сцена', 'тело', 'клеймо', 'sha тела', 'Qk', 'dQmax', 'Q2(662)', 'hist', 'fmt'))
    for r in rows:
        print('%-32s %-10s %-9s %-17s %5d %8.4f %8.4f %9d %s' % r)
    print('тел побитово %d из %d; блок Q_k у %d из %d' % (same, len(keys), withqk, len(keys)))
    if csv:
        with io.open(csv, 'w', encoding='utf-8', newline='') as f:
            f.write('scene,body,stamp,body_sha16,qk_nodes,dq_max,q2_662,qk_histories,format\n')
            for r in rows:
                f.write('%s,%s,%s,%s,%d,%r,%r,%d,%s\n' % r)
    return 0 if same == len(keys) and withqk == len(keys) else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
