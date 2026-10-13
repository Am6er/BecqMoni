# -*- coding: utf-8 -*-
r"""П114 (19.09.2026) — читатели числа 21 / ключа lbang: EfficiencySimulator.cs, EfficiencyCalculation.cs,
BoundProbeF59.cs, CorpusMatrixProbe.cs, G4RawProbe.cs, LayerReturnProbe.cs, check_matrix_keys.py,
check_corpus_scenes.py. Байты: перевод строки файла сохраняется; каждая замена — ровно один раз."""
import sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
WT = r'D:\BqMoni_Claude\p114\wt'


def edit(rel, pairs):
    p = WT + '\\' + rel
    raw = open(p, 'rb').read()
    crlf = raw.count(b'\r\n'); lf = raw.count(b'\n') - crlf
    nl = '\r\n' if crlf >= lf else '\n'
    txt = raw.decode('utf-8')
    for old, new in pairs:
        old = old.replace('\n', nl); new = new.replace('\n', nl)
        n = txt.count(old)
        if n != 1:
            print(u'⛔ %s: ожидалось 1, найдено %d: %r' % (rel, n, old[:90]))
            sys.exit(1)
        txt = txt.replace(old, new)
    out = txt.encode('utf-8')
    open(p, 'wb').write(out)
    crlf2 = out.count(b'\r\n'); lf2 = out.count(b'\n') - crlf2
    print(u'%s: CRLF %d→%d, LF %d→%d, замен %d' % (rel, crlf, crlf2, lf, lf2, len(pairs)))


# 1. EfficiencySimulator.cs — описание поля
edit(r'BecquerelMonitor\EfficiencyMaker\EfficiencySimulator.cs', [
    (u"""        /// (`M13`, остаток; П111 19.09.2026) НАПРАВЛЕНИЕ КВАНТА ТОРМОЗНОГО В
        /// СЛОЯХ ОБВЯЗКИ — 2BS Коха—Моца (как `G4Generator2BS` у арбитра
        /// option4) вместо модифицированного Цая — ключ сделан ВЫКЛ. Умолчание
        /// ПОЛЯ — умолчание СКЛАДА""",
     u"""        /// (`M13`, остаток; П111 19.09.2026) НАПРАВЛЕНИЕ КВАНТА ТОРМОЗНОГО В
        /// СЛОЯХ ОБВЯЗКИ — 2BS Коха—Моца (как `G4Generator2BS` у арбитра
        /// option4) вместо модифицированного Цая — ключ сделан ВЫКЛ; ВКЛ
        /// умолчанием с ночи 19→20.09.2026 — физика 22 (П114, решение Amber «ВКЛ
        /// единым счётом ночью»). Умолчание ПОЛЯ — умолчание СКЛАДА"""),
])

# 2. EfficiencyCalculation.cs — комментарий клейма кривой
edit(r'BecquerelMonitor\EfficiencyMaker\EfficiencyCalculation.cs', [
    (u"""            // `; lbang=1` (`M13`, П111 19.09.2026) — направление тормозного в слоях
            // 2BS, только включённым (ВЫКЛ умолчанием склада).""",
     u"""            // `; lbang=1` (`M13`, П111 19.09.2026) — направление тормозного в слоях
            // 2BS, тем же именем, что у клейма матрицы, только включённым; с
            // физики 22 (П114, ночь 19→20.09.2026) ключ ВКЛ умолчанием склада, и
            // у кривой он в клейме всегда."""),
])

# 3. BoundProbeF59.cs — версия 22, печать и проверка lbang
edit(r'tools\effmaker\probes\BoundProbeF59.cs', [
    (u"""            Say("-- A120: умолчания физики 17, 18, 19, 20 и 21 (склад = кривая = симулятор) --");""",
     u"""            Say("-- A120: умолчания физики 17, 18, 19, 20, 21 и 22 (склад = кривая = симулятор) --");"""),
    (u"""            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronLayerBremAlongPath = {0}", options.ElectronLayerBremAlongPath));
            Ok(ResponseMatrix.PhysicsVersion == 21,
               string.Format(CultureInfo.InvariantCulture, "версия физики склада — 21 (есть {0})", ResponseMatrix.PhysicsVersion));""",
     u"""            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronLayerBremAlongPath = {0}", options.ElectronLayerBremAlongPath));
            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronLayerBremAngular2BS = {0}", options.ElectronLayerBremAngular2BS));
            Ok(ResponseMatrix.PhysicsVersion == 22,
               string.Format(CultureInfo.InvariantCulture, "версия физики склада — 22 (есть {0})", ResponseMatrix.PhysicsVersion));"""),
    (u"""            Ok(options.ElectronLayerBremAlongPath,
               "ключ физики 21 умолчанием ВКЛ: lbrem=1");
        }""",
     u"""            Ok(options.ElectronLayerBremAlongPath,
               "ключ физики 21 умолчанием ВКЛ: lbrem=1");
            Ok(options.ElectronLayerBremAngular2BS,
               "ключ физики 22 умолчанием ВКЛ: lbang=1");
        }"""),
])
