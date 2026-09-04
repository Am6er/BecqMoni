# -*- coding: utf-8 -*-
"""Сверка `TODO.md` со снимком, снятым в начале захода.

    python todo_check.py <снимок> [--todo TODO.md] [--tally] [--series A]

Без ключей отвечает на один вопрос: **не потеряли ли строку**. Это главное,
потому что правка реестра идёт параллельно чужим заходам, и пропажа строки
не видна ни в диффе (он большой), ни глазом.

Что проверяется:

* строки не пропали и не задвоились;
* текст файла ВНЕ строк реестра не тронут — заголовки, пояснения, таблица
  «так не делать» должны остаться в точности;
* `--tally` даёт счёт захода: закрыто, заведено, осталось открытым.

⚠ Сравнение НЕ построчное. Вставка строки сдвигает всё, что ниже, и наивный
`zip` объявит расхождением полфайла. Считаем множествами и мешками.

Код возврата: 0 — цел, 1 — есть потери или задвоения.
"""
import argparse
import collections
import io
import re
import sys

ROW = re.compile(r'\| \*\*([AT]\d+)\*\* \|')
CLOSED = ('~~открыто~~', 'ЗАКРЫТА', 'ОТМЕНЕНА')


def read(path):
    with io.open(path, 'r', encoding='utf-8', newline='') as f:
        return f.read().split('\n')


def rows(lines):
    out = {}
    dup = collections.Counter()
    for ln in lines:
        m = ROW.match(ln)
        if m:
            dup[m.group(1)] += 1
            out[m.group(1)] = ln
    return out, [k for k, v in dup.items() if v > 1]


def is_closed(line):
    cells = line.split('|')
    st = cells[2] if len(cells) > 2 else ''
    return any(w in st for w in CLOSED)


def key(rid):
    return (rid[0], int(rid[1:]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('snapshot')
    ap.add_argument('--todo', default='TODO.md')
    ap.add_argument('--tally', action='store_true')
    ap.add_argument('--series', default=None, help='считать только эту серию, напр. A')
    args = ap.parse_args()

    a, b = read(args.snapshot), read(args.todo)
    ra, dupa = rows(a)
    rb, dupb = rows(b)

    lost = sorted(set(ra) - set(rb), key=key)
    new = sorted(set(rb) - set(ra), key=key)

    # Текст вне строк реестра — мешком, а не построчно.
    na = collections.Counter(x for x in a if not ROW.match(x))
    nb = collections.Counter(x for x in b if not ROW.match(x))
    drift = sum(((na - nb) + (nb - na)).values())

    print('строк было %d, стало %d' % (len(ra), len(rb)))
    print('пропало: %s' % (' '.join(lost) if lost else 'нет'))
    print('задвоено: %s' % (' '.join(sorted(set(dupa) | set(dupb))) or 'нет'))
    print('прочий текст файла тронут в %d строках' % drift)

    if args.tally:
        sel = (lambda r: r.startswith(args.series)) if args.series else (lambda r: True)
        old_open = [k for k in ra if not is_closed(ra[k])]
        closed_old = [k for k in old_open if k in rb and is_closed(rb[k])]
        closed_new = [k for k in new if is_closed(rb[k])]
        still_new = [k for k in new if not is_closed(rb[k])]
        print()
        print('ЗАКРЫТО за заход: %d' % (len(closed_old) + len(closed_new)))
        print('  было открыто до захода: %d  %s'
              % (len(closed_old), ' '.join(sorted(closed_old, key=key))))
        print('  заведено и закрыто тут же: %d  %s'
              % (len(closed_new), ' '.join(sorted(closed_new, key=key))))
        print('НОВЫХ строк: %d, из них осталось открытыми: %d'
              % (len(new), len(still_new)))
        oa = [k for k in ra if not is_closed(ra[k]) and sel(k)]
        ob = [k for k in rb if not is_closed(rb[k]) and sel(k)]
        tag = ('серия ' + args.series) if args.series else 'всего'
        print('открытых (%s): было %d -> стало %d (%+d)'
              % (tag, len(oa), len(ob), len(ob) - len(oa)))

    bad = bool(lost or dupa or dupb)
    if bad:
        print()
        print('⛔ РЕЕСТР ПОВРЕЖДЁН — чинить точечно, файл целиком НЕ откатывать')
    sys.exit(1 if bad else 0)


main()
