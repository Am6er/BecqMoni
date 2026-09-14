# -*- coding: utf-8 -*-
"""П64 (AMBER29): два довеска к основным плечам.

  1. `coalA_sum30.xml` / `coalA_sum10.xml` — СУММА равновесных съёмок e01…e30 (432 000 с) и
     e21…e30 (144 000 с, радон ниже всего): пределы Ra-226 (186) и Pb-210 (46.5) с 30×
     статистикой (п. 4) и состав «подставки» (§3 журнала). Калибровка у всех 30 одна (проверено),
     живое/полное время — суммы, старт — первой съёмки; фон и узел <Efficiency> — из `coalA_e01.xml`.
  2. `coalX_e01.xml` — e01 с кривой ЗАВЕДОМО НЕ ТОЙ формы: живая корпусная
     `G1S_mar1l_oisn16_155_p24` (набивка ОИСН-16 ρ 1.55 вместо угля 0.461; узел <Efficiency> —
     из `corpus/spectra/G1S24_K40_Mar_2.xml`, матрица `response/38d9c7f8-….rmx` — копия в wd).
     Положительный контроль чувствительности Bi-214/Pb-214 к ФОРМЕ кривой (сосуд А/Б её не меняет:
     ε(А)/ε(Б) = 0.80…0.82 на всех узлах).

    python handover/p64-amber29/mk_extra.py <spectra_ab> <repo>
"""
import io
import os
import re
import sys


def main():
    sp, repo = sys.argv[1], sys.argv[2]
    e01 = io.open(os.path.join(sp, 'coalA_e01.xml'), encoding='utf-8-sig').read()
    # ⚠ внутри <Curve> есть <Efficiency>число</Efficiency> — узел ловится по <UseResponseMatrix>
    NODE = r'<Efficiency><Guid>.*?</UseResponseMatrix></Efficiency>'
    eff = re.search(NODE, e01, re.S).group(0)
    # --- суммы
    for tag, rng in (('sum30', range(1, 31)), ('sum10', range(21, 31))):
        total = None
        live = real = 0.0
        cal = None
        for i in rng:
            s = io.open(os.path.join(sp, 'coalA_e%02d.xml' % i), encoding='utf-8-sig').read()
            es = re.search(r'<EnergySpectrum>(.*?)</EnergySpectrum>', s, re.S).group(1)
            c = re.search(r'<Coefficients>(.*?)</Coefficients>', es, re.S).group(1)
            cal = cal or c
            assert c == cal, i
            pts = [int(x) for x in re.findall(r'<DataPoint>(-?\d+)</DataPoint>', es)]
            total = pts if total is None else [a + b for a, b in zip(total, pts)]
            live += float(re.search(r'<LiveTime>([^<]+)', es).group(1))
            real += float(re.search(r'<MeasurementTime>([^<]+)', es).group(1))
        s = e01
        es = re.search(r'<EnergySpectrum>(.*?)</EnergySpectrum>', s, re.S)
        block = es.group(1)
        block = re.sub(r'<Spectrum>.*?</Spectrum>', '<Spectrum>\n' + '\n'.join('          <DataPoint>%d</DataPoint>' % v for v in total) + '\n        </Spectrum>', block, flags=re.S)
        block = re.sub(r'<LiveTime>[^<]+', '<LiveTime>%.3f' % live, block)
        block = re.sub(r'<MeasurementTime>[^<]+', '<MeasurementTime>%.3f' % real, block)
        block = re.sub(r'<ValidPulseCount>\d+', '<ValidPulseCount>%d' % sum(total), block)
        block = re.sub(r'<TotalPulseCount>\d+', '<TotalPulseCount>%d' % sum(total), block)
        s = s[:es.start(1)] + block + s[es.end(1):]
        s = s.replace('<Name>Уголь активированный с радоном в равновесии с дпр_01</Name>', '<Name>уголь: сумма %s</Name>' % tag)
        with io.open(os.path.join(sp, 'coalA_%s.xml' % tag), 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(s)
        print('%s: %d съёмок, живое %.0f с, отсчётов %d' % (tag, len(list(rng)), live, sum(total)))
    # --- чужая кривая
    src = io.open(os.path.join(repo, 'tools', 'CORPUS', 'corpus', 'spectra', 'G1S24_K40_Mar_2.xml'), encoding='utf-8-sig').read()
    eff_x = re.search(NODE, src, re.S).group(0)
    guid = re.search(r'<Guid>([^<]+)', eff_x).group(1)
    name = re.search(r'<Name>([^<]+)', eff_x).group(1)
    s = e01.replace(eff, eff_x)
    with io.open(os.path.join(sp, 'coalX_e01.xml'), 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(s)
    print('coalX_e01: кривая %s, guid %s' % (name, guid))


if __name__ == '__main__':
    main()
