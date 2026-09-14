# -*- coding: utf-8 -*-
r"""П73 (V10): копии спектров стенда в D:\BqMoni_Claude\p73\spectra\ (корпус и YandexDisk — только чтение).

  RC103_Cs137_50mm.xml        — съёмка Amber 14.09.2026 как есть (фон встроен, 8 701 048 с);
  RC103_Cs137_50mm_recal.xml  — та же, калибровка переднего плана умножена на GAIN (пик 662 в файле сидит на 650.08 кэВ,
                                 подгонка peak662.py; фон не трогается — FSA перекладывает его по энергии сам);
  RC103_Cs137_0cm.xml         — корпусный (узел <Efficiency> живого склада внутри);
  ASN16_Cs137_10cm.xml        — корпусный (узла нет — непонятная часть).
"""
import os, re, shutil, sys

REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
SP = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'spectra')
P = r'D:\BqMoni_Claude\p73'
OUT = os.path.join(P, 'spectra')
os.makedirs(OUT, exist_ok=True)
GAIN = 661.657 / 650.08   # E(662) в файле = 650.08 ± 0.55 кэВ (гаусс+линия, peak662.py)

shutil.copyfile(os.path.join(P, 'Cs137_point50_raw.xml'), os.path.join(OUT, 'RC103_Cs137_50mm.xml'))
shutil.copyfile(os.path.join(SP, 'RC103_Cs137_0cm.xml'), os.path.join(OUT, 'RC103_Cs137_0cm.xml'))
shutil.copyfile(os.path.join(SP, 'ASN16_Cs137_10cm.xml'), os.path.join(OUT, 'ASN16_Cs137_10cm.xml'))

with open(os.path.join(P, 'Cs137_point50_raw.xml'), 'rb') as f:
    data = f.read()
# первая <EnergyCalibration> — передний план; вторая (в BackgroundEnergySpectrum) не трогается
m = re.search(rb'<EnergyCalibration>.*?</EnergyCalibration>', data, re.S)
block = m.group(0)
def scale(mm):
    v = float(mm.group(1)) * GAIN
    return ('<Coefficient>%s</Coefficient>' % repr(v)).encode()
new_block, n = re.subn(rb'<Coefficient>([^<]+)</Coefficient>', scale, block)
if n != 5:
    raise SystemExit('коэффициентов %d, ожидалось 5' % n)
data2 = data[:m.start()] + new_block + data[m.end():]
with open(os.path.join(OUT, 'RC103_Cs137_50mm_recal.xml'), 'wb') as f:
    f.write(data2)
print('GAIN = %.6f' % GAIN)
print(new_block.decode())
print(sorted(os.listdir(OUT)))
