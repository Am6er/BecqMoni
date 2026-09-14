# -*- coding: utf-8 -*-
"""П64 (AMBER29): 33 спектра угля (3 ранних + 30 равновесных) + 2 фона ЛСРМ (35 файлов) `.spe` → XML BecqMoni для проб FSA.

Разбор `.spe` — `tools/CORPUS/scripts/spe_import.py` (`read_spe`, `to_xml`, поверка
суммы `CPS·TLIVE` на каждом файле). Что дописывается сверх конвертера:

  * прибор — корпусная конфигурация эпохи G1S24 (`corpus/devices/Gamma-1S UDS-GC 63x63
    1024 (corpus, поверка 2024).xml`, guid 9e5a1c00-…-11c0de00000c): прибор
    `УДС-ГЦ-63х63-USB №0086-16`, ноябрь 2025;
  * фон — ВСТРОЕН узлом `<BackgroundEnergySpectrum>` (разбор берёт фон из файла, а не
    по имени; корпусные XML устроены так же), имя — `<BackgroundSpectrumFile>`;
    основной фон — вода в той же маринелли 19.11.2025 (422 719 с), второй (05.04.2026,
    414 717 с) — контрольные копии `*_bg2.xml` для трёх съёмок;
  * ПШПВ — `SqrtFwhmCalibration` В КАНАЛАХ по ИЗМЕРЕННЫМ на этих же спектрах пикам
    609 / 1120 / 1764 кэВ (`fwhm_measure.py`, `fwhm.csv`: 50.0 / 68.2 / 95.3 кэВ;
    модель корпуса G1S24 уже на 7–12 %, ширина образа в FSA не подгоняется).
    Модель FWHM² = c1·E + c2·E² (кэВ) подогнана к трём точкам и переведена в каналы
    по калибровке проб.

    python handover/p64-amber29/mk_spectra.py <каталог .spe> <каталог XML>

Ключи файлов: `coal_t20m`, `coal_t2h`, `coal_t3h`, `coal_e01`…`coal_e30`,
`bg_2025_11_19`, `bg_2026_04_05`. Пишет и `timeline.csv` (ключ, старт ISO, живое, полное,
отсчёты) — вход скриптов физики.
"""
import glob
import io
import math
import os
import re
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
sys.path.insert(0, os.path.join(REPO, 'tools', 'CORPUS', 'scripts'))
from spe_import import read_spe, to_xml, start_time  # noqa: E402

DEVICE = ('Gamma-1S UDS-GC 63x63 1024 (corpus, поверка 2024)', '9e5a1c00-000c-4a00-9000-11c0de00000c')

# измерено fwhm_measure.py на сумме 30 равновесных (кэВ): (E, FWHM)
FWHM_POINTS = [(609.3, 49.68), (1120.3, 68.18), (1764.5, 95.28)]


def key_of(name):
    m = re.search(r'дпр_(\d\d)', name)
    if m:
        return 'coal_e' + m.group(1)
    if '20 мин' in name:
        return 'coal_t20m'
    if '2 часа' in name:
        return 'coal_t2h'
    if '3 часа' in name:
        return 'coal_t3h'
    if '19-11-2025' in name:
        return 'bg_2025_11_19'
    if '06-04-2026' in name:
        return 'bg_2026_04_05'
    raise SystemExit('неизвестный файл ' + name)


def ecal_of(head):
    v = [float(x) for x in head['ENERGY'].split(',')]
    return v[1:int(v[0]) + 2]


def fwhm_channels(ecal):
    """Коэффициенты SqrtFwhmCalibration (каналы) из модели по кэВ."""
    A = np.array([[e, e * e] for e, _ in FWHM_POINTS])
    b = np.array([f * f for _, f in FWHM_POINTS])
    c1, c2 = np.linalg.lstsq(A, b, rcond=None)[0]
    chs = np.arange(20.0, 1000.0, 5.0)
    es = np.array([sum(c * ch ** k for k, c in enumerate(ecal)) for ch in chs])
    d = np.array([sum(k * c * ch ** (k - 1) for k, c in enumerate(ecal) if k > 0) for ch in chs])
    fw_ch2 = (c1 * es + c2 * es * es) / (d * d)
    keep = es > 30.0
    M = np.vstack([np.ones(keep.sum()), chs[keep], chs[keep] ** 2]).T
    sol = np.linalg.lstsq(M, fw_ch2[keep], rcond=None)[0]
    return (c1, c2), sol


def bg_block(head, counts):
    """Узел BackgroundEnergySpectrum из фона — теми же строками, что EnergySpectrum."""
    xml = to_xml(head, counts, '', {'device': DEVICE})
    m = re.search(r'      <EnergySpectrum>\n(.*?)      </EnergySpectrum>\n', xml, re.S)
    return '    <BackgroundEnergySpectrum>\n' + m.group(1) + '      </BackgroundEnergySpectrum>\n'


def main():
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    files = {}
    for p in sorted(glob.glob(os.path.join(src, '*.spe'))):
        files[key_of(os.path.basename(p))] = p
    assert len(files) == 35, len(files)   # 33 съёмки угля + 2 фона (в строке AMBER29 «35 спектров + 2 фона» — пересчёт)
    bgs = {}
    for k in ('bg_2025_11_19', 'bg_2026_04_05'):
        h, c = read_spe(files[k])
        bgs[k] = (h, c, bg_block(h, c))
        xml = to_xml(h, c, 'П64 AMBER29: фон, вода в маринелли 1 л', {'device': DEVICE})
        with io.open(os.path.join(out, k + '.xml'), 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(xml)
    lines = ['key,start,live_s,real_s,counts,cps,src']
    fwhm_kev = fwhm_ch = None
    for k in sorted(files):
        if k.startswith('bg_'):
            h, c = bgs[k][0], bgs[k][1]
            lines.append('%s,%s,%s,%s,%d,%s,%s' % (k, start_time(h), h['TLIVE'], h['TREAL'], sum(c), h['CPS'], os.path.basename(files[k])))
            continue
        h, c = read_spe(files[k])
        ecal = ecal_of(h)
        if fwhm_ch is None:
            fwhm_kev, fwhm_ch = fwhm_channels(ecal)
        for bgk, suffix in (('bg_2025_11_19', ''), ('bg_2026_04_05', '_bg2')):
            if suffix and k not in ('coal_t20m', 'coal_e01', 'coal_e30'):
                continue
            xml = to_xml(h, c, 'П64 AMBER29: уголь 461 г в маринелли 1 л, фон ' + bgk,
                         {'device': DEVICE, 'fwhm': [float(x) for x in fwhm_ch]})
            # фон — перед узлом ПШПВ, как в корпусных файлах
            xml = xml.replace('      </EnergySpectrum>\n',
                              '      </EnergySpectrum>\n' + bgs[bgk][2]
                              + '    <BackgroundSpectrumFile>%s.xml</BackgroundSpectrumFile>\n' % bgk, 1)
            with io.open(os.path.join(out, k + suffix + '.xml'), 'w', encoding='utf-8', newline='\n') as fh:
                fh.write(xml)
        lines.append('%s,%s,%s,%s,%d,%s,%s' % (k, start_time(h), h['TLIVE'], h['TREAL'], sum(c), h['CPS'], os.path.basename(files[k])))
    with io.open(os.path.join(out, 'timeline.csv'), 'w', encoding='utf-8', newline='') as fh:
        fh.write('\n'.join(lines) + '\n')
    print('ПШПВ по кэВ: FWHM² = %.5g·E + %.5g·E²; в каналах c0=%.6g c1=%.6g c2=%.6g'
          % (fwhm_kev[0], fwhm_kev[1], fwhm_ch[0], fwhm_ch[1], fwhm_ch[2]))
    for e in (242.0, 351.9, 609.3, 1120.3, 1764.5, 2614.5):
        print('  %7.1f кэВ: %.2f кэВ (%.2f %%)' % (e, math.sqrt(fwhm_kev[0] * e + fwhm_kev[1] * e * e),
                                              100 * math.sqrt(fwhm_kev[0] * e + fwhm_kev[1] * e * e) / e))
    print('записано XML: %d в %s' % (len(glob.glob(os.path.join(out, '*.xml'))), out))


if __name__ == '__main__':
    main()
