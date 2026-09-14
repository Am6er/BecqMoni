# -*- coding: utf-8 -*-
"""Правка G5 (T137): вставить блок sys.stdout/sys.stderr.reconfigure(utf-8, replace)
в скрипты, у которых есть литералы вне cp1251 и нет лекарства.

  python fix_reconfigure.py <repo> [--apply]

Без --apply печатает план. Правила:
  * лекарство уже есть — `reconfigure(` или `TextIOWrapper(sys.stdout` — не трогать;
  * литералов вне cp1251 нет — не трогать;
  * модуль только импортируется (нет `__main__`, есть импортёры) — не трогать: реконфигурация
    при импорте — побочный эффект на импортёра (check_corpus.py нарочно держит cp1251);
  * есть `__main__` И есть импортёры (двойное назначение) — блок первым в теле `if __name__`;
  * иначе — блок первой конструкцией после блока импортов (после последнего импорта верхнего
    уровня, что стоит до первого def/class/if/try/for/while/with);
  * если к этому месту `sys` не импортирован — блок начинается с `import sys`.
Байты: переводы строк файла (LF/CRLF) и BOM сохраняются; пишется через bytes.
"""
import ast, glob, os, re, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
REPO = sys.argv[1]
APPLY = '--apply' in sys.argv
GLOBS = ['tools/check_*.py', 'tools/CORPUS/scripts/*.py', 'tools/nucdb/*.py',
         'tools/pie/*.py', '.claude/skills/todo-work/scripts/*.py']
IMPORTER_GLOBS = ['tools/**/*.py', '.claude/**/*.py', 'handover/**/*.py']

BLOCK = [
    "# T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.",
    "for _stream in (sys.stdout, sys.stderr):",
    "    try:",
    "        _stream.reconfigure(encoding='utf-8', errors='replace')",
    "    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт",
    "        pass",
]
STOP = (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef, ast.If, ast.Try,
        ast.For, ast.While, ast.With)


def bad(s):
    try:
        s.encode('cp1251')
        return 0
    except UnicodeEncodeError:
        return sum(1 for ch in s if not _ok(ch))


def _ok(ch):
    try:
        ch.encode('cp1251')
        return True
    except UnicodeEncodeError:
        return False


files = []
for g in GLOBS:
    files += glob.glob(os.path.join(REPO, g))
files = sorted(set(os.path.normpath(f) for f in files), key=lambda p: p.lower())
modnames = {os.path.splitext(os.path.basename(f))[0]: f for f in files}

# импортёры: кто импортирует модуль с таким именем (кроме самого себя)
importers = {m: set() for m in modnames}
allpy = []
for g in IMPORTER_GLOBS:
    allpy += glob.glob(os.path.join(REPO, g), recursive=True)
rx = re.compile(r'^\s*(?:import\s+([\w\s,]+)|from\s+\.?(\w+)\s+import)', re.M)
for p in set(os.path.normpath(x) for x in allpy):
    try:
        txt = open(p, 'rb').read().decode('utf-8', 'replace')
    except OSError:
        continue
    for m in rx.finditer(txt):
        names = [n.strip().split(' as ')[0] for n in (m.group(1) or '').split(',')] + [m.group(2)]
        for n in names:
            if n and n in importers and os.path.normpath(modnames[n]) != p:
                importers[n].add(os.path.relpath(p, REPO).replace(os.sep, '/'))

plan = []
for path in files:
    rel = os.path.relpath(path, REPO).replace(os.sep, '/')
    mod = os.path.splitext(os.path.basename(path))[0]
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    body = raw[3:] if bom else raw
    crlf = body.count(b'\r\n')
    lf = body.count(b'\n') - crlf
    if crlf and lf:
        plan.append((rel, 'ПРОПУСК: смешанные переводы строк (%d/%d)' % (crlf, lf)))
        continue
    nl = '\r\n' if crlf else '\n'
    src = body.decode('utf-8')
    m_cure = re.search(r'reconfigure\(|TextIOWrapper\(sys\.stdout|io\.open\(1,|open\(sys\.stdout\.fileno\(\)', src)
    if m_cure:
        plan.append((rel, 'есть лекарство: `%s`' % m_cure.group(0)))
        continue
    tree = ast.parse(src)
    nbad = sum(bad(n.value) for n in ast.walk(tree)
               if isinstance(n, ast.Constant) and isinstance(n.value, str))
    if nbad == 0:
        plan.append((rel, 'знаков вне cp1251 нет'))
        continue
    has_main = any(isinstance(n, ast.If) and isinstance(n.test, ast.Compare)
                   and isinstance(n.test.left, ast.Name) and n.test.left.id == '__name__'
                   for n in tree.body)
    imp = importers[mod]
    if not has_main and imp:
        plan.append((rel, 'библиотека (импортируют: %s), не трогать' % ', '.join(sorted(imp))))
        continue
    lines = src.split(nl)
    if has_main and imp:
        node = next(n for n in tree.body if isinstance(n, ast.If) and isinstance(n.test, ast.Compare)
                    and isinstance(n.test.left, ast.Name) and n.test.left.id == '__name__')
        first = node.body[0]
        indent = ' ' * first.col_offset
        sys_ok = bool(re.search(r'^\s*import\s+(?:[\w\s,]*\b)?sys\b|^\s*from\s+sys\s+import', src, re.M))
        ins = ([indent + 'import sys'] if not sys_ok else []) + [indent + b if b else '' for b in BLOCK]
        at = first.lineno - 1           # индекс строки, ПЕРЕД которой вставляем
        mode = 'в теле __main__ (импортируют: %s), строка %d' % (', '.join(sorted(imp)), first.lineno)
    else:
        last_imp = None
        sys_seen = False
        for n in tree.body:
            if isinstance(n, STOP):
                break
            if isinstance(n, (ast.Import, ast.ImportFrom)):
                last_imp = n
                if isinstance(n, ast.Import) and any(a.name == 'sys' for a in n.names):
                    sys_seen = True
                if isinstance(n, ast.ImportFrom) and n.module == 'sys':
                    sys_seen = True
        if last_imp is None:
            plan.append((rel, 'ПРОПУСК: не нашёл блока импортов'))
            continue
        ins = ([] if sys_seen else ['import sys']) + list(BLOCK)
        at = last_imp.end_lineno     # индекс строки после импорта (0-based = end_lineno)
        mode = 'после импортов, строка %d%s' % (last_imp.end_lineno + 1, '' if sys_seen else ' (+import sys)')
        # пустая строка перед блоком и после, если её там нет
        ins = [''] + ins
        if at < len(lines) and lines[at].strip() != '':
            ins = ins + ['']
    new_lines = lines[:at] + ins + lines[at:]
    plan.append((rel, 'ПРАВКА: %s; %s%s; знаков %d' % (mode, 'CRLF' if crlf else 'LF', ' BOM' if bom else '', nbad)))
    if APPLY:
        out = nl.join(new_lines).encode('utf-8')
        if bom:
            out = b'\xef\xbb\xbf' + out
        open(path, 'wb').write(out)
        # контроль: компилируется, переводы строк те же
        chk = open(path, 'rb').read()
        b2 = chk[3:] if bom else chk
        assert (b2.count(b'\r\n') > 0) == (crlf > 0) and (b2.count(b'\n') - b2.count(b'\r\n') > 0) == (lf > 0), rel
        compile(b2.decode('utf-8'), rel, 'exec')

for rel, what in plan:
    print('| `%s` | %s |' % (rel, what))
n = sum(1 for _, w in plan if w.startswith('ПРАВКА'))
print()
print('%s: %d из %d' % ('ПРАВЛЕНО' if APPLY else 'к правке', n, len(plan)))
