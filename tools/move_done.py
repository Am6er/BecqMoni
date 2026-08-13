# -*- coding: utf-8 -*-
"""
Перенести ПОЛНОСТЬЮ закрытые строки из `TODO.md` в `DONE.md`.

⛔ Переносит только по прямому указанию Amber (правило в шапках обоих файлов),
и только строки БЕЗ остатка: вычеркнутая строка вида «сделано, осталось …»
остаётся в `TODO.md`. Решение «остатка нет» инструмент НЕ принимает — номера
строк перечисляются в командной строке.

    python tools/move_done.py B4 S1 S37 ...        # покажет, что сделает
    python tools/move_done.py --apply B4 S1 S37 ...

Что делает с каждой строкой: снимает её из таблицы `TODO.md`, помечает номер
вычеркнутым и состояние «закрыто» (формат `DONE.md`) и кладёт в КОНЕЦ таблицы
того же раздела `DONE.md`. Раздел ищется по заголовку — они в двух файлах
названы одинаково с первого переноса.

После переноса печатает сверку: сколько строк было и стало в обоих файлах,
не задвоился ли номер и не потерялась ли строка. Правило «сверить машинно»
записано в шапке `TODO.md` — вот это и есть проверка.
"""

import io
import re
import sys


def read(path):
    return io.open(path, encoding="utf-8-sig").read()


def write(path, text):
    io.open(path, "w", encoding="utf-8-sig", newline="").write(text)


def row_number(line):
    """Номер строки таблицы: `| **B4** | ...` или `| B4 | ...` -> B4."""
    m = re.match(r"^\|\s*~?~?\*{0,2}([A-Z]\d+)\*{0,2}~?~?\s*\|", line)
    return m.group(1) if m else None


def sections(text):
    """Заголовок раздела -> (начало, конец) в строках файла."""
    lines = text.split("\n")
    out, current, start = {}, None, 0
    for i, line in enumerate(lines):
        if line.startswith("## "):
            if current is not None:
                out[current] = (start, i)
            current = line[3:].strip()
            start = i
    if current is not None:
        out[current] = (start, len(lines))
    return lines, out


def main():
    apply_it = "--apply" in sys.argv[1:]
    wanted = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not wanted:
        sys.exit(__doc__)

    todo_path, done_path = "TODO.md", "DONE.md"
    todo_lines, todo_sections = sections(read(todo_path))
    done_lines, done_sections = sections(read(done_path))

    todo_rows_before = sum(1 for l in todo_lines if row_number(l))
    done_rows_before = sum(1 for l in done_lines if row_number(l))

    # Где какая строка лежит и в каком разделе.
    found = {}
    for title, (start, end) in todo_sections.items():
        for i in range(start, end):
            number = row_number(todo_lines[i])
            if number in wanted:
                found[number] = (i, title)

    missing = [n for n in wanted if n not in found]
    if missing:
        sys.exit("нет таких строк в TODO.md: " + ", ".join(missing))

    done_numbers = set(filter(None, (row_number(l) for l in done_lines)))
    collided = [n for n in wanted if n in done_numbers]
    if collided:
        sys.exit("номер уже занят в DONE.md, перенос задвоил бы его: "
                 + ", ".join(collided))

    # Перенос: сверху вниз собираем, снизу вверх удаляем.
    moved = []
    for number in wanted:
        index, title = found[number]
        line = todo_lines[index]

        # Формат DONE.md: номер вычеркнут, состояние — «закрыто».
        cells = line.split("|")
        cells[1] = " ~~%s~~ " % cells[1].strip()
        cells[2] = " закрыто "
        moved.append((number, title, "|".join(cells)))

    for number in sorted(wanted, key=lambda n: -found[n][0]):
        del todo_lines[found[number][0]]

    # Вставка в конец таблицы своего раздела DONE.md. Конец таблицы — последняя
    # строка, начинающаяся с «|», иначе строка уедет за раздел.
    for number, title, line in moved:
        if title not in done_sections:
            sys.exit("в DONE.md нет раздела «%s» — куда класть %s?" % (title, number))
        _, done_sections_now = sections("\n".join(done_lines))
        start, end = done_sections_now[title]
        last = max(i for i in range(start, end) if done_lines[i].startswith("|"))
        done_lines.insert(last + 1, line)
        print("  %-5s -> DONE.md, раздел «%s»" % (number, title))

    todo_rows_after = sum(1 for l in todo_lines if row_number(l))
    done_rows_after = sum(1 for l in done_lines if row_number(l))

    print()
    print("TODO.md строк: %d -> %d (%+d)"
          % (todo_rows_before, todo_rows_after, todo_rows_after - todo_rows_before))
    print("DONE.md строк: %d -> %d (%+d)"
          % (done_rows_before, done_rows_after, done_rows_after - done_rows_before))

    ok = (todo_rows_before - todo_rows_after == len(wanted)
          and done_rows_after - done_rows_before == len(wanted))
    print("СОШЛОСЬ" if ok else "НЕ СОШЛОСЬ: перенесено не столько, сколько просили")
    if not ok:
        sys.exit(1)

    if not apply_it:
        print()
        print("--apply не задан: файлы не тронуты.")
        return

    write(todo_path, "\n".join(todo_lines))
    write(done_path, "\n".join(done_lines))
    print("ЗАПИСАНО")


if __name__ == "__main__":
    main()
