# -*- coding: utf-8 -*-
u"""Сторож разряда «ожидание счётного захода названо в строке, но нигде не сведено».

Задача `T127`. 02.09.2026 на вопрос Amber «сколько задач требует пересборки
корпуса» ответ пришлось выводить сплошным разбором всех строк `TODO.md`:
признака «ждёт корпусного прогона» у строки не было, а заведённый под это
раздел «Отложенные прогоны» стоял пустым. Перечень, собранный тогда ПО СЛОВАМ
строк, протух ровно так, как и должен был: из 24 названных в `T127` номеров
к 06.09.2026 закрыто 14.

Признак выбран практикой 06.09.2026 и сторож его лишь закрепляет: ожидание
помечается В ГРАФЕ СОСТОЯНИЯ строки словами `ЖДЁТ СЧЁТНОГО ЗАХОДА`, а не
отдельным разделом. Раздел «Отложенные прогоны» — производная витрина, и
задача сторожа одна: не дать витрине разойтись со строками.

Что сверяется (и то, и другое — в обе стороны):

  (а) СОСТАВ. Каждая ОТКРЫТАЯ строка с признаком перечислена в разделе
      «Отложенные прогоны», и каждая строка раздела несёт признак. Закрытая
      строка признака нести не должна вовсе — закрыли, а признак остался,
      и витрина зовёт на несуществующую работу.

  (б) РАЗРЯД ОЖИДАНИЯ. Ждать можно ТРЁХ разных вещей, и стоят они разного:

        * пересборка файлов корпуса          (`rebuild_corpus.py`, четыре шага)
        * пересчёт склада матриц             (44 корпусные, ~10 часов ЦП)
        * один прогон FSA на готовых матрицах (минуты)

      Разряд называется ДВАЖДЫ — в графе состояния строки и в её строке
      раздела, — и оба названия обязаны совпасть. Дважды не для красоты:
      строку читают на месте (`grep` по номеру), а раздел — списком, и
      расхождение между ними и есть та беда, ради которой сторож написан.

⛔ Сторож НИЧЕГО НЕ ПИШЕТ. `TODO.md` правит распорядитель; ключ `--section`
печатает ГОТОВЫЙ текст раздела, собранный по содержимому строк, — его
переносят руками.

⚠ Разряд из тела строки НЕ УГАДЫВАЕТСЯ. Угадывание по словам («склад»,
«пересобрать») и есть тот способ, которым собран протухший перечень внутри
`T127`; сторож, повторяющий его, доказывал бы сам себя. Разряд обязан быть
НАПИСАН — там, где его прочтёт человек.

Разбор CR-стойкий: файл читается нетронутым (`newline=""`) и режется ТОЛЬКО
по `\\n`, номера строк совпадают с `grep -n`. Из черт режутся первые три,
дальше текст склеивается обратно — внутри описаний свои `|` (память
`todo-rows-have-inner-pipes`).

Запуск:
    python tools/check_pending_runs.py                  # проверить TODO.md
    python tools/check_pending_runs.py --todo X.md      # проверить копию
    python tools/check_pending_runs.py --section        # готовый текст раздела
    python tools/check_pending_runs.py --selftest       # положительный контроль

Код возврата: 0 — сходится; 1 — расхождение (в «ОСТАНОВ» — номера строк);
2 — файл не разобрался либо раздела в нём нет.
"""

import argparse
import io
import os
import re
import sys

for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_TODO = os.path.join(REPO, 'TODO.md')

MARK = u'ЖДЁТ СЧЁТНОГО ЗАХОДА'
SECTION = u'Отложенные прогоны'
CLOSED = u'~~открыто~~'

# Разряды ожидания. Порядок — по цене прогона, от дорогого к дешёвому.
KINDS = [
    u'пересборка файлов корпуса',
    u'пересчёт склада матриц',
    u'один прогон FSA на готовых матрицах',
]

SEPARATOR_RE = re.compile(r'^\|[\s:|-]*\|\s*$')
TABLE_HEAD_RE = re.compile(r'^\|\s*#\s*\|')
ID_RE = re.compile(r'^[A-Z]{1,5}\d+$')
HEADING_RE = re.compile(r'^##+\s+(.*?)\s*$')


def read_lines(path):
    u"""Строки, резанные ТОЛЬКО по `\\n`; одиночный CR остаётся внутри строки."""
    with io.open(path, encoding='utf-8-sig', newline='') as f:
        return f.read().split('\n')


def clean_id(text):
    return text.replace(u'~~', u'').replace(u'**', u'').strip()


def parse(path):
    u"""(строки реестра, строки раздела, номер строки заголовка раздела).

    Строка реестра — словарь: `n` (номер строки файла), `id`, `state`
    (графа состояния), `body` (всё остальное), `section` (в каком разделе).
    """
    rows = []
    section_rows = []
    section_line = 0
    current = u''
    for num, raw in enumerate(read_lines(path), 1):
        line = raw.rstrip('\r')
        head = HEADING_RE.match(line)
        if head:
            current = head.group(1)
            if current == SECTION:
                section_line = num
            continue
        if not line.startswith('|'):
            continue
        if SEPARATOR_RE.match(line) or TABLE_HEAD_RE.match(line):
            continue
        parts = line.split('|')
        if len(parts) < 4:
            continue
        ident = clean_id(parts[1])
        if not ID_RE.match(ident):
            continue
        row = dict(n=num, id=ident, state=parts[2].strip(),
                   body=u'|'.join(parts[3:]).rstrip(), section=current,
                   line=line)
        if current == SECTION:
            section_rows.append(row)
        else:
            rows.append(row)
    return rows, section_rows, section_line


def kinds_in(text):
    return [k for k in KINDS if k in text]


def is_closed(state):
    return CLOSED in state


def marked_rows(rows):
    u"""Открытые строки реестра с признаком, в порядке появления в файле."""
    return [r for r in rows if MARK in r['state'] and not is_closed(r['state'])]


def check(path):
    u"""Печатает разбор, возвращает код возврата."""
    try:
        rows, section_rows, section_line = parse(path)
    except (IOError, OSError, UnicodeDecodeError) as exc:
        print(u'⛔ файл не разобрался: %s — %s' % (path, exc))
        return 2
    if not section_line:
        print(u'⛔ в файле нет раздела «%s» — сверять не с чем: %s'
              % (SECTION, path))
        return 2

    marked = marked_rows(rows)
    closed_marked = [r for r in rows if MARK in r['state'] and is_closed(r['state'])]
    by_id = dict((r['id'], r) for r in rows)

    print(u'сверяю: %s' % path)
    print(u'  строк реестра вне раздела: %d; с признаком «%s»: %d'
          % (len(rows), MARK, len(marked)))
    print(u'  строк в разделе «%s» (файл:%d): %d'
          % (SECTION, section_line, len(section_rows)))
    print(u'  разряды ожидания: %s' % u' | '.join(KINDS))
    print(u'')

    bad = []

    # (а) состав, обе стороны
    in_section = set(r['id'] for r in section_rows)
    for r in marked:
        if r['id'] not in in_section:
            bad.append(u'`%s` (TODO.md:%d) несёт признак, но в разделе «%s» ЕЁ НЕТ'
                       % (r['id'], r['n'], SECTION))
    for r in section_rows:
        src = by_id.get(r['id'])
        if src is None:
            bad.append(u'`%s` (раздел, TODO.md:%d) — такой строки в реестре НЕТ ВОВСЕ'
                       % (r['id'], r['n']))
        elif is_closed(src['state']):
            bad.append(u'`%s` (раздел, TODO.md:%d) — строка ЗАКРЫТА (TODO.md:%d), '
                       u'витрина зовёт на несуществующую работу'
                       % (r['id'], r['n'], src['n']))
        elif MARK not in src['state']:
            bad.append(u'`%s` (раздел, TODO.md:%d) — у самой строки (TODO.md:%d) '
                       u'признака «%s» НЕТ' % (r['id'], r['n'], src['n'], MARK))

    for r in closed_marked:
        bad.append(u'`%s` (TODO.md:%d) ЗАКРЫТА, а признак «%s» на ней остался'
                   % (r['id'], r['n'], MARK))

    # (б) разряд ожидания назван, и назван одинаково
    for r in marked:
        found = kinds_in(r['state'])
        if len(found) != 1:
            bad.append(u'`%s` (TODO.md:%d) — разряд ожидания в графе состояния '
                       u'%s; назвать ровно один из: %s'
                       % (r['id'], r['n'],
                          u'НЕ НАЗВАН' if not found else u'назван дважды (%s)'
                          % u', '.join(found), u' / '.join(KINDS)))
    for r in section_rows:
        found = kinds_in(r['line'])
        if len(found) != 1:
            bad.append(u'`%s` (раздел, TODO.md:%d) — разряд ожидания %s'
                       % (r['id'], r['n'],
                          u'НЕ НАЗВАН' if not found else u'назван дважды (%s)'
                          % u', '.join(found)))
            continue
        src = by_id.get(r['id'])
        if src is None:
            continue
        own = kinds_in(src['state'])
        if len(own) == 1 and own[0] != found[0]:
            bad.append(u'`%s` — разряд в разделе («%s») не тот, что в строке '
                       u'(«%s»)' % (r['id'], found[0], own[0]))

    if bad:
        print(u'НАХОДОК: %d' % len(bad))
        for b in bad:
            print(u'  ⛔ %s' % b)
        print(u'')
        print(u'ОСТАНОВ: раздел «%s» разошёлся со строками. Готовый текст '
              u'раздела — `python tools/check_pending_runs.py --section`.'
              % SECTION)
        return 1

    print(u'СХОДИТСЯ: %d строк ждут счётного захода, все названы в разделе '
          u'и разряд у каждой один.' % len(marked))
    return 0


def section_text(path):
    u"""Готовый текст раздела, собранный ПО СОДЕРЖИМОМУ строк. Код возврата."""
    rows, _section_rows, section_line = parse(path)
    if not section_line:
        print(u'⛔ в файле нет раздела «%s»: %s' % (SECTION, path))
        return 2
    marked = marked_rows(rows)
    out = [u'## %s' % SECTION, u'',
           u'| # | состояние | задача | детали |',
           u'|---|---|---|---|']
    unnamed = []
    for r in marked:
        found = kinds_in(r['state'])
        if len(found) == 1:
            kind = found[0]
        else:
            kind = u'⛔ РАЗРЯД НЕ НАЗВАН В СТРОКЕ'
            unnamed.append(r)
        out.append(u'| **%s** | ⏳ %s | %s | строка `%s` выше, TODO.md:%d |'
                   % (r['id'], MARK.lower(), kind, r['id'], r['n']))
    for line in out:
        print(line)
    if unnamed:
        print(u'')
        print(u'⛔ разряд не назван в графе состояния у %d строк: %s'
              % (len(unnamed), u', '.join(u'`%s` (TODO.md:%d)' % (r['id'], r['n'])
                                          for r in unnamed)))
        print(u'   дописать в графу состояния ровно один из: %s'
              % u' / '.join(KINDS))
        return 1
    return 0


# ------------------------------------------------------------ положительный контроль

def _mk_good(lines, kind):
    u"""Копия файла, в которой признак, разряд и раздел заведомо СОГЛАСОВАНЫ."""
    out = list(lines)
    ids = []
    for i, raw in enumerate(out):
        line = raw.rstrip('\r')
        if not line.startswith('|'):
            continue
        parts = line.split('|')
        if len(parts) < 4:
            continue
        ident = clean_id(parts[1])
        if not ID_RE.match(ident):
            continue
        if MARK not in parts[2] or is_closed(parts[2]):
            continue
        # ⛔ РАЗРЯД СНАЧАЛА СНИМАЕТСЯ, А ПОТОМ СТАВИТСЯ (07.09.2026). Прежде он
        # ДОПИСЫВАЛСЯ, и пока строки реестра своего разряда не несли, копия
        # выходила согласованной. Разряды в строках появились — и «хороший
        # вход» стал называть их ДВА, то есть плечо, которое обязано давать 0,
        # давало 1. Сторож при этом исправен: он верно ловил двойной разряд в
        # том мусоре, который ему подсунул его же контроль.
        #
        # ⚠ Отказ читался задом наперёд — «сторож судит не то, что обещает», —
        # хотя судил он ровно то. Ловушка того же рода, что и зашитые литералы
        # в контроле `check_registry`: контроль стареет вместе с текстом, из
        # которого строит свой вход.
        state = parts[2]
        for other in KINDS:
            state = state.replace(other, u'')
        state = re.sub(u'[;,]?\\s*разряд:\\s*', u' ', state)
        state = re.sub(u'\\s{2,}', u' ', state).rstrip(u' ;,')
        parts[2] = state + u'; разряд: %s ' % kind
        out[i] = u'|'.join(parts)
        ids.append(ident)
    body = [u'| **%s** | ⏳ %s | %s | строка `%s` выше |'
            % (i, MARK.lower(), kind, i) for i in ids]
    # ⛔ СТАРЫЕ СТРОКИ РАЗДЕЛА СНИМАЮТСЯ, А НЕ ОСТАВЛЯЮТСЯ (07.09.2026).
    # Прежний текст здесь гласил «старая пустая шапка раздела осталась ниже —
    # она безвредна», и это было верно ровно пока раздел был ПУСТ. Строки в нём
    # появились, и «хороший вход» стал нести ЧЕТЫРЕ строки вместо двух: две
    # свои и две настоящие, с другим разрядом. Сторож честно называл
    # расхождение, а плечо, обязанное давать 0, давало 1.
    filled = []
    in_section = False
    for raw in out:
        head = HEADING_RE.match(raw.rstrip('\r'))
        if head:
            in_section = head.group(1) == SECTION
            filled.append(raw)
            if in_section:
                filled.extend([u'', u'| # | состояние | задача | детали |',
                               u'|---|---|---|---|'] + body)
            continue
        if in_section and raw.lstrip().startswith(u'|'):
            continue
        filled.append(raw)
    return filled, ids


def _write(path, lines):
    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(u'\n'.join(lines))


def _quiet(path):
    u"""Код возврата `check` без печати."""
    buf = io.StringIO()
    old = sys.stdout
    sys.stdout = buf
    try:
        rc = check(path)
    finally:
        sys.stdout = old
    return rc, buf.getvalue()


def selftest(path):
    u"""Четыре плеча: хороший вход и три испорченных. Сторож без доказанного
    ОТКАЗА неотличим от ненаписанного (`T69`)."""
    import shutil
    import tempfile
    kind = KINDS[1]
    lines = read_lines(path)
    tmp = tempfile.mkdtemp(prefix='pending_runs_')
    ok = True
    try:
        good, ids = _mk_good(lines, kind)
        if not ids:
            print(u'⛔ контроль невозможен: в %s нет ни одной строки с признаком'
                  % path)
            return 2
        gpath = os.path.join(tmp, 'good.md')
        _write(gpath, good)
        rc, out = _quiet(gpath)
        print(u'  плечо «хороший вход»: код %d (ждали 0)' % rc)
        ok &= (rc == 0)

        # ⚠ Строку РАЗДЕЛА от строки РЕЕСТРА отличаем по хвосту «выше |»:
        # обе начинаются с `| **<номер>** |`, и наивный отбор по началу
        # выкидывает ОБЕ — тогда признака не остаётся вовсе и плечо мерит
        # пустоту (поймано первым же прогоном контроля).
        def _is_section_row(line, ident):
            return line.startswith(u'| **%s** |' % ident) and line.endswith(u'выше |')

        # 1. строка с признаком выпала из раздела
        victim = ids[0]
        bad1 = [l for l in good if not _is_section_row(l, victim)]
        p1 = os.path.join(tmp, 'bad_missing.md')
        _write(p1, bad1)
        rc, out = _quiet(p1)
        named = (u'`%s`' % victim) in out
        print(u'  плечо «строки нет в разделе»: код %d (ждали 1), '
              u'названа поимённо: %s' % (rc, u'да' if named else u'НЕТ'))
        ok &= (rc == 1 and named)

        # 2. в разделе лишняя строка, которой нет в реестре
        ghost = u'| **Z99** | ⏳ %s | %s | выдумка контроля |' % (MARK.lower(), kind)
        bad2 = list(good)
        # ⚠ `|---|---|---|---|` встречается в файле десятками раз: вставлять
        # надо в СВОЙ раздел, а не в первый попавшийся — иначе Z99 окажется
        # обычной строкой реестра без признака, и плечо снова мерит пустоту.
        first_body = next(i for i, l in enumerate(bad2)
                          if _is_section_row(l, ids[0]))
        bad2.insert(first_body, ghost)
        p2 = os.path.join(tmp, 'bad_ghost.md')
        _write(p2, bad2)
        rc, out = _quiet(p2)
        named = u'`Z99`' in out
        print(u'  плечо «в разделе лишняя строка»: код %d (ждали 1), '
              u'названа поимённо: %s' % (rc, u'да' if named else u'НЕТ'))
        ok &= (rc == 1 and named)

        # 3. разряд ожидания не назван в графе состояния
        bad3 = []
        hit = False
        for l in good:
            if not hit and l.startswith(u'| **%s** |' % victim) \
                    and u'; разряд: %s' % kind in l and u'выше |' not in l:
                l = l.replace(u'; разряд: %s ' % kind, u' ')
                hit = True
            bad3.append(l)
        p3 = os.path.join(tmp, 'bad_kind.md')
        _write(p3, bad3)
        rc, out = _quiet(p3)
        named = hit and (u'`%s`' % victim) in out and u'РАЗРЯД' in out.upper()
        print(u'  плечо «разряд не назван»: код %d (ждали 1), '
              u'названа поимённо: %s' % (rc, u'да' if named else u'НЕТ'))
        ok &= (rc == 1 and named)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print(u'')
    if ok:
        print(u'КОНТРОЛЬ СОШЁЛСЯ: хороший вход 0, три испорченных — 1 '
              u'с поимённым названием строки.')
        return 0
    print(u'ОСТАНОВ: контроль ПРОВАЛЕН — сторож судит не то, что обещает.')
    return 1


def main(argv=None):
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument('--todo', default=DEFAULT_TODO, help=u'какой файл судить')
    ap.add_argument('--section', action='store_true',
                    help=u'напечатать готовый текст раздела «%s»' % SECTION)
    ap.add_argument('--selftest', action='store_true',
                    help=u'положительный контроль на временных копиях')
    args = ap.parse_args(argv)
    if not os.path.isfile(args.todo):
        print(u'⛔ файла нет: %s' % args.todo)
        return 2
    if args.selftest:
        return selftest(args.todo)
    if args.section:
        return section_text(args.todo)
    return check(args.todo)


if __name__ == '__main__':
    sys.exit(main())
