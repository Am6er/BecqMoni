# -*- coding: utf-8 -*-
"""Правка `TODO.md` по заданию из JSON — закрыть строки и вставить новые.

    python todo_edit.py задание.json [--todo TODO.md]

Задание:

    {
      "cols3":  ["A50"],
      "status": {"A89": "~~открыто~~ **СДЕЛАНО 04.09.2026**"},
      "append": {"A89": " ✅ **СДЕЛАНО.** Мерено пробой …"},
      "files":  {"A89": "`BecquerelMonitor/EfficiencyMaker/MaterialDatabase.cs`"},
      "after":  [["A104", "| **A105** | открыто | … | файл |"]]
    }

`status` заменяет ВТОРУЮ клетку, `append` дописывает в конец клетки ОПИСАНИЯ,
`files` заменяет последнюю клетку, `after` вставляет строки за названной.
`cols3` перечисляет строки без колонки файлов (три клетки вместо четырёх).

Почему так, а не `sed`:

⛔ Внутри описаний есть СВОИ трубы, и счёт клеток слева даёт не ту клетку.
   Считаем с конца: у строки из четырёх клеток описание кончается в
   `parts[-3]`, у строки из трёх — в `parts[-2]`. При внутренней трубе
   `parts[-3]` всё равно указывает на ХВОСТ описания, и дописка попадает
   куда надо.
⛔ Читаем и пишем с `newline=''`: питон рвёт текст на одиночном CR, а в
   реестре он встречается.
⚠ Скрипт НЕ идемпотентен: `append` дописывает при каждом запуске. Если запуск
   оборвался на выводе — проверь, записался ли файл, и только потом повторяй.
   Признак двойного применения — дописанный кусок стоит в строке дважды.
"""
import argparse
import io
import json
import sys


def row_index(lines, rid):
    head = '| **' + rid + '** |'
    for i, ln in enumerate(lines):
        if ln.startswith(head):
            return i
    return None


def cells_of(line, rid, cols3=False):
    parts = line.split('|')
    need = 5 if cols3 else 6
    if len(parts) < need:
        sys.exit('у строки %s клеток меньше ожидаемого: %d '
                 '(строка из трёх клеток? назови её в "cols3")' % (rid, len(parts)))
    if parts[0].strip() or parts[-1].strip():
        sys.exit('строка %s не обрамлена трубами' % rid)
    return parts


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('job')
    ap.add_argument('--todo', default='TODO.md')
    args = ap.parse_args()

    with io.open(args.job, 'r', encoding='utf-8') as f:
        job = json.load(f)

    with io.open(args.todo, 'r', encoding='utf-8', newline='') as f:
        lines = f.read().split('\n')

    cols3 = set(job.get('cols3', []))
    touched = (set(job.get('status', {})) | set(job.get('append', {}))
               | set(job.get('files', {})))

    for rid in sorted(touched):
        i = row_index(lines, rid)
        if i is None:
            sys.exit('НЕ НАЙДЕНА строка ' + rid)
        three = rid in cols3
        parts = cells_of(lines[i], rid, three)
        before = len(parts)
        tail = -2 if three else -3
        if rid in job.get('status', {}):
            parts[2] = ' ' + job['status'][rid] + ' '
        if rid in job.get('append', {}):
            parts[tail] = parts[tail].rstrip() + job['append'][rid] + ' '
        if rid in job.get('files', {}):
            if three:
                sys.exit('у %s нет клетки файлов, а она задана' % rid)
            parts[-2] = ' ' + job['files'][rid] + ' '
        if len(parts) != before:
            sys.exit('у %s изменилось число клеток — правка порвала строку' % rid)
        lines[i] = '|'.join(parts)
        print('правлена ' + rid)

    # Вставки — снизу вверх, чтобы уже найденные места не съезжали.
    plan = []
    for item in job.get('after', []):
        anchor, rows = item[0], item[1:]
        i = row_index(lines, anchor)
        if i is None:
            sys.exit('НЕ НАЙДЕНА опора ' + anchor)
        for r in rows:
            cells_of(r, 'новая за ' + anchor)
        plan.append((i, rows))
    for i, rows in sorted(plan, reverse=True):
        lines[i + 1:i + 1] = rows
        print('вставлено %d за строкой %d' % (len(rows), i + 1))

    with io.open(args.todo, 'w', encoding='utf-8', newline='') as f:
        f.write('\n'.join(lines))
    print('готово')


main()
