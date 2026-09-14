# -*- coding: utf-8 -*-
"""П38: узлы <Efficiency> живых спектров после CorpusEffProbe против узлов worktree П37 (те же 45 сцен,
та же физика 17, тот же рецепт 200 000 историй) — дословно, по тексту узла БЕЗ <LastUpdated> (отметка времени
записи); плюс клеймо кривой <ComputeStamp>.

    python handover/p38-rev21/nodes_cmp.py
"""
import glob, io, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
LIVE = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'spectra')
WT = r'C:\Users\moroz\bqp37\tools\CORPUS\corpus\spectra'
# Узел кончается ПОСЛЕДНИМ </Efficiency> строки узла: внутри него каждая точка кривой записана тем же тегом
# (<Efficiency>0.006…</Efficiency>, 37 раз) — нежадный поиск обрывал бы узел на первой точке (ловушка T30).
NODE = re.compile(r'<Efficiency>\s*<Guid>.*</Efficiency>')
STAMP = re.compile(r'<ComputeStamp>(phys=\d+;[^<]*)</ComputeStamp>')
UPDATED = re.compile(r'<LastUpdated>[^<]*</LastUpdated>')
want = 'phys=17; hist=200000;'
tail = 'kdip=1; lys=2; etr=1; e+tr=1; e+off=1; rayl2=1'
same = diff = only_live = only_wt = 0
stamped = 0
bad = []
for path in sorted(glob.glob(os.path.join(LIVE, '*.xml'))):
    name = os.path.basename(path)
    a = io.open(path, encoding='utf-8-sig', newline='').read()
    ma = NODE.search(a)
    wp = os.path.join(WT, name)
    mb = NODE.search(io.open(wp, encoding='utf-8-sig', newline='').read()) if os.path.exists(wp) else None
    if ma is None and mb is None:
        continue
    if ma is None:
        only_wt += 1; bad.append('%s: узел только в worktree' % name); continue
    if mb is None:
        only_live += 1; bad.append('%s: узел только в живом' % name); continue
    st = STAMP.search(ma.group(0))
    s = st.group(1) if st else ''
    if s.startswith(want) and s.rstrip().endswith(tail):
        stamped += 1
    else:
        bad.append('%s: клеймо кривой «%s»' % (name, s[:90]))
    if UPDATED.sub('', ma.group(0)) == UPDATED.sub('', mb.group(0)):
        same += 1
    else:
        diff += 1; bad.append('%s: узел РАЗОШЁЛСЯ с worktree' % name)
print('узлов сравнено: %d; дословно равны worktree: %d; разошлись: %d; только в живом: %d; только в worktree: %d'
      % (same + diff, same, diff, only_live, only_wt))
print('клеймо «%s …; %s»: %d' % (want, tail, stamped))
for b in bad:
    print('  ' + b)
sys.exit(0 if not bad else 1)
