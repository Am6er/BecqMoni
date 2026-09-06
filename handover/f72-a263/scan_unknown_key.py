# -*- coding: utf-8 -*-
"""F72 / A263. Перепись разборов --ключей: отказывает ли файл на НЕИЗВЕСТНОЕ ИМЯ ключа.

Отличие от handover/f70-a77/scan_key_parsers.py: тот судил по НАЛИЧИЮ СЛОВА
('unknown', 'неизвестн', ...) где угодно в файле. Здесь разбирается сама
цепочка if/else if разбора доводов: ищется её ХВОСТ и то, что он делает.
"""
import io, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = "C:/Users/moroz/source/repos/BQ Eng res .NET 4.8"
# before_src — мои же копии из HEAD для ДО-сборки, дерево их не собирает.
SKIP = re.compile(r"[\\/](build[^\\/]*|bin|obj|packages|wd_[^\\/]*|out_[^\\/]*|out|[.]git|__pycache__|before_src)[\\/]")

KEY = re.compile(r'StartsWith\(\s*"(--[A-Za-z0-9_-]+)=?"|==\s*"(--[A-Za-z0-9_-]+)"')
HEAVY = re.compile(r'ResponseMatrixBuilder|EfficiencySimulator|ResponseMatrixOptions|CorpusMatrix')

BS = chr(92)


def strip_comments(t):
    out = []
    i = 0
    n = len(t)
    while i < n:
        c = t[i]
        if c == '"':
            j = i + 1
            while j < n:
                if t[j] == BS:
                    j += 2
                    continue
                if t[j] == '"':
                    break
                j += 1
            out.append(t[i:j + 1])
            i = j + 1
            continue
        if c == '/' and i + 1 < n and t[i + 1] == '/':
            j = t.find('\n', i)
            if j < 0:
                j = n
            out.append(' ' * (j - i))
            i = j
            continue
        if c == '/' and i + 1 < n and t[i + 1] == '*':
            j = t.find('*/', i)
            j = n if j < 0 else j + 2
            out.append(' ' * (j - i))
            i = j
            continue
        out.append(c)
        i += 1
    return ''.join(out)


def block_at(t, pos):
    """pos указывает на '{'; вернуть (start, end_exclusive) тела."""
    d = 0
    for i in range(pos, len(t)):
        if t[i] == '{':
            d += 1
        elif t[i] == '}':
            d -= 1
            if d == 0:
                return pos + 1, i
    return pos + 1, len(t)


REFUSE = re.compile(r'return\s+[1-9]\d*\s*;|Environment\.Exit\s*\(\s*[1-9]|throw\s+new|Usage\s*\(|PrintUsage')
ASSIGN = re.compile(r'=\s*a\s*;|=\s*arg\s*;|=\s*args\[|\.Add\s*\(\s*a\s*\)|\.Add\s*\(\s*arg\s*\)')


def analyse(path):
    raw = io.open(path, encoding="utf-8-sig", errors="replace").read()
    t = strip_comments(raw)
    if not KEY.search(t):
        return None
    keys = sorted(set(m.group(1) or m.group(2) for m in KEY.finditer(t)))
    # Каждый 'else' в файле, за которым НЕ идёт 'if', — хвост какой-то цепочки.
    tails = []
    for m in re.finditer(r'\belse\b', t):
        rest = t[m.end():]
        if re.match(r'\s*if\b', rest):
            continue                      # else if — не хвост
        mm = re.match(r'\s*\{', rest)
        if mm:
            s, e = block_at(t, m.end() + mm.end() - 1)
            body = t[s:e]
        else:
            j = t.find(';', m.end())
            body = t[m.end(): j + 1 if j >= 0 else m.end() + 200]
        tails.append((m.start(), body))
    # Отказ бывает не только голым хвостом: там, где часть доводов ПОЗИЦИОННЫЕ и
    # читается по индексу (MeasuredPoint), хвост обязан быть с условием на «--»,
    # иначе он отказал бы на args[0]. Такая ветка — тоже отказ.
    guarded = False
    for m in re.finditer(r'\belse\s+if\s*\(\s*\w+(?:\[\w+\])?\.StartsWith\(\s*"--"', t):
        rest = t[m.end():]
        j = t.find('{', m.end())
        if j < 0:
            continue
        s, e = block_at(t, j)
        if REFUSE.search(t[s:e]):
            guarded = True
    refuses = guarded
    positional = False
    for pos, body in tails:
        window = t[max(0, pos - 4000):pos]
        if not KEY.search(window):
            continue                      # хвост не от разбора ключей
        if REFUSE.search(body):
            refuses = True
        elif ASSIGN.search(body):
            positional = True
    return dict(keys=keys, refuses=refuses, positional=positional,
                heavy=bool(HEAVY.search(t)), ntails=len(tails))


files = []
for base, dirs, names in os.walk(ROOT):
    if SKIP.search(base + os.sep):
        continue
    for n in names:
        if n.endswith(".cs"):
            files.append(os.path.join(base, n))

rows = []
for p in sorted(files):
    r = analyse(p)
    if r is None:
        continue
    r['path'] = os.path.relpath(p, ROOT).replace("\\", "/")
    rows.append(r)

silent = [r for r in rows if not r['refuses']]
deny = [r for r in rows if r['refuses']]
heavy = [r for r in silent if r['heavy']]
print("файлов с разбором --ключей: %d" % len(rows))
print("  ОТКАЗЫВАЮТ на неизвестное ИМЯ: %d" % len(deny))
print("  ГЛОТАЮТ МОЛЧА                : %d   из них тяжёлых: %d" % (len(silent), len(heavy)))
print()
print("--- глотают молча, ТЯЖЁЛЫЕ (%d) ---" % len(heavy))
for r in sorted(heavy, key=lambda x: x['path']):
    print("  %-58s поз=%s ключей=%d" % (r['path'], 'да' if r['positional'] else '- ', len(r['keys'])))
print()
print("--- глотают молча, прочие (%d) ---" % (len(silent) - len(heavy)))
for r in sorted(silent, key=lambda x: x['path']):
    if r['heavy']:
        continue
    print("  %-58s поз=%s ключей=%d" % (r['path'], 'да' if r['positional'] else '- ', len(r['keys'])))
print()
print("--- ОТКАЗЫВАЮТ (%d) ---" % len(deny))
for r in sorted(deny, key=lambda x: x['path']):
    print("  %s" % r['path'])
