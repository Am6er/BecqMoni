"""Пик Cs-137 662 кэВ во ВСЕХ спектрах корпуса с 137CS: ступенька и FWHM выделения прямой/ступенькой.

Запуск: python -I fwhm_step_corpus.py <каталог corpus>
Выделение эмулируется окном x0 ± k·FWHM (k = 1.5 и 2.5) вокруг найденной вершины;
FWHM — по сырому спектру (как панель выделения вне режимов вычитания и FSA).
"""
import csv
import math
import os
import re
import statistics
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fwhm_step_sim import fwhm_with_base, line_base, step_base  # noqa: E402

E0 = 661.657


def load(path, index):
    root = ET.parse(path).getroot()
    rds = root.findall('./ResultDataList/ResultData')
    rd = rds[index]
    es = rd.find('EnergySpectrum')
    coeffs = [float(c.text) for c in es.find('EnergyCalibration/Coefficients')]
    counts = [float(d.text) for d in es.find('Spectrum')]
    return coeffs, counts


def energy(coeffs, ch):
    return sum(c * ch ** i for i, c in enumerate(coeffs))


def channel_of(coeffs, e, n):
    lo, hi = 0.0, float(n - 1)
    for _ in range(80):
        mid = 0.5 * (lo + hi)
        if energy(coeffs, mid) < e:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def main(corpus):
    rows = list(csv.DictReader(open(os.path.join(corpus, 'manifest.csv'), encoding='utf-8-sig')))
    parts = {r['spectrum']: r['part'] for r in csv.DictReader(open(os.path.join(corpus, 'parts.csv'), encoding='utf-8-sig'))}
    sel = [r for r in rows if re.search(r'137CS', r['nuclides'], re.I)]
    print('спектров с 137CS в manifest: {}'.format(len(sel)))
    print('{:<26} {:<8} {:<8} {:>6} {:>8} {:>6} | {:>7} {:>7} | {:>7} {:>7}'.format(
        'спектр', 'группа', 'часть', 'FWHM%', 'A', 'H/A', 'k1.5 ли', 'k1.5 ст', 'k2.5 ли', 'k2.5 ст'))
    by_part = {}
    for r in sel:
        key = r['key']
        part = parts.get(key, '?')
        path = os.path.join(corpus, 'spectra', key + '.xml')
        try:
            coeffs, y = load(path, int(r['result_data'] or 0))
        except Exception as e:  # noqa: BLE001
            print('{:<26} не прочитан: {}'.format(key, e))
            continue
        n = len(y)
        pct = float(r['fwhm_662_pct'])
        c0 = channel_of(coeffs, E0, n)
        dedc = energy(coeffs, c0 + 0.5) - energy(coeffs, c0 - 0.5)
        fw = pct / 100.0 * E0 / dedc                       # FWHM в каналах
        # вершина: максимум сглаженного (3 канала) в ±0.5 FWHM
        lo, hi = int(c0 - 0.5 * fw), int(c0 + 0.5 * fw) + 1
        sm = lambda i: (y[i - 1] + y[i] + y[i + 1]) / 3.0
        top = max(range(max(lo, 1), min(hi, n - 2)), key=sm)
        ne = max(3, int(round(0.25 * fw)))
        res = {}
        for k in (1.5, 2.5):
            a = int(round(top - k * fw))
            b = int(round(top + k * fw))
            if a - ne < 0 or b + ne >= n:
                res[k] = None
                continue
            r1 = fwhm_with_base(y, a, b, line_base(y, a, b, 1))
            rs = fwhm_with_base(y, a, b, step_base(y, a, b, ne))
            res[k] = (r1, rs, a, b)
        # ступенька: уровни в [1.5, 2.0] FWHM слева/справа
        L = statistics.fmean(y[int(top - 2.0 * fw):int(top - 1.5 * fw) + 1])
        R = statistics.fmean(y[int(top + 1.5 * fw):int(top + 2.0 * fw) + 1])
        A = sm(top) - 0.5 * (L + R)
        ha = (L - R) / A if A > 0 else float('nan')

        def cell(k, which):
            v = res.get(k)
            if not v or not v[0] or not v[1]:
                return '  отказ'
            r1, rs = v[0], v[1]
            ref = rs[0]
            if which == 'line':
                return '{:7.2f}'.format(100.0 * r1[0] / fw - 100.0)
            return '{:7.2f}'.format(100.0 * rs[0] / fw - 100.0)

        print('{:<26} {:<8} {:<8} {:>6.2f} {:>8.0f} {:>6.3f} | {} {} | {} {}'.format(
            key, r['det'], part, pct, A, ha, cell(1.5, 'line'), cell(1.5, 'step'), cell(2.5, 'line'), cell(2.5, 'step')))
        for k in (1.5, 2.5):
            v = res.get(k)
            if v and v[0] and v[1]:
                by_part.setdefault((part, k), []).append((v[0][0] - v[1][0]) / v[1][0])
        by_part.setdefault((part, 'ha'), []).append(ha)
    print()
    print('столбцы «ли/ст» — FWHM выделения прямой (по одному каналу на краю, как в приложении) и ступенькой,')
    print('в % от FWHM модели разрешения группы (detectors.csv → manifest.fwhm_662_pct).')
    print()
    print('сводка по частям корпуса (числа частей не складываются):')
    for part in sorted({p for p, _ in by_part}):
        ha = by_part.get((part, 'ha'), [])
        print('  часть {}: спектров {}, H/A медиана {:.3f}, наибольшая {:.3f}'.format(
            part, len(ha), statistics.median(ha), max(ha)))
        for k in (1.5, 2.5):
            d = by_part.get((part, k), [])
            if d:
                print('    окно ±{}·FWHM: (прямая − ступенька)/ступенька: медиана {:+.2f} %, размах {:+.2f} … {:+.2f} %'.format(
                    k, 100 * statistics.median(d), 100 * min(d), 100 * max(d)))


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main(sys.argv[1])
