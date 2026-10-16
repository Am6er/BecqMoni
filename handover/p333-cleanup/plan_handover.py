# -*- coding: utf-8 -*-
"""Варианты чистки handover: считает, что уходит при каждом правиле.
argv: classes_by_filename.json classes_by_lane.json fresh_date(YYYY-MM-DD) out_plan.json"""
import json, io, sys, subprocess, re, os
from collections import Counter
ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
A = {(r['kind'], r['name']): r for r in json.load(io.open(sys.argv[1], encoding='utf-8'))}
B = {(r['kind'], r['name']): r for r in json.load(io.open(sys.argv[2], encoding='utf-8'))}
FRESH = sys.argv[3]
# первая дата появления каждого верхнего элемента handover в истории
log = subprocess.run(['git', '-C', ROOT, '-c', 'core.quotepath=false', 'log', '--reverse', '--format=@%ad', '--date=short',
                      '--name-only', '--diff-filter=A', '--', 'handover'], capture_output=True).stdout.decode('utf-8')
first = {}
d = None
for ln in log.splitlines():
    if ln.startswith('@'):
        d = ln[1:]
    elif ln.startswith('handover/'):
        top = ln.split('/')[1]
        first.setdefault(top, d)
# имена со своей датой
RE_D = re.compile(r'(20\d\d-\d\d-\d\d)')

def date_of(name):
    m = RE_D.search(name)
    if m:
        return m.group(1)
    return first.get(name, '9999-99-99')

def struck_ref(r):
    return any(x.startswith('struck:') for x in r['refs'])

def protected(key, r):
    if key[1] == 'registry-artefacts':
        return 'registry-artefacts'
    if date_of(key[1]) >= FRESH:
        return 'fresh'
    if struck_ref(r):
        return 'struck-awaiting'
    return None

plans = {}
for name, src, dele in [('A', A, {'closed', 'orphan', 'struck-only'}), ('B', B, {'closed', 'orphan', 'struck-only'})]:
    out = []
    prot = Counter()
    for key, r in src.items():
        if r['class'] in dele:
            p = protected(key, r)
            if p:
                prot[p] += 1
                continue
            out.append({'kind': key[0], 'name': key[1], 'class': r['class'], 'files': r['files'], 'bytes': r['bytes'], 'date': date_of(key[1])})
    plans[name] = out
    nf = sum(1 for o in out if o['kind'] == 'file')
    nd = sum(1 for o in out if o['kind'] == 'dir')
    files = sum(o['files'] for o in out)
    mb = sum(o['bytes'] for o in out) / 2**20
    print('%s: верхних файлов %d, каталогов %d, всего файлов %d, %.1f МБ; защищено %s' % (name, nf, nd, files, mb, dict(prot)))
json.dump(plans, io.open(sys.argv[4], 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
tot_top = len([k for k in A if k[0] == 'file']), len([k for k in A if k[0] == 'dir'])
print('было: верхних файлов %d, каталогов %d' % tot_top)
