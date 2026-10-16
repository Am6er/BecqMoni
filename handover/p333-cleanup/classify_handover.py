# -*- coding: utf-8 -*-
"""Разбор handover/ по ссылкам: живые / только из журналов / закрытые / сироты.

Правило П82 (AMBER41, 15.09.2026): живой — на кого ссылается открытая строка TODO,
CLAUDE.md, память, навыки, код/оснастка/README вне handover; закрытый — только DONE.md,
вычеркнутые строки TODO и другие журналы; сирота — никто.
"""
import os, re, subprocess, sys, json, io

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
MEM = r'C:\Users\moroz\.claude\projects\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\memory'
OUT = sys.argv[1]

def read(p):
    with open(p, 'rb') as f:
        b = f.read()
    try:
        return b.decode('utf-8')
    except UnicodeDecodeError:
        return b.decode('cp1251', 'replace')

H = os.path.join(ROOT, 'handover')
entries_files = sorted(n for n in os.listdir(H) if os.path.isfile(os.path.join(H, n)))
entries_dirs = sorted(n for n in os.listdir(H) if os.path.isdir(os.path.join(H, n)))

# --- источники ---
sources = []   # (kind, label, text)
todo = read(os.path.join(ROOT, 'TODO.md')).splitlines()
for i, line in enumerate(todo, 1):
    s = line.lstrip()
    if s.startswith('|'):
        cells = s.split('|')
        first = cells[1] if len(cells) > 1 else ''
        kind = 'struck' if '~~' in first else 'open'
    else:
        kind = 'open'
    sources.append((kind, 'TODO.md:%d' % i, line))
sources.append(('done', 'DONE.md', read(os.path.join(ROOT, 'DONE.md'))))
sources.append(('live', 'CLAUDE.md', read(os.path.join(ROOT, 'CLAUDE.md'))))
for n in os.listdir(MEM):
    if n.endswith('.md'):
        sources.append(('live', 'memory/' + n, read(os.path.join(MEM, n))))

tracked = subprocess.run(['git', '-C', ROOT, 'ls-files', '-z'], capture_output=True).stdout.decode('utf-8').split('\0')
TEXT_EXT = {'.md', '.py', '.ps1', '.cs', '.cmd', '.bat', '.txt', '.json', '.csv', '.sql', '.xml', '.resx', '.csproj', '.yml', '.yaml', '.ini', '.cfg', '.toml', '.sh', '.html', '.js', '.in'}
for t in tracked:
    if not t:
        continue
    low = t.lower()
    if low in ('todo.md', 'done.md', 'claude.md'):
        continue
    ext = os.path.splitext(low)[1]
    if ext not in TEXT_EXT:
        continue
    p = os.path.join(ROOT, t)
    if not os.path.isfile(p) or os.path.getsize(p) > 3_000_000:
        continue
    if low.startswith('tools/corpus/corpus/spectra/'):
        continue
    if low.startswith('handover/'):
        parts = t.split('/')
        if len(parts) == 2:
            sources.append(('journal', t, read(p)))
        else:
            sources.append(('artefact', t, read(p)))
    else:
        sources.append(('live', t, read(p)))

# --- поиск ссылок: один проход по каждому источнику ---
RE_TOK = re.compile(r'[\w.\-]+')
RE_DSTRICT = re.compile(r'handover[\\/]+([\w.\-]+)')
RE_DREL = re.compile(r'(?<![\w.\\/-])([\w.\-]+)[\\/]')
fileset = {n.lower(): n for n in entries_files}
dirset = {n.lower(): n for n in entries_dirs}
refs = {('file', n): [] for n in entries_files}
refs.update({('dir', n): [] for n in entries_dirs})
LANE_MODE = os.environ.get('LANE', '1') == '1'
RE_LANE = re.compile(r'(?<![\w])п(\d{1,3})(?!\d)')
RE_FLANE = re.compile(r'-p(\d{1,3})[a-z]?-')
RE_DLANE = re.compile(r'^p(\d{1,3})[a-z]?(-|$)')
lane_map = {}
for n in entries_files:
    m = RE_FLANE.search(n.lower())
    if m:
        lane_map.setdefault(m.group(1), []).append(('file', n.lower()))
for n in entries_dirs:
    m = RE_DLANE.search(n.lower())
    if m:
        lane_map.setdefault(m.group(1), []).append(('dir', n.lower()))
for k, lab, txt in sources:
    seen = set()
    low = txt.lower()
    if LANE_MODE:
        for m in RE_LANE.finditer(low):
            for e in lane_map.get(m.group(1), ()):
                seen.add(e)
    for m in RE_TOK.finditer(low):
        t = m.group(0)
        # имя файла может стоять внутри длинного токена (handover/имя) — токен без пути
        for cand in (t, t.rsplit('/', 1)[-1]):
            if cand in fileset and ('file', cand) not in seen:
                seen.add(('file', cand))
    for m in RE_DSTRICT.finditer(low):
        t = m.group(1).rstrip('.')
        if t in dirset:
            seen.add(('dir', t))
        if t in fileset:
            seen.add(('file', t))
    if k == 'journal':
        for m in RE_DREL.finditer(low):
            t = m.group(1)
            if t in dirset:
                seen.add(('dir', t))
    for kindE, t in seen:
        name = fileset[t] if kindE == 'file' else dirset[t]
        if kindE == 'file' and lab == 'handover/' + name:
            continue
        if kindE == 'dir' and lab.startswith('handover/' + name + '/'):
            continue
        refs[(kindE, name)].append((k, lab))

journal_of = {}
def classify():
    cls = {}
    for key, hits in refs.items():
        kinds = {k for k, _ in hits}
        if kinds & {'open', 'live'}:
            cls[key] = 'live'
        elif not hits:
            cls[key] = 'orphan'
        else:
            cls[key] = 'closed?'
    # второй проход: только журналы/артефакты — живой ли кто-то из ссылающихся
    changed = True
    while changed:
        changed = False
        for key, hits in refs.items():
            if cls[key] != 'closed?':
                continue
            kinds = {k for k, _ in hits}
            if kinds & {'done', 'struck'}:
                continue
            # только журналы
            src_live = False
            for k, lab in hits:
                if k in ('journal', 'artefact'):
                    parts = lab.split('/')
                    sk = ('file', parts[1]) if len(parts) == 2 else ('dir', parts[1])
                    if cls.get(sk) in ('live', 'via-live'):
                        src_live = True
            if src_live:
                cls[key] = 'via-live'
                changed = True
    for key in cls:
        if cls[key] == 'closed?':
            kinds = {k for k, _ in refs[key]}
            if kinds == {'struck'} or kinds == {'struck', 'journal'} or kinds == {'struck', 'artefact'} or kinds == {'struck', 'journal', 'artefact'}:
                cls[key] = 'struck-only'
            else:
                cls[key] = 'closed'
    return cls

cls = classify()

def size_of(key):
    kindE, name = key
    p = os.path.join(H, name)
    if kindE == 'file':
        return os.path.getsize(p), 1
    s = n = 0
    for dp, dn, fn in os.walk(p):
        for f in fn:
            s += os.path.getsize(os.path.join(dp, f)); n += 1
    return s, n

rows = []
for key, c in cls.items():
    s, n = size_of(key)
    rows.append({'kind': key[0], 'name': key[1], 'class': c, 'bytes': s, 'files': n,
                 'refs': ['%s:%s' % (k, lab) for k, lab in refs[key]][:12]})
with io.open(OUT, 'w', encoding='utf-8') as f:
    json.dump(rows, f, ensure_ascii=False, indent=1)

from collections import Counter
for kindE in ('file', 'dir'):
    cnt = Counter(r['class'] for r in rows if r['kind'] == kindE)
    mb = Counter()
    nf = Counter()
    for r in rows:
        if r['kind'] == kindE:
            mb[r['class']] += r['bytes']; nf[r['class']] += r['files']
    print(kindE, {k: (cnt[k], nf[k], round(mb[k] / 2**20, 1)) for k in cnt})
