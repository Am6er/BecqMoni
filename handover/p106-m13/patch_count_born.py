# -*- coding: utf-8 -*-
# П106: счётчик электронов, рождённых в обвязке и отданных переносу в слоях (CountLayerBorn) — для сверки населения с out_lepton арбитра.
def patch(p, reps):
    d = open(p, 'rb').read(); bom = d[:3] == b'\xef\xbb\xbf'
    s = (d[3:] if bom else d).decode('utf-8')
    for old, new in reps:
        assert s.count(old) == 1, (p, old[:60])
        s = s.replace(old, new)
    open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + s.encode('utf-8'))
    print('ok', p)
patch(r'D:\BqMoni_Claude\p106\wt\BecquerelMonitor\EfficiencyMaker\EfficiencySimulator.cs', [
    ('        public long CountLayerEscapesCarried, CountLayerReturnsCarried;\r\n',
     '        public long CountLayerEscapesCarried, CountLayerReturnsCarried;\r\n'
     '        /// <summary>(П106) Электронов, рождённых в обвязке/пробе и ОТДАННЫХ переносу в слоях (≥ 20 кэВ, занос не снят) — население заноса до отбора.</summary>\r\n'
     '        public long CountLayerBorn;\r\n'),
    ('            this.layerBremPush = push;\r\n            this.layerBremEnabled = this.LayerBornBremsstrahlung;\r\n',
     '            this.layerBremPush = push;\r\n            this.layerBremEnabled = this.LayerBornBremsstrahlung;\r\n            this.CountLayerBorn++;\r\n'),
])
patch(r'D:\BqMoni_Claude\p106\wt\tools\effmaker\probes\G4RawProbe.cs', [
    ('            Console.WriteLine("населения возврата (`M13` П106): своих вылетов {0}, своих возвратов {1} (через ту же грань {2}, через другую {3}, списано рычагом {4}); занесённых вылетов из кристалла {5}, их возвратов {6}",\r\n',
     '            Console.WriteLine("населения возврата (`M13` П106): рождённых в обвязке и отданных переносу {7}; своих вылетов {0}, своих возвратов {1} (через ту же грань {2}, через другую {3}, списано рычагом {4}); занесённых вылетов из кристалла {5}, их возвратов {6}",\r\n'),
    ('                              simulator.CountLayerEscapesCarried, simulator.CountLayerReturnsCarried);\r\n',
     '                              simulator.CountLayerEscapesCarried, simulator.CountLayerReturnsCarried, simulator.CountLayerBorn);\r\n'),
])
patch(r'D:\BqMoni_Claude\p106\wt\tools\check_matrix_keys.py', [
    ("    u'CountLayerEscapesCarried', u'CountLayerReturnsCarried',\r\n",
     "    u'CountLayerEscapesCarried', u'CountLayerReturnsCarried', u'CountLayerBorn',\r\n"),
])
