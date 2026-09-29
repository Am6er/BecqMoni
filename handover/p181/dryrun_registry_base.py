# -*- coding: utf-8 -*-
r"""П181 (копия П179/П176): сухая проверка трёх мест объявления rev36 ДО вставки текстов распорядителем.

Во временный корень D:\BqMoni_Claude\p181\regroot кладутся: TODO.md дерева с шапкой, заменённой текстом
todo_header_rev36.md (абзац строки 119 и таблица 121–126), tools/CORPUS/README.md дерева (уже переобъявлен),
и каталог памяти с corpus-base-current.md из memory_rev36.md (кусок 1). Затем зовётся
check_registry.check_corpus_base(root, out, memory=...). Дерево и настоящая память НЕ трогаются.
"""
import io
import os
import shutil
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

MAIN = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'   # TODO.md — основного дерева (только чтение)
ROOT = r'D:\BqMoni_Claude\p180\wt'                         # README и check_registry — worktree
ART = r'D:\BqMoni_Claude\p181'
TMP = r'D:\BqMoni_Claude\p181\regroot'
sys.path.insert(0, os.path.join(ROOT, 'tools'))
import check_registry  # noqa: E402

if os.path.exists(TMP):
    shutil.rmtree(TMP)
os.makedirs(os.path.join(TMP, 'tools', 'CORPUS'))
os.makedirs(os.path.join(TMP, 'mem'))

todo = io.open(os.path.join(MAIN, 'TODO.md'), encoding='utf-8', newline='').read()
nl = '\r\n' if '\r\n' in todo else '\n'
lines = todo.split(nl)
assert lines[118].startswith(u'⛔ **ДЕЙСТВУЮЩАЯ БАЗА КОРПУСА — 28.09.2026 (ночь)'), lines[118][:60]
assert lines[120].startswith(u'| база |') and lines[125].startswith(u'| **малая `out_rev35_mini`** | непонятная'), (lines[120][:30], lines[125][:40])
hdr = io.open(os.path.join(ART, 'todo_header_rev36.md'), encoding='utf-8').read()
block = hdr.split(u'8<---', 1)[1].strip('\n').split('\n')
lines2 = lines[:118] + block + lines[126:]
io.open(os.path.join(TMP, 'TODO.md'), 'w', encoding='utf-8', newline='').write(nl.join(lines2))
shutil.copy2(os.path.join(ROOT, 'tools', 'CORPUS', 'README.md'), os.path.join(TMP, 'tools', 'CORPUS', 'README.md'))

mem = io.open(os.path.join(ART, 'memory_rev36.md'), encoding='utf-8').read()
part1 = mem.split(u'===== (1)', 1)[1].split(u'===== (2)', 1)[0]
note = part1[part1.index(u'---\nname:'):].rstrip('\n') + '\n'
io.open(os.path.join(TMP, 'mem', 'corpus-base-current.md'), 'w', encoding='utf-8', newline='').write(note)

out = io.open(1, 'w', encoding='utf-8', closefd=False)
bad = check_registry.check_corpus_base(TMP, out, memory=os.path.join(TMP, 'mem'))
out.flush()
print(u'находок: %d' % bad)
sys.exit(0 if bad == 0 else 1)

