# -*- coding: utf-8 -*-
"""П68 (S173): таблица «до/после» по плечам А (HEAD) и Б (правка) из логов и rates_*.csv FsaStackShot.

    python handover/p68-s173/table.py D:\\BqMoni_Claude\\p65\\out_a D:\\BqMoni_Claude\\p65\\out_b [--out=handover/p68-s173/before_after.csv]

По каждому спектру (as80, radon1, radon2) и плечу прогона (free, eq, plant, zoom): слои экрана (ROW: имя, доля %),
χ²/ndf, строка отвязанного хвоста, невязка экрана (SCREEN), компоненты состава (скорость счёта, z, доля) и
пределы — А против Б в одной строке. Плечо plant у А не снимается (ключа нет) — в таблице пусто.
Разделитель дробной части — точка.
"""
import csv
import io
import os
import sys

RESIDUAL = ''
SPECTRA = ('as80', 'radon1', 'radon2')
ARMS = ("free", "eq", "plant", "zoom")


def read_log(path):
    rows, chi, gate = [], '', ''
    global RESIDUAL
    RESIDUAL = ''
    if not os.path.exists(path):
        return rows, chi, gate
    with io.open(path, encoding='utf-8', errors='replace', newline='') as fh:
        for line in fh:
            line = line.rstrip('\r\n')
            if line.startswith('ROW\t'):
                f = line.split('\t')
                rows.append((f[1], f[2], f[3]))
            elif line.startswith('chi2/ndf'):
                chi = line.split(',')[0].replace('chi2/ndf ', '')
            elif line.startswith('SCREEN	') and ('Невязка' in line or 'esidual' in line):
                RESIDUAL = line.split('	')[2] if len(line.split('	')) > 2 else ''
            elif line.startswith('отвязанный хвост'):
                gate = line.replace('отвязанный хвост (в невязке, не в слоях): ', 'хвост: ')
    return rows, chi, gate


def read_rates(path):
    comp, lim, meta = {}, {}, {}
    if not os.path.exists(path):
        return comp, lim, meta
    with io.open(path, encoding='utf-8', newline='') as fh:
        for r in csv.DictReader(fh):
            if r['section'] == 'component':
                comp[r['name']] = r
            elif r['section'] == 'limit':
                lim[r['name']] = r
            elif r['section'] == 'meta':
                meta[r['name']] = r['count_rate']
    return comp, lim, meta


def fmt(x, digits=4):
    try:
        return ('%.' + str(digits) + 'g') % float(x)
    except (TypeError, ValueError):
        return ''


def describe(name, comp, lim):
    c = comp.get(name)
    if c:
        tied = c.get('tied_to') or ''
        return '%s Бк z=%s доля=%s%%%s' % (fmt(c['count_rate']), fmt(c['z'], 3), fmt(c['share_pct'], 3),
                                          (' по ' + tied) if tied else '')
    L = lim.get(name)
    if L:
        return '< %s' % fmt(L['detection_limit_rate'], 3)
    return '—'


def main():
    da, db = sys.argv[1], sys.argv[2]
    out = None
    for a in sys.argv[3:]:
        if a.startswith('--out='):
            out = a[6:]
    lines = ['spectrum,arm,what,name,A,B,changed']
    for spectrum in SPECTRA:
        for arm in ARMS:
            key = '%s_%s' % (spectrum, arm)
            rowsA, chiA, gateA = read_log(os.path.join(da, key + '.log'))
            resA = RESIDUAL
            rowsB, chiB, gateB = read_log(os.path.join(db, key + '.log'))
            resB = RESIDUAL
            compA, limA, metaA = read_rates(os.path.join(da, 'rates_%s.csv' % key))
            compB, limB, metaB = read_rates(os.path.join(db, 'rates_%s.csv' % key))
            if not rowsA and not rowsB:
                continue
            lines.append(','.join([spectrum, arm, 'chi2/ndf', '', chiA, chiB, '*' if chiA != chiB else '']))
            lines.append(','.join([spectrum, arm, 'tail', '', '"%s"' % gateA, '"%s"' % gateB, '']))
            lines.append(','.join([spectrum, arm, 'residual', '', '"%s"' % resA, '"%s"' % resB, '*' if resA != resB else '']))
            layersA = dict((r[0], r[2]) for r in rowsA)
            layersB = dict((r[0], r[2]) for r in rowsB)
            names = []
            for r in rowsA + rowsB:
                if r[0] not in names:
                    names.append(r[0])
            for n in names:
                a, b = layersA.get(n, '—'), layersB.get(n, '—')
                lines.append(','.join([spectrum, arm, 'layer', n, a, b, '*' if a != b else '']))
            members = []
            for n in list(compA) + list(limA) + list(compB) + list(limB):
                if n not in members:
                    members.append(n)
            for n in members:
                a, b = describe(n, compA, limA), describe(n, compB, limB)
                lines.append(','.join([spectrum, arm, 'member', n, '"%s"' % a, '"%s"' % b, '*' if a != b else '']))
    text = '\n'.join(lines) + '\n'
    sys.stdout.write(text)
    if out:
        with io.open(out, 'w', encoding='utf-8', newline='') as fh:
            fh.write(text)


if __name__ == '__main__':
    main()
