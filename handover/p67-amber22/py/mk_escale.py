# -*- coding: utf-8 -*-
"""П67 (AMBER22): шкала СПЕКТРА по его пикам (абляция данных `escale`, как П42 §4.2) — в по-сценные копии.

Зачем: у `face81` (файл 2025-09-08) шкала файла врёт на −3…−9 % (П56: «−9…−18 %»), линейная привязка FSA (усиление +
сдвиг) её не выправляет — фит рушится (A(Th) 279 Бк, сплайн 80 %, 1 опора); у `edge93`/`contact_cal0809` полином 4-й
степени без опор 662…2614 ставит 911 на 892 кэВ (П42). Фон, встроенный в файл, несёт скопированную шкалу пробы при
своём усилении (у фона 09.09.2026 она врёт на −78/−105 кэВ на 1461/1764) — фону ставится своя шкала по ЕГО пикам
352/609/1120/1461/1764/2614 (`bgscale.py`, полином 3-й степени, остатки ≤ 5 кэВ; линейная по двум опорам ошибалась
на ±20 кэВ, «полином пробы на пересчитанном канале» — до +44); иначе остаётся файловая.

Правится ТОЛЬКО блок <EnergyCalibration> (коэффициенты и порядок) у пробы (первый <EnergySpectrum>) и у фона
(<BackgroundEnergySpectrum>); всё остальное байт в байт. Читается ПРИВЯЗАННАЯ копия <спектр>__<сцена>.xml (как есть,
с узлом <Efficiency> сцены), пишется рядом <спектр>__<сцена>.escale.xml; копия без узла сцены пропускается.

    python mk_escale.py <каталог копий spectra_scenes>
"""
import glob
import io
import os
import re
import sys

import numpy as np

sys.path.insert(0, r'D:\BqMoni_Claude\p67\py')
from spec import Spec       # noqa: E402
import recal                # noqa: E402
import lines67              # noqa: E402
import bgscale              # noqa: E402


def bg_composed(B, coef):
    """шкала фона = полином ПРОБЫ, взятый на линейно пересчитанном канале: E(ch) = P(a + b·ch), a, b — по двум
    опорам фона (1461, 2614): их центроиды в каналах фона переводятся в каналы пробы обратной функцией P.
    Форма нелинейности (низ шкалы) — от пробы, усиление — фона; линейная шкала по двум опорам (первая попытка)
    внизу ошибалась на ~10 кэВ и портила вычитание."""
    z, g = lines67.bg_two_anchor(B)          # линейная шкала фона по своим опорам: E = z + g·ch
    # каналы фона → энергия (z + g·ch) → канал пробы (обратный полином) на двух опорах → линейная карта каналов
    chs = np.arange(B.n, dtype=float)
    Ps = recal.scale_of(coef, chs)
    pts = []
    for E0 in (1460.8, 2614.511):
        ch_bg = (E0 - z) / g
        ch_s = float(np.interp(E0, Ps, chs))
        pts.append((ch_bg, ch_s))
    (x1, y1), (x2, y2) = pts
    b = (y2 - y1) / (x2 - x1)
    a = y1 - b * x1
    # P(a + b·ch) как полином по ch — точная подгонка той же степени
    ch_fit = np.linspace(0, B.n - 1, 4000)
    E_fit = recal.scale_of(coef, a + b * ch_fit)
    return list(np.polyfit(ch_fit, E_fit, len(coef) - 1)[::-1])


def cal_block(coef):
    lines = ['<EnergyCalibration>', '            <PolynomialOrder>%d</PolynomialOrder>' % (len(coef) - 1), '            <Coefficients>']
    for c in coef:
        lines.append('              <Coefficient>%s</Coefficient>' % repr(float(c)))
    lines.append('            </Coefficients>')
    lines.append('          </EnergyCalibration>')
    return '\n'.join(lines)


def replace_cal(text, start, coef):
    m = re.compile(r'<EnergyCalibration[^>]*>.*?</EnergyCalibration>', re.S).search(text, start)
    return text[:m.start()] + cal_block(coef) + text[m.end():], m.start()


def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    d = sys.argv[1]
    cache = {}
    n = 0
    for path in sorted(glob.glob(os.path.join(d, '*.xml'))):
        if path.endswith('.asis.xml') or path.endswith('.escale.xml'):
            continue
        base = os.path.basename(path).split('__')[0]
        if base not in cache:
            S = Spec(path)
            file_coef = list(S.coef)
            coef, ab, pts, fws = recal.recal(S, verbose=False)
            chs = np.array([p[0] for p in pts]); E = np.array([p[1] for p in pts])
            res = E - recal.scale_of(coef, chs)
            ok = len(pts) >= 4 and np.max(np.abs(res)) < 6.0
            if not ok:
                print('%s: перекалибровка НЕ принята (групп %d, остатки %s) — шкала пробы остаётся файловой' % (base, len(pts), ' '.join('%+.1f' % r for r in res)))
                coef = file_coef
            bgc = None
            if S.bg is not None:
                bcoef, bres = bgscale.bg_scale(S.bg, deg=3)
                if np.max(np.abs(bres)) < 3.0 and abs(bcoef[0]) < 40.0:
                    bgc = list(bcoef)
                else:
                    # кубическая по пикам фона не принята (центроид 352 у фона 2026 садится на плечо — полином
                    # с нулём 60 кэВ): линейная по двум опорам 1461/2614 (остатки на 352/609 ±20 кэВ, у файловой
                    # −33/−13 и −78/−105 на 1461/1764)
                    z, g = lines67.bg_two_anchor(S.bg)
                    bgc = [z, g]
                    print('%s: кубическая шкала ФОНА не принята (остатки %s, ноль %.1f) — линейная по 1461/2614: %.3f + %.6f·ch' % (base, ' '.join('%+.1f' % r for r in bres), bcoef[0], z, g))
            cache[base] = (list(coef), bgc)
            print('%s: проба %s (остатки %s); фон %s' % (base, ' '.join('%.6g' % c for c in coef), ' '.join('%+.1f' % r for r in res), (' '.join('%.6g' % c for c in bgc)) if bgc else 'файловая/нет'))
        coef, bgc = cache[base]
        t = io.open(path, encoding='utf-8', newline='').read()
        scene = os.path.basename(path)[:-4].split('__', 1)[1]
        if ('<Name>' + scene + '</Name>') not in t:
            continue          # сцена ещё не привязана — копия без узла <Efficiency> сцены
        i_es = t.index('<EnergySpectrum>')
        t, _ = replace_cal(t, i_es, coef)
        if bgc is not None and '<BackgroundEnergySpectrum>' in t:
            i_bg = t.index('<BackgroundEnergySpectrum>')
            t, _ = replace_cal(t, i_bg, bgc)
        io.open(path[:-4] + '.escale.xml', 'w', encoding='utf-8', newline='').write(t)
        n += 1
    print('готово: %d копий .escale.xml' % n)


if __name__ == '__main__':
    main()
