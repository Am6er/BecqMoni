# -*- coding: utf-8 -*-
u"""Завести 26 пар слов отказа мощности дозы в оба `Properties/Resources*.resx`
и согласованно — свойства в `Resources.Designer.cs` (`A237`).

⛔ Английская надпись НЕ придумывается: она обязана СОВПАСТЬ с запасным
аргументом самого вызова `DoseRateCoefficients.Text(key, fallback)` — иначе
заведение ключа МЕНЯЕТ надпись на экране, а строка просила её сохранить.
Сверка идёт машинно, до записи: расхождение — отказ, файлы не трогаются.

⛔ Пишется БАЙТАМИ: BOM + CRLF (`tools/resx_format.py`). Текстовый режим
поставил бы LF, четыре сторожа покраснели бы, а `git diff` этого не показал бы
(`.gitattributes` держит `eol=crlf`).
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, os.path.join(ROOT, 'tools'))
import check_resx_designer as C

APP = os.path.join(ROOT, 'BecquerelMonitor')

# ключ -> (английская надпись = запасная из кода, русский перевод)
PAIRS = [
    ('DoseRateBadEfficiency',
     u'Dose rate: the efficiency curve gives {0} at {1:f0} keV — division by it is meaningless.',
     u'Мощность дозы: кривая эффективности даёт {0} на {1:f0} кэВ — делить на такое бессмысленно.'),
    ('DoseRateBadScale',
     u'Dose rate: the energy scale is degenerate ({0}...{1} keV over {2} channels).',
     u'Мощность дозы: шкала энергии вырождена ({0}...{1} кэВ на {2} каналов).'),
    ('DoseRateCurveBadEnergy',
     u'Dose rate: the efficiency curve has a point at {0} keV.',
     u'Мощность дозы: у кривой эффективности точка с негодной энергией {0} кэВ.'),
    ('DoseRateCurveBadValue',
     u'Dose rate: the efficiency curve gives {0} at {1:f1} keV.',
     u'Мощность дозы: кривая эффективности даёт негодное значение {0} на {1:f1} кэВ.'),
    ('DoseRateCurveTooShort',
     u'Dose rate: the efficiency curve has {0} point(s), at least two are needed.',
     u'Мощность дозы: у кривой эффективности точек: {0}, а нужно не меньше двух.'),
    ('DoseRateEmptyRange',
     u'Dose rate: the device scale ({0:f0}...{1:f0} keV) does not overlap the range where the coefficients are defined ({2:f0}...{3:f0} keV).',
     u'Мощность дозы: шкала прибора ({0:f0}...{1:f0} кэВ) не пересекается с областью, где заданы коэффициенты ({2:f0}...{3:f0} кэВ).'),
    ('DoseRateEmptySpectrum',
     u'Dose rate: the reference spectrum has no channels.',
     u'Мощность дозы: у эталонного спектра нет каналов.'),
    ('DoseRateEnergyNotPositive',
     u'Dose rate: energy {0} keV is not positive.',
     u'Мощность дозы: энергия {0} кэВ не положительна.'),
    ('DoseRateEtalonEmpty',
     u'Dose rate: the reference spectrum has no counts inside the ranges — there is nothing to calibrate against.',
     u'Мощность дозы: в диапазонах нет ни одного отсчёта эталонного спектра — калиброваться не по чему.'),
    ('DoseRateFileUnusable',
     u'Dose rate: {0} has no spectrum with an energy calibration, channels and a non-zero measurement time.',
     u'Мощность дозы: в {0} нет спектра с калибровкой энергии, каналами и ненулевым временем набора.'),
    ('DoseRateLsrmNoHeader',
     u'Dose rate: {0} does not start with the LSRM header "Energy, keV / Efficiency / Uncertainty, %" — the first line reads "{1}".',
     u'Мощность дозы: {0} не начинается с заголовка ЛСРМ "Energy, keV / Efficiency / Uncertainty, %" — в первой строке стоит "{1}".'),
    ('DoseRateLsrmNoPoints',
     u'Dose rate: {0} yielded {1} curve point(s) — at least {2} are needed ({3} more were dropped as declared to more than {4:f0} % uncertainty).',
     u'Мощность дозы: из {0} получено точек кривой: {1}, а нужно не меньше {2} (ещё {3} отброшено с объявленной погрешностью больше {4:f0} %).'),
    ('DoseRateLsrmShortLine',
     u'Dose rate: {0} (line {1}) has {2} column(s) instead of three (energy, efficiency, uncertainty): "{3}".',
     u'Мощность дозы: в {0} (строка {1}) столбцов: {2} вместо трёх (энергия, эффективность, погрешность): "{3}".'),
    ('DoseRateNoCalibration',
     u'Dose rate: the reference spectrum has no energy calibration — the channels cannot be turned into keV.',
     u'Мощность дозы: у эталонного спектра нет калибровки энергии — каналы не перевести в кэВ.'),
    ('DoseRateNoEfficiency',
     u'Dose rate: no efficiency curve is selected.',
     u'Мощность дозы: кривая эффективности не выбрана.'),
    ('DoseRateNoElement',
     u'Dose rate: element Z={0} is missing from the material database.',
     u'Мощность дозы: элемента Z={0} нет в базе веществ.'),
    ('DoseRateNoExpected',
     u'Dose rate: the declared dose rate of the source must be positive.',
     u'Мощность дозы: объявленная мощность дозы источника обязана быть положительной.'),
    ('DoseRateNoGrid',
     u'Dose rate: the energy grid is empty.',
     u'Мощность дозы: сетка энергий пуста.'),
    ('DoseRateNoScale',
     u'Dose rate: neither the device configuration nor the spectrum has an energy scale — there is nothing to build the ranges on.',
     u'Мощность дозы: ни у конфигурации прибора, ни у спектра нет шкалы энергии — диапазоны строить не на чём.'),
    ('DoseRateNoSpectrum',
     u'Dose rate: no reference spectrum is selected.',
     u'Мощность дозы: эталонный спектр не выбран.'),
    ('DoseRateNoTime',
     u'Dose rate: the reference spectrum has zero measurement time.',
     u'Мощность дозы: у эталонного спектра нулевое время набора.'),
    ('DoseRateNotFinite',
     u'Dose rate: the sum over the ranges is not a finite number — the calibration points are unusable.',
     u'Мощность дозы: сумма по диапазонам не конечное число — точки калибровки негодны.'),
    ('DoseRateOutsideCurve',
     u'Dose rate: {0:f1} keV is outside the efficiency curve ({1:f1}...{2:f1} keV).',
     u'Мощность дозы: {0:f1} кэВ вне кривой эффективности ({1:f1}...{2:f1} кэВ).'),
    ('DoseRateOutsideIcrp',
     u'Dose rate: {0} keV is outside the ICRP 74 h*(10)/Ka table ({1}...{2} keV).',
     u'Мощность дозы: {0} кэВ вне таблицы h*(10)/Ka ICRP 74 ({1}...{2} кэВ).'),
    ('DoseRateOutsideXcom',
     u'Dose rate: {0} keV is outside the XCOM table for Z={1} ({2}...{3} keV).',
     u'Мощность дозы: {0} кэВ вне таблицы XCOM для Z={1} ({2}...{3} кэВ).'),
    ('DoseRatePartialCoverage',
     u'(covers {0:f0} % of counts)',
     u'(покрыто {0:f0} % отсчётов)'),
]

CALL = re.compile(r'(?:\bDoseRateCoefficients\.Text|(?<![\w.])Text)\s*\(')


def unescape(text):
    u"""Литерал C# -> строка, которую увидит человек."""
    out, i = [], 0
    while i < len(text):
        ch = text[i]
        if ch == '\\' and i + 1 < len(text):
            nxt = text[i + 1]
            out.append({'n': '\n', 't': '\t', 'r': '\r', '"': '"', "'": "'", '\\': '\\'}.get(nxt, nxt))
            i += 2
            continue
        out.append(ch)
        i += 1
    return ''.join(out)


def fallbacks():
    u"""{ключ: запасная надпись} прямо из дерева."""
    out = {}
    for path in C.sources(APP, designer=False):
        text = C.read(path).replace('\r\n', '\n')
        for m in CALL.finditer(text):
            args = C.balanced_args(text, m.end() - 1)
            if args is None:
                continue
            parts = C.split_args(args)
            if len(parts) < 2:
                continue
            key = re.match(r'\s*"([^"]*)"\s*\Z', parts[0], re.S)
            if not key:
                continue
            pieces = re.findall(r'"((?:[^"\\]|\\.)*)"', parts[1])
            out.setdefault(key.group(1), set()).add(unescape(''.join(pieces)))
    return out


def esc(value):
    return value.replace(u'&', u'&amp;').replace(u'<', u'&lt;').replace(u'>', u'&gt;')


def insert_resx(path, pairs, which):
    raw = io.open(path, 'rb').read()
    assert raw[:3] == b'\xef\xbb\xbf', path
    text = raw.decode('utf-8-sig')
    assert text.count(u'</root>') == 1, path
    block = u''
    for key, en, ru in pairs:
        value = en if which == 'en' else ru
        block += (u'  <data name="%s" xml:space="preserve">\r\n'
                  u'    <value>%s</value>\r\n'
                  u'  </data>\r\n' % (key, esc(value)))
    text = text.replace(u'</root>', block + u'</root>')
    io.open(path, 'wb').write(b'\xef\xbb\xbf' + text.encode('utf-8'))


def insert_designer(path, pairs):
    raw = io.open(path, 'rb').read()
    assert raw[:3] == b'\xef\xbb\xbf', path
    text = raw.decode('utf-8-sig')
    tail = u'    }\r\n}\r\n'
    assert text.endswith(tail), repr(text[-40:])
    block = u''
    for key, en, ru in pairs:
        block += (u'        /// <summary>\r\n'
                  u'        ///   Looks up a localized string similar to %s.\r\n'
                  u'        /// </summary>\r\n'
                  u'        public static string %s {\r\n'
                  u'            get {\r\n'
                  u'                return ResourceManager.GetString("%s", resourceCulture);\r\n'
                  u'            }\r\n'
                  u'        }\r\n\r\n' % (esc(en), key, key))
    text = text[:-len(tail)] + block + tail
    io.open(path, 'wb').write(b'\xef\xbb\xbf' + text.encode('utf-8'))


def main():
    found = fallbacks()
    problems = []
    for key, en, ru in PAIRS:
        if key not in found:
            problems.append(u'ключа нет в дереве: %s' % key)
            continue
        if len(found[key]) != 1:
            problems.append(u'у ключа %s несколько разных запасных надписей' % key)
            continue
        actual = list(found[key])[0]
        if actual != en:
            problems.append(u'РАЗОШЛОСЬ %s\n  код:  %r\n  тут:  %r' % (key, actual, en))
    extra = sorted(set(found) - set(k for k, _e, _r in PAIRS))
    if extra:
        problems.append(u'ключи дерева, которых нет здесь: %s' % u', '.join(extra))
    if problems:
        for line in problems:
            print(line)
        print(u'ОТКАЗ: файлы не тронуты')
        return 1
    print(u'английские надписи сошлись с кодом: %d из %d' % (len(PAIRS), len(found)))

    if '--check' in sys.argv:
        return 0
    insert_resx(os.path.join(APP, 'Properties', 'Resources.resx'), PAIRS, 'en')
    insert_resx(os.path.join(APP, 'Properties', 'Resources.ru.resx'), PAIRS, 'ru')
    insert_designer(os.path.join(APP, 'Properties', 'Resources.Designer.cs'), PAIRS)
    print(u'заведено пар: %d' % len(PAIRS))
    return 0


if __name__ == '__main__':
    sys.exit(main())
