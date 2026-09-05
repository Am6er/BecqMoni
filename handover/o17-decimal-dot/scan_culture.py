# -*- coding: utf-8 -*-
"""
O17 / A242. Сплошной счёт мест, где число ПЕЧАТАЕТСЯ или РАЗБИРАЕТСЯ
без явной культуры, по дереву BecquerelMonitor/**.

Не догадка, а разбор: строковые литералы и комментарии вырезаются, вызовы
режутся балансировкой скобок, тип получателя берётся из объявлений того же
файла (поля, свойства, локальные, параметры, foreach) — три исхода:
NUM (точно число/дата), NONUM (точно строка/enum/bool/...), UNK (не выведен).
"""
import os, re, sys, json, io

ROOT = os.path.abspath(sys.argv[1])
OUT = os.path.abspath(sys.argv[2])

SKIP_DIRS = {'bin', 'obj', 'packages', '.git', '.vs'}

NUM_TYPES = {'double', 'float', 'decimal', 'int', 'long', 'short', 'byte',
             'uint', 'ulong', 'ushort', 'sbyte', 'Double', 'Single', 'Decimal',
             'Int32', 'Int64', 'Int16', 'Byte', 'UInt32', 'UInt64', 'UInt16',
             'SByte', 'DateTime', 'TimeSpan', 'DateTimeOffset'}
NONUM_TYPES = {'string', 'String', 'bool', 'Boolean', 'char', 'Char', 'object',
               'Object', 'Guid', 'Color', 'Point', 'Size', 'Type', 'Version'}


def strip_code(src):
    """Возвращает текст той же длины, где содержимое строк/символов/комментариев
    заменено пробелами (переводы строк сохранены — номера строк не едут)."""
    out = list(src)
    i, n = 0, len(src)
    # маска: True = это настоящий код
    while i < n:
        c = src[i]
        if c == '/' and i + 1 < n and src[i+1] == '/':
            j = src.find('\n', i)
            if j < 0:
                j = n
            for k in range(i, j):
                out[k] = ' '
            i = j
            continue
        if c == '/' and i + 1 < n and src[i+1] == '*':
            j = src.find('*/', i + 2)
            j = n if j < 0 else j + 2
            for k in range(i, j):
                if src[k] != '\n':
                    out[k] = ' '
            i = j
            continue
        if c == '@' and i + 1 < n and src[i+1] == '"':
            # verbatim string
            j = i + 2
            while j < n:
                if src[j] == '"':
                    if j + 1 < n and src[j+1] == '"':
                        j += 2
                        continue
                    j += 1
                    break
                j += 1
            for k in range(i, min(j, n)):
                if src[k] != '\n':
                    out[k] = ' '
            i = j
            continue
        if c == '$' and i + 1 < n and src[i+1] == '"':
            # интерполяция: НЕ вычищаем целиком — её надо посчитать отдельно,
            # но внутренности замещаем, оставив маркер $" на месте.
            j = i + 2
            depth = 0
            while j < n:
                ch = src[j]
                if ch == '\\':
                    j += 2
                    continue
                if ch == '{':
                    depth += 1
                elif ch == '}':
                    if depth:
                        depth -= 1
                elif ch == '"' and depth == 0:
                    j += 1
                    break
                elif ch == '\n':
                    break
                j += 1
            for k in range(i + 2, min(j, n)):
                if src[k] != '\n':
                    out[k] = ' '
            i = j
            continue
        if c == '"':
            j = i + 1
            while j < n:
                if src[j] == '\\':
                    j += 2
                    continue
                if src[j] == '"':
                    j += 1
                    break
                if src[j] == '\n':
                    break
                j += 1
            for k in range(i + 1, min(j, n) - 1 + 1):
                if k < n and src[k] != '\n' and src[k] != '"':
                    out[k] = ' '
            i = j
            continue
        if c == "'":
            j = i + 1
            while j < n:
                if src[j] == '\\':
                    j += 2
                    continue
                if src[j] == "'":
                    j += 1
                    break
                if src[j] == '\n':
                    break
                j += 1
            for k in range(i + 1, min(j, n)):
                if src[k] != '\n':
                    out[k] = ' '
            i = j
            continue
        i += 1
    return ''.join(out)


def args_of(src, open_paren):
    """Текст аргументов вызова, начиная с позиции '('. Балансировка."""
    depth = 0
    i = open_paren
    n = len(src)
    while i < n:
        if src[i] == '(':
            depth += 1
        elif src[i] == ')':
            depth -= 1
            if depth == 0:
                return src[open_paren + 1:i], i
        i += 1
    return '', n


CULTURE_RE = re.compile(r'Culture|Invariant|NumberFormat|formatProvider|provider|IFormatProvider')

DECL_RE = re.compile(
    r'\b(?:(?:public|private|protected|internal|static|readonly|const|override|virtual|new|volatile|extern|unsafe|async|sealed|partial|ref|out|in|this|params)\s+)*'
    r'(?P<type>[A-Za-z_][A-Za-z0-9_.]*(?:<[^<>()=;]{0,80}>)?(?:\[\])?)\s+'
    r'(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?=[=;,)\{])')


def collect_types(code):
    """Карта имя -> тип по объявлениям в файле (поля, локальные, параметры)."""
    m = {}
    for mo in DECL_RE.finditer(code):
        t = mo.group('type')
        nm = mo.group('name')
        if t in ('return', 'new', 'else', 'case', 'is', 'as', 'using', 'namespace',
                 'class', 'struct', 'enum', 'interface', 'if', 'while', 'for',
                 'foreach', 'switch', 'catch', 'try', 'get', 'set', 'var',
                 'public', 'private', 'protected', 'internal', 'static'):
            continue
        base = t.rstrip('?')
        if nm in m and m[nm] != base:
            m[nm] = '<conflict>'
        else:
            m.setdefault(nm, base)
    return m


NUM_LITERAL = re.compile(r'^(?:[0-9][0-9_]*(?:\.[0-9_]+)?(?:[eE][+-]?[0-9]+)?[dDfFmMlLuU]*|\)|\])$')


def receiver_of(code, dot_pos):
    """Текст выражения-получателя перед '.'; вернуть (краткое имя, полный кусок)."""
    i = dot_pos - 1
    while i >= 0 and code[i] in ' \t\r\n':
        i -= 1
    end = i + 1
    depth = 0
    while i >= 0:
        c = code[i]
        if c in ')]':
            depth += 1
        elif c in '([':
            if depth == 0:
                break
            depth -= 1
        elif depth == 0 and not (c.isalnum() or c in '_.$'):
            break
        i -= 1
    return code[i + 1:end].strip()


def classify_receiver(expr, types):
    if not expr:
        return 'UNK'
    # литерал числа
    if re.match(r'^[0-9]', expr) or re.match(r'^\(\s*[0-9]', expr):
        return 'NUM'
    # цепочка: берём последний сегмент имени до скобок
    base = expr.split('.')[0]
    base = re.sub(r'[\[\(].*$', '', base)
    if base in ('this', 'base'):
        parts = expr.split('.')
        base = parts[1] if len(parts) > 1 else base
        base = re.sub(r'[\[\(].*$', '', base)
    t = types.get(base)
    tail = expr.split('.')[-1]
    tail = re.sub(r'[\[\(].*$', '', tail)
    if t is None:
        t = types.get(tail)
    if t is None:
        return 'UNK'
    if t in NUM_TYPES:
        return 'NUM'
    if t in NONUM_TYPES:
        return 'NONUM'
    return 'UNK'


PARSE_OWNERS_NUM = {'double', 'Double', 'float', 'Single', 'decimal', 'Decimal',
                    'int', 'Int32', 'long', 'Int64', 'short', 'Int16', 'byte',
                    'Byte', 'uint', 'UInt32', 'ulong', 'UInt64', 'ushort',
                    'UInt16', 'sbyte', 'SByte', 'DateTime', 'TimeSpan',
                    'DateTimeOffset'}

# ⛔ Без отрицательного взгляда назад сюда попадает `XmlConvert.ToDouble`, а он
#   ИНВАРИАНТЕН по определению (спецификация XSD). Первый прогон насчитал так
#   12 несуществующих мест в одном только `DocumentManager`.
CONVERT_NUM = re.compile(r'(?<![A-Za-z0-9_.])Convert\.To(Double|Single|Decimal|Int32|Int64|Int16|Byte|UInt32|UInt64|UInt16|SByte|DateTime)\s*\(')


def line_of(code, pos):
    return code.count('\n', 0, pos) + 1


def scan(path):
    with open(path, 'r', encoding='utf-8-sig', errors='replace', newline='') as f:
        src = f.read()
    code = strip_code(src)
    types = collect_types(code)
    hits = []

    # --- ПЕЧАТЬ: .ToString(...)
    for mo in re.finditer(r'\.ToString\s*\(', code):
        a, close = args_of(code, code.index('(', mo.start()))
        if CULTURE_RE.search(a):
            continue
        recv = receiver_of(code, mo.start())
        # XmlConvert.ToString(...) инвариантен по определению — не место.
        if recv.split('.')[-1] == 'XmlConvert' or recv.endswith('XmlConvert'):
            continue
        kind = classify_receiver(recv, types)
        hits.append(('print', 'ToString', kind, line_of(code, mo.start()), recv[:60], a.strip()[:40]))

    # --- ПЕЧАТЬ НЕЯВНАЯ: конкатенация числа со строкой (`"…" + x`, `x + "…"`).
    #     Оператор `+` зовёт `x.ToString()` БЕЗ культуры — того же рода дефект,
    #     что и явный вызов, только его не видно глазами.
    for mo in re.finditer(r'(?<![A-Za-z0-9_"])([A-Za-z_][A-Za-z0-9_]*)\s*\+\s*"', code):
        nm = mo.group(1)
        t = types.get(nm)
        if t in NUM_TYPES:
            hits.append(('print', 'concat', 'NUM', line_of(code, mo.start()), nm, ''))
    for mo in re.finditer(r'"\s*\+\s*([A-Za-z_][A-Za-z0-9_]*)(?![A-Za-z0-9_(])', code):
        nm = mo.group(1)
        t = types.get(nm)
        if t in NUM_TYPES:
            hits.append(('print', 'concat', 'NUM', line_of(code, mo.start()), nm, ''))

    # --- ПЕЧАТЬ: string.Format(...) / String.Format(...)
    for mo in re.finditer(r'\b(?:string|String)\.Format\s*\(', code):
        a, close = args_of(code, code.index('(', mo.start()))
        first = a.split(',')[0]
        if CULTURE_RE.search(first):
            continue
        hits.append(('print', 'string.Format', 'UNK', line_of(code, mo.start()), '', first.strip()[:40]))

    # --- ПЕЧАТЬ: интерполяция
    for mo in re.finditer(r'\$"', code):
        hits.append(('print', 'interpolation', 'UNK', line_of(code, mo.start()), '', ''))

    # --- ПЕЧАТЬ: Convert.ToString(x) без провайдера
    for mo in re.finditer(r'Convert\.ToString\s*\(', code):
        a, close = args_of(code, code.index('(', mo.start()))
        if CULTURE_RE.search(a):
            continue
        hits.append(('print', 'Convert.ToString', 'UNK', line_of(code, mo.start()), '', a.strip()[:40]))

    # --- РАЗБОР: T.Parse / T.TryParse
    for mo in re.finditer(r'\b([A-Za-z_][A-Za-z0-9_.]*)\.(TryParse|Parse)\s*\(', code):
        owner = mo.group(1).split('.')[-1]
        a, close = args_of(code, code.index('(', mo.start()))
        if CULTURE_RE.search(a):
            continue
        kind = 'NUM' if owner in PARSE_OWNERS_NUM else 'NONUM'
        hits.append(('parse', owner + '.' + mo.group(2), kind,
                     line_of(code, mo.start()), owner, a.strip()[:50]))

    # --- РАЗБОР: Convert.ToXxx
    for mo in CONVERT_NUM.finditer(code):
        a, close = args_of(code, code.index('(', mo.start()))
        if CULTURE_RE.search(a):
            continue
        # Convert.ToInt32(char) и т.п. — всё равно считаем
        hits.append(('parse', 'Convert.To' + mo.group(1), 'NUM',
                     line_of(code, mo.start()), '', a.strip()[:50]))

    return hits


def main():
    rows = []
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if not fn.endswith('.cs'):
                continue
            p = os.path.join(dirpath, fn)
            rel = os.path.relpath(p, os.path.dirname(ROOT)).replace('\\', '/')
            for h in scan(p):
                rows.append((rel,) + h)

    with io.open(OUT, 'w', encoding='utf-8', newline='') as f:
        f.write('file\tside\tapi\tkind\tline\treceiver\targs\n')
        for r in rows:
            # ⛔ Аргументы бывают МНОГОСТРОЧНЫЕ, и перевод строки внутри поля
            #   рвёт TSV: первый прогон записал 994 места, а прочиталось 969 —
            #   двадцать пять записей исчезли молча. Схлопываем пробелами.
            cells = [re.sub(r'\s+', ' ', str(x)).replace('\t', ' ') for x in r]
            f.write('\t'.join(cells) + '\n')

    print('всего мест: %d' % len(rows))


main()
