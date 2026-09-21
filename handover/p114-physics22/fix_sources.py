# -*- coding: utf-8 -*-
r"""П114 — отпечаток sources= в объявлении README, тексте (б)/(в) журнала и §25 после правки комментариев
(календарь захода): 4e75c3c0d01e916c5fd1c152c3b5c33fa7ae2f6e01cf96a98e2ab4b7603ab0a3 -> 5b371450889b70b006dae7fd5a093794437941f033803af6ca36ad2c3649f1cb;
sha256 exe главного дерева 24cf7ed3 -> ae896b3c. Байты: переводы строк файлов сохраняются (двоичная замена)."""
import sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
PAIRS = [
    (b'4e75c3c0d01e916c5fd1c152c3b5c33fa7ae2f6e01cf96a98e2ab4b7603ab0a3', b'5b371450889b70b006dae7fd5a093794437941f033803af6ca36ad2c3649f1cb'),
    ('4e75c3c0d01e916c…'.encode('utf-8'), '5b371450889b70b0…'.encode('utf-8')),
    ('4e75c3c0…'.encode('utf-8'), '5b371450…'.encode('utf-8')),
    ('24cf7ed320377fbf…'.encode('utf-8'), 'ae896b3ccea1934b…'.encode('utf-8')),
    ('24cf7ed3…'.encode('utf-8'), 'ae896b3c…'.encode('utf-8')),
]
for rel in (r'tools\CORPUS\README.md', r'handover\handover-2026-09-19-p114-physics22-rev32.md', r'tools\effmaker\handover-response-matrix.md'):
    p = ROOT + '\\' + rel
    b = open(p, 'rb').read()
    n = 0
    for a, c in PAIRS:
        n += b.count(a)
        b = b.replace(a, c)
    open(p, 'wb').write(b)
    print('%-60s замен %d' % (rel, n))
# копия объявления в артефактах
p = r'D:\BqMoni_Claude\p114\scripts\readme_declare_rev32.md'
b = open(p, 'rb').read()
for a, c in PAIRS:
    b = b.replace(a, c)
open(p, 'wb').write(b)
