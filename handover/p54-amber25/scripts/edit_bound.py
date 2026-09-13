# -*- coding: utf-8 -*-
import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\BoundProbeF59.cs'
t = io.open(p, encoding='utf-8', newline='').read()
lines = t.split('\n')


def idx(sub):
    for i, l in enumerate(lines):
        if sub in l:
            return i
    raise SystemExit('not found: ' + sub)


# 1. вызов
i = idx('                A222_FitterReference();')
del lines[i]
# 2. метод A222_FitterReference (с его summary) и Extrapolated — до разделителя A183
a = idx('        static void A222_FitterReference()')
while '/// <summary>' not in lines[a]:
    a -= 1
b = idx('        // A183 — знак, которого в 1251 нет')
while not lines[b].strip().startswith('// ====='):
    b -= 1
print('cut', a + 1, '..', b, repr(lines[b]))
note = [
    '        // A222.5 — «опорная кривая фиттера» — снята 13.09.2026 вместе с самим',
    '        // фитом по спектрам (`EfficiencyFitter`, `AMBER25`, решение Amber):',
    '        // мерить больше нечего.',
    '',
]
lines = lines[:a] + note + lines[b:]
io.open(p, 'w', encoding='utf-8', newline='').write('\n'.join(lines))
print('ok', len(lines))
