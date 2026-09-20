# -*- coding: utf-8 -*-
# П106: слово outbrem в --ret-kill= (G4RawProbe.cs, BOM+CRLF) и поле LayerBornBremsstrahlung в реестре check_matrix_keys.py (CRLF).
p = r'D:\BqMoni_Claude\p106\wt\tools\effmaker\probes\G4RawProbe.cs'
d = open(p, 'rb').read()
assert d[:3] == b'\xef\xbb\xbf'
s = d[3:].decode('utf-8')
n = 0
def rep(old, new):
    global s, n
    assert s.count(old) == 1, old[:60]
    s = s.replace(old, new); n += 1
rep('    ///                [--elmix=0|1] [--ret-kill=own,carry,brem,ret,same,other]\r\n',
    '    ///                [--elmix=0|1] [--ret-kill=own,carry,brem,ret,same,other,outbrem]\r\n')
rep('    /// `killretother`; `ret`, `same`, `other` взаимно исключают друг друга).\r\n',
    '    /// `killretother`; `ret`, `same`, `other` взаимно исключают друг друга);\r\n'
    '    /// `outbrem` — тормозное электронов, РОЖДЁННЫХ в обвязке/пробе, не\r\n'
    '    /// разыгрывать (`OutsideBremsstrahlung`; = `killoutbrem`).\r\n')
rep('            bool retOwn = true, retCarry = true, retBrem = true;\r\n',
    '            bool retOwn = true, retCarry = true, retBrem = true, retOutBrem = true;\r\n')
rep('                            case "other": retKill = 3; break;\r\n',
    '                            case "other": retKill = 3; break;\r\n                            case "outbrem": retOutBrem = false; break;\r\n')
rep('                                Console.Error.WriteLine("--ret-kill= принимает own, carry, brem, ret, same, other через запятую: " + a);\r\n',
    '                                Console.Error.WriteLine("--ret-kill= принимает own, carry, brem, ret, same, other, outbrem через запятую: " + a);\r\n')
rep('            simulator.LayerReturnKill = retKill;\r\n',
    '            simulator.LayerReturnKill = retKill;\r\n            simulator.LayerBornBremsstrahlung = retOutBrem;\r\n')
rep('                                + ", тормозное выхода " + (retBrem ? "есть" : "НЕТ")\r\n',
    '                                + ", тормозное выхода " + (retBrem ? "есть" : "НЕТ")\r\n                                + ", тормозное рождённых в обвязке " + (retOutBrem ? "есть" : "НЕТ")\r\n')
open(p, 'wb').write(b'\xef\xbb\xbf' + s.encode('utf-8'))
print('probe patched', n)

p = r'D:\BqMoni_Claude\p106\wt\tools\check_matrix_keys.py'
d = open(p, 'rb').read()
s = d.decode('utf-8')
old = ("    (u'LayerReturnKill', False, False, u'неприменимо',\r\n"
       "     u'M13, П106: рычаг замера — списать вернувшегося своего электрона на входе: 1 всех, 2 через ту же грань, 3 через другую (= killret/killretsame/killretother); двигает G4RawProbe --ret-kill=ret|same|other'),\r\n")
new = old + ("    (u'LayerBornBremsstrahlung', False, False, u'неприменимо',\r\n"
             "     u'M13, П106: рычаг замера — тормозное электронов, рождённых в обвязке/пробе (OutsideBremsstrahlung), не разыгрывать (= killoutbrem); двигает G4RawProbe --ret-kill=outbrem'),\r\n")
assert s.count(old) == 1
s = s.replace(old, new)
open(p, 'wb').write(s.encode('utf-8'))
print('keys patched')
