# -*- coding: utf-8 -*-
"""П88: что лежит в четырёх файлах — шкалы пробы и встроенного фона, времена, счётчики, sha256."""
import re, sys, io, hashlib
sys.stdout.reconfigure(encoding='utf-8')
BASE = r'C:\Users\moroz\YandexDisk\Спектры\!AS80x80'
FILES = [
    ('bg',        BASE + r'\Фон дом 09.09.2026.xml'),
    ('contact0809', BASE + r'\калибровка 08.09.2026\Th-232.xml'),
    ('edge1409',  BASE + r'\Th-232(медальон ребром) - дистанция 81мм.xml'),
    ('edge1709',  BASE + r'\Th-232(медальон ребром) - дистанция 81мм с другого бока.xml'),
]
def block(t, tag):
    m = re.search(r'<%s>(.*?)</%s>' % (tag, tag), t, re.S)
    return m.group(1) if m else None
def cal(es):
    c = block(es, 'EnergyCalibration')
    return [x for x in re.findall(r'<Coefficient>([^<]+)</Coefficient>', c)]
def num(es, tag):
    m = re.search(r'<%s>([^<]*)</%s>' % (tag, tag), es)
    return m.group(1) if m else None
for key, path in FILES:
    b = open(path, 'rb').read()
    sha = hashlib.sha256(b).hexdigest()
    t = b.decode('utf-8-sig')
    n_rd = t.count('<ResultData>')
    es = block(t, 'EnergySpectrum')
    bg = block(t, 'BackgroundEnergySpectrum')
    print('== %s  %d байт  sha256 %s  ResultData×%d' % (key, len(b), sha, n_rd))
    print('   проба: N=%s  T=%s  live=%s  total=%s' % (num(es,'NumberOfChannels'), num(es,'MeasurementTime'), num(es,'LiveTime'), num(es,'TotalPulseCount')))
    print('          шкала: %s' % ' '.join(cal(es)))
    if bg:
        print('   фон  : N=%s  T=%s  live=%s  total=%s' % (num(bg,'NumberOfChannels'), num(bg,'MeasurementTime'), num(bg,'LiveTime'), num(bg,'TotalPulseCount')))
        print('          шкала: %s' % ' '.join(cal(bg)))
        print('          BackgroundSpectrumFile: %s' % num(t, 'BackgroundSpectrumFile'))
    fw = re.search(r'<(\w+FwhmCalibration)>', t)
    print('   ПШПВ: %s' % (fw.group(1) if fw else 'НЕТ'))
