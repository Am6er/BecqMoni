#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ разбора хи-квадрата — оснастка полосы П31 (`A281`).

Зачем. Разложение хи-квадрата на систематику и статистику обязано РАЗЛИЧАТЬ:
нужен вход, где вклад заведомо пуассоновский, и вход, где заведомо
систематический. Если на обоих выходит одно — разбор ничего не меряет.

Что делает. Берёт ПОДОГНАННУЮ МОДЕЛЬ настоящей сцены (столбец `model` из
`--dump-curves`) и разыгрывает по ней пуассоновский спектр:

  плечо P (`_synP`) — среднее РАВНО модели. Систематики нет по построению:
      истина лежит В МОДЕЛЬНОМ ПРОСТРАНСТВЕ, разложению её достаточно достать.
      Ожидание: eps ~ 0 и хи2/ndf ~ 1 на ВСЕЙ лестнице прореживания.

  плечо S (`_synS`) — то же, но среднее домножено на (1 + a*sin(2*pi*ch/L)):
      известная относительная систематика размера a, которую ни сплайн (шаг
      узлов 4 ПШПВ), ни сетка дрейфа повторить не могут. Ожидание:
      eps = a/sqrt(2) НЕЗАВИСИМО от набора, а хи2_пуассон/ndf - 1 растёт
      ЛИНЕЙНО с числом отсчётов.

⛔ ВСТРОЕННЫЙ ФОН РАЗЫГРЫВАЕТСЯ ТОЖЕ, и это не украшение. Разбор вычитает фон
   из файла и добавляет его шум в дисперсию (`variance += full*scale`), то есть
   считает вычтенную кривую СЛУЧАЙНОЙ. Отдать ему неслучайный фон значит завысить
   дисперсию, и плечо P дало бы хи2 НИЖЕ единицы по причине оснастки.
   Розыгрыш: передний план ~ Пуассон(model + bg*scale), фон ~ Пуассон(bg).

⛔ ПОЧИНЕННАЯ ЛОВУШКА, стоившая одного круга. Первая редакция восстанавливала
   вычтенный фон как `raw - net` по дампу. Но `net` — ПОКАЗНАЯ кривая, у которой
   отрицательное подрезано нулём (`FsaResult.NetSpectrum`), а фит держит y без
   подрезки: у `AS80_Onyx` таких каналов 2672 из 8192. Плечо P дало тогда
   хи2/ndf 3.71 вместо единицы — СОБСТВЕННАЯ систематика оснастки, неотличимая
   от измеряемой. Вторая редакция снимала фон из манифеста — и это тоже не
   работало: у `AS80_Onyx` фон ВСТРОЕН В САМ ФАЙЛ (`BackgroundEnergySpectrum`),
   графа манифеста его не выключает, и хи2 вышел 3785. Признак обеих бед один:
   плечо P далеко от единицы. Оно и есть сторож этой оснастки.

Зерно входит в поток явно и вместе с ключом — как у `handover/p28-z/thin.py`.

  python handover/p31-inflate/synth.py --corpus=<копия> --curves=<каталог>
         --base=G1S24_Eu152_P5 --amp=0.05 --period=32 --seed=101
"""

import argparse
import csv
import io
import os
import sys
import xml.etree.ElementTree as ET

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'p28-z'))
import thin as T  # noqa: E402


def points(es):
    return es.find('Spectrum').findall('DataPoint')


def counts_of(es):
    return np.array([int(p.text or '0') for p in points(es)], dtype=np.float64)


def live_of(es):
    for tag in ('LiveTime', 'MeasurementTime'):
        el = es.find(tag)
        if el is not None and el.text:
            v = float(el.text)
            if v > 0.0:
                return v
    return 0.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--corpus', required=True)
    ap.add_argument('--curves', required=True)
    ap.add_argument('--base', required=True)
    ap.add_argument('--amp', type=float, default=0.05)
    ap.add_argument('--period', type=float, default=32.0)
    ap.add_argument('--seed', type=int, default=101)
    a = ap.parse_args()

    corpus = os.path.abspath(a.corpus)
    spectra = os.path.join(corpus, 'spectra')
    src = os.path.join(spectra, a.base + '.xml')

    rows = list(csv.DictReader(io.open(os.path.join(a.curves, a.base + '_curves.csv'),
                                       encoding='utf-8-sig')))
    model = np.array([float(r['model']) for r in rows])

    probe = ET.parse(src).getroot()
    fore = probe.find('.//EnergySpectrum')
    back = probe.find('.//BackgroundEnergySpectrum')
    bg = counts_of(back) if back is not None else None
    scale = 0.0
    if bg is not None:
        lb = live_of(back)
        scale = live_of(fore) / lb if lb > 0.0 else 0.0
        if scale <= 0.0:
            bg = None
    print(u'встроенный фон: %s' % (u'нет' if bg is None
                                   else u'%.0f отсчётов, множитель %.4f' % (bg.sum(), scale)))

    ch = np.arange(len(model), dtype=np.float64)
    wig = 1.0 + a.amp * np.sin(2.0 * np.pi * ch / a.period)

    man_head, man_rows = T.read_table(os.path.join(corpus, 'manifest.csv'))
    par_head, par_rows = T.read_table(os.path.join(corpus, 'parts.csv'))
    mat_head, mat_rows = T.read_table(os.path.join(corpus, 'materials.csv'))

    def find(rows_, key):
        for r in rows_:
            if T.csv_cell(r, 0) == key:
                return r
        raise SystemExit('нет строки ' + key)

    man, par, mat = find(man_rows, a.base), find(par_rows, a.base), find(mat_rows, a.base)

    new_man, new_par, new_mat = [], [], []
    print('%-34s %14s %14s %s' % ('key', 'counts', 'sum(mean)', 'sha256'))
    for tag, mean in (('synP', model.copy()), ('synS', model * wig)):
        key = '%s_%s' % (a.base, tag)
        rng = np.random.default_rng(np.random.SeedSequence(
            entropy=int(a.seed), spawn_key=(T.hash_key(key),)))
        tree = ET.parse(src)
        root = tree.getroot()
        f_es = root.find('.//EnergySpectrum')
        b_es = root.find('.//BackgroundEnergySpectrum')

        mean_fore = np.maximum(mean, 0.0)
        if bg is not None:
            mean_fore = mean_fore + bg * scale
        drawn = rng.poisson(mean_fore)
        for p, v in zip(points(f_es), drawn):
            p.text = str(int(v))
        total = int(drawn.sum())
        T.set_num(f_es, 'ValidPulseCount', total, '%d')
        tp = f_es.find('TotalPulseCount')
        if tp is not None:
            tp.text = str(total)

        if bg is not None:
            drawn_b = rng.poisson(bg)
            for p, v in zip(points(b_es), drawn_b):
                p.text = str(int(v))
            T.set_num(b_es, 'ValidPulseCount', int(drawn_b.sum()), '%d')
            tpb = b_es.find('TotalPulseCount')
            if tpb is not None:
                tpb.text = str(int(drawn_b.sum()))

        dst = os.path.join(spectra, key + '.xml')
        tree.write(dst, encoding='utf-8', xml_declaration=True)

        cells = T.split_row(man)
        cells[0] = key
        cells[4] = str(total)
        new_man.append(','.join(cells))
        pc = T.split_row(par)
        pc[0] = key
        new_par.append(','.join(pc))
        mc = T.split_row(mat)
        mc[0] = key
        new_mat.append(','.join(mc))
        print('%-34s %14d %14.0f %s' % (key, total, mean_fore.sum(), T.sha(dst)))

    T.append_rows(os.path.join(corpus, 'manifest.csv'), new_man)
    T.append_rows(os.path.join(corpus, 'parts.csv'), new_par)
    T.append_rows(os.path.join(corpus, 'materials.csv'), new_mat)
    print(u'ожидание для плеча S: eps = amp/sqrt(2) = %.4f %%' % (100.0 * a.amp / np.sqrt(2.0)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
