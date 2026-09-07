# -*- coding: utf-8 -*-
u"""Сторож формы ROI: перестроение списка не перечисляет ЖИВОЙ список менеджера.

## Откуда (`A296`, 07.09.2026)

Окно ROI падало прямо на открытии:

    System.InvalidOperationException: Collection was modified;
      enumeration operation may not execute.
       at BecquerelMonitor.ROIConfigForm.ListupConfigFiles() ... line 101
       at BecquerelMonitor.ROIConfigForm.ROIConfigForm_Load(...)

Путь замкнутый: `ListupConfigFiles` шёл `foreach` по `manager.ROIConfigList`,
внутри цикла звал `Selections.AddCell`, тот поднимал `SelectionChanged`, тот —
`ConfirmSaveROIConfig`, а он при грязной конфигурации — `manager.SaveConfig`,
который делает `roiConfigList.Remove`. То есть метод правил ту самую коллекцию,
по которой шёл.

## Почему сторож СТАТИЧЕСКИЙ, а не прогон

Падение живёт за модальным окном: `SaveConfig` зовётся только после ответа «Да»
в `MessageBox`. Безоконная проба на нём ЗАВИСЛА БЫ, а не упала (память
«проба виснет на модальном окне»), то есть плечо «до починки» headless-путём не
снимается вовсе. Поэтому здесь судится ФОРМА ПРАВИЛА — ровно как у
`check_escape_parents.py` и `check_refit_z_class.py`, где симптом тоже не
отличим от выигрыша без глаз.

Судятся три вещи, и каждая — своя половина починки:

  1. перечисляется СНИМОК (`.ToArray()`), а не живой список менеджера;
  2. перестроение под замком `listing` (иначе вызов рекурсивен);
  3. `reenter` в обработчике выбора снимается в `finally` — прежде ранний
     `return` оставлял его поднятым навсегда, и выбор строки умирал молча.

Положительный контроль: `--selftest` подсовывает копию файла с каждой из трёх
порч по очереди и требует отказа именно на ней.

    python tools/check_roi_listup.py [--selftest]
"""

import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
FORM = os.path.join(ROOT, "BecquerelMonitor", "ROIConfigForm.cs")


def _console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def read(path):
    with io.open(path, encoding="utf-8-sig", newline="") as f:
        return f.read()


def body(text, signature):
    u"""Тело метода от подписи до строки, где скобки сошлись."""
    i = text.find(signature)
    if i < 0:
        return None
    j = text.index("{", i)
    depth = 0
    for k in range(j, len(text)):
        if text[k] == "{":
            depth += 1
        elif text[k] == "}":
            depth -= 1
            if depth == 0:
                return text[i:k + 1]
    return None


def findings(text):
    u"""Список бед. Пусто — форма правила цела."""
    bad = []

    listup = body(text, "void ListupConfigFiles()")
    if listup is None:
        bad.append(u"метода ListupConfigFiles нет вовсе")
    else:
        if "ROIConfigList.ToArray()" not in listup:
            bad.append(u"ListupConfigFiles перечисляет ЖИВОЙ список менеджера: "
                       u"нет `ROIConfigList.ToArray()` (это и есть падение A296)")
        if not re.search(r"if\s*\(\s*this\.listing\s*\)", listup):
            bad.append(u"ListupConfigFiles без замка `listing`: перестроение "
                       u"вызывает само себя через SelectionChanged")
        if "this.listing = true" not in listup or "finally" not in listup:
            bad.append(u"замок `listing` не поднимается или снимается не в finally")

    handler = body(text, "void table3_SelectionChanged(")
    if handler is None:
        bad.append(u"обработчика table3_SelectionChanged нет вовсе")
    else:
        if "finally" not in handler or "this.reenter = false" not in handler:
            bad.append(u"`reenter` снимается не в finally: ранний выход оставит "
                       u"обработчик выбора мёртвым до закрытия окна")

    core = body(text, "void SelectionChangedCore()")
    if core is None:
        bad.append(u"SelectionChangedCore не выделен — проверить вопрос о "
                   u"сохранении посреди перестроения нечем")
    elif not re.search(r"!\s*this\.listing\s*&&\s*!\s*this\.ConfirmSaveROIConfig\(\)", core):
        bad.append(u"вопрос о сохранении задаётся и посреди перестроения: "
                   u"`ConfirmSaveROIConfig` не прикрыт `listing`")

    return bad


SPOIL = (
    (u"перечисление живого списка",
     "ROIConfigList.ToArray()", "ROIConfigList"),
    (u"замок перестроения снят",
     "if (this.listing)", "if (false)"),
    (u"reenter снимается не в finally",
     "                this.reenter = false;\n            }", "            }"),
)


def selftest(out):
    out.write(u"# Положительный контроль (A296)\n\n")
    text = read(FORM)
    clean = findings(text)
    out.write(u"  чистый файл: находок %d\n" % len(clean))
    failures = []
    if clean:
        failures.append(u"на чистом файле уже есть находки")

    for name, needle, replacement in SPOIL:
        raw = text
        nl = "\r\n" if raw.count("\r\n") else "\n"
        needle_nl = needle.replace("\n", nl)
        replacement_nl = replacement.replace("\n", nl)
        if needle_nl not in raw:
            out.write(u"  ⛔ %-34s подсадить нечего: образца нет\n" % name)
            failures.append(name)
            continue

        spoiled = raw.replace(needle_nl, replacement_nl, 1)
        got = findings(spoiled)
        ok = len(got) > len(clean)
        out.write(u"  %-34s находок %d — %s\n"
                  % (name, len(got), u"замечено" if ok else u"⛔ НЕ ЗАМЕЧЕНО"))
        if not ok:
            failures.append(name)

    out.write(u"\n  %s\n" % (u"КОНТРОЛЬ СОШЁЛСЯ: каждая порча замечена"
                            if not failures
                            else u"⛔ КОНТРОЛЬ ПРОВАЛЕН: " + u"; ".join(failures)))
    return 1 if failures else 0


def main():
    _console()
    out = io.open(1, "w", encoding="utf-8", errors="replace", closefd=False)
    if "--selftest" in sys.argv[1:]:
        code = selftest(out)
        out.flush()
        return code

    out.write(u"# Перестроение списка ROI не правит то, по чему идёт (A296)\n\n")
    if not os.path.exists(FORM):
        out.write(u"  ⛔ нет файла %s\n" % FORM)
        out.flush()
        return 1

    bad = findings(read(FORM))
    for line in bad:
        out.write(u"  ⛔ %s\n" % line)

    out.write(u"\n%s\n" % (u"ФОРМА ПРАВИЛА ЦЕЛА" if not bad
                           else u"НАХОДОК: %d" % len(bad)))
    out.write(u"положительный контроль: python tools/check_roi_listup.py --selftest\n")
    out.flush()
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
