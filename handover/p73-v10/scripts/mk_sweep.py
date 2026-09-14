# -*- coding: utf-8 -*-
r"""П73 (V10): развёртка «кристалл утоплен за корпусом» — торцевой ЗАЗОР (`DS_CrystalFrontGapThickness`, воздух, `AMBER1`)
между отражателем и корпусом сцены RC103_point0 при двух дистанциях источника (pdistance 0 и 5 см, от наружной грани корпуса).
Сцены и опись — D:\BqMoni_Claude\p73\store_sweep\, спектры-носители узла <Efficiency> — store_sweep\spectra\ (копии съёмки 50 мм).

  python mk_sweep.py            -> сцены RC103_g{мм}_d{мм}.in, index.csv, копии спектров
"""
import os, re, shutil

P = r'D:\BqMoni_Claude\p73'
SRC = os.path.join(P, 'store', 'RC103_point0.in')
ST = os.path.join(P, 'store_sweep')
SP = os.path.join(ST, 'spectra')
os.makedirs(SP, exist_ok=True)
GAPS = [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 15, 20]
DIST = [0, 50]

with open(SRC, 'rb') as f:
    base = f.read()
nl = b'\r\n' if b'\r\n' in base else b'\n'
GAP_MAT = nl.join([
    b'// Gap between reflector and cladding ',
    b'DS_nCrystalGapElements = 2',
    b'DS_RoCrystalGap = 0.001205',
    b'DS_ZCrystalGap[0] = 7',
    b'DS_FractionsCrystalGap[0] = 0.636483',
    b'DS_ZCrystalGap[1] = 8',
    b'DS_FractionsCrystalGap[1] = 0.363517',
    b'DS_FractionTypeGap = MASS',
    b'M_DS_Gap.MName = Air, dry',
    b'M_DS_Gap.Nmaterials = 1',
    b'M_DS_Gap.Name[0] = Air, dry                                 ',
    b'M_DS_Gap.MatRelWeight[0] = 1',
    b'', b''])

rows = []
for g in GAPS:
    for d in DIST:
        key = 'RC103_g%02d_d%02d' % (g, d)
        data = base
        # ключи толщин зазора — сразу за боковым отражателем
        data, n = re.subn(rb'^(DS_CrystalSideReflectorThickness = [^\r\n]*)(\r?\n)',
                          lambda m: m.group(1) + m.group(2) + ('DS_CrystalFrontGapThickness = %s cm' % (g / 10.0)).encode() + m.group(2) + b'DS_CrystalSideGapThickness = 0 cm' + m.group(2),
                          data, flags=re.M)
        assert n == 1, key
        data, n = re.subn(rb'^pdistance = 0 cm', ('pdistance = %s cm' % (d / 10.0)).encode(), data, flags=re.M)
        assert n == 1, key
        # вещество зазора — перед блоком сосуда
        data, n = re.subn(rb'^(// Cylindrical beaker materials:)', GAP_MAT + rb'\1', data, flags=re.M)
        assert n == 1, key
        with open(os.path.join(ST, key + '.in'), 'wb') as f:
            f.write(data)
        shutil.copyfile(os.path.join(P, 'Cs137_point50_raw.xml'), os.path.join(SP, key + '.xml'))
        rows.append('%s,%s,RadiaCode-103,"зазор %d мм, точка %d мм"' % (key, key, g, d))
with open(os.path.join(ST, 'index.csv'), 'w', encoding='utf-8', newline='') as f:
    f.write('geometry,spectrum,preset,vessel\n' + '\n'.join(rows) + '\n')
print('сцен:', len(rows))
