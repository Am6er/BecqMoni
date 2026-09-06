# -*- coding: utf-8 -*-
u"""F77 / `S42`: выгрузка ОПОР и НАЙДЕННЫХ ЛИНИЙ корпуса для мерки сторожа изгиба.

Ничего не пишет в `tools/CORPUS/**` — только читает свои копии стадии 1
(`scripts/_corpus_raw`) тем же кодом, каким корпус калибруется, и складывает
две таблицы в указанный каталог:

  anchors.csv  key,channels,ch,e,count,sig   — опоры, принятые стадией 1
  lines.csv    key,e,ch                      — известные линии и их каналы,
                                               определённые ОДИН РАЗ по данным
                                               (внешняя мерка, как у F56)

    python handover/f77-s42/dump_anchors_f77.py --out=<каталог> [--only=K1,K2]
"""
import io
import os
import sys

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCRIPTS = os.path.join(ROOT, 'tools', 'CORPUS', 'scripts')
sys.path.insert(0, SCRIPTS)

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

import corpus_def                                     # noqa: E402
import corpus_calib as cc                             # noqa: E402
import build_corpus                                   # noqa: E402
import ecal_extrapolation as ee                       # noqa: E402


def main(argv):
    out_dir = None
    only = None
    for a in argv:
        if a.startswith('--out='):
            out_dir = a.split('=', 1)[1]
        elif a.startswith('--only='):
            only = set(a.split('=', 1)[1].split(','))
    if not out_dir:
        print(u'нужен --out=<каталог>')
        return 2
    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)

    entries = [e for e in corpus_def.NEW + corpus_def.VIBE + corpus_def.ETALON
               if only is None or e['key'] in only]
    print(u'спектров на вход: %d' % len(entries))
    state = ee.build_state(entries)
    print(u'стадия 1+2а прошла у %d' % len(state))

    a_rows = []
    l_rows = []
    for key in sorted(state):
        st = state[key]
        if not st['accepted']:
            print(u'%-24s опор нет' % key)
            continue
        counts = np.asarray(st['sp'].counts, dtype=float)
        n = int(st['sp'].n)
        for a in st['accepted']:
            ch = int(round(float(a['ch'])))
            cnt = int(counts[ch]) if 0 <= ch < len(counts) else 0
            a_rows.append((key, n, ch, float(a['e_ref']), cnt, float(a['sig'])))

        res_a = st['r662'] * np.sqrt(662.0)
        ent = dict(st['entry'])
        ent['wanted'] = build_corpus.wanted_lines(st['entry'])
        build_corpus.calibrate.sample_lines = build_corpus.sample_lines
        lines = build_corpus.calibrate.curate(
            ent, lambda e: res_a * np.sqrt(max(float(e), 5.0)), min_purity=0.45)
        found = cc.match_lines(counts, st['ecal'], lines, res_a,
                               tol_fwhm=6.0, width_lo=0.3, width_hi=3.0)
        for f in found:
            l_rows.append((key, float(f['e_ref']), float(f['ch'])))

    with io.open(os.path.join(out_dir, 'anchors.csv'), 'w', encoding='utf-8', newline='') as h:
        h.write(u'key,channels,ch,e,count,sig\n')
        for r in a_rows:
            h.write(u'%s,%d,%d,%.4f,%d,%.3f\n' % r)
    with io.open(os.path.join(out_dir, 'lines.csv'), 'w', encoding='utf-8', newline='') as h:
        h.write(u'key,e,ch\n')
        for r in l_rows:
            h.write(u'%s,%.4f,%.4f\n' % r)
    print(u'опор %d, линий %d, спектров с опорами %d'
          % (len(a_rows), len(l_rows), len({r[0] for r in a_rows})))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
