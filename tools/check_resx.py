# -*- coding: utf-8 -*-
"""Пары `Foo.resx` / `Foo.ru.resx`: что осталось без перевода.

Правило проекта — английский основной, русский второй, и у почти каждого типа
WinForms есть пара resx. Проверить её глазами нельзя: в `.resx` лежат вперемешку
подписи, координаты, размеры и картинки, а русский файл ПЕРЕКРЫВАЕТ только то,
что переведено, — отсутствие ключа там законно у всего, кроме текста.

Поэтому сравниваются не все ключи, а только ТЕКСТОВЫЕ, и из них — только
непустые и не состоящие из одних цифр:

* `имя.Text`, `.ToolTipText`, `.HeaderText`, `.TabText`, `.Title`, `.Caption`;
* строковые ресурсы без точки в имени и без явного типа (`Resources.resx`).

Первый заход этой проверки считал все ключи подряд и объявил непереведёнными
2600 строк, из которых почти все были `button1.Location`. Число, которое ничего
не значит, хуже отсутствия числа.

Второй такой же случай — ЗАГЛУШКИ КОНСТРУКТОРА: `table1.Text` со значением
`table1`, `toolStripButton1.Text` со значением `toolStripButton1`. Конструктор
форм заводит их сам, пользователю они не видны никогда (у `Table`, `ToolStrip`,
`MenuStrip`, `StatusStrip`, `ToolStripContainer` свойство `Text` не рисуется), и
перевод у них был бы переводом имени переменной. Такие пропускаются: значение
либо совпадает с именем контрола (`toolStripSplitButtonBgMode.Text` =
`toolStripSplitButton7` — след переименования), либо само имеет вид
конструкторского имени `<вид><номер>`. Решение Amber 12.08.2026, W18.

    python tools/check_resx.py [--list] [путь]

Возвращает 1, если непереведённое есть.

## Проход 12.08.2026 (W18): проверка сошлась

Было 142 непереведённых и 10 ключей без английской пары, стало 0 и 0. Разбор:
30 заглушек конструктора (правило выше), 112 переводов, 10 «сирот» — разобраны
поимённо, и половина из них оказалась не мусором, а ЖИВЫМ переводом под старым
именем контрола: `toolStripMenuItem1` держал «&Экспорт спектра в файл» для
переименованного `exportToFileStripMenuItem`, `SPEFileFilter` — фильтр открытия
для `GBSFileFilter`. То есть переименование контрола молча роняет русскую
подпись, а пара при этом выглядит полной с обеих сторон.

**Чего эта проверка НЕ ловит** (все три случая найдены чтением, не ею — W20):
перевод, у которого нет читателя (`--- Все изотопы ---` лежал в паре, а поле,
из которого его брали, не присваивалось никогда); `ResXNullRef` с пустым
значением — русский файл не переводит строку, а ГАСИТ её; и строку, которой нет
в английском файле, потому что её там не завели вовсе (заголовок `AboutForm`).
Ключи, одинаковые по-русски и по-английски, заводятся в паре с тем же значением
сознательно — это отметка «смотрели, по-русски так же» (решение Amber).
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

TEXTY = ('.Text', '.ToolTipText', '.HeaderText', '.TabText', '.Title', '.Caption')
NUMERIC = re.compile(r'[-0-9.,:%\s]+')
# Имя, какое конструктор форм даёт контролу сам: вид плюс номер.
DESIGNER = re.compile(r'(?:table|tableSets|textColumn|numberColumn|checkColumn|imageColumn'
                      r'|toolStrip\w*?|menuStrip|statusStrip|contextMenuStrip|panel|splitContainer'
                      r'|tabPage|tabControl|groupBox|dataGridView)\d*$')


def wanted(name, node):
    if name.startswith('>>') or node.get('mimetype'):
        return False
    if any(name.endswith(s) for s in TEXTY):
        return True
    return '.' not in name and not node.get('type')


def placeholder(name, value):
    """Заглушка конструктора: переводить её нечего, она не показывается."""
    if not name.endswith('.Text'):
        return False
    value = value.strip()
    return value == name[:-len('.Text')] or bool(DESIGNER.fullmatch(value))


def load(path):
    root = ET.parse(path).getroot()
    out = {}
    for node in root.findall('data'):
        name = node.get('name')
        if wanted(name, node):
            out[name] = node.findtext('value') or ''
    return out


def duplicates(path):
    """Ключи, лежащие в файле ДВАЖДЫ. Побеждает последний — молча."""
    seen, dup = {}, {}
    for node in ET.parse(path).getroot().findall('data'):
        name = node.get('name')
        value = node.findtext('value')
        if name in seen:
            dup.setdefault(name, set()).add(seen[name])
            dup[name].add(value)
        seen[name] = value
    return dup


def main(argv):
    show = '--list' in argv
    rest = [a for a in argv if not a.startswith('--')]
    root = rest[0] if rest else 'BecquerelMonitor'

    rows, total, extra_total = [], 0, 0
    for ru in sorted(glob.glob(os.path.join(root, '**', '*.ru.resx'), recursive=True)):
        en = ru[:-len('.ru.resx')] + '.resx'
        if not os.path.exists(en):
            rows.append((0, os.path.basename(ru), ['НЕТ АНГЛИЙСКОЙ ПАРЫ'], []))
            continue
        eng, rus = load(en), load(ru)
        missing = sorted(k for k in eng
                         if k not in rus and eng[k].strip()
                         and not NUMERIC.fullmatch(eng[k].strip())
                         and not placeholder(k, eng[k]))
        orphan = sorted(k for k in rus if k not in eng)
        if missing or orphan:
            rows.append((len(missing), os.path.basename(en), missing, orphan))
            total += len(missing)
            extra_total += len(orphan)

    rows.sort(reverse=True)
    for count, name, missing, orphan in rows:
        print('%-34s без перевода %3d, лишних в ru %2d' % (name, count, len(orphan)))
        if show:
            if missing:
                print('      нет в ru: %s' % ', '.join(missing))
            if orphan:
                print('      нет в en: %s' % ', '.join(orphan))

    print()
    print('файлов с расхождением: %d' % len(rows))
    print('непереведённых осмысленных строк: %d' % total)
    print('ключей, которых нет в английской паре: %d' % extra_total)

    # Повторы ключей внутри файла: пока значения совпадают, это только лишний
    # вес, но стоит одной копии разойтись — победит последняя, и молча.
    dup_files, dup_conflict = 0, 0
    for path in sorted(glob.glob(os.path.join(root, '**', '*.resx'), recursive=True)):
        dup = duplicates(path)
        if not dup:
            continue
        dup_files += 1
        conflict = {k: v for k, v in dup.items() if len(v) > 1}
        dup_conflict += len(conflict)
        print('повторы ключей: %-34s %3d шт., из них с разными значениями %d'
              % (os.path.basename(path), len(dup), len(conflict)))
        if show:
            for k in sorted(conflict):
                print('      %s: %s' % (k, sorted(conflict[k])))
    if dup_files:
        print('файлов с повторами ключей: %d (расхождение значений: %d)'
              % (dup_files, dup_conflict))

    print('РАЗОШЛОСЬ' if total or extra_total or dup_conflict else 'СОШЛОСЬ')
    return 1 if total or extra_total or dup_conflict else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
