# -*- coding: utf-8 -*-
"""П60 (S171): свод развёртки гейта привязки из логов и rates_*.csv FsaStackShot.

    python handover/p60-s171/sweep_table.py D:\\BqMoni_Claude\\p60\\out_b [--out=handover/p60-s171/sweep.csv]

Читает <спектр>_<плечо>.log (строки TIE/ROW/SCREEN/chi2) и rates_<спектр>_<плечо>.csv; печатает по
каждому спектру и плечу члены рядов: приговор гейта (доля по Шуру, парная доля, партнёр), скорость
счёта (распадов/с в единицах корня ряда), отношение к равновесной (эталон AS80: 865.3 — плечо «eq»
П59, `handover/p59-amber27/ratios.csv`), z, доля слоя на экране и строка экрана. Разделитель — точка.
"""
import csv
import glob
import io
import os
import re
import sys

EQ_AS80 = 865.3
MEMBERS = ['Th-232', 'Ra-228', 'Ac-228', 'Th-228', 'Ra-224', 'Rn-220', 'Po-216', 'Pb-212', 'Bi-212', 'Tl-208',
           'Ra-226', 'Rn-222', 'Pb-214', 'Bi-214', 'Pb-210', 'Bi-210', 'Th-234', 'Pa-234m', 'U-238', 'U-234', 'Th-230',
           'U-235', 'Th-231', 'Pa-231', 'Ac-227', 'Th-227', 'Ra-223', 'Rn-219', 'Pb-211', 'Bi-211', 'Tl-207']


def read_log(path):
    ties, rows, screen, chi = {}, {}, {}, ''
    with io.open(path, encoding='utf-8', errors='replace', newline='') as fh:
        for line in fh:
            line = line.rstrip('\r\n')
            if line.startswith('TIE\t'):
                _, member, partner, share, pair, verdict = line.split('\t')
                ties[member] = (partner, float(share), float(pair), verdict)
            elif line.startswith('ROW\t'):
                f = line.split('\t')
                rows[f[1]] = (float(f[3]), f[5] if len(f) > 5 else '-')
            elif line.startswith('SCREEN\t'):
                f = line.split('\t')
                name = f[1]
                base = re.split(r' — ', name)[0]
                screen[base] = (name, f[2] if len(f) > 2 else '')
            elif line.startswith('chi2/ndf'):
                chi = line
    return ties, rows, screen, chi


def read_rates(path):
    comp, lim = {}, {}
    if not os.path.exists(path):
        return comp, lim
    with io.open(path, encoding='utf-8', newline='') as fh:
        for r in csv.DictReader(fh):
            if r['section'] == 'component':
                comp[r['name']] = r
            elif r['section'] == 'limit':
                lim[r['name']] = r
    return comp, lim


def main():
    d = sys.argv[1]
    out = None
    for a in sys.argv[2:]:
        if a.startswith('--out='):
            out = a[6:]
    lines = ['spectrum,arm,member,gate_share,gate_pair,gate_partner,gate_verdict,rate,x_eq,z,share_pct,tied_to,screen']
    logs = sorted(glob.glob(os.path.join(d, '*.log')))
    for log in logs:
        key = os.path.basename(log)[:-4]
        if key.startswith('rates_') or '_' not in key:
            continue
        spectrum, arm = key.split('_', 1)
        if spectrum not in ('as80', 'radon1', 'radon2', 'uglass'):
            continue
        ties, rows, screen, chi = read_log(log)
        comp, lim = read_rates(os.path.join(d, 'rates_%s.csv' % key))
        for m in MEMBERS:
            if m not in ties and m not in comp and m not in lim:
                continue
            t = ties.get(m)
            c = comp.get(m)
            L = lim.get(m)
            rate = float(c['count_rate']) if c else float('nan')
            z = float(c['z']) if c and c['z'] else float('nan')
            share_pct = float(c['share_pct']) if c else float('nan')
            tied_to = (c.get('tied_to') or '') if c else ((L.get('tied_to') or '') if L else '')
            xeq = rate / EQ_AS80 if spectrum == 'as80' and c else float('nan')
            if not c and L:
                dl = L['detection_limit_rate']
                rate_s = ('<%.4g' % float(dl)) if dl else 'n/a'
            else:
                rate_s = '%.4g' % rate
            scr = screen.get(m, ('', ''))
            lines.append('%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s' % (
                spectrum, arm, m,
                ('%.4f' % t[1]) if t else '', ('%.4f' % t[2]) if t else '', t[0] if t else '', t[3] if t else '',
                rate_s, ('%.3f' % xeq) if xeq == xeq else '', ('%.1f' % z) if z == z else '',
                ('%.2f' % share_pct) if share_pct == share_pct else '', tied_to,
                '"%s %s"' % (scr[0], scr[1]) if scr[0] else ''))
    text = '\n'.join(lines) + '\n'
    sys.stdout.write(text)
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write(text)


if __name__ == '__main__':
    main()
