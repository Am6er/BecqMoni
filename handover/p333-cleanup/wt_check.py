# -*- coding: utf-8 -*-
"""Доехало ли незакоммиченное содержимое worktree до истории master (побайтно, по хешу блоба)."""
import subprocess, sys, os
REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
def git(*a, cwd=REPO):
    return subprocess.run(['git', '-c', 'core.quotepath=false'] + list(a), cwd=cwd, capture_output=True).stdout.decode('utf-8', 'replace')
for w in sys.argv[1:]:
    st = git('status', '--porcelain=v1', '-z', '--untracked-files=all', cwd=w).split('\0')
    inhist = notin = untracked = 0
    miss = []
    for e in st:
        if not e:
            continue
        code, p = e[:2], e[3:]
        fp = os.path.join(w, p)
        if not os.path.isfile(fp):
            continue
        h = git('hash-object', '--', p, cwd=w).strip()
        raw = git('log', 'master', '--all', '--format=', '--raw', '--no-abbrev', '--', p)
        blobs = {ln.split()[3] for ln in raw.splitlines() if ln.startswith(':') and len(ln.split()) > 4}
        if code == '??':
            untracked += 1
        if h in blobs:
            inhist += 1
        else:
            notin += 1
            miss.append(code + ' ' + p)
    print('%s: в истории %d, НЕТ в истории %d (из них новых %s)' % (w, inhist, notin, untracked))
    for m in miss[:25]:
        print('    ', m)
