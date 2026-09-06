# -*- coding: utf-8 -*-
import io, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = "C:/Users/moroz/source/repos/BQ Eng res .NET 4.8"
SKIP = re.compile(r"[\/](build[^\/]*|bin|obj|packages|wd_[^\/]*|out_[^\/]*|\.git)[\/]")
files=[]
for base,dirs,names in os.walk(ROOT):
    if SKIP.search(base+os.sep): continue
    for n in names:
        if n.endswith(".cs"): files.append(os.path.join(base,n))
KEY = re.compile(r'StartsWith\(\s*"(--[A-Za-z0-9_-]+)=?"|==\s*"(--[A-Za-z0-9_-]+)"')
DENY = [u'неизвестн', u'не знаю ключа', u'не знаю аргумента', u'unknown', u'неопознан', u'лишний ключ']
HEAVY = re.compile(r'ResponseMatrixBuilder|EfficiencySimulator|ResponseMatrixOptions|CorpusMatrix')
silent=[]; deny=[]; heavy=[]
for p in files:
    t=io.open(p,encoding="utf-8-sig",errors="replace").read()
    if not KEY.search(t): continue
    rel=os.path.relpath(p,ROOT).replace("\\","/")
    low=t.lower()
    if any(w in low for w in DENY): deny.append(rel)
    else:
        silent.append(rel)
        if HEAVY.search(t): heavy.append(rel)
print("файлов с разбором --ключей: %d" % (len(silent)+len(deny)))
print("  ОТКАЗЫВАЮТ на неизвестный ключ: %d" % len(deny))
print("  ГЛОТАЮТ МОЛЧА               : %d   из них с тяжёлым счётом: %d" % (len(silent), len(heavy)))
print()
print("тяжёлые, глотающие молча:")
for r in sorted(heavy): print("  "+r)
