# -*- coding: utf-8 -*-
u"""Сверка раздела «чего не хватает» (`database/scheme.md`, §9а) с реестром.

Заведена по строке T26. Причина там записана прямо: список «брать неоткуда»
устарел ЗА ДВОЕ СУТОК и молча — «дифференциальные сечения» закрылись A-1,
«схемы уровней вне распада» закрылись D-2, а третья строка оказалась не
«неоткуда», а «не искали» (N15). Текст поправили руками, но НИЧЕГО не мешало
ему разойтись снова: ни одна строка §9а не была связана с номером в `TODO.md`,
и утверждение «этого нет нигде» ничем не проверялось.

Проверок две.

**1. Каждая открытая строка §9а обязана называть живой номер.** Строка либо
помечена закрытой (`✔`), либо помечена решением НЕ делать (`⛔` — тогда причина
лежит в таблице «Чего делать НЕ надо» в `TODO.md`), либо ссылается на задачу —
тогда видно, кто и где эту дыру закрывает. Строка без того, другого и третьего —
утверждение без владельца: никто не узнает, что оно устарело. Ссылка
засчитывается в трёх видах, все три уже встречаются в тексте: `TODO M1`,
`(D11)` и `~~F24~~`.

Почему не «любое имя вида буква+цифра»: в §9а сплошь оболочки (`M1`, `L1-3`,
`K/L/M`) и нуклиды (`Ba-137`), и наивный поиск принял бы оболочку M1 за задачу
M1. Ссылка обязана быть НАЗВАНА ссылкой — это и делает проверку возможной.

**2. Ни одна ссылка не должна вести в пустоту.** Номер, которого нет ни в
`TODO.md`, ни в `DONE.md`, — след переименования или опечатка.

Печатается к глазам, а не валит прогон: ссылка на строку из `DONE.md` у ОТКРЫТОЙ
дыры §9а — это либо «дыру закрыли, а здесь не отметили», либо «закрыли одно, а
дыра про другое». Разобрать это может только человек.

    python tools/check_scheme_gaps.py [--root <каталог>]

Выход 1 — есть строка без владельца либо ссылка в пустоту.
"""
import argparse
import io
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from check_registry import read_rows          # noqa: E402  общий разбор реестра

SECTION = re.compile(r"^##\s+9а\.")
NEXT_SECTION = re.compile(r"^##\s+(?!#)")
GAP_ROW = re.compile(r"^\|\s*\**\s*([A-F])-(\d{1,2})\s*\**\s*\|(.*)$")
# Ссылка на задачу, названная ссылкой: «TODO M1», «(D11)», «~~F24~~».
REFS = (re.compile(r"TODO\s+`?([A-Z]{1,2}\d{1,3})`?"),
        re.compile(r"~~([A-Z]{1,2}\d{1,3})~~"),
        re.compile(r"\(`?([A-Z]{1,2}\d{1,3})`?\)"))
CLOSED = u"✔"
NOTDO = u"⛔"       # решено НЕ делать; причина — в «Чего делать НЕ надо» TODO.md


def section_lines(path):
    """Строки §9а с номерами: от заголовка до следующего раздела того же уровня."""
    out, inside = [], False
    with io.open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            if SECTION.match(line):
                inside = True
                continue
            if inside and NEXT_SECTION.match(line):
                break
            if inside:
                out.append((n, line))
    return out


def refs_of(text):
    found = []
    for pat in REFS:
        found.extend(pat.findall(text))
    return found


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", default=os.path.dirname(os.path.dirname(
        os.path.abspath(__file__))))
    a = p.parse_args()
    root = a.root
    out = io.open(1, "w", encoding="utf-8", closefd=False)

    todo = {num for num, _, _ in read_rows(os.path.join(root, "TODO.md"))}
    done = {num for num, _, _ in read_rows(os.path.join(root, "DONE.md"))}
    known = todo | done

    lines = section_lines(os.path.join(root, "database", "scheme.md"))
    if not lines:
        out.write(u"§9а не найден — проверять нечего\n")
        return 1

    rows, orphan, dangling, stale = [], [], [], []
    for n, line in lines:
        m = GAP_ROW.match(line)
        if not m:
            continue
        num, text = u"%s-%s" % (m.group(1), m.group(2)), m.group(3)
        rows.append(num)
        names = refs_of(text)
        closed = CLOSED in text or NOTDO in text
        for r in names:
            if r not in known:
                dangling.append((num, n, r))
        if not closed and not names:
            orphan.append((num, n))
        if not closed:
            for r in names:
                if r in done and r not in todo:
                    stale.append((num, n, r))

    # Ссылки в прозе того же раздела — проверяются только на существование.
    for n, line in lines:
        if GAP_ROW.match(line):
            continue
        for r in refs_of(line):
            if r not in known:
                dangling.append((u"текст", n, r))

    out.write(u"# §9а: строки и владельцы\n\n")
    out.write(u"строк в таблицах: %d\n\n" % len(rows))

    out.write(u"# Открытые строки, которые не называют задачи\n\n")
    if orphan:
        out.write(u"  Утверждение «этих данных нет» без номера устаревает молча —\n"
                  u"  ровно так §9а разошёлся с реестром за двое суток (T26).\n")
        for num, n in orphan:
            out.write(u"  %-5s строка %d\n" % (num, n))
    else:
        out.write(u"  нет\n")

    out.write(u"\n# Ссылки в пустоту\n\n")
    if dangling:
        for num, n, r in dangling:
            out.write(u"  %-5s строка %-5d -> %s\n" % (num, n, r))
    else:
        out.write(u"  нет\n")

    out.write(u"\n# Открытые строки, чья задача уже в DONE.md\n\n")
    if stale:
        out.write(u"  К глазам: либо дыра закрыта и здесь не отмечена, либо\n"
                  u"  закрыто другое. Прогон этим не валится.\n")
        for num, n, r in stale:
            out.write(u"  %-5s строка %-5d -> %s (в DONE.md)\n" % (num, n, r))
    else:
        out.write(u"  нет\n")

    bad = len(orphan) + len(dangling)
    out.write(u"\n%s\n" % (u"§9а СОШЁЛСЯ С РЕЕСТРОМ" if bad == 0
                           else u"НАХОДОК: %d" % bad))
    out.flush()
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
