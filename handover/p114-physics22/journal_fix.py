# -*- coding: utf-8 -*-
r"""П114 — починка двух байтов порчи от heredoc (\t вместо `\tools`, \b вместо `\build`) и вставка §6."""
import io
import sys
sys.stdout.reconfigure(encoding='utf-8')
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\handover-2026-09-19-p114-physics22-rev32.md'
t = io.open(p, encoding='utf-8').read()
n1 = t.count(u'<worktree>\tools\\CORPUS')
t = t.replace(u'<worktree>\tools\\CORPUS', u'<worktree>\\tools\\CORPUS')
n2 = t.count(u'probes\build')
t = t.replace(u'probes\build', u'probes\\build')
print('починено: \\t', n1, ', \\b', n2)
assert '\t' not in t and '\b' not in t
# все ли другие обратные слэши на месте: ожидаемые пути
for s in (u'D:\\BqMoni_Claude\\p114\\store_backup\\', u'D:\\BqMoni_Claude\\p114\\store\\', u'store\\response\\', u'bin\\p114', u'handover\\'):
    print(s, t.count(s))
io.open(p, 'w', encoding='utf-8', newline='').write(t)
