# -*- coding: utf-8 -*-
u"""Столкновения ускорителей ВСЕХ меню дерева — внутри узла, на оба языка.

Ускоритель пункта меню — буква после одиночного `&` в подписи (`&Файл`,
`Save &As...`); `&&` — экранированный амперсанд, не ускоритель. Windows
разбирает ускорители В ПРЕДЕЛАХ ОДНОГО ОТКРЫТОГО СПИСКА, поэтому две одинаковые
буквы в РАЗНЫХ выпадающих списках законны, а в одном — столкновение: нажатие
буквы ходит по кругу вместо вызова пункта.

## Откуда (`A103` → `T139`, 04–05.09.2026)

Строка `A103` считала столкновения ПО ВСЕМУ ФАЙЛУ `MainForm.ru.resx` (И×5,
Н×4…) — и ошибалась в предмете: по узлам настоящих столкновений было **1** в
русском и **2** в английском, все три разведены в `87016183`. Разбор, который
это нашёл и подтвердил на старой сборке (exit 1), жил в scratchpad и в
репозиторий не попал — этот файл его оживляет по методу из
`handover/handover-2026-09-04-a103.md` §6.

## Как считается

1. **Дерево** — регулярным разбором `MainForm.Designer.cs`: каждый
   `this.X.(Items|DropDownItems).AddRange(new … { this.A, this.B, … })` даёт
   узел `X` с детьми `A, B, …`. Берутся только узлы, достижимые от корня
   `--menu` (умолчание `menuStrip1`): `statusStrip1` тоже строится через
   `AddRange`, но ускорителей у него нет.
2. **Подписи** — из `MainForm.resx` (ключ `<имя>.Text`) для английского; для
   русского — из `MainForm.ru.resx` с фолбэком на английский, ровно как
   делает сателлит в приложении. Читается XML через `ElementTree`, поэтому
   формат файла (BOM, переводы строк) разбору безразличен — так можно
   подсовывать файлы из `git show`, которые всегда LF.
3. ⚠ **Подписи, заданные КОДОМ.** `showLogToolStripMenuItem` не имеет
   `.Text` в resx формы вовсе: подпись ставит `MainForm.cs`
   (`this.showLogToolStripMenuItem.Text = Resources.MenuShowLog;`), и любому
   сторожу только по `MainForm.resx` этот пункт НЕВИДИМ. Поэтому `MainForm.cs`
   разбирается на присваивания `X.Text = Resources.<Ключ>;` (значение берётся
   из `Properties/Resources.resx` и `Resources.ru.resx`, отсутствие ключа —
   находка `НЕТ КЛЮЧА`) и `X.Text = "литерал";` (одна подпись на оба языка).
   Присваивание кодом ПЕРЕКРЫВАЕТ resx: оно исполняется после
   `InitializeComponent`. Что оно бывает условным — разбору не видно; он
   считает подпись действующей всегда.
4. ⚠ **Пункты, добавляемые в меню в runtime** (`DropDownItems.Add(…)` в
   `MainForm.cs`), разбору недоступны. Сегодня их 0 (проверено `A103`);
   появятся — сторож печатает `НЕДОСТУПНО` и возвращает 1, потому что молча
   терять покрытие хуже, чем покраснеть.
5. **Ускоритель** — первая буква после одиночного `&` (так делает
   `WindowsFormsUtils.GetMnemonic`); столкновения группируются по
   `(узел, буква.upper())` отдельно для каждого языка. Пункт без `.Text` и
   без подписи кодом (сепаратор) в счёт не идёт.

## Выход и код возврата

Строки `СТОЛКНОВЕНИЕ <узел> [<язык>] : '<буква>' -> <пункт>, <пункт>`,
затем сводка по языкам: узлов, пунктов, с ускорителем, столкновений; сколько
подписей задано кодом (`--list` называет их поимённо). Возврат 0 — чисто,
1 — есть столкновение, ключ без значения или недоступный разбору пункт.

## Положительный контроль (05.09.2026)

* Файлы из родителя `87016183` (`git show 8d58ec52:BecquerelMonitor/MainForm.resx`
  и `.ru.resx`) через `--resx`/`--ru-resx` — exit 1, названы все три
  столкновения `A103` (`A` у File, `L` у View, `Н` у Спектр).
* Подброшенный общий resx с `MenuShowLog` = `&Instruction`, где `&I…` уже
  занята соседом по узлу Help, — exit 1, столкновение названо ЧЕРЕЗ пункт,
  подпись которого задана кодом. Это и есть проверка, что сторож видит
  `showLogToolStripMenuItem`.

## ⛔ ОБХОД ВСЕГО ДЕРЕВА — умолчание (`T167`, 11.09.2026)

До этой правки сторож знал ОДНУ форму — `MainForm`, — и был зелен ровно
потому, что смотрел в одну точку. Ключи `--designer/--resx/--code/--menu`
позволяли натравить его на другую форму, но каждый КОРЕНЬ надо было называть
своим `--menu`, а корней у `DocEnergySpectrum` три и узлов пятнадцать: такой
сторож существует, только пока кто-то помнит все ключи наизусть.

Теперь корни ищутся САМИ, и это главное: слепое пятно закрывается не списком
(список устаревает от первой новой формы), а разбором. Порядок такой:

1. каждый `*.Designer.cs` под `--root` даёт узлы `X.(Items|DropDownItems)
   .AddRange(new … { this.A, … })`; узлы без детей-контролов (`ComboBox`,
   у которого в `Items` строки) отбрасываются сразу;
2. **корень** — узел, который сам никому не ребёнок;
3. род корня — по типу из `this.X = new <Тип>(`: имя типа содержит
   `MenuStrip` или `ToolStrip` — судится (`MenuStripEx`, `ContextMenuStrip`,
   `ToolStripEx` и любой будущий потомок попадают сюда сами), `StatusStrip`
   и прочие списки — пропускаются со счётом;
4. ⛔ **корень, род которого определить не удалось, — НАХОДКА и код 1**
   (`НЕИЗВЕСТЕН КОРЕНЬ`). Молча выкинуть непонятный корень значит завести
   ровно то слепое пятно, ради которого всё это писалось.

Замер дерева 11.09.2026: корней с детьми **9** в шести файлах, из них меню
**8**, не-меню **1** (`statusStrip1`), неизвестных **0**. Формы —
`MainForm` (`menuStrip1`), `DocEnergySpectrum` (`contextMenuStrip1`,
`toolStrip1`, `toolStrip2` — под ними и четырнадцать `toolStripSplitButton*`),
`DCPeakDetectionView`, `ToolWindow`, `Utils/CalibrationGraph`,
`Utils/FWHMCalibrationGraph` (у всех четырёх `contextMenuStrip1`).

⚠ **Расширение покрытия НИЧЕГО НЕ НАШЛО, и это надо сказать прямо:** узлов
стало 10 → **29**, пунктов 69 → **132**, ускорителей 41 → **53**, а
столкновений как было 0, так и осталось. Прибавка ускорителей — девять в
контекстном меню спектра и три в `ToolWindow`; все **45** пунктов панелей
`toolStrip1`/`toolStrip2` (23 + 22, вместе с выпадающими списками четырнадцати
`toolStripSplitButton*`) ускорителей не имеют ВОВСЕ. То есть польза правки не
в находке, а в том, что 63 новых пункта теперь СУДЯТСЯ: следующая подпись с
`&` в них молча не пройдёт.

⚠ У `ToolWindow` и `Utils/FWHMCalibrationGraph` парного `*.ru.resx` НЕТ —
русский там законно берётся из английского, ровно как делает сателлит.

## Положительный контроль обхода (11.09.2026, `T167`)

Всё — во ВРЕМЕННОЙ копии шести форм (24 файла), дерево не трогалось; каждая
ветвь снимается возвратом из памяти со сверкой байтов. Ветвей четыре, и в
первых трёх рядом стоит сторож ИЗ `HEAD`, чтобы слепое пятно было видно, а не
объявлено:

* **столкновение в контекстном меню спектра.** `Закрыть(&C)` → `Закрыть(&S)`
  в `DocEnergySpectrum.ru.resx`, рядом в том же узле `Сохранить (&S)`.
  Сторож из `HEAD` — «столкновений 0», код **0**; после правки — код **1** и
  `СТОЛКНОВЕНИЕ contextMenuStrip1 [ru] : 'S' -> Сохранить (S), Закрыть(S)`,
  причём английское плечо остаётся чистым;
* **корень неизвестного рода.** Подброшен `FakeForm.Designer.cs` с
  `this.weirdRoot = new BecquerelMonitor.SomethingNobodyKnows()` и `AddRange` —
  код **1**, `НЕИЗВЕСТЕН КОРЕНЬ  FakeForm: weirdRoot (…)`;
* **пункт, добавляемый кодом.** В `ToolWindow.cs` вписан
  `this.contextMenuStrip1.Items.Add(extraItem);` — код **1**, `НЕДОСТУПНО …`.
  ⚠ Отрицательное плечо тут же: живой `comboBoxNuclSet.Items.Add(...)` в
  `DCPeakDetectionView.cs` НЕ считается, потому что получатель — не узел меню;
  на нетронутом дереве «пунктов, добавляемых кодом: 0»;
* **разбор одной формы ключами жив.** Старый контроль `T139` — файлы из
  `git show 8d58ec52` через `--resx`/`--ru-resx` — по-прежнему даёт код 1 и те
  же три столкновения (`A` у File, `L` у View, `Н` у Спектр).

На чистой копии и на нетронутом дереве — код 0.

Запуск (из корня дерева):

    python tools/check_menu_accelerators.py [--list] [--root BecquerelMonitor]
        [--designer F] [--resx F] [--ru-resx F] [--code F]
        [--shared F] [--shared-ru F] [--menu menuStrip1]

Без ключей путей идёт ОБХОД ВСЕГО ДЕРЕВА под `--root`. Названный хотя бы один
из `--designer/--resx/--ru-resx/--code` переводит сторожа в разбор ОДНОЙ формы
с корнем `--menu` (умолчание `menuStrip1`) — этим и держатся положительные
контроли `T139` выше, подсовывающие файлы из `git show`. Умолчания путей в
этом разборе — `MainForm.*` и `Properties/Resources.resx` / `.ru.resx` под
`--root`; общие ресурсы одни на все формы и в обходе тоже.
"""
import argparse
import glob
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
APP = os.path.normpath(os.path.join(HERE, '..', 'BecquerelMonitor'))

ADDRANGE = re.compile(
    r'this\.(\w+)\.(?:DropDownItems|Items)\.AddRange\(new [^{]*\{(.*?)\}\s*\)\s*;', re.S)
CHILD = re.compile(r'this\.(\w+)')
#: Род контрола — из `this.X = new <Тип>(` в том же генерируемом файле.
NEWTYPE = re.compile(r'this\.(\w+)\s*=\s*new\s+([\w.]+)\s*\(')
#: `X.Text = Resources.Key;` — подпись из общих ресурсов приложения.
CODE_RES = re.compile(r'(?:this\.)?(\w+)\.Text\s*=\s*(?:Properties\.)?Resources\.(\w+)\s*;')
#: `X.Text = "литерал";` — одна подпись на оба языка.
CODE_LIT = re.compile(r'(?:this\.)?(\w+)\.Text\s*=\s*"((?:[^"\\]|\\.)*)"\s*;')
#: Пункт, добавляемый в меню кодом, — разбору недоступен. `DropDownItems` есть
#: только у пунктов меню, потому ловится безоговорочно; `Items` есть и у списков
#: (`comboBox.Items.Add` в дереве живой), потому судится по ПОЛУЧАТЕЛЮ — им
#: должен оказаться узел судимого меню.
RUNTIME_DROP = re.compile(r'\.DropDownItems\.(?:Add|Insert|AddRange)\w*\(')
RUNTIME_ITEMS = re.compile(r'(?:this\.)?(\w+)\.Items\.(?:Add|Insert|AddRange)\w*\(')
#: Строка кода без комментария (`//…`) — чтобы закомментированное не считалось.
COMMENT = re.compile(r'//.*$')

#: Род корня по имени типа: судим всё, что несёт `MenuStrip` либо `ToolStrip`
#: (`MenuStripEx`, `ContextMenuStrip`, `ToolStripEx`, будущие потомки — сами).
MENU_MARKS = ('MenuStrip', 'ToolStrip')
#: Роды, которые ускорителей не несут и потому пропускаются СО СЧЁТОМ, а не
#: молча. Список короткий нарочно: непонятный корень обязан краснеть.
NOT_MENU = ('StatusStrip', 'ComboBox', 'ListBox', 'CheckedListBox',
            'ListView', 'TreeView', 'DataGridView', 'TabControl')


def read(path):
    with io.open(path, encoding='utf-8-sig', errors='replace', newline='') as fh:
        return fh.read()


def all_nodes(designer):
    u"""({узел: [дети]}, {контрол: тип}) по всему генерируемому файлу.

    Узел без детей-КОНТРОЛОВ выброшен здесь же: `comboBox.Items.AddRange(new
    object[] { "имп/с", … })` — это строки, а не пункты меню.
    """
    src = read(designer)
    nodes = {}
    for m in ADDRANGE.finditer(src):
        kids = CHILD.findall(m.group(2))
        if kids:
            nodes[m.group(1)] = kids
    return nodes, dict(NEWTYPE.findall(src))


def kind_of(name, types):
    u"""'меню' | 'не меню' | None (род не определён — это находка)."""
    typename = types.get(name)
    if not typename:
        return None
    short = typename.split('.')[-1]
    if any(mark in short for mark in MENU_MARKS):
        return u'меню'
    if short in NOT_MENU:
        return u'не меню'
    return None


def menu_roots(nodes, types):
    u"""([корни-меню], [(корень, род)] пропущенных, [корни неизвестного рода])."""
    kids = set(c for v in nodes.values() for c in v)
    roots = sorted(n for n in nodes if n not in kids)
    menus, skipped, unknown = [], [], []
    for name in roots:
        kind = kind_of(name, types)
        if kind == u'меню':
            menus.append(name)
        elif kind == u'не меню':
            skipped.append((name, types.get(name, u'?')))
        else:
            unknown.append((name, types.get(name, u'тип не найден')))
    return menus, skipped, unknown


def reachable(nodes, roots):
    u"""{узел: [дети]} — только узлы, достижимые от любого из `roots`."""
    reach, todo = {}, list(roots)
    while todo:
        n = todo.pop()
        if n in reach or n not in nodes:
            continue
        reach[n] = nodes[n]
        todo.extend(nodes[n])
    return reach


def build_tree(designer, root):
    u"""{узел: [дети]} от ОДНОГО названного корня (разбор одной формы)."""
    nodes, _types = all_nodes(designer)
    return reachable(nodes, [root]), len(nodes)


def texts(resx):
    u"""{имя контрола: подпись} по ключам `<имя>.Text`; None, если файла нет."""
    if not resx or not os.path.exists(resx):
        return None
    out = {}
    for node in ET.parse(resx).getroot().findall('data'):
        name = node.get('name') or ''
        if name.endswith('.Text') and not name.startswith('>>'):
            out[name[:-len('.Text')]] = node.findtext('value') or ''
    return out


def code_labels(code, nodes=()):
    u"""[(контрол, вид, ключ-или-литерал, номер строки)] из рукописного кода.

    Вторым списком — строки, добавляющие пункт в меню НА ХОДУ: разбору такой
    пункт недоступен, и терять его молча нельзя. `Items.Add` засчитывается,
    только если получатель — узел судимого меню (`nodes`); иначе в счёт пошёл
    бы каждый `comboBox.Items.Add` дерева.
    """
    found, runtime = [], []
    if not code or not os.path.exists(code):
        return found, runtime
    for num, line in enumerate(read(code).replace('\r\n', '\n').split('\n'), 1):
        line = COMMENT.sub('', line)
        for ctl, key in CODE_RES.findall(line):
            found.append((ctl, 'res', key, num))
        for ctl, lit in CODE_LIT.findall(line):
            found.append((ctl, 'lit', lit, num))
        if RUNTIME_DROP.search(line):
            runtime.append((num, line.strip()))
            continue
        hit = RUNTIME_ITEMS.search(line)
        if hit and hit.group(1) in nodes:
            runtime.append((num, line.strip()))
    return found, runtime


def mnemonic(label):
    u"""Первая буква после одиночного `&`; None, если ускорителя нет."""
    i = 0
    while i < len(label):
        if label[i] == '&':
            if i + 1 < len(label) and label[i + 1] == '&':
                i += 2
                continue
            return label[i + 1] if i + 1 < len(label) else None
        i += 1
    return None


def visible(label):
    return label.replace('&&', '\x00').replace('&', '').replace('\x00', '&')


def shared_values(path):
    u"""Общие ресурсы приложения: ключ -> значение (без суффикса `.Text`)."""
    if not path or not os.path.exists(path):
        return {}
    return {n.get('name'): (n.findtext('value') or '')
            for n in ET.parse(path).getroot().findall('data')
            if n.get('name') and not n.get('name').startswith('>>')}


def judge_form(designer, resx_en, resx_ru, code, sh_en, sh_ru, roots, show):
    u"""Разбор ОДНОЙ формы от названных корней.

    Возвращает `(проблемы, счёт)`; счёт — словарь по языкам плюс общие числа.
    Печать блока — на вызывающем: сторож обходит дерево и печатает одну сводку.
    """
    nodes, _types = all_nodes(designer)
    tree = reachable(nodes, roots)
    en, ru = texts(resx_en), texts(resx_ru) or {}
    if en is None:
        return [u'НЕТ ФАЙЛА  %s' % resx_en], None

    in_tree = set(c for kids in tree.values() for c in kids)
    labels = {'en': dict(en), 'ru': dict(en)}
    labels['ru'].update(ru)

    problems, by_code = [], []
    found, runtime = code_labels(code, tree)
    for ctl, kind, val, num in found:
        if ctl not in in_tree:
            continue
        if kind == 'lit':
            labels['en'][ctl] = labels['ru'][ctl] = val
            by_code.append((ctl, u'"%s"' % val, num))
            continue
        if val not in sh_en:
            problems.append(u'НЕТ КЛЮЧА  %s:%d  %s.Text = Resources.%s — ключа нет в общих ресурсах'
                            % (code.replace(os.sep, '/'), num, ctl, val))
            continue
        labels['en'][ctl] = sh_en[val]
        labels['ru'][ctl] = sh_ru.get(val, sh_en[val])
        by_code.append((ctl, u'Resources.%s' % val, num))
    for num, line in runtime:
        problems.append(u'НЕДОСТУПНО  %s:%d  пункт добавляется в меню кодом: %s'
                        % (code.replace(os.sep, '/'), num, line))

    count = {'nodes': len(tree), 'all_nodes': len(nodes),
             'by_code': by_code, 'runtime': len(runtime), 'labels': labels}
    for lang in ('en', 'ru'):
        lab = labels[lang]
        items = accel = 0
        collisions = []
        for node, kids in sorted(tree.items()):
            groups = {}
            for kid in kids:
                if kid not in lab:
                    continue          # сепаратор или пункт без подписи
                items += 1
                m = mnemonic(lab[kid])
                if m is None:
                    continue
                accel += 1
                groups.setdefault(m.upper(), []).append((kid, lab[kid]))
                if show:
                    print(u'  [%s] %-34s %-34s &%s U+%04X' % (
                        lang, node, kid, m, ord(m)))
            for letter, members in sorted(groups.items()):
                if len(members) > 1:
                    collisions.append((node, letter, members))
        for node, letter, members in collisions:
            problems.append(u'СТОЛКНОВЕНИЕ %s [%s] : %r -> %s' % (
                node, lang, letter, u', '.join(visible(v) for _k, v in members)))
        count[lang] = (items, accel, len(collisions))
    return problems, count


def forms_of_tree(root):
    u"""[(имя, designer, корни-меню, пропущенные, неизвестные)] по всему дереву."""
    out = []
    for designer in sorted(glob.glob(os.path.join(root, '**', '*.Designer.cs'),
                                     recursive=True)):
        nodes, types = all_nodes(designer)
        if not nodes:
            continue
        menus, skipped, unknown = menu_roots(nodes, types)
        if not menus and not unknown:
            continue
        name = os.path.relpath(designer, root).replace(os.sep, '/')
        out.append((name[:-len('.Designer.cs')], designer, menus, skipped, unknown))
    return out


def main(argv):
    ap = argparse.ArgumentParser(description=u'столкновения ускорителей внутри узла меню')
    ap.add_argument('--root', default=APP, help=u'корень приложения')
    ap.add_argument('--designer', help=u'MainForm.Designer.cs')
    ap.add_argument('--resx', help=u'MainForm.resx (английский)')
    ap.add_argument('--ru-resx', dest='ru_resx', help=u'MainForm.ru.resx (русский)')
    ap.add_argument('--code', help=u'MainForm.cs — подписи, заданные кодом')
    ap.add_argument('--shared', help=u'Properties/Resources.resx')
    ap.add_argument('--shared-ru', dest='shared_ru', help=u'Properties/Resources.ru.resx')
    ap.add_argument('--menu', default='menuStrip1', help=u'корень дерева меню (разбор одной формы)')
    ap.add_argument('--list', action='store_true', help=u'печатать все пункты с ускорителями')
    args = ap.parse_args(argv)

    r = args.root
    sh_en = shared_values(args.shared or os.path.join(r, 'Properties', 'Resources.resx'))
    sh_ru = shared_values(args.shared_ru or os.path.join(r, 'Properties', 'Resources.ru.resx'))

    # ⛔ Разбор ОДНОЙ формы — только когда её назвали ключом (`T139`, контроли
    # через `git show`). Без ключей идёт обход всего дерева (`T167`).
    single = any((args.designer, args.resx, args.ru_resx, args.code))
    if single:
        designer = args.designer or os.path.join(r, 'MainForm.Designer.cs')
        forms = [(os.path.basename(designer)[:-len('.Designer.cs')], designer,
                  [args.menu], [], [])]
    else:
        forms = forms_of_tree(r)
        if not forms:
            print(u'ПУСТОЙ КОРЕНЬ  %s: ни одного меню — судить нечего' % r)
            print(u'РАЗОШЛОСЬ')
            return 1

    problems = []
    total = {'forms': 0, 'roots': 0, 'nodes': 0, 'by_code': 0, 'runtime': 0,
             'en': [0, 0, 0], 'ru': [0, 0, 0]}
    lines = []
    for name, designer, menus, skipped, unknown in forms:
        base = designer[:-len('.Designer.cs')]
        resx_en = args.resx or base + '.resx'
        resx_ru = args.ru_resx or base + '.ru.resx'
        code = args.code or base + '.cs'
        for ctl, typename in unknown:
            problems.append(u'НЕИЗВЕСТЕН КОРЕНЬ  %s: %s (%s) — род не определён, '
                            u'судить его или нет, решить нечем'
                            % (name, ctl, typename))
        if not menus:
            continue
        found, count = judge_form(designer, resx_en, resx_ru, code,
                                  sh_en, sh_ru, menus, args.list)
        problems += found
        if count is None:
            continue
        total['forms'] += 1
        total['roots'] += len(menus)
        total['nodes'] += count['nodes']
        total['by_code'] += len(count['by_code'])
        total['runtime'] += count['runtime']
        for lang in ('en', 'ru'):
            for i in range(3):
                total[lang][i] += count[lang][i]
        lines.append(u'  %-32s корней %d, узлов %d, пунктов en/ru %d/%d, '
                     u'с ускорителем %d/%d, столкновений %d/%d%s'
                     % (name, len(menus), count['nodes'],
                        count['en'][0], count['ru'][0],
                        count['en'][1], count['ru'][1],
                        count['en'][2], count['ru'][2],
                        u'' if not skipped else u'; пропущено не-меню: %s'
                        % u', '.join(u'%s (%s)' % (n, t.split('.')[-1])
                                     for n, t in skipped)))
        for ctl, src, num in count['by_code']:
            lines.append(u'      подпись кодом: %s <- %s (%s:%d): en %r / ru %r'
                         % (ctl, src, os.path.basename(code), num,
                            visible(count['labels']['en'][ctl]),
                            visible(count['labels']['ru'][ctl])))

    for line in problems:
        print(line)
    print()
    for line in lines:
        print(line)
    print(u'ФОРМ судится %d, корней меню %d, узлов %d'
          % (total['forms'], total['roots'], total['nodes']))
    for lang, title in (('en', u'АНГЛИЙСКИЙ'), ('ru', u'РУССКИЙ')):
        items, accel, coll = total[lang]
        print(u'%-11s пунктов %d, с ускорителем %d, столкновений %d'
              % (title + ':', items, accel, coll))
    print(u'подписей, заданных кодом: %d' % total['by_code'])
    print(u'пунктов, добавляемых в меню кодом (разбору недоступны): %d' % total['runtime'])
    print(u'РАЗОШЛОСЬ' if problems else u'СОШЛОСЬ')
    return 1 if problems else 0


if __name__ == '__main__':
    try:
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    except AttributeError:
        pass
    sys.exit(main(sys.argv[1:]))
