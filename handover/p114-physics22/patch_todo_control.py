# -*- coding: utf-8 -*-
r"""П114 — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ текста (б): в КОНТРОЛЬНОМ worktree D:\BqMoni_Claude\p114\regcheck заменить в TODO.md
абзац шапки базы (строка ~119) и таблицу под ним текстом §8.2 журнала, затем check_registry.py --root <regcheck>.
Главное дерево НЕ трогается (TODO.md правит распорядитель)."""
import io
import re
import sys
sys.stdout.reconfigure(encoding='utf-8')
WT = r'D:\BqMoni_Claude\p114\regcheck'
J = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\handover-2026-09-19-p114-physics22-rev32.md'
j = io.open(J, encoding='utf-8', newline='').read()
i = j.index(u'### 8.2 (б)')
k = j.index(u'### 8.3 (в)')
sec = j[i:k]
# текст (б) — от «⛔ **ДЕЙСТВУЮЩАЯ БАЗА КОРПУСА» до конца таблицы
a = sec.index(u'⛔ **ДЕЙСТВУЮЩАЯ БАЗА КОРПУСА')
body = sec[a:].rstrip('\n')
lines = body.split('\n')
assert lines[0].startswith(u'⛔ **ДЕЙСТВУЮЩАЯ БАЗА КОРПУСА — 21.09.2026 (день)')
p = WT + r'\TODO.md'
raw = open(p, 'rb').read()
crlf = raw.count(b'\r\n')
nl = '\r\n' if crlf else '\n'
t = raw.decode('utf-8')
tl = t.split(nl)
idx = [n for n, l in enumerate(tl) if l.startswith(u'⛔ **ДЕЙСТВУЮЩАЯ БАЗА КОРПУСА')]
assert len(idx) == 1, idx
s = idx[0]
# абзац + пустая строка + таблица (строки, начинающиеся с |) до первой не-табличной
e = s + 1
while e < len(tl) and (tl[e].strip() == '' or tl[e].startswith('|')):
    e += 1
print('заменяю строки TODO.md %d..%d (%d строк) на %d строк текста (б)' % (s + 1, e, e - s, len(lines)))
print('старое начало:', tl[s][:90])
print('старый конец :', tl[e - 1][:90])
tl[s:e] = lines
out = nl.join(tl)
open(p, 'wb').write(out.encode('utf-8'))
print('готово')
