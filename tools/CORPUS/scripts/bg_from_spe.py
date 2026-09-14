# -*- coding: utf-8 -*-
u"""Вернуть корпусным спектрам ПОЛНЫЙ фон из оригинального `.spe` (S44).

ЗАЧЕМ. У одиннадцати спектров G1S понятной части встроенный фон обрезан по
верхним каналам — 1012 или 1004 против 1024 у самого спектра, — а
`FsaAnalyzer` при несовпадении числа каналов молча ставит `background = null`.
Фон есть в файле, манифест пишет «встроен», проба печатает `background=1`, а
вычитания нет. Обрезку сделал сторонний конвертер, которым эти спектры попали
в библиотеку (`import_vibe.py`); оригиналы поверки целы.

ПОЧЕМУ НЕ НУЛЯМИ. В обрезанных каналах живой счёт (8–13 отсчётов на канал,
83 и 198 суммарно) — дополнение нулями было бы подделкой данных там, где
настоящие данные лежат рядом на диске.

ЧТО ДЕЛАЕТСЯ. Ссылка на фоновый файл записана в самом спектре
(`<BackgroundSpectrumFile>`); по ней берётся оригинал серии поверки, и узел
`<BackgroundEnergySpectrum>` получает все свои каналы. Правка ХИРУРГИЧЕСКАЯ —
меняются только `NumberOfChannels`, счётчики импульсов и массив `DataPoint`
внутри узла фона; калибровки, времена и передний план не трогаются.

СЕРИИ ПОВЕРКИ НАКОПИТЕЛЬНЫЕ. `фон пустая защита_01…_15` — это не пятнадцать
независимых измерений, а одно нарастающее: _01 = 3600 с, _15 = 54 000 с,
каждый следующий включает предыдущий. Складывать их нельзя — берётся
последний. Проверено против встроенного фона: первые 1012 (1004) каналов
последнего файла совпали с ним ДО ОТСЧЁТА.

    python tools/CORPUS/scripts/bg_from_spe.py [--spectra=…] [--apply]

⛔ `--apply` ВЫКЛЮЧЕН по умолчанию: без него печатается план.
"""
import argparse
import glob
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from corpus_paths import resolve                     # noqa: E402
from spe_import import read_spe                      # noqa: E402

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

LIB = r'C:\Users\moroz\YandexDisk\Спектры'
POVERKI = os.path.join(LIB, 'Спектры источники эталоны', 'Spe - поверки')

# Имя, записанное в спектре -> оригинал серии поверки (ПОСЛЕДНИЙ файл).
SOURCES = {
    'background_bg_2016_empty_shield_point5cm.spe':
        os.path.join('Поверка 2016', 'фон пустая защита', 'фон пустая защита_15.spe'),
    'background_bg_2016_open_lid_point25cm.spe':
        os.path.join('Поверка 2016', 'Фон с открытыми крышками', 'фон открытый_15.spe'),
}

SPECTRA_DEFAULT = os.path.join(HERE, os.pardir, 'corpus', 'spectra')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--spectra', default=SPECTRA_DEFAULT)
    ap.add_argument('--apply', action='store_true')
    args = ap.parse_args()

    fixed = 0
    for path in sorted(glob.glob(os.path.join(args.spectra, '*.xml'))):
        key = os.path.splitext(os.path.basename(path))[0]
        text = io.open(path, encoding='utf-8-sig').read()
        start = text.find('<BackgroundEnergySpectrum>')
        if start < 0:
            continue

        end = text.find('</BackgroundEnergySpectrum>', start)
        block = text[start:end]
        main_channels = int(re.search(r'<NumberOfChannels>(\d+)', text).group(1))
        bg_channels = int(re.search(r'<NumberOfChannels>(\d+)', block).group(1))
        if bg_channels == main_channels:
            continue

        reference = re.search(r'<BackgroundSpectrumFile>([^<]*)', text)
        name = reference.group(1) if reference else ''
        source = SOURCES.get(name)
        if source is None:
            print('%-22s фон %d из %d каналов, оригинал НЕ НАЗВАН (%s) — пропуск'
                  % (key, bg_channels, main_channels, name or '-'))
            continue

        head, counts = read_spe(resolve(os.path.join(POVERKI, source)))
        theirs = [int(x) for x in re.findall(r'<DataPoint>(-?\d+)</DataPoint>', block)]
        if counts[:len(theirs)] != theirs:
            print('%-22s ОРИГИНАЛ НЕ ТОТ: первые %d каналов не совпали — пропуск'
                  % (key, len(theirs)))
            continue

        if len(counts) != main_channels:
            print('%-22s у оригинала %d каналов, спектру нужно %d — пропуск'
                  % (key, len(counts), main_channels))
            continue

        print('%-22s фон %d -> %d каналов, +%d отсчётов (%s)'
              % (key, bg_channels, main_channels, sum(counts) - sum(theirs),
                 os.path.basename(source)))
        fixed += 1
        if not args.apply:
            continue

        points = u'\n'.join(u'          <DataPoint>%d</DataPoint>' % c for c in counts)
        block2 = re.sub(r'<NumberOfChannels>\d+</NumberOfChannels>',
                        '<NumberOfChannels>%d</NumberOfChannels>' % main_channels,
                        block, count=1)
        for tag in ('ValidPulseCount', 'TotalPulseCount'):
            block2 = re.sub(r'<%s>\d+</%s>' % (tag, tag),
                            '<%s>%d</%s>' % (tag, sum(counts), tag), block2, count=1)
        block2 = re.sub(r'<Spectrum>.*?</Spectrum>',
                        u'<Spectrum>\n%s\n        </Spectrum>' % points,
                        block2, count=1, flags=re.S)
        io.open(path, 'w', encoding='utf-8', newline='').write(
            u'\ufeff' + text[:start] + block2 + text[end:])

    print()
    print('спектров с обрезанным фоном: %d%s'
          % (fixed, '' if args.apply else '  (--apply не задан, файлы не тронуты)'))


if __name__ == '__main__':
    main()
