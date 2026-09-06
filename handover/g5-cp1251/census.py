# -*- coding: utf-8 -*-
"""Перепись G5 (T137): у каких скриптов есть строковые литералы со знаками вне cp1251
и стоит ли у них sys.stdout.reconfigure. Ищет по AST: все Constant-строки (в т.ч. части
f-строк), кодирует cp1251 strict; отдельно считает те, что лежат внутри вызова print()
или *.write()/error()/exit(). Печатает markdown-таблицу и сводку."""
import ast, glob, os, re, sys, collections
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
REPO = sys.argv[1]
GLOBS = ['tools/check_*.py', 'tools/CORPUS/scripts/*.py', 'tools/nucdb/*.py',
         'tools/pie/*.py', '.claude/skills/todo-work/scripts/*.py']
files = []
for g in GLOBS:
    files += sorted(glob.glob(os.path.join(REPO, g)))
files = sorted(set(files), key=lambda p: os.path.relpath(p, REPO).lower())


def bad_chars(s):
    out = []
    for ch in s:
        try:
            ch.encode('cp1251')
        except UnicodeEncodeError:
            out.append(ch)
    return out


def in_print(parents):
    for p in parents:
        if isinstance(p, ast.Call):
            f = p.func
            if isinstance(f, ast.Name) and f.id == 'print':
                return True
            if isinstance(f, ast.Attribute) and f.attr in ('write', 'error', 'exit'):
                return True
    return False


rows = []
for path in files:
    rel = os.path.relpath(path, REPO).replace(os.sep, '/')
    raw = open(path, 'rb').read()
    crlf = raw.count(b'\r\n')
    lf = raw.count(b'\n') - crlf
    eol = 'CRLF' if crlf and not lf else ('LF' if lf and not crlf else 'MIX(%d/%d)' % (crlf, lf))
    bom = raw.startswith(b'\xef\xbb\xbf')
    src = raw.decode('utf-8-sig' if bom else 'utf-8', 'surrogateescape')
    try:
        tree = ast.parse(src)
    except SyntaxError as e:
        rows.append((rel, '?', '?', 'SyntaxError %s' % e, eol))
        continue
    parent = {}
    for node in ast.walk(tree):
        for ch in ast.iter_child_nodes(node):
            parent[ch] = node
    total = 0
    inprint = 0
    chars = collections.Counter()
    lines_print = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Constant) and isinstance(node.value, str):
            b = bad_chars(node.value)
            if not b:
                continue
            total += len(b)
            chars.update(b)
            ps = []
            p = parent.get(node)
            while p is not None:
                ps.append(p)
                p = parent.get(p)
            if in_print(ps):
                inprint += len(b)
                lines_print.add(node.lineno)
    m = re.search(r'reconfigure\(([^)]*)\)', src)
    if m:
        a = m.group(1)
        kind = 'utf-8' if 'utf' in a else 'enc как есть'
        me = re.search(r"errors\s*=\s*['\"](\w+)", a)
        if me:
            kind += ', errors=' + me.group(1)
    else:
        kind = 'НЕТ'
    has_main = '__main__' in src
    imports_sys = bool(re.search(r'^\s*import sys\b|^\s*import .*\bsys\b|^\s*from sys ', src, re.M))
    rows.append((rel, total, inprint, kind, eol, has_main, imports_sys, bom,
                 ''.join(sorted(set(chars))), sorted(lines_print)[:12]))

print('| скрипт | знаков вне cp1251 всего / в print,write | reconfigure | `__main__` | import sys | EOL | знаки | строки print |')
print('|---|---|---|---|---|---|---|---|')
for r in rows:
    if len(r) < 10:
        print('| `%s` | %s | %s | %s | %s |' % r[:5])
        continue
    rel, total, inprint, kind, eol, has_main, imp, bom, ch, lp = r
    print('| `%s` | %d / %d | %s | %s | %s | %s%s | %s | %s |' % (
        rel, total, inprint, kind, 'да' if has_main else 'нет', 'да' if imp else 'нет', eol,
        ' BOM' if bom else '', ch, ','.join(map(str, lp))))
good = [r for r in rows if len(r) >= 10]
n_all = len(good)
n_bad = len([r for r in good if r[1] > 0])
n_bad_print = len([r for r in good if r[2] > 0])
n_fix = len([r for r in good if r[2] > 0 and r[3] == 'НЕТ'])
n_fix_any = len([r for r in good if r[1] > 0 and r[3] == 'НЕТ'])
print()
print('скриптов: %d; со знаками вне cp1251 в литералах: %d; из них в print/write: %d; '
      'без reconfigure и со знаками в print: %d; без reconfigure со знаками где угодно: %d'
      % (n_all, n_bad, n_bad_print, n_fix, n_fix_any))
