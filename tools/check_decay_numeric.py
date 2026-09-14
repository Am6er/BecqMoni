# -*- coding: utf-8 -*-
u"""Сторож `D35`: числовые колонки `decay_radiations` хранятся ТЕКСТОМ, и
сравнение по ним молча идёт лексикографически.

ЗАЧЕМ. `energy`, `intensity`, `intensity_unc`, `energy_unc`, `endpoint` в
`nucdb.decay_radiations` объявлены `text` — все 66 265 строк. SQLite сравнивает
их как строки, отказа при этом нет, есть ДРУГОЙ ОТВЕТ: `where energy > 100`
даёт 65 164 строки, `where energy_num > 100` — 53 067, расхождение 12 149
(18 %). Найдено 25.08.2026 при разборе `S94`, когда вывод чуть не сделали по
запросу `where energy > 5` и получили бы ноль K-линий там, где их шесть.

`cast(energy as real)` лекарством не является: 6 987 значений — это ДИАПАЗОНЫ
вида `114.332 - 114.560`, у них `cast` молча берёт первое число, а у `(8074)`
даёт 0. Правильный ответ — готовые колонки `energy_num` / `intensity_num`
(`real`), у которых диапазон записан серединой, а точность ВЫШЕ текста (у 4 271
строки текст `996` при числе `995.5`). Подробности — `database/scheme.md`, §2,
врезка «Ловушка `D35`».

ЧТО СУДИТСЯ. Запрос к `decay_radiations` в оснастке, приложении и пробах, в
котором ТЕКСТОВАЯ колонка стоит под знаком сравнения, под `order by`, под
`min`/`max`/`sum`/`avg` либо под `cast(... as real|float|numeric)`. Присвоение,
`is null`, `= '…'` и печать — не судятся: текст как текст брать законно.

⚠ **Сторож судит ИСХОДНИК, а не базу.** Он не открывает `nucdb.sqlite` вовсе:
дефект здесь не в данных, а в том, что кто-то напишет по ним арифметику. Тип
колонок проверяется отдельно, запросом из строки `D35`.

    python tools/check_decay_numeric.py [--root <каталог>] [--self-test]

Выход 0 — таких мест нет; 1 — есть, с именем файла и строкой; 2 — сам сторож
не смог отработать (например, `--self-test` не прошёл).
"""
import argparse
import io
import os
import re
import sys

#: Колонки `decay_radiations`, объявленные `text`. У `energy` и `intensity`
#: есть числовая пара; у остальных — нет, и по ним арифметика невозможна вовсе.
TEXT_COLUMNS = (u"energy", u"intensity", u"intensity_unc", u"energy_unc",
                u"endpoint", u"endpoint_unc")

#: Чем заменять. Пусто — числовой пары нет, значение надо разбирать явно.
NUMERIC_PAIR = {
    u"energy": u"energy_num",
    u"intensity": u"intensity_num",
}

#: Расширения, в которых ищем SQL.
SUFFIXES = (u".py", u".cs")

#: Каталоги, куда не ходим.
SKIP_DIRS = (u".git", u"packages", u"obj", u"bin", u"__pycache__", u"handover",
             u"node_modules")

#: Имя таблицы в тексте запроса. Ищем ТОЛЬКО там, где она названа: колонка
#: `energy` встречается ещё в десятке чужих таблиц, и без привязки к таблице
#: сторож ловил бы их.
TABLE = re.compile(r"decay_radiations", re.IGNORECASE)


def _column_pattern(column):
    u"""Опасные употребления одной колонки.

    Колонка может стоять с квалификатором (`dr.energy`, `d.energy`) и в
    обратных кавычках (``  `energy`  ``) — всё это одно и то же место.
    """
    name = r"(?:[A-Za-z_]\w*\s*\.\s*)?`?" + column + r"`?"
    return (
        # сравнение: energy > 100, 100 <= dr.energy, energy between … and …
        re.compile(r"(?<![\w.])" + name + r"\s*(?:>=|<=|<>|!=|>|<)(?!=)",
                   re.IGNORECASE),
        re.compile(r"(?:>=|<=|<>|!=|>|<)\s*" + name + r"(?![\w.])",
                   re.IGNORECASE),
        re.compile(r"(?<![\w.])" + name + r"\s+between\b", re.IGNORECASE),
        # порядок и свёртки
        re.compile(r"\border\s+by\s+(?:[^,;'\"]*,\s*)*" + name + r"(?![\w.])",
                   re.IGNORECASE),
        re.compile(r"\b(?:min|max|sum|avg|total)\s*\(\s*" + name + r"\s*\)",
                   re.IGNORECASE),
        # приведение — оно и есть подмена числовой колонки текстовой
        re.compile(r"\bcast\s*\(\s*" + name
                   + r"\s+as\s+(?:real|float|numeric|integer|int)\b",
                   re.IGNORECASE),
    )


PATTERNS = [(c, _column_pattern(c)) for c in TEXT_COLUMNS]

#: Числовые колонки, которые НЕЛЬЗЯ спутать с текстовыми: `energy_num` кончается
#: на `_num`, и шаблон колонки `energy` его не берёт (стоит `(?![\w.])`).
#: Проверяется `--self-test`.
SELF_TEST_BAD = (
    u"select energy from decay_radiations where energy > 100",
    u"select * from decay_radiations order by energy",
    u'"select x from decay_radiations" + " where cast(energy as real) >= 5";',
    u"select max(intensity) from decay_radiations",
    u"select e from decay_radiations where dr.energy_unc <> ''",
    # склейка через строки — имя таблицы и сравнение в РАЗНЫХ строках
    u'db.execute(\n    "select x from decay_radiations"\n    " where energy >= 5")',
)
SELF_TEST_GOOD = (
    u"select energy_num from decay_radiations where energy_num > 100",
    u"select * from decay_radiations order by energy_num",
    u"select energy, intensity from decay_radiations where parent_nucid = $n",
    u"select max(intensity_num) from decay_radiations",
    u"select energy from ensdf_gammas where energy > 100",
    u"select energy from decay_radiations where energy is not null",
    # ⛔ СОСЕД, А НЕ ТОТ ЖЕ ЗАПРОС: имя таблицы стоит в предыдущем операторе,
    # и притягивать его к следующему нельзя (ловушка окна, 10.09.2026).
    u'a = db.execute("select energy_num from decay_radiations")\n'
    u'b = db.execute("select energy from ensdf_gammas where energy > 100")',
)


def statements(lines, csharp):
    u"""Разбить текст на ОПЕРАТОРЫ: [(текст оператора, номера его строк)].

    ⛔ ОКНО В НЕСКОЛЬКО СТРОК ЗДЕСЬ НЕ ГОДИТСЯ, и это измерено, а не
    предположено. Первая редакция сторожа искала имя таблицы в окне ±3 строки
    и на подброшенном дереве 10.09.2026 объявила плохим запрос
    `select energy from ensdf_gammas where energy > 100` — только потому, что
    `decay_radiations` стоял в СОСЕДНЕМ, ни при чём не бывшем запросе.
    Самопроверка этого не поймала: в её образцах соседей нет.

    Границу даёт сам язык. Питон продолжает оператор, пока не закрыты скобки;
    C# — пока не встретится `;` (а `{` и `}` разделяют тела, чтобы имя таблицы
    не переползало через весь метод). Кавычки при счёте скобок пропускаются:
    скобка внутри текста SQL — это текст, а не скобка.
    """
    out = []
    buf = []
    nums = []
    depth = 0
    for i, line in enumerate(lines, 1):
        buf.append(line)
        nums.append(i)
        quote = None
        for ch in line:
            if quote:
                if ch == quote:
                    quote = None
                continue
            if ch in u"\"'":
                quote = ch
            elif ch in u"([":
                depth += 1
            elif ch in u")]":
                depth = max(0, depth - 1)
        if depth > 0:
            continue
        tail = line.rstrip()
        if csharp and not (tail.endswith(u";") or tail.endswith(u"{")
                           or tail.endswith(u"}")):
            continue
        out.append((u"\n".join(buf), list(nums)))
        buf = []
        nums = []
    if buf:
        out.append((u"\n".join(buf), list(nums)))
    return out


def judge(text, csharp=False):
    u"""Опасные места в одном тексте: [(колонка, номер строки, строка)]."""
    found = []
    lines = text.split(u"\n")
    for body, nums in statements(lines, csharp):
        if not TABLE.search(body):
            continue
        for number in nums:
            line = lines[number - 1]
            for column, patterns in PATTERNS:
                for pattern in patterns:
                    if pattern.search(line):
                        found.append((column, number, line.strip()))
                        break
    return found


def self_test():
    u"""Положительный контроль: заведомо плохое ловится, заведомо доброе — нет."""
    bad = 0
    for sample in SELF_TEST_BAD:
        if not judge(sample):
            sys.stdout.write(u"САМОПРОВЕРКА: ПЛОХОЕ НЕ ПОЙМАНО: %s\n" % sample)
            bad += 1
    for sample in SELF_TEST_GOOD:
        hits = judge(sample)
        if hits:
            sys.stdout.write(u"САМОПРОВЕРКА: ДОБРОЕ ОБЪЯВЛЕНО ПЛОХИМ: %s -> %s\n"
                             % (sample, hits))
            bad += 1
    if bad:
        sys.stdout.write(u"САМОПРОВЕРКА ПРОВАЛЕНА: %d случаев\n" % bad)
        return 2
    sys.stdout.write(u"самопроверка: %d плохих поймано, %d добрых пропущено\n"
                     % (len(SELF_TEST_BAD), len(SELF_TEST_GOOD)))
    return 0


def walk(root):
    for base, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in files:
            if name.endswith(SUFFIXES):
                yield os.path.join(base, name)


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except AttributeError:
        pass
    parser = argparse.ArgumentParser()
    here = os.path.dirname(os.path.abspath(__file__))
    parser.add_argument("--root", default=os.path.dirname(here))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return self_test()

    # ⛔ Самопроверка идёт ВСЕГДА, а не по ключу. Сторож, который перестал
    # ловить, при коде 0 и находках 0 выглядит ровно как исправный, — это
    # повторяющаяся беда дерева, и лечится она только тем, что признак
    # проверяется каждым прогоном.
    if self_test():
        return 2

    me = os.path.abspath(__file__)
    hits = []
    for path in walk(args.root):
        if os.path.abspath(path) == me:
            continue
        try:
            text = io.open(path, encoding="utf-8").read()
        except (IOError, ValueError, UnicodeDecodeError):
            continue
        for column, line_no, line in judge(text, path.endswith(u".cs")):
            hits.append((os.path.relpath(path, args.root), line_no, column, line))

    if not hits:
        sys.stdout.write(u"текстовых колонок `decay_radiations` под арифметикой"
                         u" не найдено\n")
        return 0

    sys.stdout.write(u"ОСТАНОВ: текстовая колонка `decay_radiations` под"
                     u" сравнением/порядком — %d место(а)\n" % len(hits))
    for path, line_no, column, line in hits:
        pair = NUMERIC_PAIR.get(column)
        sys.stdout.write(u"  %s:%d  колонка `%s` -> брать %s\n"
                         % (path, line_no, column,
                            u"`%s`" % pair if pair
                            else u"разбор явным кодом, числовой пары нет"))
        sys.stdout.write(u"      %s\n" % line[:160])
    sys.stdout.write(u"почему — `database/scheme.md`, §2, врезка «Ловушка `D35`»\n")
    return 1


if __name__ == "__main__":
    sys.exit(main())
