# -*- coding: utf-8 -*-
"""П73 (V10): сцены полосы из корпусных образцов, побайтно (cp1251 + CRLF не трогаются).

  RC103_point50 — RC103_point0.in с `pdistance = 5 cm` (источник в 50 мм от наружной грани корпуса модели);
  RC103_point0  — копия корпусной сцены как есть (матрица — копия живого склада, чтение);
  ASN16_point10 — ASN16_lu_side.in: SourceType = POINT, pdistance = 10 cm, DS_Facing снят (торцом);
  index.csv     — опись для CorpusEffProbe (geometry,spectrum,preset,vessel).
"""
import os, re, shutil, sys

REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
GEOM = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'geometries')
STORE = r'D:\BqMoni_Claude\p73\store'
os.makedirs(STORE, exist_ok=True)


def edit(src, dst, subs, drop=()):
    with open(src, 'rb') as f:
        data = f.read()
    n_total = 0
    for pat, rep in subs:
        data, n = re.subn(pat, rep, data, flags=re.M)
        if n != 1:
            raise SystemExit('%s: шаблон %r найден %d раз, ожидался 1' % (src, pat, n))
        n_total += n
    for pat in drop:
        data, n = re.subn(pat, b'', data, flags=re.M)
        if n != 1:
            raise SystemExit('%s: снимаемая строка %r найдена %d раз' % (src, pat, n))
    with open(dst, 'wb') as f:
        f.write(data)
    return n_total


# 1. RC103_point50
edit(os.path.join(GEOM, 'RC103_point0.in'), os.path.join(STORE, 'RC103_point50.in'),
     [(rb'^pdistance = 0 cm', b'pdistance = 5 cm')])
# 2. RC103_point0 — как есть, с матрицей живого склада (только чтение оригинала)
shutil.copyfile(os.path.join(GEOM, 'RC103_point0.in'), os.path.join(STORE, 'RC103_point0.in'))
shutil.copyfile(os.path.join(GEOM, 'RC103_point0.rmx'), os.path.join(STORE, 'RC103_point0.rmx'))
# 3. ASN16_point10 — торцом, точка 10 см
edit(os.path.join(GEOM, 'ASN16_lu_side.in'), os.path.join(STORE, 'ASN16_point10.in'),
     [(rb'^SourceType = CYLINDER', b'SourceType = POINT'),
      (rb'^pdistance = 10 cm', b'pdistance = 10 cm')],
     drop=[rb'^DS_Facing = SIDE\r?\n'])

with open(os.path.join(STORE, 'index.csv'), 'w', encoding='utf-8', newline='') as f:
    f.write('geometry,spectrum,preset,vessel\n')
    f.write('RC103_point50,RC103_Cs137_50mm,RadiaCode-103,"точечный источник, 50 мм от торца"\n')
    f.write('RC103_point0,RC103_Cs137_0cm,RadiaCode-103,"точечный источник, вплотную к торцу"\n')
    f.write('ASN16_point10,ASN16_Cs137_10cm,Atom Spectra Nano 16 Pro,"точечный источник, 10 см от торца"\n')

for name in ('RC103_point50', 'RC103_point0', 'ASN16_point10'):
    path = os.path.join(STORE, name + '.in')
    with open(path, 'rb') as f:
        data = f.read()
    keys = [l.decode('cp1251') for l in data.split(b'\n')
            if re.match(rb'^(SourceType|pdistance|DS_Facing|DS_CrystalBox[XYZ]|DS_Crystal(Diameter|Height)|DS_Crystal(Front|Side)(Reflector|Cladding)Thickness|DS_DetectorMountingThickness|DS_Fwhm662)\b', l)]
    print(name, len(data), 'байт, CRLF' if b'\r\n' in data else 'LF')
    for k in keys:
        print('   ', k.rstrip())
print('store:', sorted(os.listdir(STORE)))
