# -*- coding: utf-8 -*-
# П106: реестры check_matrix_keys.py — рычаги замера состава возврата (SIM, «неприменимо») и счётчики (SIM_NOT_SETTINGS).
# Файл CRLF: правка в LF-форме, запись обратно с CRLF.
import sys
p = r'D:\BqMoni_Claude\p106\wt\tools\check_matrix_keys.py'
d = open(p, 'rb').read()
assert b'\r\n' in d
s = d.decode('utf-8').replace('\r\n', '\n')
old = u"""    (u'LayerHardCutoffDeg', False, False, u'неприменимо',
     u'M13, П100: угол отсечки жёстких столкновений смешанной схемы (умолчание 20°) — режим замера '
     u'независимости η от отсечки; двигает LayerReturnProbe --cutoff=; в клеймо не входит — единый счёт '
     u'идёт умолчанием'),
"""
new = old + u"""    # (`M13`, П106 19.09.2026) Рычаги замера состава возврата электрона по
    # населениям -- зеркала рычагов арбитра g4cf (killescown / killesccarry /
    # killescbrem / killret*); двигает только G4RawProbe --ret-kill=. Тот же
    # разряд, что ElectronTransportNoEarlyExit: ни один путь их не ставит.
    (u'LayerReturnOwn', False, False, u'неприменимо',
     u'M13, П106: рычаг замера — свой электрон на грани списывается (= killescown); двигает G4RawProbe --ret-kill=own'),
    (u'LayerReturnCarried', False, False, u'неприменимо',
     u'M13, П106: рычаг замера — занесённый электрон при выходе списывается (= killesccarry); двигает G4RawProbe --ret-kill=carry'),
    (u'LayerExitBremsstrahlung', False, False, u'неприменимо',
     u'M13, П106: рычаг замера — тормозное слоя в точке выхода своего электрона (= killescbrem); двигает G4RawProbe --ret-kill=brem'),
    (u'LayerReturnKill', False, False, u'неприменимо',
     u'M13, П106: рычаг замера — списать вернувшегося своего электрона на входе: 1 всех, 2 через ту же грань, 3 через другую (= killret/killretsame/killretother); двигает G4RawProbe --ret-kill=ret|same|other'),
"""
assert s.count(old) == 1, 'SIM anchor'
s = s.replace(old, new)
old2 = u"""    u'CountLayerSteps', u'CountLayerHardCollisions',
"""
new2 = old2 + u"""    # (`M13`, П106) Счётчики населений возврата -- вылеты/возвраты занесённого,
    # возвраты своего по граням, списанные рычагом; читает `G4RawProbe`.
    u'CountLayerEscapesCarried', u'CountLayerReturnsCarried',
    u'CountLayerReturnsSameFace', u'CountLayerReturnsOtherFace', u'CountLayerReturnsKilled',
"""
assert s.count(old2) == 1, 'SIM_NOT_SETTINGS anchor'
s = s.replace(old2, new2)
open(p, 'wb').write(s.replace('\n', '\r\n').encode('utf-8'))
print('ok')
