# -*- coding: utf-8 -*-
r"""П88 (AMBER22): плечо `eq` — шкала ПРОБЫ по её пикам (recal.py П56/П67: группы ряда Th-232 в каналах, кубика),
шкала ФОНА — ОСТАЁТСЯ ФАЙЛОВОЙ (у файлов полосы она уже перекалибрована пробой RecalibrateBackgroundProbe; у копий
*_bg0 — прежняя скопированная, нарочно: так плечо `eq` меряет ТОЛЬКО перекалибровку фона при выправленной шкале пробы).
Отличие от mk_escale.py П67 — фон не трогается (там ему ставилась кубика/линейная по его пикам, и она разбор портила, П67 §5).

Правится ТОЛЬКО блок <EnergyCalibration> первого <EnergySpectrum>; всё остальное байт в байт. Читается привязанная копия
<спектр>__<сцена>.xml (с узлом <Efficiency>), пишется рядом <спектр>__<сцена>.escale.xml; копия без узла сцены пропускается.

    python mk_escale_p88.py <каталог копий spectra_scenes>
"""
import glob
import io
import os
import re
import sys

import numpy as np

sys.path.insert(0, r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p67-amber22\py')
from spec import Spec       # noqa: E402
import recal                # noqa: E402
import recal_p88            # noqa: E402


def cal_block(coef):
    lines = ['<EnergyCalibration>', '            <PolynomialOrder>%d</PolynomialOrder>' % (len(coef) - 1), '            <Coefficients>']
    for c in coef:
        lines.append('              <Coefficient>%s</Coefficient>' % repr(float(c)))
    lines.append('            </Coefficients>')
    lines.append('          </EnergyCalibration>')
    return '\n'.join(lines)


def replace_cal(text, start, coef):
    m = re.compile(r'<EnergyCalibration[^>]*>.*?</EnergyCalibration>', re.S).search(text, start)
    return text[:m.start()] + cal_block(coef) + text[m.end():]


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    d = sys.argv[1]
    cache = {}
    n = 0
    for path in sorted(glob.glob(os.path.join(d, '*.xml'))):
        if path.endswith('.escale.xml'):
            continue
        base = os.path.basename(path).split('__')[0]
        if base not in cache:
            S = Spec(path)
            file_coef = list(S.coef)
            coef, pts, res, dropped = recal_p88.recal_probe(S)
            ok = len(pts) >= 4 and np.max(np.abs(res)) < 6.0
            if not ok:
                print('%s: перекалибровка пробы НЕ принята (групп %d, остатки %s) — шкала пробы остаётся файловой' % (base, len(pts), ' '.join('%+.1f' % r for r in res)))
                coef = file_coef
            cache[base] = list(coef)
            print('%s: проба %s (группы %s; остатки %s%s); фон — файловая' % (base, ' '.join('%.6g' % c for c in coef), ' '.join(p[0] for p in pts), ' '.join('%+.1f' % r for r in res),
                  ('; выброшены ' + ' '.join('%s %+.1f' % d for d in dropped)) if dropped else ''))
        coef = cache[base]
        t = io.open(path, encoding='utf-8', newline='').read()
        scene = os.path.basename(path)[:-4].split('__', 1)[1]
        if ('<Name>' + scene + '</Name>') not in t:
            continue
        i_es = t.index('<EnergySpectrum>')
        t = replace_cal(t, i_es, coef)
        io.open(path[:-4] + '.escale.xml', 'w', encoding='utf-8', newline='').write(t)
        n += 1
    print('готово: %d копий .escale.xml' % n)


if __name__ == '__main__':
    main()
