# -*- coding: utf-8 -*-
"""Сверка `TODO.md` со снимком, снятым в начале захода.

    python todo_check.py <снимок> [--todo TODO.md] [--tally] [--series A]
                                  [--strict] [--allow-new A1,A2] [--allow-changed A3]

Без ключей отвечает на один вопрос: **не потеряли ли строку**. Это главное,
потому что правка реестра идёт параллельно чужим заходам, и пропажа строки
не видна ни в диффе (он большой), ни глазом.

Что проверяется:

* строки не пропали и не задвоились — ВСЕХ серий (`[A-Z]+\\d+`, с учётом
  вычеркнутых `~~**S32**~~`; до 12.09.2026 сторож видел только `A` и `T` —
  ~~`T206`~~, и `--tally` считал по двум сериям из тринадцати);
* текст файла ВНЕ строк реестра не тронут — заголовки, пояснения, таблица
  «так не делать» должны остаться в точности;
* (`T170`) ТЕКСТ существующих строк: у каждого номера сверяются клетка
  состояния и клетка описания со снимком, изменившиеся перечисляются
  поимённо; добавленные номера, которых в снимке не было, — тоже. Так видна
  строка, переписанная или заведённая ТРЕТЬЕЙ рукой: 05.09.2026 сосед вымел
  коммитом чужие `A168`–`A170` и переписал статус `A145`, число строк
  сошлось, и сторож молчал;
* `--tally` даёт счёт захода: закрыто, заведено, осталось открытым — по всем
  сериям, `--series` сужает до одной (точное имя серии: `A` не значит `AMBER`).

Код возврата: 0 — цел, 1 — есть потери или задвоения. С ключом `--strict`
код 1 дают ещё и изменённые или новые строки, не названные в
`--allow-changed=` / `--allow-new=` (перечень через запятую): перед коммитом
распорядитель называет СВОИ правки, и всё, что сверх них, — чужая рука.
Без `--strict` изменённые и новые только перечисляются (обратная
совместимость: скрипт зовут после каждой правки, и своя правка не должна
ронять сверку).

⚠ Сравнение НЕ построчное. Вставка строки сдвигает всё, что ниже, и наивный
`zip` объявит расхождением полфайла. Считаем множествами и мешками.
"""
import argparse
import collections
import io
import re
import sys

# T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass

# Номер строки реестра — любая серия, жирный, возможно вычеркнутый снаружи
# (`~~**T3**~~`) или внутри (`**~~T3~~**`). Группа 3 — сам номер.
ROW = re.compile(r'\| (~~)?\*\*(~~)?([A-Z]+\d+)(~~)?\*\*(~~)? \|')
# `T127`: раздел «Отложенные прогоны» СОБИРАЕТСЯ из строк реестра и повторяет
# их номера в той же разметке. Всё, что ниже его заголовка, — сводка о строках,
# а не сами строки; сверяет её `tools/check_pending_runs.py`. Без обрезки сторож
# печатает «задвоено» на исправном файле — поймано 06.09.2026.
TAIL = u'## Отложенные прогоны'
CLOSED = ('~~открыто~~', 'ЗАКРЫТА', 'ОТМЕНЕНА')


def read(path):
    with io.open(path, 'r', encoding='utf-8', newline='') as f:
        return f.read().split('\n')


def rows(lines):
    out = {}
    dup = collections.Counter()
    for ln in lines:
        if ln.startswith(TAIL):
            break
        m = ROW.match(ln)
        if m:
            dup[m.group(3)] += 1
            out[m.group(3)] = ln
    return out, [k for k, v in dup.items() if v > 1]


def is_closed(line):
    cells = line.split('|')
    st = cells[2] if len(cells) > 2 else ''
    return any(w in st for w in CLOSED)


def series_of(rid):
    return re.match(r'[A-Z]+', rid).group(0)


def key(rid):
    m = re.match(r'([A-Z]+)(\d+)$', rid)
    return (m.group(1), int(m.group(2)))


def cells(line):
    u"""(состояние, описание) строки реестра.

    Описание — от третьей клетки до клетки файлов; у строки из трёх клеток
    файлов нет. Внутри описаний есть СВОИ трубы, поэтому описание берётся
    как всё между второй клеткой и последней (`parts[-2]`), а не по счёту
    слева — иначе строка с внутренней трубой сравнивалась бы по обрезку.
    """
    parts = line.split('|')
    st = parts[2] if len(parts) > 2 else ''
    body = '|'.join(parts[3:]) if len(parts) > 3 else ''
    return st.strip(), body.strip()


def ids_arg(text):
    return set(x.strip() for x in (text or '').split(',') if x.strip())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('snapshot')
    ap.add_argument('--todo', default='TODO.md')
    ap.add_argument('--tally', action='store_true')
    ap.add_argument('--series', default=None, help='считать только эту серию, напр. A')
    ap.add_argument('--strict', action='store_true',
                    help='изменённые и новые строки сверх --allow-* роняют код')
    ap.add_argument('--allow-changed', default='', help='свои правки: A89,T142')
    ap.add_argument('--allow-new', default='', help='свои новые строки: A105')
    args = ap.parse_args()

    a, b = read(args.snapshot), read(args.todo)
    ra, dupa = rows(a)
    rb, dupb = rows(b)

    lost = sorted(set(ra) - set(rb), key=key)
    new = sorted(set(rb) - set(ra), key=key)
    # (`T170`) Текст строк, живущих в обоих файлах: клетка состояния и клетка
    # описания. Сравнивается сам текст, а не хэш: хэш ничего не добавляет,
    # а перечень номеров нужен в любом случае.
    changed = sorted((k for k in set(ra) & set(rb) if cells(ra[k]) != cells(rb[k])), key=key)

    # Текст вне строк реестра — мешком, а не построчно.
    a = a[:next((i for i, x in enumerate(a) if x.startswith(TAIL)), len(a))]
    b = b[:next((i for i, x in enumerate(b) if x.startswith(TAIL)), len(b))]
    na = collections.Counter(x for x in a if not ROW.match(x))
    nb = collections.Counter(x for x in b if not ROW.match(x))
    drift = sum(((na - nb) + (nb - na)).values())

    print('строк было %d, стало %d' % (len(ra), len(rb)))
    print('пропало: %s' % (' '.join(lost) if lost else 'нет'))
    print('задвоено: %s' % (' '.join(sorted(set(dupa) | set(dupb))) or 'нет'))
    print('изменено (состояние или описание): %d  %s' % (len(changed), ' '.join(changed)))
    print('добавлено: %d  %s' % (len(new), ' '.join(new)))
    print('прочий текст файла тронут в %d строках' % drift)

    if args.tally:
        sel = (lambda r: series_of(r) == args.series) if args.series else (lambda r: True)
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
        if not args.series:
            per = collections.OrderedDict()
            for k in sorted(set(oa) | set(ob), key=key):
                s = series_of(k)
                was, now = per.get(s, (0, 0))
                per[s] = (was + (k in oa), now + (k in ob))
            print('  по сериям: ' + ', '.join('%s %d -> %d' % (s, w, n) for s, (w, n) in per.items()))

    bad = bool(lost or dupa or dupb)
    if args.strict:
        alien_changed = [k for k in changed if k not in ids_arg(args.allow_changed)]
        alien_new = [k for k in new if k not in ids_arg(args.allow_new)]
        if alien_changed or alien_new:
            bad = True
            print()
            print('⛔ СТРОГО: изменено сверх названного: %s; добавлено сверх названного: %s'
                  % (' '.join(alien_changed) or 'нет', ' '.join(alien_new) or 'нет'))
    if bad:
        print()
        print('⛔ РЕЕСТР ПОВРЕЖДЁН — чинить точечно, файл целиком НЕ откатывать')
    sys.exit(1 if bad else 0)


main()
