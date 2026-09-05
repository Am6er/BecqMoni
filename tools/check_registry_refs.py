#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож ссылок на строки реестра: проверяет, что каждый номер, процитированный
в оснастке, коде приложения и журналах, ДЕЙСТВИТЕЛЬНО есть в `TODO.md` или в
`DONE.md`.

Зачем. Номер строки реестра в комментарии читается как живой указатель: сосед идёт
по нему и попадает в пустое место, а то и ссылается на него дальше, не проверив.
С этого сторож и начался (`T200`): номер `T3` в шапке `build_all.ps1` сочли
мёртвым, потому что искали его жирным (`**T3**`), а он лежит в `DONE.md`
зачёркнутым (`~~T3~~`) — сторож ищет ОБЕ формы, и посылка `T200` снята им же.

Что считается ССЫЛКОЙ (строгий разбор, отказ кодом 1):
  * номер в обратных кавычках — `T81`, `A77`, `S56`: принятая в дереве запись;
  * форма `TODO T3` / `TODO: T3` — как в шапке скрипта, с которой всё началось.

Что считается ПОДОЗРЕНИЕМ (советом, код возврата НЕ меняет, печатается отдельно
и только при `--advisory`): голый токен вида T3 без кавычек. Голый разбор шумит —
N42 это формат файла, F1 бывает клавишей, — поэтому он вынесен из приговора.

⚠ Буква ссылки берётся ИЗ САМИХ РЕЕСТРОВ (какие префиксы там встретились, те и
судятся). Иначе в приговор попадает вещество в кавычках — `K40`, `H20`, `G8`
живут в пробах и никакими строками реестра не являются.

ИСКЛЮЧЕНИЯ — ПО ПРАВИЛУ, НЕ ПО ПОДГОНУ (`T221`). Номер, которого нет в реестрах,
проходит правила ниже по порядку; первое подошедшее его оправдывает, и в итоге
печатается строка «исключено по правилу: N ссылок (…)» с раскладкой по правилам.
Правила проверяются ПОСЛЕ реестров: заведись когда-нибудь строка с таким
номером — она найдётся первой, и правило ничего не спрячет.

  1. «удалена решением» — строка убрана приказом Amber (`REMOVED_BY_DECISION`,
     у каждой дата и где записано решение). Ссылка на неё в ЖУРНАЛЕ историчный
     факт: журнал — запись, а не инструкция (`T219`), править её — подделка.
     ⛔ Та же ссылка в КОДЕ (`.cs`, `.ps1`, `.py`, …) НЕ оправдывается: код —
     живая инструкция, и указатель в удалённую строку в нём надо снять. Так
     четыре A200 в пробах дозы остались отказом (сняты полосой G1 06.09.2026),
     а тринадцать в журналах — нет. ⚠ Правило судит и этот файл: номер удалённой
     строки здесь написан без кавычек нарочно.
  2. «приоритет» — `P0`…`P3` без такой строки в реестре — разряд строки, а не её
     номер.
  3. «формат числа .NET» — `N0`, `F0`, `X12`: номер 0 у строк реестра не бывает,
     а буква из DEFGNX на строке о печати чисел (формат, спецификатор, печатается,
     ToString, культура, разряды) — спецификатор `ToString`, а не строка.
  4. «имя полосы» — полосы захода зовутся той же буквой с числом (`C12`, `F36`,
     `O4`), и строка про «полосу», «журнал» или «обрыв» называет полосу, а не
     строку реестра. Для строк, где слова нет, полоса перечисляется поимённо в
     `NOT_A_REF` с причиной.
  5. «цитата положительного контроля» — строка, сама называющая себя
     положительным контролем, цитирует подброшенный номер (`T9999`), а не
     ссылается. ⚠ Свой контроль подбрасывать БЕЗ этих слов, иначе он пройдёт.
  6. `NOT_A_REF` — поимённо, с причиной у каждого (формат файла `N42`,
     процентиль `P25`, полосы `C12`/`F36`).

  python tools/check_registry_refs.py [--root tools ...] [--ext .ps1,.py,.cs,...]
                                      [--advisory] [--quiet]

Штатная область без доводов: `tools`, `handover`, `BecquerelMonitor`, `database`,
расширения `.ps1 .py .cs .md .cmd .bat .sql`. Читатель — `tools/check_all.py`
(`T220`); отказ этого сторожа останавливает приёмку.

Коды возврата:
  0 — все строгие ссылки нашлись в реестрах либо оправданы правилом;
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

DEFAULT_ROOTS = ['tools', 'handover', 'BecquerelMonitor', 'database']
DEFAULT_EXTS = ['.ps1', '.py', '.cs', '.md', '.cmd', '.bat', '.sql']
REGISTRIES = ['TODO.md', 'DONE.md']

# Журнал — запись, а не инструкция (`T219`): `*.md` в `handover/` и файлы
# `handover-*.md` где угодно (у `tools/effmaker`, `tools/interspec` свои).
RE_JOURNAL_NAME = re.compile(r'^handover-.*\.md$', re.IGNORECASE)

# Правило 1. Строки, убранные решением Amber. Ссылка в журнале историчная и
# оправдывается; в коде — нет. У каждой: когда убрана и где решение записано.
REMOVED_BY_DECISION = {
    u'T39': u'убрана решением Amber 18.08.2026 (handover/handover-2026-08-18-small-rows.md:18)',
    u'T51': u'убрана решением Amber 18.08.2026 (handover/handover-2026-08-18-small-rows.md:18)',
    u'T1': u'удалена приказом Amber 05.09.2026 (TODO.md, «Чего делать НЕ надо»: «Удалить задачу T1»)',
    u'A200': u'удалена приказом Amber 05.09.2026 вместе с A231–A233, T224 (TODO.md, «Чего делать НЕ надо», вопросник по A231)',
    u'A231': u'удалена приказом Amber 05.09.2026 (TODO.md, «Чего делать НЕ надо», вопросник по A231)',
    u'A232': u'удалена приказом Amber 05.09.2026 (TODO.md, «Чего делать НЕ надо», вопросник по A231)',
    u'A233': u'удалена приказом Amber 05.09.2026 (TODO.md, «Чего делать НЕ надо», вопросник по A231)',
    u'T224': u'удалена приказом Amber 05.09.2026 (TODO.md, «Чего делать НЕ надо», вопросник по A231)',
}

# Правило 2. Формат числа .NET: буквы спецификаторов и слова о печати чисел.
FORMAT_LETTERS = u'DEFGNX'
RE_FORMAT_CONTEXT = re.compile(u'формат|спецификатор|печата|ToString|культур|разряд', re.IGNORECASE)

# Правило 3. Приоритет строки.
RE_PRIORITY = re.compile(r'^P[0-3]$')

# Правило 4. Имя полосы захода: строка говорит о полосе, журнале или обрыве.
RE_LANE_CONTEXT = re.compile(u'полос|журнал|обрыв', re.IGNORECASE)

# Правило 5. Цитата положительного контроля.
RE_CONTROL_CONTEXT = re.compile(u'положительн\\w*\\s+контрол', re.IGNORECASE)

# Правило 6. Токены, которые выглядят ссылкой, но ею не являются, поимённо.
# Каждому нужна причина, иначе список станет свалкой.
NOT_A_REF = {
    u'N42': u'формат файла ANSI N42 (стандарт спектров), а не строка реестра',
    u'P25': u'25-й процентиль в записи статистики (handover-2026-09-05-f5-v18-v19.md), а не строка',
    u'C12': u'полоса захода 05.09.2026 (журнал handover-2026-09-05-c12-n42-ostatki.md), а не строка',
    u'F36': u'полоса захода 05.09.2026, оборвана (журнал handover-2026-09-05-f37-s132.md), а не строка',
}

RULE_NAMES = [
    (u'removed', u'удалена решением Amber; ссылка в журнале историчная'),
    (u'priority', u'приоритет строки P0–P3, а не её номер'),
    (u'format', u'формат числа .NET (номер 0 или буква DEFGNX на строке о печати чисел)'),
    (u'lane', u'имя полосы захода на строке о полосе/журнале/обрыве'),
    (u'control', u'цитата положительного контроля (строка сама так себя называет)'),
    (u'not_a_ref', u'поимённо, NOT_A_REF с причиной'),
]


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


def is_journal(rel):
    u"""Журнал — запись, а не инструкция: `handover/**/*.md` и `handover-*.md`."""
    low = rel.lower()
    base = os.path.basename(low)
    if not base.endswith('.md'):
        return False
    return low.startswith('handover/') or bool(RE_JOURNAL_NAME.match(base))


def waive(ref, line, journal):
    u"""Возвращает (ключ правила, причина) либо None, если номер не оправдан."""
    if ref in REMOVED_BY_DECISION:
        if journal:
            return u'removed', REMOVED_BY_DECISION[ref]
        # в коде — отказ, но с точной причиной, чтобы её не искали заново
        return None
    if RE_PRIORITY.match(ref):
        return u'priority', u'приоритет строки'
    if ref[1:] == u'0' or (ref[0] in FORMAT_LETTERS and RE_FORMAT_CONTEXT.search(line)):
        return u'format', u'спецификатор формата .NET'
    if RE_LANE_CONTEXT.search(line):
        return u'lane', u'имя полосы захода'
    if RE_CONTROL_CONTEXT.search(line):
        return u'control', u'цитата положительного контроля'
    if ref in NOT_A_REF:
        return u'not_a_ref', NOT_A_REF[ref]
    return None


def build_bare_re(prefixes):
    return re.compile(RE_BARE_TMPL % u''.join(sorted(prefixes)))


def scan(repo, files, ids, prefixes, advisory):
    bare_re = build_bare_re(prefixes) if advisory else None
    strict = []
    loose = []
    waived = {}      # правило -> {номер: число}
    n_strict_refs = 0
    n_bare_refs = 0
    n_journals = 0
    for path in files:
        rel = os.path.relpath(path, repo).replace('\\', '/')
        journal = is_journal(rel)
        if journal:
            n_journals += 1
        try:
            lines = read_lines(path)
        except (IOError, OSError) as exc:
            sys.stderr.write(u'ОТКАЗ: не читается %s: %s\n' % (rel, exc))
            return None
        for no, line in enumerate(lines, 1):
            hits = set(RE_REF_BACKTICK.findall(line)) | set(RE_REF_TODO.findall(line))
            # буква не из реестров — это не ссылка, а вещество/формат в кавычках
            hits = set(r for r in hits if r[0] in prefixes)
            n_strict_refs += len(hits)
            for ref in sorted(hits):
                if ref in ids:
                    continue
                w = waive(ref, line, journal)
                if w is not None:
                    rule = w[0]
                    waived.setdefault(rule, {})
                    waived[rule][ref] = waived[rule].get(ref, 0) + 1
                    continue
                why = u'нет ни в одном реестре'
                if ref in REMOVED_BY_DECISION:
                    why = u'%s; в КОДЕ ссылка на удалённую строку — снять' % REMOVED_BY_DECISION[ref]
                strict.append((rel, no, ref, why, line.strip()))
            if advisory:
                bare = set(bare_re.findall(line)) - hits
                n_bare_refs += len(bare)
                for ref in sorted(bare):
                    if ref not in ids and waive(ref, line, journal) is None:
                        loose.append((rel, no, ref, line.strip()))
    return strict, loose, waived, n_strict_refs, n_bare_refs, n_journals


def fmt_refs(counts):
    return u', '.join(u'%s ×%d' % (k, v) if v > 1 else k
                      for k, v in sorted(counts.items(), key=lambda kv: (kv[0][0], int(kv[0][1:]))))


def main(argv=None):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--repo', default=None, help=u'корень дерева (по умолчанию — от файла скрипта)')
    ap.add_argument('--root', action='append', default=None,
                    help=u'что обходить; можно повторять (по умолчанию: %s)' % u', '.join(DEFAULT_ROOTS))
    ap.add_argument('--ext', default=','.join(DEFAULT_EXTS),
                    help=u'расширения через запятую (по умолчанию: %s)' % u','.join(DEFAULT_EXTS))
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

    res = scan(repo, files, ids, prefixes, args.advisory)
    if res is None:
        return 2
    strict, loose, waived, n_strict, n_bare, n_journals = res

    n_waived = sum(sum(d.values()) for d in waived.values())
    all_waived = {}
    for d in waived.values():
        for k, v in d.items():
            all_waived[k] = all_waived.get(k, 0) + v

    if not args.quiet:
        print(u'реестры: %s' % u', '.join(
            u'%s — %d строк' % (k, len(v)) for k, v in sorted(per_file.items())))
        print(u'известных номеров всего: %d, буквы: %s'
              % (len(ids), u''.join(sorted(prefixes))))
        print(u'обойдено файлов: %d (из них журналов-записей %d), строгих ссылок проверено: %d'
              % (len(files), n_journals, n_strict))
        if args.advisory:
            print(u'голых токенов проверено дополнительно: %d' % n_bare)

    # явная строка об исключениях — всегда, и при --quiet тоже: приёмка её читает
    if n_waived:
        print(u'исключено по правилу: %d ссылок (%s)' % (n_waived, fmt_refs(all_waived)))
        for key, title in RULE_NAMES:
            if key in waived:
                print(u'  правило «%s»: %s' % (title, fmt_refs(waived[key])))
                if key in (u'removed', u'not_a_ref'):
                    src = REMOVED_BY_DECISION if key == u'removed' else NOT_A_REF
                    for ref in sorted(waived[key]):
                        print(u'    %s — %s' % (ref, src[ref]))
    else:
        print(u'исключено по правилу: 0 ссылок')

    if strict:
        print(u'')
        print(u'ОТКАЗ: ссылок в никуда — %d' % len(strict))
        for rel, no, ref, why, text in strict:
            print(u'  %s:%d: %s -> %s | %s' % (rel, no, ref, why, text[:160]))
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
