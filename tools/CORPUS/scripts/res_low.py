# -*- coding: utf-8 -*-
u"""Модель разрешения НИЖЕ 180 кэВ: измерить и сравнить с экстраполяцией (`V2`).

Зачем. Модель разрешения группы строится по линиям, которых внизу шкалы почти
нет, и ниже ~180 кэВ она не измерена, а ПРОДОЛЖЕНА формулой. `B17` упёрлась
ровно в это: у двух спектров с главными линиями 59.5 и 88 кэВ модель не
описывает спектр на 95 %, а три других кандидата отпали измерением.

Что делает. Читает СОБСТВЕННЫЕ копии корпуса — в библиотеку не лезет. Линии
берёт тем же путём, что и сборка (`calibrate.curate` по составу из манифеста),
ширину меряет тем же `calibrate.measure`, а сравнивает с моделью группы из
`corpus/detectors.csv`. То есть числа получаются ТЕМ ЖЕ инструментом, которым
корпус собран, — иначе расхождение мерило бы разницу инструментов.

⚠ Оценка СНИЗУ по величине расхождения: `calibrate.measure` принимает пик,
только если его ширина укладывается в 0.5…2.0 модельной, — то есть самые
непохожие на модель пики в выборку не попадают вовсе. Настоящее расхождение не
меньше напечатанного.

Ничего не пишет: печатает точки и сводку по группам.

    python tools/CORPUS/scripts/res_low.py [--below=200] [--det=G1S16]
"""
import argparse
import csv
import io
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import calibrate
import corpus_calib
from build_corpus import wanted_lines, sample_lines
from spectrum import Spectrum

CORPUS = os.path.join(HERE, os.pardir, 'corpus')
FWHM_SIGMA = 2.3548


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--below', type=float, default=200.0, help=u'порог, кэВ')
    ap.add_argument('--det', default=None, help=u'только эта группа')
    args = ap.parse_args()

    calibrate.sample_lines = sample_lines

    dets = {}
    with io.open(os.path.join(CORPUS, 'detectors.csv'), encoding='utf-8-sig', newline='') as fh:
        for r in csv.DictReader(fh):
            dets[list(r.values())[0]] = [float(r['res_c0']), float(r['res_c1']), float(r['res_c2'])]

    rows = []
    with io.open(os.path.join(CORPUS, 'manifest.csv'), encoding='utf-8-sig', newline='') as fh:
        for r in csv.DictReader(fh):
            r['key'] = list(r.values())[0]
            rows.append(r)

    print(u'модель разрешения ниже %.0f кэВ: измерено против экстраполяции (V2)' % args.below)
    print(u'%-22s %-9s %8s %10s %9s %8s' % (u'спектр', u'группа', u'E, кэВ',
                                            u'изм. ПШПВ', u'модель', u'изм/мод'))
    by_det = {}
    for row in rows:
        det = row['det']
        if (args.det and det != args.det) or det not in dets:
            continue

        path = os.path.join(CORPUS, 'spectra', row['key'] + '.xml')
        if not os.path.isfile(path):
            continue

        entry = dict(key=row['key'], det=det,
                     nuclides=[x for x in (row.get('nuclides') or '').split(';') if x],
                     chains=[x for x in (row.get('chains') or '').split(';') if x])
        entry['wanted'] = wanted_lines(entry)
        if not entry['wanted']:
            continue

        sp = Spectrum(path)
        ecal = corpus_calib.Ecal(sp.ecal, sp.n)
        model = corpus_calib.resolution_fn(dets[det])
        lines = calibrate.curate(entry, model, min_purity=0.45)
        if not lines:
            continue

        # Ширина меряется тем же `calibrate.measure`, которым корпус собран:
        # свой фиттер здесь мерил бы разницу фиттеров, а не разрешения.
        # Допуск по ширине СНЯТ (`width_tol=None` даёт 0.5…2.0) нарочно — мы
        # как раз и ищем, насколько ширина отличается от модели.
        found = calibrate.measure(sp.counts, ecal, lines, model,
                                  tol_frac=0.004, min_sig=5.0)
        for a in found:
            energy = ecal.energy(a['ch'])
            if energy > args.below:
                continue
            measured = a['fwhm'] * ecal.dEdch(a['ch'])
            want = model(energy)
            if not (want > 0):
                continue
            ratio = measured / want
            print(u'%-22s %-9s %8.1f %10.2f %9.2f %8.2f'
                  % (row['key'], det, energy, measured, want, ratio))
            by_det.setdefault(det, []).append(ratio)

    print()
    print(u'%-10s %6s %9s %9s %9s' % (u'группа', u'точек', u'медиана', u'мин', u'макс'))
    for det in sorted(by_det):
        r = np.array(by_det[det])
        print(u'%-10s %6d %9.2f %9.2f %9.2f' % (det, len(r), np.median(r), r.min(), r.max()))

    if by_det:
        allr = np.concatenate([np.array(v) for v in by_det.values()])
        print()
        print(u'всего точек %d, медиана изм/мод %.2f' % (len(allr), np.median(allr)))


if __name__ == '__main__':
    main()
