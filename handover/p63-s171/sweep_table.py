# -*- coding: utf-8 -*-
"""П63 (S171, второе правило): свод плеч из логов и rates_*.csv FsaStackShot.

    python handover/p63-s171/sweep_table.py D:\\BqMoni_Claude\\p63\\out_b [--out=handover/p63-s171/sweep.csv]
    python handover/p63-s171/sweep_table.py D:\\BqMoni_Claude\\p63\\out_b --before=tie --after=both --out=handover/p63-s171/before_after.csv

Читает <спектр>_<плечо>.log (строки LIM/TIE/ROW/SCREEN/chi2) и rates_<спектр>_<плечо>.csv;
печатает по каждому спектру и плечу члены рядов: приговор правила предела (опорный член,
ожидаемая и фактическая значимость, порог, круг, исход), скорость счёта (распадов/с в единицах
корня ряда), отношение к равновесной (эталон AS80: 865.3 — плечо «eq» П59,
`handover/p59-amber27/ratios.csv`), z, доля слоя на экране и строка экрана. Разделитель — точка.
С `--before=`/`--after=` — таблица «до/после» по двум плечам.
"""
import csv
import glob
import io
import os
import re
import sys

EQ_AS80 = 865.3
MEMBERS = ['Th-232', 'Ra-228', 'Ac-228', 'Th-228', 'Ra-224', 'Rn-220', 'Po-216', 'Pb-212', 'Bi-212', 'Tl-208',
           'Ra-226', 'Rn-222', 'Pb-214', 'Bi-214', 'Po-214', 'Pb-210', 'Bi-210', 'Po-210']


def read_log(path):
    lims, ties, rows, screen, chi = {}, {}, {}, {}, ''
    with io.open(path, encoding='utf-8', errors='replace', newline='') as fh:
        for line in fh:
            line = line.rstrip('\r\n')
            if line.startswith('LIM\t'):
                _, member, ref, ez, z, thr, rnd, verdict = line.split('\t')
                # приговор члена — запись круга, где он СНЯТ (`limit`); у оставшихся
                # свободными — последний круг (после всех снятий)
                prev = lims.get(member)
                if prev is None or prev[5] != 'limit':
                    lims[member] = (ref, float(ez), float(z), float(thr), int(rnd), verdict)
            elif line.startswith('TIE\t'):
                _, member, partner, share, pair, verdict = line.split('\t')
                ties[member] = (partner, float(share), float(pair), verdict)
            elif line.startswith('ROW\t'):
                f = line.split('\t')
                rows[f[1]] = (float(f[3]), f[5] if len(f) > 5 else '-')
            elif line.startswith('SCREEN\t'):
                f = line.split('\t')
                name = f[1]
                base = re.split(r' — ', name)[0]
                if base not in screen:
                    screen[base] = (name, f[2] if len(f) > 2 else '', f[3] if len(f) > 3 else '')
            elif line.startswith('chi2/ndf'):
                chi = line
    return lims, ties, rows, screen, chi


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


def member_row(spectrum, m, lims, ties, comp, lim, screen):
    """Одна строка по члену: (rate_s, x_eq, z, share_pct, tied_to, screen_text, status)."""
    c = comp.get(m)
    L = lim.get(m)
    v = lims.get(m)
    rate = float(c['count_rate']) if c else float('nan')
    z = float(c['z']) if c and c['z'] else float('nan')
    share_pct = float(c['share_pct']) if c else float('nan')
    tied_to = (c.get('tied_to') or '') if c else ((L.get('tied_to') or '') if L else '')
    xeq = rate / EQ_AS80 if spectrum == 'as80' and c else float('nan')
    if not c and L:
        dl = L['detection_limit_rate']
        rate_s = ('<%.4g' % float(dl)) if dl else 'n/a'
    elif c:
        rate_s = '%.4g' % rate
    else:
        rate_s = ''
    scr = screen.get(m, ('', '', ''))
    if scr[0]:
        screen_text = '%s %s' % (scr[0], scr[1])
    else:
        # свёрнутая строка: имя в подсказке
        folded = [s for s in screen.values() if s[0].startswith('не определяются') and m in s[2].split(', ')]
        screen_text = ('свёрнут: %s %s' % (folded[0][0], folded[0][1])) if folded else ''
    if c and tied_to:
        status = 'по ' + tied_to
    elif c:
        status = 'свободен'
    elif v and v[5] == 'limit':
        status = 'предел (правило)'
    elif L:
        status = 'предел'
    else:
        status = ''
    return rate_s, xeq, z, share_pct, tied_to, screen_text, status


def main():
    d = sys.argv[1]
    out = before = after = None
    for a in sys.argv[2:]:
        if a.startswith('--out='):
            out = a[6:]
        elif a.startswith('--before='):
            before = a[9:]
        elif a.startswith('--after='):
            after = a[8:]
    if before and after:
        lines = ['spectrum,member,before_rate,before_x_eq,before_z,before_screen,before_status,'
                 'after_rate,after_x_eq,after_z,after_screen,after_status,expected_z,reference,threshold']
        for spectrum in ('as80', 'radon1', 'radon2'):
            lb = os.path.join(d, '%s_%s.log' % (spectrum, before))
            la = os.path.join(d, '%s_%s.log' % (spectrum, after))
            if not os.path.exists(lb) or not os.path.exists(la):
                continue
            limsB, tiesB, _, screenB, _ = read_log(lb)
            limsA, tiesA, _, screenA, _ = read_log(la)
            compB, limB = read_rates(os.path.join(d, 'rates_%s_%s.csv' % (spectrum, before)))
            compA, limA = read_rates(os.path.join(d, 'rates_%s_%s.csv' % (spectrum, after)))
            for m in MEMBERS:
                if m not in compB and m not in limB and m not in compA and m not in limA:
                    continue
                b = member_row(spectrum, m, limsB, tiesB, compB, limB, screenB)
                a = member_row(spectrum, m, limsA, tiesA, compA, limA, screenA)
                v = limsA.get(m)
                lines.append(','.join([
                    spectrum, m,
                    b[0], ('%.3f' % b[1]) if b[1] == b[1] else '', ('%.1f' % b[2]) if b[2] == b[2] else '',
                    '"%s"' % b[5], b[6],
                    a[0], ('%.3f' % a[1]) if a[1] == a[1] else '', ('%.1f' % a[2]) if a[2] == a[2] else '',
                    '"%s"' % a[5], a[6],
                    ('%.3f' % v[1]) if v else '', v[0] if v else '', ('%.3g' % v[3]) if v else '']))
    else:
        lines = ['spectrum,arm,member,reference,expected_z,z_at_judgement,threshold,round,verdict,'
                 'rate,x_eq,z,share_pct,tied_to,screen,status']
        for log in sorted(glob.glob(os.path.join(d, '*.log'))):
            key = os.path.basename(log)[:-4]
            if key.startswith('rates_') or '_' not in key:
                continue
            spectrum, arm = key.split('_', 1)
            if spectrum not in ('as80', 'radon1', 'radon2'):
                continue
            lims, ties, rows, screen, chi = read_log(log)
            comp, lim = read_rates(os.path.join(d, 'rates_%s.csv' % key))
            for m in MEMBERS:
                if m not in lims and m not in comp and m not in lim:
                    continue
                v = lims.get(m)
                r = member_row(spectrum, m, lims, ties, comp, lim, screen)
                lines.append(','.join([
                    spectrum, arm, m,
                    v[0] if v else '', ('%.3f' % v[1]) if v else '', ('%.1f' % v[2]) if v else '',
                    ('%.3g' % v[3]) if v else '', str(v[4]) if v else '', v[5] if v else '',
                    r[0], ('%.3f' % r[1]) if r[1] == r[1] else '', ('%.1f' % r[2]) if r[2] == r[2] else '',
                    ('%.2f' % r[3]) if r[3] == r[3] else '', r[4], '"%s"' % r[5], r[6]]))
    text = '\n'.join(lines) + '\n'
    sys.stdout.write(text)
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write(text)


if __name__ == '__main__':
    main()
