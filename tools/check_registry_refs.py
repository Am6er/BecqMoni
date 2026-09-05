#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож ссылок на строки реестра: проверяет, что каждый номер, процитированный
в оснастке, ДЕЙСТВИТЕЛЬНО есть в `TODO.md` или в `DONE.md`.

Зачем. Номер строки реестра в комментарии читается как живой указатель: сосед идёт
по нему и попадает в пустое место, а то и ссылается на него дальше, не проверив.
Ровно это и случилось с `TODO T3` в шапке `tools/effmaker/probes/build_all.ps1`
(строка `T200`): строки `T3` нет ни в одном реестре, а на неё уже сослались.

Что считается ССЫЛКОЙ (строгий разбор, отказ кодом 1):
  * номер в обратных кавычках — `T81`, `A77`, `S56`: принятая в дереве запись;
  * форма `TODO T3` / `TODO: T3` — как в шапке скрипта, с которой всё началось.

Что считается ПОДОЗРЕНИЕМ (советом, код возврата НЕ меняет, печатается отдельно
и только при `--advisory`): голый токен вида T3 без кавычек. Голый разбор шумит —
N42 это формат файла, F1 бывает клавишей, — поэтому он вынесен из приговора.

⚠ Буква ссылки берётся ИЗ САМИХ РЕЕСТРОВ (какие префиксы там встретились, те и
судятся). Иначе в приговор попадает вещество в кавычках — `K40`, `H20`, `G8`
живут в пробах и никакими строками реестра не являются.

  python tools/check_registry_refs.py [--root tools] [--ext .ps1,.py,.cs]
                                      [--advisory] [--quiet]

Коды возврата:
  0 — все строгие ссылки нашлись в реестрах;
  1 — есть ссылка, которой нет ни в `TODO.md`, ни в `DONE.md` (перечень поимённо:
      файл, номер строки, номер ссылки, сама строка);
  2 — не читается реестр или не задан ни один каталог для обхода.

⚠ Реестры и файлы дерева бывают и `LF`, и `CRLF`; всё читается с `newline=''`,
чтобы одиночный `CR` не разорвал строку и не сдвинул нумерацию.
"""

import argparse
import io
import os
import re
import sys

# Первая клетка строки таблицы реестра: | **T81** | ... | или | ~~T75~~ | ... |
RE_REGISTRY_ID = re.compile(r'^\|\s*[*~`\s]*([A-Z]{1,2}[0-9]{1,4})[*~`\s]*\|')

# Строгая ссылка: номер в обратных кавычках либо форма «TODO T3».
RE_REF_BACKTICK = re.compile(r'`([A-Z][0-9]{1,4})`')
RE_REF_TODO = re.compile(r'\bTODO[:\s]+([A-Z][0-9]{1,4})\b')

# Подозрение: голый токен (буква подставляется из реестров, см. build_bare_re).
RE_BARE_TMPL = r'(?<![A-Za-z0-9_])([%s][0-9]{1,4})(?![A-Za-z0-9_])'

DEFAULT_ROOTS = ['tools']
DEFAULT_EXTS = ['.ps1', '.py', '.cs']
REGISTRIES = ['TODO.md', 'DONE.md']

# Токены, которые выглядят ссылкой, но ею не являются. Проверяются ПОСЛЕ реестров:
# заведись когда-нибудь настоящая строка с таким номером — она найдётся первой,
# и оговорка ничего не спрячет. Каждой нужна причина, иначе список станет свалкой.
NOT_A_REF = {
    u'N42': u'формат файла ANSI N42 (стандарт спектров), а не строка реестра',
}


def _utf8_console():
    u"""Консоль здесь cp1251, а весь текст русский: без этого печать падает
    `UnicodeEncodeError` и сторож выглядит сломанным вместо того, чтобы судить."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def read_lines(path):
    u"""Читает файл строками, не нормализуя переводы строк."""
    with io.open(path, 'r', encoding='utf-8-sig', errors='replace', newline='') as fh:
        return fh.read().splitlines()


def load_registry_ids(repo, names):
    ids = set()
    per_file = {}
    for name in names:
        path = os.path.join(repo, name)
        if not os.path.isfile(path):
            sys.stderr.write(u'ОТКАЗ: реестр не найден: %s\n' % path)
            return None, None
        found = set()
        for line in read_lines(path):
            m = RE_REGISTRY_ID.match(line)
            if m:
                found.add(m.group(1))
        per_file[name] = found
        ids |= found
    return ids, per_file


def walk_files(repo, roots, exts):
    out = []
    for root in roots:
        base = os.path.join(repo, root)
        if os.path.isfile(base):
            out.append(base)
            continue
        if not os.path.isdir(base):
            sys.stderr.write(u'ОТКАЗ: нет такого каталога: %s\n' % base)
            return None
        for dirpath, dirnames, filenames in os.walk(base):
            # каталоги сборки проб (`build`, `build_rel`, `build_o4`, …) в .gitignore:
            # там лежат копии, а не исходники, и ссылки в них считать нечего.
            dirnames[:] = [d for d in dirnames
                           if d not in ('.git', 'bin', 'obj', 'packages', '__pycache__')
                           and not re.match(r'^build(_[A-Za-z0-9]+)?$', d)]
            for fn in filenames:
                if os.path.splitext(fn)[1].lower() in exts:
                    out.append(os.path.join(dirpath, fn))
    return sorted(out)


def build_bare_re(prefixes):
    return re.compile(RE_BARE_TMPL % u''.join(sorted(prefixes)))


def scan(repo, files, ids, prefixes, advisory):
    bare_re = build_bare_re(prefixes) if advisory else None
    strict = []
    loose = []
    waived = {}
    n_strict_refs = 0
    n_bare_refs = 0
    for path in files:
        rel = os.path.relpath(path, repo).replace('\\', '/')
        try:
            lines = read_lines(path)
        except (IOError, OSError) as exc:
            sys.stderr.write(u'ОТКАЗ: не читается %s: %s\n' % (rel, exc))
            return None, None, None, 0, 0
        for no, line in enumerate(lines, 1):
            hits = set(RE_REF_BACKTICK.findall(line)) | set(RE_REF_TODO.findall(line))
            # буква не из реестров — это не ссылка, а вещество/формат в кавычках
            hits = set(r for r in hits if r[0] in prefixes)
            n_strict_refs += len(hits)
            for ref in sorted(hits):
                if ref in ids:
                    continue
                if ref in NOT_A_REF:
                    waived[ref] = waived.get(ref, 0) + 1
                    continue
                strict.append((rel, no, ref, line.strip()))
            if advisory:
                bare = set(bare_re.findall(line)) - hits
                n_bare_refs += len(bare)
                for ref in sorted(bare):
                    if ref not in ids and ref not in NOT_A_REF:
                        loose.append((rel, no, ref, line.strip()))
    return strict, loose, waived, n_strict_refs, n_bare_refs


def main(argv=None):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--repo', default=None, help=u'корень дерева (по умолчанию — от файла скрипта)')
    ap.add_argument('--root', action='append', default=None,
                    help=u'что обходить; можно повторять (по умолчанию: tools)')
    ap.add_argument('--ext', default=','.join(DEFAULT_EXTS),
                    help=u'расширения через запятую (по умолчанию: .ps1,.py,.cs)')
    ap.add_argument('--registry', action='append', default=None,
                    help=u'реестры (по умолчанию: TODO.md и DONE.md)')
    ap.add_argument('--advisory', action='store_true',
                    help=u'дополнительно печатать голые токены; на код возврата НЕ влияет')
    ap.add_argument('--quiet', action='store_true', help=u'только итог')
    args = ap.parse_args(argv)

    repo = args.repo or os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    roots = args.root or DEFAULT_ROOTS
    exts = [e if e.startswith('.') else '.' + e for e in args.ext.split(',') if e.strip()]
    registries = args.registry or REGISTRIES

    ids, per_file = load_registry_ids(repo, registries)
    if ids is None:
        return 2
    files = walk_files(repo, roots, exts)
    if files is None:
        return 2
    if not files:
        sys.stderr.write(u'ОТКАЗ: обходить нечего (roots=%s ext=%s)\n' % (roots, exts))
        return 2

    prefixes = set(i[0] for i in ids)

    strict, loose, waived, n_strict, n_bare = scan(repo, files, ids, prefixes, args.advisory)
    if strict is None:
        return 2

    if not args.quiet:
        print(u'реестры: %s' % u', '.join(
            u'%s — %d строк' % (k, len(v)) for k, v in sorted(per_file.items())))
        print(u'известных номеров всего: %d, буквы: %s'
              % (len(ids), u''.join(sorted(prefixes))))
        print(u'обойдено файлов: %d, строгих ссылок проверено: %d' % (len(files), n_strict))
        if args.advisory:
            print(u'голых токенов проверено дополнительно: %d' % n_bare)
        for ref in sorted(waived):
            print(u'не судим %d раз: %s — %s' % (waived[ref], ref, NOT_A_REF[ref]))

    if strict:
        print(u'')
        print(u'ОТКАЗ: ссылок в никуда — %d' % len(strict))
        for rel, no, ref, text in strict:
            print(u'  %s:%d: %s -> нет ни в одном реестре | %s' % (rel, no, ref, text[:160]))
    else:
        print(u'битых строгих ссылок: 0')

    if args.advisory:
        print(u'')
        if loose:
            print(u'СОВЕТ (на код возврата не влияет): голых токенов без строки — %d' % len(loose))
            for rel, no, ref, text in loose:
                print(u'  %s:%d: %s | %s' % (rel, no, ref, text[:160]))
        else:
            print(u'СОВЕТ: голых токенов без строки нет')

    return 1 if strict else 0


if __name__ == '__main__':
    sys.exit(main())
