# -*- coding: utf-8 -*-
import os
REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'


def rw(rel, pairs):
    p = os.path.join(REPO, rel)
    raw = open(p, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    t = raw.decode('utf-8-sig')
    nl = '\r\n' if '\r\n' in t else '\n'
    for old, new in pairs:
        old = old.replace('\n', nl)
        new = new.replace('\n', nl)
        assert t.count(old) == 1, (p, old[:50], t.count(old))
        t = t.replace(old, new)
    out = t.encode('utf-8')
    if bom:
        out = b'\xef\xbb\xbf' + out
    open(p, 'wb').write(out)
    print('ok', rel)


rw(r'BecquerelMonitor\DoseRate.cs', [(
'''                // `EfficiencySimulator.Efficiency` (зарегистрировано/испущено),
                // а `EfficiencyFitter` опирается на ту же границу ЧЕТЫРЕЖДЫ:
                // просеивает опорную кривую (`A222`), отвергает наблюдения с
                // ε > 1, режет выходную кривую по единице и отказывает на упоре
                // в потолок.
''',
'''                // `EfficiencySimulator.Efficiency` (зарегистрировано/испущено),
                // а снятый 13.09.2026 фит по спектрам (`AMBER25`) опирался на
                // ту же границу четырежды: просеивал опорную кривую (`A222`),
                // отвергал наблюдения с ε > 1, резал выходную кривую по единице
                // и отказывал на упоре в потолок.
''')])
rw(r'BecquerelMonitor\PolynomialEnergyCalibration.cs', [(
'''        /// зовёт проверку ради него и ответ отбрасывает
        /// (<c>EfficiencyFitter</c>, пробы), и на годном входе поведение
        /// обязано остаться прежним до бита.''',
'''        /// зовёт проверку ради него и ответ отбрасывает
        /// (<c>EfficiencyCurveIo.LoadResultData</c>, пробы), и на годном входе
        /// поведение обязано остаться прежним до бита.''')])
rw(r'tools\effmaker\probes\SetColorProbe.cs', [(
'''    /// Конфиг читается из ТЕКУЩЕГО каталога (`config\\NuclideDefinition.xml`),
    /// как и у SetProbe: запускать из копии, чужой конфиг пробой не трогать.''',
'''    /// Конфиг читается из ТЕКУЩЕГО каталога (`config\\NuclideDefinition.xml`):
    /// запускать из копии, чужой конфиг пробой не трогать.''')])
