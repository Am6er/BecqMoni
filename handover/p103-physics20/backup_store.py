# -*- coding: utf-8 -*-
r"""П103 (18.09.2026) — шаг Б: снимок живого склада ДО чего бы то ни было.
tools\CORPUS\corpus\geometries\ (46 .in, 46 .rmx, response\ 46, index.csv) ->
D:\BqMoni_Claude\p103\store_backup\ + sha256.txt (копия списка в артефакты).
Копия — copy2, каждая сверяется по sha256 после копирования. Ничего в живом складе не пишется.
"""
import hashlib
import io
import os
import shutil
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
LIVE = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'geometries')
BACKUP = r'D:\BqMoni_Claude\p103\store_backup'
ART = r'D:\BqMoni_Claude\p103\art'


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def main():
    if os.path.isdir(BACKUP) and os.listdir(BACKUP):
        print(u'⛔ снимок уже есть: %s — не перезаписываю' % BACKUP)
        return 1
    os.makedirs(os.path.join(BACKUP, 'response'), exist_ok=True)
    os.makedirs(ART, exist_ok=True)
    rows = []
    counts = {}
    total = 0
    for sub in ('', 'response'):
        src_dir = os.path.join(LIVE, sub)
        for f in sorted(os.listdir(src_dir)):
            p = os.path.join(src_dir, f)
            if not os.path.isfile(p):
                continue
            ext = os.path.splitext(f)[1].lower()
            if sub == '' and ext not in ('.in', '.rmx', '.csv'):
                continue
            if sub == 'response' and ext != '.rmx':
                continue
            dst = os.path.join(BACKUP, sub, f)
            shutil.copy2(p, dst)
            s1, s2 = sha(p), sha(dst)
            if s1 != s2:
                print(u'⛔ копия не совпала: %s' % f)
                return 1
            rel = (sub + '/' if sub else '') + f
            rows.append((s1, rel))
            counts[(sub or 'store', ext)] = counts.get((sub or 'store', ext), 0) + 1
            total += os.path.getsize(p)
    with io.open(os.path.join(BACKUP, 'sha256.txt'), 'w', encoding='utf-8', newline='') as fh:
        for s, rel in rows:
            fh.write(u'%s  %s\n' % (s, rel))
    shutil.copy2(os.path.join(BACKUP, 'sha256.txt'), os.path.join(ART, 'store_backup_sha256.txt'))
    for k in sorted(counts):
        print(u'%-10s %-5s %d' % (k[0], k[1], counts[k]))
    print(u'файлов %d, байт %d' % (len(rows), total))
    return 0


if __name__ == '__main__':
    sys.exit(main())
