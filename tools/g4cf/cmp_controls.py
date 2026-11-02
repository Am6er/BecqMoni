# -*- coding: utf-8 -*-
u"""Сверка контролей бета-раздела (`AMBER274`, физика 30, полоса `phys29c` 02.11.2026): модель против Geant4.

Модель — CSV `BetaSectionProbeA274 --direct --direct-csv=` (столбцы `kev`, `per_decay`; бины 2 кэВ с центром в
2b). Эталон — гистограммы `g4cf` (`HISTBEGIN … decays=`, `HIST <бин> <счёт>`, бин 2 кэВ) шести физлистов
(`opt0 opt3 opt4 ss liv pen`): `hemi` (нормировка на 2N) или конус (`FATE cone=<cos>` — на (1 − cos)/2 · N), вес
бета-ветвей — `SETUP espec … weight=`; конверсия Cs-137 — моно-линии с выходами модели.

Печать по полосе: модель / среднее шести ± стат. (решение Amber 30.10.2026: порог судится против среднего шести),
в пороге или нет; модель / GS3 (opt4, liv, pen — Goudsmit–Saunderson с Моттом); модель / среднее пяти без SS
(справочно); и РАЗБРОС ШЕСТИ sd/ср6 рядом со «± стат.» (Н6 критика замеров: «± стат.» — шум среднего, а не
расхождение физлистов, у которых χ² около среднего до 1800 на 5 ст. св.).

  python tools/g4cf/cmp_controls.py <контроль> <каталог эталона> <model.csv> [...]
      контроль: cs_rc0 | y90_rc0 | k40_rc0 | k40_rc50 | y90_asn
      каталог эталона: файлы <часть>_<физлист>.txt (части контроля — CTL ниже); у k40_rc50 с воздухом —
      k40s_<физлист>.txt (сцена airslab, конус 35°).
"""
import csv
import io
import math
import os
import re
import sys

PHYS = ['opt0', 'opt3', 'opt4', 'ss', 'liv', 'pen']
GS3 = ['opt4', 'liv', 'pen']
CS_PARTS = [('cs_espec', None), ('ce_K', 0.076), ('ce_L', 0.013696), ('ce_M', 0.0029273), ('ce_N', 0.00073059)]
B4 = [(40, 200, 0.10, u''), (200, 500, 0.10, u''), (500, 700, 0.10, u''), (700, 1000, 0.10, u'')]
CTL = {
    'cs_rc0': (CS_PARTS, [(40, 100, 0.10, u''), (100, 200, 0.10, u''), (200, 300, 0.10, u''),
                          (300, 400, 0.25, u'краевая'), (400, 700, 0.10, u''), (40, 3000, 0.05, u'сумма')]),
    'y90_rc0': ([('y90_rc0', None)], B4 + [(1000, 1500, 0.25, u'краевая'), (500, 1500, 0.10, u''),
                                          (40, 3000, 0.05, u'сумма')]),
    'k40_rc0': ([('k40_rc0', None)], B4 + [(1000, 1400, 0.25, u'краевая'), (40, 3000, 0.05, u'сумма')]),
    'k40_rc50': ([('k40s', None)], B4 + [(1000, 1400, 0.25, u'краевая'), (40, 3000, 0.05, u'сумма')]),
    'y90_asn': ([('y90_asn', None)], B4 + [(1000, 1500, 0.25, u'краевая'), (500, 1500, 0.10, u''),
                                          (40, 3000, 0.05, u'сумма')]),
}


def g4hist(path):
    h = {}
    dec = cone = None
    w = 1.0
    for line in io.open(path, encoding='utf-8', errors='replace'):
        if line.startswith('SETUP espec'):
            w = float(re.search(r'weight=([\d.eE+-]+)', line).group(1))
        elif line.startswith('HISTBEGIN'):
            m = re.search(r'bin_kev=([\d.]+) decays=(\d+)', line)
            if abs(float(m.group(1)) - 2.0) > 1e-9:
                raise RuntimeError(u'бин не 2 кэВ: ' + path)
            dec = int(m.group(2))
        elif line.startswith('HIST '):
            _, b, c = line.split()
            h[int(b)] = int(c)
        elif line.startswith('FATE'):
            cone = float(line.split()[1].split('=')[1])
    if dec is None:
        raise RuntimeError(u'нет HISTBEGIN: ' + path)
    return h, dec, w, cone


def g4spec(ref_dir, ctl, phys):
    S, V = {}, {}
    for name, y in CTL[ctl][0]:
        h, dec, w, cone = g4hist(os.path.join(ref_dir, '%s_%s.txt' % (name, phys)))
        norm = (1.0 - cone) / 2.0 if cone is not None else 0.5
        f = (w if y is None else y) * norm / dec
        for b, c in h.items():
            S[b] = S.get(b, 0.0) + c * f
            V[b] = V.get(b, 0.0) + c * f * f
    return S, V


def band(S, lo, hi):
    return sum(v for b, v in S.items() if lo <= 2 * b < hi)


def model(path):
    return dict((int(round(float(r['kev']) / 2)), float(r['per_decay'])) for r in csv.DictReader(io.open(path)))


def mean(specs, keys, lo, hi):
    vals = [band(specs[p][0], lo, hi) for p in keys]
    errs = [math.sqrt(band(specs[p][1], lo, hi)) for p in keys]
    return sum(vals) / len(vals), math.sqrt(sum(e * e for e in errs)) / len(vals), vals


def main(argv):
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass
    if len(argv) < 3 or argv[0] not in CTL:
        print(__doc__)
        return 2
    ctl, ref_dir = argv[0], argv[1]
    specs = {}
    for p in PHYS:
        try:
            specs[p] = g4spec(ref_dir, ctl, p)
        except (OSError, IOError, RuntimeError) as e:
            print(u'  нет %s: %s' % (p, e))
    have = [p for p in PHYS if p in specs]
    gs = [p for p in GS3 if p in specs]
    print(u'== %s: эталон — %s (%s)' % (ctl, u', '.join(have), ref_dir))
    bad = 0
    for path in argv[2:]:
        M = model(path)
        print(u'-- модель %s' % os.path.basename(path))
        print(u'| полоса, кэВ | порог | /ср%d ± стат. | в пороге | /GS3 ± стат. | /ср без SS | разброс sd/ср |' % len(have))
        print(u'|---|---|---|---|---|---|---|')
        for lo, hi, thr, nm in CTL[ctl][1]:
            mv = band(M, lo, hi)
            m6, e6, vals = mean(specs, have, lo, hi)
            sd = math.sqrt(sum((v - m6) ** 2 for v in vals) / (len(vals) - 1)) if len(vals) > 1 else 0.0
            g3, eg3, _ = mean(specs, gs, lo, hi) if gs else (float('nan'), 0.0, [])
            no_ss = [p for p in have if p != 'ss']
            m5, e5, _ = mean(specs, no_ss, lo, hi)
            r6 = mv / m6 if m6 > 0 else float('nan')
            ok = abs(r6 - 1.0) <= thr
            bad += 0 if ok else 1
            print(u'| %d–%d%s | ±%d %% | %.3f ± %.3f | %s | %.3f ± %.3f | %.3f ± %.3f | %.1f %% |'
                  % (lo, hi, u' (%s)' % nm if nm else u'', round(thr * 100), r6, r6 * e6 / m6, u'да' if ok else u'НЕТ',
                     mv / g3, mv / g3 * eg3 / g3, mv / m5, mv / m5 * e5 / m5, 100.0 * sd / m6))
    print(u'вне порога полос: %d' % bad)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
