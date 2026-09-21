# -*- coding: utf-8 -*-
r"""П114 — починка `store\response\` (heredoc: `\r` -> CR -> LF при чтении питоном)."""
import io
import sys
sys.stdout.reconfigure(encoding='utf-8')
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\handover-2026-09-19-p114-physics22-rev32.md'
b = open(p, 'rb').read()
bad = '`store\nesponse\\`'.encode('utf-8')
good = '`store\\response\\`'.encode('utf-8')
print('найдено', b.count(bad))
b = b.replace(bad, good)
open(p, 'wb').write(b)
t = b.decode('utf-8')
print('store\\response\\ теперь', t.count('store\\response\\'))
# ещё раз: все ожидаемые пути
for s in ('D:\\BqMoni_Claude\\p114\\store_backup\\', 'D:\\BqMoni_Claude\\p114\\store\\', '<worktree>\\tools\\CORPUS', 'probes\\build', 'D:\\BqMoni_Claude\\p114\\store_run.cmd', 'bin\\p114', 'obj\\p114', 'bin\\Debug_Codex'):
    print('%-45s %d' % (s, t.count(s)))
