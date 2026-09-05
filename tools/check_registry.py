# -*- coding: utf-8 -*-
u"""Машинная проверка реестра задач: `TODO.md` и `DONE.md`.

Заведён по строкам S32 и T25. Обе просят одного: проверять реестр СЧЁТОМ, а не
глазами, потому что глазами уже не поймали трижды — одиннадцать находок из
тридцати трёх без строки, столкновение номеров S15/S16/S17 (~~W16~~), и
закрытие N8, обещавшее правку файла, которой в дереве не было ни в одном
коммите (S32).

Проверок семь (нумерация ниже; 6 и 7 заведены 05.09.2026).

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

**4. Две копии `config/`.** В дереве отслеживаются ДВЕ копии поставочной
конфигурации: `config/` в корне и `BecquerelMonitor/config/`. В поставку и в
оснастку корпуса уходит ВТОРАЯ (`BecquerelMonitor.csproj`, `Content`/
`PublishFile`, путь относительно каталога проекта; `mk_appwd.ps1` копирует
оттуда же). Корневая не читается ни кодом, ни скриптами — но лежит рядом,
называется так же и уже увела измерение: потолок `S63` мерен по корневому
`NuclideDefinition.xml`, а корпус считался по поставочному (`T66`).

Проверка — храповик, а не уговор. Расхождения, которые есть СЕГОДНЯ и уже
разобраны, перечислены в `CONFIG_COPIES_KNOWN` вместе с обеими sha256.
Находкой считается всё, что из этого состояния вышло: новый разошедшийся
файл, файл в одной копии из двух, файл, ОТСЛЕЖИВАЕМЫЙ GIT-ОМ, НО ПРОПАВШИЙ С
ДИСКА, и ИЗМЕНЕНИЕ любой из сторон известного расхождения — потому что
разбирали не это. Сошедшийся файл печатается к глазам с просьбой снять его из
списка.

⛔ Слепота к УДАЛЕНИЮ измерена и закрыта 26.08.2026 (встречная проверка `T66`).
Список брался у `git ls-files`, а затем файлы молча отбрасывались условием
`os.path.exists`: файл, стёртый с диска в ОБЕИХ копиях, но живой в индексе,
давал НОЛЬ находок — менялось только напечатанное «24 файлов» на «23 файлов».
Опыт на своём git-репозитории в `%TEMP%`: было 0 находок, стало 2 (по одной на
копию); удаление в ОДНОЙ копии прежде давало 1 находку с ЧУЖИМ диагнозом
(«только в одной копии»), теперь — свою.

ЧЕГО ЭТА ПРОВЕРКА НЕ ЛОВИТ, и это надо понимать. Она проверяет, что названное
СУЩЕСТВУЕТ, но не что обещанное СДЕЛАНО. Ровно на этом прошла N8:
`fill_intensity.py` лежал на месте, а выходов, которые он должен был проставить,
в конфиге не было. Строку, обещающую ИЗМЕНЕНИЕ ДАННЫХ, приходится проверять
самим артефактом — счётом строк файла или таблицы. Такие проверки живут рядом
с данными (`tools/nucdb/check_edges.py`, сверка в конце импортёров), а сюда
вынесено то, что общее для всех строк.

    python tools/check_registry.py [--root <каталог>]

**5. Объявление действующей базы корпуса.** Смена базы — это ТРИ МЕСТА:
преамбула `TODO.md`, журнал корпуса и память агента, — и объявить в двух значит
НЕ объявить. Проверка заведена 03.09.2026 после ВТОРОГО подряд отставания на
две базы (27.08.2026 — снятая `out_v5`, 03.09.2026 — снятая `out_v9`). Оба раза
строка `grep` для этой сверки стояла в самой преамбуле, и оба раза её никто не
позвал: помнить о ней надо ровно тогда, когда занят другим. Два места из трёх
лежат в репозитории и сверяются строго; память — вне его, и проверяется, только
если найдена, а её отсутствие печатается словами.

**6. Форма графы состояния.** Заведена 05.09.2026 по `T100`. Графа бывает
нечитаемой машинно: слово «открыто» стоит в ней И вычеркнутым, И нет — у
`S102` и `T92` было «открыто ~~открыто~~ **СДЕЛАНО …**», у `T88` — «открыто,
**РЕШЕНИЕ Amber … ЕСТЬ** ~~открыто~~ **ИСПОЛНЕНО …**»: правка дописывала итог,
не вычёркивая прежнее слово. Для человека мелочь, для разбора — нет: сверка
«сколько строк открыто» считает такую строку открытой и закрытой сразу, а
триаж вычеркнутых её либо пропустит, либо возьмёт дважды. Правило простое и
третьего не даёт: слово состояния либо вычеркнуто целиком, либо не вычеркнуто
вовсе. Ловятся два вида — «и так и эдак» и слово, РАЗРЕЗАННОЕ маркерами
вычёркивания (`~~откры~~то`), которое не читается ни так, ни эдак.

⛔ Сторож, у которого нет доказанного ОТКАЗА, неотличим от ненаписанного: код
0, находок 0 (`T69`). Поэтому положительный контроль встроен и зовётся
`python tools/check_registry.py --selftest`: во ВРЕМЕННОЙ копии `TODO.md`
подсаживаются ШЕСТЬ порч — четыре графы (обе исторические формы `T100`,
разрезанное слово и «закрыто» обоими способами), задвоенный номер и одиночный
CR с лишней `|` в описании, — и контроль требует, чтобы находок по графе стало
ровно «чистые + четыре» и ровно в подставленных строках, задвоенный номер был
назван проверкой 1, а от CR и `|` не прибавилось ни строк, ни находок.
Оригинал `TODO.md` при этом не открывается на запись вовсе.

Проверка 2 покрыта тем же ключом с 06.09.2026 (`T223`): в описание ещё одной
строки копии подсаживаются битая ссылка, ссылка на файл, который лежит
только на диске, и имя из `OUTSIDE_ON_PURPOSE`; контроль требует, чтобы
первая была названа в списке «нет в дереве», вторая — в списке «НЕТ В
РЕПОЗИТОРИИ» с прибавкой к счёту ровно единицы, а третья не всплыла нигде.

⛔ Чтение реестра — ТОЛЬКО через `read_lines`: файл берётся нетронутым и режется
по переводу строки LF руками. Построчное чтение Python режет строку и по одиночному CR, а он
в описаниях реестра встречался: хвост строки терялся молча, номера строк после
него уезжали от `grep -n`.

**7. Заголовки `DONE.md` против их тел.** Заведена 05.09.2026 по `T102`.
Человек ищет по архиву `grep`-ом и читает заголовок ячейки; если заголовок
утверждает незнание («причина неизвестна», «не воспроизводится»), а тело той же
ячейки его снимает (блок ✅, «СНЯТА», «НАХОДКИ НЕТ»), поиск выдаёт
противоположное действительности — на `S82` так и споткнулся заход 27.08.2026.
Разбор живёт в `tools/check_done_headers.py` (свои `--scan` и `--selftest`),
здесь его находки считаются в итог. ⛔ `DONE.md` правит ТОЛЬКО Amber: проверка
называет строки, а замены текстом собирает тот, кто её позвал.

Выход 1 — есть столкновение номеров внутри файла, ссылка на файл, который лежит
только на диске, а не в репозитории, имя из кода, которого нет в дереве, находка храповика двух копий `config/`, расхождение объявлений
действующей базы, нечитаемая графа состояния, либо заголовок `DONE.md`,
противоречащий своему телу. Остальное печатается к глазам.
"""
import argparse
import collections
import glob
import hashlib
import io
import os
import re
import shutil
import subprocess
import sys
import tempfile

ROW = re.compile(r"^\|\s*~*\**~*\s*([A-Z]{1,2}\d{1,3})\b(.*)$")

# Объявление действующей базы корпуса. Привязка к НАЧАЛУ строки обязательна:
# без неё ловятся исторические упоминания внутри задач, и они законны.
BASE_IN_TODO = re.compile(
    u"^⛔ \\*\\*ДЕЙСТВУЮЩАЯ БАЗА КОРПУСА — `([^`]+)`,\\s*"
    u"(\\d{2}\\.\\d{2}\\.\\d{4})")
BASE_IN_CORPUS = re.compile(
    u"^## ✅ ДЕЙСТВУЮЩАЯ БАЗА: `([^`]+)`,\\s*(\\d{2}\\.\\d{2}\\.\\d{4})")
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
# Две копии конфига (`T66`). Слева путь внутри `config/`, справа пара sha256
# — (корневая копия, поставочная `BecquerelMonitor/config/`) на 25.08.2026.
# Это НЕ разрешение расходиться, а снимок разобранного состояния: пока строка
# `T66` открыта, эти три расхождения известны и описаны, любое другое — находка.
#
# Чем расходятся, коротко и числом (мерено 25.08.2026):
#   * NuclideDefinition.xml — корень 143 записи, поставка 152. У поставочной
#     есть поля `Sets` (64 записи) и `Chain` (52), у корневой их НЕТ ВООБЩЕ;
#     в корневой до сих пор `K40` без дефиса, в поставочной `K-40`. Выходы
#     `Tl-208` в поставке приведены к распаду РОДИТЕЛЯ ряда (2614 кэВ: 35.85
#     против 99.754), то есть числа несопоставимы напрямую.
#   * ROI/Obsidian Marinelli 0.5.xml — 34 точки кривой против 150.
#   * ROI/RadiaCode Marinelli 0.5.xml — то же, кривая старой длины.
CONFIG_COPIES = (u"config", os.path.join(u"BecquerelMonitor", u"config"))
CONFIG_COPIES_KNOWN = {
    u"NuclideDefinition.xml":
        (u"82cbe1717447cc1a32fab220e2a6c674a6452ebe5f020385f9a2ca720f06f812",
         u"7aaa0b01c9bd4a7621b8ed1f642b7efbe5833a4b8b3a86bd7b3156b714efa380"),
    u"ROI/Obsidian Marinelli 0.5.xml":
        (u"bee051b3fbf5c237acae6dbc9ea155ba207f126a1922a24cb1221f4bab64b764",
         u"b153cfb1df418a2ea5b2104920b77719b6cb9b7f2d6de6344bb439681bc6cc14"),
    u"ROI/RadiaCode Marinelli 0.5.xml":
        (u"ba12284b442620aca1f347316719ad27c91196cc768596f05512eaaaaafb034d",
         u"2b0bd97b8b78d366b5f4f7bfdbcba1d5548d0363ff1d1bf5e24ef19f51c6ad39"),
}

OUTSIDE_ON_PURPOSE = {
    "claude.md": u"личные указания агенту, в .gitignore; T2 как раз про то, "
                 u"что правила «для всех» так хранить нельзя",
    "agents.md": u"личные указания сборки у сопровождающего, в .gitignore "
                 u"(CLAUDE.md, «Build»)",
    # Три записи ниже — T223 (06.09.2026): до них проверка 2 краснела на
    # десяти ссылках из пятнадцати, и все десять были на эти три имени.
    ".appwd.json": u"отметка оснастки корпуса, которую пишет сам "
                   u"appwd_plan.ps1 в каждый рабочий каталог "
                   u"tools/CORPUS/scripts/wd_*/ (каталоги в .gitignore); "
                   u"реестр ссылается на неё как на признак, а не на файл "
                   u"проекта (T63, T66, T68, T80, T84)",
    "settings.local.json": u"личный файл настроек агента Amber "
                           u".claude/settings.local.json, в .gitignore "
                           u"(T218)",
    "effprobe.xml": u"конфигурация прибора, которую пробы EffMaker кладут в "
                    u"свой рабочий каталог <wd>\\config\\device\\ "
                    u"(tools/effmaker/probes/build_*/, в .gitignore); в "
                    u"реестре стоит шаблоном пути `<wd>\\…` (T153)",
}


def read_lines(path):
    u"""Строки файла, резанные ТОЛЬКО по `\\n`; номер строки = номер у `grep -n`.

    ⛔ Одиночный CR внутри строки реестра — ЗАКОННЫЙ знак (в описаниях он
    встречался), а построчное чтение Python в любом режиме, кроме
    `newline=""` + своего разреза, режет по нему строку пополам: хвост
    перестаёт начинаться с `|`, теряется из разбора МОЛЧА, а номера строк
    после него уезжают от тех, что показывает редактор и `grep -n`. Поэтому
    файл читается целиком нетронутым и режется по `\\n` руками; CR остаётся
    внутри своей строки. Положительный контроль: `--selftest` подсаживает
    CR в описание строки и требует, чтобы строк не прибавилось.
    """
    with io.open(path, encoding="utf-8-sig", newline=u"") as f:
        text = f.read()
    return text.split(u"\n")


def read_rows(path):
    rows = []
    for n, line in enumerate(read_lines(path), 1):
        m = ROW.match(line)
        if m:
            rows.append((m.group(1), m.group(2), n))
    return rows


def file_targets(text):
    u"""Цели проверки 2 в описании строки: ссылки `[…](путь)` и имена файлов
    в обратных кавычках (по `FILEY`, без пробелов внутри)."""
    targets = set()
    for t in LINK.findall(text):
        t = t.split("#")[0].strip()
        if t and not t.startswith("http"):
            targets.add(t)
    for c in CODE.findall(text):
        c = c.strip()
        if FILEY.search(c) and " " not in c:
            targets.add(c)
    return targets


def check_file_refs(root, out, files, index=None, tracked=None):
    u"""Проверка 2: ссылки на файлы. Возвращает число НАХОДОК В СЧЁТ.

    Два списка, и в счёт входит только второй (`T223`, 06.09.2026 — разбор
    посылки: «шаблоны» вроде `*.resx` и `Foo.Designer.cs` попадают в первый
    список, к глазам, и сторожа не красят):

    1. «нет в дереве» — цели, которых нет ни у git, ни на диске: к глазам,
       потому что строка может рассказывать об удалённом файле;
    2. «НЕТ В РЕПОЗИТОРИИ» — цель есть на диске (по пути или по голому имени
       в индексе дерева), а git её не знает: случай N8, считается.

    `OUTSIDE_ON_PURPOSE` сверяется по имени файла (basename, нижний регистр)
    и снимает цель с обоих списков; у каждой записи обязана быть причина.
    `index` и `tracked` можно передать готовыми, чтобы не строить их дважды
    (`--selftest` зовёт проверку на чистой и на подделанной копии).
    """
    if index is None:
        index = build_index(root)
    if tracked is None:
        tracked = tracked_set(root)
    tracked_paths, tracked_names = tracked
    bad = 0
    untracked = collections.defaultdict(set)
    out.write(u"# Ссылки на файлы, которых нет в дереве\n\n")
    missing_any = False
    for name, rows in files.items():
        for num, text, line in rows:
            lost = []
            for t in file_targets(text):
                key = t.replace("\\", "/").lstrip("./").lower()
                base = os.path.basename(t.replace("\\", "/")).lower()
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
        out.write(u"  %-20s %s\n" % (name, OUTSIDE_ON_PURPOSE[name]))
    out.write(u"\n")
    return bad


def selftest_file_refs(root, out):
    u"""Положительный контроль проверки 2 (T223). Возвращает список провалов.

    Во ВРЕМЕННУЮ копию `TODO.md` подсаживаются в описание одной чистой строки
    три цели:
      * битая ссылка на файл, которого нет нигде, — обязана быть названа в
        списке «нет в дереве» ровно в этой строке;
      * ссылка на файл, который контроль сам кладёт рядом с копией (на
        диске есть, git не знает), — обязана быть названа в списке «НЕТ В
        РЕПОЗИТОРИИ» и прибавить к счёту ровно единицу;
      * имя из `OUTSIDE_ON_PURPOSE` — НЕ должно всплыть ни в одном списке
        (иначе список исключений не работает, а это ровно то, чем лечится
        T223).
    Чистая копия обязана дать столько же, сколько чистая копия до подсадки
    (сравнение не с нулём: реестр сегодня не безупречен, и сторож обязан
    работать на нём таком).
    """
    failures = []
    src = os.path.join(root, u"TODO.md")
    out.write(u"# Положительный контроль проверки 2 (T223)\n\n")
    if not os.path.exists(src):
        return [u"TODO.md не найден — контроль проверки 2 не проведён"]
    tmp = tempfile.mkdtemp(prefix=u"check_registry_selftest2_")
    try:
        dst = os.path.join(tmp, u"TODO.md")
        shutil.copyfile(src, dst)
        planted_file = os.path.join(tmp, u"g3_planted_untracked.md")
        with io.open(planted_file, "w", encoding="utf-8") as f:
            f.write(u"подсадка контроля проверки 2\n")
        index = build_index(root)
        tracked = tracked_set(root)
        excluded = sorted(OUTSIDE_ON_PURPOSE)[0]
        broken = u"g3-нет-такого-файла.md"

        quiet = io.StringIO()
        clean = check_file_refs(root, quiet, collections.OrderedDict(
            [(u"копия", read_rows(dst))]), index, tracked)

        lines = read_lines(dst)
        planted_line = planted_num = None
        for i, line in enumerate(lines):
            m = ROW.match(line.rstrip(u"\r\n"))
            if not m:
                continue
            cells = line.rstrip(u"\n").split(u"|")
            if len(cells) < 5:
                continue
            cells[-2] = (cells[-2] + u" подсадка: `%s`, [есть только на диске](%s), `%s` "
                         % (broken, planted_file.replace(u"\\", u"/"), excluded))
            lines[i] = u"|".join(cells)
            planted_line, planted_num = i + 1, m.group(1)
            break
        if planted_line is None:
            return [u"не нашлось строки для подсадки проверки 2"]
        with io.open(dst, "w", encoding="utf-8", newline=u"") as f:
            f.write(u"\n".join(lines))

        loud = io.StringIO()
        dirty = check_file_refs(root, loud, collections.OrderedDict(
            [(u"копия", read_rows(dst))]), index, tracked)
        text = loud.getvalue()
        sec_lost = text.split(u"# Ссылки на файлы, которых НЕТ В РЕПОЗИТОРИИ")[0]
        sec_untr = text.split(u"# Ссылки на файлы, которых НЕТ В РЕПОЗИТОРИИ")[1]
        row_re = u"копия\\s+%s\\s+строка %d .*%s" % (planted_num, planted_line, u"%s")
        named_lost = re.search(row_re % re.escape(broken), sec_lost) is not None
        named_untr = re.search(row_re % re.escape(
            planted_file.replace(u"\\", u"/")), sec_untr) is not None
        excl_seen = re.search(row_re % re.escape(excluded), text) is not None

        out.write(u"  чистая копия: находок в счёт %d\n" % clean)
        out.write(u"  подсажено в строку %s (строка %d): битая `%s`, "
                  u"файл только на диске `%s`, исключение `%s`\n"
                  % (planted_num, planted_line, broken, planted_file, excluded))
        out.write(u"  подделанная копия: находок в счёт %d (ожидалось %d)\n"
                  % (dirty, clean + 1))
        out.write(u"    битая ссылка названа в «нет в дереве»: %s\n"
                  % (u"да" if named_lost else u"НЕТ"))
        out.write(u"    файл только на диске назван в «НЕТ В РЕПОЗИТОРИИ»: %s\n"
                  % (u"да" if named_untr else u"НЕТ"))
        out.write(u"    имя из OUTSIDE_ON_PURPOSE всплыло: %s\n"
                  % (u"ДА" if excl_seen else u"нет"))
        if dirty != clean + 1:
            failures.append(u"проверка 2: находок %d вместо %d" % (dirty, clean + 1))
        if not named_lost:
            failures.append(u"проверка 2: битая ссылка `%s` не названа в строке %d"
                            % (broken, planted_line))
        if not named_untr:
            failures.append(u"проверка 2: файл только на диске не назван в строке %d"
                            % planted_line)
        if excl_seen:
            failures.append(u"проверка 2: исключение `%s` всплыло находкой" % excluded)
        out.write(u"\n  %s\n\n" % (u"КОНТРОЛЬ ПРОВЕРКИ 2 СОШЁЛСЯ"
                                  if not failures else
                                  u"⛔ КОНТРОЛЬ ПРОВЕРКИ 2 ПРОВАЛЕН: " + u"; ".join(failures)))
        if failures:
            out.write(text)
        return failures
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def check_numbers(out, files):
    u"""Проверка 1 — столкновения номеров. Возвращает число находок.

    `files` — упорядоченный словарь имя → строки `read_rows`; вынесена из
    `main`, чтобы положительный контроль (`--selftest`) мог подсунуть ей
    КОПИЮ реестра с задвоенным номером и увидеть отказ с этим номером.
    """
    out.write(u"# Номера\n\n")
    bad = 0
    seen = {}
    for name, rows in files.items():
        counts = collections.Counter(num for num, _, _ in rows)
        dup = sorted(n for n, c in counts.items() if c > 1)
        out.write(u"%-8s строк %3d, столкновений внутри файла: %s\n"
                  % (name, len(rows), u", ".join(dup) if dup else u"нет"))
        if dup:
            bad += len(dup)
        seen[name] = set(counts)

    names = list(files)
    shared = sorted(set.intersection(*[seen[n] for n in names])) if len(names) > 1 else []
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
    return bad


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


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()


def config_tree(root, base):
    """Копия конфига: (пути внутри неё -> sha256, пути БЕЗ ФАЙЛА НА ДИСКЕ).

    Спрашивается git, а не дерево каталогов, — по той же причине, что и в
    проверке ссылок: «есть на диске» и «есть в проекте» разные вещи.

    ⛔ Второе значение возвращается с 26.08.2026 и заведено опытом. Прежде
    файлы, которых на диске нет, ОТБРАСЫВАЛИСЬ молча условием `os.path.exists`,
    и удаление файла с диска в ОБЕИХ копиях сразу давало НОЛЬ находок: менялось
    только напечатанное «24 файлов» на «23 файлов». Отслеживаемый git-ом файл,
    пропавший с диска, — это находка, а не пустое место.
    """
    prefix = base.replace(os.sep, "/") + "/"
    # `-z`, а не построчно: имена с кириллицей (`ROI/Atom Spectra 2 Маринелли
    # 40х40.xml`) git печатает В КАВЫЧКАХ с восьмеричными экранами, строка
    # начинается с `"` и мимо `startswith(prefix)` проходит МОЛЧА — на первой
    # редакции проверки так потерялся ровно один файл из двадцати четырёх.
    out = subprocess.check_output(["git", "ls-files", "-z", prefix], cwd=root)
    tree, gone = {}, []
    for name in out.decode("utf-8", "replace").split("\0"):
        name = name.strip()
        if not name or not name.startswith(prefix):
            continue
        disk = os.path.join(root, name.replace("/", os.sep))
        if os.path.exists(disk):
            tree[name[len(prefix):]] = sha256_of(disk)
        else:
            gone.append(name[len(prefix):])
    return tree, gone


def check_config_copies(root, out):
    """Храповик по двум копиям `config/` (T66). Возвращает число находок."""
    left, right = CONFIG_COPIES
    a, gone_a = config_tree(root, left)
    b, gone_b = config_tree(root, right)
    out.write(u"# Две копии `config/` (T66)\n\n")
    # «Копии нет» — это ПУСТО В GIT, а не «пусто на диске»: копия, целиком
    # стёртая с диска, обязана дать находки, а не пропуск проверки.
    if not (a or gone_a) or not (b or gone_b):
        out.write(u"  копии не найдены в git (%s: %d, %s: %d) — проверка пропущена\n\n"
                  % (left, len(a) + len(gone_a), right, len(b) + len(gone_b)))
        return 0

    bad = 0
    # Состав сравнивается по ИНДЕКСУ git, иначе пропавший с диска файл выглядел
    # бы как «есть только в одной копии» — диагноз не тот, и находка одна вместо
    # своей.
    keys_a = set(a) | set(gone_a)
    keys_b = set(b) | set(gone_b)
    only = sorted(keys_a ^ keys_b)
    same = sorted(k for k in set(a) & set(b) if a[k] == b[k])
    diff = sorted(k for k in set(a) & set(b) if a[k] != b[k])
    out.write(u"  %-24s %d файлов в git, на диске нет: %d\n  %-24s %d файлов в git, на диске нет: %d\n"
              % (left.replace(os.sep, u"/"), len(keys_a), len(gone_a),
                 right.replace(os.sep, u"/"), len(keys_b), len(gone_b)))
    out.write(u"  совпадают побайтно: %d, расходятся: %d, есть только в одной: %d\n\n"
              % (len(same), len(diff), len(only)))

    for where, gone in ((left, gone_a), (right, gone_b)):
        for k in sorted(gone):
            out.write(u"  ⛔ В ИНДЕКСЕ GIT ЕСТЬ, НА ДИСКЕ НЕТ (%s): %s\n"
                      % (where.replace(os.sep, u"/"), k))
            bad += 1

    for k in only:
        where = (left if k in keys_a else right).replace(os.sep, u"/")
        out.write(u"  ⛔ ТОЛЬКО В ОДНОЙ КОПИИ (%s): %s\n" % (where, k))
        bad += 1

    for k in diff:
        known = CONFIG_COPIES_KNOWN.get(k)
        if known is None:
            out.write(u"  ⛔ НОВОЕ РАСХОЖДЕНИЕ: %s\n" % k)
            out.write(u"      %s %s\n      %s %s\n"
                      % (left, a[k][:16], right, b[k][:16]))
            bad += 1
        elif (a[k], b[k]) != known:
            out.write(u"  ⛔ ИЗВЕСТНОЕ РАСХОЖДЕНИЕ ИЗМЕНИЛОСЬ: %s\n" % k)
            out.write(u"      было  %s / %s\n      стало %s / %s\n"
                      % (known[0][:16], known[1][:16], a[k][:16], b[k][:16]))
            out.write(u"      разбирали ДРУГОЕ состояние — либо перемерить, "
                      u"либо поправить CONFIG_COPIES_KNOWN\n")
            bad += 1
        else:
            out.write(u"  известно (T66): %s\n" % k)

    for k in sorted(CONFIG_COPIES_KNOWN):
        if k in a and k in b and a[k] == b[k]:
            out.write(u"  сошлось, снять из CONFIG_COPIES_KNOWN: %s\n" % k)

    out.write(u"\n")
    return bad


def declared_base(path, pattern):
    u"""Найти объявление действующей базы. Возвращает (база, дата, номер строки)."""
    if not os.path.exists(path):
        return None
    with io.open(path, encoding="utf-8-sig") as f:
        for i, line in enumerate(f, 1):
            m = pattern.match(line)
            if m:
                return (m.group(1), m.group(2), i)
    return None


def find_memory_dir(root):
    u"""Каталог памяти агента ИМЕННО ЭТОГО дерева. Иначе None.

    ⛔ Брать первый попавшийся нельзя: на машине их несколько (здесь пять — по
    одному на проект), и первый по алфавиту принадлежит чужому дереву. Первая
    редакция так и делала и отвечала «база названа в указателе», читая чужую
    память; поймано положительным контролем 03.09.2026.

    Имя каталога — путь дерева, где каждый не-буквенно-цифровой знак заменён
    дефисом: `C:\\Users\\…\\BQ Eng res .NET 4.8` → `C--Users-…-BQ-Eng-res--NET-4-8`.
    """
    projects = os.path.join(os.path.expanduser("~"), ".claude", "projects")
    if not os.path.isdir(projects):
        return None
    slug = re.sub(r"[^A-Za-z0-9]", "-", os.path.abspath(root))
    mine = os.path.join(projects, slug, "memory")
    if os.path.isfile(os.path.join(mine, "MEMORY.md")):
        return mine
    return None


def check_corpus_base(root, out):
    u"""Храповик по объявлению действующей базы корпуса. Возвращает число находок.

    Смена базы — это ТРИ МЕСТА: преамбула `TODO.md`, журнал корпуса и память
    агента. Объявить в двух значит НЕ объявить, и это уже случалось ДВАЖДЫ
    ПОДРЯД: 27.08.2026 в `TODO.md` стояла снятая `out_v5` (две базы отставания),
    03.09.2026 — снятая `out_v9` (тоже две). Оба раза проверка существовала
    строкой `grep` в самой преамбуле, и оба раза её никто не позвал: помнить о
    ней приходится ровно в тот момент, когда занят другим.

    Привязка к НАЧАЛУ строки здесь не украшение: без неё ловятся ещё и
    исторические упоминания внутри задач (`B26` пересказывает объявления
    `out_v6` и `out_v5` как часть своего разбора, и они законны). Строка
    таблицы всегда начинается с `|`, объявление — нет.

    Память лежит ВНЕ репозитория и на другой машине её может не быть вовсе,
    поэтому третье место проверяется, только если найдено, а отсутствие
    печатается словами — молчаливый пропуск здесь был бы той же слепотой,
    какую эта проверка и ловит.
    """
    out.write(u"# Действующая база корпуса — объявлена ли в ТРЁХ местах\n\n")
    places = [
        (u"TODO.md", os.path.join(root, "TODO.md"), BASE_IN_TODO),
        (u"tools/CORPUS/README.md",
         os.path.join(root, "tools", "CORPUS", "README.md"), BASE_IN_CORPUS),
    ]
    found = []
    bad = 0
    for name, path, pattern in places:
        got = declared_base(path, pattern)
        if got is None:
            out.write(u"  ⛔ %s: объявления НЕ НАЙДЕНО — либо оно переписано в\n"
                      u"      другом виде, либо базу забыли объявить\n" % name)
            bad += 1
            continue
        out.write(u"  %-24s %s, %s (строка %d)\n"
                  % (name, got[0], got[1], got[2]))
        found.append((name, got))

    if len(found) == len(places):
        bases = set(g[0] for _, g in found)
        dates = set(g[1] for _, g in found)
        if len(bases) > 1:
            out.write(u"  ⛔ РАЗНЫЕ БАЗЫ: %s. Объявить в двух местах значит НЕ\n"
                      u"      объявить: читатель сверяет свежий прогон со снятой\n"
                      u"      моделью и расходится заведомо.\n"
                      % u" против ".join(sorted(bases)))
            bad += 1
        elif len(dates) > 1:
            out.write(u"  ⛔ база одна (%s), а ДАТЫ разные: %s\n"
                      % (bases.pop(), u" против ".join(sorted(dates))))
            bad += 1

    # Третье место — память. Сверяется НАЛИЧИЕМ памятки этой базы, а не
    # упоминанием её имени: указатель памяти называет и СНЯТЫЕ базы (реестром
    # «не цитировать»), поэтому подстрочная сверка даёт ложный пропуск —
    # поймано положительным контролем 03.09.2026, где плечо с откаченной
    # `out_v9` прошло со словами «назван в указателе».
    mem = find_memory_dir(root)
    if mem is None:
        out.write(u"  ⚠ память агента на этой машине не найдена — ТРЕТЬЕ место\n"
                  u"      не проверено, сверь глазами\n")
    elif found:
        base = found[0][1][0]
        note = os.path.join(mem, u"corpus-base-%s.md" % base.replace(u"_", u"-"))
        others = sorted(os.path.basename(p) for p in
                        glob.glob(os.path.join(mem, u"corpus-base-*.md"))
                        if os.path.basename(p) != os.path.basename(note))
        if os.path.exists(note):
            out.write(u"  %-24s %s\n" % (u"память агента", os.path.basename(note)))
        else:
            out.write(u"  ⛔ памятки %s НЕТ — третье место отстало\n"
                      % os.path.basename(note))
            bad += 1
        out.write(u"      рядом лежат памятки СНЯТЫХ баз (%s) — какая из них\n"
                  u"      действующая, машинно НЕ проверяется, только наличие\n"
                  % (u", ".join(others) if others else u"нет"))

    out.write(u"\n")
    return bad


# ---------------------------------------------------------------------------
# 6. форма графы состояния (`T100`)
# ---------------------------------------------------------------------------
#: Слова, которыми реестр называет состояние. Регистр не важен — в обоих файлах
#: живут `открыто`, `ЗАКРЫТО` и `СНЯТА`; окончание важно: «снято» и «снята»
#: пишут оба. Список ВЫВЕДЕН разбором, а не назначен: с ним без слова состояния
#: остаётся ровно одна строка из 726 (`A50`, «половина ПРИЛОЖЕНИЯ СДЕЛАНА…»),
#: без «снят[оа]» — четыре.
STATE_WORDS = (u"открыт[оа]", u"закрыт[оа]", u"снят[оа]")
STATE_STRUCK = re.compile(u"~~(.+?)~~")


def state_cell(line):
    u"""Графа состояния строки реестра и число её столбцов.

    ⛔ Внутри ОПИСАНИЙ реестра стоят свои `|`: в `TODO.md` таких строк 40 из
    397, до десяти столбцов вместо четырёх. Поэтому брать клетку С КОНЦА
    («детали», `cells[-2]`) нельзя — у такой строки конец уезжает. Графа
    состояния — ВТОРАЯ клетка СЛЕВА, а первая, номер, содержать `|` не может;
    отсчёт слева поэтому надёжен. Проверено счётом: ни в одной строке обоих
    файлов слово состояния не уехало в следующую клетку.

    Второе значение — сколько у строки столбцов. У шести строк `TODO.md`
    (`A36`, `A41`, `A44`, `A45`, `A46`, `A49`) их всего два: описание слилось с
    графой состояния, и разбор там идёт по всему тексту строки. Форму это не
    ломает, но находку в такой строке надо читать с оговоркой, и она её несёт.
    """
    cells = line.rstrip(u"\r\n").split(u"|")
    return (cells[2] if len(cells) > 3 else u""), max(len(cells) - 2, 0)


def state_findings(cell):
    u"""Находки по форме графы: ([(слово, вид, живых, вычеркнутых)], есть_слово).

    `both` — слово стоит И вычеркнутым, И нет (ровно `T100`).
    `cut` — маркеры вычёркивания РАЗРЕЗАЮТ слово (`~~откры~~то`): человек
    видит вычеркнутое «открыто», машина не видит слова вовсе.
    """
    spans = [(m.start(1), m.end(1)) for m in STATE_STRUCK.finditer(cell)]
    bare = cell.replace(u"~~", u"")
    found, seen = [], False
    for w in STATE_WORDS:
        pat = re.compile(w, re.IGNORECASE)
        plain = struck = 0
        for m in pat.finditer(cell):
            if any(a <= m.start() < b for a, b in spans):
                struck += 1
            else:
                plain += 1
        if plain or struck:
            seen = True
        if plain and struck:
            found.append((w, u"both", plain, struck))
        if len(pat.findall(bare)) > plain + struck:
            found.append((w, u"cut", plain, struck))
    return found, seen


def check_state_column(out, files):
    u"""Приёмка на форму графы состояния (`T100`). Возвращает число находок.

    `files` — список пар (имя, путь): так эту проверку зовёт и положительный
    контроль, подсовывая ей ВРЕМЕННУЮ копию.

    Находкой (выход 1) считается только форма: «и так и эдак» и разрезанное
    слово. Графа, не называющая состояния ВООБЩЕ, печатается к глазам и прогон
    не валит — «СНЯТА 18.08.2026 — основание исчезло» законно, а требовать
    здесь словарь значило бы завести сторожа, который красен на чистом дереве.
    """
    out.write(u"# Форма графы состояния (T100)\n\n")
    bad = 0
    for name, path in files:
        if not os.path.exists(path):
            out.write(u"  %s: файла нет\n" % name)
            continue
        rows = noword = merged = 0
        eyes = []
        for n, line in enumerate(read_lines(path), 1):
            m = ROW.match(line.rstrip(u"\r\n"))
            if not m:
                continue
            rows += 1
            cell, ncols = state_cell(line)
            if ncols < 3:
                merged += 1
            found, seen = state_findings(cell)
            for w, kind, plain, struck in found:
                bad += 1
                what = (u"И ВЫЧЕРКНУТО, И НЕТ (живых %d, вычеркнутых %d)"
                        % (plain, struck) if kind == u"both"
                        else u"РАЗРЕЗАНО МАРКЕРАМИ ВЫЧЁРКИВАНИЯ")
                out.write(u"  ⛔ %-8s %-5s строка %-4d «%s» %s%s\n"
                          % (name, m.group(1), n, w, what,
                             u" [графа слита с задачей]" if ncols < 3 else u""))
                out.write(u"      %s\n" % cell.strip()[:150])
            if not seen:
                noword += 1
                eyes.append(u"  ⚠ %-8s %-5s строка %-4d состояния не называет: %s\n"
                            % (name, m.group(1), n, cell.strip()[:110]))
        out.write(u"  %-8s строк %3d, графа слита с задачей: %d, "
                  u"состояния не называют: %d\n" % (name, rows, merged, noword))
        for e in eyes:
            out.write(e)
    out.write(u"\n")
    return bad


#: Подделки для положительного контроля: (пометка, чем заменить графу).
#: Первые две — исторические формы `T100` дословно, третья — слово, разрезанное
#: маркерами, четвёртая — то же самое на «закрыто», чтобы проверка не оказалась
#: написанной под одно-единственное слово.
STATE_FAKES = (
    (u"форма S102/T92", u"открыто ~~открыто~~ **СДЕЛАНО 27.08.2026**"),
    (u"форма T88", u"открыто, **РЕШЕНИЕ Amber ЕСТЬ** ~~открыто~~ "
                   u"**ИСПОЛНЕНО 27.08.2026**"),
    (u"разрезанное слово", u"~~откры~~то **СДЕЛАНО**"),
    (u"то же на «закрыто»", u"закрыто ~~закрыто~~ **СДЕЛАНО**"),
)


def selftest_registry(root, out):
    u"""Положительный контроль проверок 1 и 6. Возвращает 0, если сошёлся.

    ⛔ Заведён потому, что сторож без доказанного ОТКАЗА выглядит работающим:
    код 0, находок 0 — ровно то, что даёт и ненаписанная проверка (`T69`).
    Здесь проверка обязана СНАЧАЛА дать на чистой копии столько же, сколько
    на оригинале, а ПОТОМ покраснеть на подделанной — ровно на подставленных
    строках и ни на одной другой: находка не в той строке такой же провал,
    как отсутствие находки.

    Подсаживается ШЕСТЬ порч в одну копию `TODO.md`:
      * четыре графы состояния (`STATE_FAKES`) — проверка 6 обязана назвать
        каждую своей строкой;
      * задвоенный номер — номер первой чистой строки переписывается в ещё
        одну строку, и проверка 1 обязана назвать именно его;
      * одиночный CR и лишняя `|` внутри ОПИСАНИЯ ещё одной строки — это
        законные знаки реестра, и от них не должно ни прибавиться строк, ни
        появиться находки в этой строке (`T100`, память «питон рвёт текст на
        одиночном CR»).

    Чистых находок на оригинале может быть и больше нуля — тогда контроль
    требует ровно «чистые + подсаженные», а не нуля: сторож обязан работать и
    на реестре, который сегодня не безупречен.

    Оригинал `TODO.md` только читается; правится копия в каталоге временных
    файлов, который в конце сносится.
    """
    src = os.path.join(root, u"TODO.md")
    out.write(u"# Положительный контроль проверок 1 и 6 (T100)\n\n")
    if not os.path.exists(src):
        out.write(u"  ⛔ TODO.md не найден — контроль не проведён\n")
        return 1
    tmp = tempfile.mkdtemp(prefix=u"check_registry_selftest_")
    try:
        dst = os.path.join(tmp, u"TODO.md")
        shutil.copyfile(src, dst)
        files = [(u"копия", dst)]
        failures = []

        quiet = io.StringIO()
        clean = check_state_column(quiet, files)
        clean_rows = read_rows(dst)
        clean_dups = sorted(n for n, c in collections.Counter(
            num for num, _, _ in clean_rows).items() if c > 1)
        out.write(u"  чистая копия: строк %d, находок по графе %d, "
                  u"задвоенных номеров %d\n"
                  % (len(clean_rows), clean, len(clean_dups)))

        # подделываем графы у первых строк, чья графа сегодня безупречна;
        # следом за ними — ещё две чистые строки под номер и под CR
        lines = read_lines(dst)
        planted, k = [], 0
        dup_num = dup_line = cr_line = None
        for i, line in enumerate(lines):
            if cr_line is not None:
                break
            m = ROW.match(line.rstrip(u"\r\n"))
            if not m:
                continue
            cell, ncols = state_cell(line)
            found, seen = state_findings(cell)
            if found or not seen or ncols < 4:
                continue
            cells = line.rstrip(u"\n").split(u"|")
            if k < len(STATE_FAKES):
                cells[2] = u" %s " % STATE_FAKES[k][1]
                planted.append((m.group(1), i + 1, STATE_FAKES[k][0]))
                if k == 0:
                    dup_num = m.group(1)
                k += 1
            elif dup_line is None:
                # тот же номер, что у первой подделанной строки
                cells[1] = re.sub(r"[A-Z]{1,2}\d{1,3}", dup_num, cells[1], count=1)
                dup_line = i + 1
            else:
                # CR и лишняя черта — В ОПИСАНИИ, графы не касаются
                cells[3] = cells[3] + u" хвост после CR\r а тут | черта "
                cr_line = i + 1
            lines[i] = u"|".join(cells)
        with io.open(dst, "w", encoding="utf-8", newline=u"") as f:
            f.write(u"\n".join(lines))

        loud = io.StringIO()
        dirty = check_state_column(loud, files)
        text = loud.getvalue()
        dirty_rows = read_rows(dst)
        dirty_dups = sorted(n for n, c in collections.Counter(
            num for num, _, _ in dirty_rows).items() if c > 1)
        numbers_out = io.StringIO()
        numbers_bad = check_numbers(numbers_out, collections.OrderedDict(
            [(u"копия", dirty_rows)]))

        out.write(u"  подделано граф: %d — %s\n"
                  % (len(planted), u", ".join(u"%s (%s)" % (p[0], p[2])
                                              for p in planted)))
        out.write(u"  задвоен номер %s в строке %s; CR и `|` подсажены в "
                  u"описание строки %s\n" % (dup_num, dup_line, cr_line))
        out.write(u"  подделанная копия: находок по графе %d (ожидалось %d)\n"
                  % (dirty, clean + len(planted)))
        for num, ln, what in planted:
            named = re.search(u"⛔ .*строка %d " % ln, text) is not None
            out.write(u"    %-6s строка %-4d %-20s названа: %s\n"
                      % (num, ln, what, u"да" if named else u"НЕТ"))
            if not named:
                failures.append(u"графа строки %d не названа" % ln)
        if dirty != clean + len(planted):
            failures.append(u"находок по графе %d вместо %d"
                            % (dirty, clean + len(planted)))
        if len(planted) != len(STATE_FAKES) or dup_line is None or cr_line is None:
            failures.append(u"не хватило чистых строк для подсадки")
        if re.search(u"⛔ .*строка %d " % cr_line, text):
            failures.append(u"строка с CR названа находкой, а порчи в её графе нет")
        out.write(u"  строк после подсадки CR: %d (ожидалось %d, CR строку не режет)\n"
                  % (len(dirty_rows), len(clean_rows)))
        if len(dirty_rows) != len(clean_rows):
            failures.append(u"CR разрезал строку: строк %d вместо %d"
                            % (len(dirty_rows), len(clean_rows)))
        cr_row = [r for r in dirty_rows if r[2] == cr_line]
        if not cr_row or u"| черта" not in cr_row[0][1]:
            failures.append(u"хвост строки после CR потерян из разбора")
        else:
            out.write(u"  хвост описания за CR в разборе цел: да\n")
        out.write(u"  задвоенных номеров: %s (ожидалось %s), проверка 1 дала "
                  u"находок %d (ожидалось %d)\n"
                  % (u", ".join(dirty_dups) or u"нет",
                     u", ".join(sorted(set(clean_dups) | {dup_num})),
                     numbers_bad, len(clean_dups) + 1))
        if dup_num not in dirty_dups or numbers_bad != len(clean_dups) + 1:
            failures.append(u"задвоенный номер %s не пойман" % dup_num)

        ok = not failures
        out.write(u"\n  %s\n\n" % (u"КОНТРОЛЬ СОШЁЛСЯ: молчит на чистом, "
                                  u"краснеет на подделанном, CR и `|` переживает"
                                  if ok else
                                  u"⛔ КОНТРОЛЬ ПРОВАЛЕН — проверка меряет не то: "
                                  + u"; ".join(failures)))
        if not ok:
            out.write(text)
            out.write(numbers_out.getvalue())
        return 0 if ok else 1
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def check_done_headers_section(root, out):
    u"""Проверка 7 — заголовок строки `DONE.md` против её тела (`T102`).

    Сам разбор живёт в `tools/check_done_headers.py` (у него свои ключи
    `--scan`, `--dump-heads` и свой `--selftest`); здесь он только зовётся и
    его находки считаются в общий итог. Ловится узкий разряд: слова незнания
    в заголовке («причина неизвестна», «не воспроизводится», …) при ответе в
    теле той же ячейки (блок ✅, «СНЯТА», «НАХОДКИ НЕТ»). ⛔ `DONE.md` правит
    только Amber — проверка называет строки, замены текстом собирает тот, кто
    её позвал.
    """
    out.write(u"# Заголовки DONE.md против их тел (T102)\n\n")
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    try:
        import check_done_headers
    except ImportError as e:
        out.write(u"  ⛔ tools/check_done_headers.py не импортируется: %s\n\n" % e)
        return 1
    path = os.path.join(root, u"DONE.md")
    if not os.path.exists(path):
        out.write(u"  DONE.md: файла нет\n\n")
        return 0
    findings, stats = check_done_headers.analyse_file(path)
    out.write(u"  DONE.md  строк %d, заголовков со словами незнания: %d, "
              u"противоречий: %d\n" % (stats["rows"], stats["unknown_head"],
                                       len(findings)))
    for f in findings:
        out.write(u"  ⛔ %-8s %-5s строка %-4d заголовок: %s\n"
                  u"      тело: %s\n"
                  % (u"DONE.md", f["id"], f["line"], f["head"][:120],
                     u", ".join(f["answers"])))
    out.write(u"  положительный контроль этой проверки: "
              u"python tools/check_done_headers.py --selftest\n\n")
    return len(findings)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", default=os.path.dirname(os.path.dirname(
        os.path.abspath(__file__))))
    p.add_argument("--selftest", action="store_true",
                   help=u"положительный контроль проверок 1, 2 и 6 (T100, "
                        u"T223): подсадить во ВРЕМЕННУЮ копию TODO.md кривые "
                        u"графы, задвоенный номер, CR и `|`, битую ссылку и "
                        u"файл только на диске — и убедиться, что проверка "
                        u"краснеет ровно там, где надо")
    a = p.parse_args()

    if a.selftest:
        out = io.open(1, "w", encoding="utf-8", closefd=False)
        rc = selftest_registry(a.root, out)
        failures2 = selftest_file_refs(a.root, out)
        out.flush()
        return 1 if (rc or failures2) else 0

    root = a.root
    out = io.open(1, "w", encoding="utf-8", closefd=False)
    bad = 0

    files = collections.OrderedDict([
        ("TODO.md", read_rows(os.path.join(root, "TODO.md"))),
        ("DONE.md", read_rows(os.path.join(root, "DONE.md")))])

    # --- 1. номера ------------------------------------------------------
    bad += check_numbers(out, files)

    # --- 2. ссылки на файлы ---------------------------------------------
    bad += check_file_refs(root, out, files)

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

    # --- 4. две копии config/ -------------------------------------------
    bad += check_config_copies(root, out)

    # --- 5. объявление действующей базы корпуса --------------------------
    bad += check_corpus_base(root, out)

    # --- 6. форма графы состояния (T100) ---------------------------------
    bad += check_state_column(out, [
        (u"TODO.md", os.path.join(root, u"TODO.md")),
        (u"DONE.md", os.path.join(root, u"DONE.md"))])
    out.write(u"  положительный контроль этой проверки: "
              u"python tools/check_registry.py --selftest\n")

    # --- 7. заголовки DONE.md против их тел (T102) -----------------------
    bad += check_done_headers_section(root, out)

    out.write(u"\n%s\n" % (u"РЕЕСТР ЧИСТ" if bad == 0
                           else u"НАХОДОК: %d" % bad))
    out.flush()
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
