# -*- coding: utf-8 -*-
"""Варианты уборки D:\\BqMoni_Claude: сборки vs каталоги полос целиком. Только счёт, ничего не трогает.
argv: cutoff(YYYY-MM-DD) out.json"""
import os, re, sys, json, io, datetime
D = r'D:\BqMoni_Claude'
CUT = datetime.datetime.strptime(sys.argv[1], '%Y-%m-%d').timestamp()
WT = {r'p122\wt', r'p164\wt_m', r'p173\wt_b', r'p174\wt2'}
KEEP = [r'p233\store', r'p235\store_backup_rev44', r'p205\gaussfit_fractional.py', r'p317\estar_tie_v2.patch']
BUILD_EXT = {'.exe', '.dll', '.pdb', '.winmd', '.nupkg', '.so', '.dylib', '.a', '.lib', '.resources', '.cache', '.ilk', '.exp', '.obj'}
BUILD_DIR = re.compile(r'^(app\w*|bin|obj|build\w*|wd\w*|release\w*|debug\w*|runtimes|packages|x64|x86)$', re.I)
lanes = []
for name in sorted(os.listdir(D)):
    p = os.path.join(D, name)
    if not os.path.isdir(p):
        continue
    tot = bld = n = nb = 0
    newest = 0
    for dp, dn, fn in os.walk(p):
        rel = os.path.relpath(dp, D)
        in_wt = any(rel == w or rel.startswith(w + os.sep) for w in WT)
        comps = rel.split(os.sep)[1:]
        in_build_dir = any(BUILD_DIR.match(c) for c in comps)
        for f in fn:
            fp = os.path.join(dp, f)
            try:
                st = os.stat(fp)
            except OSError:
                continue
            tot += st.st_size; n += 1
            newest = max(newest, st.st_mtime)
            if in_wt or in_build_dir or os.path.splitext(f)[1].lower() in BUILD_EXT:
                bld += st.st_size; nb += 1
    keep = [k for k in KEEP if k.split('\\')[0] == name]
    lanes.append({'lane': name, 'bytes': tot, 'files': n, 'build_bytes': bld, 'build_files': nb,
                  'newest': datetime.datetime.fromtimestamp(newest).strftime('%Y-%m-%d %H:%M') if newest else '',
                  'fresh': newest >= CUT, 'keep': keep})
json.dump(lanes, io.open(sys.argv[2], 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
GB = 2**30
old = [l for l in lanes if not l['fresh']]
fresh = [l for l in lanes if l['fresh']]
print('полос %d, %.2f ГБ; свежих (>= %s) %d, %.2f ГБ' % (len(lanes), sum(l['bytes'] for l in lanes) / GB, sys.argv[1], len(fresh), sum(l['bytes'] for l in fresh) / GB))
print('вариант «сборки»: у старых полос %d файлов, %.2f ГБ (из них worktree %s)' % (sum(l['build_files'] for l in old), sum(l['build_bytes'] for l in old) / GB, len(WT)))
print('вариант «каталог целиком»: %d каталогов, %d файлов, %.2f ГБ; оставить %s' % (len(old), sum(l['files'] for l in old), sum(l['bytes'] for l in old) / GB, [k for l in old for k in l['keep']]))
print('у свежих полос сборки: %.2f ГБ' % (sum(l['build_bytes'] for l in fresh) / GB))
for l in sorted(fresh, key=lambda x: x['lane']):
    print('   свежая %-8s %s  %.1f МБ (сборки %.1f)' % (l['lane'], l['newest'], l['bytes'] / 2**20, l['build_bytes'] / 2**20))
