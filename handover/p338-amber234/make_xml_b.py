"""Вариант Б: те же спектры (с уже вписанным узлом Efficiency), но у пробы И фона — ОДНА шкала прибора
(energy_scale_file_coeffs, у всех их файлов одинаковая), ПШПВ в каналах пересчитана на неё."""
import sys, re, os
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
G = 'D:/BqMoni_Claude/p338/g4/'
K, P = 0.7880103601331053, 0.6335125944403551   # их закон ПШПВ(E) = k·E^p, кэВ


class mx:   # функции make_xml.py без его верхнего кода (импорт перезаписал бы спектры варианта А)
    @staticmethod
    def INV(x):
        return repr(float(x))

    @staticmethod
    def E(cf, ch):
        return sum(c * np.asarray(ch, float) ** i for i, c in enumerate(cf))

    @staticmethod
    def fwhm_ch(cf):
        ch = np.arange(300, 6000, 10.0); e = mx.E(cf, ch); sl = np.gradient(e, ch)
        fw = K * e ** P / sl; p, lna = np.polyfit(np.log(ch), np.log(fw), 1)
        return float(np.exp(lna)), float(p), float(np.max(np.abs(np.exp(lna) * ch ** p / fw - 1)))


SRC = 'D:/BqMoni_Claude/p338/show/spectra/'; DST = 'D:/BqMoni_Claude/p338/show/spectra_b/'
os.makedirs(DST, exist_ok=True)
FILE = None
for line in open(G + 'th232_reference_marinelli_1l.csv', encoding='utf-8'):
    if line.startswith('# energy_scale_file_coeffs:'):
        FILE = [float(x) for x in line.split(':', 1)[1].split()]
assert FILE and len(FILE) == 5


def coeff_block(cf, ind):
    return ''.join(f'\n{ind}<Coefficient>{mx.INV(c)}</Coefficient>' for c in cf) + '\n' + ind[:-2]


for key in ('GS2020_Th232_oisn10', 'GS2020_berry_482g'):
    t = open(SRC + key + '.xml', encoding='utf-8-sig').read()
    assert '<Efficiency>' in t, key + ': узла Efficiency нет — сперва CorpusEffProbe'
    n = 0

    def rep(m):
        global n
        n += 1
        return m.group(1) + coeff_block(FILE, '            ') + m.group(3)
    t = re.sub(r'(<EnergyCalibration>\s*<PolynomialOrder>4</PolynomialOrder>\s*<Coefficients>)(.*?)(</Coefficients>)', rep, t, flags=re.S)
    assert n == 2, (key, n)
    a, p, d = mx.fwhm_ch(FILE)
    i = t.index('<PowerFwhmCalibration>'); j = t.index('</PowerFwhmCalibration>', i)
    blk = t[i:j]
    blk = re.sub(r'(</CalibrationPeaks>\s*<Coefficients>).*?(</Coefficients>)',
                 lambda m: m.group(1) + f'\n        <Coefficient>{mx.INV(a)}</Coefficient>\n        <Coefficient>{mx.INV(p)}</Coefficient>\n      ' + m.group(2),
                 blk, count=1, flags=re.S)
    t = t[:i] + blk + t[j:]
    t = t.replace('шкалы — page_scale их страницы', 'шкала — одна шкала прибора у пробы и фона (energy_scale_file_coeffs)')
    open(DST + key + '.xml', 'wb').write(('\ufeff' + t).encode('utf-8'))
    print(f'{key}: шкала прибора у пробы и фона; ПШПВ a={a:.4f} p={p:.4f}')
