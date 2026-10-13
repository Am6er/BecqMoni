# П178: две цепочки отсоединённого счёта (ASCII, CRLF).
d = 'D:/BqMoni_Claude/p178/g4/'.replace('/', '\\')
run = 'call ' + 'D:/BqMoni_Claude/p178/g4cf/run_g4cf.bat'.replace('/', '\\')
p175 = 'D:/BqMoni_Claude/p175/g4/'.replace('/', '\\')


def g4(keys, scene, E, N, log):
    return [f'echo start {log} %DATE% %TIME% >> {d}chainA_status.txt',
            f'{run} vacuum {keys} scene {scene} hist {E} {N} 0.1 > {d}{log} 2> {d}{log}.err',
            f'echo code=%ERRORLEVEL% >> {d}{log}.err',
            f'echo done {log} %DATE% %TIME% >> {d}chainA_status.txt']


A = ['@echo off', 'rem P178 chain A: g4cf runs (norayl / nogp / seed)', f'cd /d {d}']
A += g4('norayl seed 16460', d + 'scene_Bnc60.txt', 60, 100000000, 'g4_nr60.log')
A += g4('nogp seed 16460', p175 + 'scene_B_all.txt', 60, 100000000, 'g4_gp60.log')
A += g4('norayl seed 16432', d + 'scene_Cnc32.txt', 32, 200000000, 'g4_nr32.log')
A += g4('norayl seed 16460', p175 + 'scene_P2.txt', 60, 50000000, 'g4_P2nr60.log')
A += g4('seed 26460', p175 + 'scene_B_all.txt', 60, 100000000, 'g4_B60_s2.log')
A += [f'echo ALLDONE %DATE% %TIME% >> {d}chainA_status.txt']

pb = 'D:/BqMoni_Claude/p178/wt/tools/effmaker/probes/build_p178'.replace('/', '\\')
geo = 'D:/BqMoni_Claude/p178/wt/tools/CORPUS/corpus/geometries/RC103_marinelli05_kcl.in'.replace('/', '\\')


def ours(g, E, N, seed, out, extra):
    exe = pb + '\\G4RawProbe.exe'
    return [f'echo start {out} %DATE% %TIME% >> {d}chainB_status.txt',
            f'"{exe}" --geometry={g} --energy={E} --n={N} --bin=0.1 --no-light --peakw --seed={seed} --out={d}{out}.csv {extra} > {d}{out}.txt 2>&1',
            f'echo code=%ERRORLEVEL% >> {d}{out}.txt',
            f'echo done {out} %DATE% %TIME% >> {d}chainB_status.txt']


B = ['@echo off', 'rem P178 chain B: our G4RawProbe runs (coh=0, big analog)', f'cd /d {pb}']
B += ours(geo, 60, 20000000, 17860, 'our_nc60', '--coh=0')
B += ours(geo, 32, 40000000, 17832, 'our_nc32', '--coh=0')
B += ours(p175 + 'geo_P2.in', 60, 5000000, 17860, 'our_P2nc60', '--coh=0')
B += ours(geo, 60, 200000000, 27860, 'our_big60', '')
B += [f'echo ALLDONE %DATE% %TIME% >> {d}chainB_status.txt']

import os
for name, L in (('chainA.cmd', A), ('chainB.cmd', B)):
    if os.path.exists(d + name):
        continue  # идущую цепочку не переписывать
    s = '\r\n'.join(L) + '\r\n'
    open(d + name, 'wb').write(s.encode('ascii'))
print('ok')

# Цепочка C (после сборки p178b): когерентное в CsI по сечению арбитра (--cohx).
pb = pb + 'b'
C = ['@echo off', 'rem P178 chain C: our G4RawProbe with CsI coherent scaled to Geant4 (--cohx)', f'cd /d {pb}']
C += [x.replace('chainB_status', 'chainC_status') for x in ours(geo, 32, 40000000, 18832, 'our_cx32', '--cohx=0.76741')]
C += [x.replace('chainB_status', 'chainC_status') for x in ours(geo, 60, 20000000, 18860, 'our_cx60', '--cohx=1.05876')]
C += [x.replace('chainB_status', 'chainC_status') for x in ours(p175 + 'geo_P2.in', 32, 5000000, 18832, 'our_P2cx32', '--cohx=0.76741')]
C += [f'echo ALLDONE %DATE% %TIME% >> {d}chainC_status.txt']
if not os.path.exists(d + 'chainC.cmd'):
    open(d + 'chainC.cmd', 'wb').write(('\r\n'.join(C) + '\r\n').encode('ascii'))
print('C ok')

# Цепочка D: контроль разборки общего процесса на 32 кэВ (Rayl ВКЛ, сцена C32 П175).
D = ['@echo off', 'rem P178 chain D: g4cf nogp control at 32 keV', f'cd /d {d}']
D += [x.replace('chainA_status', 'chainD_status') for x in g4('nogp seed 16432', p175 + 'scene_C32_all.txt', 32, 200000000, 'g4_gp32.log')]
D += [f'echo ALLDONE %DATE% %TIME% >> {d}chainD_status.txt']
if not os.path.exists(d + 'chainD.cmd'):
    open(d + 'chainD.cmd', 'wb').write(('\r\n'.join(D) + '\r\n').encode('ascii'))
print('D ok')

# Цепочка E: аналоговый пик на 32 кэВ, 2e8 историй (кратное рассеяние когерентного против одного у взвешенной).
E_ = ['@echo off', 'rem P178 chain E: our G4RawProbe 32 keV 2e8 (analog vs weighted peak)', f'cd /d {pb}']
E_ += [x.replace('chainB_status', 'chainE_status') for x in ours(geo, 32, 200000000, 28832, 'our_big32', '')]
E_ += [f'echo ALLDONE %DATE% %TIME% >> {d}chainE_status.txt']
if not os.path.exists(d + 'chainE.cmd'):
    open(d + 'chainE.cmd', 'wb').write(('\r\n'.join(E_) + '\r\n').encode('ascii'))
print('E ok')

# Цепочка F (сборка p178c): фотоэффект вне кристалла по фиту арбитра (--photox), сверка с НЕмасштабированной сценой П164.
pbc = pb[:-1] + 'c'
def oursF(*a):
    return [x.replace('chainB_status', 'chainF_status').replace(pb + chr(92), pbc + chr(92)) for x in ours(*a)]
F = ['@echo off', 'rem P178 chain F: our G4RawProbe with outside photo scaled to Geant4 fit (--photox)', f'cd /d {pbc}']
F += oursF(geo, 60, 20000000, 19860, 'our_px60', '--photox=0.9770')
F += oursF(geo, 32, 40000000, 19832, 'our_px32', '--photox=1.0104')
F += oursF(geo, 32, 40000000, 29832, 'our_pxcx32', '--photox=1.0104 --cohx=0.76741')
F += [f'echo ALLDONE %DATE% %TIME% >> {d}chainF_status.txt']
if not os.path.exists(d + 'chainF.cmd'):
    open(d + 'chainF.cmd', 'wb').write(('\r\n'.join(F) + '\r\n').encode('ascii'))
print('F ok')
