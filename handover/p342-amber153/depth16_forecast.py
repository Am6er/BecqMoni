# -*- coding: utf-8 -*-
u"""П342 (AMBER153, остаток (г)): прогноз паспорта G1S16 в сосудах, если дать сценам G1S16 ту же глубину
кристалла, что у G1S24 (9.0 мм, `CorpusGeomProbe.G1S24CrystalDepthMm`, П228).

ЗАЧЕМ. G1S16 и G1S24 — ОДИН прибор (УДС-ГЦ-63×63 №0086-16, README §1.3). У G1S24 кристалл опущен на 9.0 мм у
ВСЕХ 26 сцен; у G1S16 точечные сцены стоят на 58.5 / 257 мм (50 + 7 + 1.5 и 250 + 7 — то есть те же +7…+8.5 мм
расстоянием), а 13 сосудных — на нуле. Кривые обеих глубин посчитаны ОДНОЙ сборкой, одним числом историй
(`CorpusEffProbe --n=400000 --force`, свои копии сцен и спектров в `D:\\BqMoni_Claude\\p342\\eff\\`), прогноз —
как у П228: разбор/паспорт × ε_ref/ε_var по главным линиям нуклида (среднее геометрическое с весом выхода).

Положительный контроль: кривая `ref`, пересчитанная этой сборкой, против кривой в корпусе (узел `<Efficiency>`
спектра дерева) — расхождение по узлам печатается; оно и есть мера шума/воспроизводимости прогноза.
"""
import csv
import io
import math
import os
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
EFF = r'D:\BqMoni_Claude\p342\eff'
CORPUS_SPECTRA = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'spectra')

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

#: Главные линии (кэВ, выход %) — ряд/нуклид; для прогноза уровня, не для разбора.
LINES = {
    'Cs-137': [(661.66, 85.1)],
    'K-40': [(1460.82, 10.66)],
    'Ra-226': [(351.93, 35.6), (609.31, 45.5), (1120.29, 14.9), (1764.49, 15.3), (295.22, 18.4)],
    'Th-232': [(238.63, 43.6), (338.32, 11.3), (583.19, 30.4), (911.20, 25.8), (968.97, 15.8), (2614.51, 35.6)],
    'Am-241': [(59.54, 35.9)],
}


#: Спектр -> спектр, чьей сценой посчитаны кривые ref/var (представитель: тот же сосуд, тот же класс набивки).
REP = {
    'G1S16_Th232_Denta100': 'G1S16_Th232_Denta100', 'G1S16_Mix_Denta100': 'G1S16_Th232_Denta100',
    'G1S16_Th232_Petri': 'G1S16_Th232_Petri', 'G1S16_Mix_Petri': 'G1S16_Th232_Petri',
    'G1S16_Mix_Mar': 'G1S16_Cs137_Mar',
    'G1S16_Cs137_Mar': 'G1S16_Cs137_Mar', 'G1S16_K40_Mar': 'G1S16_Cs137_Mar', 'G1S16_Ra226_Mar': 'G1S16_Cs137_Mar',
    'G1S16_Th232_Mar_2': 'G1S16_Cs137_Mar',
    'G1S16_Cs137_Mar_2': 'G1S16_Cs137_Mar_2', 'G1S16_K40_Mar_2': 'G1S16_Cs137_Mar_2',
    'G1S16_Ra226_Mar_2': 'G1S16_Cs137_Mar_2', 'G1S16_Th232_Mar_3': 'G1S16_Cs137_Mar_2',
}


def curve(path):
    root = ET.parse(path).getroot()
    rd = root.find('ResultDataList').find('ResultData')
    eff = rd.find('Efficiency')
    pts = []
    for d in eff.find('Curve'):
        e = float(d.find('Energy').text)
        v = float(d.find('Efficiency').text)
        pts.append((e, v))
    pts.sort()
    return pts, eff.find('ComputeStamp').text


def at(pts, e):
    for (e0, v0), (e1, v1) in zip(pts[:-1], pts[1:]):
        if e0 <= e <= e1:
            t = (math.log(e) - math.log(e0)) / (math.log(e1) - math.log(e0))
            return math.exp(math.log(v0) + t * (math.log(v1) - math.log(v0)))
    raise ValueError('энергия %.1f вне кривой' % e)


def level(pts, nuc):
    ws = LINES[nuc]
    tot = sum(w for _, w in ws)
    return math.exp(sum(w * math.log(at(pts, e)) for e, w in ws) / tot)


def main():
    pas = {r['spectrum']: r for r in csv.DictReader(io.open(os.path.join(HERE, 'passport_rev47_full.csv'),
                                                            encoding='utf-8-sig'))}
    rows = []
    print('контроль: кривая ref этой сборки против кривой корпуса (узлы ≥ 50 кэВ): медиана |Δ| и худший узел')
    for key in sorted(set(REP.values())):
        name = key + '.xml'
        ref, st_ref = curve(os.path.join(EFF, 'spectra_ref', name))
        cor, st_cor = curve(os.path.join(CORPUS_SPECTRA, name))
        ds = []
        for e, v in ref:
            if e >= 50:
                ds.append(abs(v / at(cor, e) - 1.0))
        ds.sort()
        print('  %-24s медиана %.4f %%, худший %.4f %%, клеймо %s' % (
            key, 100 * ds[len(ds) // 2], 100 * ds[-1], 'то же' if st_ref == st_cor else 'ДРУГОЕ'))
    print()
    print('%-24s %-7s %-10s %7s | %-18s | ε_var/ε_ref | прогноз | порог' % ('спектр', 'нуклид', 'группа', 'rev47',
                                                                          'сцена кривых'))
    for key in sorted(REP):
        if key not in pas:
            continue
        p = pas[key]
        nuc = p['nuclide']
        rep = REP[key]
        ref, _ = curve(os.path.join(EFF, 'spectra_ref', rep + '.xml'))
        var, st = curve(os.path.join(EFF, 'spectra_var', rep + '.xml'))
        if 'gap' in st:
            pass
        k = level(var, nuc) / level(ref, nuc)
        r0 = float(p['ratio'])
        r1 = r0 / k
        tol = float(p['tol'])
        print('%-24s %-7s %-10s %7.3f | %-18s | %11.4f | %7.3f | %s' % (
            key, nuc, p['group'], r0, rep + ('' if rep == key else ' *'), k, r1,
            ('вне' if abs(r1 - 1) > tol else 'в пороге') + (' (было вне)' if abs(r0 - 1) > tol else '')))
        rows.append([key, nuc, p['group'], rep, '%.5f' % r0, '%.5f' % k, '%.5f' % r1, '%.3f' % tol,
                     int(abs(r0 - 1) > tol), int(abs(r1 - 1) > tol)])
    print('  * — кривые соседней сцены того же сосуда (маринелли — тот же класс набивки ОИСН-06 / ОИСН-16; '
          'РИСН-379 ρ 1.0 — по ОИСН-06; цилиндры смеси — по сцене ОИСН-10 того же сосуда)')
    with io.open(os.path.join(HERE, 'depth16_forecast.csv'), 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.writer(fh)
        w.writerow(['spectrum', 'nuclide', 'group', 'curve_scene_spectrum', 'ratio_rev47', 'eff_var_over_ref',
                    'ratio_forecast', 'tol', 'out_rev47', 'out_forecast'])
        w.writerows(rows)
    out0 = sum(r[8] for r in rows)
    out1 = sum(r[9] for r in rows)
    print('вне порога: rev47 %d → прогноз %d из %d' % (out0, out1, len(rows)))
    # ИНТЕРПОЛЯЦИЯ (не счёт): ln ε линейно по глубине, k(d) = k(9)^(d/9) — какая глубина вывела бы отношение на 1.0
    print()
    print('интерполяция (ln ε линейно по глубине, НЕ посчитано): глубина, при которой разбор/паспорт = 1.000')
    for key in sorted(REP):
        r = [x for x in rows if x[0] == key]
        if not r:
            continue
        r0, k9 = float(r[0][4]), float(r[0][5])
        d = 9.0 * math.log(r0) / math.log(k9)
        print('  %-24s %-7s k(9 мм) %.4f (%.2f %%/мм)  глубина под 1.0 ≈ %4.1f мм;  при 6 мм %.3f, при 3.5 мм %.3f' % (
            key, r[0][1], k9, 100 * (k9 ** (1 / 9.0) - 1), d, r0 / k9 ** (6 / 9.0), r0 / k9 ** (3.5 / 9.0)))


if __name__ == '__main__':
    main()
