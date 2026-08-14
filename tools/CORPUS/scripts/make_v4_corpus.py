# -*- coding: utf-8 -*-
"""V4: временная копия корпуса с ОДНИМ спектром ASN16_Th232, которому дана
кривая эффективности из поставки (форма ЛСРМ-цилиндра; по V1 одной кривой на
модель достаточно выше 200 кэВ). Сам корпус НЕ трогается.

Вопрос V4: уйдёт ли избыток 430–500 кэВ, когда амплитуду компонента перестанут
задавать одни сильные высокие линии. Ответ 14.08.2026: уходит ТРЕТЬ
(35.1 σ → 23.1 σ), разбор — tools/pie/README.md, «разбор недобора», п. 3.
Перегонять этот же опыт стоит, когда появится ТОЧНАЯ кривая ASN16 (E1 или
аттестация): оставшиеся две трети — либо форма поставочной кривой, либо
другой механизм.

    python tools/CORPUS/scripts/make_v4_corpus.py
    cd tools/CORPUS/scripts/wd_app
    .\CorpusFsaProbe.exe --corpus=..\wd_v4 --only=ASN16_Th232 --near=430:500
"""
import csv
import io
import os
import shutil

repo = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                     '..', '..', '..'))
corpus = os.path.join(repo, 'tools', 'CORPUS', 'corpus')
# wd_* попадает под .gitignore рабочих каталогов корпуса
scratch = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'wd_v4')

if os.path.exists(scratch):
    shutil.rmtree(scratch)
os.makedirs(os.path.join(scratch, 'spectra'))
shutil.copy(os.path.join(corpus, 'parts.csv'), scratch)

rows = [r for r in csv.DictReader(open(
    os.path.join(repo, 'tools', 'CORPUS', 'data', 'eff_by_spectrum_lsrm.csv'),
    encoding='utf-8-sig')) if r['spectrum'] == 'ASN16_Th232']
if len(rows) != 151:
    raise SystemExit('ожидалась 151 точка кривой ASN16_Th232, найдено %d' % len(rows))

points = []
for r in rows:
    points.append('<ROIEfficiencyData><Energy>%s</Energy><Efficiency>%s</Efficiency>'
                  '<ErrorPercent>5</ErrorPercent></ROIEfficiencyData>'
                  % (r['E_keV'], r['eps']))

node = ('<Efficiency><Guid>a4000000-0000-4000-9000-000000000004</Guid>'
        '<Name>ASN16 LSRM cylinder (V4 experiment)</Name>'
        '<LastUpdated>2026-08-14T12:00:00+03:00</LastUpdated>'
        '<Origin>Simulation</Origin><Curve>%s</Curve></Efficiency>' % ''.join(points))

src = os.path.join(corpus, 'spectra', 'ASN16_Th232.xml')
with io.open(src, encoding='utf-8-sig') as f:
    text = f.read()
anchor = '</DeviceConfigReference>'
if anchor not in text:
    raise SystemExit('в спектре нет ' + anchor)
if '<Efficiency>' in text.split('<EnergySpectrum>')[0]:
    raise SystemExit('узел Efficiency уже есть — корпус изменился, проверить руками')
text = text.replace(anchor, anchor + node, 1)
with io.open(os.path.join(scratch, 'spectra', 'ASN16_Th232.xml'), 'w',
             encoding='utf-8-sig', newline='') as f:
    f.write(text)
print('готово:', scratch)
