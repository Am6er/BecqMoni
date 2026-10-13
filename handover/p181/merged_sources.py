# -*- coding: utf-8 -*-
r"""П181 (30.09.2026): ОЖИДАЕМЫЙ отпечаток набора разбора `sources=` после слияния p180-physics25 в master d4f2b8a7.
Контроль: отпечаток рабочего дерева worktree обязан совпасть с клеймом прогона (.run.json).
Слияние: всё — из ветки, кроме файлов, менявшихся ТОЛЬКО в master после базы ветки cac34379 (csproj, AssemblyInfo).
Только чтение.  python merged_sources.py
"""
import json
import subprocess
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

WT = r'D:\BqMoni_Claude\p180\wt'
sys.path.insert(0, WT + r'\tools')
import check_declared_base as cdb  # noqa: E402

run = json.load(open(r'D:\BqMoni_Claude\p181\art\run_out_rev36_full.json', encoding='utf-8'))
tree = cdb.sources_tree()
fp_tree = cdb.fold(tree)
print(u'worktree: файлов %d, sources=%s; клеймо прогона %s; совпало: %s'
      % (len(tree), fp_tree, run['sources'], fp_tree == run['sources']))


def git(*a):
    return subprocess.run(['git', '-C', WT] + list(a), capture_output=True).stdout


base = git('merge-base', 'master', 'p180-physics25').decode().strip()
m_changed = set(git('diff', '--name-only', base, 'master').decode().split())
b_changed = set(git('diff', '--name-only', base, 'p180-physics25').decode().split())
both = sorted((m_changed & b_changed) & set(tree))
print(u'база слияния %s; в наборе менялись в master: %s; в обеих ветках: %s'
      % (base[:8], sorted(m_changed & set(tree)), both))
merged = dict(tree)
# в обеих ветках: правка master = вишенке ветки (для EfficiencySimulator.cs проверено: diff cac34379..master == diff cac34379..84c50660),
# слияние берёт версию ветки; из master — только файлы, которых ветка не трогала
for rel in sorted((m_changed - b_changed) & set(tree)):
    blob = git('show', 'master:' + rel)
    merged[rel] = cdb.sha_norm(blob)
print(u'ожидаемый после слияния: sources=%s' % cdb.fold(merged))
print(u'из master взяты: %s' % sorted((m_changed - b_changed) & set(tree)))
sys.exit(0 if fp_tree == run['sources'] else 1)

