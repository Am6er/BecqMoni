# -*- coding: utf-8 -*-
"""
F18 (доделка П8а). Перепись мест формы «Матрица отклика», которых сканер
`handover/o17-decimal-dot/scan_culture.py` НЕ ВИДИТ: у них культура УКАЗАНА
явно, и сканер считает такое место переведённым, хотя `CurrentCulture` — тот
самый дефект по правилу Amber. Плюс отдельный поиск `StringBuilder.Append`
(сканер к нему слеп, оговорка полосы П7) и форматов с группировкой разрядов.

    python census_f18.py <файл.cs> [<файл.cs> ...]

Печатает поимённо, чтобы остаток разбирался по строкам, а не по числу.
"""
import io, re, sys

BACKSLASH = chr(92)
QUOTE = chr(34)
APOS = chr(39)


# строковые литералы и комментарии вырезаются — иначе слово CurrentCulture из
# комментария полосы считалось бы местом печати.
def strip(src):
    out = list(src)
    i = 0
    n = len(src)
    while i < n:
        c = src[i]
        if c == '/' and i + 1 < n and src[i + 1] == '/':
            j = src.find('\n', i)
            j = n if j < 0 else j
            for k in range(i, j):
                out[k] = ' '
            i = j
            continue
        if c == '/' and i + 1 < n and src[i + 1] == '*':
            j = src.find('*/', i + 2)
            j = n if j < 0 else j + 2
            for k in range(i, j):
                if src[k] != '\n':
                    out[k] = ' '
            i = j
            continue
        if c == '@' and i + 1 < n and src[i + 1] == QUOTE:
            j = i + 2
            while j < n:
                if src[j] == QUOTE:
                    if j + 1 < n and src[j + 1] == QUOTE:
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
        if c == QUOTE or c == APOS:
            q = c
            j = i + 1
            while j < n:
                if src[j] == BACKSLASH:
                    j += 2
                    continue
                if src[j] == q:
                    j += 1
                    break
                if src[j] == '\n':
                    break
                j += 1
            for k in range(i, min(j, n)):
                if src[k] != '\n':
                    out[k] = ' '
            i = j
            continue
        i += 1
    return ''.join(out)


PATS = [
    ('ЯВНАЯ КУЛЬТУРА ПОТОКА (CurrentCulture)',
     re.compile(r'CultureInfo\s*\.\s*CurrentCulture')),
    ('ЯВНАЯ КУЛЬТУРА ИНТЕРФЕЙСА (CurrentUICulture)',
     re.compile(r'CultureInfo\s*\.\s*CurrentUICulture')),
    ('ИНВАРИАНТ',
     re.compile(r'CultureInfo\s*\.\s*InvariantCulture')),
    ('StringBuilder / Append',
     re.compile(r'\.\s*Append(?:Line|Format)?\s*\(')),
    ('РАЗБОР Parse / TryParse / Convert.To',
     re.compile(r'\b(?:double|float|decimal|int|long|short|byte|Double|Single|Decimal|Int32'
                r'|Int64|DateTime|TimeSpan)\s*\.\s*(?:Parse|TryParse)\s*\('
                r'|Convert\s*\.\s*To(?:Double|Single|Decimal|Int32|Int64|DateTime)\s*\(')),
    ('ToString', re.compile(r'\.\s*ToString\s*\(')),
    ('string.Format', re.compile(r'\bstring\s*\.\s*Format\s*\(')),
    ('ИНТЕРПОЛЯЦИЯ $"', None),                       # ищется по СЫРОМУ тексту
    ('ФОРМАТ С ГРУППИРОВКОЙ (n0..n9 / N0..N9)', None),  # ищется по СЫРОМУ тексту
    ('ThousandsSeparator', re.compile(r'ThousandsSeparator')),
]
RAW_INTERP = re.compile(r'\$' + QUOTE)
RAW_GROUP = re.compile(QUOTE + r'\s*[nN][0-9]?\s*' + QUOTE + r'|:\s*[nN][0-9]?\s*[}]')

for path in sys.argv[1:]:
    src = io.open(path, encoding='utf-8-sig', newline='').read()
    code = strip(src)
    lines_code = code.split('\n')
    lines_raw = src.split('\n')
    print('=== %s (%d строк)' % (path.replace(BACKSLASH, '/'), len(lines_raw)))
    for name, rx in PATS:
        hits = []
        if rx is None:
            r = RAW_INTERP if name.startswith('ИНТЕРПОЛЯЦИЯ') else RAW_GROUP
            for i, ln in enumerate(lines_raw, 1):
                if r.search(ln):
                    hits.append((i, ln.strip()))
        else:
            for i, ln in enumerate(lines_code, 1):
                if rx.search(ln):
                    hits.append((i, lines_raw[i - 1].strip()))
        print('  %-46s %d' % (name, len(hits)))
        for i, t in hits:
            print('        %4d  %s' % (i, t[:110]))
    print('')
