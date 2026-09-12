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

⛔ **ДЫРА, которой сторож не видел до 12.09.2026 (`AMBER19`).** Корпусный путь
зовёт `new PeakDetector()` (с явным списком), а `PeakDetector` — код
ПРИЛОЖЕНИЯ — поднимал `NuclideDefinitionManager` ИНИЦИАЛИЗАТОРОМ ПОЛЯ, то есть
при каждом `new`, независимо от того, дали ли список. Файл
`config\\NuclideDefinition.xml` в каталоге прогона был от этого ОБЯЗАТЕЛЕН (безоконный
подъём без файла бросает, `S100`), оснастка клала его туда нарочно, и сторож
здесь был зелен, потому что смотрел только в пробу. Теперь сторож судит и
`BecquerelMonitor/PeakDetector.cs` — ОСОБЫМ правилом, потому что приложению
менеджер там нужен по праву: `GetInstance` допустим ТОЛЬКО ВНУТРИ МЕТОДА
(ленивая ветвь «списка не дали»), а в ТЕЛЕ КЛАССА (инициализатор поля) или в
КОНСТРУКТОРЕ — отказ. Решается ГЛУБИНОЙ ФИГУРНЫХ СКОБОК: `namespace{ class{` —
глубина 2, это тело класса; метод — глубина 3 и глубже. Конструктор
распознаётся по заголовку члена `PeakDetector(` без типа возврата.

Гейт ВРЕМЕНИ ИСПОЛНЕНИЯ — в самой пробе (`SuppliedLibraryGuard`, код 12 на
файле в каталоге прогона и на счётчике `NuclideDefinitionManager.RaiseCount`);
этот сторож — текстовая половина той же двери.

⛔ **ПРОБЫ ОСНАСТКИ КОРПУСА (полоса П11, 12.09.2026, следствие `AMBER19`).**
Кроме корпусной пробы в оснастке `wd_app` живут ещё восемь проб — им нужны
корпусные конфигурации приборов и склад матриц `config\\device\\response`, а
значит, запускаются они ТОЛЬКО оттуда. До 12.09.2026 они поднимали
`NuclideDefinitionManager` (состав по подписям пиков из поставочного списка,
либо просто «на всякий случай»), и после гейта `AMBER19` все восемь падали в
оснастке броском `S100` ещё до первого числа — измерено дымовым прогоном
40 проб (журнал `handover/handover-2026-09-12-p11-probes-sample.md`). Теперь
состав у них — из базы по ключам `--sample=`/`--chain=` через общий вход
`FsaSampleSpec.FromManifest` (`T257`), и этот сторож судит их тем же правилом,
что корпусную пробу, ЗА ОДНИМ исключением: `FsaLibrary.BuildFromPeaks` им
разрешён — `FsaDoubleCountProbe` строит им библиотеку «по пикам» из
определений, собранных ИЗ БАЗЫ (`FsaSampleLibrary.AsDefinitions`), это и есть
её замер (`A168`). Запрещены подъём менеджера и его поля (`.NuclideDefinitions`,
`.NuclideSets`, `.ActiveSet`). Список — явный, с причиной у каждой строки
(`RIG_PROBES`); проба каталога проб (`build_pN`, там поставочный файл лежит по
решению П5) в него НЕ входит и поставочный список читает по праву.

    python tools/check_corpus_library.py            # проверить дерево
    python tools/check_corpus_library.py --selftest # доказать, что отказывает

⚠ Сторож без доказанного отказа — это `T69`, поэтому `--selftest` подкладывает
нарушение в копию файла и требует от проверки кода 1; на чистом дереве — 0.
Для `PeakDetector.cs` контроль трёхсторонний: инициализатор поля — отказ,
конструктор — отказ, ленивый метод — тишина. Для проб оснастки — подъём,
подложенный в НАСТОЯЩИЙ текст `FsaChannelSplitProbe.cs` (внутрь `Main`), обязан
дать ровно одну находку, а чистый текст — ноль; `BuildFromPeaks` в нём
находкой быть не должен.
"""

import io
import os
import re
import sys

# T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass

# Корпусный путь: чем считается корпус и чем он собирается.
WATCHED = [
    "tools/effmaker/probes/CorpusFsaProbe.cs",
    "tools/CORPUS/scripts/mkconfig.py",
]

# Код ПРИЛОЖЕНИЯ, который корпусный путь зовёт с явным списком, — судится
# особым правилом (`AMBER19`): менеджер только ЛЕНИВО, внутри метода.
LAZY_ONLY = [
    "BecquerelMonitor/PeakDetector.cs",
]

# Пробы ОСНАСТКИ КОРПУСА (П11, 12.09.2026): живут в `wd_app` — им нужен склад
# матриц рядом с exe и корпусные конфигурации приборов, — а там по `AMBER19`
# поставочного списка нет. Каждая — с причиной, почему она в оснастке, а не в
# каталоге проб. ⛔ `FsaStackShot.cs` сюда НЕ входит: он живёт в чужой
# незакоммиченной правке (12.09.2026) и остаётся хвостом `T257`.
RIG_PROBES = [
    ("tools/effmaker/probes/FsaChannelSplitProbe.cs",
     "тождества каналов отклика — матрица из склада (`ResponseMatrixStore.Load`)"),
    ("tools/effmaker/probes/FsaDoubleCountProbe.cs",
     "клетки A168 {матрица есть/нет} — матрица из склада по Guid кривой"),
    ("tools/effmaker/probes/ResponseRowDumpProbe.cs",
     "строки матрицы отклика — из склада; состава у пробы нет вовсе"),
    ("tools/effmaker/probes/FsaChannelShot.cs",
     "картинки каналов отклика — матрица из склада, без неё каналов нет"),
    ("tools/effmaker/probes/FsaComponentDumpProbe.cs",
     "ленты компонентов по каналам — матрица из склада"),
    ("tools/effmaker/probes/FsaSumPeakAccountProbe.cs",
     "сумм-пики строятся только с матрицей — из склада"),
    ("tools/effmaker/probes/FsaCascadeProbe.cs",
     "три прогона с матрицей из склада; `--rebuild` пишет туда же"),
    ("tools/effmaker/probes/CrystalXrayGateProbe.cs",
     "раздел 1 — вылет K-рентгена по строкам матрицы из склада"),
    ("tools/effmaker/probes/PeakOriginProbe.cs",
     "обход всего корпуса: состав каждого спектра из manifest.csv (`P5`, П19)"),
]

# Что запрещено пробам оснастки: подъём менеджера и его поля. `BuildFromPeaks`
# — НЕ запрещён (определения приходят из базы, `FsaSampleLibrary.AsDefinitions`).
RIG_FORBIDDEN = [
    (re.compile(r"NuclideDefinitionManager\s*\.\s*GetInstance"),
     "поднимает NuclideDefinitionManager — в оснастке корпуса файла нет (AMBER19), подъём падает"),
    (re.compile(r"\.\s*NuclideDefinitions\b"),
     "берёт NuclideDefinitions поставочного менеджера"),
    (re.compile(r"\.\s*NuclideSets\b"),
     "берёт NuclideSets поставочного менеджера"),
    (re.compile(r"\.\s*ActiveSet\b"),
     "берёт ActiveSet поставочного менеджера"),
]

GET_INSTANCE = re.compile(r"NuclideDefinitionManager\s*\.\s*GetInstance")
# Заголовок конструктора класса PeakDetector: модификаторы, имя, скобка — и НЕТ
# типа возврата перед именем (метод `X PeakDetector(` сюда не попадает).
CTOR = re.compile(r"^\s*(?:(?:public|internal|private|protected|static)\s+)*PeakDetector\s*\(")

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


def offences(path, text, rules=FORBIDDEN):
    """Строки-нарушители: код, а не комментарий и не строковый литерал."""
    out = []
    for n, line in enumerate(text.split("\n"), 1):
        if COMMENT.match(line):
            continue
        # строковые литералы гасим целиком — в них живут сообщения отказа
        bare = re.sub(r'"(?:[^"\\]|\\.)*"', '""', line)
        for pattern, why in rules:
            if pattern.search(bare):
                out.append((n, why, line.strip()[:100]))
    return out


def strip_noise(line):
    """Строка без комментария и без содержимого строковых литералов —
    для счёта скобок и поиска исполнения."""
    bare = re.sub(r'"(?:[^"\\]|\\.)*"', '""', line)
    bare = re.sub(r"'(?:[^'\\]|\\.)*'", "''", bare)
    bare = re.sub(r"//.*$", "", bare)
    return bare


def lazy_offences(path, text):
    """`PeakDetector.cs`: `GetInstance` в теле класса (инициализатор поля) или в
    конструкторе — отказ; внутри обычного метода — допустимо."""
    out = []
    depth = 0          # глубина фигурных скобок ДО строки
    ctor_depth = None  # глубина, на которой открыт конструктор; None — вне его
    in_block_comment = False
    for n, line in enumerate(text.split("\n"), 1):
        raw = line
        if in_block_comment:
            if "*/" in raw:
                raw = raw.split("*/", 1)[1]
                in_block_comment = False
            else:
                continue
        if "/*" in raw and "*/" not in raw.split("/*", 1)[1]:
            raw = raw.split("/*", 1)[0]
            in_block_comment = True
        bare = strip_noise(raw)
        if COMMENT.match(line):
            bare = ""
        if depth == 2 and ctor_depth is None and CTOR.search(bare):
            ctor_depth = depth
        if GET_INSTANCE.search(bare):
            if depth <= 2:
                out.append((n, "поднимает NuclideDefinitionManager ИНИЦИАЛИЗАТОРОМ ПОЛЯ — "
                               "при каждом new PeakDetector(), и с явным списком тоже (AMBER19)",
                            line.strip()[:100]))
            elif ctor_depth is not None:
                out.append((n, "поднимает NuclideDefinitionManager в КОНСТРУКТОРЕ — "
                               "при каждом new PeakDetector(), и с явным списком тоже (AMBER19)",
                            line.strip()[:100]))
        depth += bare.count("{") - bare.count("}")
        if ctor_depth is not None and depth <= ctor_depth and "}" in bare:
            ctor_depth = None
    return out


def check(paths, lazy=(), rig=()):
    bad = 0
    for path, reason in rig:
        if not os.path.isfile(path):
            print("  ⛔ НЕТ ФАЙЛА: %s" % path)
            bad += 1
            continue
        text = io.open(path, encoding="utf-8-sig", newline="").read()
        found = offences(path, text, RIG_FORBIDDEN)
        if not found:
            print("  ЧИСТО   %s (проба оснастки: %s)" % (path, reason))
            continue
        for n, why, line in found:
            print("  ⛔ %s:%d  %s" % (path, n, why))
            print("       %s" % line)
            bad += 1
    for path in lazy:
        if not os.path.isfile(path):
            print("  ⛔ НЕТ ФАЙЛА: %s" % path)
            bad += 1
            continue
        text = io.open(path, encoding="utf-8-sig", newline="").read()
        found = lazy_offences(path, text)
        if not found:
            print("  ЧИСТО   %s (менеджер только лениво, внутри метода)" % path)
            continue
        for n, why, line in found:
            print("  ⛔ %s:%d  %s" % (path, n, why))
            print("       %s" % line)
            bad += 1
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
    clean = check(WATCHED, LAZY_ONLY, RIG_PROBES)
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

    print("контроль PeakDetector.cs (AMBER19): инициализатор поля и конструктор — отказ, ленивый метод — тишина:")
    field_init = ("namespace BecquerelMonitor\n"
                  "{\n"
                  "    public class PeakDetector\n"
                  "    {\n"
                  "        public List<Peak> DetectPeak() { return null; }\n"
                  "        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();\n"
                  "    }\n"
                  "}\n")
    ctor = ("namespace BecquerelMonitor\n"
            "{\n"
            "    public class PeakDetector\n"
            "    {\n"
            "        public PeakDetector()\n"
            "        {\n"
            "            this.nuclideManager = NuclideDefinitionManager.GetInstance();\n"
            "        }\n"
            "        NuclideDefinitionManager nuclideManager;\n"
            "    }\n"
            "}\n")
    lazy = ("namespace BecquerelMonitor\n"
            "{\n"
            "    public class PeakDetector\n"
            "    {\n"
            "        // NuclideDefinitionManager.GetInstance() в комментарии — не в счёт\n"
            "        List<NuclideDefinition> SuppliedDefinitions()\n"
            "        {\n"
            "            if (this.nuclideManager == null)\n"
            "            {\n"
            "                this.nuclideManager = NuclideDefinitionManager.GetInstance();\n"
            "            }\n"
            "            return this.nuclideManager.NuclideDefinitions;\n"
            "        }\n"
            "        NuclideDefinitionManager nuclideManager;\n"
            "    }\n"
            "}\n")
    f1 = lazy_offences("<инициализатор поля>", field_init)
    f2 = lazy_offences("<конструктор>", ctor)
    f3 = lazy_offences("<ленивый метод>", lazy)
    for name, f in (("<инициализатор поля>", f1), ("<конструктор>", f2), ("<ленивый метод>", f3)):
        for n, why, line in f:
            print("  ⛔ %s:%d  %s" % (name, n, why))
    print("  находок: инициализатор %d (ожидался 1), конструктор %d (ожидался 1), ленивый метод %d (ожидался 0)"
          % (len(f1), len(f2), len(f3)))
    lazy_ok = len(f1) == 1 and len(f2) == 1 and len(f3) == 0

    print("контроль ПРОБ ОСНАСТКИ (AMBER19, П11): подложенный подъём в настоящем тексте пробы — одна находка, чистый — ноль:")
    rig_path = RIG_PROBES[0][0]
    rig_text = io.open(rig_path, encoding="utf-8-sig", newline="").read()
    rig_clean = offences(rig_path, rig_text, RIG_FORBIDDEN)
    # Подлог кладётся ВНУТРЬ Main — строкой после `FsaTuningReport.Snapshot();`,
    # то есть в исполняемый код, а не в комментарий и не в строковый литерал.
    anchor = "FsaTuningReport.Snapshot();"
    at = rig_text.find(anchor)
    planted = (rig_text[:at + len(anchor)]
               + "\n            NuclideDefinitionManager.GetInstance();"
               + rig_text[at + len(anchor):]) if at >= 0 else rig_text
    rig_planted = offences("<подлог в %s>" % os.path.basename(rig_path), planted, RIG_FORBIDDEN)
    for n, why, line in rig_planted:
        print("  ⛔ <подлог в %s>:%d  %s" % (os.path.basename(rig_path), n, why))
    # `BuildFromPeaks` пробам оснастки разрешён: определения — из базы (A168).
    peaks_text = "        byPeaks = FsaLibrary.BuildFromPeaks(peaks, definitions, null);\n"
    rig_peaks = offences("<BuildFromPeaks из базы>", peaks_text, RIG_FORBIDDEN)
    print("  находок: чистая проба %d (ожидался 0), с подлогом %d (ожидалась 1), BuildFromPeaks %d (ожидался 0)"
          % (len(rig_clean), len(rig_planted), len(rig_peaks)))
    rig_ok = at >= 0 and len(rig_clean) == 0 and len(rig_planted) == 1 and len(rig_peaks) == 0

    ok = clean == 0 and len(found) >= 3 and len(noise) == 0 and lazy_ok and rig_ok
    print("СОШЛОСЬ" if ok else "НЕ СОШЛОСЬ")
    return 0 if ok else 1


def main():
    if "--selftest" in sys.argv[1:]:
        return selftest()
    print("⛔ поставочный NuclideDefinition.xml на корпусе не используется "
          "(правило Amber 01.09.2026; гейт AMBER19 12.09.2026)")
    bad = check(WATCHED, LAZY_ONLY, RIG_PROBES)
    print("НАХОДОК: %d" % bad)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
