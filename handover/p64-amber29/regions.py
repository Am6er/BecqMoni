# -*- coding: utf-8 -*-
"""П64 (AMBER29): что видно ДО разбора — проба против фона по областям энергии, все 33
съёмки, и разница двух фонов (19.11.2025 против 05.04.2026) по тем же областям.

Отсчёты в окне энергий — по калибровке КАЖДОГО файла (у фона 19.11 усиление 3.11 против
3.02 кэВ/кан у проб): накопленная сумма по каналам интерполируется на дробную границу
канала. Фон вычитается с β = 1 (отношение живых времён), как в приложении.

    python handover/p64-amber29/regions.py <каталог .spe> [--out=<csv>]

Столбцы: ключ, старт (ч от 01.11.2025 12:52:52 — гипотеза t₀), живое, cps всего; по каждой
области — нетто (отсчёты) и σ; отдельно скорость нетто в окне 609 (1/с) и 352 — вход
сырой оценки распада (`decay_fit.py --raw`).
"""
import glob
import io
import math
import os
import re
import sys
from datetime import datetime

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, os.path.join(REPO, 'tools', 'CORPUS', 'scripts'))
sys.path.insert(0, HERE)
from spe_import import read_spe, start_time  # noqa: E402
from mk_spectra import key_of, ecal_of  # noqa: E402

T0 = datetime(2025, 11, 1, 12, 52, 52)

REGIONS = [('40-100', 40, 100), ('160-215 (186)', 160, 215), ('215-265 (239/242)', 215, 265),
           ('270-320 (295)', 270, 320), ('320-390 (352)', 320, 390), ('555-660 (583/609)', 555, 660),
           ('690-760 (727)', 690, 760), ('860-1010 (911/969)', 860, 1010), ('1070-1170 (1120)', 1070, 1170),
           ('1400-1520 (1461)', 1400, 1520), ('1690-1850 (1764)', 1690, 1850), ('2100-2300 (2204)', 2100, 2300),
           ('2500-2750 (2614)', 2500, 2750)]


def energy(coef, ch):
    return sum(c * ch ** k for k, c in enumerate(coef))


def channel(coef, e):
    ch = (e - coef[0]) / coef[1]
    for _ in range(30):
        f = energy(coef, ch) - e
        d = sum(k * c * ch ** (k - 1) for k, c in enumerate(coef) if k > 0)
        ch -= f / d
    return ch


def window_counts(counts, coef, e_lo, e_hi):
    """Отсчёты между энергиями: канал i покрывает [i-0.5; i+0.5)."""
    cum = np.concatenate([[0.0], np.cumsum(counts)])
    edges = np.arange(len(counts) + 1) - 0.5
    lo, hi = channel(coef, e_lo), channel(coef, e_hi)
    return float(np.interp(hi, edges, cum) - np.interp(lo, edges, cum))


def main():
    src = sys.argv[1]
    out = None
    for a in sys.argv[2:]:
        if a.startswith('--out='):
            out = a[6:]
    spectra = {}
    for p in sorted(glob.glob(os.path.join(src, '*.spe'))):
        h, c = read_spe(p)
        spectra[key_of(os.path.basename(p))] = (h, np.array(c, dtype=float), ecal_of(h))
    bg1 = spectra['bg_2025_11_19']
    bg2 = spectra['bg_2026_04_05']
    live_bg1 = float(bg1[0]['TLIVE'])
    live_bg2 = float(bg2[0]['TLIVE'])

    print('== фоны: 19.11.2025 (%.0f с, %.4f cps) против 05.04.2026 (%.0f с, %.4f cps) ==' % (
        live_bg1, bg1[1].sum() / live_bg1, live_bg2, bg2[1].sum() / live_bg2))
    print('%-22s %12s %12s %8s %8s' % ('область, кэВ', 'cps 19.11', 'cps 05.04', 'отн.', 'σ'))
    for name, lo, hi in REGIONS:
        a = window_counts(bg1[1], bg1[2], lo, hi)
        b = window_counts(bg2[1], bg2[2], lo, hi)
        ra, rb = a / live_bg1, b / live_bg2
        sig = (rb - ra) / math.sqrt(a / live_bg1 ** 2 + b / live_bg2 ** 2)
        print('%-22s %12.5f %12.5f %8.4f %8.1f' % (name, ra, rb, rb / ra, sig))
    print()

    header = ['key', 't_mid_h', 'live_s', 'cps_total', 'cps_net_total'] + \
             sum([[n + '_net', n + '_sig'] for n, _, _ in REGIONS], []) + ['rate_609_net', 'rate_609_err', 'rate_352_net', 'rate_352_err']
    rows = []
    print('%-10s %7s %8s %7s | %s' % ('ключ', 't_mid,ч', 'cps', 'нетто', ' '.join('%14s' % n.split(' ')[0] for n, _, _ in REGIONS)))
    for k in sorted(spectra):
        if k.startswith('bg_'):
            continue
        h, c, coef = spectra[k]
        live = float(h['TLIVE'])
        st = datetime.fromisoformat(start_time(h))
        t_mid = ((st - T0).total_seconds() + 0.5 * live) / 3600.0
        row = [k, '%.4f' % t_mid, '%.1f' % live, '%.4f' % (c.sum() / live),
               '%.4f' % (c.sum() / live - bg1[1].sum() / live_bg1)]
        cells = []
        r609 = r352 = None
        for name, lo, hi in REGIONS:
            s = window_counts(c, coef, lo, hi)
            b = window_counts(bg1[1], bg1[2], lo, hi) * live / live_bg1
            net = s - b
            sig = math.sqrt(s + b * live / live_bg1)
            row += ['%.1f' % net, '%.1f' % sig]
            cells.append('%8.0f (%4.0f)' % (net, sig))
            if lo == 555:
                r609 = (net / live, sig / live)
            if lo == 320:
                r352 = (net / live, sig / live)
        row += ['%.6f' % r609[0], '%.6f' % r609[1], '%.6f' % r352[0], '%.6f' % r352[1]]
        rows.append(row)
        print('%-10s %7.2f %8.3f %7.3f | %s' % (k, t_mid, c.sum() / live, c.sum() / live - bg1[1].sum() / live_bg1, ' '.join(cells)))
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write(','.join(header) + '\n')
            for r in rows:
                fh.write(','.join(r) + '\n')


if __name__ == '__main__':
    main()
