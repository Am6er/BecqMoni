#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Форма пика и низкая точка ПШПВ в рабочих каталогах корпуса.

Корпус собран с приведением формы пика к гауссиане (`build_corpus.py`, п. 5) —
это было сознательное упрощение исходной девятки, но у приборов, где форма
измерена, оно делает образ полноспектральной декомпозиции заведомо неверным:
у ASN16 конфиг устройства объявляет ExpGaussExp с хвостами 1.5/5, у AS80x80 —
1.15/4, а в спектрах корпуса стоит PeakType = 0.

Вторая правка — низкая опорная точка ПШПВ. Модель разрешения корпуса
(`corpus_calib.fit_resolution_kev`) намеренно без свободного члена: почти все
опорные линии выше 180 кэВ, и c0 по ним не определён. Ниже опорных линий кривая
поэтому не измерена, а экстраполирована, и врёт. Точка (E, ПШПВ) в кэВах,
заданная ключом `--anchor`, добавляется к выборке кривой и полином
FWHM²(канал) = c0 + c1·ch + c2·ch² пересчитывается по спектру.

Скрипт правит ТОЛЬКО рабочие каталоги `wd_*` (они gitignored). Корпус
`corpus/spectra` — эталон и остаётся нетронутым; `--restore` возвращает рабочие
копии к нему.

    python peakshape.py --restore
    python peakshape.py --shape ASN16,AS80x80
    python peakshape.py --anchor ASN16=58.4:16.1
    python peakshape.py --anchor ASN16=59.5:10.5 --shape ASN16
    python peakshape.py --report ASN16
"""
import argparse
import os
import re
import shutil
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS_SPECTRA = os.path.join(HERE, '..', 'corpus', 'spectra')

# группа -> рабочий каталог
WD = {
    'AS1PRO': 'wd_AS1PRO', 'AS80x80': 'wd_AS80x80', 'ASN16': 'wd_ASN16',
    'ASN3': 'wd_ASN3', 'ASN8_1024': 'wd_ASN8_1024', 'ASN8_2048': 'wd_ASN8_2048',
    'ASN8_3000': 'wd_ASN8_3000', 'ASN8_4096': 'wd_ASN8_4096',
    'ASN8_8192': 'wd_ASN8_8192', 'CZT': 'wd_CZT', 'CZT_TECD': 'wd_CZT_TECD',
    # `G1S` разделена по эпохе поверки 15.08.2026 (см. corpus_def.py); прежнее
    # имя оставлено на случай старых рабочих каталогов.
    'G1S': 'wd_G1S', 'G1S16': 'wd_G1S16', 'G1S24': 'wd_G1S24',
    'GS4000': 'wd_GS4000', 'HPGE': 'wd_HPGE',
    'HPGE_GEM': 'wd_HPGE_GEM', 'HPGE_GMX': 'wd_HPGE_GMX',
    'LABR_BRIL': 'wd_LABR_BRIL', 'LaBr3': 'wd_LaBr3', 'OBS': 'wd_OBS',
    'RC101': 'wd_RC101', 'RC103': 'wd_RC103', 'RC103g': 'wd_RC103g',
    'SrI2': 'wd_SrI2',
}

# Форма пика по конфигам устройства пользователя (%AppData%\BecqMoni\config\device).
# Только то, что там действительно измерено и записано: у остальных приборов
# формы нет, и ставить её наугад нельзя — гауссиана честнее выдумки.
SHAPES = {
    'ASN16':   (1, 1.5, 5.0),    # 1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml
    'AS80x80': (1, 1.15, 4.0),   # Atom Spectra 80x80.xml
}


def wd_dir(group):
    d = os.path.join(HERE, WD[group])
    if not os.path.isdir(d):
        sys.exit('нет рабочего каталога %s' % d)
    return d


def spectra_of(group):
    d = os.path.join(wd_dir(group), 'spectra')
    return [os.path.join(d, f) for f in sorted(os.listdir(d)) if f.endswith('.xml')]


def read_block(text, tag):
    m = re.search(r'<%s>(.*?)</%s>' % (tag, tag), text, re.S)
    return m


def ecal_of(text):
    m = read_block(text, 'EnergyCalibration')
    return [float(x) for x in re.findall(r'<Coefficient>([-0-9.eE+]+)</Coefficient>', m.group(1))]


def fwhm_of(text):
    m = read_block(text, 'SqrtFwhmCalibration')
    if m is None:
        return None, None
    return m, [float(x) for x in re.findall(r'<Coefficient>([-0-9.eE+]+)</Coefficient>', m.group(1))]


def energy(ec, ch):
    return sum(c * np.power(ch, i) for i, c in enumerate(ec))


def dEdch(ec, ch):
    return sum(i * c * np.power(ch, i - 1) for i, c in enumerate(ec) if i > 0)


def channel_of(ec, e, nch):
    ch = np.arange(0.0, nch, 1.0)
    return float(np.interp(e, energy(ec, ch), ch))


def nchannels(text):
    m = re.search(r'<NumberOfChannels>(\d+)</NumberOfChannels>', text)
    return int(m.group(1))


def refit_with_anchor(ec, coef, nch, anchor_e, anchor_fwhm_kev):
    """Пересчитать FWHM²(ch) = c0 + c1·ch + c2·ch², добавив измеренную точку.

    Существующая кривая выше опорных линий подтверждена данными, ниже — нет.
    Поэтому она пересемплируется в точках выше порога и вместе с якорем
    подгоняется заново; вес якоря — как у всей верхней выборки, чтобы одна
    точка не осталась в меньшинстве против четырёхсот пересемплированных.
    """
    anchor_ch = channel_of(ec, anchor_e, nch)
    anchor_fw = anchor_fwhm_kev / abs(dEdch(ec, anchor_ch))

    lo = channel_of(ec, 180.0, nch)          # ниже 180 кэВ опорных линий почти нет
    if not np.isfinite(lo) or lo <= anchor_ch:
        lo = anchor_ch * 2.0
    ch = np.linspace(lo, nch - 1.0, 400)
    fw = np.sqrt(np.maximum(coef[0] + coef[1] * ch + coef[2] * ch * ch, 1e-9))

    x = np.concatenate([ch, np.full(len(ch), anchor_ch)])
    y = np.concatenate([fw, np.full(len(ch), anchor_fw)])
    A = np.vstack([np.ones_like(x), x, x * x]).T
    new, *_ = np.linalg.lstsq(A, y ** 2, rcond=None)

    # ПШПВ обязана расти по шкале: подгонка с якорем может выгнуть параболу
    # вниз, и тогда квадратичный член выбрасывается.
    probe = np.arange(1.0, nch, 1.0)
    v = new[0] + new[1] * probe + new[2] * probe * probe
    if not (np.all(v > 0) and np.all(np.diff(np.sqrt(np.maximum(v, 0.0))) >= -1e-12)):
        A = np.vstack([np.ones_like(x), x]).T
        lin, *_ = np.linalg.lstsq(A, y ** 2, rcond=None)
        new = np.array([lin[0], lin[1], 0.0])
    return new, anchor_ch, anchor_fw


def rewrite(path, coef=None, shape=None):
    text = open(path, encoding='utf-8', errors='surrogateescape').read()
    m, old = fwhm_of(text)
    if m is None:
        return False
    block = m.group(1)
    if coef is not None:
        body = ''.join('<Coefficient>%r</Coefficient>' % float(c) for c in coef)
        block = re.sub(r'<Coefficients>.*?</Coefficients>',
                       '<Coefficients>%s</Coefficients>' % body, block, flags=re.S)
    if shape is not None:
        pt, left, right = shape
        block = re.sub(r'<PeakType>\d+</PeakType>', '<PeakType>%d</PeakType>' % pt, block)
        block = re.sub(r'<ExpGaussExpLeftTail>[^<]*</ExpGaussExpLeftTail>',
                       '<ExpGaussExpLeftTail>%r</ExpGaussExpLeftTail>' % left, block)
        block = re.sub(r'<ExpGaussExpRightTail>[^<]*</ExpGaussExpRightTail>',
                       '<ExpGaussExpRightTail>%r</ExpGaussExpRightTail>' % right, block)
    text = text[:m.start(1)] + block + text[m.end(1):]
    open(path, 'w', encoding='utf-8', errors='surrogateescape').write(text)
    return True


def do_restore(groups):
    for g in groups:
        for p in spectra_of(g):
            src = os.path.join(CORPUS_SPECTRA, os.path.basename(p))
            shutil.copyfile(src, p)
        print('%-10s восстановлено из корпуса' % g)


def do_shape(groups):
    for g in groups:
        if g not in SHAPES:
            print('%-10s формы в конфиге устройства нет — пропуск' % g)
            continue
        shape = SHAPES[g]
        n = sum(1 for p in spectra_of(g) if rewrite(p, shape=shape))
        print('%-10s PeakType=%d хвосты %.2f/%.2f -> %d спектров' % ((g,) + shape + (n,)))


def do_anchor(items):
    for g, (ae, af) in items:
        for p in spectra_of(g):
            text = open(p, encoding='utf-8', errors='surrogateescape').read()
            ec = ecal_of(text)
            _, coef = fwhm_of(text)
            nch = nchannels(text)
            new, ach, afw = refit_with_anchor(ec, coef, nch, ae, af)
            before = np.sqrt(max(coef[0] + coef[1] * ach + coef[2] * ach * ach, 0.0))
            after = np.sqrt(max(new[0] + new[1] * ach + new[2] * ach * ach, 0.0))
            rewrite(p, coef=new)
            print('%-24s якорь %.1f кэВ: ПШПВ %.2f -> %.2f кан (цель %.2f)' %
                  (os.path.basename(p), ae, before, after, afw))


def do_report(groups):
    for g in groups:
        for p in spectra_of(g):
            text = open(p, encoding='utf-8', errors='surrogateescape').read()
            ec = ecal_of(text)
            _, coef = fwhm_of(text)
            nch = nchannels(text)
            pt = re.search(r'<PeakType>(\d+)</PeakType>', text).group(1)
            cells = []
            for e in (59.5, 238.6, 662.0, 1460.8, 2614.5):
                ch = channel_of(ec, e, nch)
                fw = np.sqrt(max(coef[0] + coef[1] * ch + coef[2] * ch * ch, 0.0))
                cells.append('%5.2f%%' % (100 * fw * abs(dEdch(ec, ch)) / e))
            print('%-24s PeakType=%s  %s' % (os.path.basename(p), pt, ' '.join(cells)))


def parse_groups(value):
    out = []
    for g in value.split(','):
        g = g.strip()
        if not g:
            continue
        if g not in WD:
            sys.exit('нет такой группы: %s' % g)
        out.append(g)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--restore', help='группы через запятую (или all)')
    ap.add_argument('--shape', help='группы через запятую (или all)')
    ap.add_argument('--anchor', action='append', default=[],
                    help='ГРУППА=E:FWHM в кэВ, например ASN16=58.4:16.1')
    ap.add_argument('--report', help='группы через запятую (или all)')
    args = ap.parse_args()

    def groups(v):
        return list(WD) if v == 'all' else parse_groups(v)

    if args.restore:
        do_restore(groups(args.restore))
    if args.anchor:
        items = []
        for a in args.anchor:
            g, _, rest = a.partition('=')
            e, _, f = rest.partition(':')
            items.append((parse_groups(g)[0], (float(e), float(f))))
        do_anchor(items)
    if args.shape:
        do_shape(groups(args.shape))
    if args.report:
        do_report(groups(args.report))


if __name__ == '__main__':
    main()
