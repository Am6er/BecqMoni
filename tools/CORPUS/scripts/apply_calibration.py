# -*- coding: utf-8 -*-
"""Разложить девять тестовых спектров в scripts/spectra/, применив УЖЕ ПОСЧИТАННЫЕ
калибровки из data/calibration.json.

Штатный calibrate.py заново подбирает энерго- и FWHM-калибровку, но импортирует
модуль gainscan, которого в дереве нет, — стадия подбора не запускается. Нам она и
не нужна: коэффициенты той самой калибровки, на которой мерялись числа отчёта,
сохранены в calibration.json. Применяем их тем же write_spectrum, что и оригинал,
чтобы копии спектров получились байт-в-байт такими же.

⛔ ИСТОЧНИК — БИБЛИОТЕКА СОПРОВОЖДАЮЩЕГО, И ОНА ДВИЖЕТСЯ (`B8`). Замер
07.09.2026 (полоса П29): сплошной прогон без единой правки переписал ДВЕ копии
из девяти — `AS80_Th232WT20` (319 517 -> 645 734 байта) и `ASN16_Charoite`, —
потому что в библиотечные оригиналы 02.09.2026 добавлен узел `<Efficiency>`.
Каталог `scripts/spectra` в git НЕ ЛЕЖИТ, то есть прежних байтов после такого
прогона не существует. Отсюда ключ `--only=`: перекалибровка одного спектра не
обязана трогать остальные восемь.

    python tools/CORPUS/scripts/apply_calibration.py
    python tools/CORPUS/scripts/apply_calibration.py --only=AS80_Charoite

⚠ `bg_ecal` в записи калибровки — СВОЯ шкала встроенного фона; без него фон
получает шкалу переднего плана, как было всегда. Зачем он появился и чем
измерен — в шапке `calibrate.write_spectrum` и в `A278`.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = os.path.dirname(HERE)

sys.path.insert(0, HERE)

import calibrate                                        # noqa: E402

only = None
for a in sys.argv[1:]:
    if a.startswith('--only='):
        only = set(a.split('=', 1)[1].split(','))
    else:
        print('неизвестный ключ:', a)
        sys.exit(2)

cal = {e['key']: e for e in json.load(
    open(os.path.join(LAB, 'data', 'calibration.json'), encoding='utf-8'))}

entries = [e for e in calibrate.SPECTRA if only is None or e['key'] in only]
if only is not None:
    unknown = only - {e['key'] for e in calibrate.SPECTRA}
    if unknown:
        print('НЕТ ТАКИХ КЛЮЧЕЙ В calibrate.SPECTRA:', ', '.join(sorted(unknown)))
        sys.exit(2)

written = 0
for entry in entries:
    key = entry['key']
    if key not in cal:
        print('НЕТ КАЛИБРОВКИ:', key)
        continue
    if not os.path.isfile(entry['path']):
        print('НЕТ ФАЙЛА:', key, entry['path'])
        continue
    c = cal[key]
    dest = calibrate.write_spectrum(entry, c['ecal'], c['fwhm_ch'],
                                    bg_ecal_coef=c.get('bg_ecal'))
    written += 1
    print('%-18s ch=%-5s live=%-9s rms=%.2f %s-> %s' % (
        key, c.get('channels'), c.get('live'), c.get('rms', -1),
        'фон: своя шкала ' if c.get('bg_ecal') else '', os.path.basename(dest)))

print('\nзаписано:', written, 'из', len(entries))
