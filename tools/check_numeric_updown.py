#!/usr/bin/env python3
# -*- coding: utf-8 -*-
u"""Сторож полей со стрелками: КАЖДОЕ поле приложения — общий
`InvariantNumericUpDown`, и НИ У ОДНОГО нет группировки разрядов.

Зачем. Штатный `System.Windows.Forms.NumericUpDown` печатает и разбирает своё
содержимое КУЛЬТУРОЙ ПОТОКА, внутри себя, и обойти это снаружи нельзя: подмена
мест `ToString`/`Parse` в форме до контрола не достаёт. Под `ru-RU` человек
видел «0,50», набранную ТОЧКУ поле съедало с писком, а «0.5» читало как другое
число — приказ Amber 05.09.2026 («разделитель дробной части ВСЕГДА ТОЧКА»)
на этих полях не выполнялся вовсе (`A261`, решение 06.09.2026 «лечить все 36»).

Лечение — наследник `BecquerelMonitor.InvariantNumericUpDown`, и цена отката
здесь необычно низка: ОДНО поле, заведённое дизайнером Visual Studio заново,
молча вернёт культуру потока в форму, где всё остальное уже вылечено. Ни
сборка, ни замер этого не заметят — поле выглядит и ведёт себя как соседи,
пока система не русская. Этот сторож — тот самый читатель признака.

Что судится: `BecquerelMonitor/**/*.cs` — приложение, то, что уходит людям.

Что НЕ судится, по правилу, а не по подгону:

  * `tools/**` — оснастка. Проба `NumericCultureProbeF68` заводит ГОЛЫЙ
    `NumericUpDown` нарочно: это её положительный контроль, без которого
    замер ничего не значит. Сторож, запретивший бы его, сломал бы замер.
  * сам `InvariantNumericUpDown.cs` — он `NumericUpDown` наследует, а не
    создаёт; наследование под правило не подпадает и подпадать не должно.
  * `bin`, `obj`, `packages`, `.git`, `.vs` — не исходники.

  python tools/check_numeric_updown.py [<корень или файл> ...]

Без доводов обходится `BecquerelMonitor` от корня репозитория.

⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ИДЁТ КАЖДЫЙ РАЗ, а не по ключу: перед обходом дерева
сторож прогоняет свой разбор по четырём подброшенным образцам (голое создание,
создание с полным именем, создание с инициализатором объекта, группировка) и
по трём заведомо чистым (общий тип, наследование, группировка ВЫКЛЮЧЕНА).
Не поймал подброшенное или зацепил чистое — код 3 и приговора по дереву нет:
сторож, который не мерит, хуже отсутствующего.

Коды возврата:
  0 — все поля приложения общего типа, группировки нет;
  1 — есть голое создание `NumericUpDown` или включённая группировка
      (перечень поимённо: файл, строка, текст);
  2 — обходить нечего;
  3 — САМОПРОВЕРКА НЕ ПРОШЛА (сторож не мерит того, ради чего заведён).
"""

import io
import os
import re
import sys

# Создание голого поля: `new [global::][System.Windows.Forms.]NumericUpDown`.
# ⚠ `new InvariantNumericUpDown` под шаблон НЕ подпадает: после `new` идёт
#   либо уточнение пространством имён, либо сразу слово `NumericUpDown`, а
#   «Invariant…» — ни то, ни другое.
RE_RAW_NEW = re.compile(
    r'\bnew\s+(?:global::)?(?:System\.Windows\.Forms\.)?NumericUpDown\b')

# Группировка разрядов: её нет вовсе (`A244`, решение Amber 05.09.2026).
RE_GROUPING = re.compile(r'\bThousandsSeparator\s*=\s*true\b')

SKIP_DIRS = ('.git', '.vs', 'bin', 'obj', 'packages', '__pycache__', 'node_modules')
OWN_FILE = 'InvariantNumericUpDown.cs'


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


RE_WHOLE_COMMENT = re.compile(r'^\s*(?://|/\*|\*)')


def scan(text):
    u"""Находки в одном тексте: список (номер строки, род, текст строки).

    Разбор построчный нарочно: и создание, и группировка живут в одной строке.

    ⚠ СТРОКА, НАЧИНАЮЩАЯСЯ КОММЕНТАРИЕМ, НЕ СУДИТСЯ, и это не поблажка.
    Правка `A244` сняла группировку у формы матрицы отклика и объяснила это
    комментарием, который называет снятую строку дословно — запись о том, чего
    больше нет. Сторож, судивший бы её, требовал бы стереть объяснение, то
    есть подделать запись. Хвостовой комментарий на строке КОДА судится
    по-прежнему; закомментированное целиком создание — цена правила, и она
    невелика: такой код не исполняется."""
    out = []
    # ⚠ newline='' у читателя: файлы дерева и LF, и CRLF, и мешаные;
    #   нормализовать их молча нельзя (правило «переводы строк — байтами»).
    for no, line in enumerate(text.splitlines(), 1):
        if RE_WHOLE_COMMENT.match(line):
            continue
        if RE_RAW_NEW.search(line):
            out.append((no, u'голое создание NumericUpDown', line.strip()[:110]))
        if RE_GROUPING.search(line):
            out.append((no, u'группировка разрядов включена', line.strip()[:110]))
    return out


def selftest():
    u"""Подброшенные образцы: сторож обязан поймать плохое и не тронуть чистое."""
    planted = [
        (u'голое создание',
         u'this.numericUpDown1 = new NumericUpDown();'),
        (u'создание с полным именем',
         u'this.box = new System.Windows.Forms.NumericUpDown();'),
        (u'создание с инициализатором объекта',
         u'var box = new NumericUpDown { DecimalPlaces = 2 };'),
        (u'группировка разрядов',
         u'this.historiesBox.ThousandsSeparator = true;'),
    ]
    clean = [
        (u'общий тип',
         u'this.numericUpDown1 = new InvariantNumericUpDown();'),
        (u'общий тип с инициализатором',
         u'var box = new InvariantNumericUpDown { DecimalPlaces = 2 };'),
        (u'наследование, а не создание',
         u'public class InvariantNumericUpDown : NumericUpDown'),
        (u'группировка выключена',
         u'this.historiesBox.ThousandsSeparator = false;'),
        (u'запись о снятой строке в комментарии',
         u'            // `historiesBox.ThousandsSeparator = true` — снята по `A244`'),
    ]
    # Хвостовой комментарий на строке КОДА чистым НЕ считается — иначе правило
    # «строка-комментарий не судится» превратилось бы в дыру во всю ширину.
    planted.append((u'создание с хвостовым комментарием',
                    u'this.box = new NumericUpDown();  // временно'))
    ok = True
    print(u'самопроверка: подброшенных образцов %d, заведомо чистых %d'
          % (len(planted), len(clean)))
    for name, sample in planted:
        if not scan(sample):
            print(u'  ⛔ НЕ ПОЙМАН подброшенный образец «%s»: %s' % (name, sample))
            ok = False
    for name, sample in clean:
        hits = scan(sample)
        if hits:
            print(u'  ⛔ ЗАЦЕПЛЕН чистый образец «%s»: %s' % (name, sample))
            ok = False
    print(u'  САМОПРОВЕРКА ПРОШЛА' if ok else u'  ⛔ САМОПРОВЕРКА НЕ ПРОШЛА')
    return ok


def walk(roots):
    for root in roots:
        if os.path.isfile(root):
            yield root
            continue
        if not os.path.isdir(root):
            sys.stderr.write(u'ОТКАЗ: нет такого пути: %s\n' % root)
            continue
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            for fn in filenames:
                if fn.lower().endswith('.cs'):
                    yield os.path.join(dirpath, fn)


def main(argv):
    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    if not selftest():
        return 3

    roots = argv[1:] or [os.path.join(repo, 'BecquerelMonitor')]
    files = sorted(set(walk(roots)))
    if not files:
        sys.stderr.write(u'ОТКАЗ: обходить нечего: %s\n' % roots)
        return 2

    bad = []
    n_own = 0
    n_ok = 0
    for path in files:
        rel = os.path.relpath(path, repo).replace('\\', '/')
        if os.path.basename(path) == OWN_FILE:
            n_own += 1
            continue
        try:
            text = io.open(path, 'r', encoding='utf-8-sig',
                           errors='replace', newline='').read()
        except (IOError, OSError) as exc:
            sys.stderr.write(u'ОТКАЗ: не читается %s: %s\n' % (rel, exc))
            return 2
        hits = scan(text)
        if hits:
            for no, kind, line in hits:
                bad.append((rel, no, kind, line))
        elif u'InvariantNumericUpDown' in text:
            n_ok += 1

    print(u'обойдено файлов: %d (из них с общим полем %d; не судится сам %s — %d)'
          % (len(files), n_ok, OWN_FILE, n_own))

    if bad:
        print(u'')
        print(u'ОТКАЗ: мест мимо общего приёма — %d (`A261`, `A244`)' % len(bad))
        for rel, no, kind, line in bad:
            print(u'  %s:%d | %s | %s' % (rel, no, kind, line))
        print(u'')
        print(u'Лечение: `new BecquerelMonitor.InvariantNumericUpDown(…)` вместо голого')
        print(u'`NumericUpDown`; группировку разрядов снять совсем (`A244`).')
        return 1

    print(u'мест мимо общего приёма: 0')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
