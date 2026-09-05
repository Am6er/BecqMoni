# -*- coding: utf-8 -*-
u"""Три пары слов отказа «спектр без модели разрешения» (`A234`, `A235`).

Пишется БАЙТАМИ: BOM + CRLF, как и `add_keys.py` рядом.
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
APP = os.path.join(ROOT, 'BecquerelMonitor')

PAIRS = [
    ('ERRNoFwhmCalibrationImport',
     u'{0}: {1} of {2} imported spectra have no resolution model (FWHM calibration). '
     u'Peak search cannot run on them. Reason: {3}',
     u'{0}: у {1} из {2} ввезённых спектров нет модели разрешения (калибровки ПШПВ). '
     u'Поиск пиков по ним работать не может. Причина: {3}'),
    ('ERRFwhmDefaultNotMonotonic',
     u'the default cannot be built from the device settings — the straight line through '
     u'(channel 0, FWHM {0}) and (channel {1}, FWHM {2}) does not grow, while the width '
     u'must not decrease along the scale.',
     u'умолчание по настройкам прибора не строится — прямая через (канал 0, ПШПВ {0}) и '
     u'(канал {1}, ПШПВ {2}) не растёт, а ширина вдоль шкалы убывать не может.'),
    ('ERRFwhmCalibrationUnset',
     u'the spectrum carries no FWHM calibration and none was built for it.',
     u'у спектра нет калибровки ПШПВ, и никто её ему не построил.'),
]


def esc(value):
    return value.replace(u'&', u'&amp;').replace(u'<', u'&lt;').replace(u'>', u'&gt;')


def insert_resx(path, which):
    raw = io.open(path, 'rb').read()
    assert raw[:3] == b'\xef\xbb\xbf', path
    text = raw.decode('utf-8-sig')
    assert text.count(u'</root>') == 1, path
    block = u''
    for key, en, ru in PAIRS:
        assert u'<data name="%s"' % key not in text, u'ключ уже есть: %s' % key
        block += (u'  <data name="%s" xml:space="preserve">\r\n'
                  u'    <value>%s</value>\r\n'
                  u'  </data>\r\n' % (key, esc(en if which == 'en' else ru)))
    text = text.replace(u'</root>', block + u'</root>')
    io.open(path, 'wb').write(b'\xef\xbb\xbf' + text.encode('utf-8'))


def insert_designer(path):
    raw = io.open(path, 'rb').read()
    text = raw.decode('utf-8-sig')
    tail = u'    }\r\n}\r\n'
    assert text.endswith(tail)
    block = u''
    for key, en, ru in PAIRS:
        block += (u'        /// <summary>\r\n'
                  u'        ///   Looks up a localized string similar to %s.\r\n'
                  u'        /// </summary>\r\n'
                  u'        public static string %s {\r\n'
                  u'            get {\r\n'
                  u'                return ResourceManager.GetString("%s", resourceCulture);\r\n'
                  u'            }\r\n'
                  u'        }\r\n\r\n' % (esc(en), key, key))
    io.open(path, 'wb').write(b'\xef\xbb\xbf' + (text[:-len(tail)] + block + tail).encode('utf-8'))


insert_resx(os.path.join(APP, 'Properties', 'Resources.resx'), 'en')
insert_resx(os.path.join(APP, 'Properties', 'Resources.ru.resx'), 'ru')
insert_designer(os.path.join(APP, 'Properties', 'Resources.Designer.cs'))
print(u'заведено пар: %d' % len(PAIRS))
