#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Прореживание корпусного спектра по Пуассону — оснастка полосы П28 (`A279`).

Зачем. Закон z(N) нельзя вывести сравнением РАЗНЫХ спектров: у них разные
приборы, шкалы, состав, фон и модель. Прореживание меняет ОДНУ величину —
статистику: тот же прибор, та же шкала, тот же состав, тот же склад.

Что делает. Читает спектр из копии корпуса, прореживает передний план
биномиально (каждый отсчёт выживает с вероятностью f), делит на f живое и
измеренное время (чтобы скорость счёта осталась ТОЙ ЖЕ), и кладёт копию под
новым ключом. Встроенный фон НЕ трогается: физически это «та же комната,
измерение короче», и вычитание фона по отношению времён отработает само.

⛔ Воспроизводимость. Зерно входит в поток явно (`numpy.random.default_rng`
от пары «зерно, ключ»), два прореживания с одним зерном обязаны дать
побитово один файл, с разными — разный. Это проверяет `--selftest`.

Запуск:
  python handover/p28-z/thin.py --corpus=<копия> --base=AS80_Onyx \
         --den=2,5,10,25,50,100,157,300 --seeds=11,22,33
  python handover/p28-z/thin.py --corpus=<копия> --selftest
"""

import argparse
import hashlib
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

import numpy as np

#: Нуклиды, которых в камнях корпуса ЗАВЕДОМО НЕТ. Нужны для нулевой
#: гипотезы: их z под верной нормировкой обязан иметь единичную ширину
#: независимо от статистики.
ABSENT = ['60CO', '54MN', '109CD', '88Y', '65ZN', '22NA', '134CS']


def read_table(path):
    with io.open(path, encoding='utf-8-sig', newline='') as f:
        head = f.readline().rstrip('\r\n')
        rows = [ln.rstrip('\r\n') for ln in f if ln.strip()]
    return head, rows


def append_rows(path, new_rows):
    with io.open(path, 'a', encoding='utf-8', newline='') as f:
        for r in new_rows:
            f.write(r + '\n')


def csv_cell(row, idx):
    # Разбор с кавычками — в корпусных таблицах есть поля с запятыми.
    out, cur, q = [], '', False
    for ch in row:
        if ch == '"':
            q = not q
        elif ch == ',' and not q:
            out.append(cur)
            cur = ''
        else:
            cur += ch
    out.append(cur)
    return out[idx] if idx < len(out) else ''


def split_row(row):
    out, cur, q = [], '', False
    for ch in row:
        if ch == '"':
            q = not q
            cur += ch
        elif ch == ',' and not q:
            out.append(cur)
            cur = ''
        else:
            cur += ch
    out.append(cur)
    return out


def thin_spectrum(src, dst, frac, seed, key):
    tree = ET.parse(src)
    root = tree.getroot()
    es = root.find('.//EnergySpectrum')
    if es is None:
        raise SystemExit('нет EnergySpectrum в ' + src)

    pts = es.find('Spectrum').findall('DataPoint')
    counts = np.array([int(p.text or '0') for p in pts], dtype=np.int64)

    # Зерно входит в поток ЯВНО и вместе с ключом: смена зерна обязана
    # сменить поток (грабля `seed-or-else-measures-nothing`).
    rng = np.random.default_rng(np.random.SeedSequence(
        entropy=int(seed),
        spawn_key=(hash_key(key),)))
    thinned = rng.binomial(counts, frac)

    for p, v in zip(pts, thinned):
        p.text = str(int(v))

    total = int(thinned.sum())
    set_num(es, 'ValidPulseCount', total, '%d')
    tp = es.find('TotalPulseCount')
    if tp is not None:
        tp.text = str(int(round(float(tp.text) * frac)))
    for tag, fmt in (('MeasurementTime', '%.10g'), ('LiveTime', '%.10g')):
        el = es.find(tag)
        if el is not None:
            el.text = fmt % (float(el.text) * frac)

    tree.write(dst, encoding='utf-8', xml_declaration=True)
    return total


def hash_key(key):
    return int(hashlib.sha256(key.encode('utf-8')).hexdigest()[:8], 16)


def set_num(parent, tag, value, fmt):
    el = parent.find(tag)
    if el is not None:
        el.text = fmt % value


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        h.update(f.read())
    return h.hexdigest()[:16]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--corpus', required=True)
    ap.add_argument('--base', default='')
    ap.add_argument('--den', default='2,5,10,25,50,100,300')
    ap.add_argument('--seeds', default='11,22,33')
    ap.add_argument('--null', action='store_true',
                    help='вторая семья ключей с ЗАВЕДОМО ОТСУТСТВУЮЩИМИ нуклидами')
    ap.add_argument('--selftest', action='store_true')
    a = ap.parse_args()

    corpus = os.path.abspath(a.corpus)
    spectra = os.path.join(corpus, 'spectra')

    if a.selftest:
        return selftest(corpus, spectra)

    dens = [int(x) for x in a.den.split(',') if x]
    seeds = [int(x) for x in a.seeds.split(',') if x]

    man_head, man_rows = read_table(os.path.join(corpus, 'manifest.csv'))
    par_head, par_rows = read_table(os.path.join(corpus, 'parts.csv'))
    mat_head, mat_rows = read_table(os.path.join(corpus, 'materials.csv'))

    def find(rows, key):
        for r in rows:
            if csv_cell(r, 0) == key:
                return r
        raise SystemExit('нет строки ' + key)

    base = a.base
    man = find(man_rows, base)
    par = find(par_rows, base)
    mat = find(mat_rows, base)

    new_man, new_par, new_mat = [], [], []
    print('%-34s %6s %5s %12s %s' % ('key', '1/den', 'seed', 'counts', 'sha256'))
    for den in dens:
        for seed in seeds:
            for family in (['n'] if a.null else ['']):
                key = '%s_x%d_s%d%s' % (base, den, seed, family)
                dst = os.path.join(spectra, key + '.xml')
                total = thin_spectrum(os.path.join(spectra, base + '.xml'),
                                      dst, 1.0 / den, seed, key)
                cells = split_row(man)
                cells[0] = key
                cells[4] = str(total)
                cells[3] = '%.10g' % (float(csv_cell(man, 3)) / den)
                if family == 'n':
                    have = [x for x in csv_cell(man, 6).split(';') if x]
                    cells[6] = ';'.join(have + ABSENT)
                new_man.append(','.join(cells))
                pc = split_row(par)
                pc[0] = key
                new_par.append(','.join(pc))
                mc = split_row(mat)
                mc[0] = key
                new_mat.append(','.join(mc))
                print('%-34s %6d %5d %12d %s' % (key, den, seed, total, sha(dst)))

    append_rows(os.path.join(corpus, 'manifest.csv'), new_man)
    append_rows(os.path.join(corpus, 'parts.csv'), new_par)
    append_rows(os.path.join(corpus, 'materials.csv'), new_mat)
    print('дописано строк: манифест %d, части %d, вещества %d'
          % (len(new_man), len(new_par), len(new_mat)))
    return 0


def selftest(corpus, spectra):
    u"""Положительный контроль зерна: одно зерно — один файл, разные — разные."""
    base = 'AS80_Onyx'
    src = os.path.join(spectra, base + '.xml')
    tmp = os.path.join(spectra, '_selftest')
    outs = {}
    for tag, seed in (('a', 11), ('b', 11), ('c', 12)):
        dst = tmp + tag + '.xml'
        thin_spectrum(src, dst, 0.1, seed, 'ключ_один')
        outs[tag] = sha(dst)
        os.remove(dst)
    print('зерно 11 (первый)  : ' + outs['a'])
    print('зерно 11 (второй)  : ' + outs['b'])
    print('зерно 12           : ' + outs['c'])
    ok = outs['a'] == outs['b'] and outs['a'] != outs['c']
    print('ЗЕРНО ВОСПРОИЗВОДИМО И РАЗЛИЧАЕТ' if ok else 'ЗЕРНО НЕ РАБОТАЕТ')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
