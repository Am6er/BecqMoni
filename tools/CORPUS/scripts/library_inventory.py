# -*- coding: utf-8 -*-
"""Опись библиотеки спектров: строка на каждый ResultData.

Первый шаг отбора в корпус. Обходит `C:\\Users\\moroz\\YandexDisk\\Спектры`,
разбирает каждый XML и выписывает то, по чему спектр вообще можно отбирать:
модель детектора (она лежит ВНУТРИ файла, имя файла врёт), число каналов, live,
сумму отсчётов, наличие измеренного фона, диапазон энергий и калибровки.

Сборные файлы («Калибровка 13.01.2023.xml» и подобные) несут до шести
ResultData, и каждый из них — самостоятельный спектр, поэтому опись ведётся по
ResultData, а не по файлам.

Каталог `Разное с канала` пропускается: спектры там заведомо плохие.

Запуск:  python library_inventory.py [--out=inventory.json] [--print]
"""
import json
import os
import sys
import xml.etree.ElementTree as ET

ROOT = r'C:\Users\moroz\YandexDisk\Спектры'
SKIP_DIRS = {'Разное с канала'}


def txt(node, path, default=None):
    found = node.find(path)
    if found is None or found.text is None:
        return default
    return found.text.strip()


def num(node, path, default=None):
    value = txt(node, path)
    if value is None:
        return default
    try:
        return float(value)
    except ValueError:
        return default


def spectrum_info(es):
    """Всё, что нужно знать о EnergySpectrum, одним проходом по DataPoint."""
    if es is None:
        return None
    points = es.findall('Spectrum/DataPoint')
    total = 0
    top_nonzero = 0
    for i, point in enumerate(points):
        try:
            value = int(point.text)
        except (TypeError, ValueError):
            value = 0
        total += value
        if value > 0:
            top_nonzero = i
    coefficients = [float(x.text) for x in
                    es.findall('EnergyCalibration/Coefficients/Coefficient')]
    return dict(
        channels=int(num(es, 'NumberOfChannels', len(points)) or len(points)),
        npoints=len(points),
        live=num(es, 'LiveTime') or num(es, 'MeasurementTime'),
        real=num(es, 'MeasurementTime'),
        valid=num(es, 'ValidPulseCount'),
        total=num(es, 'TotalPulseCount'),
        sum_counts=total,
        top_nonzero=top_nonzero,
        ecal=coefficients,
    )


def walk(root=ROOT):
    rows, errors = [], []
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            if not name.lower().endswith('.xml'):
                continue
            path = os.path.join(dirpath, name)
            try:
                tree_root = ET.parse(path).getroot()
            except Exception as ex:
                errors.append((path, 'parse: %s' % ex))
                continue
            # в библиотеке лежат и конфиги устройств, и наборы ROI, и база
            # нуклидов — всё это тоже .xml
            if tree_root.tag != 'ResultDataFile':
                errors.append((path, 'не ResultDataFile: %s' % tree_root.tag))
                continue
            result_data = tree_root.findall('ResultDataList/ResultData')
            for idx, rd in enumerate(result_data):
                foreground = spectrum_info(rd.find('EnergySpectrum'))
                if foreground is None:
                    continue
                fwhm = rd.find('SqrtFwhmCalibration')
                rows.append(dict(
                    path=path, rel=os.path.relpath(path, root),
                    idx=idx, nrd=len(result_data),
                    device=txt(rd, 'DeviceConfigReference/Name'),
                    guid=txt(rd, 'DeviceConfigReference/Guid'),
                    sample=txt(rd, 'SampleInfo/Name'),
                    note=txt(rd, 'SampleInfo/Note'),
                    time=txt(rd, 'SampleInfo/Time'),
                    bgfile=txt(rd, 'BackgroundSpectrumFile'),
                    fg=foreground,
                    bg=spectrum_info(rd.find('BackgroundEnergySpectrum')),
                    fwhm=[float(x.text) for x in
                          fwhm.findall('Coefficients/Coefficient')] if fwhm is not None else [],
                    peaktype=txt(fwhm, 'PeakType') if fwhm is not None else None,
                ))
    return rows, errors


def main():
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'inventory.json')
    for arg in sys.argv[1:]:
        if arg.startswith('--out='):
            out = arg.split('=', 1)[1]
    rows, errors = walk()
    with open(out, 'w', encoding='utf-8') as fh:
        json.dump(dict(rows=rows, errors=errors), fh, ensure_ascii=False, indent=1)
    print('ResultData: %d, не спектры: %d -> %s' % (len(rows), len(errors), out))
    if '--print' in sys.argv:
        for r in sorted(rows, key=lambda r: r['rel']):
            fg = r['fg']
            print('%-58s %-32s %6d ch  live=%-9s cnt=%-9s %s' % (
                r['rel'][:58] + ('#%d' % r['idx'] if r['nrd'] > 1 else ''),
                (r['device'] or '?')[:32], fg['channels'],
                '%.0f' % fg['live'] if fg['live'] else '?',
                '%.3gM' % (fg['sum_counts'] / 1e6) if fg['sum_counts'] >= 1e6
                else '%.0fk' % (fg['sum_counts'] / 1e3),
                'фон' if r['bg'] else ''))


if __name__ == '__main__':
    main()
