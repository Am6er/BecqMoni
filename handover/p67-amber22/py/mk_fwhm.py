# -*- coding: utf-8 -*-
"""П67: у `face81` (файл Amber 2025-09-08) НЕТ блока калибровки ПШПВ вовсе (`PowerFwhmCalibration` отсутствует) —
разбор FSA берёт ширину неизвестно откуда и рушится (A(Th) 280 Бк, сплайн 80 %, 2614 −22 %). В по-сценные копии
`face81__*.xml` (и `.escale.xml`, если есть) вставляется блок `<PowerFwhmCalibration>` из `edge93.xml` (тот же прибор,
калибровка Amber по 10 пикам 29…2614 кэВ, ExpGaussExp 1.2/2.6; усиление файлов различается на ~10 %, в кэВ это
~3 % ширины) — перед `</ResultData>`, как в файлах Amber. Идемпотентно.

    python mk_fwhm.py <каталог копий>
"""
import glob
import io
import os
import re
import sys

src = io.open(r'D:\BqMoni_Claude\p67\spectra\edge93.xml', encoding='utf-8', newline='').read()
i = src.index('<PowerFwhmCalibration>')
j = src.index('</PowerFwhmCalibration>') + len('</PowerFwhmCalibration>')
block = src[i:j]
n = 0
for path in sorted(glob.glob(os.path.join(sys.argv[1], 'face81__*.xml'))):
    t = io.open(path, encoding='utf-8', newline='').read()
    if '<PowerFwhmCalibration>' in t:
        continue
    k = t.index('</ResultData>')
    t = t[:k] + block + '\n    ' + t[k:]
    io.open(path, 'w', encoding='utf-8', newline='').write(t)
    n += 1
print('вставлено в %d копий face81' % n)
