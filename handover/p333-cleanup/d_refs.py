# -*- coding: utf-8 -*-
"""Кто ссылается на D:\\BqMoni_Claude\\<каталог>: открытые строки TODO, CLAUDE.md, память,
навыки, код/оснастка вне handover, журналы handover (с классом журнала)."""
import os, re, subprocess, json, io, sys
ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
MEM = r'C:\Users\moroz\.claude\projects\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\memory'
D = r'D:\BqMoni_Claude'
cls = {}
for r in json.load(io.open(sys.argv[1], encoding='utf-8')):
    cls[r['name']] = r['class']

def read(p):
    b = open(p, 'rb').read()
    try:
        return b.decode('utf-8')
    except UnicodeDecodeError:
        return b.decode('cp1251', 'replace')

RE = re.compile(r'd:[\\/]+bqmoni_claude[\\/]+([\w.\-]+)([\\/][^\s`\'"|)\]]*)?', re.IGNORECASE)
lanes = {n.lower(): n for n in os.listdir(D) if os.path.isdir(os.path.join(D, n))}
out = {}
def scan(kind, lab, txt):
    for m in RE.finditer(txt):
        t = m.group(1).lower()
        if t in lanes:
            out.setdefault(lanes[t], []).append((kind, lab, (m.group(0))[:160]))

todo = read(os.path.join(ROOT, 'TODO.md')).splitlines()
for i, line in enumerate(todo, 1):
    s = line.lstrip()
    kind = 'open'
    if s.startswith('|'):
        cells = s.split('|')
        if len(cells) > 1 and '~~' in cells[1]:
            kind = 'struck'
    scan(kind, 'TODO.md:%d' % i, line)
scan('done', 'DONE.md', read(os.path.join(ROOT, 'DONE.md')))
scan('live', 'CLAUDE.md', read(os.path.join(ROOT, 'CLAUDE.md')))
for n in os.listdir(MEM):
    if n.endswith('.md'):
        scan('live', 'memory/' + n, read(os.path.join(MEM, n)))
tracked = subprocess.run(['git', '-C', ROOT, 'ls-files', '-z'], capture_output=True).stdout.decode('utf-8').split('\0')
for t in tracked:
    if not t or t.lower() in ('todo.md', 'done.md', 'claude.md'):
        continue
    ext = os.path.splitext(t.lower())[1]
    if ext not in ('.md', '.py', '.ps1', '.cs', '.cmd', '.bat', '.txt', '.json', '.csv'):
        continue
    p = os.path.join(ROOT, t)
    if not os.path.isfile(p) or os.path.getsize(p) > 3_000_000:
        continue
    if t.startswith('handover/'):
        top = t.split('/')[1]
        c = cls.get(top, '?')
        kind = 'journal-' + c
    else:
        kind = 'live'
    scan(kind, t, read(p))
res = []
for lane, hits in sorted(out.items()):
    kinds = sorted({k for k, _, _ in hits})
    res.append({'lane': lane, 'kinds': kinds, 'hits': [list(h) for h in hits[:20]], 'n': len(hits)})
json.dump(res, io.open(sys.argv[2], 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
for r in res:
    live = [h for h in r['hits'] if h[0] in ('open', 'live') or h[0] in ('journal-live', 'journal-via-live')]
    print('%-14s n=%-3d %s' % (r['lane'], r['n'], ','.join(r['kinds'])))
