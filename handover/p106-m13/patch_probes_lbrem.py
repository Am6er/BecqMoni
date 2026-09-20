# -*- coding: utf-8 -*-
# П106: ключ --lbrem=0|1 у CorpusMatrixProbe и G4RawProbe (BOM+CRLF).
def patch(p, reps):
    d = open(p, 'rb').read()
    bom = d[:3] == b'\xef\xbb\xbf'
    s = (d[3:] if bom else d).decode('utf-8')
    for old, new in reps:
        assert s.count(old) == 1, (p, old[:70])
        s = s.replace(old, new)
    open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + s.encode('utf-8'))
    print('patched', p, len(reps))

patch(r'D:\BqMoni_Claude\p106\wt\tools\effmaker\probes\CorpusMatrixProbe.cs', [
    ('//                     [--ecomp=1] [--bpath=0|1|2] [--eltr=1] [--elmix=1]\r\n',
     '//                     [--ecomp=1] [--bpath=0|1|2] [--eltr=1] [--elmix=1] [--lbrem=1]\r\n'),
    ('// `--elmix=1` (`M13`, П100 18.09.2026) — СМЕШАННАЯ СХЕМА УПРУГОГО РАССЕЯНИЯ В\r\n',
     '// `--lbrem=1` (`M13`, вторая половина, П106 19.09.2026) — ТОРМОЗНОЕ ЭЛЕКТРОНА В\r\n'
     '// СЛОЯХ ОБВЯЗКИ ПО ХОДУ ПЕРЕНОСА (только под `eltr=1`): кванты тонкой мишени\r\n'
     '// вещества текущего слоя на шагах переноса, направление по электрону (Цай);\r\n'
     '// толстые мишени в точке рождения (`OutsideBremsstrahlung`) и в точке выхода\r\n'
     '// (`LayerBremsstrahlung`) у ведомых электронов не разыгрываются. Входит в\r\n'
     '// клеймо (`lbrem=1`), хвост `LBRM`. ВЫКЛ умолчанием (склад физики 20 не\r\n'
     '// тронут); ВКЛ = физика 21 — решение Amber по числам П106 (арбитр: тормозное\r\n'
     '// электронов обвязки у нас вдвое больше Geant4 в 0–50 кэВ RC103 при 2614).\r\n'
     '//\r\n'
     '// `--elmix=1` (`M13`, П100 18.09.2026) — СМЕШАННАЯ СХЕМА УПРУГОГО РАССЕЯНИЯ В\r\n'),
    ('                options.ElectronLayerMixedScattering = Flag(a, 8);\r\n            else if (a.StartsWith("--bpath=", StringComparison.Ordinal))\r\n',
     '                options.ElectronLayerMixedScattering = Flag(a, 8);\r\n'
     '            else if (a.StartsWith("--lbrem=", StringComparison.Ordinal))\r\n'
     '                // ⛔ `M13`, П106: тормозное электрона в слоях обвязки по ходу\r\n'
     '                // переноса; входит в клеймо (`lbrem=1`). ВЫКЛ умолчанием;\r\n'
     '                // `--lbrem=1` — матрица честно другая по клейму.\r\n'
     '                options.ElectronLayerBremAlongPath = Flag(a, 8);\r\n'
     '            else if (a.StartsWith("--bpath=", StringComparison.Ordinal))\r\n'),
])

patch(r'D:\BqMoni_Claude\p106\wt\tools\effmaker\probes\G4RawProbe.cs', [
    ('    ///                [--elmix=0|1] [--ret-kill=own,carry,brem,ret,same,other,outbrem]\r\n',
     '    ///                [--elmix=0|1] [--lbrem=0|1] [--ret-kill=own,carry,brem,ret,same,other,outbrem]\r\n'
     '    ///\r\n'
     '    /// `--lbrem=1` (`M13`, вторая половина, П106 19.09.2026): ключ\r\n'
     '    /// `ElectronLayerBremAlongPath` — тормозное электрона в слоях обвязки ПО ХОДУ\r\n'
     '    /// переноса (тонкая мишень вещества текущего слоя на шагах, направление по\r\n'
     '    /// электрону) вместо толстой мишени в точке рождения / выхода. Умолчание —\r\n'
     '    /// склада (ВЫКЛ; решение о ВКЛ = физике 21 — Amber). Мерка: RC103 П55 2614\r\n'
     '    /// база «ни возврата своего, ни заноса» и полное `def` против `g4cf`\r\n'
     '    /// (`killescown killcarry`, `killoutbrem`, def) по полосам 0–50/0–100 кэВ и\r\n'
     '    /// четвертям; 1461/662/59.5 и диск AS80 не хуже П103. Печатает счётчик\r\n'
     '    /// квантов тормозного по ходу в слоях.\r\n'),
    ('            bool elmix = store.ElectronLayerMixedScattering;   // `M13`, П100 — умолчание склада (ВКЛ с физики 20, П103)\r\n',
     '            bool elmix = store.ElectronLayerMixedScattering;   // `M13`, П100 — умолчание склада (ВКЛ с физики 20, П103)\r\n'
     '            bool lbrem = store.ElectronLayerBremAlongPath;     // `M13`, П106 — умолчание склада (ВЫКЛ)\r\n'),
    ('                // `M13` (П106): рычаги замера состава возврата — список через запятую.\r\n',
     '                // `M13` (П106): тормозное электрона в слоях обвязки по ходу переноса.\r\n'
     '                if (a.StartsWith("--lbrem=", StringComparison.Ordinal))\r\n'
     '                {\r\n'
     '                    lbrem = Flag01(a, 8);\r\n'
     '                    continue;\r\n'
     '                }\r\n'
     '                // `M13` (П106): рычаги замера состава возврата — список через запятую.\r\n'),
    ('            simulator.ElectronLayerMixedScattering = elmix;   // `M13`, П100\r\n',
     '            simulator.ElectronLayerMixedScattering = elmix;   // `M13`, П100\r\n'
     '            simulator.ElectronLayerBremAlongPath = lbrem;     // `M13`, П106\r\n'),
    ('            Console.WriteLine("рычаги замера состава возврата (`M13` П106, --ret-kill=): {0}",\r\n',
     '            Console.WriteLine("тормозное электрона в слоях обвязки по ходу переноса (`M13` П106, --lbrem=): {0}{1}",\r\n'
     '                              lbrem ? (eltr ? "ВКЛ (тонкая мишень вещества слоя на шагах, по электрону; толстых мишеней рождения/выхода нет)" : "ВКЛ, но без переноса в слоях (--eltr=0) бездействует")\r\n'
     '                                    : "выкл (толстая мишень в точке рождения / выхода, изотропно)",\r\n'
     '                              lbrem == store.ElectronLayerBremAlongPath ? " (умолчание склада)" : " (ключом)");\r\n'
     '            Console.WriteLine("рычаги замера состава возврата (`M13` П106, --ret-kill=): {0}",\r\n'),
    ('            // (`M13`, П100) Счётчики смешанной схемы: без ключа нули.\r\n',
     '            // (`M13`, П106) Тормозное по ходу переноса в слоях: без ключа нули.\r\n'
     '            Console.WriteLine("тормозное по ходу в слоях (`M13` П106): квантов {0}, энергия {1} кэВ ({2} на историю)",\r\n'
     '                              simulator.CountLayerBremPhotons,\r\n'
     '                              simulator.SumLayerBremKev.ToString("0.0", CultureInfo.InvariantCulture),\r\n'
     '                              ((double)simulator.CountLayerBremPhotons / histories).ToString("0.000E+00", CultureInfo.InvariantCulture));\r\n'
     '            // (`M13`, П100) Счётчики смешанной схемы: без ключа нули.\r\n'),
])
