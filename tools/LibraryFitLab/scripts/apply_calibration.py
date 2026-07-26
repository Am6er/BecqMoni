# -*- coding: utf-8 -*-
"""Разложить девять тестовых спектров в scripts/spectra/, применив УЖЕ ПОСЧИТАННЫЕ
калибровки из data/calibration.json.

Штатный calibrate.py заново подбирает энерго- и FWHM-калибровку, но импортирует
модуль gainscan, которого в дереве нет, — стадия подбора не запускается. Нам она и
не нужна: коэффициенты той самой калибровки, на которой мерялись числа отчёта,
сохранены в calibration.json. Применяем их тем же write_spectrum, что и оригинал,
чтобы копии спектров получились байт-в-байт такими же.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\LibraryFitLab'
SCRIPTS = os.path.join(LAB, 'scripts')

sys.path.insert(0, HERE)        # заглушка gainscan
sys.path.append(SCRIPTS)

import calibrate                                        # noqa: E402

cal = {e['key']: e for e in json.load(
    open(os.path.join(LAB, 'data', 'calibration.json'), encoding='utf-8'))}

written = 0
for entry in calibrate.SPECTRA:
    key = entry['key']
    if key not in cal:
        print('НЕТ КАЛИБРОВКИ:', key)
        continue
    if not os.path.isfile(entry['path']):
        print('НЕТ ФАЙЛА:', key, entry['path'])
        continue
    c = cal[key]
    dest = calibrate.write_spectrum(entry, c['ecal'], c['fwhm_ch'])
    written += 1
    print('%-18s ch=%-5s live=%-9s rms=%.2f -> %s' % (
        key, c.get('channels'), c.get('live'), c.get('rms', -1), os.path.basename(dest)))

print('\nзаписано:', written, 'из', len(calibrate.SPECTRA))
