# -*- coding: utf-8 -*-
"""
Сторож на модальные окна в БЕЗОКОННОМ пути (остаток строки `S100`).

⛔ Беда, ради которой всё. Проба или харнесс, запущенные без окон, наткнувшись
на `MessageBox.Show`, ВИСНУТ насмерть: окно поднято, нажать «ОК» некому,
прогон стоит до убийства процесса. Оплачено трижды — строки `D42`, `T87`,
`T98`. Хуже всего то, что окна стоят не в диалогах настроек, а прямо в ПУТИ
РАЗБОРА, который пробы и зовут: разбор файла, калибровка, фон, сглаживание.

Механизм отказа БЕЗ окон уже есть — класс `AppUi`
(`BecquerelMonitor/GlobalConfigManager.cs:37`): `AppUi.HasWindows` отвечает,
есть ли кому нажать «ОК», `AppUi.Report` сообщает о беде окном ИЛИ строкой в
поток ошибок. Этот скрипт — ЧИТАТЕЛЬ того признака: он требует, чтобы в
безоконном пути звали `AppUi.Report`, а не `MessageBox.Show` напрямую.

Что делает
----------
1. Обходит `BecquerelMonitor/**/*.cs` (без `obj/`, `bin/`, без `*.Designer.cs`).
2. Гасит комментарии и строковые литералы, СОХРАНЯЯ смещения, — так что номера
   строк точны, а закомментированный вызов не считается. Это не мелочь:
   из 233 попаданий голого `grep` по дереву 10 сидят в комментариях, и один из
   них — единственный `MessageBox.Show` в `FWHMPeakDetector/PeakFinder.cs`.
3. Для каждого живого `MessageBox.Show` определяет ОХВАТЫВАЮЩИЙ ТИП и
   ближайший метод — обходом по скобкам, а не по имени файла: в одном файле
   лежат и оконный `ColorPicker : Control`, и безоконный `ColorCellEditor`.
4. Делит вызовы на разряды и ОТКАЗЫВАЕТ кодом 1, если хоть один стоит в
   безоконном пути.

Как выведен разряд «безоконный путь» — ИЗМЕРЕНИЕМ, не на глаз
-------------------------------------------------------------
Засев: типы приложения, названные в `tools/effmaker/probes/*.cs` (73 пробы).
Из засева вычитаются типы, которые проба объявляет САМА, — иначе `static class
Program` каждой из 43 проб затаскивает в разряд `BecquerelMonitor/Program.cs`,
точку входа приложения, куда ни одна проба не заходит.

Обход: от засева по ссылкам на типы приложения, с БАРЬЕРОМ на оконных типах.
Оконный тип отмечается достигнутым, но НЕ раскрывается. Без барьера обход
вырождается: измерено — 459 файлов из 496, то есть почти всё дерево, потому что
`DocumentManager` ссылается на `MainForm`, а `MainForm` — на всё остальное.
Барьер и есть смысл разряда: до формы проба дойти может, но за форму путь
разбора не идёт.

⛔ У барьера одно исключение, и оно тоже ИЗМЕРЕНО, а не решено: оконный тип,
НАЗВАННЫЙ ПРОБОЙ НАПРЯМУЮ, барьером не считается — проба доказала, что держит
его без окон. Поймано якорем `SANITY_REACHABLE`: `DocEnergySpectrum` наследует
`DockContent`, то есть по наследованию — окно, а `NuclideSetMemoryProbe.cs:90`
делает `new DocEnergySpectrum()` безоконно, и в этом же классе стоит
`MessageBox.Show(this, ...)` внутри `EnsureFsaFwhm()` — счётного метода, не
обработчика щелчка. Без исключения сторож пропустил бы ровно ту беду, ради
которой заведён.

⛔ Второе исключение — КОНСТРУКТОРЫ, и оно закрывает измеренную дыру в самом
`ALLOW`. Список обеляет тип ЦЕЛИКОМ, а 14 из 32 его записей пробы называют
напрямую; 98 вызовов оказывались прощены именно там, где обход говорит
«достижимо». Дыра не теоретическая: пробы строят (`new T(...)`) 12 оконных
типов, и у одного из них — `MainForm` — в конструкторе стоит голый
`MessageBox.Show(ex.Message)` (`MainForm.cs:122`, catch вокруг копирования
`config` в `%AppData%`), а `FsaFlagsProbe.cs:50` и `NuclideSetMemoryProbe.cs:81`
делают `new MainForm()` без окон. Поэтому окно в конструкторе оконного типа,
который проба доказанно строит, попадает в безоконный разряд ПЕРВЫМ правилом,
поверх `ALLOW`. Это не переоценка: `new T(...)` исполняет конструктор
безусловно, тогда как прочие окна формы висят на обработчиках, а щелчков проба
не шлёт. Тем же способом измерено, что таких вызовов во всём дереве ровно один.

Оконность типа тоже ИЗМЕРЕНА — транзитивным замыканием по объявленным базам
(`class X : Y`), от корней `UI_ROOTS`. Не «всё, что называется Form»:
`DCControlPanel : ToolWindow : DockContent` формой не называется, а окно ему
положено; `AudioInputDeviceController` кончается на `Controller`, а окна ему не
положено.

⚠ Предел способа, называю честно. Замыкание идёт по ССЫЛКАМ НА ТИПЫ, а не по
вызовам методов, — то есть ПЕРЕОЦЕНИВАЕТ достижимое: если проба упомянула тип,
в разряд попадает весь его файл, даже та ветка, куда исполнение не заходит.
Переоценка выбрана намеренно: сторож, пропускающий висящее окно, бесполезен, а
лишняя строка чинится заменой на `AppUi.Report`, которая ничего не ломает.
Исправляет переоценку только `ALLOW` — поимённо и с доводом на каждую запись.

Запуск:  python tools/check_headless.py          (0 — чисто, 1 — есть окна)
         python tools/check_headless.py --list    (все вызовы, без приговора)
"""
import collections
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "BecquerelMonitor")
PROBES = os.path.join(ROOT, "tools", "effmaker", "probes")

# ---------------------------------------------------------------------------
# Корни оконности. Транзитивное замыкание по базам от них и даёт «оконный тип».
# ---------------------------------------------------------------------------
UI_ROOTS = {
    # WinForms: собственно окна и всё, что живёт на экране.
    "Form", "UserControl", "Control", "ContainerControl", "ScrollableControl",
    "CommonDialog", "ToolStripItem", "ToolStripControlHost", "ToolStrip",
    "ToolStripDropDown", "Panel", "TextBox", "ComboBox", "Button", "ListBox",
    "DataGridView", "TabControl", "PictureBox", "Label", "TreeView",
    "ProgressBar", "MenuStrip", "StatusStrip", "CheckBox", "RadioButton",
    "NumericUpDown", "GroupBox", "SplitContainer", "PropertyGrid",
    # WeifenLuo.WinFormsUI.Docking — основа всех «DC»-видов через `ToolWindow`.
    "DockContent",
    # XPTable: правка ячейки на месте. Не Control, но экранная снасть —
    # поднимается по щелчку в таблице и ничего не считает.
    "CellEditor", "ICellEditor",
}

# ---------------------------------------------------------------------------
# ALLOW — «этому типу окно ПОЛОЖЕНО». Держится ЯВНО и с доводом на запись.
# Ключ — имя типа (partial-класс живёт в нескольких файлах, имя одно).
#
# Проверки гигиены не дают списку стать отмычкой:
#   * запись на тип, который НЕ оконный по наследованию, — ОТКАЗ (иначе список
#     превращается в способ обелить любой менеджер);
#   * запись, под которую не нашлось ни одного живого вызова, — предупреждение
#     (значит, окно уже убрали и строка протухла).
# Два исключения не по наследованию собраны ниже отдельно, в NON_UI_ALLOW.
# ---------------------------------------------------------------------------
ALLOW = {
    # --- окна верхнего уровня -------------------------------------------
    "MainForm": "оболочка приложения; окна — её прямая работа",
    "GlobalConfigForm": "диалог общих настроек",
    "DeviceConfigForm": "диалог настройки прибора",
    "ROIConfigForm": "диалог ROI-конфигурации",
    "NuclideDefinitionForm": "диалог правки библиотеки нуклидов",
    "NuclideSetForm": "диалог правки нуклидного сета",
    "NucBase": "окно базы нуклидов (`NucBase : Form`)",
    "EfficiencyMakerForm": "окно расчёта кривой эффективности",
    "GeometryMaterialEditorForm": "окно правки геометрии и вещества",
    "ResponseMatrixForm": "окно построения матрицы отклика",
    # --- окна настройки приборов ----------------------------------------
    "AudioInputDeviceForm": "диалог звукового входа",
    "AtomSpectraVCPDeviceForm": "диалог AtomSpectra (VCP)",
    "ObsidianDeviceForm": "диалог Obsidian",
    "RadiaCodeDeviceForm": "диалог RadiaCode",
    # --- виды панели DC (все `ToolWindow : DockContent`) -----------------
    "DCControlPanel": "докируемая панель видов",
    "DCEnergyCalibrationView": "вид энергокалибровки: правка руками",
    "DCFwhmCalibrationView": "вид калибровки ПШПВ: правка руками",
    "DCPeakDetectionView": "вид настройки поиска пиков",
    # --- экранные снасти -------------------------------------------------
    "EnergySpectrumView": "большой график спектра (`UserControl`)",
    "GeometryEditorPanel": "панель правки геометрии (`UserControl`)",
    "IntegerTextBox": "поле ввода целого: сообщает о неверном вводе",
    "DoubleTextBox": "поле ввода дробного: сообщает о неверном вводе",
    "frmColorPicker": "палитра выбора цвета XPTable (`frmColorPicker : Form`)",
}

# Исключения, которые наследованием не доказываются, — поимённо и с доводом.
# Ключ — «Тип.Метод»: окно разрешено ТОЛЬКО в этом методе, а не во всём типе.
NON_UI_ALLOW = {
    "Program.Main": (
        "точка входа `WinExe`: сообщение «приложение уже запущено» до того, "
        "как поднята главная форма. Пробы сюда не заходят — у каждой свой "
        "`Program`, приложение ей библиотека."),
    "AppUi.Report": (
        "ЕДИНСТВЕННАЯ законная дверь: сам механизм. Окно поднимается только "
        "под `if (AppUi.hasWindows)`, иначе строка в поток ошибок."),
    "AppUi.AskYesNo": (
        "та же дверь, вторая половина (заведена 27.08.2026 вместе с починкой "
        "документной прослойки, `S100`). Без окон метод НЕ показывает ничего, "
        "а БРОСАЕТ: `if (!AppUi.hasWindows) throw` стоит ПЕРВЫМ оператором, и "
        "`MessageBox` за ним безоконному прогону недостижим. Обеляется метод, "
        "а не тип: `AppUi` целиком не обелён нарочно."),
}

# Якоря: эти типы ОБЯЗАНЫ оказаться в безоконном разряде. Положительный
# контроль самого измерения — если засев или обход молча выродились в пустоту,
# якорь отвалится и скажет об этом. Опыт без положительного контроля здесь уже
# мерил пустоту, повторять не будем.
SANITY_REACHABLE = [
    "DocumentManager", "EnergySpectrum", "PeakDetector", "SpectrumAriphmetics",
    "PolynomialEnergyCalibration", "DocEnergySpectrum", "ResultData",
]

RE_MB = re.compile(r"\bMessageBox\s*\.\s*Show\b")
RE_DECL = re.compile(r"\b(?:class|struct|interface|enum)\s+([A-Za-z_]\w*)")
RE_BASE = re.compile(
    r"\b(?:class|struct)\s+([A-Za-z_]\w*)\s*(?:<[^>{]*>)?\s*:\s*([^{]+)")
RE_TOK = re.compile(r"\b[A-Za-z_]\w*\b")
RE_CALL = re.compile(r"([A-Za-z_]\w*)\s*(?:<[^<>]*>)?\s*\(")
RE_NEW = re.compile(r"\bnew\s+([A-Za-z_]\w*)\s*(?:<[^<>]*>)?\s*\(")
# Строковый литерал пробы. ⚠ Ищется в СЫРОМ тексте: `blank()` литералы
# гасит, и по нему их не найти (`T106`).
RE_STR = re.compile(r'"((?:[^"\\\n]|\\.)*)"')

BLOCK_KEYWORDS = {
    "if", "for", "foreach", "while", "switch", "using", "lock", "catch",
    "fixed", "do", "else", "try", "finally", "unsafe", "checked", "unchecked",
    "return", "new", "delegate", "sizeof", "typeof", "nameof", "stackalloc",
    "when", "yield", "await", "throw",
}
ACCESSORS = {"get", "set", "add", "remove"}


def read_source(path):
    """ЕДИНСТВЕННАЯ дверь чтения файла. Отрицательный контроль подменяет её."""
    with open(path, "r", encoding="utf-8-sig", errors="replace",
              newline="") as f:
        return f.read()


def blank(src):
    """Гасит комментарии и литералы пробелами, СОХРАНЯЯ длину и переводы строк.

    Смещения и номера строк остаются точными, а `//MessageBox.Show(...)` и
    `<c>MessageBox.Show</c>` в docs-комментарии перестают быть вызовом.
    """
    out = list(src)
    n = len(src)
    i = 0
    state = None
    while i < n:
        c = src[i]
        if state is None:
            if c == "/" and i + 1 < n and src[i + 1] == "/":
                out[i] = out[i + 1] = " "
                state = "line"
                i += 2
                continue
            if c == "/" and i + 1 < n and src[i + 1] == "*":
                out[i] = out[i + 1] = " "
                state = "block"
                i += 2
                continue
            if c == "@" and i + 1 < n and src[i + 1] == '"':
                state = "vstr"
                i += 2
                continue
            if c == '"':
                state = "str"
                i += 1
                continue
            if c == "'":
                state = "char"
                i += 1
                continue
            i += 1
            continue
        if state == "line":
            if c == "\n":
                state = None
                i += 1
                continue
            out[i] = " "
            i += 1
            continue
        if state == "block":
            if c == "*" and i + 1 < n and src[i + 1] == "/":
                out[i] = out[i + 1] = " "
                state = None
                i += 2
                continue
            if c != "\n":
                out[i] = " "
            i += 1
            continue
        if state == "vstr":
            if c == '"':
                if i + 1 < n and src[i + 1] == '"':
                    out[i] = out[i + 1] = " "
                    i += 2
                    continue
                state = None
                i += 1
                continue
            if c != "\n":
                out[i] = " "
            i += 1
            continue
        # 'str' и 'char' — обычные литералы с экранированием
        if c == "\\":
            out[i] = " "
            if i + 1 < n and src[i + 1] != "\n":
                out[i + 1] = " "
            i += 2
            continue
        if (state == "str" and c == '"') or (state == "char" and c == "'"):
            state = None
            i += 1
            continue
        if c != "\n":
            out[i] = " "
        i += 1
    return "".join(out)


def classify_header(header):
    """По тексту перед `{` — что это за область: тип, метод, член, прочее."""
    h = " ".join(header.split())
    m = RE_DECL.search(h)
    if m:
        return ("type", m.group(1))
    if re.search(r"\bnamespace\s+[\w.]+\s*$", h):
        return ("ns", "")
    if h.endswith(")") or re.search(r"\)\s*where\b", h):
        m = RE_CALL.search(h)
        if m:
            if m.group(1) in BLOCK_KEYWORDS:
                return ("block", "")
            return ("method", m.group(1))
        return ("block", "")
    m = re.search(r"([A-Za-z_]\w*)\s*$", h)
    if m and m.group(1) not in BLOCK_KEYWORDS:
        return ("member", m.group(1))
    return ("block", "")


def scopes(code):
    """Все области файла как (начало, конец, вид, имя) — обходом по скобкам."""
    found = []
    stack = []
    last_break = 0
    for i, c in enumerate(code):
        if c == "{":
            kind, name = classify_header(code[last_break:i])
            stack.append((i, kind, name))
            last_break = i + 1
        elif c == "}":
            if stack:
                o, kind, name = stack.pop()
                found.append((o, i, kind, name))
            last_break = i + 1
        elif c == ";":
            last_break = i + 1
    n = len(code)
    while stack:
        o, kind, name = stack.pop()
        found.append((o, n, kind, name))
    return found


def enclosing(found, pos):
    """Охватывающие тип и ближайший метод для смещения `pos`."""
    inner = sorted((s for s in found if s[0] < pos < s[1]),
                   key=lambda s: s[0])
    typ = None
    for o, c, kind, name in inner:
        if kind == "type":
            typ = name
    meth = None
    for o, c, kind, name in inner:
        if kind == "method":
            meth = name
    if meth is None:
        members = [name for o, c, kind, name in inner if kind == "member"]
        if members:
            meth = members[-1]
            if meth in ACCESSORS and len(members) > 1:
                meth = members[-2] + "." + meth
    return typ, meth


def cs_files(root, skip_designer=True):
    out = []
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in ("obj", "bin")]
        for fn in filenames:
            if not fn.endswith(".cs"):
                continue
            if skip_designer and fn.endswith(".Designer.cs"):
                continue
            out.append(os.path.join(dirpath, fn))
    return sorted(out)


def rel(p):
    return os.path.relpath(p, ROOT).replace("\\", "/")


def measure():
    """Возвращает всё измеренное разом: вызовы, разряды, доводы."""
    files = cs_files(APP)
    code = {p: blank(read_source(p)) for p in files}

    type_files = collections.defaultdict(set)
    file_types = collections.defaultdict(set)
    for p in files:
        for m in RE_DECL.finditer(code[p]):
            type_files[m.group(1)].add(p)
            file_types[p].add(m.group(1))
    alltypes = set(type_files)

    # --- оконность: транзитивное замыкание по объявленным базам -----------
    bases = collections.defaultdict(set)
    for p in cs_files(APP, skip_designer=False):
        for m in RE_BASE.finditer(blank(read_source(p))):
            for b in m.group(2).split(","):
                b = b.strip().split("<")[0].split(".")[-1].strip()
                if b:
                    bases[m.group(1)].add(b)
    ui = set()
    changed = True
    while changed:
        changed = False
        for t, bs in bases.items():
            if t not in ui and (bs & UI_ROOTS or bs & ui):
                ui.add(t)
                changed = True

    # --- засев от проб ----------------------------------------------------
    probe_files = sorted(os.path.join(PROBES, f) for f in os.listdir(PROBES)
                         if f.endswith(".cs")) if os.path.isdir(PROBES) else []
    seed = set()
    seed_by = collections.defaultdict(set)
    built_by = collections.defaultdict(set)
    probe_literals = set()
    for pf in probe_files:
        raw = read_source(pf)
        pc = blank(raw)
        # Имена, которые проба называет строкой. Пробы зовут ЧАСТНЫЕ методы
        # формы отражением — либо прямо (`typeof(T).GetMethod("Имя", …)`),
        # либо через свой помощник (`Call(form, "DoSearch")`), — и тип у
        # второго вида известен только в работе. Поэтому берутся ВСЕ
        # литералы: имя метода, совпавшее с литералом пробы, считается
        # достижимым.
        #
        # ⚠ Правило нарочно ШИРЕ точного: сторожу дешевле запретить окно
        # там, где проба до него, может быть, и не доходит, чем пропустить
        # то, до чего доходит. Ложная тревога снимается через ALLOW поимённо.
        probe_literals.update(RE_STR.findall(raw))
        own = {m.group(1) for m in RE_DECL.finditer(pc)}
        for t in (set(RE_TOK.findall(pc)) & alltypes) - own:
            seed.add(t)
            seed_by[t].add(os.path.basename(pf))
        # `new T(` — не упоминание типа, а ДОКАЗАННЫЙ вызов его конструктора.
        for m in RE_NEW.finditer(pc):
            if m.group(1) in alltypes and m.group(1) not in own:
                built_by[m.group(1)].add(os.path.basename(pf))

    # --- ссылки файла на типы приложения ----------------------------------
    file_refs = {}
    for p in files:
        file_refs[p] = (set(RE_TOK.findall(code[p])) & alltypes) - file_types[p]

    # --- обход с барьером на оконных типах --------------------------------
    reached, barrier, why = set(), set(), {}
    for t in sorted(seed):
        why[t] = "проба " + sorted(seed_by[t])[0]
    queue = collections.deque(sorted(seed))
    while queue:
        t = queue.popleft()
        if t in reached or t in barrier:
            continue
        # Оконный тип — барьер. НО не тогда, когда проба назвала его сама:
        # это измеренное доказательство, что он живёт и без окон.
        if t in ui and t not in seed:
            barrier.add(t)
            continue
        reached.add(t)
        for p in sorted(type_files[t]):
            for u in sorted(file_refs[p]):
                if u not in reached and u not in barrier and u not in why:
                    why[u] = "%s (%s)" % (t, rel(p))
                    queue.append(u)

    # --- живые вызовы MessageBox.Show -------------------------------------
    calls = []
    for p in files:
        c = code[p]
        if not RE_MB.search(c):
            continue
        found = scopes(c)
        for m in RE_MB.finditer(c):
            typ, meth = enclosing(found, m.start())
            calls.append({
                "file": rel(p),
                "line": c.count("\n", 0, m.start()) + 1,
                "type": typ or "<вне типа>",
                "method": meth or "<вне метода>",
            })
    calls.sort(key=lambda d: (d["file"], d["line"]))
    # Конструкторы, которые проба ДОКАЗАННО исполняет: `new T(...)` у оконного
    # типа. См. CTOR_OVERRIDE в main() — это исключение из ALLOW, и оно
    # измерено, а не решено.
    built_ui = {t: v for t, v in built_by.items() if t in ui}
    return {
        "files": files, "types": alltypes, "type_files": type_files, "ui": ui,
        "seed": seed, "reached": reached, "barrier": barrier, "why": why,
        "calls": calls, "probes": probe_files, "built_ui": built_ui,
        "probe_literals": probe_literals,
    }


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    listing = "--list" in argv
    m = measure()
    ui, reached, why, calls = m["ui"], m["reached"], m["why"], m["calls"]

    print("Сторож безоконного пути (S100). Дверь: AppUi.Report — "
          "BecquerelMonitor/GlobalConfigManager.cs:37")
    print("  файлов приложения: %d, типов: %d, проб: %d"
          % (len(m["files"]), len(m["types"]), len(m["probes"])))
    print("  оконных типов по наследованию: %d; засев от проб: %d типов"
          % (len(ui), len(m["seed"])))
    print("  достижимо БЕЗ окон: %d типов; барьер (оконные): %d"
          % (len(reached), len(m["barrier"])))
    print("  оконных типов, которые пробы СТРОЯТ (`new T(...)`): %d — их "
          "конструкторы ALLOW не обеляет" % len(m["built_ui"]))
    print("  живых MessageBox.Show: %d" % len(calls))
    print()

    fatal = []
    warn = []

    # --- положительный контроль самого измерения --------------------------
    if not m["probes"]:
        fatal.append("проб не найдено в %s — мерить нечем, разряд пуст"
                     % rel(PROBES))
    for t in SANITY_REACHABLE:
        if t not in reached:
            fatal.append("якорь `%s` НЕ попал в безоконный разряд — измерение "
                         "выродилось, приговору верить нельзя" % t)
    # Якорь правила конструкторов. Само правило может законно не найти НИ
    # ОДНОГО окна (когда их уберут) — поэтому проверяется не находка, а
    # ИЗМЕРЕНИЕ: пробы обязаны строить хоть один оконный тип. Если разбор проб
    # молча выродится, `built_ui` опустеет, исключение из ALLOW тихо исчезнет
    # вместе с ним, и сторож начнёт пропускать ровно то, ради чего заведён.
    if not m["built_ui"]:
        fatal.append("ни одного `new T(...)` с оконным типом в пробах — разбор "
                     "проб выродился, исключение из ALLOW мертво")

    # --- гигиена ALLOW ----------------------------------------------------
    seen_types = {c["type"] for c in calls}
    seen_pairs = {c["type"] + "." + c["method"] for c in calls}
    for t in sorted(ALLOW):
        if t not in ui:
            fatal.append("ALLOW['%s'] — тип НЕ оконный по наследованию; "
                         "список не отмычка" % t)
        if t not in seen_types:
            warn.append("ALLOW['%s'] протухла: живых MessageBox.Show нет" % t)
    for k in sorted(NON_UI_ALLOW):
        if k not in seen_pairs:
            warn.append("NON_UI_ALLOW['%s'] протухла: такого вызова нет" % k)

    windowed, headless, outside = [], [], []
    for c in calls:
        # CTOR_OVERRIDE. Окно в КОНСТРУКТОРЕ оконного типа, который проба
        # доказанно строит (`new T(...)`), — безоконный путь, и ALLOW его НЕ
        # обеляет. Проверяется ПЕРВЫМ, иначе список становится отмычкой.
        #
        # Правило не догадка, а измерение: пробы строят 12 оконных типов, у
        # `MainForm` в конструкторе стоит голый `MessageBox.Show(ex.Message)`
        # (`MainForm.cs:122`, catch вокруг копирования config в %AppData%), а
        # `FsaFlagsProbe.cs:50` и `NuclideSetMemoryProbe.cs:81` делают
        # `new MainForm()` без окон. Копирование падает ровно там, где проба и
        # живёт, — в чужом рабочем каталоге, — и тогда прогон ВИСНЕТ. До этого
        # правила ALLOW['MainForm'] прощал этот вызов вместе с остальными 26.
        #
        # Почему именно конструктор, а не весь тип: `new T(...)` исполняет
        # конструктор БЕЗУСЛОВНО, это не переоценка достижимого. Прочие окна
        # формы висят на обработчиках, а щелчков проба не шлёт.
        if c["type"] in m["built_ui"] and c["method"] == c["type"]:
            c["why"] = "конструктор исполняет проба " + ", ".join(
                sorted(m["built_ui"][c["type"]]))
            headless.append(c)
        # REFLECT_OVERRIDE (`T106`). Окно в МЕТОДЕ оконного типа, который
        # проба назвала сама, а имя метода стоит в литералах пробы, —
        # безоконный путь. ALLOW его НЕ обеляет, и проверяется он сразу за
        # конструкторным правилом, иначе список опять станет отмычкой.
        #
        # Правило заведено ПО ИЗМЕРЕНИЮ, а не из осторожности: `T98` — окно
        # в `NucBase.DoSearch` — вешало прогон насмерть, а в отчёте сторожа
        # не появлялось НИ РАЗУ, потому что оконный тип считается барьером
        # и обелялся у него только конструктор. Между тем `ChainProbe`
        # исполняет `DoSearch` отражением (`Call(form, "DoSearch")`), то
        # есть окно стояло ровно на пути пробы.
        elif (c["type"] in m["seed"] and c["type"] in ui
              and c["method"] in m["probe_literals"]):
            c["why"] = "метод зовёт отражением проба (имя в её литералах)"
            headless.append(c)
        elif c["type"] + "." + c["method"] in NON_UI_ALLOW:
            c["why"] = NON_UI_ALLOW[c["type"] + "." + c["method"]]
            windowed.append(c)
        elif c["type"] in ALLOW:
            c["why"] = ALLOW[c["type"]]
            windowed.append(c)
        elif c["type"] in reached:
            c["why"] = why.get(c["type"], "?")
            headless.append(c)
        else:
            outside.append(c)

    if listing or headless:
        print("=== БЕЗОКОННЫЙ ПУТЬ: %d вызовов в %d файлах ==="
              % (len(headless), len({c["file"] for c in headless})))
        for c in headless:
            print("  %s:%d  %s.%s()" % (c["file"], c["line"], c["type"],
                                        c["method"]))
            print("      достижим от: %s" % c["why"])
        print()

    if listing:
        print("=== ОКОННЫЕ (окно положено): %d вызовов ===" % len(windowed))
        for t in sorted({c["type"] for c in windowed}):
            n = sum(1 for c in windowed if c["type"] == t)
            r = next(c["why"] for c in windowed if c["type"] == t)
            print("  %-28s %3d  %s" % (t, n, r))
        print()
        print("=== ВНЕ РАЗРЯДА (пробы не дотягиваются): %d вызовов ==="
              % len(outside))
        for c in outside:
            print("  %s:%d  %s.%s()" % (c["file"], c["line"], c["type"],
                                        c["method"]))
        print()

    for w in warn:
        print("ПРЕДУПРЕЖДЕНИЕ: " + w)
    for f in fatal:
        print("ОТКАЗ ИЗМЕРЕНИЯ: " + f)

    print()
    print("итого: безоконный путь %d, оконные %d, вне разряда %d"
          % (len(headless), len(windowed), len(outside)))

    if fatal:
        return 2
    if headless:
        print("ОТКАЗ: в безоконном пути %d модальных окон. Проба, дойдя до "
              "любого, ВИСНЕТ. Замена: AppUi.Report(text, caption, icon)."
              % len(headless))
        return 1
    print("ЧИСТО: в безоконном пути модальных окон нет.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
