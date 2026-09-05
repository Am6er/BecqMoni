# -*- coding: utf-8 -*-
"""Перечень вызывающих AppUi.Report с охватывающим типом и методом.

Комментарии и литералы гасятся С СОХРАНЕНИЕМ СМЕЩЕНИЙ — иначе номера строк
уезжают, а закомментированный вызов считается живым.
"""
import io
import os
import re
import sys

ROOT = r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8"
BS = chr(92)  # обратный слэш — отдельной константой, см. грабли heredoc


def blank(src):
    out = list(src)
    i = 0
    n = len(src)
    while i < n:
        c = src[i]
        if c == '/' and i + 1 < n and src[i + 1] == '/':
            while i < n and src[i] != '\n':
                out[i] = ' '
                i += 1
        elif c == '/' and i + 1 < n and src[i + 1] == '*':
            out[i] = ' '
            out[i + 1] = ' '
            i += 2
            while i < n and not (src[i] == '*' and i + 1 < n and src[i + 1] == '/'):
                if src[i] != '\n':
                    out[i] = ' '
                i += 1
            if i < n:
                out[i] = ' '
                out[i + 1] = ' '
                i += 2
        elif c == '"':
            verbatim = i > 0 and src[i - 1] == '@'
            out[i] = ' '
            i += 1
            while i < n:
                if verbatim:
                    if src[i] == '"':
                        if i + 1 < n and src[i + 1] == '"':
                            out[i] = ' '
                            out[i + 1] = ' '
                            i += 2
                            continue
                        out[i] = ' '
                        i += 1
                        break
                    if src[i] != '\n':
                        out[i] = ' '
                    i += 1
                else:
                    if src[i] == BS and i + 1 < n:
                        out[i] = ' '
                        out[i + 1] = ' '
                        i += 2
                        continue
                    if src[i] == '"':
                        out[i] = ' '
                        i += 1
                        break
                    if src[i] == '\n':
                        break
                    out[i] = ' '
                    i += 1
        elif c == "'":
            out[i] = ' '
            i += 1
            while i < n:
                if src[i] == BS and i + 1 < n:
                    out[i] = ' '
                    out[i + 1] = ' '
                    i += 2
                    continue
                if src[i] == "'":
                    out[i] = ' '
                    i += 1
                    break
                out[i] = ' '
                i += 1
        else:
            i += 1
    return ''.join(out)


MODS = ('public', 'private', 'protected', 'internal', 'static', 'sealed', 'abstract',
        'override', 'virtual', 'async', 'new', 'partial', 'extern', 'unsafe')
NAME = re.compile(r'[A-Za-z_@][A-Za-z0-9_@]*')


def span_of_block(txt, brace):
    d = 0
    e = brace
    while e < len(txt):
        if txt[e] == '{':
            d += 1
        elif txt[e] == '}':
            d -= 1
            if d == 0:
                return e
        e += 1
    return len(txt) - 1


def enclosing_type(txt, pos):
    best = None
    for m in re.finditer(r'\b(class|struct|interface|enum)[ \t\r\n]+([A-Za-z_@][A-Za-z0-9_@]*)', txt):
        b = txt.find('{', m.end())
        if b < 0:
            continue
        e = span_of_block(txt, b)
        if b <= pos <= e:
            if best is None or b > best[0]:
                best = (b, m.group(2))
    return best[1] if best else '?'


def enclosing_method(txt, pos):
    """Ближайшее объявление вида `Имя ( ... ) {`, чей блок охватывает pos."""
    best = None
    for m in NAME.finditer(txt):
        name = m.group(0)
        if name in MODS or name in ('if', 'for', 'foreach', 'while', 'switch', 'catch',
                                    'lock', 'using', 'return', 'new', 'fixed', 'do'):
            continue
        j = m.end()
        while j < len(txt) and txt[j] in ' \t\r\n':
            j += 1
        if j >= len(txt) or txt[j] != '(':
            continue
        d = 0
        q = j
        while q < len(txt):
            if txt[q] == '(':
                d += 1
            elif txt[q] == ')':
                d -= 1
                if d == 0:
                    break
            q += 1
        k = q + 1
        while k < len(txt) and txt[k] in ' \t\r\n':
            k += 1
        if k >= len(txt) or txt[k] != '{':
            continue
        e = span_of_block(txt, k)
        if k <= pos <= e:
            if best is None or k > best[0]:
                best = (k, name)
    return best[1] if best else '?'


def main():
    rows = []
    for base, dirs, files in os.walk(os.path.join(ROOT, 'BecquerelMonitor')):
        dirs[:] = [d for d in dirs if d not in ('bin', 'obj')]
        for f in files:
            if not f.endswith('.cs'):
                continue
            p = os.path.join(base, f)
            src = io.open(p, encoding='utf-8-sig', errors='replace').read()
            txt = blank(src)
            for m in re.finditer(r'AppUi[ \t\r\n]*\.[ \t\r\n]*Report[ \t\r\n]*\(', txt):
                line = src.count('\n', 0, m.start()) + 1
                rel = os.path.relpath(p, ROOT).replace(BS, '/')
                rows.append((rel, line, enclosing_type(txt, m.start()),
                             enclosing_method(txt, m.start())))
    rows.sort()
    for rel, line, t, meth in rows:
        print("%s:%d\t%s.%s" % (rel, line, t, meth))
    print("ВСЕГО в приложении: %d" % len(rows))


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main()
