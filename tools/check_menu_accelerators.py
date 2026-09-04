# -*- coding: utf-8 -*-
u"""Столкновения ускорителей главного меню — ВНУТРИ одного узла, на оба языка.

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

Запуск (из корня дерева):

    python tools/check_menu_accelerators.py [--list] [--root BecquerelMonitor]
        [--designer F] [--resx F] [--ru-resx F] [--code F]
        [--shared F] [--shared-ru F] [--menu menuStrip1]

Умолчания путей — `MainForm.Designer.cs`, `MainForm.resx`, `MainForm.ru.resx`,
`MainForm.cs`, `Properties/Resources.resx`, `Properties/Resources.ru.resx`
под `--root`.
"""
import argparse
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
#: `X.Text = Resources.Key;` — подпись из общих ресурсов приложения.
CODE_RES = re.compile(r'(?:this\.)?(\w+)\.Text\s*=\s*(?:Properties\.)?Resources\.(\w+)\s*;')
#: `X.Text = "литерал";` — одна подпись на оба языка.
CODE_LIT = re.compile(r'(?:this\.)?(\w+)\.Text\s*=\s*"((?:[^"\\]|\\.)*)"\s*;')
#: Пункт, добавляемый в меню кодом, — разбору недоступен.
RUNTIME_ADD = re.compile(r'\.DropDownItems\.(?:Add|Insert|AddRange)\w*\(')
#: Строка кода без комментария (`//…`) — чтобы закомментированное не считалось.
COMMENT = re.compile(r'//.*$')


def read(path):
    with io.open(path, encoding='utf-8-sig', errors='replace', newline='') as fh:
        return fh.read()


def build_tree(designer, root):
    u"""{узел: [дети]} — только узлы, достижимые от `root`."""
    src = read(designer)
    nodes = {}
    for m in ADDRANGE.finditer(src):
        nodes[m.group(1)] = CHILD.findall(m.group(2))
    reach, todo = {}, [root]
    while todo:
        n = todo.pop()
        if n in reach or n not in nodes:
            continue
        reach[n] = nodes[n]
        todo.extend(nodes[n])
    return reach, len(nodes)


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


def code_labels(code):
    u"""[(контрол, вид, ключ-или-литерал, номер строки)] из рукописного кода."""
    found, runtime = [], []
    for num, line in enumerate(read(code).replace('\r\n', '\n').split('\n'), 1):
        line = COMMENT.sub('', line)
        for ctl, key in CODE_RES.findall(line):
            found.append((ctl, 'res', key, num))
        for ctl, lit in CODE_LIT.findall(line):
            found.append((ctl, 'lit', lit, num))
        if RUNTIME_ADD.search(line):
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


def main(argv):
    ap = argparse.ArgumentParser(description=u'столкновения ускорителей внутри узла меню')
    ap.add_argument('--root', default=APP, help=u'корень приложения')
    ap.add_argument('--designer', help=u'MainForm.Designer.cs')
    ap.add_argument('--resx', help=u'MainForm.resx (английский)')
    ap.add_argument('--ru-resx', dest='ru_resx', help=u'MainForm.ru.resx (русский)')
    ap.add_argument('--code', help=u'MainForm.cs — подписи, заданные кодом')
    ap.add_argument('--shared', help=u'Properties/Resources.resx')
    ap.add_argument('--shared-ru', dest='shared_ru', help=u'Properties/Resources.ru.resx')
    ap.add_argument('--menu', default='menuStrip1', help=u'корень дерева меню')
    ap.add_argument('--list', action='store_true', help=u'печатать все пункты с ускорителями')
    args = ap.parse_args(argv)

    r = args.root
    designer = args.designer or os.path.join(r, 'MainForm.Designer.cs')
    resx_en = args.resx or os.path.join(r, 'MainForm.resx')
    resx_ru = args.ru_resx or os.path.join(r, 'MainForm.ru.resx')
    code = args.code or os.path.join(r, 'MainForm.cs')
    shared_en = args.shared or os.path.join(r, 'Properties', 'Resources.resx')
    shared_ru = args.shared_ru or os.path.join(r, 'Properties', 'Resources.ru.resx')

    tree, all_nodes = build_tree(designer, args.menu)
    en, ru = texts(resx_en), texts(resx_ru) or {}
    sh_en, sh_ru = texts(shared_en) or {}, texts(shared_ru) or {}
    if en is None:
        print(u'НЕТ ФАЙЛА  %s' % resx_en)
        return 1
    # Общие ресурсы — ключи без суффикса `.Text`, читаем их отдельно.
    def shared_values(path):
        if not path or not os.path.exists(path):
            return {}
        return {n.get('name'): (n.findtext('value') or '')
                for n in ET.parse(path).getroot().findall('data')
                if n.get('name') and not n.get('name').startswith('>>')}
    sh_en, sh_ru = shared_values(shared_en), shared_values(shared_ru)

    in_tree = set(c for kids in tree.values() for c in kids)
    labels = {'en': dict(en), 'ru': dict(en)}
    labels['ru'].update(ru)

    problems = []
    by_code = []
    found, runtime = code_labels(code)
    for ctl, kind, val, num in found:
        if ctl not in in_tree:
            continue
        if kind == 'lit':
            labels['en'][ctl] = labels['ru'][ctl] = val
            by_code.append((ctl, u'"%s"' % val, num))
            continue
        if val not in sh_en:
            problems.append(u'НЕТ КЛЮЧА  %s:%d  %s.Text = Resources.%s — ключа нет в %s'
                            % (code.replace(os.sep, '/'), num, ctl, val,
                               os.path.basename(shared_en)))
            continue
        labels['en'][ctl] = sh_en[val]
        labels['ru'][ctl] = sh_ru.get(val, sh_en[val])
        by_code.append((ctl, u'Resources.%s' % val, num))
    for num, line in runtime:
        problems.append(u'НЕДОСТУПНО  %s:%d  пункт добавляется в меню кодом: %s'
                        % (code.replace(os.sep, '/'), num, line))

    stats = {}
    for lang in ('en', 'ru'):
        lab = labels[lang]
        items = accel = 0
        collisions = []
        for node, kids in tree.items():
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
                if args.list:
                    print(u'  [%s] %-34s %-34s &%s U+%04X' % (
                        lang, node, kid, m, ord(m)))
            for letter, members in sorted(groups.items()):
                if len(members) > 1:
                    collisions.append((node, letter, members))
        for node, letter, members in collisions:
            problems.append(u'СТОЛКНОВЕНИЕ %s [%s] : %r -> %s' % (
                node, lang, letter, u', '.join(visible(v) for _k, v in members)))
        stats[lang] = (len(tree), items, accel, len(collisions))

    for line in problems:
        print(line)
    print()
    for lang, name in (('en', u'АНГЛИЙСКИЙ'), ('ru', u'РУССКИЙ')):
        nodes, items, accel, coll = stats[lang]
        print(u'%-11s узлов %d, пунктов %d, с ускорителем %d, столкновений %d'
              % (name + ':', nodes, items, accel, coll))
    print(u'узлов AddRange в Designer всего %d, из них в дереве меню %d'
          % (all_nodes, len(tree)))
    print(u'подписей, заданных кодом (%s): %d' % (os.path.basename(code), len(by_code)))
    for ctl, src, num in by_code:
        print(u'      %s <- %s  (строка %d): en %r / ru %r'
              % (ctl, src, num, visible(labels['en'][ctl]), visible(labels['ru'][ctl])))
    print(u'пунктов, добавляемых в меню кодом (разбору недоступны): %d' % len(runtime))
    print(u'РАЗОШЛОСЬ' if problems else u'СОШЛОСЬ')
    return 1 if problems else 0


if __name__ == '__main__':
    try:
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    except AttributeError:
        pass
    sys.exit(main(sys.argv[1:]))
