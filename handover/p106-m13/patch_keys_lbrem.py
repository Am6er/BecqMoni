# -*- coding: utf-8 -*-
# П106: реестры check_matrix_keys.py для ключа ElectronLayerBremAlongPath (MATRIX, SIM общая, ONE_TRUTH) и счётчиков.
p = r'D:\BqMoni_Claude\p106\wt\tools\check_matrix_keys.py'
d = open(p, 'rb').read()
s = d.decode('utf-8')
def rep(old, new):
    global s
    assert s.count(old) == 1, old[:70]
    s = s.replace(old, new)
# MATRIX (реестр 1)
rep("     u'тело побитово = склад rev29. Путь КРИВОЙ берёт ключ от этого же умолчания (реестр 3)'),\r\n    (u'Seed', True, u'--seed=', None, u'намеренно',\r\n",
    "     u'тело побитово = склад rev29. Путь КРИВОЙ берёт ключ от этого же умолчания (реестр 3)'),\r\n"
    "    (u'ElectronLayerBremAlongPath', True, u'--lbrem=', None, u'намеренно',\r\n"
    "     u'M13 (вторая половина), П106 19.09.2026: ТОРМОЗНОЕ ЭЛЕКТРОНА В СЛОЯХ ОБВЯЗКИ ПО ХОДУ ПЕРЕНОСА '\r\n"
    "     u'(только под eltr=1) — кванты тонкой мишени вещества текущего слоя на шагах переноса, направление по '\r\n"
    "     u'электрону (Цай); толстые мишени в точке рождения (OutsideBremsstrahlung) и выхода (LayerBremsstrahlung) '\r\n"
    "     u'у ведомых электронов не разыгрываются. Двигает континуум сцен с обвязкой (мягкие полосы) и поток '\r\n"
    "     u'случайных чисел, поэтому входит в клеймо (lbrem=1) и пишется хвостом LBRM — только включённым (T42); '\r\n"
    "     u'ВЫКЛ умолчанием, решение о ВКЛ (= физика 21, единый счёт) — Amber по числам П106. Путь КРИВОЙ берёт '\r\n"
    "     u'ключ от этого же умолчания (реестр 3)'),\r\n"
    "    (u'Seed', True, u'--seed=', None, u'намеренно',\r\n")
# SIM (реестр 2)
rep("     u'в клеймо кривой — elmix=1 только включённым. Умолчание поля симулятора — умолчание склада (правило I)'),\r\n    (u'LayerHardCutoffDeg', False, False, u'неприменимо',\r\n",
    "     u'в клеймо кривой — elmix=1 только включённым. Умолчание поля симулятора — умолчание склада (правило I)'),\r\n"
    "    (u'ElectronLayerBremAlongPath', True, True, u'общая',\r\n"
    "     u'M13, П106 19.09.2026: тормозное электрона в слоях обвязки по ходу переноса. Матрица от своих '\r\n"
    "     u'настроек (MakeSimulator), кривая от их умолчания (EfficiencyCalculation.Run, тем же путём, что '\r\n"
    "     u'ElectronLayerMixedScattering) — ВЫКЛ до решения Amber о едином счёте; в клеймо кривой — lbrem=1 '\r\n"
    "     u'только включённым. Умолчание поля симулятора — умолчание склада (правило I)'),\r\n"
    "    (u'LayerHardCutoffDeg', False, False, u'неприменимо',\r\n")
# ONE_TRUTH (реестр 4)
rep("    (u'ElectronLayerMixedScattering', u'ElectronLayerMixedScattering', None),\r\n)\r\n",
    "    (u'ElectronLayerMixedScattering', u'ElectronLayerMixedScattering', None),\r\n"
    "    # (`M13`, П106 19.09.2026) Ключ, заведённый ВЫКЛ до решения Amber о едином счёте\r\n"
    "    # (физика 21): то же правило -- пробы прямого вызова берут умолчание склада.\r\n"
    "    (u'ElectronLayerBremAlongPath', u'ElectronLayerBremAlongPath', None),\r\n)\r\n")
# счётчики
rep("    u'CountLayerReturnsSameFace', u'CountLayerReturnsOtherFace', u'CountLayerReturnsKilled',\r\n",
    "    u'CountLayerReturnsSameFace', u'CountLayerReturnsOtherFace', u'CountLayerReturnsKilled',\r\n"
    "    # (`M13`, П106) Счётчики тормозного по ходу переноса в слоях -- квантов и энергия; читает `G4RawProbe`.\r\n"
    "    u'CountLayerBremPhotons', u'SumLayerBremKev',\r\n")
open(p, 'wb').write(s.encode('utf-8'))
print('ok')
