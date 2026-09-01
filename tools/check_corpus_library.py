# -*- coding: utf-8 -*-
"""⛔ Сторож глобального правила Amber 01.09.2026.

**Поставочный `config\\NuclideDefinition.xml` на корпусе не используется
НИКОГДА.** Корпус гоняется только по УКАЗАННЫМ нуклидам, привязанным к
конкретному спектру: состав берётся из `manifest.csv`, линии — из
`nucdb`/`matdb` (`FsaSampleLibrary`), подписи пиков — из той же своей базы.

⚠ **Правило про КОРПУСНЫЕ ПРОГОНЫ, а не про приложение** (указание Amber
01.09.2026): `BecqMoni` читает поставочный список как читал — он там и нужен,
человеку и подписям пиков. Поэтому сторож смотрит ТОЛЬКО корпусный путь и в
код приложения не заглядывает.

Цена нарушения измерена и носит имя (`N18`): одна запись поставочного списка —
`Pu-238`, 152 кэВ, выход 0.0009 %, — дала на `ASN16_Lu176` состав с плутонием
долей 1.7 % при z = 31.77. Плутония не объявлял ни один из 129 спектров.

Сторож смотрит КОРПУСНЫЙ ПУТЬ — пробу, которой считается корпус, и скрипты
конвейера, — и отказывает, если там появилось чтение поставочного списка:
`NuclideDefinitionManager`, `nuclides.NuclideDefinitions`,
`FsaLibrary.BuildFromPeaks`. Комментарии и строки-объяснения не в счёт: правило
надо УМЕТЬ обсуждать, запрещено его ИСПОЛНЯТЬ.

    python tools/check_corpus_library.py            # проверить дерево
    python tools/check_corpus_library.py --selftest # доказать, что отказывает

⚠ Сторож без доказанного отказа — это `T69`, поэтому `--selftest` подкладывает
нарушение в копию файла и требует от проверки кода 1; на чистом дереве — 0.
"""

import io
import os
import re
import sys

# Корпусный путь: чем считается корпус и чем он собирается.
WATCHED = [
    "tools/effmaker/probes/CorpusFsaProbe.cs",
    "tools/CORPUS/scripts/mkconfig.py",
]

# Что считается ИСПОЛНЕНИЕМ запрета (а не разговором о нём).
FORBIDDEN = [
    (re.compile(r"NuclideDefinitionManager\s*\.\s*GetInstance"),
     "поднимает NuclideDefinitionManager — он читает поставочный список"),
    (re.compile(r"\.\s*NuclideDefinitions\b"),
     "берёт NuclideDefinitions поставочного менеджера"),
    (re.compile(r"FsaLibrary\s*\.\s*BuildFromPeaks"),
     "строит библиотеку по подписям поиска пиков (список — поставочный)"),
]

COMMENT = re.compile(r"^\s*(///|//|#)")


def offences(path, text):
    """Строки-нарушители: код, а не комментарий и не строковый литерал."""
    out = []
    for n, line in enumerate(text.split("\n"), 1):
        if COMMENT.match(line):
            continue
        # строковые литералы гасим целиком — в них живут сообщения отказа
        bare = re.sub(r'"(?:[^"\\]|\\.)*"', '""', line)
        for pattern, why in FORBIDDEN:
            if pattern.search(bare):
                out.append((n, why, line.strip()[:100]))
    return out


def check(paths):
    bad = 0
    for path in paths:
        if not os.path.isfile(path):
            print("  ⛔ НЕТ ФАЙЛА: %s" % path)
            bad += 1
            continue
        text = io.open(path, encoding="utf-8-sig", newline="").read()
        found = offences(path, text)
        if not found:
            print("  ЧИСТО   %s" % path)
            continue
        for n, why, line in found:
            print("  ⛔ %s:%d  %s" % (path, n, why))
            print("       %s" % line)
            bad += 1
    return bad


def selftest():
    """Двусторонний контроль: подложенное нарушение обязано УРОНИТЬ проверку."""
    print("положительный контроль (чистое дерево):")
    clean = check(WATCHED)
    print("  находок: %d" % clean)

    print("отрицательный контроль (подложенное нарушение):")
    sample = ("class X {\n"
              "    void M() {\n"
              "        var n = NuclideDefinitionManager.GetInstance();\n"
              "        lib = FsaLibrary.BuildFromPeaks(peaks, n.NuclideDefinitions);\n"
              "    }\n"
              "}\n")
    found = offences("<подлог>", sample)
    for n, why, line in found:
        print("  ⛔ <подлог>:%d  %s" % (n, why))
    print("  находок: %d (ожидалось не меньше 3)" % len(found))

    print("контроль ЛОЖНОЙ ТРЕВОГИ (те же слова в комментарии и в строке):")
    talk = ('        // NuclideDefinitionManager.GetInstance() здесь запрещён\n'
            '        Console.Error.WriteLine("не зовите FsaLibrary.BuildFromPeaks");\n')
    noise = offences("<разговор>", talk)
    for n, why, line in noise:
        print("  ⛔ ложная тревога <разговор>:%d  %s" % (n, why))
    print("  находок: %d (ожидался 0)" % len(noise))

    ok = clean == 0 and len(found) >= 3 and len(noise) == 0
    print("СОШЛОСЬ" if ok else "НЕ СОШЛОСЬ")
    return 0 if ok else 1


def main():
    if "--selftest" in sys.argv[1:]:
        return selftest()
    print("⛔ поставочный NuclideDefinition.xml на корпусе не используется "
          "(правило Amber 01.09.2026)")
    bad = check(WATCHED)
    print("НАХОДОК: %d" % bad)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
