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

    python tools/check_resx.py [--list] [путь]

Возвращает 1, если непереведённое есть.
"""
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

TEXTY = ('.Text', '.ToolTipText', '.HeaderText', '.TabText', '.Title', '.Caption')
NUMERIC = re.compile(r'[-0-9.,:%\s]+')


def wanted(name, node):
    if name.startswith('>>') or node.get('mimetype'):
        return False
    if any(name.endswith(s) for s in TEXTY):
        return True
    return '.' not in name and not node.get('type')


def load(path):
    root = ET.parse(path).getroot()
    out = {}
    for node in root.findall('data'):
        name = node.get('name')
        if wanted(name, node):
            out[name] = node.findtext('value') or ''
    return out


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
                         and not NUMERIC.fullmatch(eng[k].strip()))
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
    print('РАЗОШЛОСЬ' if total or extra_total else 'СОШЛОСЬ')
    return 1 if total or extra_total else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
