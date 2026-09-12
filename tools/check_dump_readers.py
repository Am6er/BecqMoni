#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож ЧИТАТЕЛЕЙ ДАМПА (`T254`, остаток `A284`): никакой читатель дампа
разбора не мерит модель ПОКАЗНОЙ кривой.

## Что судится и почему

Под именем «измерение» в разборе живут ДВЕ кривые (`A284`, найдено 07.09.2026
полосой П31): `NetSpectrum` подрезает отрицательные отсчёты нулём — это
ПОКАЗНАЯ кривая, её рисует экран; `FitSpectrum` не подрезает — по ней считан
фит. У `AS80_Onyx` они расходятся в 2673 каналах из 8192, и восстановление
χ²/ndf по показной кривой давало 5.605 против 11.072 приложения (−49 %), по
кривой фита — 9.863 (−11 %, остаток — известная полоса П31); на синтетике с
известным ответом показная давала −23 %, кривая фита +0.28 %. Кривые разведены
именами 10.09.2026 (полоса П2): дамп `CorpusFsaProbe --dump=` несёт столбцы
`ch,keV,net,fit,model,continuum_raw,<слои>` — `net` показная, `fit` та, по
которой считан фит.

⛔ Запретить сам столбец `net` НЕЛЬЗЯ: для `wave_shape.py` и `wave_owner.py`
(`S88`, волна отношения модель/измерение) он остаётся правильным ответом — они
мерят ФОРМУ волны, а не χ² модели, и тот же формат без `fit` пишет
`FsaStackShot --dump=`. Запрещено ИСПОЛЬЗОВАНИЕ показной кривой в мерке
модели. Признак такой мерки в тексте читателя: столбец `net` берётся ключом
(`row['net']`, `d["net"]`, `usecols=[..., 'net', ...]`) в том же файле, где
ключом берётся `model`. Файл с обоими ключами вне списка `ALLOWED` — нарушение.

⚠ Судится ТЕКСТ, а не поведение: читатель, который берёт `net` и `model`, но
делит одно на другое ради формы волны, здесь неотличим от читателя, который
считает по ним χ². Поэтому разрешённые названы ПО ПУТИ (не по имени файла: тот
же `wave_shape.py`, положенный в другой каталог, — нарушение) и с причиной; новый
законный читатель `net` добавляется в `ALLOWED` строкой с причиной — и это
осознанное действие, а не тишина. Ложных тревог в дереве на 12.09.2026 нет:
ключ `net` берёт ровно один файл (`wave_shape.py`), и он в списке.

⚠ Ключ ищется как СТРОКОВЫЙ ЛИТЕРАЛ `'net'`/`"net"` (и `'model'`/`"model"`).
Переменная `net = counts - continuum` (`spectrum.py`, `ecal_accept_check.py`)
литералом не является и не судится — она не читает дамп.

## Самопроверка (`--selftest`) — положительный контроль

Сторож без доказанного отказа — это `T69`. Самопроверка подкладывает во
временный каталог четыре файла и судит их тем же `judge()`, что судит дерево:
плохой читатель (`net` + `model`) — обязан быть пойман; хороший (`fit` +
`model`) — чист; читатель одного `net` без `model` — чист (не мерка модели);
`wave_shape.py` В ЧУЖОМ каталоге с обоими ключами — пойман (список по пути).
Приговор дереву печатается ПЕРВЫМ, самопроверка — второй: она не зависит от
дерева и обязана отвечать и на красном дереве.

  python tools/check_dump_readers.py [--selftest]

Коды возврата: 0 — читатели чисты (и самопроверка прошла); 1 — нарушение в
дереве (перечень «файл: строки с net / строки с model»); 2 — самопроверка не
прошла, сторож слеп; 3 — каталога `tools/` нет.
"""

from __future__ import print_function

import io
import os
import re
import shutil
import sys
import tempfile

ROOT = u'tools'

# Разрешённые читатели столбца `net` рядом с `model` — ПО ПУТИ от корня дерева,
# с косой чертой, и с причиной. Имя файла в другом каталоге не разрешено.
ALLOWED = {
    u'tools/CORPUS/scripts/wave_shape.py':
        u'`S88`: волна отношения модель/измерение — мерится ФОРМА, а не χ²; '
        u'тот же формат без `fit` пишет `FsaStackShot --dump=`',
    u'tools/CORPUS/scripts/wave_owner.py':
        u'`S88`: читает дамп через `wave_shape.load` (сам ключей не берёт; '
        u'назван, потому что назван строкой `T254`)',
}

# Сам сторож несёт оба литерала в тексте — исключается по пути, как и читатель.
SELF = u'tools/check_dump_readers.py'
READER = u'tools/check_all.py'

# Литерал-ключ столбца: 'net' / "net" — но не часть другого слова ('netto',
# 'network') и не имя параметра (`net=`): ровно строка из трёх букв в кавычках.
KEY_NET = re.compile(u'''(['"])net\\1''')
KEY_MODEL = re.compile(u'''(['"])model\\1''')


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


def repo():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def sources(root):
    u"""Все `*.py` под `root`, кроме `__pycache__`; путь — от родителя `root`."""
    out = []
    base = os.path.dirname(root)
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d != u'__pycache__']
        for fn in filenames:
            if not fn.endswith(u'.py'):
                continue
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, base).replace(os.sep, u'/')
            out.append((rel, full))
    return sorted(out)


def read_text(path):
    # ⚠ `newline=''` (грабля «питон рвёт текст на одиночном CR»): судится текст
    #   как есть, переводы строк не переписываются.
    with io.open(path, encoding=u'utf-8', errors=u'replace', newline=u'') as f:
        return f.read()


def judge(root, allowed=ALLOWED, skip=(SELF, READER)):
    u"""Список нарушений: (путь, [строки с net], [строки с model])."""
    bad = []
    for rel, full in sources(root):
        if rel in skip or rel in allowed:
            continue
        text = read_text(full)
        net_lines = []
        model_lines = []
        for i, line in enumerate(text.splitlines(), 1):
            code = line.split(u'#', 1)[0]
            if KEY_NET.search(code):
                net_lines.append(i)
            if KEY_MODEL.search(code):
                model_lines.append(i)
        if net_lines and model_lines:
            bad.append((rel, net_lines, model_lines))
    return bad


def selftest():
    u"""Четыре подложенных читателя во временном `tools/` — кто пойман."""
    tmp = tempfile.mkdtemp(prefix=u'p28_dump_readers_')
    root = os.path.join(tmp, u'tools')
    stand = {
        u'tools/bad_reader.py':
            u"import csv\nfor row in csv.DictReader(open('d.csv')):\n"
            u"    r = float(row['net']) - float(row['model'])\n",
        u'tools/good_reader.py':
            u"import csv\nfor row in csv.DictReader(open('d.csv')):\n"
            u"    r = float(row['fit']) - float(row['model'])\n",
        u'tools/net_only.py':
            u"import csv\nfor row in csv.DictReader(open('d.csv')):\n"
            u"    y = float(row[\"net\"])  # show only\n",
        u'tools/elsewhere/wave_shape.py':
            u"def load(p):\n    return row['net'], row['model']\n",
        u'tools/commented.py':
            u"# 'net' и 'model' только в комментарии\nx = 1\n",
    }
    try:
        for rel, text in stand.items():
            full = os.path.join(tmp, *rel.split(u'/'))
            d = os.path.dirname(full)
            if not os.path.isdir(d):
                os.makedirs(d)
            with io.open(full, u'w', encoding=u'utf-8', newline=u'') as f:
                f.write(text)
        found = dict((rel, (n, m)) for rel, n, m in judge(root))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    missed = []
    want_bad = [u'tools/bad_reader.py', u'tools/elsewhere/wave_shape.py']
    want_ok = [u'tools/good_reader.py', u'tools/net_only.py', u'tools/commented.py']
    for rel in want_bad:
        if rel not in found:
            missed.append(u'не пойман плохой читатель: %s' % rel)
    for rel in want_ok:
        if rel in found:
            missed.append(u'ложная тревога на чистом читателе: %s' % rel)
    if u'tools/bad_reader.py' in found and found[u'tools/bad_reader.py'] != ([3], [3]):
        missed.append(u'строки названы неверно: %r' % (found[u'tools/bad_reader.py'],))
    return missed


def main(argv):
    _utf8_console()
    root = os.path.join(repo(), ROOT)
    if not os.path.isdir(root):
        print(u'ОСТАНОВ: каталога оснастки нет — %s' % root)
        return 3

    files = sources(root)
    print(u'сторож читателей дампа (T254, A284): столбец `net` (показная кривая)'
          u' рядом с `model` — только у названных по пути; судятся %d файлов %s/**/*.py,'
          u' разрешённых %d' % (len(files), ROOT, len(ALLOWED)))
    for rel in sorted(ALLOWED):
        mark = u'есть' if os.path.isfile(os.path.join(repo(), *rel.split(u'/'))) else u'НЕТ НА ДИСКЕ'
        print(u'  разрешён: %s (%s) — %s' % (rel, mark, ALLOWED[rel]))

    bad = judge(root)
    selfonly = u'--selftest' in argv
    if bad and not selfonly:
        print(u'ОСТАНОВ: модель мерится ПОКАЗНОЙ кривой — читателей %d:' % len(bad))
        for rel, n, m in bad:
            print(u'   %s: `net` в строках %s, `model` в строках %s'
                  % (rel, u','.join(str(x) for x in n[:6]), u','.join(str(x) for x in m[:6])))
        print(u'  Столбец фита — `fit`; показная `net` в мерке модели даёт −11…−49 % χ² (A284).')
        print(u'  Законный читатель формы (не χ²) добавляется в ALLOWED этого сторожа ПО ПУТИ, с причиной.')
        return 1

    missed = selftest()
    if missed:
        print(u'ОСТАНОВ: САМОПРОВЕРКА НЕ ПРОШЛА — сторож слеп:')
        for m in missed:
            print(u'   ' + m)
        return 2
    print(u'  самопроверка: плохой читатель и чужой wave_shape.py пойманы;'
          u' читатель по `fit`, читатель одного `net` и комментарий чисты')

    if selfonly:
        if bad:
            print(u'  (нарушения в дереве ЕСТЬ — %d; --selftest судит сторожа, а не дерево)' % len(bad))
            for rel, n, m in bad:
                print(u'   %s: net %s / model %s' % (rel, n[:6], m[:6]))
        return 0

    print(u'ЧИТАТЕЛИ ДАМПА ЧИСТЫ: столбец `net` рядом с `model` берут только разрешённые.')
    return 0


if __name__ == u'__main__':
    sys.exit(main(sys.argv[1:]))
