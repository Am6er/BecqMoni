# -*- coding: utf-8 -*-
import io

REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'


def load(p):
    raw = open(p, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    return raw.decode('utf-8-sig'), bom


def save(p, text, bom):
    out = text.encode('utf-8')
    if bom:
        out = b'\xef\xbb\xbf' + out
    open(p, 'wb').write(out)


def idx(lines, sub, start=0):
    for i in range(start, len(lines)):
        if sub in lines[i]:
            return i
    raise SystemExit('not found: ' + sub)


# ---------------- ChainProbe: снять кусок про конструктор кривой ----------------
p = REPO + r'\tools\effmaker\probes\ChainProbe.cs'
t, bom = load(p)
lines = t.split('\n')
a = idx(lines, '            // Конструктор кривой: имя нуклида в строке цепочки берётся у того')
b = idx(lines, '            Console.WriteLine("  конструктор кривой: цепочек {0}, линий {1}", chains.Count, checkedLines);')
# после этой строки идёт печать "нуклидов в конфиге" и return bad — их оставляем
print('ChainProbe cut', a + 1, '..', b + 1)
note = [
    '            // Второй потребитель разбора — конструктор кривой (`EfficiencyLibrary',
    '            // .BuildChains`, цепочки из наборов) — снят 13.09.2026 вместе с фитом',
    '            // по спектрам (`AMBER25`, решение Amber); остался один читатель.',
    '            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();',
]
lines = lines[:a] + note + lines[b + 1:]
# заголовок: пункт 3
i = idx(lines, '    /// 3. СОГЛАСИЕ ПОТРЕБИТЕЛЕЙ. Оба места, разбиравшие подпись сами, теперь')
old3 = lines[i:i + 4]
print('\n'.join(old3))
new3 = [
    '    /// 3. СОГЛАСИЕ ПОТРЕБИТЕЛЕЙ. Библиотека образов зовёт общий разбор —',
    '    ///    сверяется, что она даёт ровно его ответ. До 13.09.2026 таких мест',
    '    ///    было два (второе — конструктор кривой, `EfficiencyLibrary`); фит по',
    '    ///    спектрам снят (`AMBER25`), и второго потребителя больше нет.',
]
lines = lines[:i] + new3 + lines[i + 4:]
save(p, '\n'.join(lines), bom)

# ---------------- XrayLinesProbe: снять раздел 5 ----------------
p = REPO + r'\tools\effmaker\probes\XrayLinesProbe.cs'
t, bom = load(p)
lines = t.split('\n')
i = idx(lines, '            ElementXrayStaysOutOfEfficiencyCurve();')
del lines[i]
a = idx(lines, '        static void ElementXrayStaysOutOfEfficiencyCurve()')
# до следующего разделителя "// ----"
b = a
while not lines[b].strip().startswith('// ----'):
    b += 1
print('XrayLinesProbe cut', a + 1, '..', b)
note = [
    '        // Раздел 5 — «в кривую эффективности не идёт» (`EfficiencyLibrary',
    '        // .BuildChains` отбрасывал рентген элемента) — снят 13.09.2026 вместе с',
    '        // фитом по спектрам (`AMBER25`, решение Amber): потребителя нет.',
    '',
]
lines = lines[:a] + note + lines[b:]
save(p, '\n'.join(lines), bom)
print('ok')
