# -*- coding: utf-8 -*-
"""Приёмка корпуса: насколько записанные копии попадают в табличные энергии.

Независимо от того, как строилась калибровка, вопрос один: садится ли пик на
свою табличную энергию и насколько промахивается — в долях полуширины, потому
что библиотечный фит опознаёт линию по якорю с допуском 0.5 FWHM
(LibraryPeakFitter.AnchorMatchToleranceFwhm), а «заявляет» её в 0.25 FWHM.
Отсюда и критерий приёмки: медиана невязки заметно меньше 0.25 FWHM.

Разбор идёт по ЗАПИСАННОМУ файлу: берётся его EnergyCalibration и его же
SqrtFwhmCalibration, ничего не подгоняется заново.

Запуск: python check_corpus.py [--key=...] [--verbose]
"""
import os
import sys
import xml.etree.ElementTree as ET

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = os.path.dirname(HERE)
SPECTRA = os.path.join(LAB, 'corpus', 'spectra')

sys.path.insert(0, HERE)
import calibrate                                      # noqa: E402
import corpus_calib                                   # noqa: E402
import corpus_def                                     # noqa: E402
import build_corpus                                   # noqa: E402
from gaussfit import fit_peak, FWHM_SIGMA             # noqa: E402

calibrate.sample_lines = build_corpus.sample_lines

# Порог приёмки в долях FWHM: за ним якорь перестаёт совпадать с пиком.
TOL_OK = 0.25
TOL_WARN = 0.40


def load(path):
    root = ET.parse(path).getroot()
    rd = root.find('ResultDataList/ResultData')
    es = rd.find('EnergySpectrum')
    counts = np.array([int(d.text) for d in es.findall('Spectrum/DataPoint')], dtype=float)
    ecal = [float(x.text) for x in es.findall('EnergyCalibration/Coefficients/Coefficient')]
    fw = rd.find('SqrtFwhmCalibration')
    fwhm = [float(x.text) for x in fw.findall('Coefficients/Coefficient')] if fw is not None else None
    return counts, ecal, fwhm, rd


def fwhm_ch_at(coef, ch):
    v = coef[0] + coef[1] * ch + coef[2] * ch * ch
    return float(np.sqrt(max(v, 1e-6)))


def check(entry, verbose=False):
    path = os.path.join(SPECTRA, entry['key'] + '.xml')
    if not os.path.isfile(path):
        return dict(key=entry['key'], err='нет файла')
    counts, ecal_coef, fwhm_coef, rd = load(path)
    cal = corpus_calib.Ecal(ecal_coef, len(counts))
    if fwhm_coef is None:
        return dict(key=entry['key'], err='нет SqrtFwhmCalibration')

    ent = dict(entry)
    ent['wanted'] = build_corpus.wanted_lines(entry)
    # разрешение берём из САМОГО файла — проверяем то, что записано
    def res_fn(e):
        ch = cal.channel(e)
        return fwhm_ch_at(fwhm_coef, ch) * abs(cal.dEdch(ch))

    # Порог чистоты ослабляется, пока не наберётся три линии: на 1024-канальном
    # приборе с полушириной 12 % чистых линий нет вообще, и фиксированный порог
    # оставлял фон и Obsidian вовсе без проверки.
    rows = []
    for purity_bar in (0.85, 0.75, 0.60, 0.45):
        rows = []
        for e_ref, label, purity, e_table in calibrate.curate(
                ent, res_fn, min_purity=purity_bar):
            ch0 = cal.channel(e_ref)
            if ch0 < 5 or ch0 > len(counts) - 6:
                continue
            fw_ch = fwhm_ch_at(fwhm_coef, ch0)
            r = fit_peak(counts, ch0, fw_ch / FWHM_SIGMA, window=2.4)
            if r is None or r['sig'] < 8.0:
                continue
            if abs(r['mu'] - ch0) > 1.5 * fw_ch:
                continue
            width_ratio = r['fwhm'] / fw_ch
            if not 0.4 <= width_ratio <= 2.5:
                continue
            rows.append(dict(e=e_ref, label=label, purity=purity,
                             d_kev=cal.energy(r['mu']) - e_ref,
                             d_fwhm=(r['mu'] - ch0) / fw_ch,
                             width_ratio=width_ratio, sig=r['sig']))
        if len(rows) >= 3:
            break
    if not rows:
        return dict(key=entry['key'], det=entry['det'], n=0, err='ни одной линии')
    d = np.array([abs(r['d_fwhm']) for r in rows])
    wr = np.array([r['width_ratio'] for r in rows])
    out = dict(key=entry['key'], det=entry['det'], n=len(rows),
               med=float(np.median(d)), p90=float(np.percentile(d, 90)),
               worst=float(d.max()),
               med_kev=float(np.median([abs(r['d_kev']) for r in rows])),
               width_med=float(np.median(wr)), rows=rows)
    if verbose:
        print('  %-24s %8s %9s %8s %7s' % ('линия', 'E, кэВ', 'd, кэВ', 'd/FWHM', 'ширина'))
        for r in sorted(rows, key=lambda x: x['e']):
            print('  %-24s %8.2f %+9.2f %+8.2f %7.2f' % (
                r['label'][:24], r['e'], r['d_kev'], r['d_fwhm'], r['width_ratio']))
    return out


def main():
    only = None
    verbose = '--verbose' in sys.argv
    for a in sys.argv[1:]:
        if a.startswith('--key='):
            only = set(a.split('=', 1)[1].split(','))

    print('%-20s %-9s %4s %8s %8s %8s %8s  %s' % (
        'спектр', 'детектор', 'лин', 'медиана', 'p90', 'макс', 'ширина', 'вердикт'))
    bad, warn = [], []
    for e in corpus_def.ALL:
        if only and e['key'] not in only:
            continue
        if verbose:
            print('== %s' % e['key'])
        r = check(e, verbose)
        if r.get('err'):
            print('%-20s %-9s  --  %s' % (r['key'], e['det'], r['err']))
            warn.append(r['key'])
            continue
        if r['med'] > TOL_WARN:
            verdict, sink = 'ПЛОХО', bad
        elif r['med'] > TOL_OK:
            verdict, sink = 'на грани', warn
        else:
            verdict, sink = 'ок', None
        if sink is not None:
            sink.append(r['key'])
        print('%-20s %-9s %4d %8.3f %8.3f %8.3f %8.2f  %s' % (
            r['key'], r['det'], r['n'], r['med'], r['p90'], r['worst'],
            r['width_med'], verdict))
    print('\nплохих: %d %s' % (len(bad), ', '.join(bad)))
    print('спорных/непроверенных: %d %s' % (len(warn), ', '.join(warn)))


if __name__ == '__main__':
    main()
