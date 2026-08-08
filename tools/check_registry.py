# -*- coding: utf-8 -*-
u"""Машинная проверка реестра задач: `TODO.md` и `DONE.md`.

Заведён по строкам S32 и T25. Обе просят одного: проверять реестр СЧЁТОМ, а не
глазами, потому что глазами уже не поймали трижды — одиннадцать находок из
тридцати трёх без строки, столкновение номеров S15/S16/S17 (~~W16~~), и
закрытие N8, обещавшее правку файла, которой в дереве не было ни в одном
коммите (S32).

Проверок три.

**1. Столкновения номеров.** Номер обязан быть уникален внутри файла. Между
`TODO.md` и `DONE.md` переиспользование сегодня есть и оставлено сознательно
(T25) — оно печатается предупреждением, а не ошибкой: трогать номера, на
которые ссылаются коммиты, дороже, чем жить с ними. Но новых заводить нельзя,
и правило простое: номер берётся максимальным по ОБОИМ файлам.

**2. Ссылки на файлы.** Каждая ссылка `[…](путь)` и каждое имя файла в
обратных кавычках должны разрешаться в дереве. Ссылка на удалённый файл — это
не всегда ошибка (строка может рассказывать, как его удаляли), поэтому
печатается списком к глазам, а не валит прогон.

Отдельно и ГРОМЧЕ: файл, который на диске есть, а в репозитории НЕТ. Первая
редакция проверки такие пропускала — искала по дереву каталогов, — и это была
та же слепота, что у N8: работа лежит у одного человека на машине, реестр на
неё ссылается, а из репозитория её не видно вовсе. Проверяется по
`git ls-files`, потому что «есть на диске» и «есть в проекте» — разные вещи.

**3. Имена из кода.** Каждое имя в обратных кавычках, похожее на символ
(класс, метод, поле, ключ), должно где-то в дереве встречаться. Имя, которого
нет, — признак либо закрытия без правки, либо переименования, за которым
реестр не пошёл.

ЧЕГО ЭТА ПРОВЕРКА НЕ ЛОВИТ, и это надо понимать. Она проверяет, что названное
СУЩЕСТВУЕТ, но не что обещанное СДЕЛАНО. Ровно на этом прошла N8:
`fill_intensity.py` лежал на месте, а выходов, которые он должен был проставить,
в конфиге не было. Строку, обещающую ИЗМЕНЕНИЕ ДАННЫХ, приходится проверять
самим артефактом — счётом строк файла или таблицы. Такие проверки живут рядом
с данными (`tools/nucdb/check_edges.py`, сверка в конце импортёров), а сюда
вынесено то, что общее для всех строк.

    python tools/check_registry.py [--root <каталог>]

Выход 1 — есть столкновение номеров внутри файла либо имя из кода, которого
нет в дереве. Остальное печатается к глазам.
"""
import argparse
import collections
import io
import os
import re
import subprocess
import sys

ROW = re.compile(r"^\|\s*~*\**~*\s*([A-Z]{1,2}\d{1,3})\b(.*)$")
LINK = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
CODE = re.compile(r"`([^`]+)`")
FILEY = re.compile(r"\.(cs|py|ps1|md|xml|sqlite|resx|csproj|tsv|csv|json)$")
SYMBOL = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")

# Имена, которые НЕ наши: каталоги и модули чужих поставок, ключи чужих
# программ. Ищутся они в чужом дереве, и отсутствие у нас — не находка.
FOREIGN = {"Ttb", "Elib", "ENSDF2", "MDATX3", "FCOMP", "Epdl97", "Glecs",
           "ECCBINDX", "NuclideMaster", "TCCFCALC", "SpecUtils"}

# Вне репозитория НАРОЧНО — и почему именно. Причина обязательна: исключение
# без причины через полгода неотличимо от забытой недоделки, а это ровно тот
# способ потерять проблему, против которого заведена вся проверка.
OUTSIDE_ON_PURPOSE = {
    "claude.md": u"личные указания агенту, в .gitignore; T2 как раз про то, "
                 u"что правила «для всех» так хранить нельзя",
    "agents.md": u"личные указания сборки у сопровождающего, в .gitignore "
                 u"(CLAUDE.md, «Build»)",
}


def read_rows(path):
    rows = []
    with io.open(path, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            m = ROW.match(line)
            if m:
                rows.append((m.group(1), m.group(2), n))
    return rows


def build_index(root):
    """Имена всех файлов дерева (без .git и выходных каталогов)."""
    names = collections.defaultdict(list)
    for base, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs
                   if d not in (".git", "packages", "obj") and not d.startswith("bin")]
        for f in files:
            names[f.lower()].append(os.path.relpath(os.path.join(base, f), root))
    return names


def tracked_set(root):
    """Пути, которые ЗНАЕТ git, — в нижнем регистре, и путём, и именем."""
    out = subprocess.check_output(["git", "ls-files"], cwd=root)
    paths, names = set(), set()
    for name in out.decode("utf-8", "replace").split("\n"):
        name = name.strip()
        if name:
            paths.add(name.lower())
            names.add(os.path.basename(name).lower())
    return paths, names


def tracked_text(root):
    """Содержимое отслеживаемых текстовых файлов одной строкой на поиск имён."""
    out = subprocess.check_output(["git", "ls-files"], cwd=root)
    blob = []
    for name in out.decode("utf-8", "replace").split("\n"):
        name = name.strip()
        if not name or FILEY.search(name) is None:
            continue
        path = os.path.join(root, name.replace("/", os.sep))
        try:
            with io.open(path, encoding="utf-8", errors="replace") as f:
                blob.append(f.read())
        except (IOError, OSError):
            continue
    return "\n".join(blob)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", default=os.path.dirname(os.path.dirname(
        os.path.abspath(__file__))))
    a = p.parse_args()
    root = a.root
    out = io.open(1, "w", encoding="utf-8", closefd=False)
    bad = 0

    files = {"TODO.md": read_rows(os.path.join(root, "TODO.md")),
             "DONE.md": read_rows(os.path.join(root, "DONE.md"))}

    # --- 1. номера ------------------------------------------------------
    out.write(u"# Номера\n\n")
    seen = {}
    for name, rows in files.items():
        counts = collections.Counter(num for num, _, _ in rows)
        dup = sorted(n for n, c in counts.items() if c > 1)
        out.write(u"%-8s строк %3d, столкновений внутри файла: %s\n"
                  % (name, len(rows), u", ".join(dup) if dup else u"нет"))
        if dup:
            bad += len(dup)
        seen[name] = set(counts)

    shared = sorted(seen["TODO.md"] & seen["DONE.md"])
    out.write(u"переиспользовано между файлами: %s\n"
              % (u", ".join(shared) if shared else u"нет"))
    if shared:
        out.write(u"  (T25: оставлено сознательно, новых так заводить нельзя —\n"
                  u"   номер берётся максимальным по обоим файлам)\n")

    # какой номер следующий у каждой серии
    nxt = collections.defaultdict(int)
    for rows in files.values():
        for num, _, _ in rows:
            series = re.match(r"^([A-Z]{1,2})(\d+)$", num)
            nxt[series.group(1)] = max(nxt[series.group(1)], int(series.group(2)))
    out.write(u"следующий свободный номер: %s\n\n"
              % u", ".join(u"%s%d" % (s, n + 1) for s, n in sorted(nxt.items())))

    # --- 2. ссылки на файлы ---------------------------------------------
    index = build_index(root)
    tracked_paths, tracked_names = tracked_set(root)
    untracked = collections.defaultdict(set)
    out.write(u"# Ссылки на файлы, которых нет в дереве\n\n")
    missing_any = False
    for name, rows in files.items():
        for num, text, line in rows:
            targets = set()
            for t in LINK.findall(text):
                t = t.split("#")[0].strip()
                if t and not t.startswith("http"):
                    targets.add(t)
            for c in CODE.findall(text):
                c = c.strip()
                if FILEY.search(c) and " " not in c:
                    targets.add(c)
            lost = []
            for t in targets:
                key = t.replace("\\", "/").lstrip("./").lower()
                base = os.path.basename(t).lower()
                known_to_git = key in tracked_paths or base in tracked_names
                on_disk = (os.path.exists(os.path.join(root, t.replace("/", os.sep)))
                           or base in index)
                if known_to_git or base in OUTSIDE_ON_PURPOSE:
                    continue
                if on_disk:
                    untracked[name].add((num, line, t))
                    continue
                lost.append(t)
            if lost:
                missing_any = True
                out.write(u"  %-8s %-5s строка %-4d %s\n"
                          % (name, num, line, u", ".join(sorted(lost))))
    if not missing_any:
        out.write(u"  нет\n")

    out.write(u"\n# Ссылки на файлы, которых НЕТ В РЕПОЗИТОРИИ (лежат только на диске)\n\n")
    if untracked:
        out.write(u"  Это случай N8: реестр ссылается на работу, которой из\n"
                  u"  репозитория не видно. Либо закоммитить, либо не ссылаться.\n")
        for name in sorted(untracked):
            for num, line, t in sorted(untracked[name], key=lambda r: r[1]):
                out.write(u"  %-8s %-5s строка %-4d %s\n" % (name, num, line, t))
        bad += sum(len(v) for v in untracked.values())
    else:
        out.write(u"  нет\n")

    out.write(u"\n# Вне репозитория НАРОЧНО\n\n")
    for name in sorted(OUTSIDE_ON_PURPOSE):
        out.write(u"  %-12s %s\n" % (name, OUTSIDE_ON_PURPOSE[name]))
    out.write(u"\n")

    # --- 3. имена из кода -----------------------------------------------
    blob = tracked_text(root)
    out.write(u"# Имена из кода, которых нет в дереве\n\n")
    lost_any = False
    for name, rows in files.items():
        for num, text, line in rows:
            for c in CODE.findall(text):
                c = c.strip()
                if FILEY.search(c) or u" " in c or len(c) < 4:
                    continue
                head = c.split(u"(")[0].split(u"=")[0].strip()
                if not SYMBOL.match(head):
                    continue
                probe = head.split(u".")[-1]
                if len(probe) < 4 or probe in FOREIGN or head in FOREIGN:
                    continue
                if probe not in blob:
                    lost_any = True
                    bad += 1
                    out.write(u"  %-8s %-5s строка %-4d %s\n"
                              % (name, num, line, head))
    if not lost_any:
        out.write(u"  нет\n")

    out.write(u"\n%s\n" % (u"РЕЕСТР ЧИСТ" if bad == 0
                           else u"НАХОДОК: %d" % bad))
    out.flush()
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
